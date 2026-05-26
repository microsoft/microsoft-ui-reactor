using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Hosting.Messaging;
using Microsoft.UI.Reactor.Hosting.Persistence;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Owns one OS top-level Window and one <see cref="ReactorHost"/>. Created via
/// <see cref="ReactorApp.OpenWindow(WindowSpec, Func{Component}, Action{ReactorHost})"/>.
/// (spec 036 §3.2 / §4.2)
/// </summary>
/// <remarks>
/// <para>Public mutators (<see cref="Activate"/>, <see cref="Hide"/>,
/// <see cref="Show"/>, <see cref="Close"/>, <see cref="Update"/>,
/// <see cref="SetSize"/>, <see cref="SetPosition"/>, <see cref="CenterOnScreen"/>,
/// <see cref="Mount(Component)"/>) must be called on the UI thread captured by
/// <see cref="ReactorApp.UIDispatcher"/>. Read-only properties
/// (<see cref="Spec"/>, <see cref="Dpi"/>, <see cref="State"/>,
/// <see cref="IsVisible"/>, <see cref="IsActive"/>) snapshot a
/// <c>Volatile.Read</c> field and are safe from any thread.</para>
/// <para>Disposal is idempotent — a second <see cref="Close"/> or
/// <see cref="Dispose"/> is a no-op, not an exception.</para>
/// </remarks>
public sealed class ReactorWindow : IDisposable
{
    // Spec 044 §6.7 catch-shape conventions used throughout this file:
    //
    //   §6.7.2 WinUI API narrow catch:
    //     `catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))`
    //     for AppWindow / Window / Win32 calls that can throw the well-known
    //     proxy-disconnect / handle-gone HRESULTs during teardown, DPI flux,
    //     and presenter transitions. Anything outside that HR set propagates
    //     as a real bug.
    //
    //   §6.7.3 iteration sibling-independence:
    //     Broad `catch (Exception)` is kept ONLY where one slot/iteration
    //     failure must not block forward progress on its siblings (closing
    //     guards, owned-window cascade, effect-flush loops in RenderContext).
    //     Each such site has an inline comment naming the contract.
    //
    //   try / finally for cleanup ordering:
    //     User-callback invocations followed by framework cleanup use
    //     try { Handler?.Invoke(...); } finally { ... }. The user's exception
    //     propagates (the developer sees their bug); the framework cleanup
    //     still runs (no stale references in the limp-along case where the
    //     app catches via Application.UnhandledException).
    //
    //   Purely-advisory user callbacks (SizeChanged, StateChanged, Closing,
    //   Closed) have NO try/catch — a throwing handler propagates to the
    //   dispatcher. Swallowing those just hides the developer's bug.
    private static int s_nextId;

    private readonly string _id;
    private readonly Window _window;
    private readonly AppWindow _appWindow;
    private readonly ReactorHost _host;
    private readonly nint _hwnd;
    private readonly WindowMessageMonitor _messageMonitor;
    private readonly Core.WindowPersistedScope _persistedScope = new();
    // HICON loaded by TryApplyExeIconFallback. We hold it for the window's
    // lifetime and DestroyIcon in Dispose — Microsoft.UI.Win32Interop
    // .GetIconIdFromIcon is a thin Windows.Graphics.IconId factory and does
    // not transfer HICON ownership (per
    // learn.microsoft.com/.../Microsoft.UI.Win32Interop.GetIconIdFromIcon —
    // "the caller is responsible for the lifetime of the icon handle"). The
    // AppWindow already holds its own reference internally by the time
    // SetIcon returns, so destruction at window-close is safe and avoids
    // leaking one HICON per window.
    private nint _exeFallbackHIcon;
    // Lazy-init shell wrappers — apps that never read these never instantiate
    // them, keeping the cold-start budget clean (spec 036 §0.7 / §11.7).
    private TaskbarProgress? _taskbarProgress;
    private TaskbarOverlay? _taskbarOverlay;
    private Hosting.Shell.ThumbnailToolbarState? _thumbnailToolbar;
    private readonly object _shellLock = new();
    // Owned windows (this window's children). Copy-on-write so the cascade
    // path can iterate without holding a lock during user-supplied close
    // handlers / guards.
    private ReactorWindow[] _ownedWindows = global::System.Array.Empty<ReactorWindow>();
    private readonly object _ownedLock = new();
    private WindowSpec _spec;
    private uint _dpi = 96;
    private int _stateValue; // backing storage for State (cast WindowState <-> int)
    private bool _disposed;
    private bool _userResized; // Phase 2: once true we no longer overwrite size on DPI events.
    private bool _firstDpiApplied;
    private bool _persistenceRestoreAttempted;
    private WindowCloseReason _closingReason = WindowCloseReason.UserClosed;

    /// <summary>Stable id, e.g. <c>"win-3"</c>. Allocated monotonically per process.</summary>
    public string Id => _id;

    /// <summary>Optional stable identity (from <see cref="WindowSpec.Key"/>).</summary>
    public WindowKey? Key => Volatile.Read(ref _spec).Key;

    /// <summary>The underlying WinUI <see cref="Microsoft.UI.Xaml.Window"/>.</summary>
    public Window NativeWindow => _window;

    /// <summary>The WinUI <see cref="AppWindow"/> for this window.</summary>
    public AppWindow AppWindow => _appWindow;

    /// <summary>The <see cref="ReactorHost"/> driving this window's render loop.</summary>
    public ReactorHost Host => _host;

    /// <summary>
    /// Per-window persisted-state scope. Bounded by this window's lifetime —
    /// disposed when the window closes. Used by
    /// <see cref="RenderContext.UsePersisted{T}(string, T, PersistedScope)"/>
    /// when <see cref="PersistedScope.Window"/> is requested. (spec 036 §3.4 /
    /// §4.4)
    /// </summary>
    public Core.WindowPersistedScope PersistedScope => _persistedScope;

    /// <summary>Last applied <see cref="WindowSpec"/> snapshot.</summary>
    public WindowSpec Spec => Volatile.Read(ref _spec);

    /// <summary>
    /// Taskbar progress indicator for this window. Lazily allocated on first
    /// read; apps that never touch it pay no shell-COM init cost.
    /// (spec 036 §11.1)
    /// </summary>
    public TaskbarProgress Progress
    {
        get
        {
            var existing = Volatile.Read(ref _taskbarProgress);
            if (existing is not null) return existing;
            lock (_shellLock)
            {
                if (_taskbarProgress is not null) return _taskbarProgress;
                _taskbarProgress = new TaskbarProgress(_hwnd, () => _disposed);
                return _taskbarProgress;
            }
        }
    }

