// Minimal windowing layer for the Uno port.
//
// The Windows framework's windowing stack (src/Reactor/Hosting/ReactorWindow.cs,
// ReactorDisplay.cs, WindowSpec.cs, …) is heavily Win32/AppWindow/DWM coupled and
// is excluded from the Uno build. The shared core (Core/RenderContext.cs windowing
// hooks, Core/Element.cs title-bar wiring) still references these *types*, so this
// file provides Uno-friendly equivalents with the exact member surface the shared
// source touches. Windows are real: ReactorApp.OpenWindow builds one of these per
// Microsoft.UI.Xaml.Window, each with its own ReactorHost. The chrome members Skia
// can't honour (tray, drag-move, aspect-ratio lock, display enumeration) are no-op
// stubs so the shared core still compiles and degrades gracefully.

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Windowing;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace Microsoft.UI.Reactor;

/// <summary>Window display state. Mirrors the Windows framework's enum.</summary>
public enum WindowState
{
    Normal,
    Minimized,
    Maximized,
}

/// <summary>Stable identity key for <c>UseOpenWindow</c> de-duplication.</summary>
public readonly record struct WindowKey(string Name);

/// <summary>
/// Caption height for a window whose content extends into the title bar.
/// Mirrors the Windows framework's enum (and <c>Microsoft.UI.Windowing.TitleBarHeightOption</c>).
/// </summary>
/// <remarks>
/// Declared here rather than source-shared because it lives in the Windows-only
/// <c>Hosting/WindowSpec.cs</c>, which the Uno port replaces — but the shared
/// <c>Core/Element.cs</c> and <c>Elements/ElementExtensions.cs</c> reference the
/// type (<c>TitleBarElement.HeightOption</c> / <c>.Tall()</c>, issue #917).
/// Uno implements the underlying <c>AppWindowTitleBar.PreferredHeightOption</c>
/// on the desktop heads, so this is honoured for real.
/// </remarks>
public enum WindowTitleBarHeight
{
    /// <summary>Standard 32 DIP caption.</summary>
    Standard,
    /// <summary>Tall 48 DIP caption — the layout used when the title bar hosts navigation chrome.</summary>
    Tall,
    /// <summary>No caption area at all.</summary>
    Collapsed,
}

/// <summary>
/// Declarative description of a window's chrome. Only the members the shared
/// core reads are modelled; the rest of the Windows <c>WindowSpec</c> surface
/// (backdrop, embed, persistence, splitters, …) is intentionally omitted.
/// </summary>
public sealed record WindowSpec
{
    public string Title { get; init; } = "Reactor App";

    /// <summary>
    /// Initial DIP width. <c>null</c> (the default) leaves the initial width to
    /// the OS, matching the Windows framework (issue #924) — Reactor does not
    /// override that axis. When both axes are <c>null</c> no resize is issued.
    /// </summary>
    public double? Width { get; init; }

    /// <summary>Initial DIP height. <c>null</c> defers to the OS. See <see cref="Width"/>.</summary>
    public double? Height { get; init; }

    public bool FullScreen { get; init; }
    public WindowKey? Key { get; init; }
    /// <summary>Null = framework default; true/false = explicit opt-in/out.</summary>
    public bool? ExtendsContentIntoTitleBar { get; init; }

    /// <summary>
    /// System caption height. <c>null</c> leaves the platform default. Honoured
    /// on the Uno desktop heads via <c>AppWindow.TitleBar.PreferredHeightOption</c>.
    /// </summary>
    public WindowTitleBarHeight? TitleBarHeight { get; init; }
}

/// <summary>Tray-icon spec stub — tray icons are a Windows shell feature.</summary>
public sealed record TrayIconSpec(WindowKey Key);

/// <summary>Live tray-icon handle stub.</summary>
public sealed class ReactorTrayIcon
{
    internal ReactorTrayIcon(TrayIconSpec spec) => Spec = spec;
    public TrayIconSpec Spec { get; private set; }
    public void Update(TrayIconSpec spec) => Spec = spec;
    public void Close() { }
}

/// <summary>Snapshot of a single display. Minimal shape used by <c>UseDisplays</c>.</summary>
public readonly record struct DisplayInfo(
    string Name,
    double X,
    double Y,
    double Width,
    double Height,
    double Scale,
    bool IsPrimary);

