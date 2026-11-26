using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinShapes = Microsoft.UI.Xaml.Shapes;
using ReactorSourceMap = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap;

namespace Microsoft.UI.Reactor.Core;

// Reconciler partial: Implicit, theme, layout, property, enter/exit transitions, and stagger.
public sealed partial class Reconciler
{
    /// <summary>
    /// Thread-static stagger context for enter transitions. When a parent with StaggerConfig
    /// mounts children, it pushes this context so each child's ApplyEnterTransition can
    /// compute stagger delay from its index among siblings.
    /// </summary>
    [ThreadStatic] private static StaggerScope? _staggerScope;

    private sealed class StaggerScope
    {
        public TimeSpan Delay;
        public int NextIndex;
        public StaggerScope? Previous;
    }

    private static void PushStaggerScope(TimeSpan delay)
    {
        _staggerScope = new StaggerScope { Delay = delay, NextIndex = 0, Previous = _staggerScope };
    }

    private static void PopStaggerScope()
    {
        if (_staggerScope is not null)
            _staggerScope = _staggerScope.Previous;
    }

    private static (int index, TimeSpan delay) ConsumeStaggerIndex()
    {
        if (_staggerScope is null) return (0, default);
        var idx = _staggerScope.NextIndex++;
        return (idx, _staggerScope.Delay);
    }

    /// <summary>
    /// Applies the implicit and theme transitions a layer declares. <paramref name="previous"/> is
    /// what the same layer declared on the last render; a channel it had and
    /// <paramref name="implicitT"/> lacks is released (#1626). <paramref name="slot"/> is the
    /// layer's depth in the shared-target chain (spec 066 §6.1).
    /// </summary>
    internal static void ApplyTransitions(UIElement uie, ImplicitTransitions? previous, ImplicitTransitions? implicitT,
        ThemeTransitions? themeT, int slot = 0)
    {
        ApplyImplicitTransitions(uie, previous, implicitT, slot);
        ApplyThemeTransitions(uie, themeT);
    }

    /// <summary>True when the transitions include one that runs on the composition visual (spec 066 §5).</summary>
    internal static bool TakesVisual(ImplicitTransitions? transitions)
        => transitions is not null && (transitions.Opacity is not null || transitions.Scale is not null
            || transitions.Rotation is not null || transitions.Translation is not null);

    /// <summary>
    /// Spec 066 §5. Opacity, Scale and Rotation transitions are implicit keys on the composition
    /// visual, and Translation a write-time animation, all with the XAML curve. Reactor never sets
    /// the <c>UIElement.*Transition</c> properties, which would block the handoff (#1623).
    /// Background stays a XAML transition (§5.6). Update runs this before setters and modifiers,
    /// so a value changed in the same reconcile follows the new policy (§6.3).
    /// </summary>
    internal static void ApplyImplicitTransitions(UIElement uie, ImplicitTransitions? previous, ImplicitTransitions? implicitT, int slot)
    {
        if (implicitT is null && previous is null) return;

        var current = implicitT;
        if (TakesVisual(implicitT))
        {
            // The visual is taken even with animations off, so readback, the pool lane and the
            // setters that throw don't depend on the system setting (§9 rows 1 and 2).
            AnimationHelper.GetElementVisual(uie);
            // XAML transitions honour "Animation effects" and composition animations don't, so
            // with the setting off no key is installed and Translation writes jump (§5.5).
            if (!global::Microsoft.UI.Reactor.Navigation.TransitionEngine.AnimationsEnabled) current = null;
        }

        ApplyTransitionChannel(uie, "Opacity", previous?.Opacity, current?.Opacity, slot);
        ApplyTransitionChannel(uie, "RotationAngle", previous?.Rotation, current?.Rotation, slot);
        ApplyTransitionChannel(uie, "Scale", previous?.Scale, current?.Scale, slot);
        ApplyTransitionChannel(uie, ImplicitAnimationSlots.TranslationKey, previous?.Translation, current?.Translation, slot);

        if (implicitT?.Background is { } background)
            SetBackgroundTransition(uie, background);
        else if (previous?.Background is not null)
            SetBackgroundTransition(uie, null);
    }

    private static void SetBackgroundTransition(UIElement uie, BrushTransition? transition)
    {
        switch (uie)
        {
            case WinUI.Grid g: g.BackgroundTransition = transition; break;
            case WinUI.StackPanel sp: sp.BackgroundTransition = transition; break;
            case WinUI.ContentPresenter cp: cp.BackgroundTransition = transition; break;
        }
    }

