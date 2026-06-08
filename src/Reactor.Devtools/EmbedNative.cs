using System.Runtime.InteropServices;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

internal static class EmbedNative
{
    public const int GWLP_HWNDPARENT = -8;
    public const int GWL_STYLE = -16;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const long WS_OVERLAPPEDWINDOW = 0x00CF0000L;
    public const long WS_POPUP = unchecked((int)0x80000000);
    public const long WS_CHILD = 0x40000000L;
    public const long WS_CLIPSIBLINGS = 0x04000000L;
    public const long WS_VISIBLE = 0x10000000L;
    public const int SW_SHOW = 5;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetParent(nint hWndChild, nint hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint GetParent(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetFocus(nint hWnd);

    [DllImport("user32.dll")]
    public static extern nint GetWindowDpiAwarenessContext(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AreDpiAwarenessContextsEqual(nint dpiContextA, nint dpiContextB);

    public static void SetWindowStyleForChildEmbed(nint hwnd)
    {
        var style = (long)GetWindowLongPtr(hwnd, GWL_STYLE);
        var updated = (style & ~(WS_OVERLAPPEDWINDOW | WS_POPUP)) | WS_CHILD | WS_CLIPSIBLINGS | WS_VISIBLE;
        SetWindowLongPtr(hwnd, GWL_STYLE, (nint)updated);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }
}
