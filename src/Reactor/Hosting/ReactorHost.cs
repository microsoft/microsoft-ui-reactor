using System.Diagnostics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Hosts a Reactor component tree inside a WinUI Window.
/// Manages the render loop: when state changes, re-renders the component
/// and reconciles the virtual tree against the real WinUI control tree.
/// </summary>
public sealed class ReactorHost : IDisposable
{
#pragma warning disable CS0414 // Design constant for render-loop limiting; wiring pending
    private static readonly int MaxRenderIterations = 50;
#pragma warning restore CS0414

    private readonly Window _window;
    private readonly Reconciler _reconciler;
    private readonly DispatcherQueue _dispatcherQueue;
    // Null when the caller passes no logger and ReactorApp.AppLogger is unset.
    // Snapshotted at ctor; later AppLogger writes don't retro-wire this host.
    // Leaving null keeps the Microsoft.Extensions.Logging call paths off the
    // JIT critical path — saves ~3-5 ms of cold-start JIT + assembly resolve.
    private readonly ILogger? _logger;

    private Component? _rootComponent;
    // The root's ReactorEventSource.ComponentRendered bookkeeping (id, first render).
    private readonly Microsoft.UI.Reactor.Core.Diagnostics.RootRenderDiagnostics _rootDiagnostics = new();
    private Func<RenderContext, Element>? _rootRenderFunc;
    private RenderContext? _funcContext;

    private Element? _currentTree;
    private UIElement? _currentControl;
    private int _renderPending;    // 0 or 1 — Interlocked for thread-safe access
    private volatile bool _isRendering;     // only touched on UI thread
    private volatile bool _needsRerender;   // only touched on UI thread
    private FrameworkElement? _themeListenerElement;
    // Test-only accessor (InternalsVisibleTo Reactor.AppTests.Host).
    internal FrameworkElement? ThemeListenerElement => _themeListenerElement;
    private volatile bool _disposed;

    // Set when the owning window has raised Closed — i.e. the native window is
    // being torn down. Drives BackdropApplier.Reset so it does NOT write
    // Window.SystemBackdrop on an already-destroyed window, which AVs
    // (0xC0000005) and corrupts the WinUI backdrop interop for later windows in
    // the same process. (issue #647)
    private bool _windowClosed;

    // Spec 033 §6 — declarative SystemBackdrop modifier on the root tree.
    // Owned for the host's lifetime; reset on dispose so a non-Reactor
    // window-reuse path returns to a clean slate.
    private readonly BackdropApplier _backdropApplier;

    /// <summary>
    /// Internal accessor for <see cref="ReactorWindow"/> to seed
    /// <see cref="WindowSpec.Backdrop"/> as the window-level default before
    /// mount. (spec 036 §3.3)
    /// </summary>
    internal BackdropApplier BackdropApplier => _backdropApplier;
    private readonly global::Windows.Foundation.TypedEventHandler<object, WindowEventArgs> _closedHandler;

    // Accessibility: forced-colors and reduced-motion auto-propagation.
    // Allocation is deferred until the first chart element is created (see
    // EnsureChartingActive). Apps without charts skip the WinRT activation
    // cost (15–30 ms each) and the Charting.D3Charts cctor cascade.
    private global::Windows.UI.ViewManagement.AccessibilitySettings? _accessibilitySettings;
    private global::Windows.UI.ViewManagement.UISettings? _uiSettings;
    private volatile bool _isForcedColors;
    private volatile bool _isReducedMotion;
    // Opaque forced-colors theme payload produced by the charting bridge
    // (issue #498). Typed as object so the host never statically references
    // Charting.ForcedColorsTheme — keeping it out of chart-free AOT binaries.
    private object? _forcedColorsTheme;
    // 0 = inactive, 1 = activation in flight or done. Flipped atomically
    // via Interlocked.CompareExchange so concurrent chart-element creation
    // from background threads can't double-subscribe HighContrastChanged.
    private int _chartingActiveFlag;

    // Captured AnimationScope curve — when a state setter is called inside
    // WithAnimation, the scope is synchronous but the render is async.
    // We capture the curve here so the reconcile pass can restore it.
    private Curve? _pendingAnimationCurve;

    // Captured AmbientAnimation snapshot (spec 042 §6 Q3). Setters that
    // fire inside Animations.Animate(...) snapshot AnimationAmbient.Current
    // synchronously and stash it here; the render loop re-pushes it onto
    // the AsyncLocal stack around the reconcile pass so KeyedListDiff /
    // ChildReconciler observe the same intent even when the rerender hops
    // a dispatcher. Last-writer-wins matches _pendingAnimationCurve.
    private Microsoft.UI.Reactor.Core.Internal.AmbientAnimation? _pendingAmbientAnimation;

    // ── Single shared overlay surface ──
    // One wrapper Grid + Canvas hosts every dev overlay (reconcile highlight,
    // layout cost, future additions). Constructed lazily when any overlay flag
    // flips on.
    private OverlayHostWiring? _overlayWiring;

    /// <summary>Test-only: access the live overlay-host wiring (null if not constructed).</summary>
    internal OverlayHostWiring? OverlayWiring => _overlayWiring;

    // Render phase timing instrumentation
    private readonly Stopwatch _phaseSw = new();
    private double _treeBuildSum;
    private double _reconcileSum;
    private double _effectsSum;
    private int _renderCount;
    private readonly Stopwatch _reportClock = Stopwatch.StartNew();
    private long _totalRenderCount;

    // Last render's total duration (tree + reconcile + effects), in ms.
    // Read by RequestRender to demote the next enqueue to Low priority when a
    // slow render is starving the dispatcher of input/layout/paint slots.
    // Published via Interlocked.Exchange / read via Volatile.Read because the
    // write happens on the UI thread inside Render() but RequestRender() can
    // be called from any thread — a plain double write is not guaranteed
    // atomic on 32-bit and lacks the publication semantics this contract
    // implies. See RenderPriorityPolicy.
    private double _lastRenderMs;

    // Public perf snapshot — updated every ~1 second, readable from components
    private RenderStats _stats;

    // Issue #660 (#179/#180): per-frame delegates cached once instead of
    // re-allocated every render. _rerenderAction wraps the optional-arg
    // RequestRender (which can't bind to an Action method group directly);
    // _renderLoopHandler is the DispatcherQueueHandler passed to TryEnqueue.
    private Action? _rerenderAction;
    private Microsoft.UI.Dispatching.DispatcherQueueHandler? _renderLoopHandler;

    /// <summary>
    /// Live render performance snapshot, updated every ~1 second.
    /// Always available (FPS, frame time). DEBUG builds include per-reconcile element counters.
    /// </summary>
    public ref readonly RenderStats Stats => ref _stats;

    /// <summary>
    /// Provides access to the underlying reconciler for RegisterType calls.
    /// </summary>
    public Reconciler Reconciler => _reconciler;

    /// <summary>
    /// Optional callback invoked after each render pass with phase timings (ms):
    /// treeBuildMs, reconcileMs, effectsMs. Used by perf harnesses to capture
    /// the breakdown of a Reactor render cycle.
    /// </summary>
    public Action<double, double, double>? OnRenderComplete { get; set; }

