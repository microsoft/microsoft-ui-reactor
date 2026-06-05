using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting.Persistence;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>Spec 054 Phase 3 fixtures for taskbar/switcher split and placement persistence.</summary>
internal static class Phase3WindowingFixtures
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

    private sealed class MemoryStore : IWindowPersistenceStore
    {
        private readonly Dictionary<string, byte[]> _data = new(StringComparer.Ordinal);
        public int WriteCount { get; private set; }
        public Dictionary<string, int> WriteCountById { get; } = new(StringComparer.Ordinal);
        public List<byte[]> Writes { get; } = new();

        public bool TryRead(string id, out byte[]? data)
        {
            if (_data.TryGetValue(id, out var existing))
            {
                data = existing.ToArray();
                return true;
            }
            data = null;
            return false;
        }

        public void Write(string id, byte[] data)
        {
            WriteCount++;
            WriteCountById[id] = WriteCountById.TryGetValue(id, out var count) ? count + 1 : 1;
            var copy = data.ToArray();
            _data[id] = copy;
            Writes.Add(copy);
        }
    }

    private static async Task<ReactorWindow> OpenAndSettle(WindowSpec spec)
    {
        var win = ReactorApp.OpenWindow(spec, () => new StubComponent());
        await win.Host.WaitForIdleAsync();
        await Harness.Render(80);
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

    private static bool Near((double X, double Y) actual, double x, double y, double tolerance = 4)
        => Math.Abs(actual.X - x) <= tolerance && Math.Abs(actual.Y - y) <= tolerance;

    private static bool SizeNear(ReactorWindow win, double widthDip, double heightDip, double tolerancePx = 6)
    {
        var size = win.AppWindow.Size;
        return Math.Abs(size.Width - widthDip * win.DipScale) <= tolerancePx
            && Math.Abs(size.Height - heightDip * win.DipScale) <= tolerancePx;
    }

    private static long ExStyle(ReactorWindow win)
        => (long)Native.GetWindowLongPtr(WinRT.Interop.WindowNative.GetWindowHandle(win.NativeWindow), Native.GWL_EXSTYLE);

    private static void CheckTaskbarBits(Harness h, string prefix, ReactorWindow win, bool showInTaskbar, bool showInSwitcher)
    {
        long bits = ExStyle(win);
        h.Check($"{prefix}_ToolWindow", showInTaskbar ? (bits & Native.WS_EX_TOOLWINDOW) == 0 : (bits & Native.WS_EX_TOOLWINDOW) != 0);
        h.Check($"{prefix}_AppWindow", showInTaskbar ? (bits & Native.WS_EX_APPWINDOW) != 0 : (bits & Native.WS_EX_APPWINDOW) == 0);
        h.Check($"{prefix}_Switcher", win.AppWindow.IsShownInSwitchers == showInSwitcher);
    }

    internal class ShowInTaskbarMatrix(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var combos = new[]
            {
                (Taskbar: true, Switcher: true, Name: "TT"),
                (Taskbar: true, Switcher: false, Name: "TF"),
                (Taskbar: false, Switcher: true, Name: "FT"),
                (Taskbar: false, Switcher: false, Name: "FF"),
            };

            foreach (var combo in combos)
            {
                var win = await OpenAndSettle(new WindowSpec
                {
                    Title = $"Show Matrix {combo.Name}",
                    Width = 240,
                    Height = 160,
                    ShowInTaskbar = combo.Taskbar,
                    ShowInSwitcher = combo.Switcher,
                });
                try { CheckTaskbarBits(H, $"ShowInTaskbarMatrix_{combo.Name}", win, combo.Taskbar, combo.Switcher); }
                finally { await CloseAndSettle(win); }
            }
        }
    }

    internal class ShowInTaskbarRuntimeFlip(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            ReactorWindow.TaskbarVisibilityCycleCountForTests = 0;
            var spec = new WindowSpec { Title = "Taskbar Runtime", Width = 240, Height = 160 };
            var win = await OpenAndSettle(spec);
            try
            {
                CheckTaskbarBits(H, "ShowInTaskbar_RuntimeFlip_Before", win, true, true);
                win.Update(spec with { ShowInTaskbar = false });
                await Harness.Render(100);
                CheckTaskbarBits(H, "ShowInTaskbar_RuntimeFlip_After", win, false, true);
                H.Check("ShowInTaskbar_RuntimeFlip_CycledOnce", ReactorWindow.TaskbarVisibilityCycleCountForTests == 1);
            }
            finally { await CloseAndSettle(win); ReactorWindow.TaskbarVisibilityCycleCountForTests = 0; }
        }
    }

    internal class PersistPlacementRoundTrip(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var store = new MemoryStore();
            using var scope = ReactorApp.UseWindowPersistenceStoreForTests(store);
            string id = "phase3-roundtrip";
            var spec = new WindowSpec { Title = "Persist RoundTrip", Width = 260, Height = 180 }.WithPersistence(id);
            var first = await OpenAndSettle(spec);
            first.SetPosition(320, 220);
            first.SetSize(360, 240);
            await Harness.Render(100);
            await CloseAndSettle(first);

            ReactorWindow? second = null;
            try
            {
                second = await OpenAndSettle(spec);
                bool positionRestored = await Harness.WaitFor(() => Near(second.Position, 320, 220), maxPasses: 10, perPassMs: 30);
                H.Check("PersistPlacement_RoundTrip_Position", positionRestored);
                H.Check("PersistPlacement_RoundTrip_Size", SizeNear(second, 360, 240));
            }
            finally { await CloseAndSettle(second); }
        }
    }

    internal class PersistPlacementFallbackWhenEmpty(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            using var scope = ReactorApp.UseWindowPersistenceStoreForTests(new MemoryStore());
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
                Title = "Persist Fallback",
                Width = 240,
                Height = 160,
            }.WithPersistence("phase3-fallback", WindowStartPosition.CenterOnCurrent));
            try
            {
                var pos = win.AppWindow.Position;
                var size = win.AppWindow.Size;
                var center = (X: pos.X + size.Width / 2, Y: pos.Y + size.Height / 2);
                bool inCursorWorkArea = haveCursor
                    && center.X >= work.Left && center.X < work.Right
                    && center.Y >= work.Top && center.Y < work.Bottom;
                H.Check("PersistPlacement_FallbackWhenEmpty", inCursorWorkArea);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class PersistPlacementNoIdThrows(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            EnsureUIDispatcher();
            bool threw = false;
            try { new WindowSpec { PersistPlacement = true }.Validate(); }
            catch (ArgumentException) { threw = true; }
            H.Check("PersistPlacement_NoIdThrows", threw);
            return Task.CompletedTask;
        }
    }

    internal class PersistPlacementFalseDoesNotSaveOrRestore(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var store = new MemoryStore();
            using var scope = ReactorApp.UseWindowPersistenceStoreForTests(store);
            var spec = new WindowSpec
            {
                Title = "Persist False",
                Width = 260,
                Height = 180,
                StartPosition = WindowStartPosition.Manual,
                ManualPosition = (100, 100),
                PersistenceId = "phase3-disabled",
                PersistPlacement = false,
            };
            var first = await OpenAndSettle(spec);
            first.SetPosition(360, 260);
            await Harness.Render(80);
            await CloseAndSettle(first);

            ReactorWindow? second = null;
            try
            {
                second = await OpenAndSettle(spec);
                H.Check("PersistPlacement_FalseDoesNotSave", store.WriteCount == 0);
                H.Check("PersistPlacement_FalseDoesNotRestore", Near(second.Position, 100, 100));
            }
            finally { await CloseAndSettle(second); }
        }
    }

    internal class SavePlacementIdempotent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var store = new MemoryStore();
            using var scope = ReactorApp.UseWindowPersistenceStoreForTests(store);
            var win = await OpenAndSettle(new WindowSpec { Title = "Save Placement", Width = 260, Height = 180 }.WithPersistence("phase3-save"));
            try
            {
                win.SavePlacement();
                win.SavePlacement();
                H.Check("SavePlacement_Idempotent_WriteCount", store.WriteCount == 2);
                H.Check("SavePlacement_Idempotent_SamePayload", store.Writes.Count == 2 && store.Writes[0].SequenceEqual(store.Writes[1]));
            }
            finally { await CloseAndSettle(win); }
        }
    }

    private static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_TOOLWINDOW = 0x00000080;
        public const long WS_EX_APPWINDOW = 0x00040000;
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

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);
    }
}
