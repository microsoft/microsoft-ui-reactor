using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Microsoft.UI.Reactor.Navigation;

/// <summary>
/// Runs GPU-accelerated Composition-layer transitions between navigation pages.
/// All animations run on the compositor thread — zero managed-code involvement during playback.
/// </summary>
internal static class TransitionEngine
{
    /// <summary>
    /// Runs a transition animation between two page visuals.
    /// </summary>
    /// <param name="outgoing">The page being navigated away from.</param>
    /// <param name="incoming">The page being navigated to (mounted at Opacity 0).</param>
    /// <param name="transition">The transition type to apply.</param>
    /// <param name="mode">The navigation mode (used for automatic reverse on GoBack).</param>
    /// <param name="onComplete">Callback invoked when the transition finishes.</param>
    public static void RunTransition(
        UIElement outgoing, UIElement incoming,
        NavigationTransition transition, NavigationMode mode,
        Action onComplete)
    {
        if (transition is SuppressTransition)
        {
            // Instant swap — no animation
            var inVis = ElementCompositionPreview.GetElementVisual(incoming);
            inVis.Opacity = 1;
            inVis.Offset = Vector3.Zero;
            inVis.Scale = Vector3.One;
            onComplete();
            return;
        }

        var outVisual = ElementCompositionPreview.GetElementVisual(outgoing);
        var inVisual = ElementCompositionPreview.GetElementVisual(incoming);
        var compositor = outVisual.Compositor;
        var usesCenterPointBinding = transition is DrillInTransition;
        var suppressesHitTesting = transition is SlideTransition;

        // Unknown transition type — NavigationTransition is a public abstract record, so a
        // third party can subclass it. Handle it before the scoped batch exists: there is
        // nothing to animate, and an early return past a subscribed batch would leak it.
        if (transition is not (EntranceTransition or SlideTransition or FadeTransition
            or DrillInTransition or SpringSlideTransition or ConnectedTransition))
        {
            inVisual.Opacity = 1;
            inVisual.Offset = Vector3.Zero;
            inVisual.Scale = Vector3.One;
            onComplete();
            return;
        }

        // Reset stale compositor properties from previous animations
        inVisual.Offset = Vector3.Zero;
        inVisual.Scale = Vector3.One;

        if (suppressesHitTesting)
        {
            SuppressHitTesting(outgoing);
            SuppressHitTesting(incoming);
        }

        if (usesCenterPointBinding)
        {
            // Expression animations are persistent, so start these before the scoped
            // batch to keep them from blocking the transition's completion callback.
            BindCenterPointToVisualSize(compositor, outVisual);
            BindCenterPointToVisualSize(compositor, inVisual);
        }

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        // Subscribe before End(). A scoped batch can complete synchronously inside End()
        // — most obviously when it captured no animations — and a handler attached
        // afterwards would never run, stranding the navigation: the outgoing page would
        // never be cached or unmounted, onNavigatedTo/From would never fire, and a
        // hit-test-suppressed page would stay unclickable.
        batch.Completed += (_, _) =>
        {
            ReleaseAnimatedProperties(inVisual);

            // Finalize: ensure incoming is fully visible
            inVisual.Opacity = 1;
            inVisual.Offset = Vector3.Zero;
            inVisual.Scale = Vector3.One;
            if (usesCenterPointBinding)
            {
                outVisual.StopAnimation("CenterPoint");
                inVisual.StopAnimation("CenterPoint");
            }
            if (suppressesHitTesting)
            {
                RestoreHitTesting(outgoing);
                RestoreHitTesting(incoming);
            }

            try
            {
                // onComplete runs app lifecycle callbacks (onNavigatedTo / onNavigatedFrom) and
                // can throw. The finally block still has to run: it takes the outgoing page out
                // of the animation's end state, and disposes the batch.
                onComplete();
            }
            finally
            {
                // Normalize the outgoing visual too, but only after onComplete has taken it out
                // of the tree — cached or unmounted. Its animation left it faded out and possibly
                // offset or scaled, and NavigationCacheMode can hand that same control back as a
                // later page: NavigationHostLifecycle's instant-swap path (SuppressTransition, or
                // a navigation with no outgoing page) adds a cached control to the tree without
                // touching its visual, so a page cached mid-fade would return invisible. Doing
                // this before onComplete would instead flash the old page at full opacity over
                // the new one, since both are still children of the host Grid.
                ReleaseAnimatedProperties(outVisual);
                outVisual.Opacity = 1;
                outVisual.Offset = Vector3.Zero;
                outVisual.Scale = Vector3.One;

                batch.Dispose();
            }
        };

        switch (transition)
        {
            case EntranceTransition:
                RunEntrance(compositor, outVisual, inVisual, mode);
                break;
            case SlideTransition slide:
                RunSlide(compositor, outVisual, inVisual, slide, mode);
                break;
            case FadeTransition fade:
                RunFade(compositor, outVisual, inVisual, fade);
                break;
            case DrillInTransition drill:
                RunDrillIn(compositor, outVisual, inVisual, drill, mode);
                break;
            case SpringSlideTransition spring:
                RunSpringSlide(compositor, outVisual, inVisual, spring, mode);
                break;
            case ConnectedTransition:
                // Stub — shared-element animation isn't implemented yet, so play the platform
                // default instead. Entrance is the right fallback: it's what the navigation would
                // have got had no transition been requested, and unlike a slide it doesn't invent
                // a direction or opt into the slide path's hit-test suppression.
                global::System.Diagnostics.Debug.WriteLine("[Reactor] ConnectedTransition not yet implemented; falling back to EntranceTransition.");
                RunEntrance(compositor, outVisual, inVisual, mode);
                break;
        }

        batch.End();
    }