    /// <summary>
    /// The WinUI Window hosting this Reactor tree.
    /// Useful for obtaining the HWND (e.g., for file pickers in unpackaged apps).
    /// </summary>
    public Window Window => _window;

    /// <summary>
    /// The <see cref="ReactorWindow"/> that owns this host, when the host was
    /// constructed by Reactor's window primitive. Null for hosts created
    /// directly (test harnesses, <see cref="ReactorHostControl"/> embeds).
    /// (spec 036 §3.4)
    /// </summary>
    public ReactorWindow? OwningWindow
    {
        get => Volatile.Read(ref _owningWindow);
        internal set => Volatile.Write(ref _owningWindow, value);
    }
    private ReactorWindow? _owningWindow;

    /// <summary>
    /// The currently mounted root Component, if any. Used by MCP devtools to
    /// resolve event handlers on the root for the <c>fire</c> escape-hatch tool.
    /// </summary>
    internal Component? RootComponent => _rootComponent;

    internal UIElement? CurrentControl => _currentControl;

    /// <summary>
    /// Optional: when set, Reactor renders into this Border instead of Window.Content.
    /// Useful for embedding Reactor content in a pre-existing layout (e.g., a test harness
    /// with a persistent TitleBar).
    /// </summary>
    public WinUI.Border? ContentTarget { get; set; }

    private RenderErrorHandler? _renderErrorHandler;

    /// <summary>
    /// Replaces the built-in render-error fallback for this host only. <c>null</c> (the
    /// default) falls through to <see cref="ReactorApp.DefaultRenderErrorHandler"/> at
    /// error time. A <see cref="ReactorWindow"/> seeds it from
    /// <see cref="WindowSpec.RenderErrorHandler"/>. See <see cref="Core.RenderErrorHandler"/>.
    /// (issue #1291)
    /// </summary>
    public RenderErrorHandler? RenderErrorHandler
    {
        get => Volatile.Read(ref _renderErrorHandler);
        set => Volatile.Write(ref _renderErrorHandler, value);
    }

    /// <summary>
    /// The handler this host would use right now: its own <see cref="RenderErrorHandler"/>,
    /// else <see cref="ReactorApp.DefaultRenderErrorHandler"/>, else <c>null</c> (built-in
    /// fallback).
    /// </summary>
    public RenderErrorHandler? EffectiveRenderErrorHandler => RenderErrorDispatch.Resolve(RenderErrorHandler);

    public ReactorHost(Window window, ILogger? logger = null)
    {
        // Fall back to <see cref="ReactorApp.AppLogger"/> when the caller
        // didn't pass one — apps that want unified host diagnostics set
        // AppLogger once before constructing their first host. Snapshotted
        // at ctor time; later AppLogger writes don't propagate to existing
        // hosts. Apps that don't set either pay zero JIT cost for the
        // Microsoft.Extensions.Logging call paths.
        _logger = logger ?? ReactorApp.AppLogger;
        _reconciler = new Reconciler(_logger);
        // Diagnostics mode needs call sites stamped from the very first render, so it
        // implies source mapping (REACTOR_SOURCEMAP=1 alone remains the narrower opt-in).
        if (global::Microsoft.UI.Reactor.Hosting.ReactorFeatures.DevtoolsSupported
            && Microsoft.UI.Reactor.Core.Diagnostics.ReactorSourcePublisher.IsEnabled)
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled = true;
        _reconciler.RenderErrorHandlerProvider = () => EffectiveRenderErrorHandler;
        _window = window;
        _backdropApplier = new BackdropApplier(window);
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        // Off-thread rerenders marshal via ReactorApp.UIDispatcher (captured
        // in OnLaunched). For embedded ReactorHostControl scenarios where
        // there's no Reactor.Run, fall back to seeding UIDispatcher with this
        // host's queue if it hasn't been set yet so cross-thread setState
        // callers still resolve a target. (spec 036 §4.3)
        if (ReactorApp.UIDispatcher is null)
            ReactorApp.UIDispatcher = _dispatcherQueue;
        ReactorApp.ActiveHostInternal = this;

        // Route QueryCache.EntryChanged notifications through our dispatcher so subscribers
        // observe cache changes on the UI thread even when Set/Invalidate were called from
        // a background thread (fetch continuation). First host on the process wins — all
        // hosts share the same process-wide default cache.
        var dq = _dispatcherQueue;
        var defaultCache = AppContexts.QueryCache.DefaultValue;
        defaultCache.DispatcherPost ??= action =>
        {
            // Issue #660 (#178): pass the Action's Invoke method group directly
            // instead of wrapping it in a fresh `() => action()` closure per post.
            if (!dq.TryEnqueue(action.Invoke))
                action(); // dispatcher shut down — fall back to inline
        };

        // Hook the window's Activated event into the focus-revalidation service.
        // The service itself lives in <see cref="AppContexts.FocusRevalidation"/> and is
        // always live; enrollment is a no-op when nothing has opted in. Only fire the
        // sweep when the feature flag is on — apps that don't want window-focus
        // revalidation pay zero cost.
        var focusService = AppContexts.FocusRevalidation.DefaultValue;
        if (focusService is not null)
        {
            try
            {
                _window.Activated += (_, args) =>
                {
                    if (!ReactorFeatureFlags.FocusRevalidation) return;
                    if (args.WindowActivationState != WindowActivationState.Deactivated)
                        focusService.RevalidateNow();
                };
            }
            catch { /* windowless / headless host — no activation hook */ }
        }

        // ── Accessibility: forced-colors / reduced-motion ──
        // Deferred to EnsureChartingActive(), called the first time a chart
        // element is constructed (Charting.ChartingActivation.RequestActivation).
        // Apps without charts pay zero cost here.

        // Stop the render loop when the window closes — background threads
        // may still call setState after this, but RequestRender will bail out.
        // Mark the window's native surface gone BEFORE disposing so neither this
        // host's Reset nor any host later created on a reused Window writes
        // SystemBackdrop on it — that AVs (0xC0000005) and corrupts the WinUI
        // backdrop interop for the rest of the process. This handler is wired in
        // the host ctor, before ReactorWindow.OnNativeClosed, so the guard is set
        // ahead of any teardown that reaches a SystemBackdrop write. (issue #647)
        _closedHandler = (_, _) =>
        {
            _windowClosed = true;
            BackdropApplier.MarkWindowClosed(_window);
            Dispose();
        };
        _window.Closed += _closedHandler;
    }

    /// <summary>Ensure the overlay wrapper exists whenever any dev overlay flag is on.</summary>
    private bool AnyOverlayFlagOn => ReactorFeatureFlags.HighlightReconcileChanges;

