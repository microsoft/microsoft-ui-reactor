using System.Runtime.InteropServices;

namespace Microsoft.UI.Reactor.Hosting.Shell;

/// <summary>
/// Classic <c>[ComImport]</c> wrappers for <c>ITaskbarList3</c> — the shell
/// surface for taskbar progress, overlay icons, and thumbnail toolbars. Lives
/// behind <see cref="TaskbarComSingleton"/> so apps that never touch any of the
/// taskbar surface pay zero startup cost. (spec 036 §11.1 / §11.2 / §11.5)
/// </summary>
/// <remarks>
/// The interop is hand-rolled rather than generated because the public surface
/// is small and we want trim/AOT-safe COM marshaling. <c>[PreserveSig]</c> on
/// every method lets us inspect the HRESULT — the shell returns
/// <c>S_FALSE</c> on certain spurious failures (e.g. SetProgressValue before
/// <c>HrInit</c>) that we want to treat as recoverable.
/// </remarks>
[ComImport]
[Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITaskbarList3
{
    // ITaskbarList ----------------------------------------------------------
    [PreserveSig] int HrInit();
    [PreserveSig] int AddTab(nint hwnd);
    [PreserveSig] int DeleteTab(nint hwnd);
    [PreserveSig] int ActivateTab(nint hwnd);
    [PreserveSig] int SetActiveAlt(nint hwnd);

    // ITaskbarList2 ---------------------------------------------------------
    [PreserveSig] int MarkFullscreenWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

    // ITaskbarList3 ---------------------------------------------------------
    [PreserveSig] int SetProgressValue(nint hwnd, ulong ullCompleted, ulong ullTotal);
    [PreserveSig] int SetProgressState(nint hwnd, NativeTaskbarProgressState state);
    [PreserveSig] int RegisterTab(nint hwndTab, nint hwndMDI);
    [PreserveSig] int UnregisterTab(nint hwndTab);
    [PreserveSig] int SetTabOrder(nint hwndTab, nint hwndInsertBefore);
    [PreserveSig] int SetTabActive(nint hwndTab, nint hwndMDI, uint dwReserved);

    [PreserveSig]
    int ThumbBarAddButtons(nint hwnd, uint cButtons,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] THUMBBUTTON[] pButton);

    [PreserveSig]
    int ThumbBarUpdateButtons(nint hwnd, uint cButtons,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] THUMBBUTTON[] pButton);

    [PreserveSig] int ThumbBarSetImageList(nint hwnd, nint himl);

    [PreserveSig]
    int SetOverlayIcon(nint hwnd, nint hIcon,
        [MarshalAs(UnmanagedType.LPWStr)] string? pszDescription);

    [PreserveSig] int SetThumbnailTooltip(nint hwnd, [MarshalAs(UnmanagedType.LPWStr)] string? pszTip);
    [PreserveSig] int SetThumbnailClip(nint hwnd, nint prcClip);
}

/// <summary>Wire shape for <c>ITaskbarList3.SetProgressState</c>.</summary>
[Flags]
internal enum NativeTaskbarProgressState : uint
{
    NoProgress    = 0,
    Indeterminate = 0x1,
    Normal        = 0x2,
    Error         = 0x4,
    Paused        = 0x8,
}

[ComImport]
[Guid("56fdf344-fd6d-11d0-958a-006097c9a090")]
[ClassInterface(ClassInterfaceType.None)]
internal class TaskbarInstance { }

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct THUMBBUTTON
{
    public ThumbButtonMask dwMask;
    public uint iId;
    public uint iBitmap;
    public nint hIcon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string szTip;
    public ThumbButtonFlags dwFlags;
}

[Flags]
internal enum ThumbButtonMask : uint
{
    BITMAP  = 0x00000001,
    ICON    = 0x00000002,
    TOOLTIP = 0x00000004,
    THBF_FLAGS = 0x00000008,
}

[Flags]
internal enum ThumbButtonFlags : uint
{
    Enabled        = 0x00000000,
    Disabled       = 0x00000001,
    DismissOnClick = 0x00000002,
    NoBackground   = 0x00000004,
    Hidden         = 0x00000008,
    NonInteractive = 0x00000010,
}

/// <summary>
/// Process-wide lazy <c>ITaskbarList3</c>. The COM object is created on first
/// access (typically <see cref="TaskbarProgress.State"/> assignment); apps that
/// never touch the taskbar surface stay clean of CoCreateInstance. Thread-safe;
/// the underlying COM is free-threaded for our usage pattern.
/// </summary>
internal static class TaskbarComSingleton
{
    private static ITaskbarList3? s_instance;
    private static readonly object s_lock = new();
    private static int s_initFailed;

    /// <summary>
    /// Returns the shared <see cref="ITaskbarList3"/> or null when the platform
    /// lookup fails (Windows 7 minimum normally satisfies this). Failures are
    /// cached so we don't hammer CoCreateInstance.
    /// </summary>
    public static ITaskbarList3? TryGet()
    {
        if (Volatile.Read(ref s_initFailed) != 0) return null;
        var existing = Volatile.Read(ref s_instance);
        if (existing is not null) return existing;

        lock (s_lock)
        {
            if (s_instance is not null) return s_instance;
            try
            {
                var instance = (ITaskbarList3)new TaskbarInstance();
                int hr = instance.HrInit();
                // S_OK = 0; S_FALSE = 1 (also allowed). HRESULT < 0 == failure.
                if (hr < 0)
                {
                    Volatile.Write(ref s_initFailed, 1);
                    return null;
                }
                Volatile.Write(ref s_instance, instance);
                return instance;
            }
            catch (Exception ex)
            {
                global::System.Diagnostics.Debug.WriteLine($"[Reactor] TaskbarComSingleton init failed: {ex.GetType().Name}: {ex.Message}");
                Volatile.Write(ref s_initFailed, 1);
                return null;
            }
        }
    }

    // Test-only — selftest tear-down can reset the singleton between fixtures.
    internal static void ResetForTests()
    {
        lock (s_lock)
        {
            s_instance = null;
            s_initFailed = 0;
        }
    }
}