    /// <summary>
    /// Stops the animations this engine starts, so the following direct assignments are
    /// unambiguous.
    /// </summary>
    /// <remarks>
    /// This is defensive rather than load-bearing for the keyframe animations used here: a
    /// completed <c>KeyFrameAnimation</c> does release its property, and a direct write that
    /// disagrees with the final keyframe takes effect — <c>NavCov_CompletedAnimationReleasesProperty</c>
    /// pins that behaviour, because it is the assumption every reset in this file rests on and it
    /// is not obvious from the API. The stops are kept for the animation kinds that have no
    /// keyframe to settle on (the spring path), and to keep the reset's intent explicit.
    /// </remarks>
    private static void ReleaseAnimatedProperties(Visual visual)
    {
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Offset");
        visual.StopAnimation("Scale");
    }

    // ════════════════════════════════════════════════════════════════
    //  Hit-test suppression
    //
    //  Slide transitions make both pages non-hit-testable while they animate. Navigations
    //  can overlap (double-clicking a NavigationView item is enough), so this is nest-aware:
    //  the value from *before* any suppression is recorded once and restored only when the
    //  last overlapping transition finishes. Snapshotting `IsHitTestVisible` per transition
    //  instead would let the second transition capture the first one's `false` and restore
    //  that — and a page cached by NavigationCacheMode would come back permanently
    //  unclickable.
    // ════════════════════════════════════════════════════════════════

    private sealed class HitTestSuppression
    {
        public bool OriginalValue;
        public int Depth;
    }

    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, HitTestSuppression>
        _hitTestSuppressions = new();

    internal static void SuppressHitTesting(UIElement element)
    {
        if (_hitTestSuppressions.TryGetValue(element, out var existing))
        {
            existing.Depth++;
        }
        else
        {
            _hitTestSuppressions.Add(element, new HitTestSuppression
            {
                OriginalValue = element.IsHitTestVisible,
                Depth = 1,
            });
        }

        element.IsHitTestVisible = false;
    }

    internal static void RestoreHitTesting(UIElement element)
    {
        if (!_hitTestSuppressions.TryGetValue(element, out var state)) return;

        if (--state.Depth > 0) return;

        _hitTestSuppressions.Remove(element);
        element.IsHitTestVisible = state.OriginalValue;
    }

    // ════════════════════════════════════════════════════════════════
    //  Slide transition
    // ════════════════════════════════════════════════════════════════

    private static void RunSlide(
        Compositor compositor, Visual outVisual, Visual inVisual,
        SlideTransition slide, NavigationMode mode)
    {
        if (UsesWinUISlideSpecification(slide))
        {
            RunWinUISlide(compositor, outVisual, inVisual, slide.Direction, mode);
            return;
        }

        RunCustomSlide(compositor, outVisual, inVisual, slide, mode);
    }

    internal static bool UsesWinUISlideSpecification(SlideTransition slide) =>
        slide.Direction != SlideDirection.FromTop
        && slide.Duration is null
        && slide.Distance is null
        && slide.Easing is null;