    /// <summary>
    /// Called by chart elements (via <see cref="ChartingActivation.RequestActivation"/>)
    /// the first time a chart appears in the tree. Lazily allocates the WinRT
    /// accessibility settings, reads their initial values, subscribes for change
    /// notifications, and pushes the values into <c>Charting.D3Charts</c>'s
    /// thread-statics so the about-to-mount chart sees correct forced-colors /
    /// reduced-motion state.
    /// <para>
    /// Idempotent and thread-safe. The 0→1 transition is gated by an
    /// <see cref="Interlocked.CompareExchange(ref int, int, int)"/> so concurrent
    /// callers can't double-subscribe the change handlers. The init body runs
    /// on the dispatcher thread regardless of the caller's thread —
    /// <c>AccessibilitySettings</c> / <c>UISettings</c> are WinRT projections
    /// that prefer the UI thread, and <c>D3Charts</c>'s <c>[ThreadStatic]</c>
    /// flags must be written on the thread that will read them (the UI thread,
    /// where reconciliation runs).
    /// </para>
    /// </summary>
    internal void EnsureChartingActive()
    {
        if (Interlocked.CompareExchange(ref _chartingActiveFlag, 1, 0) != 0)
            return;

        if (_dispatcherQueue.HasThreadAccess)
            InitChartingState();
        else
            _dispatcherQueue.TryEnqueue(InitChartingState);
    }

    private void InitChartingState()
    {
        try
        {
            _accessibilitySettings = new global::Windows.UI.ViewManagement.AccessibilitySettings();
            _isForcedColors = _accessibilitySettings.HighContrast;
            if (_isForcedColors)
                _forcedColorsTheme = s_chartingBridge?.CaptureForcedColorsTheme();
        }
        catch { /* headless / unit-test host — no accessibility settings */ }

        try
        {
            _uiSettings = new global::Windows.UI.ViewManagement.UISettings();
            _isReducedMotion = !_uiSettings.AnimationsEnabled;
            // ColorValuesChanged covers high-contrast toggles and palette changes. It does
            // NOT fire for AnimationsEnabled — that needs AnimationsEnabledChanged, which
            // requires Windows 10 2004 (19041), above this assembly's minimum.
            _uiSettings.ColorValuesChanged += OnColorValuesChanged;
            if (UiSettingsCapabilities.HasAnimationsEnabledChanged)
                _uiSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
        }
        catch { /* headless / unit-test host — no UI settings */ }

        PushChartingState();
    }

    // Registered by the Charting subsystem (issue #498) the first time a chart
    // element activates. Null in chart-free apps, so the trimmer never roots
    // the concrete Charting bridge implementation or its D3Color/ForcedColorsTheme
    // /D3Charts dependency chain.
    private static IChartingHostBridge? s_chartingBridge;

    /// <summary>
    /// Internal registration hook for the Charting subsystem to install its
    /// host bridge. Idempotent at the call site (Charting registers once on
    /// first chart activation). See <see cref="IChartingHostBridge"/>.
    /// </summary>
    internal static void RegisterChartingBridge(IChartingHostBridge bridge)
        => s_chartingBridge = bridge;

    // Kept in a separate method so the JIT doesn't resolve the charting bridge
    // when Render() is compiled — the bridge only loads (and runs the D3Charts
    // static cctor cascade) the first time PushChartingState is actually
    // invoked, which only happens for apps that use charts.
    private void PushChartingState()
        => s_chartingBridge?.PushAccessibilityState(_isForcedColors, _isReducedMotion, _forcedColorsTheme);

    /// <summary>Owner / root= name of this host's root component for ReactorDiagnostics.SourceProperty.</summary>
    private string DiagnosticRootName()
        => _rootComponent is not null
            ? Microsoft.UI.Reactor.Core.Diagnostics.ReactorSourceFormat.ComponentName(_rootComponent.GetType())
            : nameof(FuncElement);

    /// <summary>
    /// The root's Render() threw: report the render (as a throwing child component's is)
    /// and map its id to the error panel that now stands in for the root's content.
    /// </summary>
    private void ShowRootRenderError(Exception ex, bool hotReloadRender, string? componentName, double elapsedMilliseconds)
    {
        TraceRootRendered(hotReloadRender, elapsedMilliseconds);
        ShowErrorFallback(ex, RenderErrorSource.RootRender, componentName);
    }

    /// <summary>ComponentRendered for the root; must run before Reconcile consumes ForceFullRenderPending.</summary>
    private void TraceRootRendered(bool hotReloadRender, double elapsedMilliseconds)
        => _rootDiagnostics.TraceRendered(
            _rootComponent?.GetType().Name ?? nameof(FuncElement),
            hotReloadRender, _reconciler.ForceFullRenderPending, elapsedMilliseconds);

    // Set by RetireRoot while the previous root's content is still shown; cleared once the
    // replacement renders content, or when it renders nothing and the old tree is released.
    private bool _releaseReplacedTreeOnNullRender;

    /// <summary>
    /// A replacement root's render returned null: nothing reconciles the previous root's tree
    /// away, so release it here (its components unmount and their cleanups run) and show no
    /// content. A root that merely re-renders to null keeps its content, as before.
    /// </summary>
    private void ReleaseReplacedTree()
    {
        _releaseReplacedTreeOnNullRender = false;
        if (_currentTree is not null)
            _reconciler.Reconcile(_currentTree, null, _currentControl, _rerenderAction ??= () => RequestRender());
        // Installs "no content" through the same path as a render-error outcome that shows
        // nothing: clears the content, moves the theme listener and window hooks off it.
        SetErrorContent(null, null, replacesTree: true);
        _rootDiagnostics.TrackContent(null);
    }

