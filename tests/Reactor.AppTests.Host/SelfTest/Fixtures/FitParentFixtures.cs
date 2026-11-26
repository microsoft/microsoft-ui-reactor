using System.Diagnostics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Charting.Charts;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;
using WinBorder = Microsoft.UI.Xaml.Controls.Border;
using WinCanvas = Microsoft.UI.Xaml.Controls.Canvas;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #174 — <see cref="Factories.FitParent"/> selftests (design 03 §3 "WP-C fixture design").
/// Each fixture renders into a native <see cref="WinBorder"/> it resizes directly, observes the
/// committed size through the probe <c>D3Canvas(w, h)</c>'s Width/Height, and counts the calls
/// to <c>render</c>. The internal <see cref="FitParentBox"/> seam is reached per instance through
/// <see cref="FitParentOptions.TestBoxCreated"/>.
/// </summary>
internal static class FitParentFixtures
{
    private const int PerPassMs = 20;
    private const double Tol = 0.01;

    private static readonly TimeSpan Debounce60 = TimeSpan.FromMilliseconds(60);

    /// <summary>Per-fixture observation state: render calls, the sizes passed, and the boxes created.</summary>
    private sealed class Probe
    {
        public int Renders;
        public readonly List<(double W, double H)> Sizes = new();
        public readonly List<FitParentBox> Boxes = new();
        public FitParentBox? Box => Boxes.Count > 0 ? Boxes[^1] : null;

        public FitParentOptions Options(TimeSpan debounce, bool scale = true) => new()
        {
            Debounce = debounce,
            ScaleDuringResize = scale,
            TestBoxCreated = b => Boxes.Add(b),
        };

        public Func<double, double, Element> Render(Func<double, double, Element>? inner = null) => (w, h) =>
        {
            Renders++;
            Sizes.Add((w, h));
            return (inner ?? ProbeCanvas)(w, h);
        };

        public WinBorder? Host => Box?.Host?.Current;
    }

    private static Element ProbeCanvas(double w, double h) =>
        D3Canvas(w, h).AutomationName(Name(w, h));

    private static string Name(double w, double h) => $"{w:0}x{h:0}";

    /// <summary>A native resizable test container plus a Reactor host rendering into it.</summary>
    private sealed class Rig : IDisposable
    {
        public required WinBorder Box { get; init; }
        public required ReactorHost Host { get; init; }
        public void Dispose() => Host.Dispose();
    }

    private static Rig Mount(Harness h, Func<RenderContext, Element> root, double width = 400, double height = 300)
    {
        var box = new WinBorder
        {
            Width = width,
            Height = height,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        h.SetContent(box);
        var host = h.CreateHost();
        host.ContentTarget = box;
        host.Mount(root);
        return new Rig { Box = box, Host = host };
    }

    private static async Task TearDown(Harness h, Rig rig)
    {
        rig.Dispose();
        h.SetContent(null);
        await Harness.Render();
    }

    private static void Resize(WinBorder box, double w, double h)
    {
        box.Width = w;
        box.Height = h;
        box.UpdateLayout();
    }

    private static T? FindDescendant<T>(DependencyObject? root, Func<T, bool> predicate) where T : DependencyObject
    {
        if (root is null) return null;
        if (root is T t && predicate(t)) return t;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var hit = FindDescendant(VisualTreeHelper.GetChild(root, i), predicate);
            if (hit is not null) return hit;
        }
        return null;
    }

    /// <summary>The probe canvas (explicit Width/Height + an automation name), or null.</summary>
    private static WinCanvas? FindProbe(DependencyObject root) =>
        FindDescendant<WinCanvas>(root, c => !double.IsNaN(c.Width) && !string.IsNullOrEmpty(AutomationProperties.GetName(c)));

    /// <summary>The first canvas with an explicit size under <paramref name="root"/> (a chart's plot canvas).</summary>
    private static WinCanvas? FindSizedCanvas(DependencyObject? root) =>
        FindDescendant<WinCanvas>(root, c => !double.IsNaN(c.Width) && !double.IsNaN(c.Height));

    private static bool ProbeIs(DependencyObject root, double w, double h) =>
        FindProbe(root) is { } c && Near(c.Width, w) && Near(c.Height, h)
            && AutomationProperties.GetName(c) == Name(w, h);

    private static bool Near(double a, double b, double tol = Tol) => Math.Abs(a - b) <= tol;

    /// <summary>
    /// FitParent and ElementPool.CleanElement both <c>ClearValue</c> RenderTransform, so "cleared"
    /// means no local value plus an effective transform that maps points to themselves. Never
    /// match on the read-back type: WinUI's default reads back as an identity MatrixTransform
    /// under JIT but as the base <see cref="Transform"/> under NativeAOT, where the projection has
    /// no type-map entry for it. ReadLocalValue and TransformPoint are projection-independent.
    /// </summary>
    private static bool IsCleared(UIElement? e) =>
        e is not null
        && ReferenceEquals(e.ReadLocalValue(UIElement.RenderTransformProperty), DependencyProperty.UnsetValue)
        && MapsIdentity(e.RenderTransform);

    /// <summary>
    /// Positive counterpart of <see cref="IsCleared"/>, never written as <c>!IsCleared</c>: FitParent's
    /// transient is a locally-set <see cref="CompositeTransform"/> (a type the AOT projection maps,
    /// because FitParent constructs it) that actually moves points. All three must hold, so the
    /// check is red under both JIT and AOT when no transient is in effect.
    /// </summary>
    private static bool HasTransient(UIElement? e) =>
        e is not null
        && !ReferenceEquals(e.ReadLocalValue(UIElement.RenderTransformProperty), DependencyProperty.UnsetValue)
        && e.RenderTransform is CompositeTransform t
        && !MapsIdentity(t);

    private static bool MapsIdentity(GeneralTransform? t)
    {
        if (t is null)
            return true;
        var a = t.TransformPoint(new Point(0, 0));
        var b = t.TransformPoint(new Point(100, 50));
        return Near(a.X, 0) && Near(a.Y, 0) && Near(b.X, 100) && Near(b.Y, 50);
    }

    private static string DescribeTransform(UIElement? e)
    {
        if (e is null)
            return "transform=<no element>";
        var local = ReferenceEquals(e.ReadLocalValue(UIElement.RenderTransformProperty), DependencyProperty.UnsetValue) ? "unset" : "set";
        var t = e.RenderTransform;
        var p = t?.TransformPoint(new Point(100, 50));
        return $"transform={t?.GetType().Name ?? "null"} local={local} map(100,50)={(p is { } q ? $"{q.X},{q.Y}" : "n/a")}";
    }

    private static Rect? ClipRect(UIElement? e) => e?.Clip is RectangleGeometry g ? g.Rect : null;

    /// <summary>The host carries FitParent's stale-content clip, at (0,0) and exactly its arranged size.</summary>
    private static bool ClipIsHostBounds(FrameworkElement? e) =>
        e is not null && ClipRect(e) is { } r && Near(r.X, 0) && Near(r.Y, 0)
            && Near(r.Width, e.ActualWidth) && Near(r.Height, e.ActualHeight);

    private static string DescribeClip(FrameworkElement? e) =>
        (ClipRect(e) is { } r ? $"clip={r.X},{r.Y},{r.Width}x{r.Height}" : $"clip={e?.Clip?.GetType().Name ?? "null"}")
        + (e is null ? "" : $" host={e.ActualWidth}x{e.ActualHeight}");

    /// <summary>
    /// Resizes step by step with a pass between steps (so a render-batching artefact cannot pass
    /// as debouncing): after each step nothing has re-rendered and the debounce is still pending.
    /// </summary>
    private static async Task<bool> StepsStayPending(Rig rig, Probe p, int rendersBefore, params (double W, double H)[] steps)
    {
        bool ok = true;
        foreach (var (w, hh) in steps)
        {
            Resize(rig.Box, w, hh);
            await Harness.Render();
            var pending = p.Renders == rendersBefore && p.Box is { IsTimerRunning: true } && ProbeIs(rig.Box, 400, 300);
            Console.WriteLine($"# step {Name(w, hh)}: pending={pending} timer={p.Box?.IsTimerRunning} {Describe(rig.Box, p)}");
            ok &= pending;
        }
        return ok;
    }

    private static string Describe(WinBorder box, Probe p)
    {
        var probe = FindProbe(box);
        var host = p.Host;
        return $"renders={p.Renders} sizes=[{string.Join(",", p.Sizes.Select(s => Name(s.W, s.H)))}] "
            + $"probe={(probe is null ? "none" : $"{probe.Width}x{probe.Height}")} "
            + $"host={(host is null ? "null" : $"{host.ActualWidth}x{host.ActualHeight}")} "
            + $"box={box.ActualWidth}x{box.ActualHeight}";
    }