    /// <summary>
    /// Taskbar overlay icon ("badge"). Lazily allocated. (spec 036 §11.2)
    /// </summary>
    public TaskbarOverlay Overlay
    {
        get
        {
            var existing = Volatile.Read(ref _taskbarOverlay);
            if (existing is not null) return existing;
            lock (_shellLock)
            {
                if (_taskbarOverlay is not null) return _taskbarOverlay;
                _taskbarOverlay = new TaskbarOverlay(_hwnd, () => _disposed);
                return _taskbarOverlay;
            }
        }
    }

    /// <summary>Per-window DPI in raw units (96, 120, 144, 192, ...). Phase 2 makes this observable.</summary>
    public uint Dpi
    {
        get => Volatile.Read(ref _dpi);
        internal set => Volatile.Write(ref _dpi, value);
    }

    /// <summary>DIP scale factor (Dpi / 96). 1.0 at 100%, 1.5 at 150%, 2.0 at 200%.</summary>
    public double DipScale => Dpi / 96.0;

    /// <summary>Coarse window state.</summary>
    public WindowState State
    {
        get => (WindowState)Volatile.Read(ref _stateValue);
        internal set => Volatile.Write(ref _stateValue, (int)value);
    }

    /// <summary>Whether the window is currently shown (post <see cref="Activate"/> / pre <see cref="Hide"/>).</summary>
    public bool IsVisible
    {
        get => Volatile.Read(ref _isVisibleFlag) != 0;
        internal set => Volatile.Write(ref _isVisibleFlag, value ? 1 : 0);
    }
    private int _isVisibleFlag;

    /// <summary>Whether the window currently holds activation.</summary>
    public bool IsActive
    {
        get => Volatile.Read(ref _isActiveFlag) != 0;
        internal set => Volatile.Write(ref _isActiveFlag, value ? 1 : 0);
    }
    private int _isActiveFlag;

    // ── events ─────────────────────────────────────────────────────────
    // Phase 1 wires Activated / Deactivated / Closed; Phases 2-3 add the rest.
#pragma warning disable CS0067 // event declared in Phase 1 surface; raisers land in Phases 2-3.

    /// <summary>Fires on the UI thread when the window's DIP size changes. (Phase 3)</summary>
    public event EventHandler<WindowDipSizeChangedEventArgs>? SizeChanged;

    /// <summary>Fires on the UI thread when per-window DPI changes. (Phase 2)</summary>
    public event EventHandler<uint>? DpiChanged;

    /// <summary>Fires on the UI thread when <see cref="State"/> changes. (Phase 3)</summary>
    public event EventHandler<WindowState>? StateChanged;

    /// <summary>
    /// Fires on the UI thread before the window closes. Set
    /// <see cref="WindowClosingEventArgs.Cancel"/> to abort. Synchronous —
    /// see <c>UseClosingGuard</c> for the async pattern. (Phase 3)
    /// </summary>
    public event EventHandler<WindowClosingEventArgs>? Closing;

#pragma warning restore CS0067

    /// <summary>Fires on the UI thread when the window gains activation.</summary>
    public event EventHandler? Activated;

    /// <summary>Fires on the UI thread when the window loses activation.</summary>
    public event EventHandler? Deactivated;

    /// <summary>Fires on the UI thread after the window closes and the host disposes.</summary>
    public event EventHandler? Closed;

    // ── construction ──────────────────────────────────────────────────

    /// <summary>
    /// Construct from a spec. Phase 1 — chrome / host are set up here; the
    /// caller invokes <see cref="MountAndActivate"/> after any pre-mount
    /// configuration (the legacy <c>Run&lt;TRoot&gt;.configure</c> hook).
    /// </summary>
    internal ReactorWindow(WindowSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        spec.Validate();

        _id = $"win-{Interlocked.Increment(ref s_nextId)}";
        _spec = spec;

        _window = new Window { Title = spec.Title };
        _appWindow = _window.AppWindow;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);

        // Snapshot initial per-window DPI before applying spec sizing so the
        // DIP -> physical conversion is correct on the first Resize call.
        _dpi = QueryDpiForWindow(_hwnd);

        ApplyChrome(spec, isInitial: true);

        _host = new ReactorHost(_window);
        _host.OwningWindow = this;
        // Seed the window-level backdrop default so the first render sees it
        // even if the root tree doesn't carry a BackdropChoice modifier.
        // (spec 036 §3.3)
        _host.BackdropApplier.SetWindowDefault(spec.Backdrop);

        // Subscribe before Activate() so WM_SHOWWINDOW / WM_DPICHANGED routed
        // during the first paint reach our handlers. The monitor is per-window
        // and disposed in our Dispose().
        _messageMonitor = new WindowMessageMonitor(_hwnd);
        _messageMonitor.MessageReceived += OnWindowMessage;

        _window.Activated += OnNativeActivated;
        _window.SizeChanged += OnNativeSizeChanged;
        _appWindow.Changed += OnAppWindowChanged;
        _appWindow.Closing += OnAppWindowClosing;
        _window.Closed += OnNativeClosed;

