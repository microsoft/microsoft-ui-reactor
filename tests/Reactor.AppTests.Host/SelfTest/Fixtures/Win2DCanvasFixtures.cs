using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Advanced.Win2D;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.UI;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Advanced.Factories;
using Colors = Microsoft.UI.Colors;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class Win2DCanvasFixtures
{
    internal class CanvasMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var drawCount = 0;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (redrawKey, setRedrawKey) = ctx.UseState(0);
                return VStack(8,
                    Button("Redraw Win2D", () => setRedrawKey(redrawKey + 1)),
                    Win2DCanvas((session, _) =>
                    {
                        Interlocked.Increment(ref drawCount);
                        session.Clear(Colors.White);
                        session.DrawText($"draw {redrawKey}", 8, 8, Colors.Black);
                    }, redrawKey)
                        .Width(240)
                        .Height(120));
            });

            H.Check("Win2D_Canvas_FirstDraw",
                await Harness.WaitFor(() => Volatile.Read(ref drawCount) >= 1, maxPasses: 40, perPassMs: 25));

            var before = Volatile.Read(ref drawCount);
            var weak = CaptureControlWeakReference<CanvasControl>(H);
            H.ClickButton("Redraw Win2D");
            await Harness.Render();

            H.Check("Win2D_Canvas_RedrawKeyInvalidates",
                await Harness.WaitFor(() => Volatile.Read(ref drawCount) > before, maxPasses: 40, perPassMs: 25));

            host.Mount(_ => TextBlock("Win2D canvas unmounted"));
            await Harness.Render();

            H.Check("Win2D_Canvas_NoCanvasControlLeak",
                await WaitForCollectedOrDetached(weak, () => H.FindControl<CanvasControl>(_ => true) is null));
        }
    }

    internal class AnimatedCanvasMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            AnimatedProbe? probe = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var state = ctx.UseDrawState(() => new AnimatedProbe());
                probe = state.Current;
                var (version, setVersion) = ctx.UseState(0);
                ctx.UseEffect(() =>
                {
                    Interlocked.Increment(ref state.Current.Ticks);
                }, version);

                return VStack(8,
                    Button("Pause Win2D", () => setVersion(version + 1)),
                    Win2DAnimatedCanvas(
                        onUpdate: (_, drawState) => Interlocked.Increment(ref ((AnimatedProbe)drawState!).Ticks),
                        onDraw: (session, _, _) =>
                        {
                            session.Clear(Colors.Black);
                        },
                        drawState: state.Current,
                        isPaused: true)
                        .TargetFps(30)
                        .Width(240)
                        .Height(120));
            });

            H.Check("Win2D_AnimatedCanvas_Ticks",
                await Harness.WaitFor(() => probe is not null && Volatile.Read(ref probe.Ticks) >= 1,
                    maxPasses: 50,
                    perPassMs: 25));

            H.ClickButton("Pause Win2D");
            await Harness.Render();

            var plateau = await WaitForPlateau(() => probe is null ? 0 : Volatile.Read(ref probe.Ticks));
            H.Check("Win2D_AnimatedCanvas_TicksPlateauWhenPaused", plateau);

            host.Mount(_ => TextBlock("Win2D animated canvas unmounted"));
            await Harness.Render();
        }
    }

    internal class VirtualCanvasMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var regionDraws = 0;
            var targetHits = 0;
            var target = new Rect(512, 512, 128, 128);
            var host = H.CreateHost();

            host.Mount(ctx =>
            {
                var (version, setVersion) = ctx.UseState(0);
                var canvas = Win2DVirtualCanvas((session, region) =>
                {
                    Interlocked.Increment(ref regionDraws);
                    if (RectApproximatelyEquals(region, target) || RectIntersects(region, target))
                        Interlocked.Increment(ref targetHits);

                    session.Clear(Colors.White);
                    session.FillRectangle(region, Color.FromArgb(255, 230, 240, 255));
                    session.DrawRectangle(region, Colors.SteelBlue, 2);
                }, new Size(4000, 4000)) with
                {
                    InvalidateRegions = version == 0 ? null : [target]
                };

                return VStack(8,
                    Button("Invalidate Win2D region", () => setVersion(version + 1)),
                    ScrollView(canvas)
                        .Width(320)
                        .Height(220));
            });

            H.Check("Win2D_VirtualCanvas_FirstRegionDraw",
                await Harness.WaitFor(() => Volatile.Read(ref regionDraws) >= 1,
                    maxPasses: 50,
                    perPassMs: 25));

            H.ClickButton("Invalidate Win2D region");
            await Harness.Render();

            H.Check("Win2D_VirtualCanvas_InvalidateRegionsDrawsTarget",
                await Harness.WaitFor(() => Volatile.Read(ref targetHits) >= 1,
                    maxPasses: 50,
                    perPassMs: 25));

            host.Mount(_ => TextBlock("Win2D virtual canvas unmounted"));
            await Harness.Render();
        }
    }

    /// <summary>
    /// Spec 053 review #1: verifies that a Win2DAnimatedCanvas mounted with
    /// <c>isPaused: true</c> can be unpaused by a subsequent re-render.
    /// Before the Mount-fix, the handler wrote <c>ctrl.Paused = true</c> at mount
    /// and Update never wrote Paused, so the native game loop stayed parked.
    /// </summary>
    internal class AnimatedCanvasInitialPausedResumes(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            AnimatedProbe? probe = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var state = ctx.UseDrawState(() => new AnimatedProbe());
                probe = state.Current;
                var (paused, setPaused) = ctx.UseState(true);  // start paused

                return VStack(8,
                    Button("Resume Win2D", () => setPaused(false)),
                    Win2DAnimatedCanvas(
                        onUpdate: (_, drawState) => Interlocked.Increment(ref ((AnimatedProbe)drawState!).Ticks),
                        onDraw: (session, _, _) => session.Clear(Colors.Black),
                        drawState: state.Current,
                        isPaused: paused)
                        .TargetFps(30)
                        .Width(240)
                        .Height(120));
            });

            // While initially paused, give the canvas multiple game-loop ticks worth of
            // time. The handler must NOT have written ctrl.Paused=true, so the game loop
            // keeps ticking, but IsPaused gates the user's OnUpdate delegate — Ticks stays
            // at 0 because the probe increment happens inside OnUpdate.
            for (int i = 0; i < 6; i++) await Harness.Render();
            H.Check("Win2D_AnimatedCanvas_InitiallyPaused_NoOnUpdateTicks",
                probe is not null && Volatile.Read(ref probe.Ticks) == 0);

            // Click to resume — re-render flips IsPaused=false; gate opens; Ticks start advancing.
            H.ClickButton("Resume Win2D");
            await Harness.Render();
            H.Check("Win2D_AnimatedCanvas_ResumesAfterInitialPaused",
                await Harness.WaitFor(() => probe is not null && Volatile.Read(ref probe.Ticks) >= 2,
                    maxPasses: 80, perPassMs: 50));

            host.Mount(_ => TextBlock("Win2D initially-paused unmounted"));
            await Harness.Render();
        }
    }

    /// <summary>
    /// Spec 053 review #3: verifies that re-rendering Win2DCanvas with the same
    /// RedrawKey value (boxed differently across renders) does NOT trigger
    /// Invalidate(). Before the fix, ReferenceEquals on boxed primitives made
    /// every parent re-render trigger a redundant redraw.
    /// </summary>
    internal class CanvasSameRedrawKeyNoExtraDraws(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var drawCount = 0;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (bump, setBump) = ctx.UseState(0);
                return VStack(8,
                    Button("Re-render same key", () => setBump(bump + 1)),
                    Win2DCanvas((session, _) =>
                    {
                        Interlocked.Increment(ref drawCount);
                        session.Clear(Colors.White);
                    }, redrawKey: 42)  // fixed value across all re-renders
                        .Width(240)
                        .Height(120));
            });

            H.Check("Win2D_Canvas_SameKey_InitialDraw",
                await Harness.WaitFor(() => Volatile.Read(ref drawCount) >= 1, maxPasses: 40, perPassMs: 25));

            var before = Volatile.Read(ref drawCount);
            const int rerenderCount = 5;
            for (int i = 0; i < rerenderCount; i++)
            {
                H.ClickButton("Re-render same key");
                await Harness.Render();
            }
            // Let any incidental draws settle.
            for (int i = 0; i < 4; i++) await Harness.Render();

            var delta = Volatile.Read(ref drawCount) - before;
            // With value-equality on RedrawKey, `rerenderCount` re-renders with the same
            // key value must not produce `rerenderCount` invalidations. Win2D may
            // invalidate on its own for window/dpi changes; allow ≤2 incidental draws.
            H.Check("Win2D_Canvas_SameKey_NoExtraDrawsFromRerender", delta <= 2);

            host.Mount(_ => TextBlock("Win2D same-key unmounted"));
            await Harness.Render();
        }
    }

    private sealed class AnimatedProbe
    {
        public int Ticks;
    }

    private static WeakReference CaptureControlWeakReference<TControl>(Harness h)
        where TControl : Microsoft.UI.Xaml.DependencyObject
    {
        var control = h.FindControl<TControl>(_ => true);
        if (control is null)
            throw new InvalidOperationException($"Expected {typeof(TControl).Name} in visual tree.");
        return new WeakReference(control);
    }

    private static async Task<bool> WaitForCollectedOrDetached(WeakReference weak, Func<bool> isDetached)
    {
        // Detection priority: the visual-tree detach check is the observable invariant
        // we actually care about. WeakReference.IsAlive is a paranoia signal for hidden
        // hard references kept alive after detach; we let normal GC do its thing rather
        // than forcing collections inside the WaitFor loop (CodeQL flags explicit
        // GC.Collect, and forced collections introduce test-suite-wide perf cost / flake).
        return await Harness.WaitFor(() => isDetached() || !weak.IsAlive,
            maxPasses: 60, perPassMs: 100);
    }

    private static async Task<bool> WaitForPlateau(Func<int> read)
    {
        var previous = read();
        var stablePolls = 0;
        return await Harness.WaitFor(() =>
        {
            var current = read();
            if (current == previous)
            {
                stablePolls++;
            }
            else
            {
                previous = current;
                stablePolls = 0;
            }

            return stablePolls >= 5;
        }, maxPasses: 30, perPassMs: 50);
    }

    private static bool RectApproximatelyEquals(Rect left, Rect right) =>
        Math.Abs(left.X - right.X) < 0.01
        && Math.Abs(left.Y - right.Y) < 0.01
        && Math.Abs(left.Width - right.Width) < 0.01
        && Math.Abs(left.Height - right.Height) < 0.01;

    private static bool RectIntersects(Rect left, Rect right) =>
        left.X < right.X + right.Width
        && left.X + left.Width > right.X
        && left.Y < right.Y + right.Height
        && left.Y + left.Height > right.Y;
}
