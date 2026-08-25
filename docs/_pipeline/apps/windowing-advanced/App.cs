using System.Runtime.InteropServices;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<AdvancedWindowingApp>("Advanced Windowing", width: 520, height: 360);

class AdvancedWindowingApp : Component
{
    public override Element Render() =>
        VStack(12,
            Heading("Advanced Windowing"),
            TextBlock("Recipes that leave the supported Reactor contract."))
        .Padding(20);
}

// <snippet:hud-spec>
static class HudWindow
{
    public static WindowSpec Spec { get; } = new()
    {
        Title = "HUD",
        Style = WindowStyle.None,
        IsMovableByBackground = true,
        Level = WindowLevel.Floating,
        CornerStyle = WindowCornerStyle.Rounded,
        Backdrop = BackdropChoice.Of(BackdropKind.DesktopAcrylic),
    };
}
// </snippet:hud-spec>

static partial class LayeredOverlayInterop
{
    // <snippet:layered-overlay>
    const int GWL_EXSTYLE = -20;
    const nint WS_EX_TRANSPARENT = 0x00000020;
    const nint WS_EX_TOOLWINDOW = 0x00000080;
    const nint WS_EX_LAYERED = 0x00080000;
    const uint LWA_ALPHA = 0x00000002;

    public static void MakeClickThrough(ReactorWindow window)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window.NativeWindow);
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE,
            ex | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
        SetLayeredWindowAttributes(hwnd, 0, 160, LWA_ALPHA);
    }
    // </snippet:layered-overlay>

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetLayeredWindowAttributes(nint hWnd, uint crKey, byte bAlpha, uint dwFlags);
}

static partial class WindowRegionInterop
{
    // <snippet:window-region>
    public static void ApplyRoundedRegion(nint hwnd, int widthPx, int heightPx, int radiusPx)
    {
        var region = CreateRoundRectRgn(0, 0, widthPx, heightPx, radiusPx, radiusPx);
        if (region == 0) return;

        // SetWindowRgn transfers ownership of the HRGN to the system on
        // success — the region must NOT be deleted afterwards. Delete it
        // only when the call fails, or the next repaint reads freed GDI
        // memory.
        if (SetWindowRgn(hwnd, region, bRedraw: true) == 0)
            DeleteObject(region);
    }
    // </snippet:window-region>

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowRgn(nint hWnd, nint hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint hObject);
}
