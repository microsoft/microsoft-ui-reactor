using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Hosting.Messaging;
using Microsoft.UI.Reactor.Hosting.Persistence;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

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
/// (<see cref="Spec"/>, <see cref="Dpi"/>, <see cref="Position"/>,
/// <see cref="State"/>, <see cref="IsVisible"/>, <see cref="IsActive"/>)
/// snapshot a <c>Volatile.Read</c> field and are safe from any thread.</para>
/// <para>Disposal is idempotent — a second <see cref="Close"/> or
/// <see cref="Dispose"/> is a no-op, not an exception.</para>
/// </remarks>
public sealed partial class ReactorWindow : IDisposable
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

#pragma warning disable CS0649 // Assigned by test code via InternalsVisibleTo.
    internal static bool SuppressDragMoveTimerForTests;
#pragma warning restore CS0649
    internal static int BeginDragMovePostCountForTests;

    private readonly string _id;
    private readonly Window _window;
    private readonly AppWindow _appWindow;
    private readonly ReactorHost _host;
    private readonly nint _hwnd;
    private readonly WindowMessageMonitor _messageMonitor;
    private readonly EmbedHostWatchdog? _embedWatchdog;
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

    // Set once the fallback has looked for an icon, whether or not it found one, so a
    // window with no icon source anywhere does not re-probe on every chrome update.
    private bool _exeFallbackAttempted;

    // True once Reactor has put an icon on this window (declared or fallback). Gates
    // ClearWindowIcon so Reactor only ever takes down an icon it applied itself.
    private bool _reactorAppliedIcon;
    // Lazy-init shell wrappers — apps that never read these never instantiate
    // them, keeping the cold-start budget clean (spec 036 §0.7 / §11.7).
    private TaskbarProgress? _taskbarProgress;
    private TaskbarOverlay? _taskbarOverlay;
    private TaskbarItem? _taskbarItem;
    private Hosting.Shell.ThumbnailToolbarState? _thumbnailToolbar;
    private readonly object _shellLock = new();
    // Owned windows (this window's children). Copy-on-write so the cascade
    // path can iterate without holding a lock during user-supplied close
    // handlers / guards.
    private ReactorWindow[] _ownedWindows = global::System.Array.Empty<ReactorWindow>();
    private readonly object _ownedLock = new();
    private WindowSpec _spec;
    private uint _dpi = 96;
    private DipPositionSnapshot _position = new(0, 0);
    private int _stateValue; // backing storage for State (cast WindowState <-> int)
    private bool _disposed;
    // Issue #647 — latched the first time the native Window.Close() is requested
    // for this window, on any path (programmatic Close, the owner-close cascade,
    // or ReactorApp exit). A second native close on an already-closing/closed
    // window re-enters native teardown and faults with an ACCESS_VIOLATION
    // (0xC0000005), poisoning later windows in the same process. All native
    // closes route through CloseNativeWindowOnce so the underlying Window.Close()
    // happens at most once.
    private bool _nativeCloseRequested;
    private bool _userResized; // Phase 2: once true we no longer overwrite size on DPI events.
    private bool _firstDpiApplied;
    private bool _persistenceRestoreAttempted;
    private WindowCloseReason _closingReason = WindowCloseReason.UserClosed;
    // Issue #537 — the WinUI Microsoft.UI.Xaml.Controls.TitleBar control corrupts
    // the heap (STATUS_HEAP_CORRUPTION) during its teardown when the window's
    // ExtendsContentIntoTitleBar is false. The control is built for the
    // content-extended (custom title bar) mode and only releases its
    // caption-button / AppWindow interop cleanly in that mode. Reactor lets an
    // app keep ExtendsContentIntoTitleBar=false while still rendering a
    // TitleBar(...) element (Reactor then skips SetTitleBar), so the control can
    // reach the unsafe teardown on close. We record that such a control is
    // mounted and flip the window back into the content-extended mode just before
    // the native close (see PrepareTitleBarForClose) so the control tears down
    // via its safe path while the HWND/AppWindow are still alive.
    //
    // NOTE: _titleBarControlPresent is an "ever mounted" latch and is deliberately
    // never cleared — teardown safety wants the conservative answer, since a
    // control that was mounted at any point may still be mid-teardown. Anything
    // that needs "is one mounted right now" must use _titleBarControlMounted.
    private bool _titleBarControlPresent;
    private bool _titleBarTeardownPrepared;
    // Current mount state, cleared on TitleBar unmount. Drives the
    // ExtendsContentIntoTitleBar inference, which must be withdrawable: a window
    // that merely *used to* host a TitleBar should not stay content-extended
    // forever on a spec that leaves the flag unset. (issue #917)
    private bool _titleBarControlMounted;

    // Issue #917 — declarative caption height. The spec value wins; a mounted
    // TitleBar(...) element supplies the fallback. _appliedTitleBarHeight tracks
    // what Reactor last wrote so removing the declaration resets to Standard
    // instead of stranding the previous value.
    private WindowTitleBarHeight? _specTitleBarHeight;
    private WindowTitleBarHeight? _elementTitleBarHeight;
    private WindowTitleBarHeight? _appliedTitleBarHeight;
    // The height Reactor last actually wrote to the caption (Standard when the
    // declaration was removed, or when the window is not content-extended and
    // the write was skipped). The mounted TitleBar control is sized to *this*,
    // not to the declaration, so the two halves never disagree.
    private WindowTitleBarHeight? _effectiveTitleBarHeight;
    private WeakReference<FrameworkElement>? _titleBarControl;
    private bool _titleBarControlExplicitHeight;
    private bool _titleBarControlHeightOwned;
    private RECT _lastSizingRect;
    private readonly object _aspectRatioOverrideLock = new();
    private AspectRatioOverride[] _aspectRatioOverrides = global::System.Array.Empty<AspectRatioOverride>();
    private int _nextAspectRatioOverrideId;
    private UIElement? _backgroundDragRoot;
    private PointerEventHandler? _backgroundDragHandler;
    private FrameworkElement? _sizeToContentRoot;
    private SizeChangedEventHandler? _sizeToContentSizeChangedHandler;
    private EventHandler<object>? _sizeToContentLayoutUpdatedHandler;
    private bool _sizeToContentApplying;
    // Edge-trigger for the "ignored while maximized" warning. ApplySizeToContent
    // runs off LayoutUpdated, i.e. every layout pass, so this tracks whether the
    // warning has already been emitted for the current maximized spell.
    private bool _sizeToContentMaximizedWarned;
    // Edge-trigger for WindowSpec's no-drag-affordance warning. Update validates
    // ahead of its own equality check, so without this an app holding that
    // configuration would emit one warning per spec change, indefinitely.
    private bool _warnedNoDragAffordance;
    internal int SizeToContentApplyCountForTests;

    /// <summary>Stable id, e.g. <c>"win-3"</c>. Allocated monotonically per process.</summary>
    public string Id => _id;

    /// <summary>Optional stable identity (from <see cref="WindowSpec.Key"/>).</summary>
    public WindowKey? Key => Volatile.Read(ref _spec).Key;

    /// <summary>The underlying WinUI <see cref="Microsoft.UI.Xaml.Window"/>.</summary>
    public Window NativeWindow => _window;

    /// <summary>The WinUI <see cref="AppWindow"/> for this window.</summary>
    public AppWindow AppWindow => _appWindow;

    internal WindowMessageMonitor MessageMonitor => _messageMonitor;

    internal nint Hwnd => _hwnd;

    /// <summary>
    /// When <c>true</c>, this window is an auxiliary surface (e.g. a docking
    /// tear-off floating window) that must never be elected the application's
    /// <see cref="ReactorApp.PrimaryWindow"/> and so never drives the
    /// <see cref="ShutdownPolicy.OnPrimaryWindowClosed"/> shutdown when it
    /// closes. Set once at open time. (issue #647)
    /// </summary>
    internal bool ExcludeFromShutdownPolicy { get; set; }

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

    /// <summary>Grouped taskbar features for this window. Lazily allocated.</summary>
    public TaskbarItem TaskbarItem
    {
        get
        {
            var existing = Volatile.Read(ref _taskbarItem);
            if (existing is not null) return existing;
            lock (_shellLock)
            {
                if (_taskbarItem is not null) return _taskbarItem;
                _taskbarItem = new TaskbarItem(this, _hwnd);
                return _taskbarItem;
            }
        }
    }

    /// <summary>
    /// Taskbar progress indicator for this window. Lazily allocated on first
    /// read; apps that never touch it pay no shell-COM init cost. Shortcut kept
    /// for compatibility; equivalent to <c>TaskbarItem.Progress</c>.
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
    /// Taskbar overlay icon ("badge"). Lazily allocated. Shortcut kept for
    /// compatibility; equivalent to <c>TaskbarItem.Overlay</c>. (spec 036 §11.2)
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

    /// <summary>DIP top-left position of the window, rounded to the nearest DIP.</summary>
    public (double X, double Y) Position
    {
        get
        {
            var snapshot = Volatile.Read(ref _position);
            return (snapshot.X, snapshot.Y);
        }
    }

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

    private sealed record DipPositionSnapshot(double X, double Y);

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

    /// <summary>Fires on the UI thread when the window's DIP top-left position changes.</summary>
    public event EventHandler<WindowDipPositionChangedEventArgs>? PositionChanged;

    /// <summary>
    /// Fires on the UI thread when Win32 reports a z-order transition. <see cref="WindowZOrderChangedEventArgs.IsCovered"/>
    /// is a covered hint based on HWND insertion order, not a pixel-accurate occlusion guarantee.
    /// </summary>
    public event EventHandler<WindowZOrderChangedEventArgs>? ZOrderChanged;

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
        // Validate() warned if the spec needs it; record that so Update doesn't
        // repeat it for the same unchanged condition.
        _warnedNoDragAffordance = spec.HasNoDragAffordance;

        _id = $"win-{Interlocked.Increment(ref s_nextId)}";
        _spec = spec;

        _window = new Window { Title = spec.Title };
        _appWindow = _window.AppWindow;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);

        if (spec.Embed is { } embed)
        {
            VerifyEmbedDpiAwareness(embed.Style);
            ApplyEmbedInitialStyles(embed.Style);
            _embedWatchdog = new EmbedHostWatchdog();
            _embedWatchdog.Start(embed.HostPid, () =>
            {
                try { NativeShell.SetParent(_hwnd, 0); } catch { }
                Environment.Exit(0);
            });
        }

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

        // Snapshot initial state and position from the realized presenter.
        _stateValue = (int)ResolveCurrentState();
        TryUpdatePositionCache(raiseEvent: false);
        _lastSizingRect = GetCurrentWindowRect();
    }

    private WindowState ResolveCurrentState()
    {
        if (NativeShell.IsIconic(_hwnd)) return Microsoft.UI.Reactor.WindowState.Minimized;
        if (NativeShell.IsZoomed(_hwnd)) return Microsoft.UI.Reactor.WindowState.Maximized;

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

    private bool TryUpdatePositionCache(bool raiseEvent)
    {
        try
        {
            var pos = _appWindow.Position;
            uint dpi = QueryDpiForWindow(_hwnd);
            double scale = dpi / 96.0;
            var next = new DipPositionSnapshot(
                RoundDip(pos.X / scale),
                RoundDip(pos.Y / scale));
            var prev = Volatile.Read(ref _position);
            // RoundDip produces integer-valued doubles, so == would be safe in
            // practice — but use an epsilon comparison for defense-in-depth
            // (and to satisfy CodeQL's floating-point-equality rule).
            const double positionEpsilonDip = 1e-6;
            if (Math.Abs(prev.X - next.X) < positionEpsilonDip
                && Math.Abs(prev.Y - next.Y) < positionEpsilonDip) return false;

            Volatile.Write(ref _position, next);
            if (raiseEvent)
                PositionChanged?.Invoke(this, new WindowDipPositionChangedEventArgs((next.X, next.Y)));
            return true;
        }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        {
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.Position.snapshot", ex);
            return false;
        }
    }

    private static double RoundDip(double value)
        => Math.Round(value, 0, MidpointRounding.AwayFromZero);

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

        if (_spec.ActivateOnOpen && !_disposed && (_spec.Embed is null || _spec.Embed.InitialVisibility))
            _window.Activate();
    }

    private void ApplyChrome(WindowSpec spec, bool isInitial)
    {
        try { _window.Title = spec.Title; }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        {
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.Title.set", ex);
        }

        bool topLevelChromeAllowed = IsTopLevelChromeAllowed(spec);

        // Presenter: full-screen / compact-overlay flip via AppWindow.SetPresenter.
        // Default Overlapped chrome modulators (resizable, minimizable, maximizable,
        // alwaysOnTop) only apply to OverlappedPresenter.
        if (topLevelChromeAllowed)
        {
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
                        ApplyWindowStyle(spec, _appWindow.Presenter as OverlappedPresenter);
                        break;
                }
            }
            catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
            {
                DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.Presenter.apply", ex);
            }

            if (spec.Embed is null)
            {
                try
                {
                    // AppWindow.IsShownInSwitchers is the WinUI Alt-Tab / shell-switcher flag.
                    // Owned windows hide by default — conventional shell behavior for owned top-level surfaces.
                    _appWindow.IsShownInSwitchers = spec.Owner is null && spec.ShowInSwitcher;
                }
                catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
                {
                    DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.ShowInSwitcher.set", ex);
                }
            }
        }

        if (spec.Embed is null)
            ApplyTaskbarVisibility(spec, isInitial);
        if (topLevelChromeAllowed)
        {
            ApplyWindowLevel(spec.Level);
            ApplyCornerStyle(spec.CornerStyle);
        }

        if (topLevelChromeAllowed)
        {
            // A null spec value means "infer": a mounted TitleBar(...) element
            // sets content-extension at mount. Re-applying chrome on Update must
            // preserve that inference — writing the raw default here would
            // silently drop the window out of content-extended mode behind a
            // still-mounted TitleBar control. Keyed on the *current* mount state,
            // so the inference is withdrawn once the element goes away rather
            // than latching the window into content-extended mode for its
            // lifetime. (issue #917; inference contract documented on
            // WindowSpec.ExtendsContentIntoTitleBar)
            try { _window.ExtendsContentIntoTitleBar = spec.ExtendsContentIntoTitleBar ?? _titleBarControlMounted; }
            catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
            {
                DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.ExtendsContentIntoTitleBar.set", ex);
            }

            // Issue #917 — the caption height option must be written AFTER the
            // content-extension flip: the native setter throws
            // ERROR_INVALID_STATE on a non-extended window.
            _specTitleBarHeight = spec.TitleBarHeight;
            ApplyTitleBarHeight(warnWhenNotExtended: spec.TitleBarHeight is not null);
        }

        // Sizing — DIP -> physical at the current per-window DPI. (spec 036 §5.1)
        // Reactor only overrides the axes the spec declares: with both null it
        // makes no Resize call at all, and with one set it resizes once, passing
        // the current (OS-chosen) extent through on the other axis.
        if (spec.Embed is null && isInitial && spec.Presenter == PresenterKind.Overlapped
            && TryBuildSpecSize(spec, out var initialSize))
        {
            try
            {
                _appWindow.Resize(initialSize);
            }
            catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
            {
                DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.InitialResize", ex);
            }
        }

        if (spec.Embed is null)
        {
            // A declared icon that cannot be resolved (wrong format, missing file, or a
            // resource URI with no matching asset) would otherwise leave the window with
            // no icon at all, because a non-null spec.Icon used to suppress the fallback.
            bool applied;
            if (spec.Icon is { } icon && icon.Apply(_appWindow))
            {
                // The declared icon is what the window shows now, so a cached fallback
                // handle no longer describes it. Drop it: otherwise removing the
                // declaration later would hit the "already applied" early-return in
                // TryApplyExeIconFallback and strand the declared icon on the window
                // instead of reverting to the fallback.
                ReleaseExeIconFallback();
                applied = true;
            }
            else
            {
                applied = TryApplyExeIconFallback();
            }

            if (applied)
            {
                _reactorAppliedIcon = true;
            }
            else if (!isInitial && _reactorAppliedIcon)
            {
                // Reactor put this window's icon there and the declaration that
                // justified it is now gone, so take it back down. Gated on having
                // applied one ourselves: an app that set its own icon imperatively
                // (a `configure:` callback calling AppWindow.SetIcon, say) must not
                // have it wiped by an unrelated chrome update, and a window that
                // never had an icon keeps whatever default the platform gave it.
                ClearWindowIcon();
                _reactorAppliedIcon = false;
            }
        }

        // Spec 045 §2.6 tear-off — window-wide alpha via WS_EX_LAYERED +
        // SetLayeredWindowAttributes. Skipped when Opacity==1.0 so opaque
        // windows pay zero layering overhead (Windows compositor fast-path).
        ApplyOpacity(spec.Opacity);

        // Spec 045 §2.6 tear-off — NoActivate must be applied before
        // Activate fires (in MountAndActivate) so the window's first show
        // observes the flag. Re-applied on Update so flips stick.
        SetNoActivate(spec.NoActivate);
        SetIgnorePointerInput(spec.IgnorePointerInput);
        if (!isInitial)
            OnHostContentRendered(_host.CurrentControl);

        // Owner relationship — only meaningful at initial apply time.
        // Subsequent Update calls do not re-parent (changing ownership of a
        // realized window has no AppWindow API and is rarely the right thing
        // for an app to do). (spec 036 §9)
        if (isInitial && spec.Embed is null && spec.Owner is { } owner && !owner._disposed)
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

        // The window's icon is ambient state for a mounted TitleBar(...) element: it is
        // not part of the element, so no element diff can observe it changing. Push it,
        // the same way the caption height is pushed via SyncTitleBarControlHeight.
        SyncTitleBarIcon(spec);
    }

    /// <summary>
    /// Re-resolves the inherited icon on the mounted <c>TitleBar(...)</c> control after
    /// the window's own icon may have changed. No-op when no title bar is mounted, or
    /// when the element declares its own icon (or opted out with <c>.NoIcon()</c>) —
    /// those own the slot.
    /// </summary>
    /// <param name="spec">
    /// This window's spec. Passed down rather than resolved from the ambient active
    /// host: <c>ApplyChrome</c> is reachable from <c>Update</c> at any time, so with two
    /// windows open the ambient host is routinely some *other* window.
    /// </param>
    private void SyncTitleBarIcon(WindowSpec spec)
    {
        if (_titleBarControl is null || !_titleBarControl.TryGetTarget(out var control)) return;
        if (control is not Microsoft.UI.Xaml.Controls.TitleBar bar) return;

        try
        {
            Core.V1Protocol.TitleBarIconDefault.ResyncInheritedIcon(bar, spec);
        }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        {
            // The WinUI TitleBar control throws teardown-reentry COMExceptions while the
            // window is closing (issue #537). A cosmetic icon refresh must never take the
            // window down.
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.SyncTitleBarIcon", ex);
        }
    }

    private static bool IsTopLevelChromeAllowed(WindowSpec spec)
        => spec.Embed?.Style != WindowEmbedStyle.Child;

    private void VerifyEmbedDpiAwareness(WindowEmbedStyle style)
    {
        bool perMonitorV2 = NativeShell.AreDpiAwarenessContextsEqual(
            NativeShell.GetProcessDpiAwarenessContext(),
            NativeShell.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        if (perMonitorV2) return;

        if (style == WindowEmbedStyle.Child)
        {
            throw new InvalidOperationException(
                "--embed requires PerMonitorV2 DPI awareness. Add <ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode> to your csproj's <PropertyGroup>.");
        }

        Console.Error.WriteLine("[reactor] --embed owner mode is running without PerMonitorV2 DPI awareness; DPI fallback may be less precise.");
    }

    /// <summary>
    /// Test hook so unit tests can verify the underlying P/Invoke resolves
    /// (Win32 entry point ``GetThreadDpiAwarenessContext`` — the historical
    /// typo ``GetProcessDpiAwarenessContext`` does not exist in user32.dll and
    /// threw at first call from the embed-Child code path, regressed silently
    /// because no test exercised this entry point).
    /// </summary>
    internal static nint GetCurrentDpiAwarenessContextForTests() => NativeShell.GetProcessDpiAwarenessContext();

    private void ApplyEmbedInitialStyles(WindowEmbedStyle style)
    {
        if (style == WindowEmbedStyle.Child)
        {
            // Keep the window top-level until the devtools embed ack. WinUI initializes
            // its DesktopChildSiteBridge during Window.Activate(); converting to WS_CHILD
            // before activation can leave the content site at 0x0 even though SetParent
            // later succeeds, producing a blank embedded preview.
            ApplyExtendedStyleBits(remove: NativeShell.WS_EX_APPWINDOW, add: 0);
            return;
        }

        ApplyExtendedStyleBits(remove: NativeShell.WS_EX_APPWINDOW, add: 0);
    }

    private static (bool Resizable, bool Minimizable, bool Maximizable) ResolveResizeMode(WindowSpec spec)
    {
        var (resizable, minimizable, maximizable) = spec.ResizeMode switch
        {
            WindowResizeMode.NoResize => (false, false, false),
            WindowResizeMode.CanMinimize => (false, true, false),
            _ => (true, true, true),
        };
        return (resizable, minimizable && spec.IsMinimizable, maximizable && spec.IsMaximizable);
    }

    private static bool EffectiveShowInTaskbar(WindowSpec spec)
        => spec.ShowInTaskbarExplicit ? spec.ShowInTaskbar : spec.Style != WindowStyle.ToolWindow;

    private void ApplyWindowStyle(WindowSpec spec, OverlappedPresenter? op)
    {
        switch (spec.Style)
        {
            case WindowStyle.None:
                op?.SetBorderAndTitleBar(false, false);
                ApplyWindowStyleBits(remove: NativeShell.WS_OVERLAPPEDWINDOW, add: NativeShell.WS_POPUP);
                // Clear WS_EX_TOOLWINDOW left over from a prior ToolWindow state
                // so the borderless window doesn't inherit tool-window framing.
                ApplyExtendedStyleBits(remove: NativeShell.WS_EX_TOOLWINDOW, add: 0);
                break;
            case WindowStyle.ToolWindow:
                op?.SetBorderAndTitleBar(true, true);
                ApplyWindowStyleBits(remove: NativeShell.WS_POPUP, add: NativeShell.WS_OVERLAPPEDWINDOW);
                ApplyExtendedStyleBits(remove: 0, add: NativeShell.WS_EX_TOOLWINDOW);
                break;
            default:
                op?.SetBorderAndTitleBar(true, true);
                ApplyWindowStyleBits(remove: NativeShell.WS_POPUP, add: NativeShell.WS_OVERLAPPEDWINDOW);
                // Strip WS_EX_TOOLWINDOW when returning to Default — otherwise
                // Default→ToolWindow→Default leaves the smaller tool-window
                // caption set on the window.
                ApplyExtendedStyleBits(remove: NativeShell.WS_EX_TOOLWINDOW, add: 0);
                break;
        }

        var (resizable, minimizable, maximizable) = ResolveResizeMode(spec);
        if (op is not null)
        {
            op.IsResizable = resizable;
            op.IsMinimizable = minimizable;
            op.IsMaximizable = maximizable;
        }
        ApplyResizeModeStyleBits(spec, resizable, minimizable, maximizable);
    }

    private void ApplyResizeModeStyleBits(WindowSpec spec, bool resizable, bool minimizable, bool maximizable)
    {
        if (spec.Style == WindowStyle.None) return;

        long add = 0;
        long remove = 0;
        if (resizable) add |= NativeShell.WS_THICKFRAME;
        else remove |= NativeShell.WS_THICKFRAME;
        if (minimizable) add |= NativeShell.WS_MINIMIZEBOX;
        else remove |= NativeShell.WS_MINIMIZEBOX;
        if (maximizable) add |= NativeShell.WS_MAXIMIZEBOX;
        else remove |= NativeShell.WS_MAXIMIZEBOX;

        ApplyWindowStyleBits(remove, add);
    }

    private void ApplyWindowStyleBits(long remove, long add)
    {
        long bits = (long)NativeShell.GetWindowLongPtr(_hwnd, NativeShell.GWL_STYLE);
        long updated = (bits & ~remove) | add;
        if (updated == bits) return;
        _ = NativeShell.SetWindowLongPtr(_hwnd, NativeShell.GWL_STYLE, (nint)updated);
        _ = NativeShell.SetWindowPos(_hwnd, 0, 0, 0, 0, 0,
            NativeShell.SWP_NOMOVE | NativeShell.SWP_NOSIZE | NativeShell.SWP_NOZORDER
            | NativeShell.SWP_NOACTIVATE | NativeShell.SWP_FRAMECHANGED);
    }

    private void ApplyExtendedStyleBits(long remove, long add)
    {
        long bits = (long)NativeShell.GetWindowLongPtr(_hwnd, NativeShell.GWL_EXSTYLE);
        long updated = (bits & ~remove) | add;
        if (updated == bits) return;
        _ = NativeShell.SetWindowLongPtr(_hwnd, NativeShell.GWL_EXSTYLE, (nint)updated);
        // Ex-style changes (e.g. WS_EX_TOOLWINDOW) don't repaint the
        // non-client area until SetWindowPos is called with SWP_FRAMECHANGED.
        // Without this, toggling Style between Default and ToolWindow shows
        // no visible chrome change until the next user resize / focus.
        _ = NativeShell.SetWindowPos(_hwnd, 0, 0, 0, 0, 0,
            NativeShell.SWP_NOMOVE | NativeShell.SWP_NOSIZE | NativeShell.SWP_NOZORDER
            | NativeShell.SWP_NOACTIVATE | NativeShell.SWP_FRAMECHANGED);
    }

    private void ApplyCornerStyle(WindowCornerStyle style)
    {
        int preference = style switch
        {
            WindowCornerStyle.Square => Hosting.DwmInterop.DWMWCP_DONOTROUND,
            WindowCornerStyle.Rounded => Hosting.DwmInterop.DWMWCP_ROUND,
            WindowCornerStyle.RoundedSmall => Hosting.DwmInterop.DWMWCP_ROUNDSMALL,
            _ => Hosting.DwmInterop.DWMWCP_DEFAULT,
        };
        _ = Hosting.DwmInterop.DwmSetWindowAttribute(
            _hwnd,
            Hosting.DwmInterop.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref preference,
            sizeof(int));
    }

    private void ApplyWindowLevel(WindowLevel level)
    {
        nint insertAfter = level switch
        {
            WindowLevel.AlwaysOnTop => HWND_TOPMOST,
            WindowLevel.Floating => HWND_TOP,
            _ => HWND_NOTOPMOST,
        };
        _ = NativeShell.SetWindowPos(_hwnd, insertAfter, 0, 0, 0, 0,
            NativeShell.SWP_NOMOVE | NativeShell.SWP_NOSIZE | NativeShell.SWP_NOACTIVATE);
    }

    internal static int TaskbarVisibilityCycleCountForTests;

    private void ApplyTaskbarVisibility(WindowSpec spec, bool isInitial)
    {
        bool showInTaskbar = spec.Owner is null && EffectiveShowInTaskbar(spec);
        long bits = (long)NativeShell.GetWindowLongPtr(_hwnd, NativeShell.GWL_EXSTYLE);
        long updated = showInTaskbar
            ? (bits | NativeShell.WS_EX_APPWINDOW) & ~NativeShell.WS_EX_TOOLWINDOW
            : (bits | NativeShell.WS_EX_TOOLWINDOW) & ~NativeShell.WS_EX_APPWINDOW;
        if (updated == bits) return;

        _ = NativeShell.SetWindowLongPtr(_hwnd, NativeShell.GWL_EXSTYLE, (nint)updated);
        if (!isInitial && IsVisible)
        {
            bool wasActive = IsActive;
            Interlocked.Increment(ref TaskbarVisibilityCycleCountForTests);
            _ = NativeShell.ShowWindow(_hwnd, NativeShell.SW_HIDE);
            _ = NativeShell.ShowWindow(_hwnd, NativeShell.SW_SHOW);
            if (wasActive)
            {
                try { _window.Activate(); }
                catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
                { DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.TaskbarVisibility.Activate", ex); }
            }
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
    /// Best-effort: when no explicit <see cref="WindowSpec.Icon"/> was supplied (or the one
    /// supplied could not be resolved), find an icon for the window so the caption /
    /// Alt-Tab / Win11 thumbnail show the developer's icon instead of the WinUI default.
    /// Unpackaged, this also reaches the taskbar button and Task Manager; packaged, those
    /// two come from package identity and the manifest logo instead (see
    /// <see cref="WindowSpec.Icon"/>).
    /// </summary>
    /// <remarks>
    /// <para>Two sources are tried, in order:</para>
    /// <list type="number">
    /// <item><description><c>Assets\AppIcon.ico</c> beside the app — the asset name the
    /// official WinUI 3 packaged template ships. Tried <b>first</b> because it is
    /// unambiguously author-supplied.</description></item>
    /// <item><description>The first icon embedded in the running executable's PE resources
    /// (what <c>&lt;ApplicationIcon&gt;</c> wires in). Second, so an explicit asset always
    /// wins over whatever the build happened to embed.</description></item>
    /// </list>
    /// <para>This runs for packaged apps too. A packaged app does <b>not</b> get its window
    /// <c>HICON</c> from <c>Package.appxmanifest</c>: the manifest's <c>VisualElements</c>
    /// tiles drive the shell's taskbar button through package identity, which bypasses the
    /// HWND entirely, so <c>WM_GETICON</c> stays 0 and Alt-Tab shows a generic icon. See
    /// microsoft-ui-xaml#6104 — which is why the official packaged template calls
    /// <c>AppWindow.SetIcon</c> explicitly. The manifest assets are not usable here either:
    /// they are named by MRT resource identifier rather than filename, so the literal
    /// manifest string does not exist on disk.</para>
    /// <para>Failures are silent — if neither source yields an icon, the AppWindow keeps
    /// its default. A .NET apphost built without <c>&lt;ApplicationIcon&gt;</c> carries no
    /// PE icon at all, so that is the ordinary outcome for an app that ships none.
    /// (spec 036 §4.1 — implementation-time addition)</para>
    /// </remarks>
    private bool TryApplyExeIconFallback()
    {
        if (_exeFallbackHIcon != 0)
        {
            // Already applied to this window and SetIcon took its own copy, so the icon
            // is still on the HWND — re-writing it would be redundant, and would also
            // clobber an icon the app set imperatively after this fallback first ran.
            return true;
        }

        // This runs on every chrome application, not just the first, so an app that
        // ships no icon at all would otherwise re-probe the filesystem and both
        // loaders on every Update. One attempt per window is enough: neither source
        // can appear while the window is alive.
        if (_exeFallbackAttempted) return false;
        _exeFallbackAttempted = true;

        // Load first, outside the try: both loaders are non-throwing (the generated
        // interop stubs return 0 on failure and LoadConventionAssetIcon guards its
        // own file probe).
        var hIcon = LoadConventionAssetIcon();
        if (hIcon == 0) hIcon = LoadExecutablePeIcon();
        if (hIcon == 0) return false;

        // This method owns hIcon until SetIcon has taken it. Tracking that explicitly
        // rather than relying on the catch means every exit — success, a swallowed
        // failure, or a throw from the loaders' marshalling — frees the handle exactly
        // once instead of leaking it for the process lifetime.
        var owned = true;
        try
        {
            var iconId = Microsoft.UI.Win32Interop.GetIconIdFromIcon(hIcon);
            _appWindow.SetIcon(iconId);
            // Ownership transfers to the window here — Dispose frees it. Assigned only
            // after SetIcon succeeds, so a throw above still leaves the handle to the
            // finally below.
            _exeFallbackHIcon = hIcon;
            owned = false;
            return true;
        }
        catch (Exception ex) when (IsIconApplyFailure(ex))
        {
            // Best-effort by contract: this is a cosmetic fallback for an app that
            // shipped no icon, so no failure of it may reach the caller — ApplyChrome
            // runs during render, and a propagating COMException would take the window
            // down over a missing icon. Mirrors WindowIcon.Apply, which swallows the
            // same AppWindow.SetIcon boundary.
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.TryApplyExeIconFallback", ex);
            return false;
        }
        finally
        {
            // DestroyIcon is a [LibraryImport] bool — it cannot throw at the marshal
            // boundary, so this is safe to run while an exception is in flight.
            if (owned) NativeIcon.DestroyIcon(hIcon);
        }
    }

    /// <summary>
    /// The failures the icon boundary can raise: a COM/WinRT fault out of
    /// <c>SetIcon</c> or the <c>IconId</c> factory, a handle the platform rejects, or a
    /// window that has already gone away (<see cref="ObjectDisposedException"/> derives
    /// from <see cref="InvalidOperationException"/>). Anything else is a genuine bug and
    /// propagates — the <c>finally</c> above still frees the handle either way.
    /// </summary>
    private static bool IsIconApplyFailure(Exception ex)
        => ex is COMException
              or ArgumentException
              or InvalidOperationException;

    /// <summary>
    /// Drops the cached fallback handle once a declared icon has replaced it on the
    /// window, so removing that declaration later re-runs the fallback instead of
    /// leaving the declared icon in place.
    /// </summary>
    private void ReleaseExeIconFallback()
    {
        if (_exeFallbackHIcon != 0)
        {
            // Safe for the same reason as in Dispose: the AppWindow holds its own
            // reference, and this handle is no longer the one the window displays.
            NativeIcon.DestroyIcon(_exeFallbackHIcon);
            _exeFallbackHIcon = 0;
        }

        _exeFallbackAttempted = false;
    }

    /// <summary>
    /// Sends <c>WM_SETICON</c> with a null handle to drop the window's small and big
    /// icons. Only called for an icon Reactor itself applied — see the call site.
    /// </summary>
    private void ClearWindowIcon()
    {
        _ = NativeIcon.SendMessageW(_hwnd, NativeIcon.WM_SETICON, NativeIcon.ICON_SMALL, 0);
        _ = NativeIcon.SendMessageW(_hwnd, NativeIcon.WM_SETICON, NativeIcon.ICON_BIG, 0);
    }

    /// <summary>
    /// Loads <c>Assets\AppIcon.ico</c> from the app's base directory, which resolves to the
    /// package install root for a packaged app. Returns 0 when absent.
    /// </summary>
    /// <remarks>
    /// The path half lives in <see cref="Hosting.AppIconConvention"/> because the
    /// <c>TitleBar</c> icon default needs the same file as a XAML <c>IconSource</c>
    /// rather than as an <c>HICON</c>, and the two must not disagree about which file
    /// the convention names.
    /// </remarks>
    private static nint LoadConventionAssetIcon()
    {
        if (!Hosting.AppIconConvention.TryGetAssetPath(AppContext.BaseDirectory, out var path))
            return 0;

        return NativeIcon.LoadImageW(0, path, NativeIcon.IMAGE_ICON,
            0, 0, NativeIcon.LR_LOADFROMFILE | NativeIcon.LR_DEFAULTSIZE);
    }

    /// <summary>
    /// Loads the icon embedded in the running executable's PE resources (what
    /// <c>&lt;ApplicationIcon&gt;</c> wires in). Returns 0 when the image carries none.
    /// </summary>
    /// <remarks>
    /// Uses <c>ExtractIconExW</c>, not <c>LoadImageW</c> with <c>LR_LOADFROMFILE</c>:
    /// that flag loads a standalone <i>image file</i> (<c>.ico</c>/<c>.bmp</c>/<c>.cur</c>)
    /// and returns 0 for every <c>.exe</c>, PE icon resources included — measured against
    /// <c>explorer.exe</c>, which has an icon and still yields 0 that way.
    /// </remarks>
    private static nint LoadExecutablePeIcon()
    {
        var exePath = global::System.Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return 0;

        // nIcons=1 extracts the first icon group only; nint.Zero for the small-icon
        // slot means there is no second handle to own and destroy.
        _ = NativeIcon.ExtractIconExW(exePath, 0, out var large, 0, 1);
        return large;
    }

    private static partial class NativeIcon
    {
        public const uint WM_SETICON = 0x0080;
        public const nint ICON_SMALL = 0;
        public const nint ICON_BIG = 1;
        public const uint IMAGE_ICON = 1;
        public const uint LR_LOADFROMFILE = 0x00000010;
        public const uint LR_DEFAULTSIZE = 0x00000040;

        // Correct for standalone .ico files. Does NOT read PE resources — see
        // LoadExecutablePeIcon.
        [global::System.Runtime.InteropServices.LibraryImport("user32.dll",
            EntryPoint = "LoadImageW",
            StringMarshalling = global::System.Runtime.InteropServices.StringMarshalling.Utf16,
            SetLastError = true)]
        public static partial nint LoadImageW(nint hInst, string lpszName,
            uint uType, int cxDesired, int cyDesired, uint fuLoad);

        // Reads icon groups out of a PE image. Both phicon parameters are
        // [out, optional] arrays; with nIcons = 1 a single `out nint` is the
        // equivalent blittable shape, and `nint.Zero` asks for large icons only.
        [global::System.Runtime.InteropServices.LibraryImport("shell32.dll",
            EntryPoint = "ExtractIconExW",
            StringMarshalling = global::System.Runtime.InteropServices.StringMarshalling.Utf16)]
        public static partial uint ExtractIconExW(string lpszFile, int nIconIndex,
            out nint phiconLarge, nint phiconSmall, uint nIcons);

        [global::System.Runtime.InteropServices.LibraryImport("user32.dll", SetLastError = true)]
        [return: global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static partial bool DestroyIcon(nint hIcon);

        [global::System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
        public static partial nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);
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

    private int DipToPhysicalScalar(double dip)
    {
        var dpi = Dpi == 0 ? 96 : Dpi;
        return (int)Math.Round(dip * dpi / 96.0);
    }

    private global::Windows.Graphics.SizeInt32 DipToPhysicalSize(double widthDip, double heightDip)
    {
        return new global::Windows.Graphics.SizeInt32(
            DipToPhysicalScalar(widthDip),
            DipToPhysicalScalar(heightDip));
    }

    /// <summary>
    /// Builds the physical-pixel size a spec asks for, or returns false when the
    /// spec declares neither axis — the "let the OS size the window" default.
    /// A half-specified spec keeps the current (OS-chosen) extent on the other
    /// axis, and is also skipped when that extent is not yet a real positive
    /// value. (spec 036 §4.1 / §5.1)
    /// </summary>
    private bool TryBuildSpecSize(WindowSpec spec, out global::Windows.Graphics.SizeInt32 size)
    {
        size = default;
        if (spec.Width is null && spec.Height is null) return false;

        int currentWidth = 0, currentHeight = 0;
        if (spec.Width is null || spec.Height is null)
        {
            try
            {
                var current = _appWindow.Size;
                currentWidth = current.Width;
                currentHeight = current.Height;
            }
            catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
            {
                DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.TryBuildSpecSize", ex);
                return false;
            }

            // A non-positive current extent means there is no real OS extent to
            // preserve yet. Resizing would drive that axis to 0, which is worse
            // than leaving the window at whatever the OS gave it — skip, and let
            // the first-DPI re-apply try again once the extent is real.
            if ((spec.Width is null && currentWidth <= 0) || (spec.Height is null && currentHeight <= 0))
                return false;
        }

        size = new global::Windows.Graphics.SizeInt32(
            spec.Width is { } w ? DipToPhysicalScalar(w) : currentWidth,
            spec.Height is { } h ? DipToPhysicalScalar(h) : currentHeight);
        return true;
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
                    // the user hasn't already resized the window manually and
                    // the spec actually declares a size (a null Width/Height
                    // leaves that axis to the OS).
                    if (_spec.Embed is null && !_userResized && !_firstDpiApplied)
                    {
                        try
                        {
                            if (TryBuildSpecSize(_spec, out var dpiSize))
                            {
                                // Latch only once the size really lands. If
                                // TryBuildSpecSize declined because the extent to
                                // preserve wasn't real yet, staying unlatched lets
                                // a later DPI report apply the declared axis.
                                _firstDpiApplied = true;
                                _appWindow.Resize(dpiSize);
                            }
                            else if (_spec.Width is null && _spec.Height is null)
                            {
                                // Nothing to apply, ever — stop re-checking.
                                _firstDpiApplied = true;
                            }
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
                _userResized = true;
                // WM_SIZING precedes WM_GETMINMAXINFO / OS track-size clamping.
                // Modify the mutable RECT in place; the OS reads it after the
                // subclass chain returns. DO NOT mark Handled — that skips
                // DefSubclassProc and bypasses any inner WinUI subclasses,
                // which then miss the size change and try to "correct" it on
                // a later message (the source of the AspectRatio flicker).
                // (spec 054 §5.2 / R1.)
                ApplyAspectRatioSizing(args.WParam, args.LParam);
                break;
            case WindowMessageMonitor.WM_EXITSIZEMOVE:
                _userResized = true;
                _lastSizingRect = GetCurrentWindowRect();
                break;
            case WindowMessageMonitor.WM_WINDOWPOSCHANGED:
                DispatchZOrderChanged(args);
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
    internal struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public nint hwnd;
        public nint hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    private const uint SWP_NOZORDER = 0x0004;
    private static readonly nint HWND_TOP = 0;
    private static readonly nint HWND_TOPMOST = -1;
    private static readonly nint HWND_NOTOPMOST = -2;

    private unsafe void DispatchZOrderChanged(WindowMessageEventArgs args)
    {
        var pos = (WINDOWPOS*)args.LParam;
        if (pos == null) return;
        if ((pos->flags & SWP_NOZORDER) != 0) return;

        bool movedToTop = pos->hwndInsertAfter == HWND_TOP || pos->hwndInsertAfter == HWND_TOPMOST;
        ZOrderChanged?.Invoke(this, new WindowZOrderChangedEventArgs(isCovered: !movedToTop, movedToTop));
    }

    internal void RaiseZOrderChangedForTests(bool movedToTop, bool isCovered)
        => ZOrderChanged?.Invoke(this, new WindowZOrderChangedEventArgs(isCovered, movedToTop));

    private unsafe void ApplyAspectRatioSizing(nuint edge, nint rectPtr)
    {
        var rect = (RECT*)rectPtr;
        if (rect == null) return;
        // Snapshot the USER'S pre-adjusted drag rect — _lastSizingRect must
        // store this (not our adjusted output) so the next frame's
        // corner-drag master selection can compare against the user's
        // actual cursor movement direction. Storing the adjusted rect
        // creates an oscillation feedback loop where each frame's master
        // flips and the window's width snaps wildly between drag-width
        // and (drag-height × ratio). (spec 054 §5.2 / R1 follow-up.)
        var userRect = *rect;
        var adjusted = userRect;
        if (ApplyAspectRatioSizing((int)edge, ref adjusted))
        {
            *rect = adjusted;
            _lastSizingRect = userRect;
        }
    }

    internal bool ApplyAspectRatioSizingForTests(int edge, ref RECT rect)
    {
        var userRect = rect;
        var adjusted = userRect;
        if (!ApplyAspectRatioSizing(edge, ref adjusted)) return false;
        rect = adjusted;
        _lastSizingRect = userRect;
        return true;
    }

    internal RECT ClampSizingRectForTests(RECT rect)
    {
        var spec = _spec;
        int minWidth = DipToPhysicalScalar(spec.MinWidth ?? 0);
        int minHeight = DipToPhysicalScalar(spec.MinHeight ?? 0);
        int maxWidth = spec.MaxWidth is { } maxW ? DipToPhysicalScalar(maxW) : int.MaxValue;
        int maxHeight = spec.MaxHeight is { } maxH ? DipToPhysicalScalar(maxH) : int.MaxValue;
        int width = Math.Clamp(rect.Right - rect.Left, minWidth, maxWidth);
        int height = Math.Clamp(rect.Bottom - rect.Top, minHeight, maxHeight);
        rect.Right = rect.Left + width;
        rect.Bottom = rect.Top + height;
        return rect;
    }

    private bool ApplyAspectRatioSizing(int edge, ref RECT rect)
    {
        double? ratio = EffectiveAspectRatio;
        if (ratio is null) return false;
        if (!(ratio.Value > 0.0) || !double.IsFinite(ratio.Value)) return false;

        // When AspectRatioBasis is Client, subtract chrome so the ratio
        // constrains the content area, not the outer window rect.
        // chromeH/chromeV are 0 for the Window basis (the no-op fallback)
        // AND for ExtendsContentIntoTitleBar=true: once the app paints into
        // the title-bar area, the OS's notion of "client area" no longer
        // matches the developer's notion of "content area" (the custom
        // title bar lives inside the client area), so Client basis becomes
        // ambiguous. Fall back to Window basis until a future API lets the
        // app declare its own content-area rectangle.
        bool useClientBasis = _spec.AspectRatioBasis == AspectRatioBasis.Client
            && !_window.ExtendsContentIntoTitleBar;
        var (chromeH, chromeV) = useClientBasis ? ComputeChromeInset() : (0, 0);

        var previous = _lastSizingRect;
        if (previous.Right <= previous.Left || previous.Bottom <= previous.Top)
            previous = GetCurrentWindowRect();

        int width = Math.Max(1, rect.Right - rect.Left);
        int height = Math.Max(1, rect.Bottom - rect.Top);
        // Side-edge drags: the user is moving the edge perpendicular to the
        // axis they want to change. WMSZ_LEFT/RIGHT means they're moving
        // the WIDTH; keep their width and compute height (widthMaster=true).
        // WMSZ_TOP/BOTTOM means they're moving the HEIGHT; keep their
        // height and compute width (widthMaster=false). Corner drags pick
        // whichever axis moved further from the user's previous drag rect.
        bool widthMaster = edge switch
        {
            WMSZ_LEFT or WMSZ_RIGHT => true,
            WMSZ_TOP or WMSZ_BOTTOM => false,
            _ => Math.Abs(width - Math.Max(1, previous.Right - previous.Left))
                 >= Math.Abs(height - Math.Max(1, previous.Bottom - previous.Top)),
        };

        if (widthMaster)
        {
            // Solve for window height H such that (W - chromeH) / (H - chromeV) == ratio
            //   →   H = (W - chromeH) / ratio + chromeV.
            // Collapses to H = W / ratio when chromeH == chromeV == 0 (Window basis).
            int clientWidth = Math.Max(1, width - chromeH);
            int desiredHeight = Math.Max(1, (int)Math.Round(clientWidth / ratio.Value) + chromeV);
            switch (edge)
            {
                case WMSZ_TOP:
                case WMSZ_TOPLEFT:
                case WMSZ_TOPRIGHT:
                    rect.Top = rect.Bottom - desiredHeight;
                    break;
                default:
                    rect.Bottom = rect.Top + desiredHeight;
                    break;
            }
        }
        else
        {
            int clientHeight = Math.Max(1, height - chromeV);
            int desiredWidth = Math.Max(1, (int)Math.Round(clientHeight * ratio.Value) + chromeH);
            switch (edge)
            {
                case WMSZ_LEFT:
                case WMSZ_TOPLEFT:
                case WMSZ_BOTTOMLEFT:
                    rect.Left = rect.Right - desiredWidth;
                    break;
                default:
                    rect.Right = rect.Left + desiredWidth;
                    break;
            }
        }
        return true;
    }

    /// <summary>
    /// Returns the chrome inset (caption + borders, in physical px at the
    /// current DPI) for the current window's style and ex-style. The two
    /// values are total horizontal (left + right border) and total vertical
    /// (caption + bottom border) padding around the client area.
    /// Returns <c>(0, 0)</c> if the OS call fails.
    /// </summary>
    private (int Horizontal, int Vertical) ComputeChromeInset()
    {
        var rect = new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 };
        long style = (long)NativeShell.GetWindowLongPtr(_hwnd, NativeShell.GWL_STYLE);
        long exStyle = (long)NativeShell.GetWindowLongPtr(_hwnd, NativeShell.GWL_EXSTYLE);
        var dpi = Dpi == 0 ? 96 : Dpi;
        if (!NativeShell.AdjustWindowRectExForDpi(ref rect, unchecked((uint)style), false, unchecked((uint)exStyle), dpi))
            return (0, 0);
        int horizontal = (rect.Right - 100) + (-rect.Left);
        int vertical = (rect.Bottom - 100) + (-rect.Top);
        return (Math.Max(0, horizontal), Math.Max(0, vertical));
    }

    internal double? EffectiveAspectRatioForTests => EffectiveAspectRatio;

    internal long GetExtendedWindowStyleBitsForTests()
        => (long)NativeShell.GetWindowLongPtr(_hwnd, NativeShell.GWL_EXSTYLE);

    private double? EffectiveAspectRatio
    {
        get
        {
            var overrides = Volatile.Read(ref _aspectRatioOverrides);
            return overrides.Length > 0 ? overrides[^1].Ratio : Volatile.Read(ref _spec).AspectRatio;
        }
    }

    private RECT GetCurrentWindowRect()
    {
        if (NativeWindowing.GetWindowRect(_hwnd, out var rect)) return rect;
        var pos = _appWindow.Position;
        var size = _appWindow.Size;
        return new RECT { Left = pos.X, Top = pos.Y, Right = pos.X + size.Width, Bottom = pos.Y + size.Height };
    }

    private const int WMSZ_LEFT = 1;
    private const int WMSZ_RIGHT = 2;
    private const int WMSZ_TOP = 3;
    private const int WMSZ_TOPLEFT = 4;
    private const int WMSZ_TOPRIGHT = 5;
    private const int WMSZ_BOTTOM = 6;
    private const int WMSZ_BOTTOMLEFT = 7;
    private const int WMSZ_BOTTOMRIGHT = 8;

    internal unsafe void ApplyMinMaxInfoForTests(ref MINMAXINFO info)
    {
        fixed (MINMAXINFO* infoPtr = &info)
        {
            var args = new WindowMessageEventArgs(_hwnd, WindowMessageMonitor.WM_GETMINMAXINFO, 0, (nint)infoPtr);
            ApplyMinMaxInfo(args);
        }
    }

    private unsafe void ApplyMinMaxInfo(WindowMessageEventArgs args)
    {
        var spec = _spec;
        var sizeRoot = _sizeToContentRoot;
        bool hasSizeToContent = spec.SizeToContent != WindowSizeToContent.Manual && sizeRoot is FrameworkElement;

        // Skip when nothing is constrained — let WinUI's default min/max stand.
        if (spec.MinWidth is null && spec.MinHeight is null
            && spec.MaxWidth is null && spec.MaxHeight is null
            && !hasSizeToContent)
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

        // SizeToContent: clamp min=max to content size for the constrained
        // axis (or both). Doing it here — at the OS's pre-drag WM_GETMINMAXINFO
        // gate — eliminates the post-resize flicker that happens when we
        // correct via AppWindow.Resize from a SizeChanged handler after the
        // OS has already painted the user's drag size. (spec 054 §6.3 R7.)
        if (hasSizeToContent)
        {
            // hasSizeToContent guarantees sizeRoot is non-null FrameworkElement.
            var fwSize = (FrameworkElement)sizeRoot!;
            var desired = ResolveSizeToContentDesiredDip(fwSize);
            if (desired.Width > 0 && desired.Height > 0)
            {
                var rect = new RECT { Left = 0, Top = 0, Right = DipToPxScalar(desired.Width), Bottom = DipToPxScalar(desired.Height) };
                long style = (long)NativeShell.GetWindowLongPtr(_hwnd, NativeShell.GWL_STYLE);
                long exStyle = (long)NativeShell.GetWindowLongPtr(_hwnd, NativeShell.GWL_EXSTYLE);
                _ = NativeShell.AdjustWindowRectExForDpi(ref rect, unchecked((uint)style), false, unchecked((uint)exStyle), dpi);
                int wPx = ClampPhysicalDimension(Math.Max(1, rect.Right - rect.Left), spec.MinWidth, spec.MaxWidth);
                int hPx = ClampPhysicalDimension(Math.Max(1, rect.Bottom - rect.Top), spec.MinHeight, spec.MaxHeight);
                if (spec.SizeToContent is WindowSizeToContent.Width or WindowSizeToContent.WidthAndHeight)
                {
                    info->ptMinTrackSize.X = wPx;
                    info->ptMaxTrackSize.X = wPx;
                }
                if (spec.SizeToContent is WindowSizeToContent.Height or WindowSizeToContent.WidthAndHeight)
                {
                    info->ptMinTrackSize.Y = hPx;
                    info->ptMaxTrackSize.Y = hPx;
                }
            }
        }

        args.Handled = true;
        args.Result = 0;
    }

    private void OnNativeActivated(object? sender, WindowActivatedEventArgs args)
    {
        bool isActive = args.WindowActivationState != WindowActivationState.Deactivated;
        bool wasActive = IsActive;
        IsActive = isActive;
        IsVisible = true;
        if (isActive)
            ReassertFloatingWindowsForActivation(this);
        if (isActive && !wasActive)
            Activated?.Invoke(this, EventArgs.Empty);
        else if (!isActive && wasActive)
            Deactivated?.Invoke(this, EventArgs.Empty);
    }

    private static void ReassertFloatingWindowsForActivation(ReactorWindow activated)
    {
        var windows = ReactorApp.Windows;
        for (int i = 0; i < windows.Count; i++)
        {
            var candidate = windows[i];
            if (ReferenceEquals(candidate, activated) || candidate._disposed) continue;
            if (candidate.Spec.Level != WindowLevel.Floating) continue;
            candidate.ApplyWindowLevel(WindowLevel.Floating);
        }
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
        if (args.DidPositionChange)
            TryUpdatePositionCache(raiseEvent: true);

        // The size-to-content maximized warning latches for one maximized spell.
        // This is the only root-independent observer of window state:
        // ApplySizeToContent's re-arm is driven by the root's SizeChanged /
        // LayoutUpdated handlers and returns at its own null-root guard, so with
        // no root attached (an Empty() tree) nothing on the content path can see
        // a restore and the next maximized spell would be silently suppressed.
        // Deliberately above the presenter/visibility filter below: a
        // ShowWindow-driven restore reports neither of those, only a size change.
        // The latch read is free and false in the common case, so the state
        // resolution is only paid while a spell is actually latched — this
        // handler also runs per move event. Uses the same predicate as the warning
        // itself, so the two can never disagree about what "maximized" means.
        if (_sizeToContentMaximizedWarned && ResolveCurrentState() != WindowState.Maximized)
            _sizeToContentMaximizedWarned = false;

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
                // Children close via the programmatic Window.Close() path,
                // which does not raise AppWindow.Closing — so make each child's
                // (and every nested owned descendant's) mounted WinUI TitleBar
                // safe to tear down here, before the native close, mirroring
                // ReactorWindow.Close(). Idempotent. (#537)
                child.PrepareTitleBarTreeForClose();
                try { child.CloseNativeWindowOnce(); }
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
        else
        {
            _closingReason = WindowCloseReason.UserClosed; // reset for the next attempt
            // The close (user chrome / Alt+F4) is now irrevocable. Make any
            // mounted WinUI TitleBar safe to tear down — while the AppWindow is
            // still alive — before the native window teardown runs. (issue #537)
            PrepareTitleBarForClose();
        }
    }

    /// <summary>
    /// Records that a WinUI <c>TitleBar</c> control is mounted in this window's
    /// content. Set from the TitleBar element's mount path (see
    /// <c>RegisterWindowTitleBar</c> in Element.cs) for every TitleBar mount —
    /// including the <c>ExtendsContentIntoTitleBar=false</c> case where Reactor
    /// deliberately skips <c>SetTitleBar</c>. Drives
    /// <see cref="PrepareTitleBarForClose"/>. (issue #537)
    /// </summary>
    internal void MarkTitleBarControlPresent()
    {
        _titleBarControlPresent = true;
        _titleBarControlMounted = true;
    }

    /// <summary>
    /// Records that the window's <c>TitleBar</c> control has been unmounted:
    /// withdraws the <c>ExtendsContentIntoTitleBar</c> inference and the
    /// element's caption-height contribution, so a window that merely used to
    /// host a TitleBar is not left content-extended and tall forever.
    /// (issue #917)
    /// <para>
    /// Deliberately does <b>not</b> clear <see cref="_titleBarControlPresent"/>:
    /// that latch drives close-time teardown safety (issue #537), where the
    /// conservative "a control was mounted at some point" answer is the correct
    /// one.
    /// </para>
    /// </summary>
    internal void ClearTitleBarControl()
    {
        _titleBarControlMounted = false;
        _titleBarControl = null;
        _titleBarControlExplicitHeight = false;
        _titleBarControlHeightOwned = false;
        _elementTitleBarHeight = null;
        ApplyTitleBarHeight(warnWhenNotExtended: false);
    }

    /// <summary>
    /// Records the caption height declared by the mounted <c>TitleBar(...)</c>
    /// element together with the control that declared it, and applies both
    /// halves. Called from the TitleBar element's mount/update path immediately
    /// after the <c>ExtendsContentIntoTitleBar</c> inference, which is the
    /// earliest point the native setter is legal. (issue #917)
    /// </summary>
    /// <param name="height">The element's <c>HeightOption</c>, or <c>null</c>.</param>
    /// <param name="control">The mounted WinUI <c>TitleBar</c> control.</param>
    /// <param name="controlHasExplicitHeight">
    /// <c>true</c> when the element carries an explicit <c>.Height(...)</c>
    /// modifier, which owns the control's height outright.
    /// </param>
    internal void SetElementTitleBarHeight(
        WindowTitleBarHeight? height,
        FrameworkElement? control,
        bool controlHasExplicitHeight)
    {
        // The element's Imperative entry runs on every render of the TitleBar,
        // but the native caption write is a COM call and the control write can
        // invalidate layout. Skip both when nothing that feeds them changed.
        // (issue #917 review)
        var sameControl = control is null
            ? _titleBarControl is null
            : _titleBarControl is not null
              && _titleBarControl.TryGetTarget(out var existing)
              && ReferenceEquals(existing, control);
        if (sameControl
            && _elementTitleBarHeight == height
            && _titleBarControlExplicitHeight == controlHasExplicitHeight)
            return;

        _elementTitleBarHeight = height;
        _titleBarControl = control is null ? null : new WeakReference<FrameworkElement>(control);
        _titleBarControlExplicitHeight = controlHasExplicitHeight;
        ApplyTitleBarHeight(warnWhenNotExtended: height is not null);
    }

    /// <summary>
    /// Writes the resolved caption height onto <c>AppWindow.TitleBar</c>. (issue #917)
    /// <para>
    /// Resolution mirrors <c>ExtendsContentIntoTitleBar</c>: an explicit
    /// <see cref="WindowSpec.TitleBarHeight"/> wins, otherwise the mounted
    /// <c>TitleBar(...)</c> element's declaration applies. When neither declares
    /// a value Reactor writes nothing — so an app that sets
    /// <c>PreferredHeightOption</c> imperatively keeps ownership — except to
    /// return a previously Reactor-applied value to
    /// <see cref="WindowTitleBarHeight.Standard"/>.
    /// </para>
    /// <para>
    /// The native setter throws <c>ERROR_INVALID_STATE</c> (0x8007139F) when the
    /// window is not content-extended, so the write is gated on
    /// <c>ExtendsContentIntoTitleBar</c>. A declared height on a window that
    /// never extends warns rather than throwing; it is re-applied automatically
    /// once a <c>TitleBar(...)</c> element flips the window into extended mode.
    /// </para>
    /// </summary>
    private void ApplyTitleBarHeight(bool warnWhenNotExtended)
    {
        var resolved = _specTitleBarHeight ?? _elementTitleBarHeight;
        if (resolved is null)
        {
            // No height is declared, so the invalid "height set on a
            // non-extended window" combination cannot hold — re-arm, or
            // re-declaring the height later would be a new invalid state that
            // never warned. Must happen before the early return below: an
            // invalid height is never applied, so _appliedTitleBarHeight stays
            // null and that return is exactly the path a removal takes.
            _warnedTitleBarHeightNotExtended = false;
            if (_appliedTitleBarHeight is null) return; // never declared — leave the app's value alone
            resolved = WindowTitleBarHeight.Standard;   // declaration removed — return to the default
        }

        bool extended;
        try { extended = _window.ExtendsContentIntoTitleBar; }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        {
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.TitleBarHeight.state", ex);
            return;
        }

        if (!extended)
        {
            // Warn on entering the invalid combination, not on every re-apply.
            // ApplyChrome runs for any unequal spec, so an app that leaves
            // TitleBarHeight set on a non-extended window would otherwise emit
            // one release-visible warning per unrelated field change. Re-armed
            // below once the window is content-extended again.
            if (warnWhenNotExtended && !_warnedTitleBarHeightNotExtended)
            {
                _warnedTitleBarHeightNotExtended = true;
                Interlocked.Increment(ref TitleBarHeightNotExtendedWarningCountForTests);
                DiagnosticLog.Warning(
                    LogCategory.Hosting,
                    "ReactorWindow.TitleBarHeight",
                    _titleBarControlMounted
                        // A TitleBar is already rendered, so "render one" is not the
                        // remedy — the spec is forcing the window out of the mode the
                        // native setter requires. (issue #917 review)
                        ? "TitleBarHeight requires a content-extended window, but WindowSpec.ExtendsContentIntoTitleBar "
                          + "is explicitly false. Remove it (a TitleBar(...) element infers true) or set it to true. "
                          + "The height option was not applied."
                        : "TitleBarHeight requires a content-extended window; set WindowSpec.ExtendsContentIntoTitleBar = true "
                          + "or render a TitleBar(...) element. The height option was not applied.");
            }
            // The caption stayed Standard, so the control must not go tall either.
            _effectiveTitleBarHeight = WindowTitleBarHeight.Standard;
            SyncTitleBarControlHeight();
            return;
        }

        // Content-extended: the invalid combination is gone, so a later relapse
        // is a new edge and warns again.
        _warnedTitleBarHeightNotExtended = false;

        try
        {
            _appWindow.TitleBar.PreferredHeightOption = resolved.Value switch
            {
                WindowTitleBarHeight.Tall => TitleBarHeightOption.Tall,
                WindowTitleBarHeight.Collapsed => TitleBarHeightOption.Collapsed,
                _ => TitleBarHeightOption.Standard,
            };
            _appliedTitleBarHeight = _specTitleBarHeight ?? _elementTitleBarHeight;
            _effectiveTitleBarHeight = resolved;
        }
        catch (COMException ex)
        {
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.TitleBarHeight.set", ex);
        }

        SyncTitleBarControlHeight();
    }

    /// <summary>
    /// Sizes the mounted WinUI <c>TitleBar</c> control to the caption height
    /// Reactor actually applied. (issue #917)
    /// <para>
    /// The control does <b>not</b> track <c>AppWindow.TitleBar.PreferredHeightOption</c>
    /// — a tall caption over a 32 DIP control leaves the two visibly disagreeing —
    /// so Reactor pairs the two. An explicit <c>.Height(...)</c> on the element
    /// owns the control outright and is never touched, and Reactor only clears a
    /// height it set itself.
    /// </para>
    /// <para>
    /// Also called by the reconciler <em>after</em> inline modifiers have run
    /// (see <c>TitleBarElement.SyncControlHeightAfterModifiers</c>), so removing
    /// an explicit <c>.Height(...)</c> from a still-tall TitleBar falls back to
    /// the implied tall height rather than to auto.
    /// </para>
    /// </summary>
    internal void SyncTitleBarControlHeight()
    {
        if (_titleBarControlExplicitHeight) return;
        if (_titleBarControl is null || !_titleBarControl.TryGetTarget(out var control)) return;

        if (_effectiveTitleBarHeight == WindowTitleBarHeight.Tall)
        {
            // Runs after every render via the post-modifier hook; skip the DP
            // write (and the layout invalidation it triggers) when the control
            // already carries the height Reactor owns. Compared with a
            // sub-pixel tolerance rather than == because Height is a double DP
            // (and is NaN while unset, which correctly falls through to the
            // write since every NaN comparison is false). (issue #917 review)
            if (_titleBarControlHeightOwned
                && Math.Abs(control.Height - TitleBarElement.TallTitleBarControlHeight) < HeightEpsilon)
                return;
            control.Height = TitleBarElement.TallTitleBarControlHeight;
            _titleBarControlHeightOwned = true;
        }
        else if (_titleBarControlHeightOwned)
        {
            control.ClearValue(FrameworkElement.HeightProperty);
            _titleBarControlHeightOwned = false;
        }
    }

    /// <summary>Sub-pixel tolerance for comparing DIP heights. (issue #917)</summary>
    private const double HeightEpsilon = 0.01;

    /// <summary>
    /// Make a mounted WinUI <c>TitleBar</c> control safe to tear down, before
    /// the native window close runs. (issue #537)
    /// <para>
    /// The WinUI <c>Microsoft.UI.Xaml.Controls.TitleBar</c> control corrupts the
    /// heap (<c>STATUS_HEAP_CORRUPTION</c>, 0xC0000374) during its teardown when
    /// the window's <c>ExtendsContentIntoTitleBar</c> is <c>false</c> — the
    /// control is designed for the content-extended (custom title bar) mode and
    /// its caption-button / AppWindow interop is only released cleanly in that
    /// mode. Reactor lets an app set <c>ExtendsContentIntoTitleBar=false</c>
    /// while still rendering a <c>TitleBar(...)</c> element (in which case
    /// Reactor skips <c>SetTitleBar</c>), so the control reaches the unsafe
    /// teardown on close.
    /// </para>
    /// <para>
    /// Flipping the window back into the content-extended mode here — while the
    /// HWND and AppWindow are still fully alive, before <c>Window.Close()</c>
    /// initiates the native destroy — routes the control through its safe
    /// teardown path. The window is closing, so the (otherwise user-visible)
    /// chrome change has no effect, and the public <c>ExtendsContentIntoTitleBar</c>
    /// value the app observes during the window's life is unchanged. When no
    /// TitleBar control was ever mounted, or the window already extends, the flip
    /// is a no-op.
    /// </para>
    /// <para>
    /// Idempotent (guarded by <see cref="_titleBarTeardownPrepared"/>) so the
    /// many teardown paths that call it — <see cref="Close"/>, the owner-close
    /// cascade, the <see cref="OnAppWindowClosing"/> chrome/Alt+F4 path,
    /// <see cref="Dispose()"/>, and the <c>ReactorApp</c> exit prep — are all
    /// safe and never double-flip. The guard is only latched <em>after</em> a
    /// successful flip (or one mooted by an in-flight native teardown); any
    /// other flip failure leaves the window un-prepared so a later
    /// close/exit/dispose path retries rather than reaching the unsafe native
    /// teardown believing the prep is already done.
    /// </para>
    /// </summary>
    private void PrepareTitleBarForClose()
    {
        if (!_titleBarControlPresent || _titleBarTeardownPrepared) return;

        try
        {
            _window.ExtendsContentIntoTitleBar = true;
        }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        {
            // The native teardown is already in flight: the TitleBar control is
            // no longer reachable and the flip is moot. Fall through and latch
            // the guard so overlapping paths don't retry into the same reentry.
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.TitleBarClosePrep", ex);
        }

        // Latch only after the flip actually succeeded (or was mooted by an
        // in-flight teardown above). A flip that throws any *other* exception
        // skips this line, leaving the window un-prepared so a later
        // close/exit/dispose path retries rather than reaching the unsafe native
        // teardown believing the prep is already done. (issue #537)
        _titleBarTeardownPrepared = true;
    }

    /// <summary>
    /// Make this window and every owned descendant's mounted WinUI
    /// <c>TitleBar</c> control safe to tear down, depth-first, before a native
    /// close. (issue #537)
    /// <para>
    /// The owner-close cascade and programmatic <see cref="Close"/> close owned
    /// children via the raw <c>Window.Close()</c> path, which does not raise
    /// <c>AppWindow.Closing</c> on the child — so the child's own cascade never
    /// runs and a grandchild's TitleBar would otherwise reach the unsafe
    /// teardown unprepared. Walking the owned tree here prepares every level.
    /// Idempotent per window (see <see cref="PrepareTitleBarForClose"/>), so
    /// overlapping close paths can't double-flip.
    /// </para>
    /// </summary>
    internal void PrepareTitleBarTreeForClose()
    {
        PrepareTitleBarForClose();

        var owned = OwnedWindows;
        for (int i = 0; i < owned.Count; i++)
        {
            var child = owned[i];
            if (child._disposed) continue;
            child.PrepareTitleBarTreeForClose();
        }
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

    internal void OnHostContentRendered(UIElement? root)
    {
        if (_disposed) return;
        var spec = Volatile.Read(ref _spec);
        if (spec.IsMovableByBackground)
            AttachBackgroundDragRoot(root);
        else
            DetachBackgroundDragRoot();
        AttachSizeToContentRoot(spec, root as FrameworkElement);
    }

    private void AttachBackgroundDragRoot(UIElement? root)
    {
        if (ReferenceEquals(_backgroundDragRoot, root)) return;
        DetachBackgroundDragRoot();
        if (root is null) return;
        _backgroundDragHandler = OnBackgroundPointerPressed;
        root.PointerPressed += _backgroundDragHandler;
        _backgroundDragRoot = root;
    }

    private void DetachBackgroundDragRoot()
    {
        if (_backgroundDragRoot is not null && _backgroundDragHandler is not null)
            _backgroundDragRoot.PointerPressed -= _backgroundDragHandler;
        _backgroundDragRoot = null;
        _backgroundDragHandler = null;
    }

    private void AttachSizeToContentRoot(WindowSpec spec, FrameworkElement? root)
    {
        if (spec.SizeToContent == WindowSizeToContent.Manual)
        {
            DetachSizeToContentRoot();
            // Size-to-content is off, which ends any ignored-while-maximized
            // spell — re-arm so enabling it again reports. Deliberately here and
            // not inside DetachSizeToContentRoot: that also runs when the
            // reconciler merely swaps the root (below) while the window stays
            // maximized, and re-arming there would emit one warning per render.
            _sizeToContentMaximizedWarned = false;
            return;
        }

        if (root is null)
        {
            // Size-to-content is still on; the tree just rendered to nothing
            // (an Empty() root reconciles to a null control, as does a root that
            // is not a FrameworkElement). That is a detach, not the end of the
            // spell, so the latch is deliberately preserved — exactly as for the
            // root-replacement path below. Re-arming here would let a component
            // alternating Empty() with a real root warn once per render.
            DetachSizeToContentRoot();
            return;
        }

        if (!ReferenceEquals(_sizeToContentRoot, root))
        {
            DetachSizeToContentRoot();
            _sizeToContentSizeChangedHandler = (_, _) => ApplySizeToContent();
            _sizeToContentLayoutUpdatedHandler = (_, _) => ApplySizeToContent();
            root.SizeChanged += _sizeToContentSizeChangedHandler;
            root.LayoutUpdated += _sizeToContentLayoutUpdatedHandler;
            _sizeToContentRoot = root;
        }

        // SizeToContent is inherently one layout pass behind the initial content mount,
        // so apply after root layout settles; this can leave one frame at the initial size.
        ApplySizeToContent();
    }

    private void DetachSizeToContentRoot()
    {
        if (_sizeToContentRoot is not null)
        {
            if (_sizeToContentSizeChangedHandler is not null)
                _sizeToContentRoot.SizeChanged -= _sizeToContentSizeChangedHandler;
            if (_sizeToContentLayoutUpdatedHandler is not null)
                _sizeToContentRoot.LayoutUpdated -= _sizeToContentLayoutUpdatedHandler;
        }
        _sizeToContentRoot = null;
        _sizeToContentSizeChangedHandler = null;
        _sizeToContentLayoutUpdatedHandler = null;
    }

    private void OnBackgroundPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (args.OriginalSource is DependencyObject source && !ShouldSuppressBackgroundDrag(source))
            BeginDragMove();
        // Deliberately do not mark handled: pointer/click semantics still bubble
        // for controls, tests, and accessibility (spec 054 §5.3 / R2).
    }

    internal static int SizeToContentMaximizedWarningCountForTests;

    // Edge-trigger for the title-bar-height "not extended" warning. ApplyChrome
    // re-applies on any unequal spec, so without this an app that keeps the
    // invalid combination would warn on every unrelated field change.
    private bool _warnedTitleBarHeightNotExtended;
    internal static int TitleBarHeightNotExtendedWarningCountForTests;

    internal void ApplySizeToContentForTests() => ApplySizeToContent();

    private void ApplySizeToContent()
    {
        var spec = Volatile.Read(ref _spec);
        // Size-to-content off: nothing to apply. Re-arming the maximized-warning
        // latch is AttachSizeToContentRoot's Manual branch's job, which ApplyChrome
        // reaches via OnHostContentRendered on every spec change. Doing it here too
        // would be redundant, and this method also runs for reasons unrelated to a
        // spec change.
        if (spec.SizeToContent == WindowSizeToContent.Manual) return;
        var root = _sizeToContentRoot;
        if (root is null || _sizeToContentApplying) return;

        if (ResolveCurrentState() == WindowState.Maximized)
        {
            // Warn on the edge into the ignored state, not on every layout pass.
            // LayoutUpdated drives this method continuously, and DiagnosticLog.Warning
            // is release-visible, so warning per pass would emit an unbounded ETW
            // stream for as long as the window stays maximized. Re-armed below once
            // it is restored, so a later maximize warns again.
            if (!_sizeToContentMaximizedWarned)
            {
                _sizeToContentMaximizedWarned = true;
                Interlocked.Increment(ref SizeToContentMaximizedWarningCountForTests);
                DiagnosticLog.Warning(LogCategory.Hosting, "ReactorWindow.SizeToContent", "SizeToContent is ignored while the window is maximized.");
            }
            return;
        }

        _sizeToContentMaximizedWarned = false;

        var desiredDip = ResolveSizeToContentDesiredDip(root);
        if (!(desiredDip.Width > 0) || !(desiredDip.Height > 0)) return;

        int desiredClientWidth = DipToPhysicalScalar(desiredDip.Width);
        int desiredClientHeight = DipToPhysicalScalar(desiredDip.Height);
        var rect = new RECT { Left = 0, Top = 0, Right = desiredClientWidth, Bottom = desiredClientHeight };
        long style = (long)NativeShell.GetWindowLongPtr(_hwnd, NativeShell.GWL_STYLE);
        long exStyle = (long)NativeShell.GetWindowLongPtr(_hwnd, NativeShell.GWL_EXSTYLE);
        _ = NativeShell.AdjustWindowRectExForDpi(ref rect, unchecked((uint)style), false, unchecked((uint)exStyle), Dpi == 0 ? 96 : Dpi);

        int targetWidth = Math.Max(1, rect.Right - rect.Left);
        int targetHeight = Math.Max(1, rect.Bottom - rect.Top);
        targetWidth = ClampPhysicalDimension(targetWidth, spec.MinWidth, spec.MaxWidth);
        targetHeight = ClampPhysicalDimension(targetHeight, spec.MinHeight, spec.MaxHeight);

        var current = _appWindow.Size;
        int nextWidth = spec.SizeToContent is WindowSizeToContent.Width or WindowSizeToContent.WidthAndHeight
            ? targetWidth
            : current.Width;
        int nextHeight = spec.SizeToContent is WindowSizeToContent.Height or WindowSizeToContent.WidthAndHeight
            ? targetHeight
            : current.Height;

        if (nextWidth == current.Width && nextHeight == current.Height) return;

        _sizeToContentApplying = true;
        try
        {
            SizeToContentApplyCountForTests++;
            _appWindow.Resize(new global::Windows.Graphics.SizeInt32(nextWidth, nextHeight));
        }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        {
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.SizeToContent.Resize", ex);
        }
        finally
        {
            _sizeToContentApplying = false;
        }
    }

    private int ClampPhysicalDimension(int value, double? minDip, double? maxDip)
    {
        if (minDip is { } min)
            value = Math.Max(value, DipToPhysicalScalar(min));
        if (maxDip is { } max)
            value = Math.Min(value, DipToPhysicalScalar(max));
        return value;
    }

    private static global::Windows.Foundation.Size ResolveSizeToContentDesiredDip(FrameworkElement root)
    {
        FrameworkElement target = root;
        if (root is ScrollViewer sv && sv.Content is FrameworkElement content)
            target = content;

        var desired = target.DesiredSize;
        double width = desired.Width > 0 ? desired.Width : target.ActualWidth;
        double height = desired.Height > 0 ? desired.Height : target.ActualHeight;
        return new global::Windows.Foundation.Size(width, height);
    }

    private static bool ShouldSuppressBackgroundDrag(DependencyObject source)
        => BackgroundDragSuppressorForTests(source) is not null;

    private static bool IsBuiltInInteractive(DependencyObject current)
        => current is ButtonBase
            or TextBox
            or PasswordBox
            or RichEditBox
            or Slider
            or Thumb
            or Selector
            or ListViewBase
            or ComboBox;

    // ── public mutators ───────────────────────────────────────────────

    /// <summary>
    /// Show and focus the window. UI-thread only. No-op after disposal.
    /// </summary>
    [UIThreadOnly]
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
    [UIThreadOnly]
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
    [UIThreadOnly]
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
    [UIThreadOnly]
    public void Close()
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(Close));
        if (_disposed) return;
        _closingReason = WindowCloseReason.AppClosed;
        // Programmatic Window.Close() does not raise AppWindow.Closing in this
        // WinUI host, so the PrepareTitleBarForClose call in OnAppWindowClosing
        // never runs on this path. Do it here, before the native close, while
        // the AppWindow is still alive: a WinUI TitleBar mounted in an
        // ExtendsContentIntoTitleBar=false window corrupts the heap during its
        // teardown unless the window is flipped back into content-extended mode
        // first. Prepare the owned tree too — closing this window can tear down
        // owned descendants that likewise never see their own AppWindow.Closing.
        // Idempotent. (issue #537)
        PrepareTitleBarTreeForClose();
        CloseNativeWindowOnce();
    }

    /// <summary>
    /// Request the underlying native <see cref="Window.Close"/> at most once for
    /// this window, regardless of how many close paths converge on it
    /// (programmatic <see cref="Close"/>, the owner-close cascade, the
    /// <c>ReactorApp</c> exit prep). A redundant native close on a window whose
    /// teardown has already started re-enters native destroy and faults with an
    /// <c>ACCESS_VIOLATION</c> (0xC0000005) that corrupts later windows in the
    /// process — the multi-window batch teardown crash this guards. (issue #647)
    /// </summary>
    // <snippet:teardown-reentry-diagnostic>
    private void CloseNativeWindowOnce()
    {
        if (_disposed || _nativeCloseRequested) return;
        _nativeCloseRequested = true;
        try { _window.Close(); }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        { DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.Close", ex); }
    }
    // </snippet:teardown-reentry-diagnostic>

    /// <summary>Force-save the current window placement when placement persistence is enabled.</summary>
    [UIThreadOnly]
    public void SavePlacement()
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(SavePlacement));
        if (_disposed) return;
        TrySavePersistedPlacement();
    }

    /// <summary>
    /// Diff <paramref name="next"/> against the current spec and apply only the
    /// fields that changed. UI-thread only.
    /// </summary>
    [UIThreadOnly]
    public void Update(WindowSpec next)
    {
        ArgumentNullException.ThrowIfNull(next);
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(Update));
        if (_disposed) throw new ObjectDisposedException(nameof(ReactorWindow));

        next.ValidateThrowing();

        // Warn on entering the condition, not on every Update. The throwing
        // invariants above must run every time, but this validation happens
        // ahead of the equality check below — so an app that keeps a chromeless,
        // non-draggable window while changing any other field (a title, an
        // opacity tween) would otherwise emit one ETW event per update for the
        // life of the window. Re-armed when the condition clears.
        if (!next.HasNoDragAffordance)
        {
            _warnedNoDragAffordance = false;
        }
        else if (!_warnedNoDragAffordance)
        {
            _warnedNoDragAffordance = true;
            next.WarnOnSuspiciousCombinations();
        }

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
            // When the aspect-ratio lock changed to a new non-null value,
            // immediately resize the window to conform — otherwise the new
            // ratio only takes effect on the next interactive resize, which
            // looks broken from the user's perspective. Also re-conform
            // when only the basis flipped (Window ↔ Client) but the ratio
            // stayed the same — switching from window-rect to client-rect
            // at 1:1 means the window must grow vertically to keep the
            // client area square.
            bool aspectChanged = !Nullable.Equals(prev.AspectRatio, next.AspectRatio);
            bool basisChanged = prev.AspectRatioBasis != next.AspectRatioBasis && next.AspectRatio is not null;
            if (aspectChanged || basisChanged)
                ConformToAspectRatio(aspectChanged ? prev.AspectRatio : null, next.AspectRatio);
        }
    }

    /// <summary>Resize to <paramref name="width"/> x <paramref name="height"/> DIPs. UI-thread only.</summary>
    [UIThreadOnly]
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
    [UIThreadOnly]
    public void SetPosition(double x, double y)
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(SetPosition));
        if (_disposed) return;
        try
        {
            _appWindow.Move(DipToPhysicalPoint(x, y));
            TryUpdatePositionCache(raiseEvent: true);
        }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        { DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.SetPosition", ex); }
    }

    /// <summary>Set or clear the width/height aspect lock used during interactive resize.</summary>
    [UIThreadOnly]
    public void SetAspectRatio(double? ratio)
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(SetAspectRatio));
        if (_disposed) return;
        ValidateAspectRatio(ratio, Volatile.Read(ref _spec).ResizeMode);
        var prev = Volatile.Read(ref _spec);
        Volatile.Write(ref _spec, prev with { AspectRatio = ratio });
        ConformToAspectRatio(prev.AspectRatio, ratio);
    }

    /// <summary>
    /// When the aspect-ratio lock changes to a non-null value (either from
    /// null or from a different ratio), immediately resize the window so its
    /// current dimensions conform to the new ratio. Width is preserved and
    /// height is recomputed (<c>height = width / ratio</c>) — matches the
    /// width-master convention used in the WM_SIZING handler for side-edge
    /// drags. Clamps the new height against <c>MinHeight</c>/<c>MaxHeight</c>
    /// constraints. No-op when the new ratio is null, invalid, the same as
    /// the previous (or close enough), or the window is already conforming.
    /// </summary>
    private void ConformToAspectRatio(double? prevRatio, double? newRatio)
    {
        if (newRatio is not double ratio) return;
        if (!(ratio > 0.0) || !double.IsFinite(ratio)) return;
        if (prevRatio is double p && Math.Abs(p - ratio) < 0.0001) return;
        try
        {
            var current = _appWindow.Size;
            if (current.Width <= 0 || current.Height <= 0) return;
            var spec = _spec;
            // See ApplyAspectRatioSizing for the ExtendsContentIntoTitleBar
            // rationale — Client basis is ambiguous when the app paints
            // into the title bar, so fall back to Window basis.
            bool useClientBasis = spec.AspectRatioBasis == AspectRatioBasis.Client
                && !_window.ExtendsContentIntoTitleBar;
            var (chromeH, chromeV) = useClientBasis ? ComputeChromeInset() : (0, 0);
            int clientW = Math.Max(1, current.Width - chromeH);
            int clientH = Math.Max(1, current.Height - chromeV);
            var currentRatio = (double)clientW / clientH;
            if (Math.Abs(currentRatio - ratio) < 0.001) return;

            int newHeight = Math.Max(1, (int)Math.Round(clientW / ratio) + chromeV);
            newHeight = ClampPhysicalDimension(newHeight, spec.MinHeight, spec.MaxHeight);
            _userResized = true;
            _appWindow.Resize(new global::Windows.Graphics.SizeInt32(current.Width, newHeight));
        }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        {
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.ConformToAspectRatio", ex);
        }
    }

    private int _dragMoveActive; // 0 = idle, 1 = drag-in-progress

    /// <summary>Begin a window drag/move loop as if the title bar had been clicked-and-dragged.</summary>
    /// <remarks>
    /// Call from a left-button <c>PointerPressed</c> handler. We snapshot the
    /// initial cursor + window screen position, then run a dispatcher timer
    /// that polls <c>GetCursorPos</c> at ~60Hz and calls <c>AppWindow.Move</c>
    /// until <c>GetAsyncKeyState(VK_LBUTTON)</c> reports the button is no
    /// longer pressed. The timer-based approach is necessary because WinUI 3
    /// routes pointer input through a child <c>InputSiteBridge</c> HWND, so
    /// synthesizing <c>WM_NCLBUTTONDOWN</c> (or <c>WM_SYSCOMMAND</c> +
    /// <c>SC_MOVE | HTCAPTION</c>) on the top-level HWND silently falls into
    /// keyboard/cursor-track move mode rather than mouse-driven click-drag
    /// (DefWindowProc doesn't see a recent WM_LBUTTONDOWN message). The
    /// polling approach is simple, reliable, and doesn't depend on the OS's
    /// non-client message routing. The trade-off vs. the OS modal drag loop
    /// is no Aero Snap during the drag — acceptable for the small floating
    /// windows (command palettes, tool palettes) that need IsMovableByBackground.
    /// </remarks>
    [UIThreadOnly]
    public void BeginDragMove()
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(BeginDragMove));
        if (_disposed) return;
        // Re-entrancy: one active drag at a time per window.
        if (Interlocked.CompareExchange(ref _dragMoveActive, 1, 0) != 0) return;

        Interlocked.Increment(ref BeginDragMovePostCountForTests);

        if (SuppressDragMoveTimerForTests)
        {
            // Selftest path: leave _dragMoveActive=1 so reentrancy tests can
            // observe the guard. Tests reset state at fixture teardown.
            return;
        }

        if (!NativeWindowing.GetCursorPos(out var startCursor))
        {
            Volatile.Write(ref _dragMoveActive, 0);
            return;
        }
        var startWindowPos = _appWindow.Position;

        var timer = _window.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16); // ~60Hz follow rate
        timer.Tick += (_, _) =>
        {
            // Stop when the left mouse button is no longer held — that ends
            // the drag the same way a real title-bar drag does on release.
            if ((NativeWindowing.GetAsyncKeyState(NativeWindowing.VK_LBUTTON) & 0x8000) == 0)
            {
                timer.Stop();
                Volatile.Write(ref _dragMoveActive, 0);
                return;
            }
            if (_disposed)
            {
                timer.Stop();
                Volatile.Write(ref _dragMoveActive, 0);
                return;
            }
            if (!NativeWindowing.GetCursorPos(out var cursor)) return;
            var dx = cursor.X - startCursor.X;
            var dy = cursor.Y - startCursor.Y;
            try
            {
                _appWindow.Move(new global::Windows.Graphics.PointInt32(
                    startWindowPos.X + dx, startWindowPos.Y + dy));
            }
            catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
            {
                // Window closing mid-drag — bail out cleanly.
                timer.Stop();
                Volatile.Write(ref _dragMoveActive, 0);
                DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.BeginDragMove.Move", ex);
            }
        };
        timer.Start();
    }

    internal bool SimulateBackgroundPointerPressedForTests(DependencyObject source)
    {
        if (ShouldSuppressBackgroundDrag(source)) return false;
        BeginDragMove();
        return true;
    }

    internal static string? BackgroundDragSuppressorForTests(DependencyObject source)
    {
        for (DependencyObject? current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (DragAttachedProperties.GetIsDragEnabled(current) == false)
                return $"DragFalse:{current.GetType().Name}";
            if (IsBuiltInInteractive(current))
                return $"Interactive:{current.GetType().Name}";
        }
        return null;
    }

    [UIThreadOnly]
    internal IDisposable RegisterAspectRatioOverride(double? ratio)
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(RegisterAspectRatioOverride));
        ValidateAspectRatio(ratio, Volatile.Read(ref _spec).ResizeMode);
        var entry = new AspectRatioOverride(Interlocked.Increment(ref _nextAspectRatioOverrideId), ratio);
        lock (_aspectRatioOverrideLock)
        {
            var current = Volatile.Read(ref _aspectRatioOverrides);
            var next = new AspectRatioOverride[current.Length + 1];
            Array.Copy(current, next, current.Length);
            next[^1] = entry;
            Volatile.Write(ref _aspectRatioOverrides, next);
        }
        return new AspectRatioOverrideToken(this, entry.Id);
    }

    private sealed record AspectRatioOverride(int Id, double? Ratio);

    private sealed class AspectRatioOverrideToken : IDisposable
    {
        private ReactorWindow? _owner;
        private readonly int _id;
        public AspectRatioOverrideToken(ReactorWindow owner, int id) { _owner = owner; _id = id; }
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null) return;
            lock (owner._aspectRatioOverrideLock)
            {
                var current = Volatile.Read(ref owner._aspectRatioOverrides);
                int idx = Array.FindIndex(current, e => e.Id == _id);
                if (idx < 0) return;
                var next = new AspectRatioOverride[current.Length - 1];
                if (idx > 0) Array.Copy(current, 0, next, 0, idx);
                if (idx < current.Length - 1) Array.Copy(current, idx + 1, next, idx, current.Length - idx - 1);
                Volatile.Write(ref owner._aspectRatioOverrides, next);
            }
        }
    }

    private static void ValidateAspectRatio(double? ratio, WindowResizeMode resizeMode)
    {
        if (ratio is null) return;
        if (!(ratio.Value > 0.0) || !double.IsFinite(ratio.Value))
            throw new ArgumentOutOfRangeException(nameof(ratio), "Aspect ratio must be finite and greater than 0.");
        if (resizeMode == WindowResizeMode.NoResize)
            throw new InvalidOperationException("Aspect ratio cannot be combined with ResizeMode.NoResize.");
    }

    /// <summary>
    /// Set window-wide alpha in [0..1]. 1.0 strips the layered-window
    /// extended style; values below 1.0 install it and call
    /// <c>SetLayeredWindowAttributes</c>. UI-thread only. No-op after disposal.
    /// </summary>
    [UIThreadOnly]
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
    [UIThreadOnly]
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
    [UIThreadOnly]
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
        {
            _ = NativeOpacity.SetWindowLongPtr(_hwnd, NativeOpacity.GWL_EXSTYLE, (nint)updated);
            // SetWindowLong's frame-style changes are cached until
            // SetWindowPos with SWP_FRAMECHANGED runs (MSDN: "Certain
            // window data is cached, so changes you make using
            // SetWindowLong will not take effect until you call the
            // SetWindowPos function"). Without this, WS_EX_TRANSPARENT
            // may be set in the bits but the OS hit-test routing still
            // treats the window as opaque — explaining why the docked
            // tab drag worked (new window, no cache yet) but the
            // floating tab drag didn't reliably (cached opaque state).
            _ = NativeOpacity.SetWindowPos(_hwnd, nint.Zero, 0, 0, 0, 0,
                NativeOpacity.SWP_NOMOVE | NativeOpacity.SWP_NOSIZE
                | NativeOpacity.SWP_NOZORDER | NativeOpacity.SWP_NOACTIVATE
                | NativeOpacity.SWP_FRAMECHANGED);
        }
        var prev = Volatile.Read(ref _spec);
        if (prev.IgnorePointerInput != ignore)
            Volatile.Write(ref _spec, prev with { IgnorePointerInput = ignore });
    }

    private static class NativeShell
    {
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const long WS_BORDER = 0x00800000;
        public const long WS_CAPTION = 0x00C00000;
        public const long WS_SYSMENU = 0x00080000;
        public const long WS_THICKFRAME = 0x00040000;
        public const long WS_MINIMIZEBOX = 0x00020000;
        public const long WS_MAXIMIZEBOX = 0x00010000;
        public const long WS_OVERLAPPEDWINDOW = WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
        public const long WS_CHILD = 0x40000000;
        public const long WS_CLIPSIBLINGS = 0x04000000;
        public const long WS_POPUP = 0x80000000L;
        public const long WS_EX_TOOLWINDOW = 0x00000080;
        public const long WS_EX_APPWINDOW = 0x00040000;
        public const long WS_EX_TOPMOST = 0x00000008;
        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(nint hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(nint hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsZoomed(nint hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AdjustWindowRectExForDpi(ref RECT lpRect, uint dwStyle,
            [MarshalAs(UnmanagedType.Bool)] bool bMenu, uint dwExStyle, uint dpi);

        // The Win32 API name is GetThreadDpiAwarenessContext (no params). The
        // historical typo `GetProcessDpiAwarenessContext` does not exist in
        // user32.dll and threw EntryPointNotFoundException at first call (the
        // thread context is set by SetProcessDpiAwarenessContext at process
        // init, so reading the thread context is equivalent for our embed
        // PerMonitorV2 check).
        [DllImport("user32.dll", EntryPoint = "GetThreadDpiAwarenessContext")]
        public static extern nint GetProcessDpiAwarenessContext();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AreDpiAwarenessContextsEqual(nint dpiContextA, nint dpiContextB);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern nint SetParent(nint hWndChild, nint hWndNewParent);
    }

    private static class NativeWindowing
    {
        public const int VK_LBUTTON = 0x01;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);
    }

    private static class NativeOpacity
    {
        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_LAYERED = 0x00080000;
        public const long WS_EX_NOACTIVATE = 0x08000000;
        public const long WS_EX_TRANSPARENT = 0x00000020;
        public const uint LWA_ALPHA = 0x00000002;

        // SetWindowPos flags — used after SetWindowLong to commit
        // cached frame-style changes (spec 045 §2.6 tear-off).
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);
    }

    /// <summary>Center on the window's current monitor. UI-thread only.</summary>
    [UIThreadOnly]
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
            TryUpdatePositionCache(raiseEvent: true);
        }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        { DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.CenterOnScreen", ex); }
    }

    /// <summary>
    /// Replace the thumbnail-toolbar buttons for this window. Shortcut kept for
    /// compatibility; equivalent to <c>TaskbarItem.SetThumbnailToolbar(...)</c>.
    /// Up to seven buttons; duplicate ids throw. The first call adds the button
    /// set, later calls diff and update only the changed slots. (spec 036 §11.5)
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when more than seven buttons are supplied or when ids are
    /// duplicated.
    /// </exception>
    [UIThreadOnly]
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
    /// Hide all thumbnail-toolbar buttons. Shortcut kept for compatibility;
    /// equivalent to <c>TaskbarItem.ClearThumbnailToolbar()</c>. Idempotent;
    /// safe to call before <see cref="SetThumbnailToolbar"/> has been called.
    /// (spec 036 §11.5)
    /// </summary>
    [UIThreadOnly]
    public void ClearThumbnailToolbar()
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(ClearThumbnailToolbar));
        if (_disposed) return;
        var state = Volatile.Read(ref _thumbnailToolbar);
        state?.Replace(global::System.Array.Empty<ThumbnailToolbarButton>());
    }

    /// <summary>Mount a new component root. UI-thread only.</summary>
    [UIThreadOnly]
    public void Mount(Component root)
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(Mount));
        if (_disposed) throw new ObjectDisposedException(nameof(ReactorWindow));
        ArgumentNullException.ThrowIfNull(root);
        _host.Mount(root);
    }

    /// <summary>Mount a new render-function root. UI-thread only.</summary>
    [UIThreadOnly]
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

        // Non-close teardown path: a direct Dispose() (not preceded by a
        // Close()/cascade) still tears down the content, including any mounted
        // WinUI TitleBar control. Make it safe first, while the window is still
        // alive. Idempotent and a no-op when a close path already prepared it
        // (the usual order: Window.Closed → Dispose). (issue #537)
        PrepareTitleBarForClose();

        _embedWatchdog?.Stop();
        DetachBackgroundDragRoot();
        DetachSizeToContentRoot();

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
    /// On the first <c>WM_SHOWWINDOW</c>, apply initial placement. When
    /// <see cref="WindowSpec.PersistPlacement"/> is enabled we first try the
    /// registered placement store; if no payload restores, we apply
    /// <see cref="WindowSpec.PersistenceFallback"/>. Idempotent: subsequent
    /// shows take no action so hide/show cycles preserve user placement.
    /// </summary>
    private void TryApplyInitialPlacement()
    {
        if (_persistenceRestoreAttempted) return;
        _persistenceRestoreAttempted = true;

        var spec = _spec;

        bool restored = spec.PersistPlacement
            && !string.IsNullOrEmpty(spec.PersistenceId)
            && TryRestorePersistedPlacementCore(spec);
        if (restored)
        {
            _userResized = true;
            return;
        }

        var placement = spec.PersistPlacement ? spec.PersistenceFallback : spec.StartPosition;
        ApplyInitialPlacement(spec, placement);
    }

    private void ApplyInitialPlacement(WindowSpec spec, WindowStartPosition placement)
    {
        try
        {
            switch (placement)
            {
                case WindowStartPosition.Manual when spec.ManualPosition is { } pos:
                    _appWindow.Move(DipToPhysicalPoint(pos.X, pos.Y));
                    TryUpdatePositionCache(raiseEvent: true);
                    break;
                case WindowStartPosition.CenterOnPrimary:
                    CenterIn(DisplayArea.Primary);
                    break;
                case WindowStartPosition.CenterOnOwner:
                    CenterIn(ResolveOwnerDisplayArea(spec.Owner));
                    break;
                case WindowStartPosition.CenterOnCurrent:
                    CenterOnCurrentMonitor();
                    break;
                // Default falls through to WinUI's default placement.
            }
        }
        catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))
        {
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "ReactorWindow.TryApplyInitialPlacement", ex);
        }
    }

    private bool TryRestorePersistedPlacementCore(WindowSpec spec)
    {
        if (!spec.PersistPlacement || string.IsNullOrEmpty(spec.PersistenceId)) return false;
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
        TryUpdatePositionCache(raiseEvent: true);
    }

    private void CenterOnCurrentMonitor()
    {
        if (!NativePlacement.GetCursorPos(out var pt))
        {
            CenterIn(DisplayArea.Primary);
            return;
        }

        nint monitor = NativePlacement.MonitorFromPoint(pt, NativePlacement.MONITOR_DEFAULTTONEAREST);
        if (monitor == 0)
        {
            CenterIn(DisplayArea.Primary);
            return;
        }

        var info = new NativePlacement.MONITORINFO { cbSize = Marshal.SizeOf<NativePlacement.MONITORINFO>() };
        if (!NativePlacement.GetMonitorInfo(monitor, ref info))
        {
            CenterIn(DisplayArea.Primary);
            return;
        }

        var work = info.rcWork;
        var size = _appWindow.Size;
        int x = work.Left + Math.Max(0, (work.Right - work.Left - size.Width) / 2);
        int y = work.Top + Math.Max(0, (work.Bottom - work.Top - size.Height) / 2);
        _appWindow.Move(new global::Windows.Graphics.PointInt32(x, y));
        TryUpdatePositionCache(raiseEvent: true);
    }

    private static class NativePlacement
    {
        public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

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
    private bool TrySavePersistedPlacement()
    {
        var spec = _spec;
        if (!spec.PersistPlacement || string.IsNullOrEmpty(spec.PersistenceId)) return false;

        var store = ReactorApp.ResolvePersistenceStore();
        if (store is null) return false;

        // Same shape as TryRestorePersistedPlacementCore — every downstream
        // failure mode now returns a sentinel value (null/false) rather than
        // throwing. store.Write narrows internally per the C.5 audit entry.
        var monitors = MonitorEnumeration.Snapshot();
        var payload = WindowPlacementCodec.Capture(_hwnd, monitors);
        if (payload is null) return false;
        store.Write(spec.PersistenceId!, payload);
        return true;
    }
}
