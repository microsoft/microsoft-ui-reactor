using System.Numerics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Microsoft.UI.Reactor.Animation;

/// <summary>
/// Bridge between Reactor's animation system and the WinUI compositor.
/// Routes property changes through compositor animations when an ambient curve is present,
/// or falls back to direct property assignment.
/// </summary>
internal static class AnimationHelper
{
    private static AnimateProperty PropertyFlag(string property) => property switch
    {
        "Opacity" => AnimateProperty.Opacity,
        "Scale" => AnimateProperty.Scale,
        "Rotation" => AnimateProperty.Rotation,
        "Translation" => AnimateProperty.Offset,
        "CenterPoint" => AnimateProperty.CenterPoint,
        _ => default,
    };

    // Both the handoff marker and property ownership follow native identity.
    private static bool ShouldWriteToVisual(UIElement element, string property)
        => element.GetValue(Reconciler.ReactorAttached.StateProperty) is Reconciler.ReactorState state
            && (state.VisualTaken || (state.CompositionModifierProperties & PropertyFlag(property)) != 0);

    private static void MarkCompositionWrite(UIElement element, string property, float value)
    {
        var state = MarkCompositionWrite(element, property);
        if (property == "Rotation") (state.TransformRest ??= new()).RotationDegrees = value;
    }

    private static void MarkCompositionWrite(UIElement element, string property, Vector3 value)
    {
        var state = MarkCompositionWrite(element, property);
        if (property == "Scale") (state.TransformRest ??= new()).Scale = value;
        else if (property == "Translation") (state.TransformRest ??= new()).Translation = value;
    }

    private static Reconciler.ReactorState MarkCompositionWrite(UIElement element, string property)
    {
        var state = Reconciler.GetOrCreateReactorState(element);
        var flag = PropertyFlag(property);
        state.CompositionModifierProperties |= flag;
        state.KeyframeChannelOwners?.Remove(flag);
        state.DeferredModifierResets &= ~flag;
        return state;
    }

    private static Visual GetAnimationVisual(UIElement element) => GetElementVisual(element);

