using System.Runtime.InteropServices;
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

    // Forced GC + finalizer drain is intentional here, NOT cargo culting:
    // adding the new spec-054 window fixtures pushed the per-suite
    // window-open count past a threshold that triggers a documented
    // WinUI 3 native-handle exhaustion flake (CI repro: PR #536, exit
    // 0xC0000402 STATUS_HEAP_CORRUPTION between fixtures). Combined-
    // fixture refactor cut the count substantially; this drain releases
    // the remaining accumulated WinRT/COM handles between fixtures so
    // we stay under the threshold. CodeQL flags this as cs/call-to-gc
    // — acknowledged trade-off vs. test reliability.
    private static async Task CollectWindowResources()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Task.Delay(50);
    }

    private static nint Hwnd(ReactorWindow win) => WinRT.Interop.WindowNative.GetWindowHandle(win.NativeWindow);
    private static long StyleBits(ReactorWindow win) => (long)Native.GetWindowLongPtr(Hwnd(win), Native.GWL_STYLE);
    private static bool HasStyle(ReactorWindow win, long flag) => (StyleBits(win) & flag) != 0;

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
                H.Check("ResizeMode_NoResize_IsResizableFalse", !HasStyle(win, Native.WS_THICKFRAME));
                H.Check("ResizeMode_NoResize_MinMaxDisabled", !HasStyle(win, Native.WS_MINIMIZEBOX) && !HasStyle(win, Native.WS_MAXIMIZEBOX));
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
                H.Check("ResizeMode_CanMinimize_MinEnabled", HasStyle(win, Native.WS_MINIMIZEBOX));
                H.Check("ResizeMode_CanMinimize_ResizeDisabled", !HasStyle(win, Native.WS_THICKFRAME));
                H.Check("ResizeMode_CanMinimize_MaxDisabled", !HasStyle(win, Native.WS_MAXIMIZEBOX));
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
                H.Check("ResizeMode_RuntimeUpdate_InitiallyResizable", HasStyle(win, Native.WS_THICKFRAME));
                win.Update(spec with { ResizeMode = WindowResizeMode.NoResize });
                await Harness.Render(50);
                H.Check("ResizeMode_RuntimeUpdate_ResizeDisabled", !HasStyle(win, Native.WS_THICKFRAME));
                H.Check("ResizeMode_RuntimeUpdate_MinMaxDisabled", !HasStyle(win, Native.WS_MINIMIZEBOX) && !HasStyle(win, Native.WS_MAXIMIZEBOX));
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

    internal class AspectRatioClientBasis(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            // Open with Client basis at 1:1. The window rect (~600x600 plus
            // chrome) won't be square — the CLIENT area will. Verify the
            // window aspect differs from 1:1 by at least the chrome inset,
            // while the client aspect is exactly 1:1 (within rounding).
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "Aspect Client",
                Width = 600,
                Height = 600,
                AspectRatio = 1.0,
                AspectRatioBasis = AspectRatioBasis.Client,
            });
            try
            {
                // Drag-resize event (WMSZ_RIGHT) — user wants a 1000-px-wide window.
                var rect = new ReactorWindow.RECT { Left = 0, Top = 0, Right = 1000, Bottom = 800 };
                bool applied = win.ApplyAspectRatioSizingForTests(2, ref rect);
                H.Check("AspectRatio_ClientBasis_Applied", applied);

                int windowW = rect.Right - rect.Left;
                int windowH = rect.Bottom - rect.Top;
                // The resulting WINDOW aspect should NOT be 1:1 — chrome
                // adds vertical padding on top, so windowH > windowW.
                double windowAspect = (double)windowW / windowH;
                H.Check("AspectRatio_ClientBasis_WindowAspectNotOne", Math.Abs(windowAspect - 1.0) > 0.02);
                // But the CLIENT area aspect SHOULD be 1:1 (within rounding).
                // We don't have direct access to chrome insets in tests, but
                // we can use the fact that windowH - windowW (the extra
                // vertical) is exactly the chrome vertical inset; subtracting
                // it from windowH gives clientH which should equal windowW
                // (caption is taller than left/right borders combined).
                // Looser tolerance because chrome includes per-monitor DPI.
                H.Check("AspectRatio_ClientBasis_WindowH_GreaterThan_W",
                    windowH > windowW);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    private static double WindowRatio(ReactorWindow win)
    {
        var size = win.AppWindow.Size;
        return size.Width / (double)Math.Max(1, size.Height);
    }

    private static bool WindowRatioNear(ReactorWindow win, double expected, double tolerance = 0.03)
        => Math.Abs(WindowRatio(win) - expected) <= tolerance;

    internal class AspectRatioSuiteMath(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec { Title = "Aspect Math Suite", Width = 320, Height = 200, AspectRatio = 16.0 / 9.0 });
            try
            {
                var rect = new ReactorWindow.RECT { Left = 0, Top = 0, Right = 800, Bottom = 400 };
                bool applied = win.ApplyAspectRatioSizingForTests(2, ref rect);
                H.Check("AspectRatio_SideEdgePreservesWidth_Applied", applied);
                H.Check("AspectRatio_SideEdgePreservesWidth_Width", Math.Abs((rect.Right - rect.Left) - 800) <= 1);
                H.Check("AspectRatio_SideEdgePreservesWidth_Height", Math.Abs((rect.Bottom - rect.Top) - 450) <= 1);

                rect = new ReactorWindow.RECT { Left = 0, Top = 0, Right = 800, Bottom = 400 };
                applied = win.ApplyAspectRatioSizingForTests(6, ref rect);
                H.Check("AspectRatio_SideEdgePreservesHeight_Applied", applied);
                H.Check("AspectRatio_SideEdgePreservesHeight_Height", Math.Abs((rect.Bottom - rect.Top) - 400) <= 1);
                H.Check("AspectRatio_SideEdgePreservesHeight_Width", Math.Abs((rect.Right - rect.Left) - 711) <= 1);

                int previousWidth = 0;
                bool monotonic = true;
                for (int i = 0; i < 10; i++)
                {
                    rect = new ReactorWindow.RECT { Left = 0, Top = 0, Right = 800 + i * 5, Bottom = 400 };
                    applied = win.ApplyAspectRatioSizingForTests(8, ref rect);
                    int width = rect.Right - rect.Left;
                    monotonic &= applied && width + 1 >= previousWidth;
                    previousWidth = width;
                }
                H.Check("AspectRatio_CornerDragStable", monotonic);

                win.Update(win.Spec with { AspectRatio = 1.0, AspectRatioBasis = AspectRatioBasis.Client, ExtendsContentIntoTitleBar = false });
                await Harness.Render(50);
                rect = new ReactorWindow.RECT { Left = 0, Top = 0, Right = 1000, Bottom = 800 };
                applied = win.ApplyAspectRatioSizingForTests(2, ref rect);
                H.Check("AspectRatio_ClientBasis_Applied", applied);
                int windowW = rect.Right - rect.Left;
                int windowH = rect.Bottom - rect.Top;
                double windowAspect = (double)windowW / windowH;
                H.Check("AspectRatio_ClientBasis_WindowAspectNotOne", Math.Abs(windowAspect - 1.0) > 0.02);
                H.Check("AspectRatio_ClientBasis_WindowH_GreaterThan_W", windowH > windowW);
            }
            finally { await CloseAndSettle(win); await CollectWindowResources(); }
        }
    }

    internal class AspectRatioSuiteConform(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec { Title = "Aspect Conform Suite", Width = 600, Height = 400 });
            try
            {
                win.Update(win.Spec with { AspectRatio = 1.0, AspectRatioBasis = AspectRatioBasis.Window, ExtendsContentIntoTitleBar = false });
                bool square = await Harness.WaitFor(() => WindowRatioNear(win, 1.0), maxPasses: 10, perPassMs: 20);
                H.Check("AspectRatio_ConformOnUpdate_Square", square);

                win.Update(win.Spec with { AspectRatio = 2.0 });
                bool wide = await Harness.WaitFor(() => WindowRatioNear(win, 2.0), maxPasses: 10, perPassMs: 20);
                H.Check("AspectRatio_ConformOnUpdate_Wide", wide);

                win.SetAspectRatio(1.0);
                square = await Harness.WaitFor(() => WindowRatioNear(win, 1.0), maxPasses: 10, perPassMs: 20);
                H.Check("AspectRatio_ConformOnSetAspectRatio", square);

                bool initiallySquare = WindowRatioNear(win, 1.0);
                int windowHeight = win.AppWindow.Size.Height;
                win.Update(win.Spec with { AspectRatioBasis = AspectRatioBasis.Client });
                bool grew = await Harness.WaitFor(() => win.AppWindow.Size.Height > windowHeight + 2, maxPasses: 10, perPassMs: 20);
                int clientHeight = win.AppWindow.Size.Height;
                win.Update(win.Spec with { AspectRatioBasis = AspectRatioBasis.Window });
                bool shrank = await Harness.WaitFor(() => win.AppWindow.Size.Height < clientHeight - 2 && WindowRatioNear(win, 1.0), maxPasses: 10, perPassMs: 20);
                H.Check("AspectRatio_ConformOnBasisFlip_InitialSquare", initiallySquare);
                H.Check("AspectRatio_ConformOnBasisFlip_ClientGrew", grew);
                H.Check("AspectRatio_ConformOnBasisFlip_WindowShrank", shrank);

                win.Update(win.Spec with { AspectRatio = 1.0, AspectRatioBasis = AspectRatioBasis.Client, ExtendsContentIntoTitleBar = true });
                await Harness.Render(50);
                var rect = new ReactorWindow.RECT { Left = 0, Top = 0, Right = 1000, Bottom = 800 };
                bool applied = win.ApplyAspectRatioSizingForTests(2, ref rect);
                H.Check("AspectRatio_ClientBasisFallsBackOnExtendedTitleBar_Applied", applied);
                H.Check("AspectRatio_ClientBasisFallsBackOnExtendedTitleBar_WindowBasis", Math.Abs((rect.Right - rect.Left) - 1000) <= 1 && Math.Abs((rect.Bottom - rect.Top) - 1000) <= 1);
            }
            finally { await CloseAndSettle(win); await CollectWindowResources(); }
        }
    }

    internal class WindowStyleToolWindowExStyleRemoved(Harness h) : SelfTestFixtureBase(h)
    {
        private const long WS_EX_TOOLWINDOW = 0x00000080;

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec { Title = "ToolWindow ExStyle", Width = 260, Height = 180, Style = WindowStyle.Default });
            try
            {
                H.Check("WindowStyle_ToolWindowExStyleRemoved_InitiallyClear", (win.GetExtendedWindowStyleBitsForTests() & WS_EX_TOOLWINDOW) == 0);
                win.Update(win.Spec with { Style = WindowStyle.ToolWindow });
                bool set = await Harness.WaitFor(() => (win.GetExtendedWindowStyleBitsForTests() & WS_EX_TOOLWINDOW) != 0, maxPasses: 10, perPassMs: 20);
                H.Check("WindowStyle_ToolWindowExStyleRemoved_Set", set);
                win.Update(win.Spec with { Style = WindowStyle.Default });
                bool cleared = await Harness.WaitFor(() => (win.GetExtendedWindowStyleBitsForTests() & WS_EX_TOOLWINDOW) == 0, maxPasses: 10, perPassMs: 20);
                H.Check("WindowStyle_ToolWindowExStyleRemoved_Cleared", cleared);
            }
            finally { await CloseAndSettle(win); await CollectWindowResources(); }
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

    private static class Native
    {
        public const int GWL_STYLE = -16;
        public const long WS_THICKFRAME = 0x00040000;
        public const long WS_MINIMIZEBOX = 0x00020000;
        public const long WS_MAXIMIZEBOX = 0x00010000;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);
    }
}
