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

    // UIElement.Opacity stays the opacity channel (spec 066 §3 invariant 4). The visual's copy is
    // written as well once the visual was taken or Reactor wrote it there, because a UIElement
    // write that doesn't change the value commits nothing to the visual.
    private static bool WritesVisualOpacity(UIElement element)
        => element.GetValue(Reconciler.ReactorAttached.StateProperty) is Reconciler.ReactorState state
            && (state.VisualTaken || (state.CompositionModifierProperties & AnimateProperty.Opacity) != 0);

    /// <summary>
    /// Spec 066 §3 invariant 2 and §4: a transform write that leaves the effective value at
    /// identity doesn't take the visual. That holds when the visual hasn't been taken, the value is
    /// the identity, and the matching facade still reads identity. A setter that left the facade
    /// non-identity makes the write take the visual, so the modifier overrides the setter.
    /// </summary>
    private static bool SkipsHandoff(UIElement element, string property, bool identity)
    {
        // The actual handoff, not VisualTaken: .InteractionStates() sets that before its first
        // state change, while the facades are still writable.
        if (!identity || element.GetValue(Reconciler.ReactorAttached.StateProperty) is Reconciler.ReactorState { HandedOutVisual: not null })
            return false;
        return property switch
        {
            "Scale" => element.Scale == Vector3.One,
            "Rotation" => element.Rotation == 0,
            "Translation" => element.Translation == Vector3.Zero,
            "CenterPoint" => element.CenterPoint == Vector3.Zero,
            _ => false,
        };
    }

    private static bool IsIdentity(string property, Vector3 value)
        => property == "Scale" ? value == Vector3.One : value == Vector3.Zero;

    /// <summary>
    /// Spec 066 §4.3: enables translation on the visual for a declared Translation channel
    /// (<c>.Translation()</c>, a <c>translation:</c> keyframe, an <c>.InteractionStates</c>
    /// translation). Nothing else enables it, because once it is enabled and the element has
    /// rendered, WinUI ignores <see cref="UIElement.Translation"/> writes, which would break a drag
    /// that writes the facade (#1642). The first time, a value user code left on the facade moves
    /// to the visual, since the two would otherwise add up until the next render.
    /// </summary>
    internal static void EnableTranslation(UIElement element, Visual visual)
        => EnableTranslation(element, Reconciler.GetOrCreateReactorState(element), visual);

    private static void EnableTranslation(UIElement element, Reconciler.ReactorState state, Visual visual)
    {
        if (state.TranslationEnabled) return;
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        state.TranslationEnabled = true;
        var facade = element.Translation;
        if (facade == Vector3.Zero) return;
        element.Translation = Vector3.Zero;
        visual.Properties.InsertVector3("Translation", facade);
        // A hover-out returns here, not to zero; a modifier write replaces it.
        (state.TransformRest ??= new()).Translation = facade;
    }

    /// <summary>Whether Reactor enabled translation on the element's visual (<see cref="EnableTranslation(UIElement, Visual)"/>).</summary>
    internal static bool TranslationEnabled(UIElement element)
        => element.GetValue(Reconciler.ReactorAttached.StateProperty) is Reconciler.ReactorState { TranslationEnabled: true };

    private static void MarkCompositionWrite(Reconciler.ReactorState state, string property, float value)
    {
        MarkCompositionWrite(state, property);
        if (property == "Rotation") (state.TransformRest ??= new()).RotationDegrees = value;
        // An animated .Opacity() leaves UIElement.Opacity alone, so the rest value is kept here.
        else if (property == "Opacity") (state.TransformRest ??= new()).Opacity = value;
    }

    private static void MarkCompositionWrite(Reconciler.ReactorState state, string property, Vector3 value)
    {
        MarkCompositionWrite(state, property);
        if (property == "Scale") (state.TransformRest ??= new()).Scale = value;
        else if (property == "Translation") (state.TransformRest ??= new()).Translation = value;
        else if (property == "CenterPoint") (state.TransformRest ??= new()).CenterPoint = value;
    }

    private static Reconciler.ReactorState MarkCompositionWrite(Reconciler.ReactorState state, string property)
    {
        var flag = PropertyFlag(property);
        state.CompositionModifierProperties |= flag;
        state.KeyframeChannelOwners?.Remove(flag);
        state.DeferredModifierResets &= ~flag;
        if (flag == AnimateProperty.Opacity) Reconciler.CancelOpacityRestart(state);
        return state;
    }

    private static Visual GetAnimationVisual(UIElement element, out Reconciler.ReactorState state) => GetElementVisual(element, out state);

    // The diagnostic is a filter that never catches, so the exception keeps its stack (spec 066 §9 row 1).
    private static Visual TakeVisual(UIElement element)
    {
        try { return ElementCompositionPreview.GetElementVisual(element); }
        catch (UnauthorizedAccessException) when (FacadeConflictDiagnostic.ReportBlockedHandoff(element)) { throw; }
    }

    private const Vector3TransitionComponents AllComponents =
        Vector3TransitionComponents.X | Vector3TransitionComponents.Y | Vector3TransitionComponents.Z;

    /// <summary>The duration and axis mask of a XAML <see cref="ScalarTransition"/> or <see cref="Vector3Transition"/>.</summary>
    internal readonly record struct TransitionTiming(TimeSpan Duration, Vector3TransitionComponents Components)
    {
        internal static TransitionTiming Of(object transition) => transition switch
        {
            ScalarTransition scalar => new(scalar.Duration, AllComponents),
            Vector3Transition vector => new(vector.Duration, vector.Components),
            _ => throw new ArgumentException("Not a XAML transition.", nameof(transition)),
        };

        internal bool Masked => (Components & AllComponents) != AllComponents;
    }

    /// <summary>
    /// The XAML implicit transitions' curve, cubic-bezier (0.8,0,0.2,1), measured sample for
    /// sample (spec 066 §5.1). It is <see cref="Easing.Standard"/>.
    /// </summary>
    internal static readonly Easing TransitionEasing = Easing.Standard;

    [ThreadStatic] private static Compositor? s_transitionEasingOwner;
    [ThreadStatic] private static CubicBezierEasingFunction? s_transitionEasing;

    // One easing function per compositor, shared by every transition animation.
    private static CubicBezierEasingFunction TransitionEasingFunction(Compositor compositor)
    {
        if (s_transitionEasing is null || !ReferenceEquals(s_transitionEasingOwner, compositor))
        {
            s_transitionEasing = compositor.CreateCubicBezierEasingFunction(
                new Vector2(TransitionEasing.X1, TransitionEasing.Y1), new Vector2(TransitionEasing.X2, TransitionEasing.Y2));
            s_transitionEasingOwner = compositor;
        }
        return s_transitionEasing;
    }

    // A composition animation needs a positive duration. A zero XAML transition jumps, and so
    // does a 1 ms animation, for practical purposes.
    private static TimeSpan TransitionDuration(TimeSpan duration)
        => duration < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : duration;

    /// <summary>
    /// The implicit animation for a <c>.OpacityTransition()</c>, <c>.ScaleTransition()</c> or
    /// <c>.RotationTransition()</c> key: the transition's duration with the XAML curve. A Vector3
    /// transition whose <see cref="Vector3Transition.Components"/> leaves an axis out starts that
    /// axis at its final value, so it jumps while the others animate (spec 066 §5.2).
    /// </summary>
    internal static CompositionAnimation CreateTransitionAnimation(Compositor compositor, string target, TransitionTiming timing)
    {
        var easing = TransitionEasingFunction(compositor);
        KeyFrameAnimation anim;
        if (target is "Opacity" or "RotationAngle")
        {
            var scalar = compositor.CreateScalarKeyFrameAnimation();
            scalar.InsertExpressionKeyFrame(1.0f, "this.FinalValue", easing);
            anim = scalar;
        }
        else
        {
            var vector = compositor.CreateVector3KeyFrameAnimation();
            if (timing.Masked)
                vector.InsertExpressionKeyFrame(0.0f, MaskedStart(timing.Components, "this.FinalValue"));
            vector.InsertExpressionKeyFrame(1.0f, "this.FinalValue", easing);
            anim = vector;
        }
        anim.Duration = TransitionDuration(timing.Duration);
        anim.Target = target;
        return anim;
    }

    // Keyframe 0 of a masked Vector3 transition: the animated axes start where they are, the
    // others at their final value.
    private static string MaskedStart(Vector3TransitionComponents components, string final)
    {
        static string Axis(bool animated, string final, char axis)
            => animated ? "this.StartingValue." + axis : final + "." + axis;
        return "Vector3(" + Axis(components.HasFlag(Vector3TransitionComponents.X), final, 'X') + ", "
            + Axis(components.HasFlag(Vector3TransitionComponents.Y), final, 'Y') + ", "
            + Axis(components.HasFlag(Vector3TransitionComponents.Z), final, 'Z') + ")";
    }

    /// <summary>
    /// Returns the element's handoff visual. Every Reactor feature that needs the visual of a
    /// user element must take it here rather than from <see cref="ElementCompositionPreview"/>,
    /// because this is where the native <c>VisualTaken</c> marker is set (spec 066 §7.2, #1643).
    /// </summary>
    /// <remarks>
    /// WinUI refuses <c>GetElementVisual</c> while <see cref="UIElement.Scale"/>,
    /// <see cref="UIElement.Rotation"/> or <see cref="UIElement.CenterPoint"/> holds a non-identity
    /// value, and refuses facade writes after the handoff (#1613). Reactor never writes those
    /// facades (spec 066 §3 invariant 1), so such a value was written by user code, a
    /// <c>.Set(...)</c> setter that runs before the modifiers. This resets it to identity, takes
    /// the visual, writes the same value to it, and marks it as a composition write so later
    /// modifier updates and unsets follow it to the visual.
    /// <see cref="UIElement.Translation"/> doesn't block the handoff and stays writable after it,
    /// so it stays where user code put it: it moves only when a Translation channel is declared,
    /// through <see cref="EnableTranslation(UIElement, Visual)"/> (§4.3, #1642).
    /// </remarks>
    internal static Visual GetElementVisual(UIElement element) => GetElementVisual(element, out _);

    /// <inheritdoc cref="GetElementVisual(UIElement)"/>
    /// <param name="element">The element whose visual to take.</param>
    /// <param name="state">The element's attached state, read once for the caller's bookkeeping.</param>
    internal static Visual GetElementVisual(UIElement element, out Reconciler.ReactorState state)
    {
        if (element.GetValue(Reconciler.ReactorAttached.StateProperty) is Reconciler.ReactorState { HandedOutVisual: { } handedOut } known)
        {
            state = known;
            return handedOut;
        }
        var scale = element.Scale;
        var rotation = element.Rotation;
        var center = element.CenterPoint;
        bool moveScale = scale != Vector3.One, moveRotation = rotation != 0, moveCenter = center != Vector3.Zero;
        if (!(moveScale || moveRotation || moveCenter))
        {
            var taken = TakeVisual(element);
            state = MarkHandedOut(element, taken);
            return taken;
        }

        if (moveScale) element.Scale = Vector3.One;
        if (moveRotation) element.Rotation = 0;
        if (moveCenter) element.CenterPoint = Vector3.Zero;

        Visual visual;
        try
        {
            visual = TakeVisual(element);
        }
        catch
        {
            // Something else still blocks the handoff; leave the element as it was.
            if (moveScale) element.Scale = scale;
            if (moveRotation) element.Rotation = rotation;
            if (moveCenter) element.CenterPoint = center;
            throw;
        }
        state = MarkHandedOut(element, visual);

        // Facade Rotation is in degrees; RotationAngle is in radians.
        if (moveScale) visual.Scale = scale;
        if (moveRotation) visual.RotationAngle = rotation * MathF.PI / 180f;
        if (moveCenter) visual.CenterPoint = center;
        if (moveScale) MarkCompositionWrite(state, "Scale", scale);
        if (moveRotation) MarkCompositionWrite(state, "Rotation", rotation);
        if (moveCenter) MarkCompositionWrite(state, "CenterPoint", center);
        return visual;
    }

    private static Reconciler.ReactorState MarkHandedOut(UIElement element, Visual visual)
    {
        var state = Reconciler.GetOrCreateReactorState(element);
        state.VisualTaken = true;
        state.HandedOutVisual = visual;
        return state;
    }

    /// <summary>
    /// Releases a modifier's compositor write. A modifier that never wrote the visual (an
    /// identity value on a control whose visual wasn't taken) wrote nothing, so there is nothing
    /// to release, and this must not call GetElementVisual: handing out the visual moves the
    /// element to the pool's visual lane and locks its facades.
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
                case "CenterPoint": rest.CenterPoint = Vector3.Zero; break;
                case "Opacity": rest.Opacity = null; break;
            }
        }
        if (state is null || (state.CompositionModifierProperties & flag) == 0) return;
        var visual = GetElementVisual(element);
        var animated = AnimatedChannel(visual, state, property, out var channel);
        // A shared target can replace the deferred animation before its decorator plays.
        if (expectedController is not null && !Equals(expectedController, animated.TryGetAnimationController(channel)))
        {
            ClearCompositionOwnership(state, flag);
            return;
        }
        if (state.KeyframeChannelOwners?.TryGetValue(flag, out var owner) == true && owner.Entry.Definition.Loop
            && animated.TryGetAnimationController(channel) is { } controller)
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
                    visual.Properties.InsertVector3("Translation", Vector3.Zero);
                    break;
                case "CenterPoint" when animated is CompositionPropertySet properties:
                    // Spec 066 §8.2: under an origin, the offset resets and the expression stays.
                    properties.StopAnimation(channel);
                    properties.InsertVector3(channel, Vector3.Zero);
                    break;
                case "CenterPoint":
                    visual.CenterPoint = Vector3.Zero;
                    TransformOriginBinding.Displaced(state);
                    break;
            }
        }
        finally { visual.ImplicitAnimations = implicitAnimations; }
        ClearCompositionOwnership(state, flag);
    }

    /// <summary>
    /// The object and property a modifier's channel animates: the visual's own property, except
    /// <c>CenterPoint</c> under an active <c>.TransformOrigin</c>, whose value is the origin
    /// expression's offset in the visual's property set (spec 066 §8.2).
    /// </summary>
    private static CompositionObject AnimatedChannel(Visual visual, Reconciler.ReactorState? state, string property, out string channel)
    {
        if (property == "CenterPoint" && TransformOriginBinding.IsActive(state))
        {
            channel = TransformOriginBinding.OffsetProperty;
            return visual.Properties;
        }
        channel = CompositorPropertyName(property);
        return visual;
    }

    private static void ClearCompositionOwnership(Reconciler.ReactorState state, AnimateProperty flag)
    {
        state.CompositionModifierProperties &= ~flag;
        state.KeyframeChannelOwners?.Remove(flag);
        state.DeferredModifierResets &= ~flag;
        if (flag == AnimateProperty.Opacity) Reconciler.CancelOpacityRestart(state);
    }

    /// <summary>
    /// Sets a scalar property on a UIElement, animating via the compositor when
    /// <see cref="ResolvePolicy(UIElement, string, Element?, Func{UIElement, AnimateProperty, AnimationConfig?})"/> finds a curve.
    /// </summary>
    internal static void SetOrAnimate(UIElement element, string property, float value, Element? source = null, Func<UIElement, AnimateProperty, AnimationConfig?>? chainLookup = null)
    {
        if (property == "Rotation" && SkipsHandoff(element, property, value == 0)) return;
        var curve = ResolvePolicy(element, property, source, chainLookup).Curve;

        if (curve is null)
        {
            SetScalarDirect(element, property, value);
            return;
        }

        var visual = GetAnimationVisual(element, out var state);
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
        MarkCompositionWrite(state, property, value);
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
    /// Sets a Vector3 property on a UIElement, animating via the compositor when
    /// <see cref="ResolvePolicy(UIElement, string, Element?, Func{UIElement, AnimateProperty, AnimationConfig?})"/> finds a curve.
    /// </summary>
    internal static void SetOrAnimateVector3(UIElement element, string property, Vector3 value, Element? source = null, Func<UIElement, AnimateProperty, AnimationConfig?>? chainLookup = null)
    {
        if (SkipsHandoff(element, property, IsIdentity(property, value))) return;
        var (curve, components) = ResolvePolicy(element, property, source, chainLookup);

        if (curve is null)
        {
            SetVector3Direct(element, property, value);
            return;
        }

        var visual = GetAnimationVisual(element, out var state);
        if (property == "Translation") EnableTranslation(element, state, visual);
        var compositor = visual.Compositor;
        // Spec 066 §8.2: under a .TransformOrigin a CenterPoint curve animates the expression's offset.
        var animated = AnimatedChannel(visual, state, property, out var target);
        if (property == "CenterPoint" && ReferenceEquals(animated, visual)) TransformOriginBinding.Displaced(state);

        switch (curve)
        {
            case SpringCurve spring:
            {
                var anim = compositor.CreateSpringVector3Animation();
                anim.DampingRatio = spring.DampingRatio;
                anim.Period = TimeSpan.FromSeconds(spring.Period);
                anim.FinalValue = value;
                animated.StartAnimation(target, anim);
                break;
            }
            case EaseCurve ease:
            {
                var anim = compositor.CreateVector3KeyFrameAnimation();
                var easing = compositor.CreateCubicBezierEasingFunction(
                    new Vector2(ease.Easing.X1, ease.Easing.Y1),
                    new Vector2(ease.Easing.X2, ease.Easing.Y2));
                // A masked .TranslationTransition(): the axes it leaves out jump (spec 066 §5.3).
                if ((components & AllComponents) != AllComponents)
                {
                    anim.SetVector3Parameter("final", value);
                    anim.InsertExpressionKeyFrame(0.0f, MaskedStart(components, "final"));
                }
                anim.InsertKeyFrame(1.0f, value, easing);
                anim.Duration = ease.Duration;
                animated.StartAnimation(target, anim);
                break;
            }
            case LinearCurve linear:
            {
                var anim = compositor.CreateVector3KeyFrameAnimation();
                anim.InsertKeyFrame(1.0f, value, compositor.CreateLinearEasingFunction());
                anim.Duration = linear.Duration;
                animated.StartAnimation(target, anim);
                break;
            }
            default:
                SetVector3Direct(element, property, value);
                return;
        }
        MarkCompositionWrite(state, property, value);
    }

    /// <summary>How a modifier write moves: a curve, and for Translation the axes it animates.</summary>
    internal readonly record struct WritePolicy(Curve? Curve, Vector3TransitionComponents Components);

    /// <summary>
    /// Spec 066 §5.3. The ambient <c>WithAnimation</c> scope wins, then <c>.Animate()</c> when its
    /// <see cref="AnimateProperty"/> flags include the channel, then, for Translation, a declared
    /// <c>.TranslationTransition()</c>, as a write-time animation with the XAML curve and its axis
    /// mask. An implicit <c>"Translation"</c> key would never fire. Opacity, Scale and Rotation
    /// transitions are implicit keys on the visual, so a direct write starts them.
    /// </summary>
    private static WritePolicy ResolvePolicy(UIElement element, string property, Element? source,
        Func<UIElement, AnimateProperty, AnimationConfig?>? chainLookup = null)
    {
        var ambient = AnimationScope.HasScope ? AnimationScope.Current : null;
        var channel = PropertyFlag(property);
        AnimationConfig? config = null;
        if (ambient is null)
        {
            // The outermost declaration that animates this channel: one that excludes it doesn't
            // hide an inner one that includes it, and an inner layer's write still finds an outer
            // one. The reconciler walks the whole shared-target chain of an update; a mount, which
            // has no chain yet, searches from the writing layer.
            config = chainLookup?.Invoke(element, channel);
            config ??= Reconciler.FindMountedElementValue(element,
                el => el.AnimationConfig is { } c && (c.Properties & channel) != 0 ? c : null, source);
        }
        TransitionTiming? translation = null;
        if (ambient is null && property == "Translation"
            && ImplicitAnimationSlots.TranslationTransition(element) is { } transition)
            translation = TransitionTiming.Of(transition);
        return ResolvePolicy(ambient, config, channel, translation);
    }

    /// <summary>The pure part of <see cref="ResolvePolicy(UIElement, string, Element?, Func{UIElement, AnimateProperty, AnimationConfig?})"/>.</summary>
    internal static WritePolicy ResolvePolicy(Curve? ambient, AnimationConfig? animate, AnimateProperty channel, TransitionTiming? translation)
    {
        if (ambient is not null) return new(ambient, AllComponents);
        // A channel the declaration excludes doesn't animate through it, so a declared transition
        // on that channel keeps it, as it keeps the implicit key (§6.1).
        if (animate is not null && (animate.Properties & channel) != 0) return new(animate.Curve, AllComponents);
        if (translation is { } t) return new(new EaseCurve(TransitionDuration(t.Duration), TransitionEasing), t.Components);
        return new(null, AllComponents);
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
        if (property == "Opacity")
        {
            if (!WritesVisualOpacity(element))
            {
                element.Opacity = value;
                return;
            }
            var opacityVisual = GetAnimationVisual(element, out var opacityState);
            element.Opacity = value;
            opacityVisual.Opacity = value;
            MarkCompositionWrite(opacityState, property, value);
            return;
        }
        // Write the RotationAngle channel, not RotationAngleInDegrees: implicit animations
        // (.Animate(), the RotationTransition key) are keyed on "RotationAngle" and only
        // trigger on a write to that exact property.
        GetAnimationVisual(element, out var state).RotationAngle = float.DegreesToRadians(value);
        MarkCompositionWrite(state, property, value);
    }

    private static void SetVector3Direct(UIElement element, string property, Vector3 value)
    {
        var visual = GetAnimationVisual(element, out var state);
        switch (property)
        {
            case "Scale": visual.Scale = value; break;
            case "Translation":
                EnableTranslation(element, state, visual);
                visual.Properties.InsertVector3("Translation", value);
                break;
            case "CenterPoint" when TransformOriginBinding.IsActive(state):
                // Spec 066 §8.2: the origin expression owns CenterPoint; the value is its offset.
                visual.Properties.StopAnimation(TransformOriginBinding.OffsetProperty);
                visual.Properties.InsertVector3(TransformOriginBinding.OffsetProperty, value);
                break;
            case "CenterPoint":
                visual.CenterPoint = value;
                TransformOriginBinding.Displaced(state);
                break;
        }
        MarkCompositionWrite(state, property, value);
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
