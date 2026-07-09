using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Microsoft.UI.Reactor.Hosting.Shell;

/// <summary>
/// Source-generated (<c>[GeneratedComInterface]</c>) wrappers for
/// <c>ITaskbarList3</c> — the shell surface for taskbar progress, overlay icons,
/// and thumbnail toolbars. Lives behind <see cref="TaskbarComSingleton"/> so apps
/// that never touch any of the taskbar surface pay zero startup cost.
/// (spec 036 §11.1 / §11.2 / §11.5)
/// </summary>
/// <remarks>
/// Uses the COM interop source generator (<c>System.Runtime.InteropServices.Marshalling</c>)
/// so the marshaling stubs are emitted at compile time and are trim/NativeAOT-safe —
/// unlike the classic <c>[ComImport]</c> path, whose runtime CoCreateInstance /
/// built-in marshaling is unsupported under full AOT (IL3052). Every method keeps its
/// raw <c>int</c> HRESULT return (the generator never throws for us) so callers can
/// inspect it — the shell returns <c>S_FALSE</c> on certain spurious failures
/// (e.g. SetProgressValue before <c>HrInit</c>) that we treat as recoverable.
/// </remarks>
[GeneratedComInterface]
[Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
internal partial interface ITaskbarList3
{
    // ITaskbarList ----------------------------------------------------------
    int HrInit();
    int AddTab(nint hwnd);
    int DeleteTab(nint hwnd);
    int ActivateTab(nint hwnd);
    int SetActiveAlt(nint hwnd);

    // ITaskbarList2 ---------------------------------------------------------
    int MarkFullscreenWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

    // ITaskbarList3 ---------------------------------------------------------
    int SetProgressValue(nint hwnd, ulong ullCompleted, ulong ullTotal);
    int SetProgressState(nint hwnd, NativeTaskbarProgressState state);
    int RegisterTab(nint hwndTab, nint hwndMDI);
    int UnregisterTab(nint hwndTab);
    int SetTabOrder(nint hwndTab, nint hwndInsertBefore);
    int SetTabActive(nint hwndTab, nint hwndMDI, uint dwReserved);

    int ThumbBarAddButtons(nint hwnd, uint cButtons,
        [MarshalUsing(CountElementName = nameof(cButtons))] THUMBBUTTON[] pButton);

    int ThumbBarUpdateButtons(nint hwnd, uint cButtons,
        [MarshalUsing(CountElementName = nameof(cButtons))] THUMBBUTTON[] pButton);

    int ThumbBarSetImageList(nint hwnd, nint himl);

    int SetOverlayIcon(nint hwnd, nint hIcon,
        [MarshalAs(UnmanagedType.LPWStr)] string? pszDescription);

    int SetThumbnailTooltip(nint hwnd, [MarshalAs(UnmanagedType.LPWStr)] string? pszTip);
    int SetThumbnailClip(nint hwnd, nint prcClip);
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

[StructLayout(LayoutKind.Sequential)]
internal struct THUMBBUTTON
{
    public ThumbButtonMask dwMask;
    public uint iId;
    public uint iBitmap;
    public nint hIcon;
    // Inline 260-char buffer (was [MarshalAs(ByValTStr, SizeConst = 260)] string).
    // Keeping it inline makes THUMBBUTTON blittable, which the COM interop source
    // generator requires to marshal a THUMBBUTTON[] array parameter.
    public TipBuffer szTip;
    public ThumbButtonFlags dwFlags;

    /// <summary>Copies <paramref name="tip"/> into the inline szTip buffer,
    /// truncating to fit and leaving a NUL terminator.</summary>
    public void SetTip(string? tip)
    {
        Span<ushort> dst = szTip;
        dst.Clear();
        if (string.IsNullOrEmpty(tip)) return;
        int n = Math.Min(tip.Length, dst.Length - 1);
        for (int i = 0; i < n; i++) dst[i] = tip[i];
    }
}

/// <summary>Inline 260-code-unit buffer backing <see cref="THUMBBUTTON.szTip"/>
/// (the native <c>WCHAR szTip[260]</c> field). Uses <c>ushort</c> rather than
/// <c>char</c> so the struct stays blittable for the COM interop source generator
/// without needing assembly-wide <c>DisableRuntimeMarshalling</c>.</summary>
[InlineArray(260)]
internal struct TipBuffer
{
    private ushort _element0;
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
internal static partial class TaskbarComSingleton
{
    private static ITaskbarList3? s_instance;
    private static readonly object s_lock = new();
    private static int s_initFailed;

    // CLSID_TaskbarList — the shell coclass that implements ITaskbarList3.
    private static readonly Guid CLSID_TaskbarList = new("56fdf344-fd6d-11d0-958a-006097c9a090");
    private static readonly Guid IID_ITaskbarList3 = new("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf");
    private const uint CLSCTX_INPROC_SERVER = 0x1;

    // Source-generated ComWrappers strategy — wraps the raw COM pointer from
    // CoCreateInstance as an AOT-safe RCW for the [GeneratedComInterface] above.
    private static readonly StrategyBasedComWrappers s_comWrappers = new();

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

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
                // Activate the shell coclass by CLSID via a plain P/Invoke (AOT-safe,
                // unlike `new [ComImport]TaskbarInstance()` which needs built-in COM).
                int hrCreate = CoCreateInstance(
                    in CLSID_TaskbarList, 0, CLSCTX_INPROC_SERVER, in IID_ITaskbarList3, out nint pUnk);
                if (hrCreate < 0 || pUnk == 0)
                {
                    Volatile.Write(ref s_initFailed, 1);
                    return null;
                }

                ITaskbarList3 instance;
                try
                {
                    // GetOrCreateObjectForComInstance AddRefs its own reference, so
                    // release the one CoCreateInstance handed us once it's wrapped.
                    instance = (ITaskbarList3)s_comWrappers.GetOrCreateObjectForComInstance(
                        pUnk, CreateObjectFlags.None);
                }
                finally
                {
                    Marshal.Release(pUnk);
                }

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
