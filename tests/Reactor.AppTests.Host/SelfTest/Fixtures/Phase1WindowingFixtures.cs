using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 054 Phase 1 windowing read-back, z-order, display, and startup-position fixtures.
/// PositionChanged_FiresOnMove validates the active monitor's DIP conversion; when the
/// machine has only one monitor, the single-monitor path is the expected coverage.
/// ZOrderChanged_FiresOnInsertAfter asserts the transition hint, not pixel-accurate cover.
/// </summary>
internal static class Phase1WindowingFixtures
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

    private static async Task<ReactorWindow> OpenAndSettle(WindowSpec spec)
    {
        var win = ReactorApp.OpenWindow(spec, () => new StubComponent());
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

    private static bool Near((double X, double Y) actual, double x, double y, double tolerance = 1.0)
        => Math.Abs(actual.X - x) <= tolerance && Math.Abs(actual.Y - y) <= tolerance;

    private static bool Contains(Rect rect, (double X, double Y) point)
        => point.X >= rect.X && point.X < rect.X + rect.Width
           && point.Y >= rect.Y && point.Y < rect.Y + rect.Height;

    private static (double X, double Y) ExpectedDipPosition(ReactorWindow win)
    {
        var p = win.AppWindow.Position;
        uint dpi = Native.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(win.NativeWindow));
        if (dpi == 0) dpi = 96;
        double scale = dpi / 96.0;
        return (Math.Round(p.X / scale, 0, MidpointRounding.AwayFromZero),
            Math.Round(p.Y / scale, 0, MidpointRounding.AwayFromZero));
    }

    internal class PositionReadBack(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "Position ReadBack",
                Width = 240,
                Height = 160,
                StartPosition = WindowStartPosition.Manual,
                ManualPosition = (100, 100),
            });

            try
            {
                H.Check("Position_ReadBack_Initial", Near(win.Position, 100, 100));
                win.SetPosition(300, 200);
                bool moved = await Harness.WaitFor(() => Near(win.Position, 300, 200), maxPasses: 10, perPassMs: 20);
                H.Check("Position_ReadBack_AfterMove", moved);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class PositionChangedFiresOnMove(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "PositionChanged Move",
                Width = 240,
                Height = 160,
                StartPosition = WindowStartPosition.Manual,
                ManualPosition = (120, 120),
            });

            try
            {
                (double X, double Y)? observed = null;
                win.PositionChanged += (_, args) => observed = args.Position;
                var targetDisplay = ReactorDisplay.Displays.FirstOrDefault(d => !d.IsPrimary) ?? ReactorDisplay.Primary;
                var target = (X: targetDisplay.WorkAreaDip.X + Math.Min(80, Math.Max(20, targetDisplay.WorkAreaDip.Width / 4)),
                    Y: targetDisplay.WorkAreaDip.Y + Math.Min(80, Math.Max(20, targetDisplay.WorkAreaDip.Height / 4)));
                win.SetPosition(target.X, target.Y);

                bool fired = await Harness.WaitFor(() => observed is not null, maxPasses: 10, perPassMs: 20);
                var expected = ExpectedDipPosition(win);
                H.Check("PositionChanged_Fires", fired);
                H.Check("PositionChanged_DipConversion", observed is { } pos && Near(pos, expected.X, expected.Y));
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class PositionChangedNoDuplicateFire(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "PositionChanged No Duplicate",
                Width = 240,
                Height = 160,
                StartPosition = WindowStartPosition.Manual,
                ManualPosition = (140, 140),
            });

            try
            {
                await Harness.Render(50);
                int count = 0;
                win.PositionChanged += (_, _) => count++;
                var current = win.Position;
                win.SetPosition(current.X, current.Y);
                await Harness.Render(100);
                H.Check("PositionChanged_NoDuplicateFire", count == 0);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class ZOrderChangedFiresOnInsertAfter(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            ReactorWindow? front = null;
            ReactorWindow? back = null;
            try
            {
                front = await OpenAndSettle(new WindowSpec
                {
                    Title = "ZOrder Front",
                    Width = 260,
                    Height = 180,
                    StartPosition = WindowStartPosition.Manual,
                    ManualPosition = (180, 180),
                });
                back = await OpenAndSettle(new WindowSpec
                {
                    Title = "ZOrder Back",
                    Width = 260,
                    Height = 180,
                    StartPosition = WindowStartPosition.Manual,
                    ManualPosition = (220, 220),
                });

                bool covered = false;
                front.ZOrderChanged += (_, args) => covered |= args.IsCovered;
                front.Activate();
                await Harness.Render(100);
                back.Activate();
                bool fired = await Harness.WaitFor(() => covered, maxPasses: 12, perPassMs: 30);
                H.Check("ZOrderChanged_FiresOnInsertAfter", fired);
            }
            finally { await CloseAndSettle(back, front); }
        }
    }

    internal class DisplaysEnumerate(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            EnsureUIDispatcher();
            var displays = ReactorDisplay.Displays;
            H.Check("Displays_Enumerate_AtLeastOne", displays.Count >= 1);
            H.Check("Displays_Enumerate_PositiveDpi", displays.All(d => d.Dpi > 0));
            H.Check("Displays_Enumerate_UniquePrimary", displays.Count(d => d.IsPrimary) == 1);
            H.Check("Displays_Enumerate_NonEmptyIds", displays.All(d => !string.IsNullOrWhiteSpace(d.Id)));
            return Task.CompletedTask;
        }
    }

    internal class DisplaysNearestTo(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var primary = ReactorDisplay.Primary;
            var target = (X: primary.WorkAreaDip.X + Math.Min(40, Math.Max(0, primary.WorkAreaDip.Width / 4)),
                Y: primary.WorkAreaDip.Y + Math.Min(40, Math.Max(0, primary.WorkAreaDip.Height / 4)));
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "Displays NearestTo",
                Width = 220,
                Height = 140,
                StartPosition = WindowStartPosition.Manual,
                ManualPosition = target,
            });

            try
            {
                var pos = win.Position;
                var nearest = ReactorDisplay.NearestTo(pos.X, pos.Y);
                H.Check("Displays_NearestTo_ContainsWindow", Contains(nearest.BoundsDip, pos));
                H.Check("Displays_NearestTo_PrimaryPoint", ReactorDisplay.NearestTo(target.X, target.Y).Id == primary.Id);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class CenterOnCurrentUsesCursorMonitor(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            bool haveCursor = Native.GetCursorPos(out var cursor);
            Native.RECT work = default;
            if (haveCursor)
            {
                nint monitor = Native.MonitorFromPoint(cursor, Native.MONITOR_DEFAULTTONEAREST);
                var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
                haveCursor = monitor != 0 && Native.GetMonitorInfo(monitor, ref info);
                work = info.rcWork;
            }

            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "CenterOnCurrent",
                Width = 240,
                Height = 160,
                StartPosition = WindowStartPosition.CenterOnCurrent,
            });

            try
            {
                var pos = win.AppWindow.Position;
                var size = win.AppWindow.Size;
                var center = (X: pos.X + size.Width / 2, Y: pos.Y + size.Height / 2);
                bool inCursorWorkArea = haveCursor
                    && center.X >= work.Left && center.X < work.Right
                    && center.Y >= work.Top && center.Y < work.Bottom;
                H.Check("CenterOnCurrent_UsesCursorMonitor", inCursorWorkArea);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    private static class Native
    {
        public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(nint hwnd);
    }
}