    internal static void ApplyThemeTransitions(UIElement uie, ThemeTransitions? themeT)
    {
        if (themeT?.Children is { } children)
        {
            var tc = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection();
            foreach (var t in children) tc.Add(t);
            switch (uie)
            {
                case WinUI.StackPanel sp: sp.ChildrenTransitions = tc; break;
                case WinUI.Grid g: g.ChildrenTransitions = tc; break;
                case WinUI.Canvas c: c.ChildrenTransitions = tc; break;
                case WinUI.Border b: b.ChildTransitions = tc; break;
                case WinUI.ContentPresenter cp: cp.ContentTransitions = tc; break;
                case WinUI.ContentControl cc: cc.ContentTransitions = tc; break;
            }
        }

        if (themeT?.ItemContainer is { } itemTransitions)
        {
            var tc = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection();
            foreach (var t in itemTransitions) tc.Add(t);
            if (uie is WinUI.ListViewBase lvb)
                lvb.ItemContainerTransitions = tc;
        }
    }

    private static void ApplyTransitionChannel(UIElement uie, string key, object? previous, object? current, int slot)
    {
        if (current is not null)
            ImplicitAnimationSlots.Claim(uie, key, ImplicitFeature.Transition, slot, current);
        else if (previous is not null)
            ImplicitAnimationSlots.Release(uie, key, ImplicitFeature.Transition, slot);
    }

    /// <summary>
    /// Sets up Composition-layer implicit animations on the element's Visual so that
    /// layout-driven Offset (and optionally Size) changes animate smoothly.
    /// Runs entirely on the Composition thread — zero managed-code callbacks during animation.
    /// The keys are claimed through <see cref="ImplicitAnimationSlots"/>, so the element's
    /// transitions and <c>.Animate()</c> keys survive (#1625).
    /// </summary>
    internal static void ApplyLayoutAnimation(UIElement uie, LayoutAnimationConfig config, int slot = 0)
    {
        ElementPool.MarkVisualTaken(uie);
        var visual = AnimationHelper.GetElementVisual(uie);
        var compositor = visual.Compositor;

        if (config.AnimateOffset)
        {
            CompositionAnimation offset;
            if (config.UseSpring)
            {
                var spring = compositor.CreateSpringVector3Animation();
                spring.DampingRatio = config.DampingRatio;
                spring.Period = TimeSpan.FromSeconds(config.Period);
                spring.Target = "Offset";
                offset = spring;
            }
            else
            {
                var anim = compositor.CreateVector3KeyFrameAnimation();
                anim.InsertExpressionKeyFrame(1.0f, "this.FinalValue", compositor.CreateLinearEasingFunction());
                anim.Duration = config.Duration;
                anim.Target = "Offset";
                offset = anim;
            }
            ImplicitAnimationSlots.Claim(uie, "Offset", ImplicitFeature.Layout, slot, offset);
        }
        else
        {
            ImplicitAnimationSlots.Release(uie, "Offset", ImplicitFeature.Layout, slot);
        }

        if (config.AnimateSize)
        {
            CompositionAnimation size;
            if (config.UseSpring)
            {
                var spring = compositor.CreateSpringVector2Animation();
                spring.DampingRatio = config.DampingRatio;
                spring.Period = TimeSpan.FromSeconds(config.Period);
                spring.Target = "Size";
                size = spring;
            }
            else
            {
                var anim = compositor.CreateVector2KeyFrameAnimation();
                anim.InsertExpressionKeyFrame(1.0f, "this.FinalValue", compositor.CreateLinearEasingFunction());
                anim.Duration = config.Duration;
                anim.Target = "Size";
                size = anim;
            }
            ImplicitAnimationSlots.Claim(uie, "Size", ImplicitFeature.Layout, slot, size);
        }
        else
        {
            ImplicitAnimationSlots.Release(uie, "Size", ImplicitFeature.Layout, slot);
        }
    }

    /// <summary>
    /// Releases the layout animation keys a layer claimed. Called when an element previously
    /// had LayoutAnimation but no longer does. Keys other features claim stay (#1625).
    /// </summary>
    internal static void ClearLayoutAnimation(UIElement uie, int slot = 0)
    {
        ImplicitAnimationSlots.Release(uie, "Offset", ImplicitFeature.Layout, slot);
        ImplicitAnimationSlots.Release(uie, "Size", ImplicitFeature.Layout, slot);
    }

