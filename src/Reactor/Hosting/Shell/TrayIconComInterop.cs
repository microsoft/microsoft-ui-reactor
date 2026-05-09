using System.Runtime.InteropServices;

namespace Microsoft.UI.Reactor.Hosting.Shell;

/// <summary>
/// <c>Shell_NotifyIcon</c> interop. Driven by <see cref="TrayHiddenWindow"/>;
/// never invoked from app code. (spec 036 §11.4)
/// </summary>
internal static class TrayIconComInterop
{
    public const uint NIM_ADD    = 0x00000000;
    public const uint NIM_MODIFY = 0x00000001;
    public const uint NIM_DELETE = 0x00000002;
    public const uint NIM_SETVERSION = 0x00000004;

    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON    = 0x00000002;
    public const uint NIF_TIP     = 0x00000004;
    public const uint NIF_STATE   = 0x00000008;
    public const uint NIF_GUID    = 0x00000020;
    public const uint NIF_SHOWTIP = 0x00000080;

    public const uint NIS_HIDDEN = 0x00000001;

    // Shell-callback message IDs sent to the host window's WndProc when the
    // icon is interacted with. We use NOTIFYICON_VERSION_4 semantics so the
    // mouse-position is in lParam HIWORD/LOWORD and the icon ID is in
    // wParam HIWORD.
    public const uint NOTIFYICON_VERSION_4 = 4;
    public const uint NIN_SELECT       = 0x400; // WM_USER
    public const uint NIN_KEYSELECT    = 0x401;
    public const uint NIN_BALLOONSHOW  = 0x402;
    public const uint NIN_BALLOONHIDE  = 0x403;
    public const uint NIN_BALLOONTIMEOUT = 0x404;
    public const uint NIN_BALLOONUSERCLICK = 0x405;
    public const uint NIN_POPUPOPEN    = 0x406;
    public const uint NIN_POPUPCLOSE   = 0x407;

    // Standard window messages we still receive in v4.
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_CONTEXTMENU = 0x007B;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint LoadImageW(nint hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    public const uint IMAGE_ICON       = 1;
    public const uint LR_LOADFROMFILE  = 0x00000010;
    public const uint LR_DEFAULTSIZE   = 0x00000040;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetSystemMetrics(int nIndex);

    // GetSystemMetricsForDpi requires Windows 10 1607+; we ship min-version
    // 10.0.19041 so it's always present, but call sites still tolerate a
    // P/Invoke failure for forward-portability.
    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    public const int SM_CXSMICON = 49;
    public const int SM_CYSMICON = 50;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int x; public int y; }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);
}