/// <summary>
/// Display enumeration. Skia heads don't expose a portable multi-monitor query,
/// so this returns an empty snapshot and never raises layout-change events.
/// </summary>
public static class ReactorDisplay
{
    public static IReadOnlyList<DisplayInfo> Displays { get; } = Array.Empty<DisplayInfo>();

#pragma warning disable CS0067 // event is part of the API surface; never raised on Skia
    public static event EventHandler? DisplayLayoutChanged;
#pragma warning restore CS0067
}

/// <summary>Event args for <see cref="ReactorWindow.PositionChanged"/>.</summary>
public sealed class WindowDipPositionChangedEventArgs : EventArgs
{
    public WindowDipPositionChangedEventArgs((double X, double Y) position) => Position = position;
    public (double X, double Y) Position { get; }
}

/// <summary>Event args for <see cref="ReactorWindow.ZOrderChanged"/>.</summary>
public sealed class WindowZOrderChangedEventArgs : EventArgs
{
    public WindowZOrderChangedEventArgs(bool movedToTop, bool isCovered)
    {
        MovedToTop = movedToTop;
        IsCovered = isCovered;
    }
    public bool MovedToTop { get; }
    public bool IsCovered { get; }
}

/// <summary>
/// Uno wrapper over a <see cref="Microsoft.UI.Xaml.Window"/>. Bridges the
/// shared windowing hooks to the live Skia window. Members the core never reads
/// are best-effort/no-op on Skia.
/// </summary>
public sealed class ReactorWindow
{
    private readonly object _titleBarLock = new();
    private bool _titleBarControlPresent;