    /// <summary>
    /// Retires the current root before another is mounted: its effects' cleanups run (as on
    /// Dispose), whichever kind it was, and both root slots are cleared so the render loop
    /// (which checks the component root first) only sees the new one.
    ///
    /// <para>Cleanup failures are routed like disposal's (issue #1291): with a
    /// <c>RenderErrorHandler</c> each is reported and the rest still run; with none, the
    /// first escapes. Either way retirement completes first (a failed cleanup is not left
    /// armed, and the hook state and root slots are cleared), and the failure to rethrow
    /// is returned so the caller can install the new root before throwing it.</para>
    /// </summary>
    private global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? RetireRoot()
    {
        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        Func<RenderErrorHandler?> cleanupHandler = () => EffectiveRenderErrorHandler;
        try
        {
            using (RenderErrorDispatch.EnterPropagationScope())
            {
                RenderErrorDispatch.RunCleanups(_rootComponent?.Context, cleanupHandler, _rootComponent?.GetType().Name,
                    isHostLevel: true, _logger, ref failure);
                RenderErrorDispatch.RunCleanups(_funcContext, cleanupHandler, componentName: null,
                    isHostLevel: true, _logger, ref failure);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // No handler: the failing cleanup escaped (it is already disarmed).
            failure ??= global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
        }
        // Drop (not just clean up) the component's hooks: the caller owns the instance and
        // may mount it again later, which must then be a fresh mount.
        _rootComponent?.Context.ClearHookState();
        _rootComponent = null;
        _rootRenderFunc = null;
        _funcContext = null;
        _rootDiagnostics.Reset();
        // The old root's content stays on screen until the replacement renders; if that
        // render produces nothing, the old tree must still be released (see Render).
        _releaseReplacedTreeOnNullRender = _currentTree is not null || _currentControl is not null;
        return failure;
    }

    public void Mount(Component component)
    {
        // Re-mounting the active instance keeps it (and its effects) alive: retiring it
        // would run its cleanups and then reuse the same context, whose unchanged effects
        // would never be scheduled again.
        if (ReferenceEquals(component, _rootComponent))
        {
            RequestRender();
            return;
        }
        var retireFailure = RetireRoot();
        _rootComponent = component;
        RequestRender();
        retireFailure?.Throw();
    }

    public void Mount(Func<RenderContext, Element> renderFunc)
    {
        var retireFailure = RetireRoot();
        _rootRenderFunc = renderFunc;
        _funcContext = new RenderContext();
        RequestRender();
        retireFailure?.Throw();
    }

    /// <summary>
    /// Thread-safe: can be called from any thread. Coalesces multiple calls into
    /// a single render. At most one RenderLoop is ever pending on the dispatcher.
    ///
    /// During render: setState calls set _needsRerender (no enqueue).
    /// Between renders: first setState CAS-flips _renderPending 0→1 and enqueues.
    /// _renderPending stays 1 throughout the render, blocking duplicate enqueues.
    ///
    /// Pass <paramref name="force"/>=true to bypass component memoization
    /// (Props/deps equality, ShouldUpdate) for the next pass — used by hot
    /// reload where the updated Render() body lives on the type, not in props.
    /// </summary>
    internal void RequestRender(bool force = false)
    {
        if (_disposed) return;

        if (force)
            _reconciler.ForceFullRenderPending = true;

        // Capture ambient animation curve so the async render pass can restore it.
        // Multiple state changes may fire before the render — last curve wins.
        if (AnimationScope.HasScope)
            _pendingAnimationCurve = AnimationScope.Current;

        // Same snapshot pattern for the Animations.Animate ambient. AsyncLocal
        // flows through DispatcherQueue.TryEnqueue on WinUI 1.5+, but a
        // setter that fires from a Task.Run that never awaits back would
        // otherwise lose the ambient. This snapshot is the explicit
        // insurance against that case (spec 042 §9 Q3).
        var captured = Microsoft.UI.Reactor.Core.Internal.AnimationAmbient.HasAny
            ? Microsoft.UI.Reactor.Core.Internal.AnimationAmbient.Current
            : null;
        if (captured is not null)
            _pendingAmbientAnimation = captured;

        // During render: just flag — the render loop will re-enqueue after Render().
        if (_isRendering)
        {
            _needsRerender = true;
            return;
        }

        // Between renders: CAS 0→1 gates a single TryEnqueue.
        if (Interlocked.CompareExchange(ref _renderPending, 1, 0) != 0)
        {
            _needsRerender = true;
            return;
        }

        // First render synchronous-on-UI-thread: the very first render
        // fires from Mount() inside OnLaunched, which is already on the
        // dispatcher. Skipping the queue saves one tick (~2-5 ms on cold
        // start) and lets content attach before window.Activate() returns,
        // matching what an imperative WinUI3 app does. Only the very first
        // render takes this path — subsequent setStates keep the existing
        // batch-via-dispatcher behavior so multiple synchronous setState
        // calls still coalesce into one render.
        if (_currentControl is null && _dispatcherQueue.HasThreadAccess)
        {
            RenderLoop();
            return;
        }

        // Demote to Low priority when the previous render exceeded the frame
        // budget — high-frequency setState sources (animation, simulation,
        // streaming data) otherwise pack the dispatcher with back-to-back
        // Normal-priority renders and starve input/layout/paint. See
        // RenderPriorityPolicy. Volatile.Read pairs with Interlocked.Exchange
        // in Render() so an off-UI-thread caller observes the latest value.
        _dispatcherQueue.TryEnqueue(
            RenderPriorityPolicy.PickPriority(Volatile.Read(ref _lastRenderMs)),
            _renderLoopHandler ??= RenderLoop);
    }

    private void RenderLoop()
    {
        if (_disposed) return;

        // _renderPending is 1 here — all concurrent RequestRender calls are
        // blocked from enqueuing duplicates. Render once, then decide.
        _needsRerender = false;
        // Outermost frame for a propagated render error (issue #1291). Scoped, so a first
        // render started synchronously from inside another frame (e.g. app cleanup code
        // mounting a new host) restores that frame's marker instead of clearing it.
        using (RenderErrorDispatch.EnterPropagationScope())
        {
            try
            {
                Render();
            }
            finally
            {
                // Reset the gate so future setState calls can enqueue — also when a render
                // error the app chose to propagate (RenderError.Propagate) escapes Render().
                Interlocked.Exchange(ref _renderPending, 0);
            }
        }

        // If state changed during render, re-enqueue at LOW priority so WinUI
        // layout/paint/input (normal priority + WM_PAINT) run first. Without this,
        // high-frequency setState sources cause back-to-back renders that starve the
        // compositor — layout never runs, property sets on dirty elements get
        // progressively slower, and reconcile time blows up non-linearly.
        if (_needsRerender)
        {
            if (Interlocked.CompareExchange(ref _renderPending, 1, 0) == 0)
                _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, _renderLoopHandler ??= RenderLoop);
        }
    }

    /// <summary>
    /// Hot Reload state migration entry point (spec 049 §6). Runs once at the
    /// start of a hot-reload render pass, before any component re-renders. Reads
    /// the set of edited types from <see cref="HotReloadService.UpdatedTypes"/>
    /// and asks every live <see cref="RenderContext"/> to value-swap matching
    /// hook cells. The root component/function context is not registered with
    /// the reconciler, so it is migrated explicitly here; child contexts are
    /// reached via <see cref="Reconciler.ForEachLiveContext"/>. Never throws out
    /// — a migration failure must not abort the reload render.
    /// </summary>
    private void MigrateHotReloadState()
    {
        // Route through IsHotReloadLive (MetadataUpdater.IsSupported) so the
        // reflection-bearing migration call graph is statically dead and trims
        // away under NativeAOT (spec 049 §8).
        if (!HotReloadService.IsHotReloadLive) return;

        var updatedTypes = HotReloadService.UpdatedTypes;
        if (updatedTypes is null || updatedTypes.Count == 0) return;

        try
        {
            _rootComponent?.Context.MigrateHooksForHotReload(updatedTypes);
            _funcContext?.MigrateHooksForHotReload(updatedTypes);
            _reconciler.ForEachLiveContext(ctx => ctx.MigrateHooksForHotReload(updatedTypes));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Hot reload: state migration pass failed; continuing with re-render");
        }
    }

