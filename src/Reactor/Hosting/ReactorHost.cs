using System.Diagnostics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting.Etw;
using Microsoft.UI.Reactor.Hosting.LayoutCost;
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
    private Func<RenderContext, Element>? _rootRenderFunc;
    private RenderContext? _funcContext;

    private Element? _currentTree;
    private UIElement? _currentControl;
    private int _renderPending;    // 0 or 1 — Interlocked for thread-safe access
    private volatile bool _isRendering;     // only touched on UI thread
    private volatile bool _needsRerender;   // only touched on UI thread
    private FrameworkElement? _themeListenerElement;
    private volatile bool _disposed;

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
    private Charting.ForcedColorsTheme? _forcedColorsTheme;
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

    // ── Layout cost overlay data pipeline (gated by ReactorFeatureFlags.ShowLayoutCost) ──
    // Owned by the host so the ETW session lifetime matches the host's lifetime.
    // Constructed lazily on first observed flag-on; never torn down except on Dispose
    // (flag flips post-init require a host restart per the flag's contract).
    private LayoutEtwConsumer? _etwConsumer;
    private EventPairing? _eventPairing;
    private LayoutEventRing? _eventRing;
    private PointerMap? _pointerMap;
    private SpatialIndex? _spatialIndex;
    private LayoutCostAttribution? _attribution;

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

    /// <summary>
    /// Optional: when set, Reactor renders into this Border instead of Window.Content.
    /// Useful for embedding Reactor content in a pre-existing layout (e.g., a test harness
    /// with a persistent TitleBar).
    /// </summary>
    public WinUI.Border? ContentTarget { get; set; }

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
            if (!dq.TryEnqueue(() => action()))
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

        // Register built-in custom element types
        Controls.ResizeGripRegistration.Register(_reconciler);

        // Stop the render loop when the window closes — background threads
        // may still call setState after this, but RequestRender will bail out.
        _closedHandler = (_, _) => Dispose();
        _window.Closed += _closedHandler;

        // Start the ETW leg of the pipeline eagerly when the flag is on at
        // host construction — ETW session creation is privileged and has
        // long-lived OS state, so we don't retry it mid-session. The overlay
        // wiring (wrapper + compositor visuals) is built lazily via
        // EnsureLayoutCostPipeline so flipping the menu toggle shows a
        // sanity-wash overlay even without a running ETW session.
        if (ReactorFeatureFlags.ShowLayoutCost)
            StartEtwPipeline();
    }

    /// <summary>Build attribution + subscribe the reconciler + attach it to the overlay wiring. Idempotent.</summary>
    private void EnsureLayoutCostPipeline()
    {
        // Try to start the ETW pipeline live — gives us layoutMs data without
        // a host restart when the user flips the flag. If the user doesn't have
        // Performance Log Users / admin rights, the consumer flips IsUnavailable
        // and the overlay still works (just without the top ms bar).
        if (_etwConsumer is null)
            StartEtwPipeline();
        else if (_attribution is null)
        {
            _pointerMap ??= new PointerMap();
            _spatialIndex ??= new SpatialIndex();
            _attribution = new LayoutCostAttribution(_eventRing!, _pointerMap, _spatialIndex);
            _attribution.BindReconciler(_reconciler);
        }
        _overlayWiring ??= new OverlayHostWiring(_dispatcherQueue);
        if (_attribution is not null)
            _overlayWiring.AttachLayoutCostAttribution(_attribution);
    }

    private void StartEtwPipeline()
    {
        if (_etwConsumer is not null) return;
        _eventPairing ??= new EventPairing();
        _eventRing ??= new LayoutEventRing();
        _etwConsumer = new LayoutEtwConsumer();
        var pairing = _eventPairing;
        var ring = _eventRing;
        _eventPairing.Paired += paired => ring.Publish(paired);
        _etwConsumer.EventReceived += raw => pairing.OnEvent(raw);

        // Build attribution here, not via EnsureLayoutCostPipeline (which
        // would re-enter this method).
        _pointerMap ??= new PointerMap();
        _spatialIndex ??= new SpatialIndex();
        _attribution ??= new LayoutCostAttribution(_eventRing, _pointerMap, _spatialIndex);
        _attribution.BindReconciler(_reconciler);

        try
        {
            _etwConsumer.Start();
            if (_etwConsumer.IsUnavailable)
            {
                _attribution.IsEtwUnavailable = true;
                Debug.WriteLine(
                    $"[Reactor.LayoutCost] ETW unavailable: {_etwConsumer.UnavailableReason}");
            }
        }
        catch (Exception ex)
        {
            _attribution.IsEtwUnavailable = true;
            Debug.WriteLine($"[Reactor.LayoutCost] StartLayoutCostPipeline failed: {ex.Message}");
        }
    }

    /// <summary>Ensure the overlay wrapper exists whenever any dev overlay flag is on.</summary>
    private bool AnyOverlayFlagOn =>
        ReactorFeatureFlags.HighlightReconcileChanges || ReactorFeatureFlags.ShowLayoutCost;

    // Tracks the last observed value of ShowLayoutCost so we only act on
    // off↔on transitions, not on every render.
    private bool _lastLayoutCostFlagState;

    /// <summary>
    /// Stop the ETW session when ShowLayoutCost goes off and restart it
    /// when it goes back on. Keeps the consumer object alive across
    /// transitions so the session-name + leak-guard stay consistent.
    /// </summary>
    private void ApplyEtwSessionState()
    {
        bool on = ReactorFeatureFlags.ShowLayoutCost;
        if (on == _lastLayoutCostFlagState) return;
        _lastLayoutCostFlagState = on;

        if (_etwConsumer is null) return; // pipeline never started, nothing to toggle.
        try
        {
            if (on) _etwConsumer.Start();
            else _etwConsumer.Stop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor.LayoutCost] ETW session toggle ({(on ? "Start" : "Stop")}) failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Internal debug hook — exposed for self-tests. Returns the running ETW
    /// consumer (or null when the flag was off at host init).
    /// </summary>
    internal LayoutEtwConsumer? EtwConsumer => _etwConsumer;

    /// <summary>
    /// Internal hook: the layout-cost reporter (attribution aggregator),
    /// or null when <see cref="ReactorFeatureFlags.ShowLayoutCost"/> hasn't
    /// been observed on. Used by selftest fixtures to inspect rollup state.
    /// </summary>
    internal LayoutCost.ILayoutCostReporter? LayoutCostReporter => _attribution;

    /// <summary>
    /// Internal hook: trigger an immediate layout-cost flush, bypassing the
    /// throttle. Selftest fixtures use this to deterministically wait for
    /// the attribution layer to refresh per-Component bounds via the visual
    /// tree walk after a tree change.
    /// </summary>
    internal void FlushLayoutCostNow()
    {
        if (_overlayWiring is null || _attribution is null) return;
        // Direct call on the UI thread — selftests run on the dispatcher.
        _attribution.RefreshComponentMetricsFromVisualTree(
            _overlayWiring.OverlayCanvas ?? (Microsoft.UI.Xaml.UIElement)_window.Content);
        _attribution.Drain();
    }

    /// <summary>Internal debug hook — paired-event ring buffer (or null when flag was off).</summary>
    internal LayoutEventRing? EventRing => _eventRing;

    /// <summary>
    /// Called by chart elements (via <see cref="ChartingActivation.RequestActivation"/>)
    /// the first time a chart appears in the tree. Lazily allocates the WinRT
    /// accessibility settings, reads their initial values, subscribes for change
    /// notifications, and pushes the values into <see cref="Charting.D3Charts"/>'s
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
                _forcedColorsTheme = Charting.ForcedColorsTheme.FromSystem();
            _accessibilitySettings.HighContrastChanged += OnHighContrastChanged;
        }
        catch { /* headless / unit-test host — no accessibility settings */ }

        try
        {
            _uiSettings = new global::Windows.UI.ViewManagement.UISettings();
            _isReducedMotion = !_uiSettings.AnimationsEnabled;
            _uiSettings.ColorValuesChanged += OnColorValuesChanged;
        }
        catch { /* headless / unit-test host — no UI settings */ }

        PushChartingState();
    }

    // Kept in a separate method so the JIT doesn't resolve Charting.D3Charts
    // when Render() is compiled — D3Charts only loads (and runs its cctor)
    // the first time PushChartingState is actually invoked, which only
    // happens for apps that use charts.
    private void PushChartingState()
    {
        Charting.D3Charts.IsForcedColors = _isForcedColors;
        Charting.D3Charts.IsReducedMotion = _isReducedMotion;
        Charting.D3Charts.ForcedColors = _forcedColorsTheme;
    }

    public void Mount(Component component)
    {
        _rootComponent = component;
        RequestRender();
    }

    public void Mount(Func<RenderContext, Element> renderFunc)
    {
        _rootRenderFunc = renderFunc;
        _funcContext = new RenderContext();
        RequestRender();
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
        var captured = Microsoft.UI.Reactor.Core.Internal.AnimationAmbient.Current;
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
            RenderLoop);
    }

    private void RenderLoop()
    {
        if (_disposed) return;

        // _renderPending is 1 here — all concurrent RequestRender calls are
        // blocked from enqueuing duplicates. Render once, then decide.
        _needsRerender = false;
        Render();

        // Reset the gate so future setState calls can enqueue.
        Interlocked.Exchange(ref _renderPending, 0);

        // If state changed during render, re-enqueue at LOW priority so WinUI
        // layout/paint/input (normal priority + WM_PAINT) run first. Without this,
        // high-frequency setState sources cause back-to-back renders that starve the
        // compositor — layout never runs, property sets on dirty elements get
        // progressively slower, and reconcile time blows up non-linearly.
        if (_needsRerender)
        {
            if (Interlocked.CompareExchange(ref _renderPending, 1, 0) == 0)
                _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, RenderLoop);
        }
    }

    private void Render()
    {
        _isRendering = true;
        // Atomic capture-and-clear gives us at-most-once recovery per
        // UpdateApplication call — see the matching block in
        // ReactorHostControl.Render() for the full rationale.
        bool hotReloadRender = HotReloadService.ConsumeUpdatePending();

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
            _logger?.LogWarning(ex,
                "Hot reload: hook order/type changed — resetting {Mode} state and re-rendering",
                mode);
            ctx.ResetForHotReload();
            RequestRender();
        }

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

            // RequestRender has an optional `force` parameter, so it can't bind
            // directly to an Action method group — wrap once and reuse.
            Action rerender = () => RequestRender();

            if (_rootComponent is not null)
            {
                _rootComponent.Context.BeginRender(rerender);
                try
                {
                    newTree = _rootComponent.Render();
                }
                catch (HookOrderException ex) when (hotReloadRender)
                {
                    RecoverFromHookOrder(ex, _rootComponent.Context, "component");
                    return;
                }
                catch (Exception ex)
                {
                    Debugger.BreakForUserUnhandledException(ex);
                    _logger?.LogError(ex, "Component Render() threw");
                    ShowErrorFallback(ex);
                    return;
                }
            }
            else if (_rootRenderFunc is not null && _funcContext is not null)
            {
                _funcContext.BeginRender(rerender);
                try
                {
                    newTree = _rootRenderFunc(_funcContext);
                }
                catch (HookOrderException ex) when (hotReloadRender)
                {
                    RecoverFromHookOrder(ex, _funcContext, "function-component");
                    return;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Function component threw");
                    ShowErrorFallback(ex);
                    return;
                }
            }

            double treeBuildMs = _phaseSw.Elapsed.TotalMilliseconds;

            if (newTree is null) return;

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
                if (capturedCurve is not null)
                    AnimationScope.PopScope();
            }

            // Single unified install path: any dev overlay flag → install the
            // shared wrapper (once). Sub-overlays paint into the shared Canvas
            // via OverlayHostWiring's root ContainerVisual.
            bool anyOverlayOn = AnyOverlayFlagOn;

            // Always ensure the LC pipeline is plumbed whenever its flag is
            // on — even if the wrapper was already installed for the highlight
            // overlay alone. Without this, a sequence of (highlight on →
            // highlight off → LC on) leaves the wiring instance without an
            // attribution reference, so ScheduleLayoutCostFlush silently
            // returns. EnsureLayoutCostPipeline is idempotent.
            if (ReactorFeatureFlags.ShowLayoutCost)
                EnsureLayoutCostPipeline();
            if (anyOverlayOn)
                _overlayWiring ??= new OverlayHostWiring(_dispatcherQueue);

            // Per-feature teardown: if a flag flipped off while another is
            // still on, dispose just that sub-overlay. The shared wrapper
            // stays put for the remaining overlay.
            _overlayWiring?.ApplyFlagState();
            ApplyEtwSessionState();

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

            // Spec 033 §6 — apply (or clear) the SystemBackdrop modifier carried on
            // the root tree's modifiers. A no-op when the modifier hasn't changed
            // since the last apply.
            _backdropApplier.Apply(newTree?.Modifiers?.Backdrop);

            // Start any connected animations now that the new tree is in the visual tree
            _reconciler.FlushConnectedAnimations();

            // Schedule overlay flushes after layout so elements have final
            // bounds. Both overlays share the same wrapper; each flush method
            // is a no-op when its own flag is off.
            _overlayWiring?.ScheduleHighlightFlush(_reconciler);
            _overlayWiring?.ScheduleLayoutCostFlush();

            double reconcileMs = _phaseSw.Elapsed.TotalMilliseconds;

            _phaseSw.Restart();

            if (_rootComponent is not null)
                _rootComponent.Context.FlushEffects();
            else if (_funcContext is not null)
                _funcContext.FlushEffects();

            double effectsMs = _phaseSw.Elapsed.TotalMilliseconds;

            // Feed RenderPriorityPolicy so the next RequestRender knows whether
            // to demote to Low priority. Stored as the most-recent measurement
            // — no smoothing — so a single slow render is enough to back off,
            // and a single fast render is enough to return to Normal priority.
            // Interlocked publishes the value to off-UI-thread RequestRender
            // callers; the matching Volatile.Read is in RequestRender.
            Interlocked.Exchange(ref _lastRenderMs, treeBuildMs + reconcileMs + effectsMs);

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

            if (_reportClock.Elapsed.TotalSeconds >= 1.0 && _renderCount > 0)
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
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Render FAILED");
            ShowErrorFallback(ex);
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
        Microsoft.UI.Reactor.Core.Reconciler.ClearStyleCache();
        RequestRender();
    }

    private void OnHighContrastChanged(
        global::Windows.UI.ViewManagement.AccessibilitySettings sender, object args)
    {
        _isForcedColors = sender.HighContrast;
        _forcedColorsTheme = _isForcedColors ? Charting.ForcedColorsTheme.FromSystem() : null;
        _logger?.LogDebug("High-contrast changed to {IsHighContrast} — re-rendering", _isForcedColors);
        RequestRender();
    }

    private void OnColorValuesChanged(
        global::Windows.UI.ViewManagement.UISettings sender, object args)
    {
        // UISettings.ColorValuesChanged fires for palette changes and also when
        // AnimationsEnabled toggles. Re-read both signals.
        _isReducedMotion = !sender.AnimationsEnabled;
        // High-contrast palette may also change — re-read to be safe.
        if (_accessibilitySettings is { } a11y)
        {
            _isForcedColors = a11y.HighContrast;
            _forcedColorsTheme = _isForcedColors ? Charting.ForcedColorsTheme.FromSystem() : null;
        }
        RequestRender();
    }

    /// <summary>
    /// Awaits until the render loop is idle (no pending or in-flight renders).
    /// Yields to the dispatcher at Low priority in a loop so that Normal-priority
    /// RenderLoop callbacks and Low-priority re-renders all complete before returning.
    /// Used by test harnesses to replace blind Task.Delay waits.
    /// </summary>
    public Task WaitForIdleAsync(int maxYields = 10)
    {
        if (_disposed) return Task.CompletedTask;
        if (_renderPending == 0 && !_isRendering && !_needsRerender)
            return Task.CompletedTask;

        var tcs = new TaskCompletionSource();
        int yields = 0;
        void CheckIdle()
        {
            if (_disposed || ++yields > maxYields ||
                (_renderPending == 0 && !_isRendering && !_needsRerender))
            {
                tcs.TrySetResult();
            }
            else
            {
                _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, CheckIdle);
            }
        }
        _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, CheckIdle);
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
        if (_accessibilitySettings is not null)
            _accessibilitySettings.HighContrastChanged -= OnHighContrastChanged;
        if (_uiSettings is not null)
            _uiSettings.ColorValuesChanged -= OnColorValuesChanged;

        _rootComponent?.Context.RunCleanups();
        _funcContext?.RunCleanups();

        // Clear the SystemBackdrop so a window-reuse path returns to the WinUI
        // default. Best effort — disposed hosts may already be torn down.
        try { _backdropApplier.Reset(); } catch { /* best effort */ }

        _reconciler.Dispose();
        _rootComponent = null;
        _rootRenderFunc = null;
        _funcContext = null;
        _currentTree = null;
        _currentControl = null;

        try { _overlayWiring?.Dispose(); } catch { /* best effort */ }
        _overlayWiring = null;
        try { _attribution?.UnbindReconciler(); } catch { /* best effort */ }
        _attribution = null;
        _pointerMap = null;
        _spatialIndex = null;
        try { _etwConsumer?.Dispose(); } catch { /* best effort */ }
        _etwConsumer = null;
        _eventPairing = null;
        _eventRing = null;

        ReactorApp.ActiveHostInternal = null;
    }

    private void ShowErrorFallback(Exception ex)
    {
        var errorPanel = Microsoft.UI.Reactor.Core.ErrorFallback.BuildPanel(ex);
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
        _currentTree = null;
    }
}