    // ════════════════════════════════════════════════════════════════
    //  Property animation (.Animate() modifier)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Claims implicit animation keys on the element's composition Visual for the targeted
    /// properties using the specified Curve. A layout animation keeps Offset while it's declared,
    /// and <c>.Animate()</c> wins Opacity, Scale and RotationAngle over a transition (spec 066 §6.1).
    /// </summary>
    internal static void ApplyPropertyAnimation(UIElement uie, AnimationConfig config, int slot = 0)
    {
        ElementPool.MarkVisualTaken(uie);
        var visual = AnimationHelper.GetElementVisual(uie);
        var compositor = visual.Compositor;

        var props = config.Properties;
        var curve = config.Curve;

        if (props.HasFlag(AnimateProperty.Opacity))
            ImplicitAnimationSlots.Claim(uie, "Opacity", ImplicitFeature.Animate, slot,
                AnimationHelper.CreateScalarImplicitAnimation(compositor, "Opacity", curve));

        if (props.HasFlag(AnimateProperty.Offset))
            ImplicitAnimationSlots.Claim(uie, "Offset", ImplicitFeature.Animate, slot,
                AnimationHelper.CreateVector3ImplicitAnimation(compositor, "Offset", curve));

        if (props.HasFlag(AnimateProperty.Scale))
            ImplicitAnimationSlots.Claim(uie, "Scale", ImplicitFeature.Animate, slot,
                AnimationHelper.CreateVector3ImplicitAnimation(compositor, "Scale", curve));

        if (props.HasFlag(AnimateProperty.Rotation))
            ImplicitAnimationSlots.Claim(uie, "RotationAngle", ImplicitFeature.Animate, slot,
                AnimationHelper.CreateScalarImplicitAnimation(compositor, "RotationAngle", curve));

        if (props.HasFlag(AnimateProperty.CenterPoint))
            ImplicitAnimationSlots.Claim(uie, "CenterPoint", ImplicitFeature.Animate, slot,
                AnimationHelper.CreateVector3ImplicitAnimation(compositor, "CenterPoint", curve));
    }

    /// <summary>
    /// Releases the specified <c>.Animate()</c> channels a layer claimed. A key another feature
    /// still claims stays, with that feature's animation. Update calls this before setters run, so
    /// a claim that takes over a released key returns only at
    /// <see cref="ImplicitAnimationSlots.FlushDeferred"/>, after the setters' writes.
    /// </summary>
    internal static void ClearPropertyAnimation(UIElement uie, AnimateProperty properties, int slot = 0)
    {
        // .Animate() took the visual; taking it again keeps the facade migration this path always ran.
        AnimationHelper.GetElementVisual(uie);
        if (properties.HasFlag(AnimateProperty.Opacity)) ImplicitAnimationSlots.Release(uie, "Opacity", ImplicitFeature.Animate, slot, deferRestore: true);
        if (properties.HasFlag(AnimateProperty.Offset)) ImplicitAnimationSlots.Release(uie, "Offset", ImplicitFeature.Animate, slot, deferRestore: true);
        if (properties.HasFlag(AnimateProperty.Scale)) ImplicitAnimationSlots.Release(uie, "Scale", ImplicitFeature.Animate, slot, deferRestore: true);
        if (properties.HasFlag(AnimateProperty.Rotation)) ImplicitAnimationSlots.Release(uie, "RotationAngle", ImplicitFeature.Animate, slot, deferRestore: true);
        if (properties.HasFlag(AnimateProperty.CenterPoint)) ImplicitAnimationSlots.Release(uie, "CenterPoint", ImplicitFeature.Animate, slot, deferRestore: true);
    }

    // ════════════════════════════════════════════════════════════════
    //  Enter/exit transitions (.Transition() modifier)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies enter transition on mount: sets initial visual state then animates to final state.
    /// </summary>
    internal static void ApplyEnterTransition(UIElement uie, ElementTransition transition, int staggerIndex = 0, TimeSpan staggerDelay = default, Reconciler? owner = null)
    {
        var enter = transition.GetEnterTransition();
        if (enter is null) return;
        ElementPool.MarkVisualTaken(uie);

        var visual = AnimationHelper.GetElementVisual(uie);
        var compositor = visual.Compositor;
        var curve = transition.Curve ?? Curve.Ease(300, Easing.Decelerate);

        // Override with ambient scope if present
        if (AnimationScope.HasScope && AnimationScope.Current is not null)
            curve = AnimationScope.Current;

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var delay = staggerIndex > 0 ? TimeSpan.FromTicks(staggerDelay.Ticks * staggerIndex) : TimeSpan.Zero;

        // The enter writes its start values straight to the visual. An implicit key on the same
        // channel (a transition, .Animate()) would animate that write and the enter would start
        // from where the element already is (spec 066 §6.1).
        WithoutImplicitAnimations(visual, () => ApplyTransitionAnimations(uie, visual, compositor, enter, curve, isEnter: true, delay, owner));

        batch.End();
    }

