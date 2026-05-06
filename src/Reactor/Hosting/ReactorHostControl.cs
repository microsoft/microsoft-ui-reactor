using System.Diagnostics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting.Etw;
using Microsoft.UI.Reactor.Hosting.LayoutCost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// A fully self-contained WinUI ContentControl that hosts a Reactor component tree.
/// Drop this into any vanilla WinUI app — no ReactorApp, ReactorApplication, or special
/// bootstrapping needed. Each instance owns its own Reconciler and render loop.
///
/// Usage in XAML:
///   <![CDATA[
///   <local:ReactorHostControl x:Name="ductHost" />
///   ]]>
///
/// Usage in code-behind:
///   ductHost.Mount(new MyComponent());
///   — or —
///   ductHost.Mount(ctx => VStack(Text("Hello from Reactor!")));
///   — or via XAML property —
///   <![CDATA[
///   <local:ReactorHostControl ComponentFactory="{x:Bind CreateMyComponent}" />
///   ]]>
///
/// Features:
///   - Thread-safe render batching (setState from any thread)
///   - Low-priority re-enqueue so layout/paint/input aren't starved
///   - Render performance stats (FPS, frame timing)
///   - Automatic theme change detection and re-render
///   - Connected animation flushing
///   - Error boundary with fallback UI
///   - Clean lifecycle via Loaded/Unloaded
/// </summary>
public sealed partial class ReactorHostControl : ContentControl, IDisposable
{
#pragma warning disable CS0414 // Design constant for render-loop limiting; wiring pending
    private static readonly int MaxRenderIterations = 50;
#pragma warning restore CS0414

    private readonly Reconciler _reconciler;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ILogger _logger;

    private Component? _rootComponent;
    private Func<RenderContext, Element>? _rootRenderFunc;
    private RenderContext? _funcContext;

    private Element? _currentTree;
    private UIElement? _currentControl;
    private int _renderPending;      // 0 or 1 — Interlocked for thread-safe access
    private volatile bool _isRendering;       // only touched on UI thread
    private volatile bool _needsRerender;     // only touched on UI thread
    private bool _themeListenerAttached;
    private volatile bool _disposed;
    private Curve? _pendingAnimationCurve;

    // Spec 033 §6 — backdrop applier in "windowless" mode. Embedded
    // ReactorHostControl does not own its window, so the modifier no-ops with
    // a single debug log. Constructed lazily so we don't pay any cost when no
    // backdrop modifier is ever set.
    private BackdropApplier? _backdropApplier;

    // ── Single shared overlay surface (see OverlayHostWiring) ──
    private OverlayHostWiring? _overlayWiring;

    // ── Layout cost data pipeline (attribution + ETW) ──
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

    // Public perf snapshot — updated every ~1 second, readable from components
    private RenderStats _stats;

    /// <summary>
    /// Live render performance snapshot, updated every ~1 second.
    /// Always available (FPS, frame time). DEBUG builds include per-reconcile element counters.
    /// </summary>
    public ref readonly RenderStats Stats => ref _stats;

    /// <summary>
    /// Factory to create the root component. Set this or use Mount() for more control.
    /// If set, the component is created and mounted when the control is Loaded.
    /// Example: ComponentFactory = () => new MyComponent();
    /// </summary>
    public Func<Component>? ComponentFactory { get; set; }

    /// <summary>
    /// Optional props to pass to the root component created by ComponentFactory.
    /// </summary>
    public object? Props { get; set; }

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

    public ReactorHostControl(Component? component = null, ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
        _reconciler = new Reconciler(_logger);
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        // ContentControl inherits IsTabStop=true from Control. Set it to false
        // so focus navigation passes through to child elements directly. Without
        // this, Shift+Tab from the first child stops on the ReactorHostControl itself
        // (invisible focus) before departing — especially problematic in XAML Islands
        // where that extra stop prevents TakeFocusRequested from firing.
        IsTabStop = false;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // Register built-in custom element types
        Controls.ResizeGripRegistration.Register(_reconciler);

        if (ReactorFeatureFlags.ShowLayoutCost)
            StartEtwPipeline();

        if (component is not null)
            Mount(component);
    }