    private void Render()
    {
        _isRendering = true;
        // Atomic capture-and-clear gives us at-most-once recovery per
        // UpdateApplication call — see the matching block in
        // ReactorHostControl.Render() for the full rationale.
        bool hotReloadRender = HotReloadService.ConsumeUpdatePending();

        // Open a tree-wide hot-reload pass for the duration of this render so
        // the reconciler can recover hook-order changes in non-root children
        // (Reconciler.UpdateComponent reads HotReloadService.WithinUpdatePass).
        // The using disposes on every exit path, clearing the flag.
        using IDisposable? hotReloadPass = hotReloadRender
            ? HotReloadService.BeginUpdatePass()
            : null;

        // Hot Reload state migration (spec 049 §6). Before any component
        // re-renders, walk every live RenderContext (root + reconciler-tracked
        // children) and value-swap hook cells whose stored type was edited, so
        // adding/removing a field on a record used in UseState/UseReducer/etc.
        // preserves surviving values instead of resetting to the initializer.
        // Gated on the pass being live + the runtime reporting updated types;
        // a plain force-render (UpdatedTypes == null) is a no-op.
        if (hotReloadRender)
            MigrateHotReloadState();

        // Multi-window: hooks (UseWindow, UseDpi, UseWindowState, UseIsActive,
        // UseClosingGuard, parameterless UseWindowSize) resolve "the rendering
        // host" via ReactorApp.ActiveHostInternal. Without a per-render push,
        // the ctor-time assignment of the most-recently-constructed host wins
        // permanently and a second window's components observe the wrong
        // owning window. Restore the previous value in the finally so
        // re-entrant renders unwind correctly. (spec 036 §3.4 / §7.1)
        var prevActiveHost = ReactorApp.ActiveHostInternal;
        ReactorApp.ActiveHostInternal = this;

        void RecoverFromHookOrder(HookOrderException ex, RenderContext ctx, string mode)
        {
            double renderMs = _phaseSw.Elapsed.TotalMilliseconds;
            // This path returns without reconciling, so nothing downstream will consume
            // or retire what the aborted render claimed (issue #1262).
            Controls.Validation.ValidationRenderScope.AbandonPendingClaims();
            // The aborted attempt still ran Render(); report it (the retry reports itself).
            TraceRootRendered(hotReloadRender: true, renderMs);
            _logger?.LogWarning(ex,
                "Hot reload: hook order/type changed — resetting {Mode} state and re-rendering",
                mode);
            ctx.ResetForHotReload();
            // The retry runs outside a hot-reload pass; keep ComponentRendered attributing
            // it to hot reload: the root here, and (since this host's hot-reload pass is a
            // forced one whose force flag is still pending) its children via the reconciler.
            _rootDiagnostics.MarkHotReloadRetry();
            _reconciler.ForceFullRenderIsHotReloadRetry = true;
            RequestRender();
        }

        // Which phase the outer catch attributes a failure to (issue #1291).
        var failurePhase = RenderErrorSource.Reconcile;
        try
        {
            Element? newTree = null;

            _phaseSw.Restart();

            // Propagate accessibility state to D3Charts thread-statics so all
            // chart rendering picks up forced-colors / reduced-motion. Skipped
            // entirely (no D3Charts type touch, no cctor cascade) when no
            // chart has ever been mounted in this host. PushChartingState is
            // a separate method so the JIT doesn't load Charting.D3Charts
            // when Render() is compiled.
            // Volatile read so a chart-element create on a background thread
            // that flipped _chartingActiveFlag is observed by this UI-thread
            // render. Plain reads can hoist past the Interlocked write under
            // sufficiently aggressive JITs.
            if (Volatile.Read(ref _chartingActiveFlag) != 0) PushChartingState();

            // Issue #660 (#179): RequestRender has an optional `force` parameter,
            // so it can't bind to an Action method group — cache the wrapper once
            // instead of allocating `() => RequestRender()` every render.
            Action rerender = _rerenderAction ??= () => RequestRender();

            if (_rootComponent is not null)
            {
                _rootComponent.Context.BeginRender(rerender);
                try
                {
                    using (ValidationRenderScope.Begin(null))
                    {
                        newTree = ValidationRenderScope.ApplyProvide(_rootComponent.Render());
                    }
                }
                catch (HookOrderException ex) when (hotReloadRender)
                {
                    RecoverFromHookOrder(ex, _rootComponent.Context, "component");
                    return;
                }
                catch (Exception ex) when (!RenderErrorDispatch.IsPropagating(ex))
                {
                    // Sampled before any debugger break or logging: this is the render's time.
                    double renderMs = _phaseSw.Elapsed.TotalMilliseconds;
                    Debugger.BreakForUserUnhandledException(ex);
                    _logger?.LogError(ex, "Component Render() threw");
                    ShowRootRenderError(ex, hotReloadRender, _rootComponent.GetType().Name, renderMs);
                    return;
                }
                // A propagation the app requested (RenderError.Propagate()) leaves without a
                // fallback; the root's Render() still ran, so report it on the way out.
                catch (Exception) when (Microsoft.UI.Reactor.Core.Diagnostics.ComponentRenderTrace.IsEnabled)
                {
                    TraceRootRendered(hotReloadRender, _phaseSw.Elapsed.TotalMilliseconds);
                    throw;
                }
            }
            else if (_rootRenderFunc is not null && _funcContext is not null)
            {
                _funcContext.BeginRender(rerender);
                try
                {
                    using (ValidationRenderScope.Begin(null))
                    {
                        newTree = ValidationRenderScope.ApplyProvide(_rootRenderFunc(_funcContext));
                    }
                }
                catch (HookOrderException ex) when (hotReloadRender)
                {
                    RecoverFromHookOrder(ex, _funcContext, "function-component");
                    return;
                }
                catch (Exception ex) when (!RenderErrorDispatch.IsPropagating(ex))
                {
                    // Sampled before logging: this is the render's time.
                    double renderMs = _phaseSw.Elapsed.TotalMilliseconds;
                    _logger?.LogError(ex, "Function component threw");
                    ShowRootRenderError(ex, hotReloadRender, componentName: null, renderMs);
                    return;
                }
                // See the component branch: report an app-requested propagation on the way out.
                catch (Exception) when (Microsoft.UI.Reactor.Core.Diagnostics.ComponentRenderTrace.IsEnabled)
                {
                    TraceRootRendered(hotReloadRender, _phaseSw.Elapsed.TotalMilliseconds);
                    throw;
                }
            }

            double treeBuildMs = _phaseSw.Elapsed.TotalMilliseconds;

            if (newTree is null)
            {
                // A root whose Render() returned null still rendered; there is just no
                // content to reconcile. (No root at all is not a render.)
                if (_rootComponent is not null || _rootRenderFunc is not null)
                    TraceRootRendered(hotReloadRender, treeBuildMs);
                if (_releaseReplacedTreeOnNullRender)
                    ReleaseReplacedTree();
                return;
            }
            TraceRootRendered(hotReloadRender, treeBuildMs);

            _phaseSw.Restart();

            // Restore captured animation scope so ApplyModifiers routes through
            // compositor animations instead of direct property sets.
            var capturedCurve = Interlocked.Exchange(ref _pendingAnimationCurve, null);
            if (capturedCurve is not null)
                AnimationScope.PushScope(capturedCurve);

            // Same restore for the Animations.Animate ambient (spec 042 §6).
            // The scope re-pushes the captured snapshot onto the AsyncLocal
            // so reconcile-time consumers (KeyedListDiff, ChildReconciler)
            // see the same intent the originating setter saw.
            var capturedAmbient = Interlocked.Exchange(ref _pendingAmbientAnimation, null);
            using var ambientRestore = capturedAmbient is not null
                ? new Microsoft.UI.Reactor.Core.Internal.AnimationAmbient.Scope(capturedAmbient)
                : default;

            // Guarded inline (not through a local) so ILC's scanner folds the switch and drops
            // the publishing path entirely from a build without Reactor.DevtoolsSupport.
            if (global::Microsoft.UI.Reactor.Hosting.ReactorFeatures.DevtoolsSupported
                && Microsoft.UI.Reactor.Core.Diagnostics.ReactorSourcePublisher.IsEnabled)
                _reconciler.DiagnosticRootOwner = DiagnosticRootName();
            UIElement? newControl;
            try
            {
                newControl = _reconciler.Reconcile(
                    _currentTree,
                    newTree,
                    _currentControl,
                    rerender
                );
            }
            finally
            {
                // The root owner names what this pass renders; a row an ItemsRepeater realizes
                // or reuses later, during layout, has no known owner (see CurrentDiagnosticOwner).
                _reconciler.DiagnosticRootOwner = null;
                if (capturedCurve is not null)
                    AnimationScope.PopScope();
            }

            // Single unified install path: any dev overlay flag → install the
            // shared wrapper (once). Sub-overlays paint into the shared Canvas
            // via OverlayHostWiring's root ContainerVisual.
            bool anyOverlayOn = AnyOverlayFlagOn;

            if (anyOverlayOn)
                _overlayWiring ??= new OverlayHostWiring(_dispatcherQueue);

            // Per-feature teardown: if a flag flipped off while another is
            // still on, dispose just that sub-overlay. The shared wrapper
            // stays put for the remaining overlay.
            _overlayWiring?.ApplyFlagState();

            if (newControl != _currentControl)
            {
                UIElement? contentToSet = newControl;
                if (anyOverlayOn)
                    contentToSet = _overlayWiring!.SetContentViaWrapper(newControl);
                if (ContentTarget is not null)
                    ContentTarget.Child = contentToSet;
                else
                    _window.Content = contentToSet;
                AttachThemeListener(newControl);
            }
            else if (anyOverlayOn && _overlayWiring!.WrapperRoot is null)
            {
                // Flag flipped on mid-session. Detach the current content
                // before re-parenting into the wrapper slot — WinUI throws
                // "Element already has a logical parent" if we skip this.
                if (ContentTarget is not null)
                    ContentTarget.Child = null;
                else
                    _window.Content = null;
                var wrapper = _overlayWiring.SetContentViaWrapper(newControl);
                if (ContentTarget is not null)
                    ContentTarget.Child = wrapper;
                else
                    _window.Content = wrapper;
                Debug.WriteLine($"[Reactor.Overlay] wrapper installed mid-session; content={newControl?.GetType().Name ?? "null"}");
            }
            else if (!anyOverlayOn && _overlayWiring?.WrapperRoot is not null)
            {
                // All overlay flags off — tear down the wrapper and reinstate
                // the raw control. Explicitly detach the content from the
                // wrapper's slot first, otherwise WinUI throws "Element
                // already has a logical parent" when we re-attach it to the
                // window.
                _overlayWiring.DetachContent();
                if (ContentTarget is not null)
                    ContentTarget.Child = newControl;
                else
                    _window.Content = newControl;
                _overlayWiring.Dispose();
                _overlayWiring = null;
            }

            _currentControl = newControl;
            _currentTree = newTree;
            _releaseReplacedTreeOnNullRender = false;
            if (global::Microsoft.UI.Reactor.Hosting.ReactorFeatures.DevtoolsSupported
                && Microsoft.UI.Reactor.Core.Diagnostics.ReactorSourcePublisher.IsEnabled)
                _reconciler.PublishRootSource(
                    newControl, newTree, DiagnosticRootName(),
                    _rootComponent is not null
                        ? Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetComponentHooks(_rootComponent.GetType())
                        : null);
            _rootDiagnostics.TrackContent(newControl);
            OwningWindow?.OnHostContentRendered(newControl);

            // Spec 033 §6 — apply (or clear) the SystemBackdrop modifier carried on
            // the root tree's modifiers. A no-op when the modifier hasn't changed
            // since the last apply.
            _backdropApplier.Apply(newTree?.Modifiers?.Backdrop);

            // Start any connected animations now that the new tree is in the visual tree
            _reconciler.FlushConnectedAnimations();

            // Schedule overlay flushes after layout so elements have final
            // bounds. The flush is a no-op when the highlight flag is off.
            _overlayWiring?.ScheduleHighlightFlush(_reconciler);

            double reconcileMs = _phaseSw.Elapsed.TotalMilliseconds;

            _phaseSw.Restart();

            failurePhase = RenderErrorSource.Effects;
            if (_rootComponent is not null)
                _rootComponent.Context.FlushEffects();
            else if (_funcContext is not null)
                _funcContext.FlushEffects();
            failurePhase = RenderErrorSource.Reconcile;

            double effectsMs = _phaseSw.Elapsed.TotalMilliseconds;

            // Feed RenderPriorityPolicy so the next RequestRender knows whether
            // to demote to Low priority. Stored as the most-recent measurement
            // — no smoothing — so a single slow render is enough to back off,
            // and a single fast render is enough to return to Normal priority.
            // Issue #660 (#185): single writer (this UI-thread Render); the
            // off-thread RequestRender readers use Volatile.Read, so a release
            // Volatile.Write suffices — no need for a full-barrier Interlocked.
            Volatile.Write(ref _lastRenderMs, treeBuildMs + reconcileMs + effectsMs);

            OnRenderComplete?.Invoke(treeBuildMs, reconcileMs, effectsMs);

#if DEBUG
            _logger?.LogDebug(
                "RECONCILE: tree={TreeBuildMs:F2}ms  reconcile={ReconcileMs:F2}ms  effects={EffectsMs:F2}ms  total={TotalMs:F2}ms  |  diffed={Diffed}  skipped={Skipped}  created={Created}  modified={Modified}",
                treeBuildMs, reconcileMs, effectsMs, treeBuildMs + reconcileMs + effectsMs,
                _reconciler.DebugElementsDiffed, _reconciler.DebugElementsSkipped,
                _reconciler.DebugUIElementsCreated, _reconciler.DebugUIElementsModified);
#endif

            // Accumulate and report every ~1 second
            _treeBuildSum += treeBuildMs;
            _reconcileSum += reconcileMs;
            _effectsSum += effectsMs;
            _renderCount++;
            _totalRenderCount++;

            // Issue #660 (#186): the per-frame report gate compared
            // Stopwatch.Elapsed.TotalSeconds — a TimeSpan construction + double
            // division every render. ElapsedMilliseconds is a plain long read;
            // compare against 1000 instead. The (rare) report branch still uses
            // the precise TotalSeconds for the FPS figure.
            if (_reportClock.ElapsedMilliseconds >= 1000 && _renderCount > 0)
            {
                double avgTree = _treeBuildSum / _renderCount;
                double avgReconcile = _reconcileSum / _renderCount;
                double avgEffects = _effectsSum / _renderCount;
                double avgTotal = avgTree + avgReconcile + avgEffects;

                _stats = new RenderStats
                {
                    Fps = _renderCount / _reportClock.Elapsed.TotalSeconds,
                    RendersInWindow = _renderCount,
                    TotalRenders = _totalRenderCount,
                    AvgTreeBuildMs = avgTree,
                    AvgReconcileMs = avgReconcile,
                    AvgEffectsMs = avgEffects,
                    AvgTotalMs = avgTotal,
                    LastDiffed = _reconciler.DebugElementsDiffed,
                    LastSkipped = _reconciler.DebugElementsSkipped,
                    LastCreated = _reconciler.DebugUIElementsCreated,
                    LastModified = _reconciler.DebugUIElementsModified,
                };

                _logger?.LogDebug(
                    "PERF [{RenderCount} renders]: tree={TreeMs:F2}ms  reconcile={ReconcileMs:F2}ms  effects={EffectsMs:F2}ms  total={TotalMs:F2}ms",
                    _renderCount, avgTree, avgReconcile, avgEffects, avgTotal);
                _treeBuildSum = 0;
                _reconcileSum = 0;
                _effectsSum = 0;
                _renderCount = 0;
                _reportClock.Restart();
            }
        }
        catch (Exception ex) when (!RenderErrorDispatch.IsPropagating(ex))
        {
            _logger?.LogError(ex, "Render FAILED");
            // A root effect failure belongs to the root component; a commit-phase one has no
            // single owning component.
            ShowErrorFallback(ex, failurePhase,
                failurePhase == RenderErrorSource.Effects ? _rootComponent?.GetType().Name : null);
        }
        finally
        {
            _isRendering = false;
            ReactorApp.ActiveHostInternal = prevActiveHost;
        }
    }

