using System.Diagnostics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Owns one OS top-level Window and one <see cref="ReactorHost"/>. Created via
/// <see cref="ReactorApp.OpenWindow(WindowSpec, Func{Component})"/> or
/// <see cref="ReactorAppContext.OpenWindow(WindowSpec, Func{Component})"/>.
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
    private static int s_nextId;

    private readonly string _id;
    private readonly Window _window;
    private readonly AppWindow _appWindow;
    private readonly ReactorHost _host;
    private WindowSpec _spec;
    private uint _dpi = 96;
    private int _stateValue; // backing storage for State (cast WindowState <-> int)
    private bool _disposed;
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

    /// <summary>Last applied <see cref="WindowSpec"/> snapshot.</summary>
    public WindowSpec Spec => Volatile.Read(ref _spec);

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

        ApplyChrome(spec, isInitial: true);

        _host = new ReactorHost(_window);
        _host.OwningWindow = this;

        _window.Activated += OnNativeActivated;
        _window.Closed += OnNativeClosed;
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
        catch (Exception ex) { Debug.WriteLine($"[Reactor] Window.Title set failed: {ex.Message}"); }

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
        catch (Exception ex) { Debug.WriteLine($"[Reactor] Presenter apply failed: {ex.Message}"); }

        try { _appWindow.IsShownInSwitchers = spec.IsShownInSwitchers; }
        catch (Exception ex) { Debug.WriteLine($"[Reactor] IsShownInSwitchers failed: {ex.Message}"); }

        try { _window.ExtendsContentIntoTitleBar = spec.ExtendsContentIntoTitleBar; }
        catch (Exception ex) { Debug.WriteLine($"[Reactor] ExtendsContentIntoTitleBar failed: {ex.Message}"); }

        // Sizing — Phase 1 still uses raw pixel semantics. Phase 2 layers a
        // DIP→physical conversion atop AppWindow.Resize via the per-window DPI.
        if (isInitial && spec.Presenter == PresenterKind.Overlapped)
        {
            try
            {
                _appWindow.Resize(new global::Windows.Graphics.SizeInt32(
                    (int)Math.Round(spec.Width), (int)Math.Round(spec.Height)));
            }
            catch (Exception ex) { Debug.WriteLine($"[Reactor] Initial resize failed: {ex.Message}"); }
        }

        if (spec.Icon is { } icon)
            icon.Apply(_appWindow);
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

    private void OnNativeClosed(object? sender, WindowEventArgs args)
    {
        if (_disposed) return;
        try { Closed?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { Debug.WriteLine($"[Reactor] Closed handler threw: {ex.Message}"); }

        ReactorApp.UnregisterWindow(this);
        Dispose();
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
        try { _appWindow.Hide(); } catch (Exception ex) { Debug.WriteLine($"[Reactor] Hide failed: {ex.Message}"); }
        IsVisible = false;
    }

    /// <summary>
    /// Show a previously hidden window. UI-thread only. No-op after disposal.
    /// </summary>
    public void Show()
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(Show));
        if (_disposed) return;
        try { _appWindow.Show(); } catch (Exception ex) { Debug.WriteLine($"[Reactor] Show failed: {ex.Message}"); }
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
        catch (Exception ex) { Debug.WriteLine($"[Reactor] Close failed: {ex.Message}"); }
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
            ApplyChrome(next, isInitial: false);
    }

    /// <summary>Resize to <paramref name="width"/> x <paramref name="height"/> DIPs. UI-thread only.</summary>
    public void SetSize(double width, double height)
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(SetSize));
        if (_disposed) return;
        if (!(width > 0) || !(height > 0))
            throw new ArgumentOutOfRangeException(nameof(width), "Width and height must be positive.");
        // Phase 1: pixel-pass-through sizing (DPI conversion in Phase 2).
        try
        {
            _appWindow.Resize(new global::Windows.Graphics.SizeInt32(
                (int)Math.Round(width), (int)Math.Round(height)));
        }
        catch (Exception ex) { Debug.WriteLine($"[Reactor] SetSize failed: {ex.Message}"); }
    }

    /// <summary>Move to <paramref name="x"/>,<paramref name="y"/> DIPs. UI-thread only.</summary>
    public void SetPosition(double x, double y)
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(SetPosition));
        if (_disposed) return;
        try
        {
            _appWindow.Move(new global::Windows.Graphics.PointInt32(
                (int)Math.Round(x), (int)Math.Round(y)));
        }
        catch (Exception ex) { Debug.WriteLine($"[Reactor] SetPosition failed: {ex.Message}"); }
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
        catch (Exception ex) { Debug.WriteLine($"[Reactor] CenterOnScreen failed: {ex.Message}"); }
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

        try { _window.Activated -= OnNativeActivated; } catch { /* best effort */ }
        try { _window.Closed -= OnNativeClosed; } catch { /* best effort */ }

        // ReactorHost already subscribes to Window.Closed; let it dispose itself.
        // We avoid double-dispose because Dispose() is idempotent there too.
        try { _host.Dispose(); } catch (Exception ex) { Debug.WriteLine($"[Reactor] Host dispose failed: {ex.Message}"); }
    }

    /// <summary>The reason the close currently in progress was initiated. Phase 3.</summary>
    internal WindowCloseReason ClosingReason => _closingReason;
}