    private static void RunCustomSlide(
        Compositor compositor, Visual outVisual, Visual inVisual,
        SlideTransition slide, NavigationMode mode)
    {
        var duration = slide.Duration ?? TimeSpan.FromMilliseconds(250);
        var distance = slide.Distance ?? 200f;
        var direction = slide.Direction;

        // Reverse direction for GoBack/Forward-reverse
        if (mode == NavigationMode.Pop)
            direction = ReverseDirection(direction);

        var (outEnd, inStart) = GetSlideOffsets(direction, distance);
        var easing = slide.Easing ?? compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1.0f));

        // Outgoing: slide out + fade out
        var outOffset = compositor.CreateVector3KeyFrameAnimation();
        outOffset.InsertKeyFrame(0f, Vector3.Zero);
        outOffset.InsertKeyFrame(1f, outEnd, easing);
        outOffset.Duration = duration;

        var outFade = compositor.CreateScalarKeyFrameAnimation();
        outFade.InsertKeyFrame(0f, 1f);
        outFade.InsertKeyFrame(1f, 0f, easing);
        outFade.Duration = duration;

        outVisual.StartAnimation("Offset", outOffset);
        outVisual.StartAnimation("Opacity", outFade);

        // Incoming: slide in + fade in
        inVisual.Offset = inStart;

        var inOffset = compositor.CreateVector3KeyFrameAnimation();
        inOffset.InsertKeyFrame(0f, inStart);
        inOffset.InsertKeyFrame(1f, Vector3.Zero, easing);
        inOffset.Duration = duration;

        var inFade = compositor.CreateScalarKeyFrameAnimation();
        inFade.InsertKeyFrame(0f, 0f);
        inFade.InsertKeyFrame(1f, 1f, easing);
        inFade.Duration = duration;

        inVisual.StartAnimation("Offset", inOffset);
        inVisual.StartAnimation("Opacity", inFade);
    }

    internal const float HorizontalSlideExitOffset = 150f;
    internal const float HorizontalSlideEntranceOffset = 200f;
    internal static readonly TimeSpan HorizontalSlideExitDuration = TimeSpan.FromMilliseconds(150);
    internal static readonly TimeSpan HorizontalSlideEntranceDuration = TimeSpan.FromMilliseconds(300);
    internal static readonly Vector2 SlideInEasingControlPoint1 = new(0.1f, 0.9f);
    internal static readonly Vector2 SlideInEasingControlPoint2 = new(0.2f, 1.0f);
    internal static readonly Vector2 SlideOutEasingControlPoint1 = new(0.7f, 0.0f);
    internal static readonly Vector2 SlideOutEasingControlPoint2 = new(1.0f, 0.5f);

    internal const float VerticalSlideOffset = 200f;
    internal const float VerticalSlideExponent = 6f;
    internal static readonly TimeSpan VerticalSlideHandoffTime = TimeSpan.FromMilliseconds(250);
    internal static readonly TimeSpan VerticalSlideDuration = TimeSpan.FromMilliseconds(600);

    internal readonly record struct HorizontalSlidePlan(Vector3 OutEnd, Vector3 InStart);

    internal static HorizontalSlidePlan GetHorizontalSlidePlan(
        SlideDirection direction, NavigationMode mode)
    {
        var directionFactor = direction == SlideDirection.FromLeft ? 1f : -1f;

        return mode == NavigationMode.Pop
            ? new(
                new Vector3(-HorizontalSlideEntranceOffset * directionFactor, 0, 0),
                new Vector3(HorizontalSlideExitOffset * directionFactor, 0, 0))
            : new(
                new Vector3(HorizontalSlideExitOffset * directionFactor, 0, 0),
                new Vector3(-HorizontalSlideEntranceOffset * directionFactor, 0, 0));
    }

    private static void RunWinUISlide(
        Compositor compositor, Visual outVisual, Visual inVisual,
        SlideDirection direction, NavigationMode mode)
    {
        if (direction is SlideDirection.FromLeft or SlideDirection.FromRight)
        {
            RunWinUIHorizontalSlide(compositor, outVisual, inVisual, direction, mode);
        }
        else
        {
            RunWinUIVerticalSlide(compositor, outVisual, inVisual, mode);
        }
    }

    private static void RunWinUIHorizontalSlide(
        Compositor compositor, Visual outVisual, Visual inVisual,
        SlideDirection direction, NavigationMode mode)
    {
        var plan = GetHorizontalSlidePlan(direction, mode);
        var inEasing = compositor.CreateCubicBezierEasingFunction(
            SlideInEasingControlPoint1, SlideInEasingControlPoint2);
        var outEasing = compositor.CreateCubicBezierEasingFunction(
            SlideOutEasingControlPoint1, SlideOutEasingControlPoint2);

        var outOffset = compositor.CreateVector3KeyFrameAnimation();
        outOffset.InsertKeyFrame(1f, plan.OutEnd, outEasing);
        outOffset.Duration = HorizontalSlideExitDuration;
        outVisual.StartAnimation("Offset", outOffset);

        inVisual.Offset = plan.InStart;
        var inOffset = compositor.CreateVector3KeyFrameAnimation();
        inOffset.InsertKeyFrame(1f, Vector3.Zero, inEasing);
        inOffset.Duration = HorizontalSlideEntranceDuration;
        inOffset.DelayTime = HorizontalSlideExitDuration;
        inVisual.StartAnimation("Offset", inOffset);

        StartDelayedOpacitySnap(compositor, outVisual, 0f, HorizontalSlideExitDuration);
        StartDelayedOpacitySnap(compositor, inVisual, 1f, HorizontalSlideExitDuration);
    }

    private static void RunWinUIVerticalSlide(
        Compositor compositor, Visual outVisual, Visual inVisual,
        NavigationMode mode)
    {
        var offset = new Vector3(0, VerticalSlideOffset, 0);
        var easingMode = mode == NavigationMode.Pop
            ? CompositionEasingFunctionMode.In
            : CompositionEasingFunctionMode.Out;
        var easing = CompositionEasingFunction.CreateExponentialEasingFunction(
            compositor, easingMode, VerticalSlideExponent);

        if (mode == NavigationMode.Pop)
        {
            var outOffset = compositor.CreateVector3KeyFrameAnimation();
            outOffset.InsertKeyFrame(1f, offset, easing);
            outOffset.Duration = VerticalSlideDuration;
            outVisual.StartAnimation("Offset", outOffset);
        }
        else
        {
            inVisual.Offset = offset;
            var inOffset = compositor.CreateVector3KeyFrameAnimation();
            inOffset.InsertKeyFrame(
                (float)(VerticalSlideHandoffTime.TotalMilliseconds / VerticalSlideDuration.TotalMilliseconds),
                offset);
            inOffset.InsertKeyFrame(1f, Vector3.Zero, easing);
            inOffset.Duration = VerticalSlideDuration;
            inVisual.StartAnimation("Offset", inOffset);
        }

        StartDelayedOpacitySnap(compositor, outVisual, 0f, VerticalSlideHandoffTime);
        StartDelayedOpacitySnap(compositor, inVisual, 1f, VerticalSlideHandoffTime);
    }

    internal static SlideDirection ReverseDirection(SlideDirection direction) => direction switch
    {
        SlideDirection.FromRight => SlideDirection.FromLeft,
        SlideDirection.FromLeft => SlideDirection.FromRight,
        SlideDirection.FromBottom => SlideDirection.FromTop,
        SlideDirection.FromTop => SlideDirection.FromBottom,
        _ => direction,
    };

    internal static (Vector3 OutEnd, Vector3 InStart) GetSlideOffsets(SlideDirection direction, float distance = 200f)
    {
        return direction switch
        {
            SlideDirection.FromRight => (new Vector3(-distance, 0, 0), new Vector3(distance, 0, 0)),
            SlideDirection.FromLeft => (new Vector3(distance, 0, 0), new Vector3(-distance, 0, 0)),
            SlideDirection.FromBottom => (new Vector3(0, -distance, 0), new Vector3(0, distance, 0)),
            SlideDirection.FromTop => (new Vector3(0, distance, 0), new Vector3(0, -distance, 0)),
            _ => (Vector3.Zero, Vector3.Zero),
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  WinUI entrance transition
    // ════════════════════════════════════════════════════════════════

    internal const float EntranceTranslationOffset = 140f;
    internal static readonly TimeSpan EntranceExitDuration = TimeSpan.FromMilliseconds(150);
    internal static readonly TimeSpan EntranceDuration = TimeSpan.FromMilliseconds(300);
    internal static readonly TimeSpan EntranceOpacitySnapDuration = TimeSpan.FromMilliseconds(1);
    internal static readonly Vector2 EntranceInEasingControlPoint1 = new(0.1f, 0.9f);
    internal static readonly Vector2 EntranceInEasingControlPoint2 = new(0.2f, 1.0f);
    internal static readonly Vector2 EntranceOutEasingControlPoint1 = new(0.7f, 0.0f);
    internal static readonly Vector2 EntranceOutEasingControlPoint2 = new(1.0f, 0.5f);

    private static void RunEntrance(
        Compositor compositor, Visual outVisual, Visual inVisual,
        NavigationMode mode)
    {
        var inEasing = compositor.CreateCubicBezierEasingFunction(
            EntranceInEasingControlPoint1, EntranceInEasingControlPoint2);
        var outEasing = compositor.CreateCubicBezierEasingFunction(
            EntranceOutEasingControlPoint1, EntranceOutEasingControlPoint2);

        if (mode == NavigationMode.Pop)
        {
            var outOffset = compositor.CreateVector3KeyFrameAnimation();
            outOffset.InsertKeyFrame(1f, new Vector3(0, EntranceTranslationOffset, 0), outEasing);
            outOffset.Duration = EntranceExitDuration;
            outVisual.StartAnimation("Offset", outOffset);

            StartDelayedOpacitySnap(compositor, outVisual, 0f, EntranceExitDuration);

            var inFade = compositor.CreateScalarKeyFrameAnimation();
            inFade.InsertKeyFrame(1f, 1f, inEasing);
            inFade.Duration = EntranceDuration;
            inFade.DelayTime = EntranceExitDuration;
            inVisual.StartAnimation("Opacity", inFade);
        }
        else
        {
            var outFade = compositor.CreateScalarKeyFrameAnimation();
            outFade.InsertKeyFrame(1f, 0f, outEasing);
            outFade.Duration = EntranceExitDuration;
            outVisual.StartAnimation("Opacity", outFade);

            inVisual.Offset = new Vector3(0, EntranceTranslationOffset, 0);
            var inOffset = compositor.CreateVector3KeyFrameAnimation();
            inOffset.InsertKeyFrame(1f, Vector3.Zero, inEasing);
            inOffset.Duration = EntranceDuration;
            inOffset.DelayTime = EntranceExitDuration;
            inVisual.StartAnimation("Offset", inOffset);

            StartDelayedOpacitySnap(compositor, inVisual, 1f, EntranceExitDuration);
        }
    }

    private static void StartDelayedOpacitySnap(
        Compositor compositor, Visual visual, float opacity, TimeSpan delay)
    {
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1f, opacity);
        animation.Duration = EntranceOpacitySnapDuration;
        animation.DelayTime = delay;
        visual.StartAnimation("Opacity", animation);
    }

    // ════════════════════════════════════════════════════════════════
    //  Fade transition
    // ════════════════════════════════════════════════════════════════

    private static void RunFade(
        Compositor compositor, Visual outVisual, Visual inVisual,
        FadeTransition fade)
    {
        var duration = fade.Duration ?? TimeSpan.FromMilliseconds(200);

        // Outgoing: fade out
        var outFade = compositor.CreateScalarKeyFrameAnimation();
        outFade.InsertKeyFrame(0f, 1f);
        outFade.InsertKeyFrame(1f, 0f);
        outFade.Duration = duration;
        outVisual.StartAnimation("Opacity", outFade);

        // Incoming: fade in
        var inFade = compositor.CreateScalarKeyFrameAnimation();
        inFade.InsertKeyFrame(0f, 0f);
        inFade.InsertKeyFrame(1f, 1f);
        inFade.Duration = duration;
        inVisual.StartAnimation("Opacity", inFade);
    }

    // ════════════════════════════════════════════════════════════════
    //  DrillIn transition
    // ════════════════════════════════════════════════════════════════

    internal const float DrillInForwardOutScale = 1.04f;
    internal const float DrillInForwardInScale = 0.94f;
    internal const float DrillInBackOutScale = 0.96f;
    internal const float DrillInBackInScale = 1.06f;
    internal static readonly TimeSpan DrillInOutScaleDuration = TimeSpan.FromMilliseconds(100);
    internal static readonly TimeSpan DrillInOutOpacityDuration = TimeSpan.FromMilliseconds(100);
    internal static readonly TimeSpan DrillInForwardInScaleDuration = TimeSpan.FromMilliseconds(783);
    internal static readonly TimeSpan DrillInForwardInOpacityDuration = TimeSpan.FromMilliseconds(333);
    internal static readonly TimeSpan DrillInBackInScaleDuration = TimeSpan.FromMilliseconds(333);
    internal static readonly TimeSpan DrillInBackInOpacityDuration = TimeSpan.FromMilliseconds(333);
    internal static readonly Vector2 DrillInScaleEasingControlPoint1 = new(0.1f, 0.9f);
    internal static readonly Vector2 DrillInScaleEasingControlPoint2 = new(0.2f, 1.0f);
    internal static readonly Vector2 DrillInBackScaleEasingControlPoint1 = new(0.12f, 0.0f);
    internal static readonly Vector2 DrillInBackScaleEasingControlPoint2 = new(0.0f, 1.0f);
    internal static readonly Vector2 DrillInOpacityEasingControlPoint1 = new(0.17f, 0.17f);
    internal static readonly Vector2 DrillInOpacityEasingControlPoint2 = new(0.0f, 1.0f);

    internal readonly record struct DrillInPlan(
        float OutEndScale,
        float InStartScale,
        TimeSpan OutScaleDuration,
        TimeSpan OutOpacityDuration,
        TimeSpan InScaleDuration,
        TimeSpan InOpacityDuration,
        Vector2 InScaleEasingControlPoint1,
        Vector2 InScaleEasingControlPoint2);

    internal static DrillInPlan GetDrillInPlan(NavigationMode mode) =>
        mode == NavigationMode.Pop
            ? new(
                DrillInBackOutScale,
                DrillInBackInScale,
                DrillInOutScaleDuration,
                DrillInOutOpacityDuration,
                DrillInBackInScaleDuration,
                DrillInBackInOpacityDuration,
                DrillInBackScaleEasingControlPoint1,
                DrillInBackScaleEasingControlPoint2)
            : new(
                DrillInForwardOutScale,
                DrillInForwardInScale,
                DrillInOutScaleDuration,
                DrillInOutOpacityDuration,
                DrillInForwardInScaleDuration,
                DrillInForwardInOpacityDuration,
                DrillInScaleEasingControlPoint1,
                DrillInScaleEasingControlPoint2);

    private static void RunDrillIn(
        Compositor compositor, Visual outVisual, Visual inVisual,
        DrillInTransition drill, NavigationMode mode)
    {
        if (!UsesWinUIDrillInSpecification(drill))
        {
            RunCustomDrillIn(compositor, outVisual, inVisual, drill.Duration!.Value, mode);
            return;
        }

        var plan = GetDrillInPlan(mode);
        var outScaleEasing = compositor.CreateCubicBezierEasingFunction(
            DrillInScaleEasingControlPoint1, DrillInScaleEasingControlPoint2);
        var inScaleEasing = compositor.CreateCubicBezierEasingFunction(
            plan.InScaleEasingControlPoint1, plan.InScaleEasingControlPoint2);
        var opacityEasing = compositor.CreateCubicBezierEasingFunction(
            DrillInOpacityEasingControlPoint1, DrillInOpacityEasingControlPoint2);

        var outScale = compositor.CreateVector3KeyFrameAnimation();
        outScale.InsertKeyFrame(1f, new Vector3(plan.OutEndScale, plan.OutEndScale, 1f), outScaleEasing);
        outScale.Duration = plan.OutScaleDuration;
        outVisual.StartAnimation("Scale", outScale);

        var outFade = compositor.CreateScalarKeyFrameAnimation();
        outFade.InsertKeyFrame(1f, 0f, opacityEasing);
        outFade.Duration = plan.OutOpacityDuration;
        outVisual.StartAnimation("Opacity", outFade);

        inVisual.Scale = new Vector3(plan.InStartScale, plan.InStartScale, 1f);

        var inScale = compositor.CreateVector3KeyFrameAnimation();
        inScale.InsertKeyFrame(1f, Vector3.One, inScaleEasing);
        inScale.Duration = plan.InScaleDuration;
        inVisual.StartAnimation("Scale", inScale);

        var inFade = compositor.CreateScalarKeyFrameAnimation();
        inFade.InsertKeyFrame(1f, 1f, opacityEasing);
        inFade.Duration = plan.InOpacityDuration;
        inVisual.StartAnimation("Opacity", inFade);
    }

    internal static bool UsesWinUIDrillInSpecification(DrillInTransition drill) =>
        drill.Duration is null;

    private static void BindCenterPointToVisualSize(Compositor compositor, Visual visual)
    {
        var centerPoint = compositor.CreateExpressionAnimation(
            "Vector3(target.Size.X / 2, target.Size.Y / 2, 0)");
        centerPoint.SetReferenceParameter("target", visual);
        visual.StartAnimation("CenterPoint", centerPoint);
    }

    private static void RunCustomDrillIn(
        Compositor compositor, Visual outVisual, Visual inVisual,
        TimeSpan duration, NavigationMode mode)
    {
        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1.0f));

        if (mode == NavigationMode.Pop)
        {
            // Reverse: outgoing scales down + fades out, incoming fades in
            var outScale = compositor.CreateVector3KeyFrameAnimation();
            outScale.InsertKeyFrame(0f, Vector3.One);
            outScale.InsertKeyFrame(1f, new Vector3(0.85f, 0.85f, 1f), easing);
            outScale.Duration = duration;

            var outFade = compositor.CreateScalarKeyFrameAnimation();
            outFade.InsertKeyFrame(0f, 1f);
            outFade.InsertKeyFrame(1f, 0f, easing);
            outFade.Duration = duration;

            outVisual.StartAnimation("Scale", outScale);
            outVisual.StartAnimation("Opacity", outFade);

            var inFade = compositor.CreateScalarKeyFrameAnimation();
            inFade.InsertKeyFrame(0f, 0f);
            inFade.InsertKeyFrame(1f, 1f, easing);
            inFade.Duration = duration;
            inVisual.StartAnimation("Opacity", inFade);
        }
        else
        {
            // Forward: incoming scales up from 0.85 + fades in, outgoing fades out
            inVisual.Scale = new Vector3(0.85f, 0.85f, 1f);

            var inScale = compositor.CreateVector3KeyFrameAnimation();
            inScale.InsertKeyFrame(0f, new Vector3(0.85f, 0.85f, 1f));
            inScale.InsertKeyFrame(1f, Vector3.One, easing);
            inScale.Duration = duration;

            var inFade = compositor.CreateScalarKeyFrameAnimation();
            inFade.InsertKeyFrame(0f, 0f);
            inFade.InsertKeyFrame(1f, 1f, easing);
            inFade.Duration = duration;

            inVisual.StartAnimation("Scale", inScale);
            inVisual.StartAnimation("Opacity", inFade);

            var outFade = compositor.CreateScalarKeyFrameAnimation();
            outFade.InsertKeyFrame(0f, 1f);
            outFade.InsertKeyFrame(1f, 0f, easing);
            outFade.Duration = duration;
            outVisual.StartAnimation("Opacity", outFade);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Spring slide transition
    // ════════════════════════════════════════════════════════════════

    private static void RunSpringSlide(
        Compositor compositor, Visual outVisual, Visual inVisual,
        SpringSlideTransition spring, NavigationMode mode)
    {
        var direction = spring.Direction;
        if (mode == NavigationMode.Pop)
            direction = ReverseDirection(direction);

        var (outEnd, inStart) = GetSlideOffsets(direction);

        // Outgoing: spring offset + fade
        var outSpring = compositor.CreateSpringVector3Animation();
        outSpring.DampingRatio = spring.DampingRatio;
        outSpring.Period = TimeSpan.FromSeconds(spring.Period);
        outSpring.FinalValue = outEnd;
        outVisual.StartAnimation("Offset", outSpring);

        var outFade = compositor.CreateScalarKeyFrameAnimation();
        outFade.InsertKeyFrame(0f, 1f);
        outFade.InsertKeyFrame(1f, 0f);
        outFade.Duration = TimeSpan.FromMilliseconds(200);
        outVisual.StartAnimation("Opacity", outFade);

        // Incoming: spring offset + fade
        inVisual.Offset = inStart;

        var inSpring = compositor.CreateSpringVector3Animation();
        inSpring.DampingRatio = spring.DampingRatio;
        inSpring.Period = TimeSpan.FromSeconds(spring.Period);
        inSpring.FinalValue = Vector3.Zero;
        inVisual.StartAnimation("Offset", inSpring);

        var inFade = compositor.CreateScalarKeyFrameAnimation();
        inFade.InsertKeyFrame(0f, 0f);
        inFade.InsertKeyFrame(1f, 1f);
        inFade.Duration = TimeSpan.FromMilliseconds(200);
        inVisual.StartAnimation("Opacity", inFade);
    }
}