    /// <summary>
    /// Returns the element's handoff visual. Every Reactor feature that needs the visual of a
    /// user element must take it here rather than from <see cref="ElementCompositionPreview"/>,
    /// because this is where the native <c>VisualTaken</c> marker is set (spec 066 §7.2, #1643).
    /// </summary>
    /// <remarks>
    /// WinUI refuses <c>GetElementVisual</c> while <see cref="UIElement.Scale"/>,
    /// <see cref="UIElement.Rotation"/> or <see cref="UIElement.CenterPoint"/> holds a non-identity
    /// value, and refuses facade writes after the handoff (#1613). This therefore resets any such
    /// facade value to identity, takes the visual, writes the same value to it, and marks it as a
    /// composition write so later modifier updates and unsets follow it to the visual.
    /// <see cref="UIElement.Translation"/> does not block the handoff and stays writable after it,
    /// but it moves too, so that the visual's Translation is the only one and features that
    /// animate it start from the right value. The facade is read on every call rather than
    /// cached so that a Translation written to the facade later is moved as well.
    /// </remarks>
    internal static Visual GetElementVisual(UIElement element)
    {
        var scale = element.Scale;
        var rotation = element.Rotation;
        var translation = element.Translation;
        var center = element.CenterPoint;
        bool moveScale = scale != Vector3.One, moveRotation = rotation != 0;
        bool moveTranslation = translation != Vector3.Zero, moveCenter = center != Vector3.Zero;
        if (!(moveScale || moveRotation || moveTranslation || moveCenter))
        {
            var taken = ElementCompositionPreview.GetElementVisual(element);
            ElementPool.MarkVisualTaken(element);
            return taken;
        }

        if (moveScale) element.Scale = Vector3.One;
        if (moveRotation) element.Rotation = 0;
        if (moveTranslation) element.Translation = Vector3.Zero;
        if (moveCenter) element.CenterPoint = Vector3.Zero;

        Visual visual;
        try
        {
            visual = ElementCompositionPreview.GetElementVisual(element);
        }
        catch
        {
            // Something else still blocks the handoff; leave the element as it was.
            if (moveScale) element.Scale = scale;
            if (moveRotation) element.Rotation = rotation;
            if (moveTranslation) element.Translation = translation;
            if (moveCenter) element.CenterPoint = center;
            throw;
        }
        ElementPool.MarkVisualTaken(element);

        // Facade Rotation is in degrees; RotationAngle is in radians.
        if (moveScale) visual.Scale = scale;
        if (moveRotation) visual.RotationAngle = rotation * MathF.PI / 180f;
        if (moveTranslation)
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            visual.Properties.InsertVector3("Translation", translation);
        }
        if (moveCenter) visual.CenterPoint = center;
        if (moveScale) MarkCompositionWrite(element, "Scale", scale);
        if (moveRotation) MarkCompositionWrite(element, "Rotation", rotation);
        if (moveTranslation) MarkCompositionWrite(element, "Translation", translation);
        if (moveCenter) MarkCompositionWrite(element, "CenterPoint");
        return visual;
    }

    /// <summary>
    /// Releases a modifier's facade or compositor write.
    /// Ordinary facade writes must not call GetElementVisual: handing out the visual
    /// permanently disables XAML implicit transitions and moves the element to the pool's visual lane.
    /// </summary>
    internal static void ResetCompositionModifier(UIElement element, string property, AnimationController? expectedController = null)
    {
        var state = element.GetValue(Reconciler.ReactorAttached.StateProperty) as Reconciler.ReactorState;
        var flag = PropertyFlag(property);
        if (state?.TransformRest is { } rest)
        {
            switch (property)
            {
                case "Scale": rest.Scale = Vector3.One; break;
                case "Rotation": rest.RotationDegrees = 0; break;
                case "Translation": rest.Translation = Vector3.Zero; break;
            }
        }
        if (state is null || (state.CompositionModifierProperties & flag) == 0)
        {
            if (property == "Opacity") return;
            try
            {
                switch (property)
                {
                    case "Scale": element.Scale = Vector3.One; break;
                    case "Rotation": element.Rotation = 0; break;
                    case "Translation": element.Translation = Vector3.Zero; break;
                    case "CenterPoint": element.CenterPoint = Vector3.Zero; break;
                }
                return;
            }
            catch (UnauthorizedAccessException)
            {
                // A different feature handed out the Visual. It already released the
                // facade and owns its visual value; do not overwrite that value.
                ElementPool.MarkVisualTaken(element);
                return;
            }
        }
        var visual = GetAnimationVisual(element);
        // A shared target can replace the deferred animation before its decorator plays.
        if (expectedController is not null && !Equals(expectedController, visual.TryGetAnimationController(CompositorPropertyName(property))))
        {
            ClearCompositionOwnership(state, flag);
            return;
        }
        if (state.KeyframeChannelOwners?.TryGetValue(flag, out var owner) == true && owner.Entry.Definition.Loop
            && visual.TryGetAnimationController(CompositorPropertyName(property)) is { } controller)
        {
            state.DeferredModifierResets |= flag;
            state.KeyframeChannelOwners[flag] = owner with { DeferredController = controller };
            return;
        }

        // A direct visual write replaces its running animation as well as its base
        // value. StopAnimation alone can leave the last animated value behind.
        // Disable implicit animations so .Animate() cannot animate this reset again.
        var implicitAnimations = visual.ImplicitAnimations;
        try
        {
            visual.ImplicitAnimations = null;
            switch (property)
            {
                case "Opacity": visual.Opacity = (float)element.Opacity; break;
                case "Scale": visual.Scale = Vector3.One; break;
                case "Rotation": visual.RotationAngle = 0; break;
                case "Translation":
                    element.Translation = Vector3.Zero;
                    visual.Properties.InsertVector3("Translation", Vector3.Zero);
                    break;
                case "CenterPoint": visual.CenterPoint = Vector3.Zero; break;
            }
        }
        finally { visual.ImplicitAnimations = implicitAnimations; }
        ClearCompositionOwnership(state, flag);
    }

    private static void ClearCompositionOwnership(Reconciler.ReactorState state, AnimateProperty flag)
    {
        state.CompositionModifierProperties &= ~flag;
        state.KeyframeChannelOwners?.Remove(flag);
        state.DeferredModifierResets &= ~flag;
    }

    /// <summary>
    /// Sets a scalar property on a UIElement, animating via the compositor if
    /// <see cref="AnimationScope.Current"/> is non-null.
    /// </summary>
    internal static void SetOrAnimate(UIElement element, string property, float value, Element? source = null,
        bool respectPropertyFlags = false)
    {
        // Resolve animation curve: prefer ambient WithAnimation scope,
        // fall back to element's .Animate() config curve.
        var curve = ResolveCurve(element, property, source, respectPropertyFlags);

        if (curve is null)
        {
            SetScalarDirect(element, property, value);
            return;
        }

        var visual = GetAnimationVisual(element);
        var compositor = visual.Compositor;
        var target = CompositorPropertyName(property);
        // Rotation is degrees, but its compositor channel is RotationAngle (radians).
        var compositorValue = property == "Rotation" ? float.DegreesToRadians(value) : value;

        switch (curve)
        {
            case SpringCurve spring:
            {
                var anim = compositor.CreateSpringScalarAnimation();
                anim.DampingRatio = spring.DampingRatio;
                anim.Period = TimeSpan.FromSeconds(spring.Period);
                anim.FinalValue = compositorValue;
                visual.StartAnimation(target, anim);
                break;
            }
            case EaseCurve ease:
            {
                var anim = compositor.CreateScalarKeyFrameAnimation();
                var easing = compositor.CreateCubicBezierEasingFunction(
                    new Vector2(ease.Easing.X1, ease.Easing.Y1),
                    new Vector2(ease.Easing.X2, ease.Easing.Y2));
                anim.InsertKeyFrame(1.0f, compositorValue, easing);
                anim.Duration = ease.Duration;
                visual.StartAnimation(target, anim);
                break;
            }
            case LinearCurve linear:
            {
                var anim = compositor.CreateScalarKeyFrameAnimation();
                anim.InsertKeyFrame(1.0f, compositorValue, compositor.CreateLinearEasingFunction());
                anim.Duration = linear.Duration;
                visual.StartAnimation(target, anim);
                break;
            }
            default:
                SetScalarDirect(element, property, value);
                return;
        }
        MarkCompositionWrite(element, property, value);
    }

    /// <summary>
    /// Creates a scalar implicit animation from a Curve instance for use in ImplicitAnimationCollections.
    /// </summary>
    internal static CompositionAnimation CreateScalarImplicitAnimation(Compositor compositor, string target, Curve curve)
    {
        switch (curve)
        {
            case SpringCurve spring:
            {
                var anim = compositor.CreateSpringScalarAnimation();
                anim.DampingRatio = spring.DampingRatio;
                anim.Period = TimeSpan.FromSeconds(spring.Period);
                anim.Target = target;
                return anim;
            }
            case EaseCurve ease:
            {
                var anim = compositor.CreateScalarKeyFrameAnimation();
                var easing = compositor.CreateCubicBezierEasingFunction(
                    new Vector2(ease.Easing.X1, ease.Easing.Y1),
                    new Vector2(ease.Easing.X2, ease.Easing.Y2));
                anim.InsertExpressionKeyFrame(1.0f, "this.FinalValue", easing);
                anim.Duration = ease.Duration;
                anim.Target = target;
                return anim;
            }
            default: // LinearCurve
            {
                var linear = curve as LinearCurve ?? new LinearCurve(TimeSpan.FromMilliseconds(300));
                var anim = compositor.CreateScalarKeyFrameAnimation();
                anim.InsertExpressionKeyFrame(1.0f, "this.FinalValue", compositor.CreateLinearEasingFunction());
                anim.Duration = linear.Duration;
                anim.Target = target;
                return anim;
            }
        }
    }

    /// <summary>
    /// Creates a Vector3 implicit animation from a Curve instance for use in ImplicitAnimationCollections.
    /// </summary>
    internal static CompositionAnimation CreateVector3ImplicitAnimation(Compositor compositor, string target, Curve curve)
    {
        switch (curve)
        {
            case SpringCurve spring:
            {
                var anim = compositor.CreateSpringVector3Animation();
                anim.DampingRatio = spring.DampingRatio;
                anim.Period = TimeSpan.FromSeconds(spring.Period);
                anim.Target = target;
                return anim;
            }
            case EaseCurve ease:
            {
                var anim = compositor.CreateVector3KeyFrameAnimation();
                var easing = compositor.CreateCubicBezierEasingFunction(
                    new Vector2(ease.Easing.X1, ease.Easing.Y1),
                    new Vector2(ease.Easing.X2, ease.Easing.Y2));
                anim.InsertExpressionKeyFrame(1.0f, "this.FinalValue", easing);
                anim.Duration = ease.Duration;
                anim.Target = target;
                return anim;
            }
            default: // LinearCurve
            {
                var linear = curve as LinearCurve ?? new LinearCurve(TimeSpan.FromMilliseconds(300));
                var anim = compositor.CreateVector3KeyFrameAnimation();
                anim.InsertExpressionKeyFrame(1.0f, "this.FinalValue", compositor.CreateLinearEasingFunction());
                anim.Duration = linear.Duration;
                anim.Target = target;
                return anim;
            }
        }
    }

    /// <summary>
    /// Creates a scalar animation targeting a specific value (for explicit StartAnimation on Visual).
    /// Returns CompositionAnimation (may be KeyFrameAnimation or SpringNaturalMotionAnimation).
    /// </summary>
    internal static CompositionAnimation CreateScalarTargetAnimation(Compositor compositor, float targetValue, Curve curve)
    {
        switch (curve)
        {
            case SpringCurve spring:
            {
                var anim = compositor.CreateSpringScalarAnimation();
                anim.DampingRatio = spring.DampingRatio;
                anim.Period = TimeSpan.FromSeconds(spring.Period);
                anim.FinalValue = targetValue;
                return anim;
            }
            case EaseCurve ease:
            {
                var anim = compositor.CreateScalarKeyFrameAnimation();
                var easing = compositor.CreateCubicBezierEasingFunction(
                    new Vector2(ease.Easing.X1, ease.Easing.Y1),
                    new Vector2(ease.Easing.X2, ease.Easing.Y2));
                anim.InsertKeyFrame(1.0f, targetValue, easing);
                anim.Duration = ease.Duration;
                return anim;
            }
            default:
            {
                var linear = curve as LinearCurve ?? new LinearCurve(TimeSpan.FromMilliseconds(300));
                var anim = compositor.CreateScalarKeyFrameAnimation();
                anim.InsertKeyFrame(1.0f, targetValue, compositor.CreateLinearEasingFunction());
                anim.Duration = linear.Duration;
                return anim;
            }
        }
    }

    /// <summary>
    /// Creates a Vector3 animation targeting a specific value (for explicit StartAnimation on Visual).
    /// Returns CompositionAnimation (may be KeyFrameAnimation or SpringNaturalMotionAnimation).
    /// </summary>
    internal static CompositionAnimation CreateVector3TargetAnimation(Compositor compositor, Vector3 targetValue, Curve curve)
    {
        switch (curve)
        {
            case SpringCurve spring:
            {
                var anim = compositor.CreateSpringVector3Animation();
                anim.DampingRatio = spring.DampingRatio;
                anim.Period = TimeSpan.FromSeconds(spring.Period);
                anim.FinalValue = targetValue;
                return anim;
            }
            case EaseCurve ease:
            {
                var anim = compositor.CreateVector3KeyFrameAnimation();
                var easing = compositor.CreateCubicBezierEasingFunction(
                    new Vector2(ease.Easing.X1, ease.Easing.Y1),
                    new Vector2(ease.Easing.X2, ease.Easing.Y2));
                anim.InsertKeyFrame(1.0f, targetValue, easing);
                anim.Duration = ease.Duration;
                return anim;
            }
            default:
            {
                var linear = curve as LinearCurve ?? new LinearCurve(TimeSpan.FromMilliseconds(300));
                var anim = compositor.CreateVector3KeyFrameAnimation();
                anim.InsertKeyFrame(1.0f, targetValue, compositor.CreateLinearEasingFunction());
                anim.Duration = linear.Duration;
                return anim;
            }
        }
    }

    /// <summary>
    /// Sets DelayTime on a CompositionAnimation (works for KeyFrameAnimation and NaturalMotionAnimation).
    /// </summary>
    internal static void SetDelay(CompositionAnimation anim, TimeSpan delay)
    {
        if (anim is KeyFrameAnimation kfa) kfa.DelayTime = delay;
        else if (anim is NaturalMotionAnimation nma) nma.DelayTime = delay;
    }

    /// <summary>
    /// Maps Reactor property names to WinUI compositor facade property names.
    /// </summary>
    /// <summary>
    /// Sets a Vector3 property on a UIElement, animating via the compositor if
    /// <see cref="AnimationScope.Current"/> is non-null.
    /// </summary>
    internal static void SetOrAnimateVector3(UIElement element, string property, Vector3 value, Element? source = null,
        bool respectPropertyFlags = false)
    {
        var curve = ResolveCurve(element, property, source, respectPropertyFlags);

        if (curve is null)
        {
            SetVector3Direct(element, property, value);
            return;
        }

        var visual = GetAnimationVisual(element);
        if (property == "Translation")
        {
            element.Translation = Vector3.Zero;
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        }
        var compositor = visual.Compositor;
        var target = CompositorPropertyName(property);

        switch (curve)
        {
            case SpringCurve spring:
            {
                var anim = compositor.CreateSpringVector3Animation();
                anim.DampingRatio = spring.DampingRatio;
                anim.Period = TimeSpan.FromSeconds(spring.Period);
                anim.FinalValue = value;
                visual.StartAnimation(target, anim);
                break;
            }
            case EaseCurve ease:
            {
                var anim = compositor.CreateVector3KeyFrameAnimation();
                var easing = compositor.CreateCubicBezierEasingFunction(
                    new Vector2(ease.Easing.X1, ease.Easing.Y1),
                    new Vector2(ease.Easing.X2, ease.Easing.Y2));
                anim.InsertKeyFrame(1.0f, value, easing);
                anim.Duration = ease.Duration;
                visual.StartAnimation(target, anim);
                break;
            }
            case LinearCurve linear:
            {
                var anim = compositor.CreateVector3KeyFrameAnimation();
                anim.InsertKeyFrame(1.0f, value, compositor.CreateLinearEasingFunction());
                anim.Duration = linear.Duration;
                visual.StartAnimation(target, anim);
                break;
            }
            default:
                SetVector3Direct(element, property, value);
                return;
        }
        MarkCompositionWrite(element, property, value);
    }

    private static Curve? ResolveCurve(UIElement element, string property, Element? source, bool respectPropertyFlags)
    {
        if (AnimationScope.HasScope && AnimationScope.Current is { } ambient) return ambient;
        var config = Reconciler.FindMountedElementValue(element, static el => el.AnimationConfig, source);
        if (config is null)
            return null;

        // Restoring another decorator's value must not restart a channel that the
        // current declaration excluded, including a channel just narrowed away.
        if (respectPropertyFlags && (config.Properties & PropertyFlag(property)) == 0)
            return null;

        return config.Curve;
    }

    private static string CompositorPropertyName(string property) => property switch
    {
        "Opacity" => "Opacity",
        "Scale" => "Scale",
        // Radians. Callers convert the degree value with float.DegreesToRadians.
        "Rotation" => "RotationAngle",
        "Translation" => "Translation",
        "CenterPoint" => "CenterPoint",
        _ => property,
    };

    private static void SetScalarDirect(UIElement element, string property, float value)
    {
        if (!ShouldWriteToVisual(element, property))
        {
            try
            {
                switch (property)
                {
                    case "Opacity": element.Opacity = value; break;
                    case "Rotation": element.Rotation = value; break;
                }
                return;
            }
            catch (UnauthorizedAccessException) { }
        }
        var visual = GetAnimationVisual(element);
        // Write the RotationAngle channel, not RotationAngleInDegrees: implicit animations
        // (.Animate(), the RotationTransition fallback) are keyed on "RotationAngle" and only
        // trigger on a write to that exact property.
        if (property == "Rotation") visual.RotationAngle = float.DegreesToRadians(value);
        else if (property == "Opacity")
        {
            element.Opacity = value;
            visual.Opacity = value;
        }
        MarkCompositionWrite(element, property, value);
    }

    private static void SetVector3Direct(UIElement element, string property, Vector3 value)
    {
        if (!ShouldWriteToVisual(element, property))
        {
            try
            {
                switch (property)
                {
                    case "Scale": element.Scale = value; break;
                    case "Translation": element.Translation = value; break;
                    case "CenterPoint": element.CenterPoint = value; break;
                }
                return;
            }
            catch (UnauthorizedAccessException) { }
        }
        var visual = GetAnimationVisual(element);
        switch (property)
        {
            case "Scale": visual.Scale = value; break;
            case "Translation":
                element.Translation = Vector3.Zero;
                ElementCompositionPreview.SetIsTranslationEnabled(element, true);
                visual.Properties.InsertVector3("Translation", value);
                break;
            case "CenterPoint": visual.CenterPoint = value; break;
        }
        MarkCompositionWrite(element, property, value);
    }
}

/// <summary>
/// Static accessor for the current Compositor instance.
/// </summary>
public static class CompositorProvider
{
    [ThreadStatic] private static Compositor? _current;

    public static Compositor? Current
    {
        get => _current;
        set => _current = value;
    }

    public static Compositor EnsureCompositor(UIElement element)
    {
        if (_current is not null) return _current;
        var visual = AnimationHelper.GetElementVisual(element);
        _current = visual.Compositor;
        return _current;
    }
}
