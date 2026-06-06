using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Windowing;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>Spec 054 Phase 5 fixtures for content-driven top-level window sizing.</summary>
internal static class Phase5WindowingFixtures
{
    private static void EnsureUIDispatcher()
    {
        if (ReactorApp.UIDispatcher is null)
            ReactorApp.UIDispatcher = DispatcherQueue.GetForCurrentThread();
        ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
    }

    private sealed class FixedContent(double width, double height) : Component
    {
        public override Element Render() => Border(TextBlock("content")).Width(width).Height(height);
    }

    private sealed class ResizableContent : Component
    {
        public Action? Grow { get; private set; }

        public override Element Render()
        {
            var (large, setLarge) = UseState(false);
            Grow = () => setLarge(true);
            return Border(TextBlock("content")).Width(large ? 420 : 280).Height(large ? 320 : 180);
        }
    }

    private static async Task<ReactorWindow> OpenAndSettle(WindowSpec spec, Func<Component> root)
    {
        var win = ReactorApp.OpenWindow(spec, root);
        await win.Host.WaitForIdleAsync();
        await Harness.Render(120);
        return win;
    }

    private static async Task CloseAndSettle(params ReactorWindow?[] windows)
    {
        foreach (var win in windows)
        {
            if (win is null) continue;
            try { win.Close(); } catch { }
        }
        await Task.Delay(100);
    }

