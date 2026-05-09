using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace Microsoft.UI.Reactor.Hosting.Shell;

/// <summary>
/// Hidden message-only window that owns the <c>Shell_NotifyIcon</c>
/// callbacks for one or more <see cref="ReactorTrayIcon"/> instances. One
/// per process; created lazily on the first tray icon registration. Routes
/// shell callbacks back to the tray-icon owners on the UI thread. (spec 036
/// §11.4)
/// </summary>
/// <remarks>
/// <para>The hidden window is internal — never exposed to app code. It runs
/// on the captured <see cref="ReactorApp.UIDispatcher"/>; tray icon callbacks
/// arrive in the WndProc thread and are forwarded to the owning
/// <see cref="ReactorTrayIcon"/> on the UI dispatcher.</para>
/// <para><b>Security — tray callbacks can be forged:</b> any process running
/// at the same Integrity Level as this app can enumerate the message-only
/// HWND and <c>PostMessage</c> a synthetic <c>WM_APP+1</c> with arbitrary
/// <c>wParam</c>/<c>lParam</c>. The framework dispatches that to the owning
/// tray icon's click handler on the UI thread without being able to verify
/// the source. (W-6, threat model 2026-05-08.)</para>
/// <para>Implication for app code: tray click handlers should perform
/// reversible UI actions only — open a window, toggle state, show a flyout.
/// Anything privileged (launching an elevated child, mutating shared state,
/// invoking a payment / destructive operation) must require a deliberate
/// in-app confirmation step rather than relying on the click as proof of
/// user intent.</para>
/// </remarks>
internal sealed class TrayHiddenWindow : IDisposable
{
    private static TrayHiddenWindow? s_instance;
    private static readonly object s_singletonLock = new();

    private readonly nint _hwnd;
    private readonly DispatcherQueue _dispatcher;
    // Strongly-typed callback collection for the static WndProc to dispatch
    // into. A copy-on-write dictionary keyed by NIN icon id (uID slot).
    private TrayCallbackEntry[] _entries = global::System.Array.Empty<TrayCallbackEntry>();
    private readonly object _entriesLock = new();
    private GCHandle _selfHandle;
    private bool _disposed;
    // RegisterWindowMessageW("TaskbarCreated") — Explorer broadcasts this to
    // every top-level window when the shell taskbar is recreated (Explorer
    // restart, signout transitions). We re-register every tray icon on
    // receipt; otherwise icons silently disappear after Explorer churn.
    // (spec 036 §11.4)
    private uint _taskbarCreatedMsg;

    /// <summary>The HWND used as the <c>hWnd</c> field of <c>NOTIFYICONDATAW</c>.</summary>
    public nint Hwnd => _hwnd;

    /// <summary>The shell callback message id used by all tray icons in this process.</summary>
    public const uint TrayCallbackMessage = 0x8001; // WM_APP + 1

    /// <summary>
    /// Acquire the per-process hidden window, creating it on first call.
    /// Throws if no UI dispatcher has been captured by <see cref="ReactorApp"/>.
    /// </summary>
    public static TrayHiddenWindow GetOrCreate()
    {
        var existing = Volatile.Read(ref s_instance);
        if (existing is not null) return existing;
        lock (s_singletonLock)
        {
            if (s_instance is not null) return s_instance;
            var dispatcher = ReactorApp.UIDispatcher
                ?? throw new InvalidOperationException(
                    "ReactorApp.UIDispatcher is not set — open a tray icon only after ReactorApp.Run has bootstrapped.");
            var instance = new TrayHiddenWindow(dispatcher);
            Volatile.Write(ref s_instance, instance);
            return instance;
        }
    }

    /// <summary>Test hook — destroy the singleton between fixtures.</summary>
    internal static void ResetForTests()
    {
        lock (s_singletonLock)
        {
            s_instance?.Dispose();
            s_instance = null;
        }
    }

