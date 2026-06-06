using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>Spec 054 Phase 4 fixtures for style, DWM corner preference, and z-order level.</summary>
internal static class Phase4WindowingFixtures
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

    private static nint Hwnd(ReactorWindow win) => WinRT.Interop.WindowNative.GetWindowHandle(win.NativeWindow);
    private static long StyleBits(ReactorWindow win) => (long)Native.GetWindowLongPtr(Hwnd(win), Native.GWL_STYLE);
    private static long ExStyleBits(ReactorWindow win) => (long)Native.GetWindowLongPtr(Hwnd(win), Native.GWL_EXSTYLE);

    private static bool IsAbove(ReactorWindow upper, ReactorWindow lower)
    {
        nint target = Hwnd(lower);
        for (nint current = Hwnd(upper); current != 0; current = Native.GetWindow(current, Native.GW_HWNDNEXT))
        {
            if (current == target) return true;
        }
        return false;
    }

    internal class WindowStyleNoneBorderless(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "Style None",
                Width = 260,
                Height = 180,
                Style = WindowStyle.None,
                IsMovableByBackground = true,
            });
            try
            {
                bool settled = await Harness.WaitFor(() =>
                {
                    long bits = StyleBits(win);
                    return (bits & (Native.WS_BORDER | Native.WS_CAPTION | Native.WS_SYSMENU)) == 0;
                }, maxPasses: 20, perPassMs: 20);

                long bits = StyleBits(win);
                H.Check("WindowStyle_None_NoBorder", settled && (bits & Native.WS_BORDER) == 0);
                H.Check("WindowStyle_None_NoCaption", settled && (bits & Native.WS_CAPTION) == 0);
                H.Check("WindowStyle_None_NoSysMenu", settled && (bits & Native.WS_SYSMENU) == 0);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class WindowStyleToolWindowHidesTaskbar(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec { Title = "ToolWindow Default", Width = 260, Height = 180, Style = WindowStyle.ToolWindow });
            try
            {
                long bits = ExStyleBits(win);
                H.Check("WindowStyle_ToolWindow_HidesTaskbar_ToolBit", (bits & Native.WS_EX_TOOLWINDOW) != 0);
                H.Check("WindowStyle_ToolWindow_HidesTaskbar_NoAppBit", (bits & Native.WS_EX_APPWINDOW) == 0);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class WindowStyleToolWindowRespectsExplicitShowInTaskbar(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "ToolWindow Explicit",
                Width = 260,
                Height = 180,
                Style = WindowStyle.ToolWindow,
                ShowInTaskbar = true,
            });
            try
            {
                long bits = ExStyleBits(win);
                H.Check("WindowStyle_ToolWindow_Explicit_AppBit", (bits & Native.WS_EX_APPWINDOW) != 0);
                H.Check("WindowStyle_ToolWindow_Explicit_NoToolBit", (bits & Native.WS_EX_TOOLWINDOW) == 0);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class WindowStyleRuntimeUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var spec = new WindowSpec { Title = "Style Runtime", Width = 260, Height = 180, IsMovableByBackground = true };
            var win = await OpenAndSettle(spec);
            try
            {
                win.Update(spec with { Style = WindowStyle.None });
                bool noneSettled = await Harness.WaitFor(() =>
                {
                    long bits = StyleBits(win);
                    return (bits & (Native.WS_CAPTION | Native.WS_SYSMENU | Native.WS_BORDER)) == 0;
                }, maxPasses: 20, perPassMs: 20);
                long none = StyleBits(win);
                H.Check("WindowStyle_RuntimeUpdate_None", noneSettled && (none & (Native.WS_CAPTION | Native.WS_SYSMENU | Native.WS_BORDER)) == 0);

                win.Update(spec with { Style = WindowStyle.Default });
                await Harness.Render(80);
                long normal = StyleBits(win);
                H.Check("WindowStyle_RuntimeUpdate_DefaultCaption", (normal & Native.WS_CAPTION) != 0);
                H.Check("WindowStyle_RuntimeUpdate_DefaultSysMenu", (normal & Native.WS_SYSMENU) != 0);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class CornerStyleApply(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            if (Environment.OSVersion.Version.Build < 22000)
            {
                H.Skip("CornerStyle_Apply", "DWM corner preference is only round-trippable on Windows 11+");
                return;
            }

            var cases = new[]
            {
                (WindowCornerStyle.Default, DwmInterop.DWMWCP_DEFAULT, "Default"),
                (WindowCornerStyle.Square, DwmInterop.DWMWCP_DONOTROUND, "Square"),
                (WindowCornerStyle.Rounded, DwmInterop.DWMWCP_ROUND, "Rounded"),
                (WindowCornerStyle.RoundedSmall, DwmInterop.DWMWCP_ROUNDSMALL, "RoundedSmall"),
            };

            foreach (var (style, expected, name) in cases)
            {
                var win = await OpenAndSettle(new WindowSpec { Title = $"Corner {name}", Width = 240, Height = 160, CornerStyle = style });
                try
                {
                    int actual;
                    int hr = DwmInterop.DwmGetWindowAttribute(Hwnd(win), DwmInterop.DWMWA_WINDOW_CORNER_PREFERENCE, out actual, sizeof(int));
                    if (hr != 0)
                        H.Skip($"CornerStyle_Apply_{name}", $"DwmGetWindowAttribute failed: 0x{hr:X8}");
                    else
                        H.Check($"CornerStyle_Apply_{name}", actual == expected);
                }
                finally { await CloseAndSettle(win); }
            }
        }
    }

    internal class WindowLevelAlwaysOnTopStyleBitSet(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec { Title = "Topmost", Width = 260, Height = 180, Level = WindowLevel.AlwaysOnTop });
            try { H.Check("WindowLevel_AlwaysOnTop_StyleBitSet", (ExStyleBits(win) & Native.WS_EX_TOPMOST) != 0); }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class WindowLevelFloatingAboveSiblings(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            ReactorWindow? normal = null;
            ReactorWindow? floating = null;
            try
            {
                normal = await OpenAndSettle(new WindowSpec { Title = "Normal Sibling", Width = 260, Height = 180 });
                floating = await OpenAndSettle(new WindowSpec { Title = "Floating Sibling", Width = 260, Height = 180, Level = WindowLevel.Floating });
                normal.Activate();
                bool above = await Harness.WaitFor(() => IsAbove(floating, normal), maxPasses: 10, perPassMs: 40);
                H.Check("WindowLevel_Floating_AboveSiblings", above);
            }
            finally { await CloseAndSettle(floating, normal); }
        }
    }

    internal class WindowLevelFloatingAboveOwner(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            ReactorWindow? owner = null;
            ReactorWindow? floating = null;
            try
            {
                owner = await OpenAndSettle(new WindowSpec { Title = "Owner", Width = 260, Height = 180 });
                floating = await OpenAndSettle(new WindowSpec { Title = "Owned Floating", Width = 240, Height = 160, Owner = owner, Level = WindowLevel.Floating });
                owner.Activate();
                bool above = await Harness.WaitFor(() => IsAbove(floating, owner), maxPasses: 10, perPassMs: 40);
                H.Check("WindowLevel_Floating_AboveOwner", above);
            }
            finally { await CloseAndSettle(floating, owner); }
        }
    }

    internal class WindowLevelRuntimeFlip(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var spec = new WindowSpec { Title = "Level Runtime", Width = 260, Height = 180 };
            var win = await OpenAndSettle(spec);
            try
            {
                win.Update(spec with { Level = WindowLevel.AlwaysOnTop });
                await Harness.Render(80);
                H.Check("WindowLevel_RuntimeFlip_Topmost", (ExStyleBits(win) & Native.WS_EX_TOPMOST) != 0);

                win.Update(spec with { Level = WindowLevel.Normal });
                await Harness.Render(80);
                H.Check("WindowLevel_RuntimeFlip_Normal", (ExStyleBits(win) & Native.WS_EX_TOPMOST) == 0);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    private static class Native
    {
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const long WS_BORDER = 0x00800000;
        public const long WS_CAPTION = 0x00C00000;
        public const long WS_SYSMENU = 0x00080000;
        public const long WS_EX_TOOLWINDOW = 0x00000080;
        public const long WS_EX_APPWINDOW = 0x00040000;
        public const long WS_EX_TOPMOST = 0x00000008;
        public const uint GW_HWNDNEXT = 2;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern nint GetWindow(nint hWnd, uint uCmd);
    }
}
