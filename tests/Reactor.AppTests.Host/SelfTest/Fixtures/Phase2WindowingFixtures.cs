using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 054 Phase 2 fixtures for resize modes, aspect-locked sizing, and drag-from-background.
/// Aspect-ratio fixtures use ReactorWindow's internal WM_SIZING test seam so the mutable RECT
/// path is exercised without needing an OS drag loop.
/// </summary>
internal static class Phase2WindowingFixtures
{
    private static void EnsureUIDispatcher()
    {
        if (ReactorApp.UIDispatcher is null)
            ReactorApp.UIDispatcher = DispatcherQueue.GetForCurrentThread();
        ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
    }

    private sealed class StubComponent : Component
    {
        public override Element Render() => TextBlock("ok");
    }

    private sealed class DragSurfaceComponent : Component
    {
        public FrameworkElement? Root { get; private set; }
        public Button? Button { get; private set; }
        public FrameworkElement? DragFalse { get; private set; }
        public int Clicks { get; private set; }

        public override Element Render() => VStack(
            Border(TextBlock("background")).OnMount(fe => Root = fe).MinHeight(80).Background("#33000000"),
            Button("Click", () => Clicks++).OnMount(fe => Button = (Button)fe),
            Border(TextBlock("no drag")).Drag(false).OnMount(fe => DragFalse = fe).MinHeight(80).Background("#33000000"));
    }

    private static async Task<ReactorWindow> OpenAndSettle(WindowSpec spec, Func<Component>? root = null)
    {
        var win = ReactorApp.OpenWindow(spec, root ?? (() => new StubComponent()));
        await win.Host.WaitForIdleAsync();
        await Harness.Render(50);
        return win;
    }

    private static async Task CloseAndSettle(params ReactorWindow?[] windows)
    {
        foreach (var win in windows)
        {
            if (win is null) continue;
            try { win.Close(); } catch { }
        }
        await Task.Delay(80);
    }

    private static OverlappedPresenter Presenter(ReactorWindow win)
        => (OverlappedPresenter)win.AppWindow.Presenter;

    private static bool WaitForSize(ReactorWindow win, int expectedWidthPx)
    {
        var size = win.AppWindow.Size;
        return Math.Abs(size.Width - expectedWidthPx) <= 2;
    }

    private static double Ratio(ReactorWindow.RECT rect)
        => (rect.Right - rect.Left) / (double)(rect.Bottom - rect.Top);

    private static void ResetDragHooks()
    {
        ReactorWindow.SuppressDragMoveTimerForTests = false;
        ReactorWindow.BeginDragMovePostCountForTests = 0;
    }