    private TrayHiddenWindow(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _selfHandle = GCHandle.Alloc(this, GCHandleType.Weak);

        // Create a message-only window class + instance. Message-only windows
        // (HWND_MESSAGE parent) never display, never appear in z-order, but can
        // receive messages — perfect for shell callback routing.
        unsafe
        {
            var className = "ReactorTrayHiddenWindow";
            fixed (char* classNamePtr = className)
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    style = 0,
                    lpfnWndProc = &WndProcStatic,
                    cbClsExtra = 0,
                    cbWndExtra = 0,
                    hInstance = GetModuleHandleW(null),
                    hIcon = 0,
                    hCursor = 0,
                    hbrBackground = 0,
                    lpszMenuName = null,
                    lpszClassName = classNamePtr,
                    hIconSm = 0,
                };
                _ = RegisterClassExW(ref wc); // class may already be registered, ignore failure

                var hwnd = CreateWindowExW(
                    0,
                    className,
                    "ReactorTrayHiddenWindow",
                    0,
                    0, 0, 0, 0,
                    HWND_MESSAGE,
                    0,
                    GetModuleHandleW(null),
                    GCHandle.ToIntPtr(_selfHandle));
                if (hwnd == 0)
                {
                    var err = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException($"CreateWindowExW failed: 0x{err:X8}");
                }
                _hwnd = hwnd;
            }
        }

        // RegisterWindowMessageW returns the same id for every caller in the
        // session, so even though we register once per process this is the
        // same value Explorer broadcasts. Zero on failure — we tolerate that
        // and just lose Explorer-restart resilience for this process.
        try { _taskbarCreatedMsg = RegisterWindowMessageW("TaskbarCreated"); }
        catch (Exception ex) { Debug.WriteLine($"[Reactor] RegisterWindowMessage(TaskbarCreated) failed: {ex.Message}"); }
    }

    public uint Register(uint id, TrayCallbackEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_entriesLock)
        {
            var current = Volatile.Read(ref _entries);
            // Dedup by id — Update path replaces the existing entry.
            var next = new List<TrayCallbackEntry>(current.Length + 1);
            for (int i = 0; i < current.Length; i++)
                if (current[i].Id != id) next.Add(current[i]);
            entry.Id = id;
            next.Add(entry);
            Volatile.Write(ref _entries, next.ToArray());
        }
        return id;
    }

    public void Unregister(uint id)
    {
        lock (_entriesLock)
        {
            var current = Volatile.Read(ref _entries);
            var next = new List<TrayCallbackEntry>(current.Length);
            for (int i = 0; i < current.Length; i++)
                if (current[i].Id != id) next.Add(current[i]);
            Volatile.Write(ref _entries, next.ToArray());
        }
    }

    private void DispatchCallback(uint id, uint mouseMessage)
    {
        // Snapshot under copy-on-write read; dispatch on UI thread.
        var entries = Volatile.Read(ref _entries);
        TrayCallbackEntry? hit = null;
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].Id == id) { hit = entries[i]; break; }
        }
        if (hit is null) return;

        // Marshal to UI thread — the WndProc may already be on it (the hidden
        // window was created from the captured dispatcher), but TryEnqueue is
        // a cheap safe no-op when same-thread execution is fine.
        _dispatcher.TryEnqueue(() =>
        {
            try
            {
                switch (mouseMessage)
                {
                    case TrayIconComInterop.WM_LBUTTONUP:
                    case TrayIconComInterop.NIN_SELECT:
                    case TrayIconComInterop.NIN_KEYSELECT:
                        hit.OnClick?.Invoke();
                        break;
                    case TrayIconComInterop.WM_LBUTTONDBLCLK:
                        hit.OnDoubleClick?.Invoke();
                        break;
                    case TrayIconComInterop.WM_RBUTTONUP:
                    case TrayIconComInterop.WM_CONTEXTMENU:
                        hit.OnRightClick?.Invoke();
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Reactor] TrayCallback dispatch threw: {ex.Message}");
            }
        });
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProcStatic(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        try
        {
            // Recover the hidden-window instance via the GWLP_USERDATA slot.
            // We seed it via WM_NCCREATE's lpCreateParams.
            if (msg == WM_NCCREATE)
            {
                unsafe
                {
                    var cs = (CREATESTRUCTW*)lParam;
                    if (cs != null)
                        SetWindowLongPtrW(hwnd, GWLP_USERDATA, cs->lpCreateParams);
                }
                return 1;
            }

            var ud = GetWindowLongPtrW(hwnd, GWLP_USERDATA);
            if (ud != 0)
            {
                var gch = GCHandle.FromIntPtr(ud);
                if (gch.IsAllocated && gch.Target is TrayHiddenWindow self)
                {
                    if (msg == TrayCallbackMessage)
                    {
                        // NOTIFYICON_VERSION_4 wire shape:
                        //   wParam = MAKEWPARAM(anchorX, anchorY)
                        //   lParam = MAKELPARAM(notification, iconId)
                        var notif = (uint)((lParam) & 0xFFFF);
                        var iconId = (uint)((lParam >> 16) & 0xFFFF);
                        self.DispatchCallback(iconId, notif);
                        return 0;
                    }
                    if (self._taskbarCreatedMsg != 0 && msg == self._taskbarCreatedMsg)
                    {
                        self.DispatchTaskbarCreated();
                        // Don't return 0 — TaskbarCreated is a broadcast and
                        // other registered handlers (e.g. third-party shell
                        // hooks living in the same process) might need it.
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] TrayHiddenWindow WndProc threw: {ex.Message}");
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void DispatchTaskbarCreated()
    {
        // Snapshot under copy-on-write read; dispatch on UI thread so re-add
        // calls (NIM_ADD + NIM_SETVERSION) run on the captured dispatcher,
        // matching the threading invariant of the original registration path.
        var entries = Volatile.Read(ref _entries);
        if (entries.Length == 0) return;
        _dispatcher.TryEnqueue(() =>
        {
            for (int i = 0; i < entries.Length; i++)
            {
                try { entries[i].OnReapply?.Invoke(); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Reactor] TrayIcon reapply after TaskbarCreated threw: {ex.Message}");
                }
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_hwnd != 0) DestroyWindow(_hwnd);
        }
        catch (Exception ex) { Debug.WriteLine($"[Reactor] DestroyWindow failed: {ex.Message}"); }
        if (_selfHandle.IsAllocated) _selfHandle.Free();
    }

    // ── Win32 plumbing ───────────────────────────────────────────────────

    private const uint WM_NCCREATE = 0x0081;
    private const int  GWLP_USERDATA = -21;
    private static readonly nint HWND_MESSAGE = -3;

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct CREATESTRUCTW
    {
        public nint lpCreateParams;
        public nint hInstance;
        public nint hMenu;
        public nint hwndParent;
        public int cy;
        public int cx;
        public int y;
        public int x;
        public uint style;
        public char* lpszName;
        public char* lpszClass;
        public uint dwExStyle;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint dwExStyle,
        [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
        [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName,
        uint dwStyle,
        int X, int Y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint hWnd, uint Msg, nuint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern unsafe nint GetModuleHandleW(char* lpModuleName);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessageW([MarshalAs(UnmanagedType.LPWStr)] string lpString);
}

/// <summary>
/// Per-icon callback target stored in <see cref="TrayHiddenWindow._entries"/>.
/// </summary>
internal sealed class TrayCallbackEntry
{
    public uint Id { get; set; }
    public Action? OnClick { get; set; }
    public Action? OnDoubleClick { get; set; }
    public Action? OnRightClick { get; set; }
    /// <summary>
    /// Invoked on the UI thread when Explorer broadcasts <c>TaskbarCreated</c>
    /// (Explorer restart, sign-out cycle). The owning <see cref="ReactorTrayIcon"/>
    /// re-runs <c>NIM_ADD</c> + <c>NIM_SETVERSION</c> so the icon reappears.
    /// </summary>
    public Action? OnReapply { get; set; }
}