    /// <summary>Runs visual writes that must not fire the visual's implicit animation keys.</summary>
    internal static void WithoutImplicitAnimations(Visual visual, Action write)
    {
        var implicitAnimations = visual.ImplicitAnimations;
        if (implicitAnimations is null) { write(); return; }
        visual.ImplicitAnimations = null;
        try { write(); }
        finally { visual.ImplicitAnimations = implicitAnimations; }
    }

    /// <summary>
    /// Applies exit transition before unmount: animates out, then invokes onComplete to remove/pool.
    /// <paramref name="onComplete"/> runs only while <paramref name="exit"/> is still the control's
    /// current exit (see <see cref="BeginExit"/>).
    /// </summary>
    internal void ApplyExitTransition(UIElement uie, ElementTransition transition, Action onComplete, object exit, int staggerIndex = 0, TimeSpan staggerDelay = default)
    {
        var exitTransition = transition.GetExitTransition();
        if (exitTransition is null)
        {
            if (EndExit(uie, exit)) onComplete();
            return;
        }
        ElementPool.MarkVisualTaken(uie);
        // The exit owns the leaving element now. A pending enter fade restart must not fade it back
        // in, nor keep it hidden through the exit (#1680).
        CancelOpacityRestart(uie);

        var visual = AnimationHelper.GetElementVisual(uie);
        var compositor = visual.Compositor;
        var curve = transition.Curve ?? Curve.Ease(300, Easing.Decelerate);

        if (AnimationScope.HasScope && AnimationScope.Current is not null)
            curve = AnimationScope.Current;

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var delay = staggerIndex > 0 ? TimeSpan.FromTicks(staggerDelay.Ticks * staggerIndex) : TimeSpan.Zero;

        ApplyTransitionAnimations(uie, visual, compositor, exitTransition, curve, isEnter: false, delay);

        batch.End();
        batch.Completed += (_, _) =>
        {
            // A control torn down with an ancestor mid-exit was already pooled, and the pool's
            // visual reset restored it (spec 066 §7.3). It may belong to another renter now.
            if (!EndExit(uie, exit)) return;
            // Reset visual state after exit, without animating it through an implicit key
            WithoutImplicitAnimations(visual, () =>
            {
                visual.Opacity = 1;
                visual.Offset = Vector3.Zero;
                visual.Scale = Vector3.One;
            });
            onComplete();
        };
    }

    private static void ApplyTransitionAnimations(
        UIElement uie, Visual visual, Compositor compositor,
        Animation.Transition transition, Curve curve, bool isEnter, TimeSpan delay, Reconciler? owner = null)
    {
        switch (transition)
        {
            case FadeTransition:
                ApplyFadeTransition(uie, compositor, curve, isEnter, delay, owner);
                break;
            case SlideTransition slide:
                ApplySlideTransition(uie, visual, compositor, slide.Edge, curve, isEnter, delay);
                break;
            case ScaleTransition scale:
                ApplyScaleTransition(uie, visual, compositor, scale.From, curve, isEnter, delay);
                break;
            case CombinedTransition combined:
                ApplyTransitionAnimations(uie, visual, compositor, combined.First, curve, isEnter, delay, owner);
                ApplyTransitionAnimations(uie, visual, compositor, combined.Second, curve, isEnter, delay, owner);
                break;
            case AsymmetricTransition asym:
                var inner = isEnter ? asym.EnterTransition : asym.ExitTransition;
                if (inner is not null)
                    ApplyTransitionAnimations(uie, visual, compositor, inner, curve, isEnter, delay, owner);
                break;
            case DirectionalTransition dir:
                var dirInner = isEnter ? dir.EnterTransition : dir.ExitTransition;
                if (dirInner is not null)
                    ApplyTransitionAnimations(uie, visual, compositor, dirInner, curve, isEnter, delay, owner);
                break;
        }
    }