    // Forced GC + finalizer drain is intentional here. See the comment on
    // CollectWindowResources in Phase2WindowingFixtures.cs for rationale —
    // releases accumulated WinRT/COM handles between fixtures to stay under
    // the WinUI 3 native-handle exhaustion threshold (PR #536 repro).
    private static async Task CollectWindowResources()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Task.Delay(50);
    }

    private static (int Width, int Height) ExpectedWindowSize(ReactorWindow win, double contentWidthDip, double contentHeightDip)
    {
        uint dpi = (uint)(win.Dpi == 0 ? 96 : win.Dpi);
        var rect = new Native.RECT
        {
            Left = 0,
            Top = 0,
            Right = (int)Math.Round(contentWidthDip * dpi / 96.0),
            Bottom = (int)Math.Round(contentHeightDip * dpi / 96.0),
        };
        long style = (long)Native.GetWindowLongPtr(Hwnd(win), Native.GWL_STYLE);
        long exStyle = (long)Native.GetWindowLongPtr(Hwnd(win), Native.GWL_EXSTYLE);
        _ = Native.AdjustWindowRectExForDpi(ref rect, unchecked((uint)style), false, unchecked((uint)exStyle), dpi);
        return (Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top));
    }

    private static bool WidthNear(ReactorWindow win, int expected, int tolerance = 6)
        => Math.Abs(win.AppWindow.Size.Width - expected) <= tolerance;

    private static bool HeightNear(ReactorWindow win, int expected, int tolerance = 6)
        => Math.Abs(win.AppWindow.Size.Height - expected) <= tolerance;

    private static nint Hwnd(ReactorWindow win) => WinRT.Interop.WindowNative.GetWindowHandle(win.NativeWindow);

    internal class SizeToContentWidthTracks(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "STC Width",
                Width = 240,
                Height = 220,
                SizeToContent = WindowSizeToContent.Width,
            }, () => new FixedContent(400, 120));
            try
            {
                var expected = ExpectedWindowSize(win, 400, 120);
                bool settled = await Harness.WaitFor(() => WidthNear(win, expected.Width), maxPasses: 10, perPassMs: 30);
                H.Check("SizeToContent_Width_Tracks", settled);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class SizeToContentHeightTracks(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "STC Height",
                Width = 360,
                Height = 160,
                SizeToContent = WindowSizeToContent.Height,
            }, () => new FixedContent(160, 300));
            try
            {
                var expected = ExpectedWindowSize(win, 160, 300);
                bool settled = await Harness.WaitFor(() => HeightNear(win, expected.Height), maxPasses: 10, perPassMs: 30);
                H.Check("SizeToContent_Height_Tracks", settled);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class SizeToContentWidthAndHeight(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "STC Both",
                Width = 240,
                Height = 160,
                SizeToContent = WindowSizeToContent.WidthAndHeight,
            }, () => new FixedContent(420, 280));
            try
            {
                var expected = ExpectedWindowSize(win, 420, 280);
                bool settled = await Harness.WaitFor(
                    () => WidthNear(win, expected.Width) && HeightNear(win, expected.Height),
                    maxPasses: 10,
                    perPassMs: 30);
                H.Check("SizeToContent_WidthAndHeight", settled);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class SizeToContentRespectsMinMax(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "STC MinMax",
                Width = 240,
                Height = 160,
                MinWidth = 500,
                SizeToContent = WindowSizeToContent.WidthAndHeight,
            }, () => new FixedContent(400, 300));
            try
            {
                int minWidthPx = (int)Math.Round(500 * win.DipScale);
                var expected = ExpectedWindowSize(win, 400, 300);
                bool settled = await Harness.WaitFor(
                    () => win.AppWindow.Size.Width >= minWidthPx && HeightNear(win, expected.Height),
                    maxPasses: 10,
                    perPassMs: 30);
                H.Check("SizeToContent_RespectsMinMax", settled);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class SizeToContentNoOpWhenMaximized(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            ReactorWindow.SizeToContentMaximizedWarningCountForTests = 0;
            var spec = new WindowSpec { Title = "STC Max", Width = 360, Height = 240 };
            var win = await OpenAndSettle(spec, () => new FixedContent(500, 400));
            try
            {
                Native.ShowWindow(Hwnd(win), Native.SW_MAXIMIZE);
                await Harness.WaitFor(() => Native.IsZoomed(Hwnd(win)), maxPasses: 10, perPassMs: 30);
                win.SizeToContentApplyCountForTests = 0;
                win.Update(spec with { SizeToContent = WindowSizeToContent.WidthAndHeight });
                await Harness.Render(120);
                H.Check("SizeToContent_NoOpWhenMaximized_State", Native.IsZoomed(Hwnd(win)));
                H.Check("SizeToContent_NoOpWhenMaximized_Warning", ReactorWindow.SizeToContentMaximizedWarningCountForTests > 0);
                H.Check("SizeToContent_NoOpWhenMaximized_NoResize", win.SizeToContentApplyCountForTests == 0);
            }
            finally { ReactorWindow.SizeToContentMaximizedWarningCountForTests = 0; await CloseAndSettle(win); }
        }
    }

    internal class SizeToContentAspectRatioBothRejected(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            EnsureUIDispatcher();
            bool threw = false;
            try { new WindowSpec { AspectRatio = 1.5, SizeToContent = WindowSizeToContent.WidthAndHeight }.Validate(); }
            catch (ArgumentException) { threw = true; }
            H.Check("SizeToContent_AspectRatio_BothRejected", threw);
            return Task.CompletedTask;
        }
    }

    internal class SizeToContentNoReentrancy(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "STC Reentrancy",
                Width = 240,
                Height = 160,
                SizeToContent = WindowSizeToContent.WidthAndHeight,
            }, () => new FixedContent(420, 320));
            try
            {
                await Harness.Render(160);
                H.Check("SizeToContent_NoReentrancy_Applied", win.SizeToContentApplyCountForTests >= 1);
                H.Check("SizeToContent_NoReentrancy_SingleResize", win.SizeToContentApplyCountForTests == 1);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    private static ReactorWindow.MINMAXINFO InitialMinMaxInfo() => new()
    {
        ptMinTrackSize = new ReactorWindow.POINT { X = 1, Y = 2 },
        ptMaxTrackSize = new ReactorWindow.POINT { X = 10000, Y = 10001 },
    };

    internal class SizeToContentMinMaxInfoSuite(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "STC MinMax Suite",
                Width = 240,
                Height = 180,
                SizeToContent = WindowSizeToContent.Width,
            }, () => new FixedContent(360, 300));
            try
            {
                var info = InitialMinMaxInfo();
                win.ApplyMinMaxInfoForTests(ref info);
                H.Check("SizeToContent_MinMaxInfoClampsWidth_X", info.ptMinTrackSize.X == info.ptMaxTrackSize.X && info.ptMinTrackSize.X > 1);
                H.Check("SizeToContent_MinMaxInfoClampsWidth_YUnchanged", info.ptMinTrackSize.Y == 2 && info.ptMaxTrackSize.Y == 10001);

                win.Update(win.Spec with { SizeToContent = WindowSizeToContent.Height });
                await Harness.Render(60);
                info = InitialMinMaxInfo();
                win.ApplyMinMaxInfoForTests(ref info);
                H.Check("SizeToContent_MinMaxInfoClampsHeight_Y", info.ptMinTrackSize.Y == info.ptMaxTrackSize.Y && info.ptMinTrackSize.Y > 2);
                H.Check("SizeToContent_MinMaxInfoClampsHeight_XUnchanged", info.ptMinTrackSize.X == 1 && info.ptMaxTrackSize.X == 10000);

                win.Update(win.Spec with { SizeToContent = WindowSizeToContent.WidthAndHeight });
                await Harness.Render(60);
                info = InitialMinMaxInfo();
                win.ApplyMinMaxInfoForTests(ref info);
                H.Check("SizeToContent_MinMaxInfoClampsBoth_X", info.ptMinTrackSize.X == info.ptMaxTrackSize.X && info.ptMinTrackSize.X > 1);
                H.Check("SizeToContent_MinMaxInfoClampsBoth_Y", info.ptMinTrackSize.Y == info.ptMaxTrackSize.Y && info.ptMinTrackSize.Y > 2);
            }
            finally { await CloseAndSettle(win); await CollectWindowResources(); }
        }
    }

    private static class Native
    {
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const int SW_MAXIMIZE = 3;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AdjustWindowRectExForDpi(ref RECT lpRect, uint dwStyle,
            [MarshalAs(UnmanagedType.Bool)] bool bMenu, uint dwExStyle, uint dpi);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(nint hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsZoomed(nint hWnd);
    }
}