        // Snapshot initial state from the realized presenter.
        _stateValue = (int)ResolveCurrentState();
    }

    private WindowState ResolveCurrentState()
    {
        try
        {
            switch (_appWindow.Presenter)
            {
                case OverlappedPresenter op:
                    return op.State switch
                    {
                        OverlappedPresenterState.Minimized => Microsoft.UI.Reactor.WindowState.Minimized,
                        OverlappedPresenterState.Maximized => Microsoft.UI.Reactor.WindowState.Maximized,
                        _ => Microsoft.UI.Reactor.WindowState.Normal,
                    };
                case Microsoft.UI.Windowing.FullScreenPresenter:
                    return Microsoft.UI.Reactor.WindowState.FullScreen;
                case CompactOverlayPresenter:
                    return Microsoft.UI.Reactor.WindowState.CompactOverlay;
            }
        }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        {
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.ResolveCurrentState", ex);
        }
        return Microsoft.UI.Reactor.WindowState.Normal;
    }

    private static uint QueryDpiForWindow(nint hwnd)
    {
        // P/Invoke on nint cannot throw at the marshal layer; both
        // GetDpiForWindow and GetDpiForSystem signal failure via a 0
        // return value, handled inline. No try/catch needed.
        uint dpi = NativeDpi.GetDpiForWindow(hwnd);
        if (dpi == 0)
            dpi = NativeDpi.GetDpiForSystemFallback();
        return dpi == 0 ? 96 : dpi;
    }

    private static class NativeDpi
    {
        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(nint hwnd);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForSystem();

        public static uint GetDpiForSystemFallback() => GetDpiForSystem();
    }

    /// <summary>
    /// Mount the supplied root and (optionally) activate the window. Pass
    /// exactly one of <paramref name="rootFactory"/> / <paramref name="renderFunc"/>.
    /// </summary>
    internal void MountAndActivate(Func<Component>? rootFactory, Func<RenderContext, Element>? renderFunc)
    {
        if ((rootFactory is null) == (renderFunc is null))
            throw new ArgumentException(
                "Exactly one of rootFactory / renderFunc must be supplied.", nameof(rootFactory));

        if (rootFactory is not null)
            _host.Mount(rootFactory());
        else
            _host.Mount(renderFunc!);

        if (_spec.ActivateOnOpen && !_disposed)
            _window.Activate();
    }

    private void ApplyChrome(WindowSpec spec, bool isInitial)
    {
        try { _window.Title = spec.Title; }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        {
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.Title.set", ex);
        }

        // Presenter: full-screen / compact-overlay flip via AppWindow.SetPresenter.
        // Default Overlapped chrome modulators (resizable, minimizable, maximizable,
        // alwaysOnTop) only apply to OverlappedPresenter.
        try
        {
            switch (spec.Presenter)
            {
                case PresenterKind.FullScreen:
                    _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                    break;
                case PresenterKind.CompactOverlay:
                    _appWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay);
                    break;
                default:
                    _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                    if (_appWindow.Presenter is OverlappedPresenter op)
                    {
                        op.IsResizable = spec.IsResizable;
                        op.IsMinimizable = spec.IsMinimizable;
                        op.IsMaximizable = spec.IsMaximizable;
                        op.IsAlwaysOnTop = spec.IsAlwaysOnTop;
                    }
                    break;
            }
        }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        {
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.Presenter.apply", ex);
        }

        try
        {
            // Owned windows hide from the taskbar / Alt-Tab switcher by
            // default — that's the conventional shell behavior for owned
            // top-level windows (about box, settings, picker). The
            // IsShownInSwitchers bool only flips when the owner is null;
            // owned windows ignore it. (spec 036 §9)
            _appWindow.IsShownInSwitchers = spec.Owner is null && spec.IsShownInSwitchers;
        }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        {
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.IsShownInSwitchers.set", ex);
        }

        try { _window.ExtendsContentIntoTitleBar = spec.ExtendsContentIntoTitleBar; }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        {
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.ExtendsContentIntoTitleBar.set", ex);
        }

        // Sizing — DIP -> physical at the current per-window DPI. (spec 036 §5.1)
        if (isInitial && spec.Presenter == PresenterKind.Overlapped)
        {
            try
            {
                _appWindow.Resize(DipToPhysicalSize(spec.Width, spec.Height));
            }
            catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
            {
                DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.InitialResize", ex);
            }
        }

        if (spec.Icon is { } icon)
            icon.Apply(_appWindow);
        else if (isInitial)
            TryApplyExeIconFallback();

        // Spec 045 §2.6 tear-off — window-wide alpha via WS_EX_LAYERED +
        // SetLayeredWindowAttributes. Skipped when Opacity==1.0 so opaque
        // windows pay zero layering overhead (Windows compositor fast-path).
        ApplyOpacity(spec.Opacity);

        // Spec 045 §2.6 tear-off — NoActivate must be applied before
        // Activate fires (in MountAndActivate) so the window's first show
        // observes the flag. Re-applied on Update so flips stick.
        SetNoActivate(spec.NoActivate);
        SetIgnorePointerInput(spec.IgnorePointerInput);

        // Owner relationship — only meaningful at initial apply time.
        // Subsequent Update calls do not re-parent (changing ownership of a
        // realized window has no AppWindow API and is rarely the right thing
        // for an app to do). (spec 036 §9)
        if (isInitial && spec.Owner is { } owner && !owner._disposed)
        {
            try
            {
                NativeOwnership.SetOwner(_hwnd, owner._hwnd);
            }
            catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
            {
                DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.SetOwner", ex);
            }
            owner.AddOwned(this);
        }
    }

    /// <summary>Owner-window list snapshot. Copy-on-write under <see cref="_ownedLock"/>.</summary>
    internal IReadOnlyList<ReactorWindow> OwnedWindows => Volatile.Read(ref _ownedWindows);

    private void AddOwned(ReactorWindow child)
    {
        lock (_ownedLock)
        {
            var current = Volatile.Read(ref _ownedWindows);
            if (Array.IndexOf(current, child) >= 0) return;
            var next = new ReactorWindow[current.Length + 1];
            Array.Copy(current, next, current.Length);
            next[^1] = child;
            Volatile.Write(ref _ownedWindows, next);
        }
    }

    private void RemoveOwned(ReactorWindow child)
    {
        lock (_ownedLock)
        {
            var current = Volatile.Read(ref _ownedWindows);
            int idx = Array.IndexOf(current, child);
            if (idx < 0) return;
            var next = new ReactorWindow[current.Length - 1];
            if (idx > 0) Array.Copy(current, 0, next, 0, idx);
            if (idx < current.Length - 1) Array.Copy(current, idx + 1, next, idx, current.Length - idx - 1);
            Volatile.Write(ref _ownedWindows, next);
        }
    }

    /// <summary>
    /// Best-effort: when no explicit <see cref="WindowSpec.Icon"/> was supplied,
    /// load the first icon embedded in the running executable's PE resources
    /// (the one the build wired in via <c>&lt;ApplicationIcon&gt;</c>) and
    /// apply it to the AppWindow so the taskbar / Alt-Tab / Win11 thumbnail
    /// show the developer's icon instead of the WinUI default.
    /// </summary>
    /// <remarks>
    /// <para>Skipped under MSIX-packaged execution — packaged apps get their
    /// AppWindow icon from <c>Package.appxmanifest</c>'s
    /// <c>VisualElements</c> tiles automatically; overriding here would just
    /// fight the manifest. Unpackaged apps have no manifest to fall back to,
    /// so the EXE PE resource is the next best source.</para>
    /// <para>Failures are silent — if there's no embedded icon, the AppWindow
    /// keeps its default. (spec 036 §4.1 — implementation-time addition)</para>
    /// </remarks>
    private void TryApplyExeIconFallback()
    {
        try
        {
            // Packaged apps: the manifest's Square*Logo assets are the
            // canonical icon source; let the platform resolve them.
            if (Hosting.Shell.PackageRuntime.IsPackaged) return;

            var exePath = global::System.Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            // LR_LOADFROMFILE on a .exe path loads the first icon group
            // from its PE resources. LR_DEFAULTSIZE picks the system
            // default size (usually 32x32) — Windows will scale to the
            // taskbar's needs from there.
            var hIcon = NativeIcon.LoadImageW(0, exePath, NativeIcon.IMAGE_ICON,
                0, 0, NativeIcon.LR_LOADFROMFILE | NativeIcon.LR_DEFAULTSIZE);
            if (hIcon == 0) return;

            var iconId = Microsoft.UI.Win32Interop.GetIconIdFromIcon(hIcon);
            _appWindow.SetIcon(iconId);
            // Stash the HICON for Dispose to free — see field comment for
            // ownership rationale.
            _exeFallbackHIcon = hIcon;
        }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        {
            // _appWindow.SetIcon during teardown reentry — the only WinRT call
            // in the try that can plausibly fail here. LoadImageW returns 0 on
            // failure (handled inline) and GetIconIdFromIcon is non-throwing.
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.TryApplyExeIconFallback", ex);
        }
    }

    private static class NativeIcon
    {
        public const uint IMAGE_ICON = 1;
        public const uint LR_LOADFROMFILE = 0x00000010;
        public const uint LR_DEFAULTSIZE = 0x00000040;

        [global::System.Runtime.InteropServices.DllImport("user32.dll", CharSet = global::System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        public static extern nint LoadImageW(nint hInst,
            [global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.LPWStr)] string lpszName,
            uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [global::System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool DestroyIcon(nint hIcon);
    }

    private static class NativeOwnership
    {
        // GWLP_HWNDPARENT — the owner-window slot. Distinct from the
        // GWLP_PARENT used by child controls (which we never want for top-
        // level windows). 64-bit Reactor builds always use SetWindowLongPtrW.
        private const int GWLP_HWNDPARENT = -8;

        [global::System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

        public static void SetOwner(nint child, nint owner)
        {
            if (child == 0 || owner == 0) return;
            _ = SetWindowLongPtr(child, GWLP_HWNDPARENT, owner);
        }
    }

    private global::Windows.Graphics.SizeInt32 DipToPhysicalSize(double widthDip, double heightDip)
    {
        var dpi = Dpi == 0 ? 96 : Dpi;
        return new global::Windows.Graphics.SizeInt32(
            (int)Math.Round(widthDip * dpi / 96.0),
            (int)Math.Round(heightDip * dpi / 96.0));
    }

    private global::Windows.Graphics.PointInt32 DipToPhysicalPoint(double xDip, double yDip)
    {
        var dpi = Dpi == 0 ? 96 : Dpi;
        return new global::Windows.Graphics.PointInt32(
            (int)Math.Round(xDip * dpi / 96.0),
            (int)Math.Round(yDip * dpi / 96.0));
    }

    private void OnWindowMessage(object? sender, WindowMessageEventArgs args)
    {
        switch (args.Msg)
        {
            case WindowMessageMonitor.WM_DPICHANGED:
                {
                    // wParam.HIWORD = newDPI Y, wParam.LOWORD = newDPI X. Both are
                    // identical on every system Reactor will run on; the OS only
                    // splits them for legacy 16-bit alignment.
                    var newDpi = (uint)(args.WParam & 0xFFFF);
                    if (newDpi == 0) newDpi = 96;
                    var prevDpi = Dpi;
                    Dpi = newDpi;
                    if (newDpi != prevDpi)
                        DpiChanged?.Invoke(this, newDpi);

                    // First DPI report after window creation: re-apply spec
                    // sizing against the now-known per-window DPI, but only if
                    // the user hasn't already resized the window manually.
                    if (!_userResized && !_firstDpiApplied)
                    {
                        _firstDpiApplied = true;
                        try
                        {
                            _appWindow.Resize(DipToPhysicalSize(_spec.Width, _spec.Height));
                        }
                        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
                        {
                            DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.FirstDpiResize", ex);
                        }
                    }
                    break;
                }
            case WindowMessageMonitor.WM_GETMINMAXINFO:
                ApplyMinMaxInfo(args);
                break;
            case WindowMessageMonitor.WM_SIZING:
            case WindowMessageMonitor.WM_EXITSIZEMOVE:
                _userResized = true;
                break;
            case WindowMessageMonitor.WM_SHOWWINDOW:
                if (args.WParam != 0)
                {
                    IsVisible = true;
                    TryApplyInitialPlacement();
                }
                else IsVisible = false;
                break;
            case WindowMessageMonitor.WM_COMMAND:
                {
                    // Thumbnail-toolbar clicks arrive as WM_COMMAND with the
                    // button's iId in the LOWORD of wParam. The HIWORD is the
                    // notification code (0 for thumb buttons, but we don't
                    // filter on it — non-thumb commands are just ignored when
                    // the iId doesn't match a slot). (spec 036 §11.5)
                    var bar = Volatile.Read(ref _thumbnailToolbar);
                    if (bar is null) break;
                    var slot = (uint)(args.WParam & 0xFFFF);
                    if (bar.TryDispatchClick(slot))
                    {
                        args.Handled = true;
                        args.Result = 0;
                    }
                    break;
                }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    private unsafe void ApplyMinMaxInfo(WindowMessageEventArgs args)
    {
        var spec = _spec;
        // Skip when nothing is constrained — let WinUI's default min/max stand.
        if (spec.MinWidth is null && spec.MinHeight is null && spec.MaxWidth is null && spec.MaxHeight is null)
            return;

        // Pointer dereferences only — no API call here that throws. An
        // invalid args.LParam would crash via AccessViolationException
        // (which doesn't reach managed catches anyway); the inline null
        // check guards the only correctable case.
        var info = (MINMAXINFO*)args.LParam;
        if (info == null) return;
        var dpi = Dpi == 0 ? 96 : Dpi;

        int DipToPxScalar(double dip) => (int)Math.Round(dip * dpi / 96.0);

        if (spec.MinWidth is { } mnw) info->ptMinTrackSize.X = DipToPxScalar(mnw);
        if (spec.MinHeight is { } mnh) info->ptMinTrackSize.Y = DipToPxScalar(mnh);
        if (spec.MaxWidth is { } mxw) info->ptMaxTrackSize.X = DipToPxScalar(mxw);
        if (spec.MaxHeight is { } mxh) info->ptMaxTrackSize.Y = DipToPxScalar(mxh);
        args.Handled = true;
        args.Result = 0;
    }

    private void OnNativeActivated(object? sender, WindowActivatedEventArgs args)
    {
        bool isActive = args.WindowActivationState != WindowActivationState.Deactivated;
        bool wasActive = IsActive;
        IsActive = isActive;
        IsVisible = true;
        if (isActive && !wasActive)
            Activated?.Invoke(this, EventArgs.Empty);
        else if (!isActive && wasActive)
            Deactivated?.Invoke(this, EventArgs.Empty);
    }

    private void OnNativeSizeChanged(object sender, Microsoft.UI.Xaml.WindowSizeChangedEventArgs args)
    {
        // Pure advisory dispatch. A throwing handler propagates to the
        // dispatcher's UnhandledException pipeline — the developer's bug
        // is theirs to see; wrapping it would just hide it.
        // Window.Bounds is already DIPs (the WinUI XAML rendering surface).
        var dip = (args.Size.Width, args.Size.Height);
        SizeChanged?.Invoke(this, new WindowDipSizeChangedEventArgs(dip, args));
    }

    private void OnAppWindowChanged(AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        if (!args.DidPresenterChange && !args.DidVisibilityChange) return;
        var newState = ResolveCurrentState();
        var prev = (WindowState)Volatile.Read(ref _stateValue);
        if (newState != prev)
        {
            Volatile.Write(ref _stateValue, (int)newState);
            // Pure advisory dispatch. A throwing handler propagates to the
            // dispatcher; we've already updated _stateValue, so the framework
            // invariant is held regardless of whether the user crashes.
            StateChanged?.Invoke(this, newState);
        }
    }

    private void OnAppWindowClosing(AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        var reason = _closingReason; // populated by Close()/Exit() / OwnerClosed cascade.
        var cea = new WindowClosingEventArgs(reason);

        // Run UseClosingGuard registrations first — any returning false
        // cancels. Snapshot the list so a guard's cleanup that mutates
        // the registration list mid-iteration doesn't crash.
        ClosingGuard[] guards;
        lock (_closingGuardsLock) { guards = _closingGuards.ToArray(); }
        bool cancel = false;
        for (int i = 0; i < guards.Length; i++)
        {
            try { if (!guards[i].CanClose()) { cancel = true; break; } }
            // User-callback isolation (spec 044 §6.7.3): IClosingGuard.CanClose
            // is app code — a throwing guard is fail-safed to "cancel" rather
            // than allowed to crash the close. (spec 036 §3.4 tests pin this.)
            catch (Exception ex)
            {
                DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.ClosingGuard.dispatch", ex);
                cancel = true;
                break;
            }
        }

        if (!cancel)
        {
            // Closing is app code. A throwing handler propagates: previous
            // behavior swallowed the throw and proceeded with close (silently
            // treating the bug as "didn't cancel") which is worse than
            // crashing — the developer needs to see their bug.
            Closing?.Invoke(this, cea);
            cancel = cea.Cancel;
        }

        // Owner-close cascade: if this window has owned children, try to
        // close them first under reason=OwnerClosed. If any owned guard
        // cancels, the owner-close cancels too. (spec 036 §9)
        if (!cancel)
        {
            var owned = OwnedWindows;
            for (int i = 0; i < owned.Count; i++)
            {
                var child = owned[i];
                if (child._disposed) continue;
                child._closingReason = WindowCloseReason.OwnerClosed;
                try { child._window.Close(); }
                // Iteration sibling-independence (spec 044 §6.7.3): one
                // failing child must not abort the cascade across its
                // siblings. The Window.Close call also re-enters the child's
                // own Closing/Closed dispatch — its user handlers now
                // propagate (per the SizeChanged/StateChanged/Closing/Closed
                // rule above), so this broad catch is the cascade-loop
                // protection that keeps the OWNER's close attempt sane even
                // when a single child's handler crashes.
                catch (Exception ex) { DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.OwnedWindow.Close", ex); }
                // After Close(), if the child is still alive (a guard
                // cancelled), abort the owner close.
                if (!child._disposed)
                {
                    cancel = true;
                    break;
                }
            }
        }

        if (cancel) args.Cancel = true;
        else _closingReason = WindowCloseReason.UserClosed; // reset for the next attempt
    }

    // ── UseClosingGuard registration ──────────────────────────────────
    private sealed class ClosingGuard
    {
        public Func<bool> CanClose { get; }
        public ClosingGuard(Func<bool> fn) { CanClose = fn; }
    }
    private readonly object _closingGuardsLock = new();
    private readonly List<ClosingGuard> _closingGuards = new();

    /// <summary>
    /// Register a synchronous "can the window close right now?" predicate.
    /// Returns an unregister token that must run during the calling
    /// component's cleanup. Multiple guards stack — any returning <c>false</c>
    /// cancels the close. (spec 036 §7 / §3.4)
    /// </summary>
    internal IDisposable RegisterClosingGuard(Func<bool> canClose)
    {
        ArgumentNullException.ThrowIfNull(canClose);
        var guard = new ClosingGuard(canClose);
        lock (_closingGuardsLock) { _closingGuards.Add(guard); }
        return new GuardToken(this, guard);
    }

    private sealed class GuardToken : IDisposable
    {
        private readonly ReactorWindow _owner;
        private ClosingGuard? _guard;
        public GuardToken(ReactorWindow owner, ClosingGuard guard) { _owner = owner; _guard = guard; }
        public void Dispose()
        {
            var g = Interlocked.Exchange(ref _guard, null);
            if (g is null) return;
            lock (_owner._closingGuardsLock) { _owner._closingGuards.Remove(g); }
        }
    }

    private void OnNativeClosed(object? sender, WindowEventArgs args)
    {
        if (_disposed) return;

        // Save BEFORE disposing the host — at this point the HWND is still
        // alive but the close is irrevocable, so GetWindowPlacement returns
        // the user's last interactive size/position. Best-effort.
        TrySavePersistedPlacement();

        // try/finally so framework cleanup (RemoveOwned, UnregisterWindow,
        // Dispose) runs regardless of whether the user's Closed handler
        // throws. The user's exception still propagates to the dispatcher
        // (the developer sees their bug); but for the limp-along case where
        // the app sets Application.UnhandledException += (..., Handled = true)
        // we don't leave stale window references behind.
        try
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            // Detach from the owner's child-list so a later owner-close cascade
            // doesn't iterate over an already-closed pointer. (spec 036 §9)
            var spec = _spec;
            spec.Owner?.RemoveOwned(this);

            ReactorApp.UnregisterWindow(this);
            Dispose();
        }
    }

    // ── public mutators ───────────────────────────────────────────────

    /// <summary>
    /// Show and focus the window. UI-thread only. No-op after disposal.
    /// </summary>
    public void Activate()
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(Activate));
        if (_disposed) return;
        _window.Activate();
        IsVisible = true;
    }

    /// <summary>
    /// Hide the window without closing. UI-thread only. No-op after disposal.
    /// </summary>
    public void Hide()
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(Hide));
        if (_disposed) return;
        try { _appWindow.Hide(); }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        { DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.Hide", ex); }
        IsVisible = false;
    }

    /// <summary>
    /// Show a previously hidden window. UI-thread only. No-op after disposal.
    /// </summary>
    public void Show()
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(Show));
        if (_disposed) return;
        try { _appWindow.Show(); }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        { DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.Show", ex); }
        IsVisible = true;
    }

    /// <summary>
    /// Close the window. UI-thread only. The <see cref="Closing"/> event
    /// (Phase 3) will run first; if any subscriber sets
    /// <see cref="WindowClosingEventArgs.Cancel"/> the close aborts.
    /// Idempotent — a second call after disposal is a no-op.
    /// </summary>
    public void Close()
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(Close));
        if (_disposed) return;
        _closingReason = WindowCloseReason.AppClosed;
        try { _window.Close(); }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        { DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.Close", ex); }
    }

    /// <summary>
    /// Diff <paramref name="next"/> against the current spec and apply only the
    /// fields that changed. UI-thread only.
    /// </summary>
    public void Update(WindowSpec next)
    {
        ArgumentNullException.ThrowIfNull(next);
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(Update));
        if (_disposed) throw new ObjectDisposedException(nameof(ReactorWindow));

        next.Validate();

        // Only re-apply chrome when something visible changed. Equality on the
        // record handles all simple scalar fields; reference-types (Icon,
        // Backdrop, Owner) compare by reference which is the right behavior here.
        var prev = _spec;
        Volatile.Write(ref _spec, next);
        if (!Equals(prev, next))
        {
            ApplyChrome(next, isInitial: false);
            // Re-seed backdrop default in case Update changed it. The next
            // render-pass Apply call will pick up the new default if the
            // tree carries no Backdrop modifier of its own. (spec 036 §3.3)
            if (!Equals(prev.Backdrop, next.Backdrop))
                _host.BackdropApplier.SetWindowDefault(next.Backdrop);
        }
    }

    /// <summary>Resize to <paramref name="width"/> x <paramref name="height"/> DIPs. UI-thread only.</summary>
    public void SetSize(double width, double height)
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(SetSize));
        if (_disposed) return;
        if (!(width > 0) || !(height > 0))
            throw new ArgumentOutOfRangeException(nameof(width), "Width and height must be positive.");
        // SetSize counts as a "user resize" — once the app code resizes, the
        // first-DPI re-apply path stops fighting it. (spec 036 §5.1)
        _userResized = true;
        try { _appWindow.Resize(DipToPhysicalSize(width, height)); }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        { DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.SetSize", ex); }
    }

    /// <summary>
    /// Move to <paramref name="x"/>,<paramref name="y"/> DIPs. UI-thread only.
    /// </summary>
    /// <remarks>
    /// The DIP→physical conversion uses the <b>current window's</b> DPI. On
    /// mixed-DPI multi-monitor setups, moving across to a monitor with a
    /// different scale factor can land at a slightly different physical
    /// position than a caller expects, because Windows virtual-screen
    /// coordinates are physical pixels with no global DIP coordinate space.
    /// For predictable cross-monitor placement, prefer <see cref="CenterOnScreen"/>
    /// (which resolves against the destination <see cref="DisplayArea"/>) or
    /// move in two steps — <c>SetPosition</c> onto the target monitor, then
    /// adjust within it. (spec 036 §5.2)
    /// </remarks>
    public void SetPosition(double x, double y)
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(SetPosition));
        if (_disposed) return;
        try { _appWindow.Move(DipToPhysicalPoint(x, y)); }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        { DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.SetPosition", ex); }
    }

    /// <summary>
    /// Set window-wide alpha in [0..1]. 1.0 strips the layered-window
    /// extended style; values below 1.0 install it and call
    /// <c>SetLayeredWindowAttributes</c>. UI-thread only. No-op after disposal.
    /// </summary>
    public void SetOpacity(double opacity)
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(SetOpacity));
        if (_disposed) return;
        if (!(opacity >= 0.0 && opacity <= 1.0) || double.IsNaN(opacity))
            throw new ArgumentOutOfRangeException(nameof(opacity), "Opacity must be in [0, 1].");
        ApplyOpacity(opacity);
        // Mirror into _spec so a subsequent Update() diff doesn't fight the
        // imperative call. Volatile.Write because Spec is read from any thread.
        var prev = Volatile.Read(ref _spec);
        Volatile.Write(ref _spec, prev with { Opacity = opacity });
    }

    private void ApplyOpacity(double opacity)
    {
        // Clamp defensively even though Validate() / SetOpacity already
        // checked — the Win32 LWA_ALPHA byte is [0..255].
        if (opacity < 0.0) opacity = 0.0;
        if (opacity > 1.0) opacity = 1.0;

        var current = NativeOpacity.GetWindowLongPtr(_hwnd, NativeOpacity.GWL_EXSTYLE);
        bool isLayered = ((long)current & NativeOpacity.WS_EX_LAYERED) != 0;

        if (opacity >= 1.0)
        {
            // Strip WS_EX_LAYERED so the compositor fast-path is restored.
            // Also strip WS_EX_TRANSPARENT — that style is only meaningful
            // on layered windows, so leaving it set after un-layering would
            // wedge the window in an inconsistent extended-style state.
            // Mirror IgnorePointerInput=false into _spec so Update() diffs
            // see the live state.
            long currentBits = (long)current;
            long strippedBits = currentBits & ~(NativeOpacity.WS_EX_LAYERED | NativeOpacity.WS_EX_TRANSPARENT);
            if (strippedBits != currentBits)
                _ = NativeOpacity.SetWindowLongPtr(_hwnd, NativeOpacity.GWL_EXSTYLE, (nint)strippedBits);
            var prevSpec = Volatile.Read(ref _spec);
            if (prevSpec.IgnorePointerInput)
                Volatile.Write(ref _spec, prevSpec with { IgnorePointerInput = false });
            return;
        }

        if (!isLayered)
        {
            nint withLayered = (nint)((long)current | NativeOpacity.WS_EX_LAYERED);
            _ = NativeOpacity.SetWindowLongPtr(_hwnd, NativeOpacity.GWL_EXSTYLE, withLayered);
        }
        byte alpha = (byte)Math.Round(opacity * 255.0);
        _ = NativeOpacity.SetLayeredWindowAttributes(_hwnd, 0, alpha, NativeOpacity.LWA_ALPHA);
    }

    /// <summary>
    /// Toggle the <c>WS_EX_NOACTIVATE</c> extended style on the underlying
    /// HWND. When set, the window appears without stealing foreground
    /// activation (matches VS tool-window / drag-preview behavior).
    /// UI-thread only. No-op after disposal.
    /// </summary>
    public void SetNoActivate(bool noActivate)
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(SetNoActivate));
        if (_disposed) return;
        var current = NativeOpacity.GetWindowLongPtr(_hwnd, NativeOpacity.GWL_EXSTYLE);
        long bits = (long)current;
        long updated = noActivate
            ? bits | NativeOpacity.WS_EX_NOACTIVATE
            : bits & ~NativeOpacity.WS_EX_NOACTIVATE;
        if (updated != bits)
            _ = NativeOpacity.SetWindowLongPtr(_hwnd, NativeOpacity.GWL_EXSTYLE, (nint)updated);
        // Mirror into _spec so Update() diffs see the live value.
        var prev = Volatile.Read(ref _spec);
        if (prev.NoActivate != noActivate)
            Volatile.Write(ref _spec, prev with { NoActivate = noActivate });
    }

    /// <summary>
    /// Toggle the <c>WS_EX_TRANSPARENT</c> extended style on the underlying
    /// HWND. When set, mouse events pass THROUGH the window to whatever's
    /// underneath. The window must already be layered (via
    /// <see cref="SetOpacity"/> with a value &lt; 1.0) when enabling —
    /// the OS only honors transparent on layered windows. UI-thread only.
    /// No-op after disposal.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="ignore"/> is true but the window is not
    /// currently layered. Call <see cref="SetOpacity"/> with a value &lt; 1.0
    /// first.
    /// </exception>
    public void SetIgnorePointerInput(bool ignore)
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(SetIgnorePointerInput));
        if (_disposed) return;
        var current = NativeOpacity.GetWindowLongPtr(_hwnd, NativeOpacity.GWL_EXSTYLE);
        long bits = (long)current;

        // Enabling transparent on a non-layered window is a silent no-op at
        // the OS level — reject up front rather than leave the caller with
        // a flag that doesn't do anything.
        if (ignore && (bits & NativeOpacity.WS_EX_LAYERED) == 0)
            throw new InvalidOperationException(
                "SetIgnorePointerInput(true) requires the window to be layered. " +
                "Call SetOpacity with a value < 1.0 first.");

        long updated = ignore
            ? bits | NativeOpacity.WS_EX_TRANSPARENT
            : bits & ~NativeOpacity.WS_EX_TRANSPARENT;
        if (updated != bits)
            _ = NativeOpacity.SetWindowLongPtr(_hwnd, NativeOpacity.GWL_EXSTYLE, (nint)updated);
        var prev = Volatile.Read(ref _spec);
        if (prev.IgnorePointerInput != ignore)
            Volatile.Write(ref _spec, prev with { IgnorePointerInput = ignore });
    }

    private static class NativeOpacity
    {
        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_LAYERED = 0x00080000;
        public const long WS_EX_NOACTIVATE = 0x08000000;
        public const long WS_EX_TRANSPARENT = 0x00000020;
        public const uint LWA_ALPHA = 0x00000002;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);
    }

    /// <summary>Center on the window's current monitor. UI-thread only.</summary>
    public void CenterOnScreen()
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(CenterOnScreen));
        if (_disposed) return;
        try
        {
            var area = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Nearest)?.WorkArea;
            if (area is null) return;
            int x = area.Value.X + (area.Value.Width - _appWindow.Size.Width) / 2;
            int y = area.Value.Y + (area.Value.Height - _appWindow.Size.Height) / 2;
            _appWindow.Move(new global::Windows.Graphics.PointInt32(x, y));
        }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        { DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.CenterOnScreen", ex); }
    }

    /// <summary>
    /// Replace the thumbnail-toolbar buttons for this window. Up to seven
    /// buttons; duplicate ids throw. The first call adds the button set, later
    /// calls diff and update only the changed slots. (spec 036 §11.5)
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when more than seven buttons are supplied or when ids are
    /// duplicated.
    /// </exception>
    public void SetThumbnailToolbar(IReadOnlyList<ThumbnailToolbarButton> buttons)
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(SetThumbnailToolbar));
        if (_disposed) throw new ObjectDisposedException(nameof(ReactorWindow));
        ArgumentNullException.ThrowIfNull(buttons);

        Hosting.Shell.ThumbnailToolbarState state;
        lock (_shellLock)
        {
            state = _thumbnailToolbar ??= new Hosting.Shell.ThumbnailToolbarState(_hwnd);
        }
        state.Replace(buttons);
    }

    /// <summary>
    /// Hide all thumbnail-toolbar buttons. Idempotent; safe to call before
    /// <see cref="SetThumbnailToolbar"/> has been called. (spec 036 §11.5)
    /// </summary>
    public void ClearThumbnailToolbar()
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(ClearThumbnailToolbar));
        if (_disposed) return;
        var state = Volatile.Read(ref _thumbnailToolbar);
        state?.Replace(global::System.Array.Empty<ThumbnailToolbarButton>());
    }

    /// <summary>Mount a new component root. UI-thread only.</summary>
    public void Mount(Component root)
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(Mount));
        if (_disposed) throw new ObjectDisposedException(nameof(ReactorWindow));
        ArgumentNullException.ThrowIfNull(root);
        _host.Mount(root);
    }

    /// <summary>Mount a new render-function root. UI-thread only.</summary>
    public void Mount(Func<RenderContext, Element> render)
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(Mount));
        if (_disposed) throw new ObjectDisposedException(nameof(ReactorWindow));
        ArgumentNullException.ThrowIfNull(render);
        _host.Mount(render);
    }

    // ── teardown ──────────────────────────────────────────────────────

    /// <summary>
    /// Idempotent dispose. Detaches event handlers and disposes the host.
    /// The native window has typically already been closed by this point —
    /// this is the cleanup that runs after Window.Closed fires.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Event unsubscription — these throw at most COMException when the
        // proxy is already disconnected (which is exactly the "we're tearing
        // down anyway" case). Narrow to the teardown-reentry HR set.
        try { _window.Activated -= OnNativeActivated; }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult)) { /* expected during teardown */ }
        try { _window.SizeChanged -= OnNativeSizeChanged; }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult)) { /* expected during teardown */ }
        try { _appWindow.Changed -= OnAppWindowChanged; }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult)) { /* expected during teardown */ }
        try { _appWindow.Closing -= OnAppWindowClosing; }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult)) { /* expected during teardown */ }
        try { _window.Closed -= OnNativeClosed; }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult)) { /* expected during teardown */ }

        // Cleanup chain: nested try/finally so all four disposes run even if
        // one throws, while the first exception still propagates. ReactorHost
        // and _persistedScope both have idempotent Dispose; double-dispose
        // is safe even if a downstream subscriber already disposed them.
        try { _messageMonitor.Dispose(); }
        finally
        {
            try { _host.Dispose(); }
            finally
            {
                try { _persistedScope.Dispose(); }
                finally
                {
                    // Release thumbnail-toolbar HICONs and clear the
                    // click-dispatch map so a late WM_COMMAND can't reach
                    // freed handlers. (spec 036 §11.5)
                    Volatile.Read(ref _thumbnailToolbar)?.Dispose();
                }
            }
        }

        // Free the EXE-fallback HICON if we loaded one. AppWindow keeps its
        // own internal reference, so post-Close destruction is safe.
        if (_exeFallbackHIcon != 0)
        {
            // DestroyIcon is a [DllImport] bool — cannot throw at the marshal
            // layer on an nint argument. Failure (handle already freed) returns
            // false silently, which is fine here.
            NativeIcon.DestroyIcon(_exeFallbackHIcon);
            _exeFallbackHIcon = 0;
        }
    }

    /// <summary>The reason the close currently in progress was initiated. Phase 3.</summary>
    internal WindowCloseReason ClosingReason => _closingReason;

    // ── Persistence + initial placement (spec 036 §3.2 / §8) ──────────

    /// <summary>
    /// On the first <c>WM_SHOWWINDOW</c>, apply the spec's
    /// <see cref="WindowSpec.StartPosition"/>. For
    /// <see cref="WindowStartPosition.RestoreFromPersistence"/> we read the
    /// persisted placement (if any) from
    /// <see cref="ReactorApp.WindowPersistenceStore"/> and re-apply it via
    /// <c>SetWindowPlacement</c>; on any of the failure modes we fall through
    /// to the default placement so the window still appears. For the other
    /// placement values we move/center the AppWindow against the resolved
    /// monitor's work area. Idempotent: subsequent shows take no action so a
    /// hide/show cycle preserves the user's interactive resize.
    /// (spec 036 §3.2 / §8)
    /// </summary>
    private void TryApplyInitialPlacement()
    {
        if (_persistenceRestoreAttempted) return;
        _persistenceRestoreAttempted = true;

        var spec = _spec;

        bool restored = false;
        if (spec.StartPosition == WindowStartPosition.RestoreFromPersistence
            || !string.IsNullOrEmpty(spec.PersistenceId))
        {
            restored = TryRestorePersistedPlacementCore(spec);
        }
        if (restored)
        {
            // The placement we just applied counts as a user-resized state
            // so the first-DPI re-apply path doesn't fight it.
            _userResized = true;
            return;
        }

        try
        {
            switch (spec.StartPosition)
            {
                case WindowStartPosition.Manual when spec.ManualPosition is { } pos:
                    _appWindow.Move(DipToPhysicalPoint(pos.X, pos.Y));
                    break;
                case WindowStartPosition.CenterOnPrimary:
                    CenterIn(DisplayArea.Primary);
                    break;
                case WindowStartPosition.CenterOnOwner:
                    CenterIn(ResolveOwnerDisplayArea(spec.Owner));
                    break;
                // Default and RestoreFromPersistence (with no saved data)
                // fall through to WinUI's default placement.
            }
        }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        {
            // _appWindow.Move / DisplayArea.GetFromWindowId during teardown
            // reentry. Anything outside this HR set is a real bug.
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.TryApplyInitialPlacement", ex);
        }
    }

    private bool TryRestorePersistedPlacementCore(WindowSpec spec)
    {
        if (string.IsNullOrEmpty(spec.PersistenceId)) return false;
        var store = ReactorApp.ResolvePersistenceStore();
        if (store is null) return false;

        // All three downstream calls now signal failure via a return value
        // rather than throwing — store.TryRead is narrowed inside the store
        // (see C.5 audit entries for JsonFileStore / PackagedSettingsStore),
        // MonitorEnumeration.Snapshot has no failure modes that surface as
        // exceptions on managed nint args, and WindowPlacementCodec.Restore
        // catches IOException internally and returns false. No outer catch
        // needed; a propagating exception would be a genuine bug.
        if (!store.TryRead(spec.PersistenceId!, out var data) || data is null)
            return false;
        var monitors = MonitorEnumeration.Snapshot();
        // Fingerprint mismatch / malformed payload returns false; caller
        // falls back to spec's default placement.
        return WindowPlacementCodec.Restore(_hwnd, data, monitors);
    }

    private void CenterIn(DisplayArea? area)
    {
        if (area is null) return;
        var work = area.WorkArea;
        var size = _appWindow.Size;
        int x = work.X + Math.Max(0, (work.Width - size.Width) / 2);
        int y = work.Y + Math.Max(0, (work.Height - size.Height) / 2);
        _appWindow.Move(new global::Windows.Graphics.PointInt32(x, y));
    }

    private static DisplayArea? ResolveOwnerDisplayArea(ReactorWindow? owner)
    {
        if (owner is null || owner._disposed) return DisplayArea.Primary;
        try { return DisplayArea.GetFromWindowId(owner._appWindow.Id, DisplayAreaFallback.Nearest); }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        {
            // Owner already torn down between the _disposed check and the
            // WinRT call — fall back to primary display.
            return DisplayArea.Primary;
        }
    }

    /// <summary>
    /// Capture the current placement on close into the persistence store.
    /// Best-effort: failures log and don't bubble into the close path.
    /// (spec 036 §8)
    /// </summary>
    private void TrySavePersistedPlacement()
    {
        var spec = _spec;
        if (string.IsNullOrEmpty(spec.PersistenceId)) return;

        var store = ReactorApp.ResolvePersistenceStore();
        if (store is null) return;

        // Same shape as TryRestorePersistedPlacementCore — every downstream
        // failure mode now returns a sentinel value (null/false) rather than
        // throwing. store.Write narrows internally per the C.5 audit entry.
        var monitors = MonitorEnumeration.Snapshot();
        var payload = WindowPlacementCodec.Capture(_hwnd, monitors);
        if (payload is null) return;
        store.Write(spec.PersistenceId!, payload);
    }
}