    internal ReactorWindow(WinUIWindow nativeWindow, WindowSpec spec)
    {
        NativeWindow = nativeWindow;
        Spec = spec;

        // Bridge the WinUI window's activation events to the simple EventHandler
        // shape the shared hooks expect.
        nativeWindow.Activated += (_, args) =>
        {
            bool deactivated =
                args.WindowActivationState == global::Windows.UI.Core.CoreWindowActivationState.Deactivated;
            if (deactivated)
            {
                IsActive = false;
                Deactivated?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                IsActive = true;
                Activated?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <summary>The underlying WinUI/Uno window.</summary>
    public WinUIWindow NativeWindow { get; }

    /// <summary>The spec this window was opened with.</summary>
    public WindowSpec Spec { get; private set; }

    /// <summary>The Reactor host rendering into this window.</summary>
    public Microsoft.UI.Reactor.Hosting.ReactorHost? Host { get; internal set; }

    public bool IsActive { get; private set; } = true;
    public WindowState State { get; private set; } = WindowState.Normal;
    public (double X, double Y) Position { get; private set; }

    /// <summary>Per-window persisted-state scope (backs <c>UsePersisted(PersistedScope.Window)</c>).</summary>
    public Microsoft.UI.Reactor.Core.IPersistedStateScope PersistedScope { get; }
        = new Microsoft.UI.Reactor.Core.WindowPersistedScope();

    /// <summary>
    /// Per-window DPI as a raw dots-per-inch value (96 = 100%). Derived from the
    /// window's <c>XamlRoot.RasterizationScale</c> when available.
    /// </summary>
    public uint Dpi
    {
        get
        {
            try
            {
                var scale = NativeWindow.Content?.XamlRoot?.RasterizationScale ?? 1.0;
                return (uint)Math.Round(96.0 * (scale <= 0 ? 1.0 : scale));
            }
            catch { return 96; }
        }
    }

    /// <summary>Raised when this window's DPI changes (monitor move or display-scale change).</summary>
    public event EventHandler<uint>? DpiChanged;

    /// <summary>Raised when the window becomes active.</summary>
    public event EventHandler? Activated;

    /// <summary>Raised when the window is deactivated.</summary>
    public event EventHandler? Deactivated;

#pragma warning disable CS0067 // Skia heads don't surface these; kept as API surface.
    public event EventHandler<WindowDipPositionChangedEventArgs>? PositionChanged;
    public event EventHandler<WindowZOrderChangedEventArgs>? ZOrderChanged;
    public event EventHandler<WindowState>? StateChanged;
#pragma warning restore CS0067

    // ── DPI-change notification ──
    //
    // Uno raises XamlRoot.Changed when the window's RasterizationScale changes
    // (dragged to a monitor with a different scale, or the display scale is
    // changed). It also fires for size changes, so only surface DpiChanged when
    // the DPI value actually moved. Each window has its own XamlRoot, so this is
    // correctly per-window under multi-window.
    private Microsoft.UI.Xaml.XamlRoot? _xamlRoot;
    private uint _lastDpi;

    // Called by ReactorHost whenever it installs new content into this window —
    // the XamlRoot only exists once content is attached.
    internal void OnContentAttached(Microsoft.UI.Xaml.UIElement? content)
    {
        var root = content?.XamlRoot;

        // XamlRoot is not assigned when Content is merely *set* — it appears
        // when the element enters the live visual tree, which happens after
        // Window.Activate(). Reactor attaches content from the render loop, so
        // the first call here always arrives with a null XamlRoot. Returning
        // early on that (null == null compares equal) would permanently skip
        // both the DPI listener and the initial resize, so re-arm on Loaded and
        // come back once the root really exists.
        if (root is null)
        {
            if (content is Microsoft.UI.Xaml.FrameworkElement fe)
            {
                void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
                {
                    fe.Loaded -= OnLoaded;
                    OnContentAttached(fe);
                }
                fe.Loaded -= OnLoaded;
                fe.Loaded += OnLoaded;
            }
            return;
        }

        if (ReferenceEquals(root, _xamlRoot)) return;

        if (_xamlRoot is not null)
            _xamlRoot.Changed -= OnXamlRootChanged;

        _xamlRoot = root;

        _lastDpi = Dpi;
        _xamlRoot.Changed += OnXamlRootChanged;

        // The XamlRoot has just appeared, so RasterizationScale is finally real —
        // retry the initial resize if it had to be deferred (see ApplyInitialSize).
        ApplyInitialSize();
    }

    private bool _initialSizeApplied;

    /// <summary>
    /// Applies <see cref="WindowSpec.Width"/>/<see cref="WindowSpec.Height"/> once.
    /// </summary>
    /// <remarks>
    /// <para>Spec sizes are <b>DIPs</b>, but <c>AppWindow.Resize</c> takes
    /// <b>physical pixels</b> — Uno's Win32 head hands the <c>SizeInt32</c>
    /// straight to <c>SetWindowPos</c>, and Uno's own startup path multiplies by
    /// <c>RasterizationScale</c> for exactly this reason. Without the conversion
    /// every window is undersized above 100% scale (a 480x360 request measured
    /// 320x240 DIP at 150%).</para>
    /// <para>The scale is only knowable once the window has a <c>XamlRoot</c>,
    /// which does not exist at <c>Activate</c> time because Reactor attaches
    /// content from the dispatcher-queued render loop. So this is attempted
    /// after activation and, if the scale isn't available yet, re-attempted from
    /// <see cref="OnContentAttached"/>. It applies at most once either way.</para>
    /// <para>A null axis means "let the OS pick" (issue #924); a spec with
    /// neither axis set never resizes.</para>
    /// </remarks>
    internal void ApplyInitialSize()
    {
        if (_initialSizeApplied) return;

        if (Spec.Width is null && Spec.Height is null)
        {
            _initialSizeApplied = true;
            return;
        }

        var scale = NativeWindow.Content?.XamlRoot?.RasterizationScale ?? 0;
        if (scale <= 0) return; // not realized yet — OnContentAttached retries

        try
        {
            var appWindow = NativeWindow.AppWindow;
            if (appWindow is null)
            {
                _initialSizeApplied = true;
                return;
            }

            // Preserve the OS-chosen extent on whichever axis the spec leaves null.
            var current = appWindow.Size;
            int width = Spec.Width is { } w ? (int)Math.Round(w * scale) : current.Width;
            int height = Spec.Height is { } h ? (int)Math.Round(h * scale) : current.Height;

            appWindow.Resize(new global::Windows.Graphics.SizeInt32
            {
                Width = width,
                Height = height,
            });
        }
        catch { /* sizing unsupported on this head */ }

        _initialSizeApplied = true;
    }

    private void OnXamlRootChanged(
        Microsoft.UI.Xaml.XamlRoot sender,
        Microsoft.UI.Xaml.XamlRootChangedEventArgs args)
    {
        uint dpi = Dpi;
        if (dpi == _lastDpi) return;

        _lastDpi = dpi;
        DpiChanged?.Invoke(this, dpi);
    }

    /// <summary>Lifetime-bound aspect-ratio lock. No-op on Skia; returns a disposable token.</summary>
    public IDisposable RegisterAspectRatioOverride(double? widthOverHeight) => NoopDisposable.Instance;

    // ── closing guards ──
    //
    // Backed by Uno's AppWindow.Closing, which honours cancellation on the desktop
    // heads (Windows / macOS / Linux). On Android, iOS and WebAssembly the event
    // still fires but Cancel has no effect, so the close proceeds — matching Uno's
    // documented behaviour rather than pretending to guard.
    //
    // AppWindow.Closing must be handled synchronously (async work does not delay the
    // close), which is exactly the Func<bool> contract the shared UseClosingGuard
    // hook exposes.
    private readonly object _closingGuardsLock = new();
    private readonly List<Func<bool>> _closingGuards = new();
    private bool _closingHooked;

    /// <summary>
    /// Registers a synchronous "can the window close right now?" predicate.
    /// Multiple guards stack — any returning <c>false</c> cancels the close.
    /// Dispose the returned token to unregister.
    /// </summary>
    public IDisposable RegisterClosingGuard(Func<bool> canClose)
    {
        ArgumentNullException.ThrowIfNull(canClose);

        lock (_closingGuardsLock)
        {
            _closingGuards.Add(canClose);
            EnsureClosingHooked();
        }

        return new GuardToken(this, canClose);
    }

    // Subscribe lazily: a window that never registers a guard pays nothing, and by
    // the time a guard arrives (from an effect, so post-mount) the AppWindow exists.
    private void EnsureClosingHooked()
    {
        if (_closingHooked) return;

        try
        {
            var appWindow = NativeWindow.AppWindow;
            if (appWindow is null) return;

            appWindow.Closing += OnAppWindowClosing;
            _closingHooked = true;
        }
        catch { /* AppWindow unavailable on this head */ }
    }

    private void OnAppWindowClosing(
        Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        Func<bool>[] guards;
        lock (_closingGuardsLock) { guards = _closingGuards.ToArray(); }

        for (int i = 0; i < guards.Length; i++)
        {
            bool canClose;

            // A guard is app code. A throwing guard fail-safes to "cancel" rather
            // than escaping mid-close, matching the Windows framework's behaviour.
            try { canClose = guards[i](); }
            catch { canClose = false; }

            if (!canClose)
            {
                args.Cancel = true;
                return;
            }
        }
    }

    private sealed class GuardToken : IDisposable
    {
        private readonly ReactorWindow _owner;
        private Func<bool>? _guard;

        public GuardToken(ReactorWindow owner, Func<bool> guard)
        {
            _owner = owner;
            _guard = guard;
        }

        public void Dispose()
        {
            var g = Interlocked.Exchange(ref _guard, null);
            if (g is null) return;
            lock (_owner._closingGuardsLock) { _owner._closingGuards.Remove(g); }
        }
    }

    /// <summary>Starts a framework-managed window drag/move loop. No-op on Skia.</summary>
    public void BeginDragMove() { }

    /// <summary>Records that a WinUI TitleBar control is mounted in this window.</summary>
    public void MarkTitleBarControlPresent()
    {
        lock (_titleBarLock) { _titleBarControlPresent = true; }
    }

    internal bool TitleBarControlPresent
    {
        get { lock (_titleBarLock) { return _titleBarControlPresent; } }
    }

    // ── Caption height (issue #917) ────────────────────────────────────────
    //
    // The shared TitleBar element and reconciler call into these three members,
    // so the Uno window has to provide them. They are NOT stubs: Uno implements
    // AppWindowTitleBar.PreferredHeightOption for real on the desktop heads
    // (Standard=32 / Tall=48 / Collapsed=0), so the caption half genuinely works.
    //
    // The *control* half (sizing the Microsoft.UI.Xaml.Controls.TitleBar so it
    // agrees with the caption) is kept faithful to the Windows implementation
    // even though Uno currently ships that control as a not-implemented stub —
    // writing Height on it is harmless today and correct the moment Uno lands it.

    private WeakReference<Microsoft.UI.Xaml.FrameworkElement>? _titleBarControl;
    private bool _titleBarControlExplicitHeight;
    private bool _titleBarControlHeightOwned;
    private WindowTitleBarHeight? _elementTitleBarHeight;
    private WindowTitleBarHeight? _effectiveTitleBarHeight;

    /// <summary>
    /// Withdraws a departing <c>TitleBar(...)</c> element's caption-height
    /// contribution so a window that merely used to host one is not left tall
    /// forever. Mirrors the Windows framework, including deliberately NOT
    /// clearing <see cref="TitleBarControlPresent"/> (that latch drives
    /// close-time teardown safety, where "was mounted at some point" is right).
    /// </summary>
    internal void ClearTitleBarControl()
    {
        _titleBarControl = null;
        _titleBarControlExplicitHeight = false;
        _titleBarControlHeightOwned = false;
        _elementTitleBarHeight = null;
        ApplyTitleBarHeight();
    }

    /// <summary>
    /// Records the caption height declared by the mounted <c>TitleBar(...)</c>
    /// element and applies both halves. <see cref="WindowSpec.TitleBarHeight"/>,
    /// when set, wins over the element's declaration.
    /// </summary>
    internal void SetElementTitleBarHeight(
        WindowTitleBarHeight? height,
        Microsoft.UI.Xaml.FrameworkElement? control,
        bool controlHasExplicitHeight)
    {
        _elementTitleBarHeight = height;
        _titleBarControl = control is null
            ? null
            : new WeakReference<Microsoft.UI.Xaml.FrameworkElement>(control);
        _titleBarControlExplicitHeight = controlHasExplicitHeight;
        ApplyTitleBarHeight();
    }

    /// <summary>
    /// Sizes the mounted WinUI <c>TitleBar</c> control to the caption height
    /// Reactor actually applied — the control does not track the caption, so
    /// Reactor pairs the two. An explicit <c>.Height(...)</c> owns the control
    /// outright, and Reactor only clears a height it set itself.
    /// </summary>
    internal void SyncTitleBarControlHeight()
    {
        if (_titleBarControlExplicitHeight) return;
        if (_titleBarControl is null || !_titleBarControl.TryGetTarget(out var control)) return;

        try
        {
            if (_effectiveTitleBarHeight == WindowTitleBarHeight.Tall)
            {
                if (_titleBarControlHeightOwned
                    && Math.Abs(control.Height - TitleBarElement.TallTitleBarControlHeight) < 0.5)
                {
                    return;
                }
                control.Height = TitleBarElement.TallTitleBarControlHeight;
                _titleBarControlHeightOwned = true;
            }
            else if (_titleBarControlHeightOwned)
            {
                control.ClearValue(Microsoft.UI.Xaml.FrameworkElement.HeightProperty);
                _titleBarControlHeightOwned = false;
            }
        }
        catch { /* control not realized on this head */ }
    }

    // Writes the native caption height. The spec wins over the element; a
    // non-extended window can't host a resized caption, so the control is held
    // at Standard in that case (same rule as the Windows framework).
    private void ApplyTitleBarHeight()
    {
        var resolved = Spec.TitleBarHeight ?? _elementTitleBarHeight;
        if (resolved is null)
        {
            _effectiveTitleBarHeight = null;
            SyncTitleBarControlHeight();
            return;
        }

        try
        {
            var titleBar = NativeWindow.AppWindow?.TitleBar;
            if (titleBar is null || !titleBar.ExtendsContentIntoTitleBar)
            {
                _effectiveTitleBarHeight = WindowTitleBarHeight.Standard;
                SyncTitleBarControlHeight();
                return;
            }

            titleBar.PreferredHeightOption = resolved.Value switch
            {
                WindowTitleBarHeight.Tall => TitleBarHeightOption.Tall,
                WindowTitleBarHeight.Collapsed => TitleBarHeightOption.Collapsed,
                _ => TitleBarHeightOption.Standard,
            };
            _effectiveTitleBarHeight = resolved;
        }
        catch { /* caption sizing unsupported on this head */ }

        SyncTitleBarControlHeight();
    }

    /// <summary>Applies a changed spec to the live window (title only on Skia).</summary>
    public void Update(WindowSpec spec)
    {
        Spec = spec;
        try { NativeWindow.Title = spec.Title; } catch { /* best effort */ }
    }

    /// <summary>Closes the underlying window.</summary>
    public void Close()
    {
        try { NativeWindow.Close(); } catch { /* best effort */ }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }
}
