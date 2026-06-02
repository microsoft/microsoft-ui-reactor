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
        return await Harness.WaitFor(() =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return !weak.IsAlive || isDetached();
        }, maxPasses: 60, perPassMs: 100);
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