    private static void ApplyFadeTransition(UIElement uie, Compositor compositor, Curve curve, bool isEnter, TimeSpan delay, Reconciler? owner = null)
    {
        // The fade owns the opacity channel now, so a pending opacity restart must not replace it (#1669).
        CancelOpacityRestart(uie);
        var visual = AnimationHelper.GetElementVisual(uie);
        if (isEnter)
            StartEnterFade(uie, visual, compositor, curve, delay, owner);
        else
        {
            var anim = AnimationHelper.CreateScalarTargetAnimation(compositor, 0f, curve);
            if (delay > TimeSpan.Zero) AnimationHelper.SetDelay(anim, delay);
            visual.StartAnimation("Opacity", anim);
        }
    }

    /// <summary>
    /// Fades <paramref name="uie"/> in from 0 to its own opacity, restarted once after XAML's first
    /// opacity push stops it (#1680). Shared by <c>.Transition(Fade)</c> and the ambient enter.
    /// </summary>
    internal static void StartEnterFade(UIElement uie, Visual visual, Compositor compositor, Curve curve, TimeSpan delay, Reconciler? owner)
    {
        visual.Opacity = 0;
        visual.StartAnimation("Opacity", EnterFade());
        // XAML's first push of a changed opacity (a modifier's, a style's) stops the fade (#1680).
        // A spring has no controller to tell a stopped fade from a running one, so it counts as
        // stopped once the visual's base value moved off the 0 written above: XAML's push writes it,
        // and so does the spring's own end, which only comes before the element ever showed.
        var controller = visual.TryGetAnimationController("Opacity");
        RestartOpacityAfterXamlPush(uie, visual, controller, owner, owns: null, restart: () =>
        {
            WithoutImplicitAnimations(visual, () => visual.Opacity = 0);
            visual.StartAnimation("Opacity", EnterFade());
            return visual.TryGetAnimationController("Opacity");
        }, conceal: true, stopped: controller is not null ? null : () => global::System.Math.Abs(visual.Opacity) > 1e-6f);

        // The fade rests at the element's own opacity, not 1: XAML pushes only a changed value, so
        // it wouldn't correct a fade that ended above it (#1680). Read when the fade (re)starts,
        // since a style's opacity may arrive between mount and the first frame.
        CompositionAnimation EnterFade()
        {
            var anim = AnimationHelper.CreateScalarTargetAnimation(compositor, RestOpacityOf(uie), curve);
            if (delay > TimeSpan.Zero) AnimationHelper.SetDelay(anim, delay);
            return anim;
        }
    }

    private static void ApplySlideTransition(UIElement uie, Visual visual, Compositor compositor, Edge edge, Curve curve, bool isEnter, TimeSpan delay)
    {
        var slideDistance = 40f;
        var offset = edge switch
        {
            Edge.Left => new Vector3(-slideDistance, 0, 0),
            Edge.Top => new Vector3(0, -slideDistance, 0),
            Edge.Right => new Vector3(slideDistance, 0, 0),
            Edge.Bottom => new Vector3(0, slideDistance, 0),
            _ => new Vector3(0, slideDistance, 0),
        };

        if (isEnter)
        {
            visual.Offset = offset;
            var anim = AnimationHelper.CreateVector3TargetAnimation(compositor, Vector3.Zero, curve);
            if (delay > TimeSpan.Zero) AnimationHelper.SetDelay(anim, delay);
            visual.StartAnimation("Offset", anim);
        }
        else
        {
            var anim = AnimationHelper.CreateVector3TargetAnimation(compositor, offset, curve);
            if (delay > TimeSpan.Zero) AnimationHelper.SetDelay(anim, delay);
            visual.StartAnimation("Offset", anim);
        }
    }