    /// <summary>Build attribution + subscribe the reconciler + attach it to the overlay wiring. Idempotent.</summary>
    private void EnsureLayoutCostPipeline()
    {
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

    private bool AnyOverlayFlagOn =>
        ReactorFeatureFlags.HighlightReconcileChanges || ReactorFeatureFlags.ShowLayoutCost;

    private bool _lastLayoutCostFlagState;

    /// <summary>
    /// Stop the ETW session when ShowLayoutCost goes off; restart on flag-on.
    /// Mirrors <see cref="ReactorHost"/>.
    /// </summary>
    private void ApplyEtwSessionState()
    {
        bool on = ReactorFeatureFlags.ShowLayoutCost;
        if (on == _lastLayoutCostFlagState) return;
        _lastLayoutCostFlagState = on;

        if (_etwConsumer is null) return;
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

    /// <summary>
    /// Mount a Component instance directly. Starts the render loop immediately.
    /// </summary>
    public void Mount(Component component)
    {
        _rootRenderFunc = null;
        _funcContext = null;
        _rootComponent = component;
        RequestRender();
    }

    /// <summary>
    /// Mount a function component. Starts the render loop immediately.
    /// </summary>
    public void Mount(Func<RenderContext, Element> renderFunc)
    {
        _rootComponent = null;
        _rootRenderFunc = renderFunc;
        _funcContext = new RenderContext();
        RequestRender();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_rootComponent is not null || _rootRenderFunc is not null)
            return; // Already mounted via Mount()

        if (ComponentFactory is null)
            return;

        var component = ComponentFactory();

        if (Props is not null && component is IPropsReceiver receiver)
            receiver.SetProps(Props);

        Mount(component);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Dispose();
    }

    /// <summary>
    /// Thread-safe: can be called from any thread. Coalesces multiple calls into
    /// a single render. At most one RenderLoop is ever pending on the dispatcher.
    ///
    /// During render: setState calls set _needsRerender (no enqueue).
    /// Between renders: first setState CAS-flips _renderPending 0→1 and enqueues.
    /// _renderPending stays 1 throughout the render, blocking duplicate enqueues.
    /// </summary>
    private void RequestRender()
    {
        if (_disposed) return;

        if (AnimationScope.HasScope)
            _pendingAnimationCurve = AnimationScope.Current;

        // Flag re-render before the _isRendering / CAS checks so the request
        // survives the TOCTOU window between Render()'s finally
        // (_isRendering = false) and RenderLoop's gate-reset
        // (Interlocked.Exchange(_renderPending, 0)).
        _needsRerender = true;

        // During render: the flag is sufficient — RenderLoop re-checks after Render().
        if (_isRendering) return;

        // Between renders: CAS 0→1 gates a single TryEnqueue.
        if (Interlocked.CompareExchange(ref _renderPending, 1, 0) != 0) return;

        _dispatcherQueue.TryEnqueue(RenderLoop);
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
        try
        {
            Element? newTree = null;

            _phaseSw.Restart();

            if (_rootComponent is not null)
            {
                _rootComponent.Context.BeginRender(RequestRender);
                try
                {
                    newTree = _rootComponent.Render();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Component Render() threw");
                    ShowErrorFallback(ex);
                    return;
                }
            }
            else if (_rootRenderFunc is not null && _funcContext is not null)
            {
                _funcContext.BeginRender(RequestRender);
                try
                {
                    newTree = _rootRenderFunc(_funcContext);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Function component threw");
                    ShowErrorFallback(ex);
                    return;
                }
            }

            double treeBuildMs = _phaseSw.Elapsed.TotalMilliseconds;

            if (newTree is null) return;

            _phaseSw.Restart();

            var capturedCurve = Interlocked.Exchange(ref _pendingAnimationCurve, null);
            if (capturedCurve is not null)
                AnimationScope.PushScope(capturedCurve);

            UIElement? newControl;
            try
            {
                newControl = _reconciler.Reconcile(
                    _currentTree,
                    newTree,
                    _currentControl,
                    RequestRender
                );
            }
            finally
            {
                if (capturedCurve is not null)
                    AnimationScope.PopScope();
            }

            bool anyOverlayOn = AnyOverlayFlagOn;

            // Always ensure the LC pipeline is plumbed whenever its flag is
            // on, even when the wrapper was previously installed for some
            // other overlay. Idempotent. See matching note in ReactorHost.
            if (ReactorFeatureFlags.ShowLayoutCost)
                EnsureLayoutCostPipeline();
            if (anyOverlayOn)
                _overlayWiring ??= new OverlayHostWiring(_dispatcherQueue);

            // Per-feature teardown for the case where one flag flipped off
            // while another is still on.
            _overlayWiring?.ApplyFlagState();
            ApplyEtwSessionState();

            if (newControl != _currentControl)
            {
                UIElement? contentToSet = newControl;
                if (anyOverlayOn)
                    contentToSet = _overlayWiring!.SetContentViaWrapper(newControl);
                Content = contentToSet;
                AttachThemeListener(newControl);
            }
            else if (anyOverlayOn && _overlayWiring!.WrapperRoot is null)
            {
                // Flag flipped on mid-session. Detach current Content before
                // re-parenting into the wrapper slot (WinUI throws "Element
                // already has a logical parent" otherwise).
                Content = null;
                Content = _overlayWiring.SetContentViaWrapper(newControl);
            }
            else if (!anyOverlayOn && _overlayWiring?.WrapperRoot is not null)
            {
                // All overlay flags off — tear down the wrapper. Detach the
                // content from the wrapper's slot first; WinUI throws
                // "Element already has a logical parent" otherwise.
                _overlayWiring.DetachContent();
                Content = newControl;
                _overlayWiring.Dispose();
                _overlayWiring = null;
            }

            _currentControl = newControl;
            _currentTree = newTree;

            // Spec 033 §6 — Backdrop modifier on the root tree is a no-op for
            // ReactorHostControl, which doesn't own its hosting Window. We
            // still construct the applier (lazily, only on first encounter) so
            // the no-op log fires exactly once per host instance.
            if (newTree?.Modifiers?.Backdrop is { } backdropChoice)
            {
                _backdropApplier ??= new BackdropApplier(window: null);
                _backdropApplier.Apply(backdropChoice);
            }

            // Start any connected animations now that the new tree is in the visual tree
            _reconciler.FlushConnectedAnimations();

            // Schedule overlay flushes after layout; each is a no-op when its
            // own flag is off.
            _overlayWiring?.ScheduleHighlightFlush(_reconciler);
            _overlayWiring?.ScheduleLayoutCostFlush();

            double reconcileMs = _phaseSw.Elapsed.TotalMilliseconds;

            _phaseSw.Restart();

            if (_rootComponent is not null)
                _rootComponent.Context.FlushEffects();
            else if (_funcContext is not null)
                _funcContext.FlushEffects();

            double effectsMs = _phaseSw.Elapsed.TotalMilliseconds;

            OnRenderComplete?.Invoke(treeBuildMs, reconcileMs, effectsMs);

#if DEBUG
            _logger.LogDebug(
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

                _logger.LogDebug(
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
            _logger.LogError(ex, "Render FAILED");
            ShowErrorFallback(ex);
        }
        finally
        {
            _isRendering = false;
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
        if (_themeListenerAttached || control is not FrameworkElement fe) return;
        _themeListenerAttached = true;

        fe.ActualThemeChanged += (_, _) =>
        {
            _logger.LogDebug("Theme changed to {Theme} — re-rendering", fe.ActualTheme);
            RequestRender();
        };
    }

    private void ShowErrorFallback(Exception ex)
    {
        var errorPanel = Microsoft.UI.Reactor.Core.ErrorFallback.BuildPanel(ex);
        if (_overlayWiring is not null && _overlayWiring.TryShowErrorInWrapper(errorPanel))
        {
            // shared overlay wrapper took it
        }
        else
        {
            Content = errorPanel;
        }
        _currentControl = errorPanel;
        _currentTree = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;

        _rootComponent?.Context.RunCleanups();
        _funcContext?.RunCleanups();
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

        Content = null;
    }
}
