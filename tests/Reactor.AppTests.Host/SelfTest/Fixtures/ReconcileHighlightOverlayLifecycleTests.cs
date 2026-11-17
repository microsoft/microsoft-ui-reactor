using System;
using Microsoft.UI.Composition;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Lifecycle / dedup / no-animation regression coverage for
/// <see cref="ReconcileHighlightOverlay"/>. These pin the architectural
/// decisions made under issue #167
/// (https://github.com/microsoft/microsoft-ui-reactor/issues/167) —
/// see that issue for the full investigation history. The headline
/// invariants are:
///   - One sprite per distinct target while live (no stacking on rapid Shows)
///   - Refresh path resets the expiry timer instead of creating a duplicate
///   - No Composition animations are involved in the sprite lifecycle
///     (the original dimming bug came from a CompositionScopedBatch capturing
///     reconciler-driven animations on the actual content Visuals)
///   - All sprites get cleaned up on Dispose with no exceptions
/// </summary>
internal static class ReconcileHighlightOverlayLifecycleTests
{
    /// <summary>
    /// Builds a Canvas + Border targets, attaches an overlay sub-container,
    /// and returns a ready-to-use overlay. Caller disposes via using-block.
    /// </summary>
    private static async Task<(Canvas canvas, Border[] targets, ContainerVisual root, ReconcileHighlightOverlay overlay)>
        SetupAsync(Harness h, int targetCount = 1, int? holdMs = null,
            Func<TimeSpan, Action, Action>? scheduleExpiry = null)
    {
        var canvas = new Canvas { Width = 400, Height = 400 };
        var targets = new Border[targetCount];
        for (int i = 0; i < targetCount; i++)
        {
            var b = new Border { Width = 30, Height = 20 };
            Canvas.SetLeft(b, (i % 10) * 35);
            Canvas.SetTop(b, (i / 10) * 25);
            canvas.Children.Add(b);
            targets[i] = b;
        }
        h.SetContent(canvas);
        await Harness.Render();

        var compositor = ElementCompositionPreview.GetElementVisual(canvas).Compositor;
        var root = compositor.CreateContainerVisual();
        ElementCompositionPreview.SetElementChildVisual(canvas, root);

        ReconcileHighlightOverlay.TestHoldDurationOverrideMs = holdMs;
        var overlay = new ReconcileHighlightOverlay(canvas, root, scheduleExpiry);
        return (canvas, targets, root, overlay);
    }

    private static void Teardown(ReconcileHighlightOverlay overlay)
    {
        overlay.Dispose();
        ReconcileHighlightOverlay.TestHoldDurationOverrideMs = null;
    }

    // Only the scheduling boundary is simulated: each callback below is the
    // overlay's actual expiry handler, including its stale-schedule guard.
    private sealed class CapturedExpiry(TimeSpan dueAt, Action onExpire)
    {
        public TimeSpan DueAt { get; } = dueAt;
        public bool Cancelled { get; set; }
        public bool Delivered { get; private set; }

        // Deliberately permits delivery after cancellation to model a tick
        // already queued on the dispatcher when Refresh/Dispose cancels it.
        public void Deliver()
        {
            Delivered = true;
            onExpire();
        }
    }

    private sealed class CapturedExpiryScheduler
    {
        public TimeSpan Elapsed { get; private set; }
        public List<CapturedExpiry> Expiries { get; } = new();

        public Action Schedule(TimeSpan delay, Action onExpire)
        {
            var expiry = new CapturedExpiry(Elapsed + delay, onExpire);
            Expiries.Add(expiry);
            return () => expiry.Cancelled = true;
        }

        public void AdvanceBy(int milliseconds, bool dispatch = true)
        {
            Elapsed += TimeSpan.FromMilliseconds(milliseconds);
            if (!dispatch) return;
            foreach (var expiry in Expiries.Where(e => !e.Cancelled && !e.Delivered
                && e.DueAt <= Elapsed).ToArray())
                expiry.Deliver();
        }
    }