    private static void ApplyScaleTransition(UIElement uie, Visual visual, Compositor compositor, float from, Curve curve, bool isEnter, TimeSpan delay)
    {
        // Scale relative to the element's own .Scale(). The visual's Scale getter returns the
        // last assigned value, which after an enter is its start value, so it is not the rest.
        var rest = RestTransformOf(uie).Scale;
        var scaled = new Vector3(rest.X * from, rest.Y * from, rest.Z);
        if (isEnter)
        {
            visual.Scale = scaled;
            var anim = AnimationHelper.CreateVector3TargetAnimation(compositor, rest, curve);
            if (delay > TimeSpan.Zero) AnimationHelper.SetDelay(anim, delay);
            visual.StartAnimation("Scale", anim);
        }
        else
        {
            var anim = AnimationHelper.CreateVector3TargetAnimation(compositor, scaled, curve);
            if (delay > TimeSpan.Zero) AnimationHelper.SetDelay(anim, delay);
            visual.StartAnimation("Scale", anim);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Stagger animation (DelayTime on child animations)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies stagger delays to an element's Visual's implicit animations.
    /// Called after layout/property animations are set up on children.
    /// </summary>
    /// <summary>
    /// Removes the delays <see cref="ApplyStaggerDelays"/> set on a panel's children. Update runs
    /// it before the children update when the parent drops <c>.Stagger()</c>, so their writes in
    /// that update start at once. Only the channels stagger delays are reset: a child's own
    /// delayed animation on another key keeps its delay.
    /// </summary>
    internal static void ClearStaggerDelays(UIElement parent)
    {
        if (parent is not WinUI.Panel panel) return;
        foreach (var child in panel.Children)
        {
            if (child.GetValue(ReactorAttached.StateProperty) is ReactorState state) state.StaggerDelay = null;
            if (!ElementPool.IsVisualTaken(child)) continue;
            SetStaggerDelayOnKeys(ElementCompositionPreview.GetElementVisual(child), TimeSpan.Zero);
        }
    }

    /// <summary>The implicit animation keys <c>.Stagger()</c> delays.</summary>
    internal static readonly string[] StaggerChannels = ["Offset", "Opacity", "Scale", "RotationAngle", "Size", "CenterPoint"];

    /// <summary>The stagger delay recorded for <paramref name="child"/>, or null when no parent staggers it.</summary>
    internal static TimeSpan? StaggerDelayOf(UIElement child)
        => (child.GetValue(ReactorAttached.StateProperty) as ReactorState)?.StaggerDelay;

    private static void SetStaggerDelayOnKeys(Visual visual, TimeSpan delay)
    {
        if (visual.ImplicitAnimations is not { } keys) return;
        foreach (var key in StaggerChannels)
            SetDelayIfPresent(keys, key, delay);
    }

    private static void SetDelayIfPresent(ImplicitAnimationCollection keys, string key, TimeSpan delay)
    {
        if (keys.TryGetValue(key, out var animation) && animation is CompositionAnimation composition)
            AnimationHelper.SetDelay(composition, delay);
    }

    /// <summary>A panel with <c>.Stagger()</c> whose update is reconciling its children.</summary>
    private sealed record UpdateStaggerScope(UIElement Panel, TimeSpan Delay);

    private UpdateStaggerScope? _updateStagger;

    // The new index of the panel child being updated, set by child reconciliation; -1 elsewhere.
    private int _updatingChildIndex = -1;

    /// <summary>
    /// Gives a child its stagger delay before it writes this update's values: the delay its
    /// parent's current <c>.Stagger()</c> sets at its new position. Without it a write in this
    /// update would start with the previous render's delay (a changed stagger or a reordered
    /// child), and a newly published key with none. Every layer of the child sees the same
    /// position, so wrapped children and repeated sibling records get their own delay.
    /// </summary>
    private void RefreshStaggerDelay(UIElement control)
    {
        if (_updateStagger is not { } stagger || _updatingChildIndex < 0) return;
        var parent = (control as FrameworkElement)?.Parent ?? VisualTreeHelper.GetParent(control);
        if (!ReferenceEquals(parent, stagger.Panel)) return;
        var delay = TimeSpan.FromTicks(stagger.Delay.Ticks * _updatingChildIndex);
        GetOrCreateReactorState(control).StaggerDelay = delay;
        if (ElementPool.IsVisualTaken(control))
            SetStaggerDelayOnKeys(ElementCompositionPreview.GetElementVisual(control), delay);
    }

    internal static void ApplyStaggerDelays(UIElement parent, StaggerConfig config)
    {
        if (parent is not WinUI.Panel panel) return;

        for (int i = 0; i < panel.Children.Count; i++)
        {
            var child = panel.Children[i];
            var visual = AnimationHelper.GetElementVisual(child);
            var delay = TimeSpan.FromTicks(config.Delay.Ticks * i);
            GetOrCreateReactorState(child).StaggerDelay = delay;
            SetStaggerDelayOnKeys(visual, delay);
        }
    }
}