    /// <summary>
    /// Subscribes to ActualThemeChanged on the root content element so that
    /// ThemeRef-bound properties are re-resolved when the theme switches.
    /// WinUI controls handle theme changes natively via {ThemeResource} bindings,
    /// but Reactor's ThemeRef values are resolved once during reconciliation —
    /// this listener triggers a re-render so they pick up the new theme.
    /// </summary>
    private void AttachThemeListener(UIElement? control)
    {
        if (_themeListenerElement is not null)
            _themeListenerElement.ActualThemeChanged -= OnActualThemeChanged;

        if (control is not FrameworkElement fe)
        {
            _themeListenerElement = null;
            return;
        }

        _themeListenerElement = fe;
        fe.ActualThemeChanged += OnActualThemeChanged;
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        _logger?.LogDebug("Theme changed to {Theme} — re-rendering", sender.ActualTheme);
        // Issue #660 (#86): drop the (key,theme)->Brush cache so ThemeRef
        // resolves re-read the now-current ThemeDictionaries on the next render.
        Microsoft.UI.Reactor.Core.ThemeRef.InvalidateResolutionCache();
        // Intentionally NOT calling Reconciler.ClearStyleCache() here:
        //   - Cached Styles are content-addressed (BuildCacheKey over the
        //     bindings dict) and use {ThemeResource} markup that WinUI
        //     re-resolves live on theme change — no Style regeneration
        //     needed for correctness.
        //   - Issue #522: ClearThemeBindings relies on stable Style identity
        //     to match fe.Style against the cache via ReferenceEquals. Clearing
        //     the cache here would create a window where a coincident
        //     ThemeBindings removal in the same render frame would silently
        //     fail to drop the prior themed Style.
        RequestRender();
    }