    internal class ResizeModeNoResizeBordersFixed(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "ResizeMode NoResize",
                Width = 260,
                Height = 180,
                ResizeMode = WindowResizeMode.NoResize,
            });
            try
            {
                var op = Presenter(win);
                H.Check("ResizeMode_NoResize_IsResizableFalse", !op.IsResizable);
                H.Check("ResizeMode_NoResize_MinMaxDisabled", !op.IsMinimizable && !op.IsMaximizable);
                win.SetSize(360, 220);
                int expectedWidth = (int)Math.Round(360 * win.DipScale);
                bool changed = await Harness.WaitFor(() => WaitForSize(win, expectedWidth), maxPasses: 10, perPassMs: 20);
                H.Check("ResizeMode_NoResize_ProgrammaticSetSizeStillChanges", changed);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class ResizeModeCanMinimizeAllowsMinimize(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "ResizeMode CanMinimize",
                Width = 260,
                Height = 180,
                ResizeMode = WindowResizeMode.CanMinimize,
            });
            try
            {
                var op = Presenter(win);
                H.Check("ResizeMode_CanMinimize_MinEnabled", op.IsMinimizable);
                H.Check("ResizeMode_CanMinimize_ResizeDisabled", !op.IsResizable);
                H.Check("ResizeMode_CanMinimize_MaxDisabled", !op.IsMaximizable);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class ResizeModeRuntimeUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var spec = new WindowSpec { Title = "ResizeMode Runtime", Width = 260, Height = 180 };
            var win = await OpenAndSettle(spec);
            try
            {
                H.Check("ResizeMode_RuntimeUpdate_InitiallyResizable", Presenter(win).IsResizable);
                win.Update(spec with { ResizeMode = WindowResizeMode.NoResize });
                await Harness.Render(50);
                var op = Presenter(win);
                H.Check("ResizeMode_RuntimeUpdate_ResizeDisabled", !op.IsResizable);
                H.Check("ResizeMode_RuntimeUpdate_MinMaxDisabled", !op.IsMinimizable && !op.IsMaximizable);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class AspectRatioLockedDrag(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec { Title = "Aspect Locked", Width = 320, Height = 200, AspectRatio = 16.0 / 9.0 });
            try
            {
                var rect = new ReactorWindow.RECT { Left = 0, Top = 0, Right = 640, Bottom = 480 };
                bool applied = win.ApplyAspectRatioSizingForTests(2, ref rect);
                H.Check("AspectRatio_LockedDrag_Applied", applied);
                H.Check("AspectRatio_LockedDrag_Ratio", Math.Abs(Ratio(rect) - 16.0 / 9.0) < 0.01);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class AspectRatioRespectsMinMax(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "Aspect MinMax",
                Width = 700,
                Height = 350,
                MinWidth = 600,
                MaxWidth = 1200,
                AspectRatio = 2.0,
            });
            try
            {
                var rect = new ReactorWindow.RECT { Left = 0, Top = 0, Right = 2000, Bottom = 300 };
                win.ApplyAspectRatioSizingForTests(2, ref rect);
                var clamped = win.ClampSizingRectForTests(rect);
                int width = clamped.Right - clamped.Left;
                int maxWidthPx = (int)Math.Round(1200 * win.DipScale);
                H.Check("AspectRatio_RespectsMinMax_MaxWins", width <= maxWidthPx + 1);

                rect = new ReactorWindow.RECT { Left = 0, Top = 0, Right = 200, Bottom = 100 };
                win.ApplyAspectRatioSizingForTests(2, ref rect);
                clamped = win.ClampSizingRectForTests(rect);
                width = clamped.Right - clamped.Left;
                int minWidthPx = (int)Math.Round(600 * win.DipScale);
                H.Check("AspectRatio_RespectsMinMax_MinWins", width >= minWidthPx - 1);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class AspectRatioRejectsNoResize(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            EnsureUIDispatcher();
            bool threw = false;
            try { new WindowSpec { ResizeMode = WindowResizeMode.NoResize, AspectRatio = 1.0 }.Validate(); }
            catch (ArgumentException) { threw = true; }
            H.Check("AspectRatio_RejectsNoResize", threw);
            return Task.CompletedTask;
        }
    }

    internal class AspectRatioRuntimeSwap(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec { Title = "Aspect Runtime", Width = 320, Height = 200 });
            try
            {
                var rect = new ReactorWindow.RECT { Left = 0, Top = 0, Right = 500, Bottom = 300 };
                win.SetAspectRatio(2.0);
                win.ApplyAspectRatioSizingForTests(2, ref rect);
                bool first = Math.Abs(Ratio(rect) - 2.0) < 0.01;

                rect = new ReactorWindow.RECT { Left = 0, Top = 0, Right = 500, Bottom = 300 };
                win.SetAspectRatio(1.0);
                win.ApplyAspectRatioSizingForTests(2, ref rect);
                bool second = Math.Abs(Ratio(rect) - 1.0) < 0.01;
                H.Check("AspectRatio_RuntimeSwap_First", first);
                H.Check("AspectRatio_RuntimeSwap_Second", second);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class DragMoveFromBackground(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            ResetDragHooks();
            ReactorWindow.SuppressDragMoveTimerForTests = true;
            var component = new DragSurfaceComponent();
            var win = await OpenAndSettle(new WindowSpec { Title = "Drag Background", Width = 300, Height = 240, IsMovableByBackground = true }, () => component);
            try
            {
                var source = component.Root ?? win.Host.CurrentControl;
                bool began = source is not null && win.SimulateBackgroundPointerPressedForTests(source);
                H.Check("DragMove_FromBackground_Begins", began && ReactorWindow.BeginDragMovePostCountForTests == 1);
            }
            finally { ResetDragHooks(); await CloseAndSettle(win); }
        }
    }

    internal class DragMoveSuppressedOnButton(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            ResetDragHooks();
            ReactorWindow.SuppressDragMoveTimerForTests = true;
            var component = new DragSurfaceComponent();
            var win = await OpenAndSettle(new WindowSpec { Title = "Drag Button", Width = 300, Height = 240, IsMovableByBackground = true }, () => component);
            try
            {
                bool began = component.Button is not null && win.SimulateBackgroundPointerPressedForTests(component.Button);
                H.Check("DragMove_SuppressedOnButton_NoBegin", !began && ReactorWindow.BeginDragMovePostCountForTests == 0);
                if (component.Button is not null)
                    ((IInvokeProvider)new ButtonAutomationPeer(component.Button).GetPattern(PatternInterface.Invoke)).Invoke();
                await Harness.Render(50);
                H.Check("DragMove_SuppressedOnButton_ClickStillFires", component.Clicks == 1);
            }
            finally { ResetDragHooks(); await CloseAndSettle(win); }
        }
    }

    internal class DragMoveSuppressedOnDragFalse(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            ResetDragHooks();
            ReactorWindow.SuppressDragMoveTimerForTests = true;
            var component = new DragSurfaceComponent();
            var win = await OpenAndSettle(new WindowSpec { Title = "Drag False", Width = 300, Height = 240, IsMovableByBackground = true }, () => component);
            try
            {
                bool began = component.DragFalse is not null && win.SimulateBackgroundPointerPressedForTests(component.DragFalse);
                H.Check("DragMove_SuppressedOnDragFalse", !began && ReactorWindow.BeginDragMovePostCountForTests == 0);
            }
            finally { ResetDragHooks(); await CloseAndSettle(win); }
        }
    }

    internal class BeginDragMoveReentrancyNoop(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            ResetDragHooks();
            // SuppressDragMoveTimerForTests leaves the per-window _dragMoveActive
            // flag set after the first call so the second call hits the
            // re-entrancy guard and no-ops without bumping the counter.
            ReactorWindow.SuppressDragMoveTimerForTests = true;
            var win = await OpenAndSettle(new WindowSpec { Title = "Drag Reentrancy", Width = 260, Height = 180 });
            try
            {
                win.BeginDragMove();
                win.BeginDragMove();
                H.Check("BeginDragMove_ReentrancyNoop", ReactorWindow.BeginDragMovePostCountForTests == 1);
            }
            finally { ResetDragHooks(); await CloseAndSettle(win); }
        }
    }
}