    // ── Baseline: 1 target → 1 sprite ──
    internal class Show_SingleTarget_CreatesOneSprite(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var (canvas, targets, _, overlay) = await SetupAsync(H);
            try
            {
                overlay.Show(canvas, targets, Array.Empty<UIElement>());

                H.Check("OverlayLifecycle_SingleTarget_OneSprite", overlay.LiveSpriteCount == 1);
                H.Check("OverlayLifecycle_SingleTarget_OneActive", overlay.ActiveTargetCount == 1);
            }
            finally { Teardown(overlay); }
        }
    }

    // ── Direct dedup regression: same UIElement shown twice → still one sprite ──
    internal class Show_SameTargetTwice_DoesNotStack(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var (canvas, targets, _, overlay) = await SetupAsync(H);
            try
            {
                overlay.Show(canvas, targets, Array.Empty<UIElement>());
                overlay.Show(canvas, targets, Array.Empty<UIElement>());
                overlay.Show(canvas, targets, Array.Empty<UIElement>());

                H.Check("OverlayLifecycle_DedupSameTarget_OneSprite",
                    overlay.LiveSpriteCount == 1);
                H.Check("OverlayLifecycle_DedupSameTarget_OneActive",
                    overlay.ActiveTargetCount == 1);
            }
            finally { Teardown(overlay); }
        }
    }

    // ── Refresh resets the expiry timer (would have expired without the reset) ──
    internal class Show_RefreshExtendsLifetime(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Issue #995: advance captured schedules instead of waiting on the
            // wall clock. A survival predicate cannot be replaced by WaitFor:
            // it is already true before any time passes.
            var scheduler = new CapturedExpiryScheduler();
            var (canvas, targets, _, overlay) = await SetupAsync(H, holdMs: 800,
                scheduleExpiry: scheduler.Schedule);
            try
            {
                int completed = 0;
                overlay.TestExpiryCompleted = () => completed++;
                overlay.Show(canvas, targets, Array.Empty<UIElement>());
                H.Check("OverlayLifecycle_Refresh_InitialSchedule", scheduler.Expiries.Count == 1);
                if (scheduler.Expiries.Count != 1) return;
                var original = scheduler.Expiries[0];
                H.Check("OverlayLifecycle_Refresh_InitialDeadline",
                    original.DueAt == TimeSpan.FromMilliseconds(800));

                scheduler.AdvanceBy(500);
                H.Check("OverlayLifecycle_Refresh_AliveBeforeRefresh",
                    overlay.LiveSpriteCount == 1 && overlay.ActiveTargetCount == 1);

                overlay.Show(canvas, targets, Array.Empty<UIElement>());
                H.Check("OverlayLifecycle_Refresh_NewSchedule", scheduler.Expiries.Count == 2);
                if (scheduler.Expiries.Count != 2) return;
                H.Check("OverlayLifecycle_Refresh_PreviousScheduleCancelled", original.Cancelled);
                H.Check("OverlayLifecycle_Refresh_FullHoldRescheduled",
                    scheduler.Expiries[1].DueAt == TimeSpan.FromMilliseconds(1300));

                // 900ms since Show, past the original 800ms deadline.
                scheduler.AdvanceBy(400);
                H.Check("OverlayLifecycle_Refresh_AliveAfterRefresh",
                    overlay.LiveSpriteCount == 1 && overlay.ActiveTargetCount == 1);

                scheduler.AdvanceBy(399);
                H.Check("OverlayLifecycle_Refresh_AliveUntilNewDeadline",
                    overlay.LiveSpriteCount == 1 && completed == 0);

                scheduler.AdvanceBy(1);
                H.Check("OverlayLifecycle_Refresh_ExpiresAfterFinalWindow",
                    overlay.LiveSpriteCount == 0 && overlay.ActiveTargetCount == 0);
                H.Check("OverlayLifecycle_Refresh_CompletionOnce", completed == 1);
            }
            finally { Teardown(overlay); }
        }
    }

    // ── No refresh → expires after hold duration ──
    internal class Show_NoRefresh_ExpiresAfterHoldDuration(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var (canvas, targets, _, overlay) = await SetupAsync(H, holdMs: 100);
            try
            {
                var expired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                overlay.TestExpiryCompleted = () => expired.TrySetResult();
                overlay.Show(canvas, targets, Array.Empty<UIElement>());
                H.Check("OverlayLifecycle_Expire_AliveImmediately",
                    overlay.LiveSpriteCount == 1);

                // Real DispatcherQueueTimer integration: completion is signalled
                // after the real handler runs; the delay is only a failure timeout.
                bool completed;
                try
                {
                    await expired.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    completed = true;
                }
                catch (TimeoutException) { completed = false; }
                H.Check("OverlayLifecycle_Expire_CompletionObserved", completed);
                H.Check("OverlayLifecycle_Expire_GoneAfterHold",
                    completed && overlay.LiveSpriteCount == 0);
                H.Check("OverlayLifecycle_Expire_DictDrained",
                    overlay.ActiveTargetCount == 0);
            }
            finally { Teardown(overlay); }
        }
    }

    internal class Show_NoRefresh_ExpiresAtScheduledDeadline(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var scheduler = new CapturedExpiryScheduler();
            var (canvas, targets, _, overlay) = await SetupAsync(H, holdMs: 100,
                scheduleExpiry: scheduler.Schedule);
            try
            {
                int completed = 0;
                overlay.TestExpiryCompleted = () => completed++;
                overlay.Show(canvas, targets, Array.Empty<UIElement>());
                H.Check("OverlayLifecycle_Deadline_Created",
                    overlay.LiveSpriteCount == 1 && overlay.ActiveTargetCount == 1);
                scheduler.AdvanceBy(99);
                H.Check("OverlayLifecycle_Deadline_AliveBeforeHold",
                    overlay.LiveSpriteCount == 1 && overlay.ActiveTargetCount == 1 && completed == 0);
                scheduler.AdvanceBy(1);
                H.Check("OverlayLifecycle_Deadline_ExpiredAtHold",
                    overlay.LiveSpriteCount == 0 && overlay.ActiveTargetCount == 0 && completed == 1);
            }
            finally { Teardown(overlay); }
        }
    }

    internal class Show_QueuedExpiryBeforeRefresh_PreservesRefreshedSprite(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var scheduler = new CapturedExpiryScheduler();
            var (canvas, targets, _, overlay) = await SetupAsync(H, holdMs: 800,
                scheduleExpiry: scheduler.Schedule);
            try
            {
                int completed = 0;
                overlay.TestExpiryCompleted = () => completed++;
                overlay.Show(canvas, targets, Array.Empty<UIElement>());
                H.Check("OverlayLifecycle_QueuedExpiry_InitialSchedule", scheduler.Expiries.Count == 1);
                if (scheduler.Expiries.Count != 1) return;
                var original = scheduler.Expiries[0];

                // The first tick is due, but dispatch has not happened yet.
                scheduler.AdvanceBy(800, dispatch: false);
                overlay.Show(canvas, targets, Array.Empty<UIElement>());
                H.Check("OverlayLifecycle_QueuedExpiry_NewSchedule", scheduler.Expiries.Count == 2);
                if (scheduler.Expiries.Count != 2) return;
                H.Check("OverlayLifecycle_QueuedExpiry_OldCancelled", original.Cancelled);
                original.Deliver();
                H.Check("OverlayLifecycle_QueuedExpiry_StaleCallbackIgnored",
                    overlay.LiveSpriteCount == 1 && overlay.ActiveTargetCount == 1 && completed == 0);

                scheduler.AdvanceBy(799);
                H.Check("OverlayLifecycle_QueuedExpiry_NewScheduleStillAlive",
                    overlay.LiveSpriteCount == 1 && overlay.ActiveTargetCount == 1);
                scheduler.AdvanceBy(1);
                H.Check("OverlayLifecycle_QueuedExpiry_CurrentCallbackExpires",
                    overlay.LiveSpriteCount == 0 && overlay.ActiveTargetCount == 0 && completed == 1);
            }
            finally { Teardown(overlay); }
        }
    }

    // ── Refresh updates geometry when target resizes between Shows ──
    internal class Show_RefreshUpdatesGeometry(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var (canvas, targets, _, overlay) = await SetupAsync(H);
            try
            {
                overlay.Show(canvas, targets, Array.Empty<UIElement>());
                var initialSize = overlay.TestActiveSprites()[0].Size;

                // Resize the underlying target and re-show
                targets[0].Width = 80;
                targets[0].Height = 60;
                await Harness.Render();

                overlay.Show(canvas, targets, Array.Empty<UIElement>());
                var resized = overlay.TestActiveSprites()[0].Size;

                H.Check("OverlayLifecycle_Refresh_GeometryInitialMatches",
                    Math.Abs(initialSize.X - 30f) < 0.5f
                    && Math.Abs(initialSize.Y - 20f) < 0.5f);
                H.Check("OverlayLifecycle_Refresh_GeometryUpdatedToNewSize",
                    Math.Abs(resized.X - 80f) < 0.5f
                    && Math.Abs(resized.Y - 60f) < 0.5f);
            }
            finally { Teardown(overlay); }
        }
    }

    // ── Refresh swaps brush when role changes (mounted → modified, last-wins) ──
    internal class Show_MountThenModified_BrushSwapped(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var (canvas, targets, _, overlay) = await SetupAsync(H);
            try
            {
                overlay.Show(canvas, targets, Array.Empty<UIElement>());
                var mountedBrush = overlay.TestActiveSprites()[0].Brush;

                overlay.Show(canvas, Array.Empty<UIElement>(), targets);
                var modifiedBrush = overlay.TestActiveSprites()[0].Brush;

                H.Check("OverlayLifecycle_BrushSwap_StillOneSprite",
                    overlay.LiveSpriteCount == 1);
                H.Check("OverlayLifecycle_BrushSwap_BrushChanged",
                    !ReferenceEquals(mountedBrush, modifiedBrush));
            }
            finally { Teardown(overlay); }
        }
    }

    // ── Distinct targets get their own sprites ──
    internal class Show_DistinctTargets_OneSpritePerTarget(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var (canvas, targets, _, overlay) = await SetupAsync(H, targetCount: 5);
            try
            {
                overlay.Show(canvas, targets, Array.Empty<UIElement>());
                H.Check("OverlayLifecycle_DistinctTargets_FiveSprites",
                    overlay.LiveSpriteCount == 5);
                H.Check("OverlayLifecycle_DistinctTargets_FiveActive",
                    overlay.ActiveTargetCount == 5);
            }
            finally { Teardown(overlay); }
        }
    }

    // ── Zero-size target is silently skipped (would TransformToVisual erratically) ──
    internal class Show_TargetWithZeroSize_Skipped(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var canvas = new Canvas { Width = 200, Height = 200 };
            var zero = new Border();   // no Width/Height — ActualWidth = 0
            canvas.Children.Add(zero);
            H.SetContent(canvas);
            await Harness.Render();

            var compositor = ElementCompositionPreview.GetElementVisual(canvas).Compositor;
            var root = compositor.CreateContainerVisual();
            ElementCompositionPreview.SetElementChildVisual(canvas, root);

            var overlay = new ReconcileHighlightOverlay(canvas, root);
            try
            {
                overlay.Show(canvas, new UIElement[] { zero }, Array.Empty<UIElement>());
                H.Check("OverlayLifecycle_ZeroSize_NoSprite",
                    overlay.LiveSpriteCount == 0 && overlay.ActiveTargetCount == 0);
            }
            finally { overlay.Dispose(); }
        }
    }

    // ── Dispose stops timers, drains container, doesn't throw ──
    internal class Dispose_StopsTimers_ClearsContainer(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var scheduler = new CapturedExpiryScheduler();
            var (canvas, targets, _, overlay) = await SetupAsync(H, targetCount: 3,
                scheduleExpiry: scheduler.Schedule);
            try
            {
                int completed = 0;
                overlay.TestExpiryCompleted = () => completed++;
                overlay.Show(canvas, targets, Array.Empty<UIElement>());
                H.Check("OverlayLifecycle_Dispose_PrePopulated",
                    overlay.LiveSpriteCount == 3);

                bool threw = false;
                try { overlay.Dispose(); }
                catch { threw = true; }

                H.Check("OverlayLifecycle_Dispose_NoException", !threw);
                H.Check("OverlayLifecycle_Dispose_DictCleared",
                    overlay.ActiveTargetCount == 0);
                H.Check("OverlayLifecycle_Dispose_SchedulesCancelled",
                    scheduler.Expiries.Count == 3 && scheduler.Expiries.All(e => e.Cancelled));

                // Callbacks queued before disposal must be harmless afterward.
                bool lateCallbackThrew = false;
                try
                {
                    foreach (var expiry in scheduler.Expiries) expiry.Deliver();
                }
                catch { lateCallbackThrew = true; }
                H.Check("OverlayLifecycle_Dispose_QueuedCallbacksIgnored",
                    !lateCallbackThrew && overlay.ActiveTargetCount == 0 && completed == 0);
            }
            finally
            {
                ReconcileHighlightOverlay.TestHoldDurationOverrideMs = null;
            }
        }
    }

    // ── Per-flush budget caps new sprites at MaxSpritesPerFlush=200 ──
    // (Doesn't directly assert the constant value — asserts the count is
    // bounded below the requested count, proving the budget gate fired.)
    internal class Show_ManyDistinctTargets_RespectsBudget(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var (canvas, targets, _, overlay) = await SetupAsync(H, targetCount: 250);
            try
            {
                overlay.Show(canvas, targets, Array.Empty<UIElement>());
                // Should be capped well below 250 — exact value depends on
                // MaxSpritesPerFlush (200) + MaxLiveSprites (500). We just
                // assert it's bounded and non-zero.
                H.Check("OverlayLifecycle_Cap_Bounded",
                    overlay.LiveSpriteCount < 250);
                H.Check("OverlayLifecycle_Cap_AddedSomething",
                    overlay.LiveSpriteCount > 0);
            }
            finally { Teardown(overlay); }
        }
    }

    // ── Architectural regression: no Composition animations are attached ──
    // The original #167 dimming bug was caused by a CompositionScopedBatch
    // capturing reconciler-driven animations. The fix removed both the
    // scoped batch AND the per-sprite ScalarKeyFrameAnimation. This pins
    // that decision: a future contributor who reintroduces StartAnimation
    // for a "smoother fade" will see this fail.
    internal class Show_NoCompositionAnimationStarted(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var (canvas, targets, _, overlay) = await SetupAsync(H, targetCount: 3);
            try
            {
                overlay.Show(canvas, targets, Array.Empty<UIElement>());

                var sprites = overlay.TestActiveSprites();
                bool noImplicit = sprites.All(s => s.ImplicitAnimations is null);
                bool opacityIsStatic = sprites.All(s => Math.Abs(s.Opacity - 0.33f) < 0.001f);

                H.Check("OverlayLifecycle_NoAnim_ImplicitAnimationsNull", noImplicit);
                H.Check("OverlayLifecycle_NoAnim_StaticOpacity", opacityIsStatic);
            }
            finally { Teardown(overlay); }
        }
    }

    // ── TestForceExpire drains everything synchronously ──
    internal class TestForceExpire_DrainsAll(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var (canvas, targets, _, overlay) = await SetupAsync(H, targetCount: 4);
            try
            {
                overlay.Show(canvas, targets, Array.Empty<UIElement>());
                H.Check("OverlayLifecycle_ForceExpire_PrePopulated",
                    overlay.LiveSpriteCount == 4);

                overlay.TestForceExpire();
                H.Check("OverlayLifecycle_ForceExpire_DrainedSprites",
                    overlay.LiveSpriteCount == 0);
                H.Check("OverlayLifecycle_ForceExpire_DrainedActive",
                    overlay.ActiveTargetCount == 0);
            }
            finally { Teardown(overlay); }
        }
    }
}