    private void OnColorValuesChanged(
        global::Windows.UI.ViewManagement.UISettings sender, object args)
    {
        // ColorValuesChanged does NOT fire for AnimationsEnabled — that arrives on
        // OnAnimationsEnabledChanged. The re-read stays because it is the only path
        // available before Windows 10 2004 (19041), where that event does not exist:
        // there, an animation flip is picked up at the next palette or theme change.
        _isReducedMotion = !sender.AnimationsEnabled;
        // High-contrast palette may also change — re-read to be safe.
        if (_accessibilitySettings is { } a11y)
        {
            _isForcedColors = a11y.HighContrast;
            _forcedColorsTheme = _isForcedColors ? s_chartingBridge?.CaptureForcedColorsTheme() : null;
        }
        // Issue #660 (#86): a palette / high-contrast change can alter the brush
        // a (key,theme) pair resolves to — drop the cache so it re-resolves.
        Microsoft.UI.Reactor.Core.ThemeRef.InvalidateResolutionCache();
        RequestRender();
    }

    private void OnAnimationsEnabledChanged(
        global::Windows.UI.ViewManagement.UISettings sender, object args)
    {
        var value = !sender.AnimationsEnabled;
        // A coincident palette change re-reads the same field through
        // OnColorValuesChanged, so bail out when nothing actually moved.
        if (value == _isReducedMotion) return;
        _isReducedMotion = value;
        // No PushChartingState here, matching OnColorValuesChanged: D3Charts' flags are
        // [ThreadStatic] and this runs on the WinRT notification thread, so a push here
        // would write a copy the render thread never reads. Render() pushes on the UI
        // thread every frame, so RequestRender is what actually propagates this.
        RequestRender();
    }

    /// <summary>
    /// True when the render loop has no pending or in-flight render and no
    /// re-render queued for the next tick. Mirrors the early-out predicate
    /// inside <see cref="WaitForIdleAsync"/>; exposed so test harnesses can
    /// drive bounded-convergence wait loops without polling the same fields
    /// reflectively. The renderPending read uses Volatile.Read so off-thread
    /// callers see a stable snapshot of the Interlocked-managed field.
    /// A disposed host reports idle (nothing left to render, ever) to match
    /// the disposed-arm short-circuit in <see cref="WaitForIdleAsync"/>;
    /// otherwise convergence loops would spin to their pass cap during/after
    /// host teardown and emit misleading non-idle diagnostics.
    /// </summary>
    public bool IsIdle =>
        _disposed ||
        (Volatile.Read(ref _renderPending) == 0 &&
         !_isRendering &&
         !_needsRerender);

