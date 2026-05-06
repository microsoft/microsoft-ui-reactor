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
        SetupAsync(Harness h, int targetCount = 1, int? holdMs = null)
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
        var overlay = new ReconcileHighlightOverlay(canvas, root);
        return (canvas, targets, root, overlay);
    }

    private static void Teardown(ReconcileHighlightOverlay overlay)
    {
        overlay.Dispose();
        ReconcileHighlightOverlay.TestHoldDurationOverrideMs = null;
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
            // Tight 200ms window; refresh at 120ms (< 200) should keep alive
            // through 280ms total wall time; let it expire after.
            var (canvas, targets, _, overlay) = await SetupAsync(H, holdMs: 200);
            try
            {
                overlay.Show(canvas, targets, Array.Empty<UIElement>());
                await Task.Delay(120);
                H.Check("OverlayLifecycle_Refresh_AliveBeforeRefresh",
                    overlay.LiveSpriteCount == 1);

                // Refresh — timer should restart from 0
                overlay.Show(canvas, targets, Array.Empty<UIElement>());
                await Task.Delay(120);
                // Now ~240ms since first Show, ~120ms since refresh — would
                // be expired without the timer reset; should still be alive.
                H.Check("OverlayLifecycle_Refresh_AliveAfterRefresh",
                    overlay.LiveSpriteCount == 1);

                // Wait past the new window — should expire.
                await Task.Delay(160);
                H.Check("OverlayLifecycle_Refresh_ExpiresAfterFinalWindow",
                    overlay.LiveSpriteCount == 0 && overlay.ActiveTargetCount == 0);
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
                overlay.Show(canvas, targets, Array.Empty<UIElement>());
                H.Check("OverlayLifecycle_Expire_AliveImmediately",
                    overlay.LiveSpriteCount == 1);

                await Task.Delay(180);
                H.Check("OverlayLifecycle_Expire_GoneAfterHold",
                    overlay.LiveSpriteCount == 0);
                H.Check("OverlayLifecycle_Expire_DictDrained",
                    overlay.ActiveTargetCount == 0);
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
            var (canvas, targets, _, overlay) = await SetupAsync(H, targetCount: 3);
            try
            {
                overlay.Show(canvas, targets, Array.Empty<UIElement>());
                H.Check("OverlayLifecycle_Dispose_PrePopulated",
                    overlay.LiveSpriteCount == 3);

                bool threw = false;
                try { overlay.Dispose(); }
                catch { threw = true; }

                H.Check("OverlayLifecycle_Dispose_NoException", !threw);
                H.Check("OverlayLifecycle_Dispose_DictCleared",
                    overlay.ActiveTargetCount == 0);
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