    /// <summary>Pumps render passes for at least <paramref name="ms"/> of wall-clock time.</summary>
    private static async Task PumpFor(int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
            await Harness.Render(PerPassMs);
    }

    private static async Task<bool> CommitAt(Rig rig, Probe p, double w, double h, int maxPasses = 60) =>
        await Harness.WaitFor(() => ProbeIs(rig.Box, w, h), maxPasses, PerPassMs);

    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// D-4 / DoD-2 — the first usable size commits without the debounce: Debounce = 10 s, and the
    /// probe must appear at the host size within ≤ 5 passes (a debounced first commit cannot).
    /// </summary>
    internal sealed class FirstCommitAtHostSize(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var p = new Probe();
            var opts = p.Options(TimeSpan.FromSeconds(10));
            var render = p.Render();
            var rig = Mount(H, _ => FitParent(render, opts));
            try
            {
                var ok = await CommitAt(rig, p, 400, 300, maxPasses: 5);
                H.Check("FitParent_FirstCommit_WithinFivePasses", ok, Describe(rig.Box, p));
                H.Check("FitParent_FirstCommit_RenderedOnceAtHostSize",
                    p.Renders == 1 && p.Sizes[0] == (400, 300), Describe(rig.Box, p));
                H.Check("FitParent_FirstCommit_HostMatchesProbe",
                    p.Host is { } host && Near(host.ActualWidth, 400) && Near(host.ActualHeight, 300), Describe(rig.Box, p));
                H.Check("FitParent_FirstCommit_NoTransform", IsCleared(p.Host), DescribeTransform(p.Host));
                H.Check("FitParent_FirstCommit_NoTimer", p.Box is { IsTimerRunning: false });
            }
            finally { await TearDown(H, rig); }
        }
    }

    /// <summary>
    /// Grows through several intermediate sizes with a render pass between steps and a 1 s
    /// debounce; nothing re-renders until the final size is committed once.
    /// </summary>
    internal sealed class GrowRebuildsAtFinalSize(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(45);

        public override async Task RunAsync()
        {
            var p = new Probe();
            var opts = p.Options(TimeSpan.FromSeconds(1));
            var render = p.Render();
            var rig = Mount(H, _ => FitParent(render, opts));
            try
            {
                H.Check("FitParent_Grow_InitialCommit", await CommitAt(rig, p, 400, 300), Describe(rig.Box, p));
                int before = p.Renders;
                int scBefore = p.Box?.SizeChangedCount ?? 0;

                var pending = await StepsStayPending(rig, p, before, (450, 330), (520, 400), (600, 450));
                H.Check("FitParent_Grow_SizeChangedObserved", (p.Box?.SizeChangedCount ?? 0) >= scBefore + 3);
                H.Check("FitParent_Grow_DebouncedBetweenSteps", pending, Describe(rig.Box, p));

                H.Check("FitParent_Grow_CommitsAtFinalSize", await CommitAt(rig, p, 600, 450, maxPasses: 200), Describe(rig.Box, p));
                await PumpFor(150);
                var after = p.Sizes.Skip(before).ToList();
                H.Check("FitParent_Grow_OnlyFinalSizeRendered",
                    after.Count == 1 && after[0] == (600, 450), Describe(rig.Box, p));
                H.Check("FitParent_Grow_TransformCleared", IsCleared(p.Host), DescribeTransform(p.Host));
                H.Check("FitParent_Grow_NoClipAfterCommit", p.Host is { Clip: null }, DescribeClip(p.Host));
            }
            finally { await TearDown(H, rig); }
        }
    }

    /// <summary>
    /// Shrinks through several intermediate sizes with a render pass between steps and a 1 s
    /// debounce; nothing re-renders until the final size is committed once.
    /// </summary>
    internal sealed class ShrinkRebuildsAtFinalSize(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(45);

        public override async Task RunAsync()
        {
            var p = new Probe();
            var opts = p.Options(TimeSpan.FromSeconds(1));
            var render = p.Render();
            var rig = Mount(H, _ => FitParent(render, opts));
            try
            {
                H.Check("FitParent_Shrink_InitialCommit", await CommitAt(rig, p, 400, 300), Describe(rig.Box, p));
                int before = p.Renders;

                var pending = await StepsStayPending(rig, p, before, (350, 260), (300, 230), (250, 200));
                H.Check("FitParent_Shrink_DebouncedBetweenSteps", pending, Describe(rig.Box, p));

                H.Check("FitParent_Shrink_CommitsAtFinalSize", await CommitAt(rig, p, 250, 200, maxPasses: 200), Describe(rig.Box, p));
                await PumpFor(150);
                var after = p.Sizes.Skip(before).ToList();
                H.Check("FitParent_Shrink_OnlyFinalSizeRendered",
                    after.Count == 1 && after[0] == (250, 200), Describe(rig.Box, p));
                H.Check("FitParent_Shrink_TransformCleared", IsCleared(p.Host), DescribeTransform(p.Host));
                H.Check("FitParent_Shrink_NoClipAfterCommit", p.Host is { Clip: null }, DescribeClip(p.Host));
            }
            finally { await TearDown(H, rig); }
        }
    }

    /// <summary>Width grows while height shrinks: the rebuild is at the exact non-uniform size.</summary>
    internal sealed class AsymmetricResize(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var p = new Probe();
            var opts = p.Options(Debounce60);
            var render = p.Render();
            var rig = Mount(H, _ => FitParent(render, opts));
            try
            {
                H.Check("FitParent_Asymmetric_InitialCommit", await CommitAt(rig, p, 400, 300), Describe(rig.Box, p));
                Resize(rig.Box, 700, 200);
                H.Check("FitParent_Asymmetric_CommitsAtExactSize", await CommitAt(rig, p, 700, 200), Describe(rig.Box, p));
                H.Check("FitParent_Asymmetric_LastRenderSize", p.Sizes[^1] == (700, 200), Describe(rig.Box, p));
                H.Check("FitParent_Asymmetric_TransformCleared", IsCleared(p.Host), DescribeTransform(p.Host));
            }
            finally { await TearDown(H, rig); }
        }
    }

    /// <summary>
    /// D-6 / D-7 — with Debounce = 2 s the host carries a transient transform before the commit
    /// and none after it.
    /// </summary>
    internal sealed class TransientTransformThenCleared(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(45);

        public override async Task RunAsync()
        {
            var p = new Probe();
            var opts = p.Options(TimeSpan.FromSeconds(2));
            var render = p.Render();
            var rig = Mount(H, _ => FitParent(render, opts));
            try
            {
                H.Check("FitParent_Transient_InitialCommit", await CommitAt(rig, p, 400, 300), Describe(rig.Box, p));
                int before = p.Renders;

                Resize(rig.Box, 600, 450);
                await Harness.Render();

                var transform = p.Host?.RenderTransform;
                H.Check("FitParent_Transient_TransformBeforeCommit", HasTransient(p.Host),
                    $"{DescribeTransform(p.Host)} {Describe(rig.Box, p)}");
                H.Check("FitParent_Transient_UniformScale",
                    transform is CompositeTransform ct && Near(ct.ScaleX, 1.5, 1e-6) && Near(ct.ScaleY, 1.5, 1e-6),
                    transform is CompositeTransform c2 ? $"scale={c2.ScaleX},{c2.ScaleY}" : "not CompositeTransform");
                H.Check("FitParent_Transient_NotYetRebuilt",
                    p.Renders == before && ProbeIs(rig.Box, 400, 300), Describe(rig.Box, p));
                H.Check("FitParent_Transient_TimerRunning", p.Box is { IsTimerRunning: true });
                // The min-scale transient already fits the host, so no clip is applied (a clip
                // under the host's own RenderTransform would be scaled with it).
                H.Check("FitParent_Transient_NoClipWhileScaled", p.Host is { Clip: null }, DescribeClip(p.Host));

                H.Check("FitParent_Transient_CommitsAfterDebounce",
                    await CommitAt(rig, p, 600, 450, maxPasses: 250), Describe(rig.Box, p));
                H.Check("FitParent_Transient_TransformClearedAfterCommit", IsCleared(p.Host),
                    DescribeTransform(p.Host));
            }
            finally { await TearDown(H, rig); }
        }
    }

    /// <summary>
    /// D-9 / DoD-3 — a +0.3 DIP resize (layout rounding off, fractional ActualWidth asserted) is
    /// observed by SizeChanged but does not rebuild.
    /// </summary>
    internal sealed class JitterDoesNotRebuild(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var p = new Probe();
            var opts = p.Options(Debounce60);
            var render = p.Render();
            var rig = Mount(H, _ => FitParent(render, opts));
            WinBorder? wrapper = null;
            WinBorder? host = null;
            bool wrapperRounding = true, hostRounding = true;
            try
            {
                H.Check("FitParent_Jitter_InitialCommit", await CommitAt(rig, p, 400, 300), Describe(rig.Box, p));
                host = p.Host;
                wrapper = host?.Parent as WinBorder;
                H.Check("FitParent_Jitter_HostAndWrapperFound", host is not null && wrapper is not null);
                if (host is null || wrapper is null) return;

                // Layout rounding off along the box → wrapper → host chain; the pooled wrapper
                // and host are restored in finally.
                rig.Box.UseLayoutRounding = false;
                wrapperRounding = wrapper.UseLayoutRounding;
                hostRounding = host.UseLayoutRounding;
                wrapper.UseLayoutRounding = false;
                host.UseLayoutRounding = false;
                Resize(rig.Box, 400, 300);
                await Harness.Render();

                int renders = p.Renders;
                int sc = p.Box!.SizeChangedCount;
                Resize(rig.Box, 400.3, 300);
                await Harness.Render();

                var aw = host.ActualWidth;
                H.Check("FitParent_Jitter_Precondition_FractionalActualWidth",
                    Math.Abs(aw - Math.Round(aw)) > 0.05 && Math.Abs(aw - 400.3) < 0.2,
                    $"host.ActualWidth={aw:R}");
                H.Check("FitParent_Jitter_SizeChangedObserved", p.Box.SizeChangedCount >= sc + 1,
                    $"SizeChangedCount {sc} -> {p.Box.SizeChangedCount}");

                await PumpFor(300);
                H.Check("FitParent_Jitter_NoRebuild", p.Renders == renders, Describe(rig.Box, p));
                H.Check("FitParent_Jitter_NoTimerNoTransform",
                    !p.Box.IsTimerRunning && IsCleared(host), $"timer={p.Box.IsTimerRunning} {DescribeTransform(host)}");
            }
            finally
            {
                if (wrapper is not null) wrapper.UseLayoutRounding = wrapperRounding;
                if (host is not null) host.UseLayoutRounding = hostRounding;
                await TearDown(H, rig);
            }
        }
    }

    /// <summary>
    /// D-8 / DoD-4 — unmount mid-debounce stops the timer; a late Tick or SizeChanged is a no-op
    /// counted by <see cref="FitParentBox.LateTickCount"/>.
    /// </summary>
    internal sealed class UnmountMidDebounceSafe(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var p = new Probe();
            var opts = p.Options(TimeSpan.FromMilliseconds(300));
            var render = p.Render();
            bool show = true;
            var rig = Mount(H, _ => show ? FitParent(render, opts) : TextBlock("FitParentGone"));
            try
            {
                H.Check("FitParent_Unmount_InitialCommit", await CommitAt(rig, p, 400, 300), Describe(rig.Box, p));

                Resize(rig.Box, 500, 400);
                var box = p.Box;
                H.Check("FitParent_Unmount_PositiveControl_BoxCaptured", box is not null);
                if (box is null) return;
                H.Check("FitParent_Unmount_PositiveControl_NotDisposed", !box.Disposed);
                H.Check("FitParent_Unmount_PositiveControl_TimerRunning", box.IsTimerRunning);

                show = false;
                rig.Host.RequestRender();
                await Harness.Render();
                H.Check("FitParent_Unmount_Unmounted", H.FindText("FitParentGone") is not null || FindProbe(rig.Box) is null);
                H.Check("FitParent_Unmount_Disposed", box.Disposed);
                H.Check("FitParent_Unmount_TimerStopped", !box.IsTimerRunning);

                int renders = p.Renders;
                Exception? thrown = null;
                try
                {
                    box.InvokeTickForTest();
                    box.InvokeSizeChangedForTest(500, 400);
                }
                catch (Exception ex) { thrown = ex; }
                H.Check("FitParent_Unmount_LateCallsNoThrow", thrown is null, thrown?.ToString() ?? "");

                await PumpFor(450);
                H.Check("FitParent_Unmount_RenderCountUnchanged", p.Renders == renders, Describe(rig.Box, p));
                H.Check("FitParent_Unmount_LateTickCountIsTwo", box.LateTickCount == 2,
                    $"LateTickCount={box.LateTickCount}");
            }
            finally { await TearDown(H, rig); }
        }
    }

    /// <summary>
    /// Pool regression guard (not DoD-4): a host unmounted mid-resize with a transient transform
    /// (scale on) or a stale-content clip (scale off) is never handed out with that state: during
    /// the gap phase, before any second FitParent can commit, no pooled Border carries it, and a
    /// Border with the same state round-tripped through the pool comes back clean. The next
    /// FitParent's host carries neither.
    /// </summary>
    internal sealed class RecycledHostHasNoTransform(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Variant("Transform", scale: true);
            await Variant("Clip", scale: false);
        }

        private async Task Variant(string v, bool scale)
        {
            var p1 = new Probe();
            var p2 = new Probe();
            var opts1 = p1.Options(TimeSpan.FromSeconds(5), scale);
            var opts2 = p2.Options(TimeSpan.FromSeconds(5), scale);
            var render1 = p1.Render();
            var render2 = p2.Render();
            int phase = 0;
            // Removing a child from a panel's children pools it (RemoveChildWithExitTransition →
            // UnmountAndPool); replacing the root element by another type only unmounts, so the
            // gap phase is an empty Grid, not a different element.
            var rig = Mount(H, _ => Grid([GridSize.Star()], [GridSize.Star()], phase switch
            {
                0 => [FitParent(render1, opts1)],
                1 => [],
                _ => [FitParent(render2, opts2)],
            }));
            try
            {
                H.Check($"FitParent_Recycled{v}_InitialCommit", await CommitAt(rig, p1, 400, 300), Describe(rig.Box, p1));
                var oldHost = p1.Host;
                Resize(rig.Box, 600, 450);
                await Harness.Render();
                if (scale)
                    H.Check($"FitParent_Recycled{v}_PositiveControl_OldHostTransformed",
                        HasTransient(oldHost), DescribeTransform(oldHost));
                else
                    H.Check($"FitParent_Recycled{v}_PositiveControl_OldHostClipped",
                        ClipIsHostBounds(oldHost), DescribeClip(oldHost));

                phase = 1;
                rig.Host.RequestRender();
                await Harness.Render();
                // The old host must not be handed to another user carrying the stale transform or
                // clip. In this tree WinUI rejects it in ElementPool.ForceDetach (COMException
                // 0x800F1000 re-parenting a host that still holds its Canvas layer), so it is
                // dropped rather than pooled; if it ever is pooled, it must come back clean.
                var pool = rig.Host.Reconciler.Pool;
                var pooled = new List<FrameworkElement>();
                while (pool.TryRent(typeof(WinBorder)) is { } r) pooled.Add(r);
                var oldHostPooled = pooled.Any(b => ReferenceEquals(b, oldHost));
                Console.WriteLine($"# FitParent_Recycled{v}: old host pooled = {oldHostPooled}, pooled borders = {pooled.Count}");
                H.Check($"FitParent_Recycled{v}_GapPhase_OldHostDisposedBox", p1.Box is { Disposed: true } && oldHost is { Parent: null });
                H.Check($"FitParent_Recycled{v}_GapPhase_NoPooledBorderTransformedOrClipped",
                    pooled.All(b => IsCleared(b) && b.Clip is null),
                    $"oldHostPooled={oldHostPooled} {DescribeTransform(oldHost)} {DescribeClip(oldHost)}");

                // Direct round-trip through the same pool: a Border carrying exactly the state the
                // host carries mid-resize (transient transform, stale-content clip) comes back
                // clean. This is what reddens if CleanElement stops clearing either property.
                var probe = new WinBorder
                {
                    RenderTransform = new CompositeTransform { ScaleX = 1.5, ScaleY = 1.5 },
                    Clip = new RectangleGeometry { Rect = new Rect(0, 0, 600, 450) },
                };
                pool.Return(probe);
                var rented = pool.TryRent(typeof(WinBorder));
                H.Check($"FitParent_Recycled{v}_PoolRoundTrip_PositiveControl_SameInstance", ReferenceEquals(rented, probe));
                H.Check($"FitParent_Recycled{v}_PoolRoundTrip_TransformCleared", IsCleared(probe),
                    DescribeTransform(probe));
                H.Check($"FitParent_Recycled{v}_PoolRoundTrip_ClipCleared", probe.Clip is null, DescribeClip(probe));
                foreach (var b in pooled) pool.Return(b);

                phase = 2;
                rig.Host.RequestRender();

                H.Check($"FitParent_Recycled{v}_SecondCommit", await CommitAt(rig, p2, 600, 450), Describe(rig.Box, p2));
                var newHost = p2.Host;
                var recycled = ReferenceEquals(oldHost, newHost);
                Console.WriteLine($"# FitParent_Recycled{v}: host recycled as new host = {recycled}");
                H.Check($"FitParent_Recycled{v}_NewHostNoTransform", IsCleared(newHost), DescribeTransform(newHost));
                H.Check($"FitParent_Recycled{v}_NewHostNoClip", newHost is { Clip: null }, DescribeClip(newHost));
                var transformed = FindDescendant<WinBorder>(rig.Box, b => !IsCleared(b) || b.Clip is not null);
                H.Check($"FitParent_Recycled{v}_NoBorderInTreeTransformedOrClipped", transformed is null,
                    transformed is null ? "none" : $"{DescribeTransform(transformed)} {DescribeClip(transformed)}");
            }
            finally { await TearDown(H, rig); }
        }
    }

    /// <summary>
    /// D-3 — caller modifiers land on the wrapper Border; FitParent's own host Border and its
    /// SizeChanged handler keep working alongside the caller's.
    /// </summary>
    internal sealed class CallerModifiersOnWrapper(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var p = new Probe();
            var opts = p.Options(Debounce60);
            var render = p.Render();
            int callerCalls = 0;
            object? callerSender = null;
            var rig = Mount(H, _ => FitParent(render, opts)
                .Margin(10)
                .OnSizeChanged((s, _) => { callerCalls++; callerSender = s; }));
            try
            {
                H.Check("FitParent_Modifiers_CommitsInsideMargin", await CommitAt(rig, p, 380, 280), Describe(rig.Box, p));
                var host = p.Host;
                var wrapper = host?.Parent as WinBorder;
                H.Check("FitParent_Modifiers_WrapperHasMargin",
                    wrapper is not null && wrapper.Margin == new Thickness(10));
                H.Check("FitParent_Modifiers_HostHasNoMargin",
                    host is not null && host.Margin == new Thickness(0));
                H.Check("FitParent_Modifiers_CallerSizeChangedOnWrapper",
                    callerCalls >= 1 && ReferenceEquals(callerSender, wrapper), $"calls={callerCalls}");
                H.Check("FitParent_Modifiers_FitParentSizeChangedStillWired", p.Box is { SizeChangedCount: >= 1 });

                Resize(rig.Box, 500, 400);
                H.Check("FitParent_Modifiers_ResizeFollows", await CommitAt(rig, p, 480, 380), Describe(rig.Box, p));
            }
            finally { await TearDown(H, rig); }
        }
    }

    /// <summary>
    /// D-11 / §0.5 — an unsized axis keeps FitParent an empty placeholder (0 renders over 1 s);
    /// giving that axis a size (.Height(240)) commits exactly once at h = 240. A FitParent that is
    /// the direct content of a <c>ScrollViewer</c> or <c>ScrollView</c> is not unsized: both
    /// arrange it at the viewport, so it commits exactly once there. Wrapped in a <c>VStack</c>
    /// inside either scroller, it measures 0 tall and stays empty.
    /// </summary>
    internal sealed class UnsizedParentStaysEmpty(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(45);

        public override async Task RunAsync()
        {
            Slice[] data = [new("a", 3), new("b", 5), new("c", 2)];

            await CommitsOnceAtViewport("ScrollViewer", p => ScrollViewer(FitParent(p.Render(), p.Options(Debounce60))), 400, 300);
            await Unsized("ScrollViewerVStack", p => ScrollViewer(VStack(TextBlock("t"), FitParent(p.Render(), p.Options(Debounce60)))));
            await CommitsOnceAtViewport("ScrollView", p => ScrollView(FitParent(p.Render(), p.Options(Debounce60))), 400, 300);
            await Unsized("ScrollViewVStack", p => ScrollView(VStack(TextBlock("t"), FitParent(p.Render(), p.Options(Debounce60)))));
            await Unsized("VStack", p => VStack(TextBlock("t"), FitParent(p.Render(), p.Options(Debounce60))));
            await Unsized("PieAlternateView", p => VStack(TextBlock("t"), FitParent(p.Render((w, hh) =>
                PieChart(data, d => d.V, d => d.L).AlternateView(TextBlock("alt")).Width(w).Height(hh)),
                p.Options(Debounce60))));

            // The documented fix: give the unsized axis a size.
            var sized = new Probe();
            var sizedRender = sized.Render();
            var sizedOpts = sized.Options(Debounce60);
            var rig = Mount(H, _ => VStack(TextBlock("t"), FitParent(sizedRender, sizedOpts).Height(240)));
            try
            {
                H.Check("FitParent_Unsized_Height240_Commits",
                    await CommitAt(rig, sized, 400, 240), Describe(rig.Box, sized));
                await PumpFor(300);
                H.Check("FitParent_Unsized_Height240_ExactlyOnce",
                    sized.Renders == 1 && sized.Sizes[0] == (400, 240), Describe(rig.Box, sized));
            }
            finally { await TearDown(H, rig); }
        }

        private async Task Unsized(string variant, Func<Probe, Element> build)
        {
            var p = new Probe();
            var element = build(p);
            Exception? thrown = null;
            var rig = Mount(H, _ => element);
            try
            {
                try { await PumpFor(1000); }
                catch (Exception ex) { thrown = ex; }
                Console.WriteLine($"# FitParent_Unsized_{variant}: {Describe(rig.Box, p)}");
                H.Check($"FitParent_Unsized_{variant}_PositiveControl_Mounted", p.Host is not null);
                H.Check($"FitParent_Unsized_{variant}_NoRenders", p.Renders == 0, Describe(rig.Box, p));
                H.Check($"FitParent_Unsized_{variant}_NoException", thrown is null, thrown?.ToString() ?? "");
                H.Check($"FitParent_Unsized_{variant}_PlaceholderChildNull", p.Host is { Child: null });
            }
            finally { await TearDown(H, rig); }
        }

        /// <summary>
        /// A scroller that arranges its direct content at the viewport size gives FitParent a
        /// finite size: it commits once at the viewport and then stays put (no feedback loop).
        /// </summary>
        private async Task CommitsOnceAtViewport(string variant, Func<Probe, Element> build, double w, double h)
        {
            var p = new Probe();
            var element = build(p);
            Exception? thrown = null;
            var rig = Mount(H, _ => element);
            try
            {
                bool committed = false;
                int rendersAtCommit = -1;
                try
                {
                    committed = await CommitAt(rig, p, w, h);
                    rendersAtCommit = p.Renders;
                    await PumpFor(1000);
                }
                catch (Exception ex) { thrown = ex; }
                Console.WriteLine($"# FitParent_Viewport_{variant}: {Describe(rig.Box, p)}");
                H.Check($"FitParent_Viewport_{variant}_CommitsAtViewport", committed, Describe(rig.Box, p));
                H.Check($"FitParent_Viewport_{variant}_ExactlyOneRender",
                    p.Renders == 1 && p.Sizes[0] == (w, h), Describe(rig.Box, p));
                H.Check($"FitParent_Viewport_{variant}_NoFurtherRendersAfterSettle",
                    rendersAtCommit == 1 && p.Renders == rendersAtCommit, $"atCommit={rendersAtCommit} {Describe(rig.Box, p)}");
                H.Check($"FitParent_Viewport_{variant}_CommittedStable",
                    p.Boxes.Count == 1 && p.Box is { } b && b.Committed == (w, h) && !b.IsTimerRunning,
                    $"boxes={p.Boxes.Count} committed={p.Box?.Committed}");
                H.Check($"FitParent_Viewport_{variant}_StillAtViewport", ProbeIs(rig.Box, w, h), Describe(rig.Box, p));
                H.Check($"FitParent_Viewport_{variant}_NoException", thrown is null, thrown?.ToString() ?? "");
            }
            finally { await TearDown(H, rig); }
        }
    }

    /// <summary>
    /// D-4 (A4) — changing the FitParent's key without resizing remounts it into a same-size
    /// slot; the new instance must still commit. Measured: WinUI raises SizeChanged on the
    /// freshly mounted host before Loaded, so the natural remount commits through SizeChanged
    /// (logged, not asserted as a Loaded commit). A second remount ignores SizeChanged through
    /// the <see cref="FitParentBox.IgnoreSizeChangedForTest"/> seam, so only the Loaded fallback
    /// can commit it — that is the assertion that proves the fallback.
    /// </summary>
    internal sealed class KeyedRemountSameSizeCommits(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var p = new Probe();
            bool ignoreSizeChanged = false;
            var opts = new FitParentOptions
            {
                Debounce = Debounce60,
                TestBoxCreated = b =>
                {
                    b.IgnoreSizeChangedForTest = ignoreSizeChanged;
                    p.Boxes.Add(b);
                },
            };
            var render = p.Render();
            string key = "a";
            var rig = Mount(H, _ => Border(FitParent(render, opts).WithKey(key)));
            try
            {
                H.Check("FitParent_KeyedRemount_InitialCommit", await CommitAt(rig, p, 400, 300), Describe(rig.Box, p));
                var oldBox = p.Box;
                int renders = p.Renders;

                key = "b";
                rig.Host.RequestRender();
                var ok = await Harness.WaitFor(
                    () => p.Boxes.Count >= 2 && p.Renders > renders && ProbeIs(rig.Box, 400, 300),
                    maxPasses: 10, PerPassMs);

                H.Check("FitParent_KeyedRemount_NewInstance",
                    p.Boxes.Count >= 2 && !ReferenceEquals(oldBox, p.Box) && oldBox is { Disposed: true },
                    $"boxes={p.Boxes.Count}");
                H.Check("FitParent_KeyedRemount_CommitsAtSameSize", ok, Describe(rig.Box, p));
                H.Check("FitParent_KeyedRemount_RenderedAtSameSize",
                    p.Renders == renders + 1 && p.Sizes[^1] == (400, 300), Describe(rig.Box, p));
                var nb = p.Box!;
                Console.WriteLine($"# FitParent_KeyedRemount natural: SizeChangedCount={nb.SizeChangedCount} "
                    + $"LoadedCommitCount={nb.LoadedCommitCount} SizeChangedCountAtLoadedCommit={nb.SizeChangedCountAtLoadedCommit}");

                // Forced Loaded path: the next instance ignores SizeChanged.
                ignoreSizeChanged = true;
                var natural = p.Box;
                renders = p.Renders;
                key = "c";
                rig.Host.RequestRender();
                var forced = await Harness.WaitFor(
                    () => p.Boxes.Count >= 3 && p.Renders > renders && ProbeIs(rig.Box, 400, 300),
                    maxPasses: 10, PerPassMs);
                var fb = p.Box!;
                Console.WriteLine($"# FitParent_KeyedRemount forced: SizeChangedCount={fb.SizeChangedCount} "
                    + $"LoadedCommitCount={fb.LoadedCommitCount} SizeChangedCountAtLoadedCommit={fb.SizeChangedCountAtLoadedCommit}");
                H.Check("FitParent_KeyedRemount_Forced_NewInstanceIgnoresSizeChanged",
                    p.Boxes.Count >= 3 && !ReferenceEquals(natural, fb) && fb.IgnoreSizeChangedForTest,
                    $"boxes={p.Boxes.Count}");
                H.Check("FitParent_KeyedRemount_Forced_LoadedFallbackCommitted",
                    forced && fb.LoadedCommitCount == 1 && p.Renders == renders + 1 && p.Sizes[^1] == (400, 300)
                        && fb.Committed == (400, 300),
                    $"LoadedCommitCount={fb.LoadedCommitCount} {Describe(rig.Box, p)}");
            }
            finally { await TearDown(H, rig); }
        }
    }

    /// <summary>
    /// D-4 (R2-2) — a hot-reload hook reset updates (not mounts) the host Border; the
    /// OnHostUpdated fallback must commit again at the same size within ≤ 5 passes. A second
    /// reset while a resize transient is active must not leave the old transform on the
    /// retained host.
    /// </summary>
    internal sealed class HotReloadResetCommits(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(45);

        public override async Task RunAsync()
        {
            var p = new Probe();
            var current = p.Options(Debounce60);
            var render = p.Render();
            var rig = Mount(H, _ => FitParent(render, current));
            try
            {
                H.Check("FitParent_HotReload_InitialCommit", await CommitAt(rig, p, 400, 300), Describe(rig.Box, p));
                var oldBox = p.Box;
                var oldHost = p.Host;
                int renders = p.Renders;

                rig.Host.Reconciler.ForEachLiveContext(c => c.ResetForHotReload());
                rig.Host.RequestRender(force: true);

                var ok = await Harness.WaitFor(
                    () => p.Boxes.Count >= 2 && p.Renders > renders && ProbeIs(rig.Box, 400, 300),
                    maxPasses: 5, PerPassMs);

                H.Check("FitParent_HotReload_PositiveControl_HooksReset",
                    p.Boxes.Count >= 2 && oldBox is { Disposed: true }, $"boxes={p.Boxes.Count}");
                H.Check("FitParent_HotReload_HostUpdatedNotRemounted", ReferenceEquals(oldHost, p.Host));
                H.Check("FitParent_HotReload_CommitsWithinFivePasses", ok, Describe(rig.Box, p));
                H.Check("FitParent_HotReload_RenderedOnceAtSameSize",
                    p.Renders == renders + 1 && p.Sizes[^1] == (400, 300), Describe(rig.Box, p));

                // Reset while a transient is active: the old box is disposed with its transform
                // still on the retained host, and the new box never knew about it.
                current = p.Options(TimeSpan.FromSeconds(10));
                rig.Host.RequestRender();
                await Harness.Render();
                Resize(rig.Box, 600, 450);
                await Harness.Render();
                var pendingBox = p.Box;
                H.Check("FitParent_HotReload_Transient_PositiveControl_PendingWithTransient",
                    pendingBox is { IsTimerRunning: true } && HasTransient(p.Host) && ProbeIs(rig.Box, 400, 300),
                    $"{DescribeTransform(p.Host)} {Describe(rig.Box, p)}");

                int boxes = p.Boxes.Count;
                rig.Host.Reconciler.ForEachLiveContext(c => c.ResetForHotReload());
                rig.Host.RequestRender(force: true);
                var rebuilt = await Harness.WaitFor(
                    () => p.Boxes.Count > boxes && ProbeIs(rig.Box, 600, 450), maxPasses: 5, PerPassMs);
                H.Check("FitParent_HotReload_Transient_HooksResetHostKept",
                    p.Boxes.Count > boxes && pendingBox is { Disposed: true } && ReferenceEquals(oldHost, p.Host),
                    $"boxes={boxes}->{p.Boxes.Count}");
                H.Check("FitParent_HotReload_Transient_CommitsAtHostSize", rebuilt, Describe(rig.Box, p));
                H.Check("FitParent_HotReload_Transient_TransformCleared", IsCleared(p.Host), DescribeTransform(p.Host));
                H.Check("FitParent_HotReload_Transient_NoClipNoTimer",
                    p.Host is { Clip: null } && p.Box is { IsTimerRunning: false }, DescribeClip(p.Host));
            }
            finally { await TearDown(H, rig); }
        }
    }

    /// <summary>
    /// R2-1 — content that fills its slot (Flex grow) does not lock the size: grow 400×300 →
    /// 600×450, then shrink back to 400×300.
    /// </summary>
    internal sealed class FillingContentGrows(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var p = new Probe();
            var opts = p.Options(Debounce60);
            var render = p.Render((w, hh) => FlexColumn(
                TextBlock("title"),
                D3Canvas(w, hh - 40).AutomationName(Name(w, hh)).Flex(grow: 1)));
            var rig = Mount(H, _ => FitParent(render, opts));
            try
            {
                H.Check("FitParent_Filling_InitialCommit", await Committed(rig, p, 400, 300), Describe(rig.Box, p));
                Resize(rig.Box, 600, 450);
                H.Check("FitParent_Filling_Grows", await Committed(rig, p, 600, 450), Describe(rig.Box, p));
                Resize(rig.Box, 400, 300);
                H.Check("FitParent_Filling_ShrinksBack", await Committed(rig, p, 400, 300), Describe(rig.Box, p));
                H.Check("FitParent_Filling_HostFollowsParent",
                    p.Host is { } host && Near(host.ActualWidth, 400) && Near(host.ActualHeight, 300), Describe(rig.Box, p));
            }
            finally { await TearDown(H, rig); }
        }

        private static Task<bool> Committed(Rig rig, Probe p, double w, double hh) =>
            Harness.WaitFor(() =>
                p.Sizes.Count > 0 && p.Sizes[^1] == (w, hh)
                && FindDescendant<WinCanvas>(rig.Box, c => AutomationProperties.GetName(c) == Name(w, hh)) is { } c
                && Near(c.Width, w) && Near(c.Height, hh - 40),
                60, PerPassMs);
    }

    /// <summary>A5 — under Flex(grow: 1, basis: 0) in a Flex row, the committed size follows a shrink.</summary>
    internal sealed class ShrinkInFlexParent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var p = new Probe();
            var opts = p.Options(Debounce60);
            var render = p.Render();
            var rig = Mount(H, _ => FlexRow(
                FitParent(render, opts).Flex(grow: 1, basis: 0),
                TextBlock("side").Width(50)));
            try
            {
                H.Check("FitParent_FlexShrink_InitialCommit", await CommitAt(rig, p, 350, 300), Describe(rig.Box, p));
                Resize(rig.Box, 300, 200);
                H.Check("FitParent_FlexShrink_Follows", await CommitAt(rig, p, 250, 200), Describe(rig.Box, p));
                Resize(rig.Box, 500, 350);
                H.Check("FitParent_FlexShrink_GrowsAgain", await CommitAt(rig, p, 450, 350), Describe(rig.Box, p));
            }
            finally { await TearDown(H, rig); }
        }
    }

    /// <summary>A5 — in a Grid * track, the committed size follows a shrink.</summary>
    internal sealed class ShrinkInGridStar(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var p = new Probe();
            var opts = p.Options(Debounce60);
            var render = p.Render();
            var rig = Mount(H, _ => Grid(
                [GridSize.Star(), GridSize.Px(50)],
                [GridSize.Star()],
                FitParent(render, opts).Grid(row: 0, column: 0),
                TextBlock("side").Grid(row: 0, column: 1)));
            try
            {
                H.Check("FitParent_GridShrink_InitialCommit", await CommitAt(rig, p, 350, 300), Describe(rig.Box, p));
                Resize(rig.Box, 300, 200);
                H.Check("FitParent_GridShrink_Follows", await CommitAt(rig, p, 250, 200), Describe(rig.Box, p));
                Resize(rig.Box, 500, 350);
                H.Check("FitParent_GridShrink_GrowsAgain", await CommitAt(rig, p, 450, 350), Describe(rig.Box, p));
            }
            finally { await TearDown(H, rig); }
        }
    }

    // ── Charts (DoD-5) ─────────────────────────────────────────────────

    private sealed record Pt(double X, double Y);
    private sealed record Slice(string L, double V);
    private sealed record Node(string Name, Node[] Children);

    /// <summary>Mounts a chart's .FitParent(), then checks its plot canvas equals the host size at 400×300 and 600×450.</summary>
    private static async Task ChartFollowsHost(Harness h, string name, Func<FitParentOptions, Element> chart)
    {
        var boxes = new List<FitParentBox>();
        var opts = new FitParentOptions { Debounce = Debounce60, TestBoxCreated = boxes.Add };
        var rig = Mount(h, _ => chart(opts));
        try
        {
            foreach (var (w, hh) in new[] { (400.0, 300.0), (600.0, 450.0) })
            {
                Resize(rig.Box, w, hh);
                WinCanvas? canvas = null;
                var ok = await Harness.WaitFor(() =>
                {
                    var host = boxes.Count > 0 ? boxes[^1].Host?.Current : null;
                    canvas = FindSizedCanvas(host);
                    return host is not null && canvas is not null
                        && Near(host.ActualWidth, w) && Near(host.ActualHeight, hh)
                        && Near(canvas.Width, host.ActualWidth) && Near(canvas.Height, host.ActualHeight);
                }, 60, PerPassMs);
                var hostNow = boxes.Count > 0 ? boxes[^1].Host?.Current : null;
                h.Check($"FitParent_{name}_CanvasEqualsHost_{w:0}x{hh:0}", ok,
                    $"canvas={(canvas is null ? "none" : $"{canvas.Width}x{canvas.Height}")} "
                    + $"host={(hostNow is null ? "null" : $"{hostNow.ActualWidth}x{hostNow.ActualHeight}")}");
            }
            h.Check($"FitParent_{name}_TransformCleared", boxes.Count > 0 && IsCleared(boxes[^1].Host?.Current),
                $"boxes={boxes.Count} {(boxes.Count > 0 ? DescribeTransform(boxes[^1].Host?.Current) : "")}");
        }
        finally { await TearDown(h, rig); }
    }

    internal sealed class PieChartFits(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            Slice[] data = [new("a", 3), new("b", 5), new("c", 2)];
            return ChartFollowsHost(H, "PieChart", o => Charts.PieChart(data, d => d.V, d => d.L).FitParent(o));
        }
    }

    internal sealed class LineChartFits(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            var data = Enumerable.Range(0, 10).Select(i => new Pt(i, Math.Sin(i * 0.5) * 50 + 50)).ToArray();
            return ChartFollowsHost(H, "LineChart", o => Charts.LineChart(data, d => d.X, d => d.Y).FitParent(o));
        }
    }

    internal sealed class TreeChartFits(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            var root = new Node("root",
            [
                new Node("a", [new Node("a1", []), new Node("a2", [])]),
                new Node("b", [new Node("b1", [])]),
            ]);
            return ChartFollowsHost(H, "TreeChart", o => Charts.TreeChart(root, n => n.Children, n => n.Name).FitParent(o));
        }
    }

    /// <summary>
    /// D-10 (A2, R2-8) — a stable ForceGraph builder (UseMemo) re-centres its free nodes by
    /// (Δw/2, Δh/2) on a resize commit and keeps the same simulation instance.
    /// </summary>
    internal sealed class ForceGraphRecenters(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var nodes = Enumerable.Range(0, 6).Select(i => new ForceNode { Label = $"n{i}" }).ToList();
            var links = new List<ForceLink> { new(0, 1), new(1, 2), new(2, 3), new(3, 4), new(4, 5), new(5, 0) };
            var handles = new List<ForceGraphHandle>();
            var boxes = new List<FitParentBox>();
            var opts = new FitParentOptions { Debounce = Debounce60, TestBoxCreated = boxes.Add };
            ForceGraphElement? builder = null;
            int rootRenders = 0, builderMismatches = 0;

            var rig = Mount(H, ctx =>
            {
                // OnReady only captures the handle; it never ticks the simulation.
                var graph = ctx.UseMemo(() => ForceGraph(nodes, links).OnReady(handles.Add), Array.Empty<object>());
                rootRenders++;
                builder ??= graph;
                if (!ReferenceEquals(builder, graph))
                    builderMismatches++;
                return graph.FitParent(opts);
            });
            try
            {
                bool At(double w, double hh) => handles.Count > 0
                    && Near(handles[^1].Canvas.Width, w) && Near(handles[^1].Canvas.Height, hh);

                H.Check("FitParent_ForceGraph_InitialCommit", await Harness.WaitFor(() => At(400, 300), 60, PerPassMs),
                    $"handles={handles.Count}");
                if (handles.Count == 0) return;
                var first = handles[^1];
                var sim = first.Simulation;
                var free0 = sim.Nodes.Where(n => n.Fx is null && n.Fy is null).ToList();
                H.Check("FitParent_ForceGraph_PositiveControl_FreeNodes", free0.Count == nodes.Count);
                double mx0 = free0.Average(n => n.X), my0 = free0.Average(n => n.Y);

                Resize(rig.Box, 600, 450);
                H.Check("FitParent_ForceGraph_ResizeCommit", await Harness.WaitFor(() => At(600, 450), 60, PerPassMs),
                    $"handles={handles.Count}");

                var last = handles[^1];
                H.Check("FitParent_ForceGraph_SameSimulation", ReferenceEquals(last.Simulation, sim));
                H.Check("FitParent_ForceGraph_StableBuilder", rootRenders >= 2 && builderMismatches == 0,
                    $"rootRenders={rootRenders} mismatches={builderMismatches}");
                var free1 = last.Simulation.Nodes.Where(n => n.Fx is null && n.Fy is null).ToList();
                double dx = free1.Average(n => n.X) - mx0, dy = free1.Average(n => n.Y) - my0;
                H.Check("FitParent_ForceGraph_MeanXShiftedByHalfDeltaW", Near(dx, 100, 0.5), $"dx={dx:R}");
                H.Check("FitParent_ForceGraph_MeanYShiftedByHalfDeltaH", Near(dy, 75, 0.5), $"dy={dy:R}");
            }
            finally { await TearDown(H, rig); }
        }
    }

    // ── Review of #1476: ScaleDuringResize = false, stale-content clip, timers ──

    /// <summary>
    /// ScaleDuringResize = false: before the commit the host carries no transient, the debounce
    /// is pending and the old content is shown unscaled (clipped to the host); after the debounce
    /// it commits once at the new size with no transform and no clip.
    /// </summary>
    internal sealed class NoScaleDuringResize(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(45);

        public override async Task RunAsync()
        {
            var p = new Probe();
            var opts = p.Options(TimeSpan.FromSeconds(2), scale: false);
            var render = p.Render();
            var rig = Mount(H, _ => FitParent(render, opts));
            try
            {
                H.Check("FitParent_NoScale_InitialCommit", await CommitAt(rig, p, 400, 300), Describe(rig.Box, p));
                int before = p.Renders;
                int sc = p.Box?.SizeChangedCount ?? 0;

                Resize(rig.Box, 600, 450);
                await Harness.Render();
                H.Check("FitParent_NoScale_PositiveControl_SizeChangedObserved", (p.Box?.SizeChangedCount ?? 0) > sc);
                H.Check("FitParent_NoScale_NoTransformBeforeCommit", IsCleared(p.Host),
                    DescribeTransform(p.Host));
                H.Check("FitParent_NoScale_TimerRunning", p.Box is { IsTimerRunning: true });
                H.Check("FitParent_NoScale_NotYetRebuilt", p.Renders == before && ProbeIs(rig.Box, 400, 300), Describe(rig.Box, p));
                H.Check("FitParent_NoScale_ClippedToHost",
                    ClipIsHostBounds(p.Host) && p.Host is { } host && Near(host.ActualWidth, 600) && Near(host.ActualHeight, 450),
                    DescribeClip(p.Host));

                H.Check("FitParent_NoScale_CommitsAfterDebounce",
                    await CommitAt(rig, p, 600, 450, maxPasses: 250), Describe(rig.Box, p));
                H.Check("FitParent_NoScale_RenderedOnceAtFinalSize",
                    p.Renders == before + 1 && p.Sizes[^1] == (600, 450), Describe(rig.Box, p));
                H.Check("FitParent_NoScale_ClearedAfterCommit",
                    IsCleared(p.Host) && p.Host is { Clip: null } && p.Box is { IsTimerRunning: false }, DescribeClip(p.Host));
            }
            finally { await TearDown(H, rig); }
        }
    }

    /// <summary>
    /// ScaleDuringResize = false and a shrink: the old, larger content stays unscaled until the
    /// commit, so it is clipped to exactly the host's arranged bounds instead of spilling.
    /// </summary>
    internal sealed class NoScaleShrinkClipped(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(45);

        public override async Task RunAsync()
        {
            var p = new Probe();
            var opts = p.Options(TimeSpan.FromSeconds(2), scale: false);
            var render = p.Render();
            var rig = Mount(H, _ => FitParent(render, opts));
            try
            {
                H.Check("FitParent_NoScaleShrink_InitialCommit", await CommitAt(rig, p, 400, 300), Describe(rig.Box, p));
                H.Check("FitParent_NoScaleShrink_NoClipAtSteadyState", p.Host is { Clip: null }, DescribeClip(p.Host));
                int before = p.Renders;

                Resize(rig.Box, 250, 200);
                await Harness.Render();
                H.Check("FitParent_NoScaleShrink_PositiveControl_StaleContentLarger",
                    p.Renders == before && ProbeIs(rig.Box, 400, 300) && p.Host is { } host
                        && Near(host.ActualWidth, 250) && Near(host.ActualHeight, 200),
                    Describe(rig.Box, p));
                H.Check("FitParent_NoScaleShrink_ClipEqualsHostBounds", ClipIsHostBounds(p.Host), DescribeClip(p.Host));
                H.Check("FitParent_NoScaleShrink_Unscaled", IsCleared(p.Host), DescribeTransform(p.Host));

                H.Check("FitParent_NoScaleShrink_CommitsAfterDebounce",
                    await CommitAt(rig, p, 250, 200, maxPasses: 250), Describe(rig.Box, p));
                H.Check("FitParent_NoScaleShrink_ClipClearedAfterCommit", p.Host is { Clip: null }, DescribeClip(p.Host));
            }
            finally { await TearDown(H, rig); }
        }
    }

    /// <summary>
    /// A host that shrinks to zero on one axis after a commit cannot commit, so the stale content
    /// is hidden behind a zero-area clip equal to the host bounds (rather than drawing at full
    /// size over siblings), any pending rebuild and transient are dropped, and growing back
    /// restores it — without a render when the host returns to the committed size.
    /// </summary>
    internal sealed class ShrinkToZeroHidesStaleContent(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(45);

        public override async Task RunAsync()
        {
            var p = new Probe();
            var opts = p.Options(TimeSpan.FromSeconds(2));
            var render = p.Render();
            var rig = Mount(H, _ => FitParent(render, opts));
            try
            {
                H.Check("FitParent_ShrinkToZero_InitialCommit", await CommitAt(rig, p, 400, 300), Describe(rig.Box, p));
                int before = p.Renders;

                // Steady state, no timer: width → 0.
                int sc = p.Box!.SizeChangedCount;
                Resize(rig.Box, 0, 300);
                await Harness.Render();
                H.Check("FitParent_ShrinkToZero_Precondition_HostZeroWidth",
                    p.Box.SizeChangedCount > sc && p.Host is { ActualWidth: 0 } && FindProbe(rig.Box) is not null,
                    $"SizeChanged {sc}->{p.Box.SizeChangedCount} {Describe(rig.Box, p)}");
                H.Check("FitParent_ShrinkToZero_Idle_ZeroAreaClipAtHostBounds",
                    ClipIsHostBounds(p.Host) && ClipRect(p.Host) is { } r0 && r0.Width * r0.Height == 0,
                    DescribeClip(p.Host));
                await PumpFor(200);
                H.Check("FitParent_ShrinkToZero_Idle_NoRenderNoTimer",
                    p.Renders == before && !p.Box.IsTimerRunning && IsCleared(p.Host), Describe(rig.Box, p));
                H.Check("FitParent_ShrinkToZero_Idle_StillHidden", ClipIsHostBounds(p.Host), DescribeClip(p.Host));

                Resize(rig.Box, 400, 300);
                await Harness.Render();
                H.Check("FitParent_ShrinkToZero_BackToCommittedSize_Unclipped",
                    p.Host is { Clip: null } && p.Renders == before && !p.Box.IsTimerRunning,
                    $"{DescribeClip(p.Host)} {Describe(rig.Box, p)}");

                // Mid-debounce: a pending grow, then height → 0.
                Resize(rig.Box, 600, 450);
                await Harness.Render();
                H.Check("FitParent_ShrinkToZero_PositiveControl_PendingWithTransient",
                    p.Box.IsTimerRunning && HasTransient(p.Host), $"{DescribeTransform(p.Host)} {Describe(rig.Box, p)}");
                Resize(rig.Box, 600, 0);
                await Harness.Render();
                H.Check("FitParent_ShrinkToZero_Pending_ZeroAreaClipAtHostBounds",
                    p.Host is { ActualHeight: 0 } && ClipIsHostBounds(p.Host)
                        && ClipRect(p.Host) is { } r1 && r1.Width * r1.Height == 0,
                    DescribeClip(p.Host));
                H.Check("FitParent_ShrinkToZero_Pending_TimerAndTransientDropped",
                    !p.Box.IsTimerRunning && IsCleared(p.Host), Describe(rig.Box, p));

                Resize(rig.Box, 500, 400);
                H.Check("FitParent_ShrinkToZero_RegrowCommits",
                    await CommitAt(rig, p, 500, 400, maxPasses: 250), Describe(rig.Box, p));
                H.Check("FitParent_ShrinkToZero_RegrowUnclipped",
                    p.Host is { Clip: null } && IsCleared(p.Host) && p.Renders == before + 1,
                    $"{DescribeClip(p.Host)} {Describe(rig.Box, p)}");

                // Queued commit, then a further usable size, then zero, all before the commit
                // renders (driven through the box seam so the interleaving is deterministic).
                // The unusable size must invalidate the pending 700×500: the landing 600×450
                // commit is final, with no timer re-armed for an obsolete size.
                var box = p.Box;
                box.InvokeSizeChangedForTest(600, 450);
                box.InvokeTickForTest();
                box.InvokeSizeChangedForTest(700, 500);
                box.InvokeSizeChangedForTest(0, 500);
                H.Check("FitParent_ShrinkToZero_Queued_PositiveControl_CommitQueuedTimerStopped",
                    box.Committed == (500, 400) && !box.IsTimerRunning,
                    $"committed={box.Committed} timer={box.IsTimerRunning}");
                var landed = await Harness.WaitFor(() => box.Committed == (600, 450), 5, PerPassMs);
                H.Check("FitParent_ShrinkToZero_Queued_CommitLanded", landed, Describe(rig.Box, p));
                H.Check("FitParent_ShrinkToZero_Queued_NoTimerNoTransientForObsoleteSize",
                    !box.IsTimerRunning && IsCleared(p.Host),
                    $"timer={box.IsTimerRunning} {DescribeTransform(p.Host)}");
            }
            finally { await TearDown(H, rig); }
        }
    }

    /// <summary>
    /// A commit that lands while the debounce is running and already matches the pending size to
    /// within jitter stops the timer (AfterCommit), and a stray Tick then commits nothing
    /// (OnTick re-checks significance). Driven through the box seam so the interleaving is
    /// deterministic: Tick queues a commit at 600×450, then a +0.3 DIP SizeChanged restarts the
    /// debounce before that commit renders.
    /// </summary>
    internal sealed class InsignificantCommitStopsTimer(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var p = new Probe();
            var opts = p.Options(TimeSpan.FromSeconds(2));
            var render = p.Render();
            var rig = Mount(H, _ => FitParent(render, opts));
            try
            {
                H.Check("FitParent_InsignificantCommit_InitialCommit", await CommitAt(rig, p, 400, 300), Describe(rig.Box, p));
                var box = p.Box!;
                int before = p.Renders;

                box.InvokeSizeChangedForTest(600, 450);
                box.InvokeTickForTest();
                box.InvokeSizeChangedForTest(600.3, 450);
                H.Check("FitParent_InsignificantCommit_PositiveControl_NotYetCommittedTimerRunning",
                    box.Committed == (400, 300) && box.IsTimerRunning && p.Renders == before,
                    $"committed={box.Committed} timer={box.IsTimerRunning} renders={p.Renders}");

                var landed = await Harness.WaitFor(() => box.Committed == (600, 450), 5, PerPassMs);
                H.Check("FitParent_InsignificantCommit_CommitLanded",
                    landed && p.Renders == before + 1 && p.Sizes[^1] == (600, 450), Describe(rig.Box, p));
                H.Check("FitParent_InsignificantCommit_TimerStopped", !box.IsTimerRunning);

                box.InvokeTickForTest();
                await Harness.Render();
                await Harness.Render();
                H.Check("FitParent_InsignificantCommit_LateTickNoCommit",
                    p.Renders == before + 1 && box.Committed == (600, 450), Describe(rig.Box, p));
            }
            finally { await TearDown(H, rig); }
        }
    }

    /// <summary>
    /// Debounce &lt;= 0 commits every usable change immediately: no timer and no transient on the
    /// resize (SizeChanged runs synchronously inside UpdateLayout), the commit after one pass.
    /// Then the AfterCommit arm: a resize that continued while a debounced commit was queued is
    /// committed straight away once the options say Debounce &lt;= 0.
    /// </summary>
    internal sealed class ImmediateDebounceCommits(Harness h, TimeSpan debounce, string label) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            string n = $"FitParent_Debounce{label}";
            var p = new Probe();
            var immediate = p.Options(debounce);
            var current = immediate;
            var render = p.Render();
            var rig = Mount(H, _ => FitParent(render, current));
            try
            {
                H.Check($"{n}_InitialCommit", await CommitAt(rig, p, 400, 300), Describe(rig.Box, p));
                var box = p.Box!;
                int before = p.Renders;
                int sc = box.SizeChangedCount;

                Resize(rig.Box, 600, 450);
                H.Check($"{n}_PositiveControl_SizeChangedSynchronous", box.SizeChangedCount > sc,
                    $"SizeChanged {sc}->{box.SizeChangedCount}");
                H.Check($"{n}_NoTimerNoTransientOnResize", !box.IsTimerRunning && IsCleared(p.Host),
                    $"timer={box.IsTimerRunning} {DescribeTransform(p.Host)}");

                await Harness.Render();
                H.Check($"{n}_CommitsAfterOneRender",
                    ProbeIs(rig.Box, 600, 450) && p.Renders == before + 1, Describe(rig.Box, p));
                H.Check($"{n}_NoTimerNoTransformNoClip",
                    !box.IsTimerRunning && IsCleared(p.Host) && p.Host is { Clip: null }, DescribeClip(p.Host));

                // AfterCommit arm: queue a debounced commit at 700×500, let the resize continue
                // to 800×550, and render the commit under the immediate options.
                current = p.Options(TimeSpan.FromSeconds(2));
                rig.Host.RequestRender();
                await Harness.Render();
                int mid = p.Renders;
                box.InvokeSizeChangedForTest(700, 500);
                box.InvokeTickForTest();
                box.InvokeSizeChangedForTest(800, 550);
                H.Check($"{n}_AfterCommit_PositiveControl_TimerRunning", box.IsTimerRunning);
                current = immediate;
                rig.Host.RequestRender();
                var caught = await Harness.WaitFor(() => box.Committed == (800, 550), 5, PerPassMs);
                var tail = p.Sizes.Skip(mid).Distinct().ToList();
                H.Check($"{n}_AfterCommit_CommitsContinuedSize",
                    caught && tail.Contains((700, 500)) && tail[^1] == (800, 550),
                    $"committed={box.Committed} tail=[{string.Join(",", tail.Select(s => Name(s.W, s.H)))}]");
                H.Check($"{n}_AfterCommit_NoTimerNoTransform", !box.IsTimerRunning && IsCleared(p.Host),
                    $"timer={box.IsTimerRunning} {DescribeTransform(p.Host)}");
            }
            finally { await TearDown(H, rig); }
        }
    }

    // ── Review of #1476: options-only renders reconcile a pending resize ──

    /// <summary>
    /// A resize is pending behind a 10 s debounce; a render that changes only the options to
    /// Debounce = 0 commits the pending size within a few passes instead of waiting out the old
    /// timer, and leaves no timer, transient or clip behind.
    /// </summary>
    internal sealed class OptionsOnlyDebounceZeroCommits(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var p = new Probe();
            var current = p.Options(TimeSpan.FromSeconds(10));
            var render = p.Render();
            var rig = Mount(H, _ => FitParent(render, current));
            try
            {
                H.Check("FitParent_OptionsDebounceZero_InitialCommit", await CommitAt(rig, p, 400, 300), Describe(rig.Box, p));
                var box = p.Box!;
                int before = p.Renders;

                Resize(rig.Box, 600, 450);
                await Harness.Render();
                H.Check("FitParent_OptionsDebounceZero_PositiveControl_PendingWithTransient",
                    box.IsTimerRunning && HasTransient(p.Host) && p.Renders == before && ProbeIs(rig.Box, 400, 300),
                    $"{DescribeTransform(p.Host)} {Describe(rig.Box, p)}");

                current = p.Options(TimeSpan.Zero);
                rig.Host.RequestRender();
                var ok = await CommitAt(rig, p, 600, 450, maxPasses: 5);
                H.Check("FitParent_OptionsDebounceZero_CommitsWithinFivePasses",
                    ok && p.Sizes.Skip(before).Distinct().SequenceEqual([(400.0, 300.0), (600.0, 450.0)]),
                    Describe(rig.Box, p));
                H.Check("FitParent_OptionsDebounceZero_NoTimerNoTransformNoClip",
                    !box.IsTimerRunning && IsCleared(p.Host) && p.Host is { Clip: null },
                    $"timer={box.IsTimerRunning} {DescribeTransform(p.Host)} {DescribeClip(p.Host)}");
            }
            finally { await TearDown(H, rig); }
        }
    }

    /// <summary>
    /// A resize is pending with a transient scale; a render that changes only the options to
    /// ScaleDuringResize = false drops the transient straight away (the old content is shown
    /// unscaled, clipped to the host, as if scaling had been off from the start). The rebuild
    /// itself still waits for the debounce.
    /// </summary>
    internal sealed class OptionsOnlyScaleOffClearsTransient(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var p = new Probe();
            var current = p.Options(TimeSpan.FromSeconds(10));
            var render = p.Render();
            var rig = Mount(H, _ => FitParent(render, current));
            try
            {
                H.Check("FitParent_OptionsScaleOff_InitialCommit", await CommitAt(rig, p, 400, 300), Describe(rig.Box, p));
                var box = p.Box!;
                int before = p.Renders;

                Resize(rig.Box, 600, 450);
                await Harness.Render();
                H.Check("FitParent_OptionsScaleOff_PositiveControl_PendingWithTransient",
                    box.IsTimerRunning && HasTransient(p.Host), $"{DescribeTransform(p.Host)} {Describe(rig.Box, p)}");

                current = p.Options(TimeSpan.FromSeconds(10), scale: false);
                rig.Host.RequestRender();
                await Harness.Render();
                await Harness.Render();
                H.Check("FitParent_OptionsScaleOff_TransientCleared", IsCleared(p.Host), DescribeTransform(p.Host));
                H.Check("FitParent_OptionsScaleOff_ClippedToHost", ClipIsHostBounds(p.Host), DescribeClip(p.Host));
                // The options-only render re-runs render at the committed 400×300; nothing at 600×450 yet.
                H.Check("FitParent_OptionsScaleOff_StillPendingNotRebuilt",
                    box.IsTimerRunning && p.Sizes.Skip(before).All(s => s == (400, 300)) && ProbeIs(rig.Box, 400, 300),
                    Describe(rig.Box, p));
            }
            finally { await TearDown(H, rig); }
        }
    }
}