    /// <summary>
    /// Awaits until the render loop is idle (no pending or in-flight renders).
    /// Yields to the dispatcher at Low priority in a loop so that Normal-priority
    /// RenderLoop callbacks and Low-priority re-renders all complete before returning.
    /// Used by test harnesses to replace blind Task.Delay waits.
    /// </summary>
    public Task WaitForIdleAsync(int maxYields = 50)
    {
        if (_disposed) return Task.CompletedTask;
        if (_renderPending == 0 && !_isRendering && !_needsRerender)
            return Task.CompletedTask;

        // RunContinuationsAsynchronously: TrySetResult is called from a
        // dispatcher callback, and without this flag any await continuation
        // would run inline on the dispatcher at Low priority — re-entering
        // UI logic inside the yield loop and partially defeating its purpose.
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int yields = 0;
        void CheckIdle()
        {
            if (_disposed)
            {
                tcs.TrySetResult();
                return;
            }
            if (_renderPending == 0 && !_isRendering && !_needsRerender)
            {
                tcs.TrySetResult();
                return;
            }
            if (++yields > maxYields)
            {
                // Returning early here is the classic flake source: callers
                // (e.g. selftest Harness.Render) move on against a half-settled
                // tree. Log so the next flake is greppable instead of silent.
                Debug.WriteLine(
                    $"[Reactor.WaitForIdle] yield cap hit ({maxYields}); " +
                    $"renderPending={_renderPending} isRendering={_isRendering} needsRerender={_needsRerender}");
                tcs.TrySetResult();
                return;
            }
            if (!_dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, CheckIdle))
            {
                // Queue refused enqueue (shutdown). Complete rather than
                // hang the caller forever.
                tcs.TrySetResult();
            }
        }
        if (!_dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, CheckIdle))
        {
            // Same fallback for the initial enqueue.
            tcs.TrySetResult();
        }
        return tcs.Task;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _window.Closed -= _closedHandler;

        // Theme listener touches UI-affine objects — marshal to UI thread if needed.
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_themeListenerElement is not null)
                _themeListenerElement.ActualThemeChanged -= OnActualThemeChanged;
            _themeListenerElement = null;
        });

        // Accessibility listener cleanup
        if (_uiSettings is not null)
        {
            _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
            if (UiSettingsCapabilities.HasAnimationsEnabledChanged)
                _uiSettings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
        }

        // Issue #1291: with a RenderErrorHandler configured, every cleanup runs and each
        // failure is reported (Source = Cleanup); a requested, unhandled propagation is
        // rethrown once disposal has finished. With no handler they escape as before.
        // The root's and the reconciler's cleanups share one propagation scope, so only
        // the first propagated failure is rethrown, and a nested frame started by app
        // cleanup code cannot disturb it.
        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? pendingPropagation = null;
        // Resolved per failure (a cleanup may change the handler), not once per batch.
        Func<RenderErrorHandler?> cleanupHandler = () => EffectiveRenderErrorHandler;
        using (RenderErrorDispatch.EnterPropagationScope())
        {
            RenderErrorDispatch.RunCleanups(_rootComponent?.Context, cleanupHandler, _rootComponent?.GetType().Name,
                isHostLevel: true, _logger, ref pendingPropagation);
            RenderErrorDispatch.RunCleanups(_funcContext, cleanupHandler, componentName: null,
                isHostLevel: true, _logger, ref pendingPropagation);

            // Clear the SystemBackdrop so a window-reuse path returns to the WinUI
            // default. Skip the actual window write when the window has already been
            // closed/destroyed — touching set_SystemBackdrop on a torn-down window
            // AVs (0xC0000005) and corrupts the backdrop interop for later windows.
            // Best effort either way. (issue #647)
            try { _backdropApplier.Reset(_windowClosed); }
            catch (global::System.Exception ex)
                when (ex is not global::System.OutOfMemoryException and not global::System.StackOverflowException)
            {
                Debug.WriteLine($"[Reactor] backdrop reset on dispose failed (best effort): {ex.GetType().Name}: {ex.Message}");
            }

            // Always dispose the reconciler (it runs every child cleanup), even when a root
            // cleanup already holds the propagation; only then keep the first propagation.
            var reconcilerPropagation = _reconciler.DisposeCollectingPropagation();
            pendingPropagation ??= reconcilerPropagation;
        }
        _rootDiagnostics.Reset();
        _rootComponent = null;
        _rootRenderFunc = null;
        _funcContext = null;
        _currentTree = null;
        _currentControl = null;

        try { _overlayWiring?.Dispose(); } catch { /* best effort */ }
        _overlayWiring = null;

        ReactorApp.ActiveHostInternal = null;
        pendingPropagation?.Throw();
    }

    private void ShowErrorFallback(Exception ex, RenderErrorSource source, string? componentName = null)
    {
        // The render that failed never reaches Reconcile, so its validation claims have
        // no consumer. Withdraw them here rather than waiting for a next render that may
        // never come (issue #1262).
        Controls.Validation.ValidationRenderScope.AbandonPendingClaims();

        // Issue #1291 — the app's handler (host override, else the app-wide default) may
        // replace the built-in panel, or ask to propagate.
        var error = new RenderError(ex, source, componentName, isHostLevel: true);
        var rerender = _rerenderAction ??= () => RequestRender();
        // Replacing or releasing the old tree must finish even when one of its cleanups
        // throws: the host forgets that tree afterwards, so anything left registered would
        // stay alive. Every cleanup runs; failures are collected and reported below.
        var teardownErrors = new RenderErrorDispatch.TeardownErrors(_logger);
        var (content, tree, propagate, replacesTree) = RenderErrorDispatch.BuildHostFallback(
            EffectiveRenderErrorHandler, error, _logger,
            install: element =>
            {
                using (_reconciler.IsolateUnmountCleanupFailures(teardownErrors.Add))
                    return _reconciler.Reconcile(_currentTree, element, _currentControl, rerender);
            },
            releaseCurrent: () =>
            {
                if (_currentTree is null) return;
                using (_reconciler.IsolateUnmountCleanupFailures(teardownErrors.Add))
                    _reconciler.Reconcile(_currentTree, null, _currentControl, rerender);
            },
            currentIsAppFallback: RenderErrorDispatch.IsAppFallback(_currentTree));
        SetErrorContent(content, tree, replacesTree);
        // An app-supplied fallback tree stands in for the root's content: name the root on it.
        if (tree is not null
            && global::Microsoft.UI.Reactor.Hosting.ReactorFeatures.DevtoolsSupported
            && Microsoft.UI.Reactor.Core.Diagnostics.ReactorSourcePublisher.IsEnabled)
            _reconciler.PublishFallbackRootSource(
                _currentControl, DiagnosticRootName(),
                _rootComponent is not null
                    ? Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetComponentHooks(_rootComponent.GetType())
                    : null);
        // ComponentRendered bookkeeping. A built-in or neutral panel is a raw control, so no
        // Reactor component is on screen any more; the built-in panel's path also leaves the
        // previous tree unmounted, so its ids must be dropped here. An app fallback was
        // reconciled against the previous tree, which kept the mappings current. Either
        // way the root's id now names whatever stands in for its content.
        if (tree is null)
            _reconciler.ForgetComponentDiagnostics();
        _rootDiagnostics.TrackContent(_currentControl);
        teardownErrors.RethrowPropagated();
        // Nothing is shown where the failure happened. Returns only when the app's
        // unhandled-exception callback handled it; otherwise rethrows.
        if (propagate)
            RenderErrorDispatch.RaiseUnhandled(ex);
    }

    // Installs the error content. A handler-supplied fallback is kept as the current tree
    // so the next successful render reconciles away from it (running its cleanups); the
    // built-in panel is a raw control, so the tree is cleared and the next render mounts fresh.
    private void SetErrorContent(UIElement? errorPanel, Element? errorTree, bool replacesTree)
    {
        // Whatever was shown has been replaced; nothing is left for ReleaseReplacedTree.
        _releaseReplacedTreeOnNullRender = false;
        if (_overlayWiring is not null && _overlayWiring.TryShowErrorInWrapper(errorPanel))
        {
            // shared overlay wrapper took it
        }
        else if (ContentTarget is not null)
        {
            ContentTarget.Child = errorPanel;
        }
        else
        {
            _window.Content = errorPanel;
        }
        _currentControl = errorPanel;
        _currentTree = errorTree;
        // When the handler's outcome replaced (and released) the tree, the theme listener
        // moves to the new content like any content swap, or is detached when there is none,
        // so it neither pins the released root nor misses a ThemeRef-bound app fallback. The
        // owning window likewise moves its background-drag and SizeToContent hooks off the
        // released root. The no-handler built-in panel keeps the pre-#1291 behavior.
        if (replacesTree)
        {
            AttachThemeListener(errorPanel);
            OwningWindow?.OnHostContentRendered(errorPanel);
        }
    }
}
