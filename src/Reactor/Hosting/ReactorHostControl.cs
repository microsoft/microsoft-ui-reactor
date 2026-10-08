using System.Diagnostics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core;
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
/// Usage in XAML (<c>xmlns:reactor="using:Microsoft.UI.Reactor.Hosting"</c>):
///   <![CDATA[
///   <reactor:ReactorHostControl ComponentType="local:StatsCard" />
///   ]]>
///   — or declare an empty host and mount it from code-behind —
///   <![CDATA[
///   <reactor:ReactorHostControl x:Name="reactorHost" />
///   ]]>
///
/// Usage in code-behind:
///   reactorHost.Mount(new MyComponent());
///   — or —
///   reactorHost.Mount(ctx => VStack(TextBlock("Hello from Reactor!")));
///   — or —
///   reactorHost.ComponentFactory = () => new MyComponent();
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
public sealed partial class ReactorHostControl : ContentControl, IDisposable, Core.Diagnostics.IReactorDiagnosticHost
{
#pragma warning disable CS0414 // Design constant for render-loop limiting; wiring pending
    private static readonly int MaxRenderIterations = 50;
#pragma warning restore CS0414

    private readonly Reconciler _reconciler;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ILogger? _logger;

    // Issue #660 (#86): UISettings.ColorValuesChanged invalidates the theme-brush
    // resolution cache on high-contrast / accent / palette changes (ActualThemeChanged
    // below covers only Light/Dark). Mirrors ReactorHost so an embedded host with no
    // ReactorApp still drops stale brushes on a non-Light/Dark theme change.
    private global::Windows.UI.ViewManagement.UISettings? _uiSettings;

    private Component? _rootComponent;
    // The root's ReactorEventSource.ComponentRendered bookkeeping (id, first render).
    // Replaced (not reset) when the root is retired, so a render already in progress keeps
    // reporting as the root it started with (see _renderingRootDiagnostics).
    private Microsoft.UI.Reactor.Core.Diagnostics.RootRenderDiagnostics _rootDiagnostics = new();
    // The root and its diagnostics as of the current Render()'s start: app code in the root's
    // Render() can Mount() a replacement, and the attempt in progress is still the old root's.
    private Microsoft.UI.Reactor.Core.Diagnostics.RootRenderDiagnostics? _renderingRootDiagnostics;
    private Component? _renderingRoot;
    // The root mount site as of the same snapshot: a root render function's hooks resolve
    // through it, and a reentrant Mount() replaces the live one mid-pass.
    private SourceLocation? _renderingMountSite;
    // Whether the root of the same snapshot is a render function.
    private bool _renderingFunctionRoot;
    private Func<RenderContext, Element>? _rootRenderFunc;
    private RenderContext? _funcContext;

    // Where app code mounted the root, when source mapping was on. Diagnostics only.
    private readonly Diagnostics.RootMountSiteSlot _mountSite = new();

    private Element? _currentTree;
    private UIElement? _currentControl;
    private int _renderPending;      // 0 or 1 — Interlocked for thread-safe access
    private volatile bool _isRendering;       // only touched on UI thread
    private volatile bool _needsRerender;     // only touched on UI thread
    private bool _themeListenerAttached;   // UISettings subscribed (once per control)
    private FrameworkElement? _themeListenerElement;   // current content root listened to

    // Test-only accessor (InternalsVisibleTo Reactor.AppTests.Host).
    internal FrameworkElement? ThemeListenerElement => _themeListenerElement;
    private volatile bool _disposed;
    private Curve? _pendingAnimationCurve;
    // Snapshot of AnimationAmbient.Current at setter dispatch time
    // (spec 042 §6 Q3). Re-pushed around the reconcile pass below.
    private Microsoft.UI.Reactor.Core.Internal.AmbientAnimation? _pendingAmbientAnimation;

    // Spec 033 §6 — backdrop applier in "windowless" mode. Embedded
    // ReactorHostControl does not own its window, so the modifier no-ops with
    // a single debug log. Constructed lazily so we don't pay any cost when no
    // backdrop modifier is ever set.
    private BackdropApplier? _backdropApplier;

    // ── Single shared overlay surface (see OverlayHostWiring) ──
    private OverlayHostWiring? _overlayWiring;

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
    // Published via Interlocked.Exchange / read via Volatile.Read — see the
    // matching note in ReactorHost.
    private double _lastRenderMs;

    // Issue #660 (#180/#181): per-frame delegates cached once. _renderLoopHandler
    // is the DispatcherQueueHandler passed to TryEnqueue; _requestRenderAction is
    // the parameterless RequestRender method group reused for BeginRender and
    // Reconcile instead of re-allocating an Action 2-3x per render.
    private Microsoft.UI.Dispatching.DispatcherQueueHandler? _renderLoopHandler;
    private Action? _requestRenderAction;

    // Public perf snapshot — updated every ~1 second, readable from components
    private RenderStats _stats;

    /// <summary>
    /// Live render performance snapshot, updated every ~1 second.
    /// Always available (FPS, frame time). DEBUG builds include per-reconcile element counters.
    /// </summary>
    /// <remarks>
    /// Returned by value, unlike <see cref="ReactorHost.Stats"/>: the XAML compiler emits
    /// type metadata for every public property of a control declared in markup, and a
    /// <c>ref</c>-returning property becomes <c>typeof(RenderStats&amp;)</c> in the app's
    /// generated <c>XamlTypeInfo.g.cs</c>, which does not compile. The copy is a few dozen
    /// bytes, read about once a second.
    /// </remarks>
    public RenderStats Stats => _stats;

    /// <summary>
    /// Factory to create the root component. Set this or use Mount() for more control.
    /// If set, the component is created and mounted when the control is Loaded.
    /// Example: ComponentFactory = () => new MyComponent();
    /// </summary>
    public Func<Component>? ComponentFactory { get; set; }

    /// <summary>
    /// The root component's type, for declaring a host in XAML:
    /// <c>&lt;reactor:ReactorHostControl ComponentType="local:StatsCard" /&gt;</c>.
    /// When the control is Loaded and nothing has been mounted, an instance is created
    /// and mounted, receiving <see cref="Props"/> like a <see cref="ComponentFactory"/> root.
    /// <see cref="ComponentFactory"/> wins when both are set.
    /// </summary>
    /// <remarks>
    /// <para>The instance is created through the app's XAML type information
    /// (<c>Application.Current</c> as <see cref="Microsoft.UI.Xaml.Markup.IXamlMetadataProvider"/>),
    /// not reflection: naming a type in markup makes the XAML compiler generate an activator
    /// for its public parameterless constructor, so this stays trim- and AOT-safe. A type
    /// that is only ever assigned from code has no such entry, and the host shows an
    /// error saying so in place of the root; set <see cref="ComponentFactory"/> there
    /// instead.</para>
    /// <para>Read once, at Loaded, like <see cref="ComponentFactory"/>; changing it after the
    /// root has mounted does not remount. Use <see cref="Mount(Component)"/> to swap roots.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The type does not derive from <see cref="Component"/>, or is abstract or an open generic.
    /// </exception>
    public Type? ComponentType
    {
        get => _componentType;
        set
        {
            if (value is not null) ValidateComponentType(value);
            _componentType = value;
        }
    }

    private Type? _componentType;

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

    private RenderErrorHandler? _renderErrorHandler;

    /// <summary>
    /// Replaces the built-in render-error fallback for this control only. <c>null</c> (the
    /// default) falls through to <see cref="ReactorApp.DefaultRenderErrorHandler"/> at
    /// error time. See <see cref="Core.RenderErrorHandler"/>. (issue #1291)
    /// </summary>
    public RenderErrorHandler? RenderErrorHandler
    {
        get => Volatile.Read(ref _renderErrorHandler);
        set => Volatile.Write(ref _renderErrorHandler, value);
    }

    /// <summary>
    /// The handler this control would use right now: its own <see cref="RenderErrorHandler"/>,
    /// else <see cref="ReactorApp.DefaultRenderErrorHandler"/>, else <c>null</c> (built-in
    /// fallback).
    /// </summary>
    public RenderErrorHandler? EffectiveRenderErrorHandler => RenderErrorDispatch.Resolve(RenderErrorHandler);

    /// <summary>
    /// Creates an empty host. This is the constructor XAML uses
    /// (<c>&lt;reactor:ReactorHostControl ... /&gt;</c>); set <see cref="ComponentType"/> or
    /// <see cref="ComponentFactory"/>, or call <c>Mount</c>, to give it a root.
    /// </summary>
    public ReactorHostControl() : this(component: null, logger: null)
    {
    }

    public ReactorHostControl(Component? component = null, ILogger? logger = null)
    {
        // Fall back to ReactorApp.AppLogger so an app that sets the process-wide
        // logger before constructing controls gets unified diagnostics. Snapshot
        // at ctor time; later AppLogger writes don't retroactively wire up
        // already-constructed controls.
        _logger = logger ?? ReactorApp.AppLogger;
        _reconciler = new Reconciler(_logger);
        // Diagnostics mode needs call sites stamped from the very first render, so it
        // implies source mapping (REACTOR_SOURCEMAP=1 alone remains the narrower opt-in).
        if (global::Microsoft.UI.Reactor.Hosting.ReactorFeatures.DevtoolsSupported
            && Microsoft.UI.Reactor.Core.Diagnostics.ReactorSourcePublisher.IsEnabled)
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled = true;
        _reconciler.RenderErrorHandlerProvider = () => EffectiveRenderErrorHandler;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        // A standalone ReactorHostControl has no ReactorApp bootstrap, so nothing else
        // sets ReactorApp.UIDispatcher. Cross-thread setState — including the re-render
        // that UseValidationContext schedules when a background async validator raises
        // ValidationContext.Changed — resolves its marshal target from that static and
        // throws when it is null. Seed it exactly as ReactorHost does (spec 036 §4.3).
        if (ReactorApp.UIDispatcher is null)
            ReactorApp.UIDispatcher = _dispatcherQueue;
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

        Core.Diagnostics.ReactorHostRegistry.Register(this);

        if (component is not null)
            MountRoot(component, mountSite: null);
    }

    void Core.Diagnostics.IReactorDiagnosticHost.TagComponentBoundaries()
    {
        if (_disposed) return;
        if (_dispatcherQueue.HasThreadAccess)
            _reconciler.TagComponentBoundaries();
        else
            _dispatcherQueue.TryEnqueue(() => { if (!_disposed) _reconciler.TagComponentBoundaries(); });
    }

    Core.Diagnostics.ReactorHostInfo? Core.Diagnostics.IReactorDiagnosticHost.CaptureDiagnosticInfo()
    {
        if (_disposed) return null;
        return Core.Diagnostics.ReactorHostInfo.ForRoot(
            Core.Diagnostics.ReactorHostKind.HostControl,
            host: null,
            hostControl: this,
            reactorWindow: null,
            window: null,
            hostElement: this,
            rootControl: _currentControl,
            rootComponent: _rootComponent,
            rootRenderFunction: _rootRenderFunc,
            mountSite: _mountSite.Value);
    }

    private bool AnyOverlayFlagOn => ReactorFeatureFlags.HighlightReconcileChanges;

    /// <summary>
    /// <c>hooks=</c> for this host's root on ReactorDiagnostics.SourceProperty: a root component's
    /// <c>Render()</c> hooks, or a root render function's, which the source map keys by the call
    /// the function was passed to: exactly the recorded root mount site.
    /// </summary>
    private string? DiagnosticRootHooks()
        => DiagnosticRoot is { } root
            ? Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetComponentHooks(root.GetType())
            : DiagnosticFunctionRoot
                ? DiagnosticMountSite is { } rootSite
                    ? Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetRenderFunctionHooks(rootSite)
                    : null
                : FailedActivationType is { } failed
                    ? Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetComponentHooks(failed)
                    : null;

    /// <summary>
    /// Owner / root= name of this host's root for ReactorDiagnostics.SourceProperty: its component,
    /// <c>FuncElement</c> for a render function, the <see cref="ComponentType"/> whose Loaded-time
    /// activation failed, or null when the root is unknown (a throwing <see cref="ComponentFactory"/>).
    /// </summary>
    private string? DiagnosticRootName()
        => DiagnosticRoot is { } root
            ? Microsoft.UI.Reactor.Core.Diagnostics.ReactorSourceFormat.ComponentName(root.GetType())
            : DiagnosticFunctionRoot
                ? nameof(FuncElement)
                : FailedActivationType is { } failed
                    ? Microsoft.UI.Reactor.Core.Diagnostics.ReactorSourceFormat.ComponentName(failed)
                    : null;

    private bool DiagnosticFunctionRoot
        => _renderingRootDiagnostics is not null ? _renderingFunctionRoot : _rootComponent is null && _rootRenderFunc is not null;

    // Loaded-time activation failed before any root existed: the root it meant is known only
    // when ComponentType was the source, since a ComponentFactory (which failed) wins over it.
    private Type? FailedActivationType
        => _activationError is not null && ComponentFactory is null ? ComponentType : null;

    // During a render pass, the root that pass renders (snapshotted before app code runs,
    // which can Mount() a replacement); otherwise the live root.
    private Component? DiagnosticRoot
        => _renderingRootDiagnostics is not null ? _renderingRoot : _rootComponent;

    private SourceLocation? DiagnosticMountSite
        => _renderingRootDiagnostics is not null ? _renderingMountSite : _mountSite.Value;

    /// <summary>
    /// A null render keeps the root's content, which no publishing pass then re-describes.
    /// Describe it again as one would, so static facts that went stale since (a hot-reload
    /// update, a late conflicting registration) are dropped: the content's own names and hooks,
    /// and the hooks the host added for the root from its mount site.
    /// </summary>
    private void RepublishRetainedRoot()
    {
        if (_currentTree is null)
        {
            // The built-in error panel (or nothing): no root element to describe.
            _reconciler.RefreshRetainedContentFacts(_currentControl);
            return;
        }
        if (RenderErrorDispatch.IsAppFallback(_currentTree))
        {
            if (DiagnosticRootName() is { } fallbackRootName)
                _reconciler.PublishFallbackRootSource(_currentControl, fallbackRootName, DiagnosticRootHooks());
            else
                _reconciler.RefreshRetainedContentFacts(_currentControl);
            return;
        }
        _reconciler.PublishRootSource(_currentControl, _currentTree, DiagnosticRootName() ?? nameof(FuncElement), DiagnosticRootHooks());
    }

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
        => (_renderingRootDiagnostics ?? _rootDiagnostics).TraceRendered(
            _renderingRoot is null ? nameof(FuncElement) : Microsoft.UI.Reactor.Core.Diagnostics.ComponentNames.For(_renderingRoot, element: null),
            hotReloadRender, _reconciler.ForceFullRenderPending, elapsedMilliseconds);

    // True when a re-entrant Mount replaced the root this pass is rendering (RetireRoot
    // swaps the diagnostics instance the pass snapshotted).
    private bool RootReplacedDuringPass
        => _renderingRootDiagnostics is { } rendering && !ReferenceEquals(rendering, _rootDiagnostics);

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
        // The teardown finishes even when one of the old tree's cleanups throws: every
        // cleanup runs and failures are collected, then routed like disposal's (issue
        // #1291) once the release is done: reported to the RenderErrorHandler as Cleanup,
        // or with none the first is rethrown.
        var cleanupFailures = new List<Exception>();
        if (_currentTree is not null)
        {
            using (_reconciler.IsolateUnmountCleanupFailures(cleanupFailures.Add))
                _reconciler.Reconcile(_currentTree, null, _currentControl, _requestRenderAction ??= RequestRender);
        }
        // Cleared only once the old tree is released: if the release itself failed, the
        // outer render-error path still sees a pending replacement and releases it there.
        _releaseReplacedTreeOnNullRender = false;
        // Installs "no content" through the same path as a render-error outcome that shows
        // nothing: clears the content, moves the theme listener and window hooks off it.
        SetErrorContent(null, null, replacesTree: true);
        _rootDiagnostics.TrackContent(null);
        if (cleanupFailures.Count > 0)
        {
            global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure;
            using (RenderErrorDispatch.EnterPropagationScope())
                failure = RenderErrorDispatch.ReportCleanupFailures(
                    cleanupFailures, () => EffectiveRenderErrorHandler, isHostLevel: false, _logger);
            failure?.Throw();
        }
    }

    // Roots replaced re-entrantly during a render pass, retired by the next pass (or Dispose).
    private List<(Component? Component, RenderContext? FuncContext)>? _deferredRetirements;

    /// <summary>
    /// Runs a retired root's cleanups (draining all of them; failures routed like disposal's)
    /// and detaches its contexts from this host. Returns the failure to rethrow, if any.
    /// </summary>
    private global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? RetireContexts(
        Component? component, RenderContext? funcContext)
    {
        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        Func<RenderErrorHandler?> cleanupHandler = () => EffectiveRenderErrorHandler;
        try
        {
            using (RenderErrorDispatch.EnterPropagationScope())
            {
                RenderErrorDispatch.RunCleanups(component?.Context, cleanupHandler, component?.GetType().Name,
                    isHostLevel: true, _logger, ref failure, drain: true);
                RenderErrorDispatch.RunCleanups(funcContext, cleanupHandler, componentName: null,
                    isHostLevel: true, _logger, ref failure, drain: true);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Defensive: the cleanups drain, so only an unexpected dispatch failure lands here.
            failure ??= global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
        }
        // Drop (not just clean up) the component's hooks: the caller owns the instance and
        // may mount it again later, which must then be a fresh mount. Both contexts also
        // let go of this host, so a retained instance or hook setter neither pins it nor
        // requests renders of the replacement root.
        component?.Context.DetachFromHost();
        funcContext?.DetachFromHost();
        return failure;
    }

    /// <summary>
    /// Retires the roots a previous pass replaced re-entrantly. Runs at the start of the next
    /// pass, inside its error handling: with no <c>RenderErrorHandler</c> a cleanup failure
    /// surfaces as that pass's render error (as <c>ReleaseReplacedTree</c>'s does).
    /// </summary>
    private void RetireDeferredRoots()
    {
        if (_deferredRetirements is not { } deferred) return;
        _deferredRetirements = null;
        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? first = null;
        foreach (var (component, funcContext) in deferred)
        {
            // Every queued root is retired; only the first failure is kept.
            var failure = RetireContexts(component, funcContext);
            first ??= failure;
        }
        first?.Throw();
    }

    /// <summary>
    /// Retires the current root before another is mounted: its effects' cleanups run (as on
    /// Dispose), whichever kind it was, and both root slots are cleared so the render loop
    /// (which checks the component root first) only sees the new one.
    ///
    /// <para>Cleanup failures are routed like disposal's (issue #1291): with a
    /// <c>RenderErrorHandler</c> each is reported; with none, the first escapes. Every
    /// cleanup runs either way. Either way retirement completes first (a failed cleanup is not left
    /// armed, and the hook state and root slots are cleared), and the failure to rethrow
    /// is returned so the caller can install the new root before throwing it.</para>
    ///
    /// <para>Called re-entrantly from the pass that is rendering the root (its Render() or
    /// an effect mounted the replacement), the cleanups and detach are deferred to the next
    /// pass: see <see cref="RetireDeferredRoots"/>.</para>
    /// </summary>
    private global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? RetireRoot()
    {
        var outgoingComponent = _rootComponent;
        var outgoingFuncContext = _funcContext;
        // The slots are cleared before any outgoing cleanup runs: a cleanup can call Mount,
        // and the root it mounts must not be retired or overwritten by this call's caller
        // (see _mountGeneration).
        _rootComponent = null;
        _rootRenderFunc = null;
        _funcContext = null;
        // A fresh instance rather than Reset(): a render of the old root may still be in
        // progress (it called Mount), and it reports as the old root.
        _rootDiagnostics.Forget();
        _rootDiagnostics = new();
        // The old root's content stays on screen until the replacement renders; if that
        // render produces nothing, the old tree must still be released (see Render).
        _releaseReplacedTreeOnNullRender = _currentTree is not null || _currentControl is not null;
        if (outgoingComponent is null && outgoingFuncContext is null)
            return null;
        if (_isRendering)
        {
            // Re-entrant: app code in this pass (the root's Render() or an effect) called
            // Mount. That code is still running against the outgoing contexts, so retiring
            // them now would let later hooks repopulate them and an effect's returned cleanup
            // land after the hook list was dropped. Retire them once the pass has exited.
            (_deferredRetirements ??= new()).Add((outgoingComponent, outgoingFuncContext));
            return null;
        }
        return RetireContexts(outgoingComponent, outgoingFuncContext);
    }

    // Bumped by every Mount. A mount whose retirement ran a cleanup that mounted another
    // root sees it changed and leaves that newer root in place (as a cleanup-mounted root
    // also wins when the retirement is deferred to the next pass).
    private int _mountGeneration;

    /// <summary>
    /// Mount a Component instance directly. Starts the render loop immediately.
    /// </summary>
    public void Mount(Component component)
        => MountRoot(component, Diagnostics.ReactorSourceMap.TakeRootMountSite());

    /// <summary>
    /// Mount a function component. Starts the render loop immediately.
    /// </summary>
    public void Mount(Func<RenderContext, Element> renderFunc)
    {
        _activationError = null;
        var mountSite = Diagnostics.ReactorSourceMap.TakeRootMountSite();
        int generation = ++_mountGeneration;
        var retireFailure = RetireRoot();
        if (generation == _mountGeneration)
        {
            _rootRenderFunc = renderFunc;
            _funcContext = new RenderContext();
            _mountSite.Value = Diagnostics.ReactorSourceMap.KeepIfEnabled(mountSite);
        }
        RequestRender();
        retireFailure?.Throw();
    }

    private void MountRoot(Component component, SourceLocation? mountSite)
    {
        _activationError = null;
        int generation = ++_mountGeneration;
        // Re-mounting the active instance keeps it (and its effects) alive: retiring it
        // would run its cleanups and then reuse the same context, whose unchanged effects
        // would never be scheduled again.
        if (ReferenceEquals(component, _rootComponent))
        {
            // Still an explicit mount: the diagnostics site follows the latest call.
            _mountSite.Value = Diagnostics.ReactorSourceMap.KeepIfEnabled(mountSite);
            RequestRender();
            return;
        }
        var retireFailure = RetireRoot();
        if (generation == _mountGeneration)
        {
            _rootComponent = component;
            _mountSite.Value = Diagnostics.ReactorSourceMap.KeepIfEnabled(mountSite);
        }
        RequestRender();
        retireFailure?.Throw();
    }

    // Set when Loaded-time root creation failed (issue #1291); cleared by Mount.
    private Exception? _activationError;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_rootComponent is not null || _rootRenderFunc is not null)
            return; // Already mounted via Mount()

        if (ComponentFactory is null && ComponentType is null)
            return;

        // Loaded-time activation is an outermost Reactor frame for render-error propagation
        // (issue #1291): the factory can synchronously run another host's first render.
        using var propagationScope = RenderErrorDispatch.EnterPropagationScope();
        var component = TryCreateLoadedRoot(
            ComponentFactory, ComponentType, Props,
            Application.Current as Microsoft.UI.Xaml.Markup.IXamlMetadataProvider,
            out var error);
        if (error is not null)
        {
            // An exception escaping a Loaded handler fail-fasts the whole WinUI process.
            // Report it in this host instead, the same way a throwing Render() is reported:
            // through the app's RenderErrorHandler as a root-render failure (issue #1291), so
            // its text is kept off screen like any other render error.
            _logger?.LogError(error, "ReactorHostControl could not create its root component");
            _activationError = error;
            ShowErrorFallback(error, RenderErrorSource.RootRender, ComponentType?.Name);
            return;
        }

        // A ComponentFactory / ComponentType root has no call site in app code (it is usually
        // set in XAML), and must not claim a root mount scope some unrelated in-flight call opened.
        if (component is not null)
            MountRoot(component, mountSite: null);
    }

    /// <summary>
    /// Creates the Loaded-time root from <see cref="ComponentFactory"/> (which wins) or
    /// <see cref="ComponentType"/>, and applies <see cref="Props"/>. Never throws for an
    /// ordinary failure (no XAML activation info for a code-only <c>ComponentType</c>, a
    /// throwing or null-returning factory, props of the wrong type): it comes back in <paramref name="error"/>
    /// for the host to show. Fatal runtime exceptions still propagate.
    /// </summary>
    internal static Component? TryCreateLoadedRoot(
        Func<Component>? factory,
        Type? componentType,
        object? props,
        Microsoft.UI.Xaml.Markup.IXamlMetadataProvider? provider,
        out Exception? error)
    {
        error = null;
        try
        {
            Component component;
            if (factory is not null)
                component = factory()
                    ?? throw new InvalidOperationException("ReactorHostControl.ComponentFactory returned null.");
            else if (componentType is not null)
                component = CreateComponent(componentType, provider);
            else
                return null;

            if (props is not null && component is IPropsReceiver receiver)
                receiver.SetProps(props);

            return component;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException
            // A render error the app already declined via RenderError.Propagate() — from nested
            // Reactor work the factory started — keeps going out (issue #1291) rather than being
            // reported a second time through this control's own handler.
            && !RenderErrorDispatch.IsPropagating(ex))
        {
            error = ex;
            return null;
        }
    }

    /// <summary>
    /// Rejects a <see cref="ComponentType"/> that can never produce a root, at assignment
    /// time, so a bad XAML attribute fails while the page loads rather than silently
    /// mounting nothing.
    /// </summary>
    internal static void ValidateComponentType(Type type)
    {
        if (!typeof(Component).IsAssignableFrom(type))
            throw new ArgumentException(
                $"ReactorHostControl.ComponentType must derive from {typeof(Component).FullName}; '{type.FullName}' does not.",
                nameof(ComponentType));
        if (type.IsAbstract || type.ContainsGenericParameters)
            throw new ArgumentException(
                $"ReactorHostControl.ComponentType must be a concrete, closed component type; '{type.FullName}' is {(type.IsAbstract ? "abstract" : "an open generic")}.",
                nameof(ComponentType));
    }

    /// <summary>
    /// Creates the root for <see cref="ComponentType"/> through the app's XAML type
    /// information — the same metadata the XAML runtime just used to turn
    /// <c>"local:StatsCard"</c> into a <see cref="Type"/>, so a markup-declared type is
    /// always resolvable and no reflection activation is involved.
    /// </summary>
    internal static Component CreateComponent(Type type, Microsoft.UI.Xaml.Markup.IXamlMetadataProvider? provider)
    {
        ValidateComponentType(type);

        var xamlType = provider?.GetXamlType(type);
        if (xamlType is null || !xamlType.IsConstructible)
            throw new InvalidOperationException(
                $"ReactorHostControl.ComponentType '{type.FullName}' has no XAML activation info in this app. " +
                "ComponentType is for declaring a host in XAML markup and needs a public parameterless constructor; " +
                "from code, set ComponentFactory = () => new " + type.Name + "(...) or call Mount(...) instead.");

        return xamlType.ActivateInstance() as Component
            ?? throw new InvalidOperationException(
                $"XAML activation of ReactorHostControl.ComponentType '{type.FullName}' did not produce a Component.");
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // WinUI fires Unloaded on *any* reparent — transient (drag between
        // TabView pivots, WinUI.Dock document moves, NavigationView header
        // swap, ContentPresenter content swap, theme reload) as well as
        // terminal teardown. There is no signal at this moment to tell the
        // two cases apart, and disposing here makes a transient reparent
        // permanently destroy the Reactor tree (issue #344).
        //
        // Treat Dispose() as consumer-driven (the IDisposable contract):
        // the hosting app calls Dispose when it's truly done with the
        // control. Until then we keep _rootComponent / _reconciler /
        // Content alive so the tree survives reparenting.
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

        // Spec 042 §6 — snapshot the AmbientAnimation set by Animations.Animate
        // so the reconcile pass can re-push it around the diff. Same
        // last-writer-wins rule as the curve capture above.
        // Issue #660 (#183): skip the AsyncLocal walk unless an ambient has ever
        // been opened in this process.
        var capturedAmbient = Microsoft.UI.Reactor.Core.Internal.AnimationAmbient.HasAny
            ? Microsoft.UI.Reactor.Core.Internal.AnimationAmbient.Current
            : null;
        if (capturedAmbient is not null)
            _pendingAmbientAnimation = capturedAmbient;

        // Flag re-render before the _isRendering / CAS checks so the request
        // survives the TOCTOU window between Render()'s finally
        // (_isRendering = false) and RenderLoop's gate-reset
        // (Interlocked.Exchange(_renderPending, 0)).
        _needsRerender = true;

        // During render: the flag is sufficient — RenderLoop re-checks after Render().
        if (_isRendering) return;

        // Between renders: CAS 0→1 gates a single TryEnqueue.
        if (Interlocked.CompareExchange(ref _renderPending, 1, 0) != 0) return;

        // Demote to Low priority after a slow render so input/layout/paint
        // catch up. See RenderPriorityPolicy and the matching code in
        // ReactorHost.RequestRender. Volatile.Read pairs with the
        // Interlocked.Exchange in Render().
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
                _renderingRoot = null;
                _renderingRootDiagnostics = null;
                _renderingMountSite = null;
                _renderingFunctionRoot = false;
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
    /// Hot Reload state migration entry point (spec 049 §6). Mirror of
    /// <c>ReactorHost.MigrateHotReloadState</c> for the in-XAML host control.
    /// Runs once at the start of a hot-reload render pass, before any component
    /// re-renders, value-swapping hook cells of edited types. Never throws out.
    /// </summary>
    private void MigrateHotReloadState()
    {
        // See ReactorHost.MigrateHotReloadState — gate on IsHotReloadLive so the
        // reflection path is statically dead under NativeAOT (spec 049 §8).
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
        // This pass is the hook-order retry if the previous one scheduled it; the marker is
        // dropped when the pass ends even if it never reconciled (a null or failed root).
        bool hotReloadRetryPass = _reconciler.HotReloadRetryPending;
        // Atomic capture-and-clear gives us at-most-once recovery per
        // UpdateApplication call:
        //
        //   UpdateApplication fires:    UpdatePending = 1
        //   Render runs:                ConsumeUpdatePending() → true
        //                               (atomic Interlocked.Exchange to 0)
        //     hooks throw:              recover + RequestRender, return
        //   Recovery render runs:       ConsumeUpdatePending() → false
        //     hooks throw again:        falls through `when (hotReloadRender)`
        //                               filter → ShowErrorFallback
        //
        // A developer who has saved genuinely broken code (hooks that
        // continue to throw after a fresh hook list) sees the error
        // fallback once, not an infinite reset loop. Each subsequent save
        // raises UpdatePending again and grants exactly one more retry.
        //
        // Atomicity matters because UpdateApplication is invoked by the
        // hot-reload runtime on a non-UI thread; a non-atomic read-then-
        // write here could miss a pending update if the hot-reload thread
        // raises the flag in the window between the read and the write.
        bool hotReloadRender = HotReloadService.ConsumeUpdatePending();

        // Open a tree-wide hot-reload pass for the duration of this render so
        // the reconciler can recover hook-order changes in non-root children
        // (see the matching block in ReactorHost.Render). The using disposes
        // on every exit path, clearing the flag.
        using IDisposable? hotReloadPass = hotReloadRender
            ? HotReloadService.BeginUpdatePass()
            : null;

        // Hot Reload state migration (spec 049 §6) — see ReactorHost.Render for
        // the full rationale. Value-swaps hook cells of edited types before any
        // component re-renders; a plain force-render (no UpdatedTypes) no-ops.
        if (hotReloadRender)
            MigrateHotReloadState();

        // Local helper centralizes the recovery sequence (log → reset
        // RenderContext → request a fresh render). Both component-mode
        // and function-mode catches share it; future tweaks (telemetry,
        // additional reset steps, throttling) only need editing here.
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
            // it to hot reload: the root here, and its children via the reconciler.
            _rootDiagnostics.MarkHotReloadRetry();
            _reconciler.HotReloadRetryPending = true;
            RequestRender();
        }

        // Which phase the outer catch attributes a failure to (issue #1291).
        var failurePhase = RenderErrorSource.Reconcile;
        // The root whose effects are being flushed, captured before app code runs: an effect
        // can Mount() a replacement root and then throw, and the failure is the original's.
        Component? effectsRoot = null;
        try
        {
            if (_deferredRetirements is not null)
            {
                failurePhase = RenderErrorSource.Cleanup;
                RetireDeferredRoots();
                failurePhase = RenderErrorSource.Reconcile;
            }
            // Snapshot the root this pass renders, after deferred retirement (whose cleanups
            // may Mount) and before any of its code runs (which may Mount again; RenderLoop
            // clears the snapshot when the pass ends).
            _renderingRoot = _rootComponent;
            _renderingRootDiagnostics = _rootDiagnostics;
            _renderingMountSite = _mountSite.Value;
            _renderingFunctionRoot = _rootComponent is null && _rootRenderFunc is not null;

            Element? newTree = null;

            _phaseSw.Restart();

            // Captured before app code runs: the root's Render() can Mount() a replacement
            // root and then throw, and the failure belongs to the component that threw.
            if (_rootComponent is { } renderingRoot)
            {
                renderingRoot.Context.BeginRender(_requestRenderAction ??= RequestRender);
                try
                {
                    using (ValidationRenderScope.Begin(null))
                    {
                        newTree = ValidationRenderScope.ApplyProvide(renderingRoot.Render());
                    }
                }
                catch (HookOrderException ex) when (hotReloadRender)
                {
                    RecoverFromHookOrder(ex, renderingRoot.Context, "component");
                    return;
                }
                catch (Exception ex) when (!RenderErrorDispatch.IsPropagating(ex))
                {
                    // Sampled before logging: this is the render's time.
                    double renderMs = _phaseSw.Elapsed.TotalMilliseconds;
                    _logger?.LogError(ex, "Component Render() threw");
                    // Before the fallback: the app's handler may call Propagate(), which throws.
                    Reconciler.EmitRenderError(
                        Microsoft.UI.Reactor.Core.Diagnostics.ComponentNames.For(renderingRoot, element: null), ex);
                    ShowRootRenderError(ex, hotReloadRender, renderingRoot.GetType().Name, renderMs);
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
                _funcContext.BeginRender(_requestRenderAction ??= RequestRender);
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
                    // Before the fallback: the app's handler may call Propagate(), which throws.
                    Reconciler.EmitRenderError(nameof(FuncElement), ex);
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
            else if (_activationError is not null)
            {
                // Loaded-time activation failed, so there is no root to render. A re-render
                // requested by the app's fallback (its own state) re-runs the handler like any
                // failing root render, so the fallback is reconciled in place rather than frozen.
                ShowErrorFallback(_activationError, RenderErrorSource.RootRender, ComponentType?.Name);
                return;
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
                if (global::Microsoft.UI.Reactor.Hosting.ReactorFeatures.DevtoolsSupported
                    && Microsoft.UI.Reactor.Core.Diagnostics.ReactorSourcePublisher.IsEnabled)
                    RepublishRetainedRoot();
                // Commit the render like any other: the root's effects still run, and a
                // failure among them is routed as an effect failure by the outer catch.
                failurePhase = RenderErrorSource.Effects;
                // Captured like the non-null path's, so a failure is named after this root.
                effectsRoot = _rootComponent;
                if (effectsRoot is not null)
                    effectsRoot.Context.FlushEffects();
                else if (_funcContext is not null)
                    _funcContext.FlushEffects();
                failurePhase = RenderErrorSource.Reconcile;
                return;
            }
            TraceRootRendered(hotReloadRender, treeBuildMs);

            _phaseSw.Restart();

            var capturedCurve = Interlocked.Exchange(ref _pendingAnimationCurve, null);
            if (capturedCurve is not null)
                AnimationScope.PushScope(capturedCurve);

            // Spec 042 §6 — re-push the captured Animations.Animate ambient
            // so KeyedListDiff / ChildReconciler observe it during reconcile.
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
                    _requestRenderAction ??= RequestRender
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

            bool anyOverlayOn = AnyOverlayFlagOn;

            if (anyOverlayOn)
                _overlayWiring ??= new OverlayHostWiring(_dispatcherQueue);

            // Per-feature teardown for the case where one flag flipped off
            // while another is still on.
            _overlayWiring?.ApplyFlagState();

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
            // A root replaced during this pass (it called Mount) still committed its own tree;
            // that tree is the replacement's to release if its first render produces nothing.
            _releaseReplacedTreeOnNullRender = RootReplacedDuringPass;
            if (global::Microsoft.UI.Reactor.Hosting.ReactorFeatures.DevtoolsSupported
                && Microsoft.UI.Reactor.Core.Diagnostics.ReactorSourcePublisher.IsEnabled)
                _reconciler.PublishRootSource(
                    // A pass that reconciled a tree rendered a component or function root.
                    newControl, newTree, DiagnosticRootName() ?? nameof(FuncElement),
                    DiagnosticRootHooks());
            _rootDiagnostics.TrackContent(newControl);

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

            // Schedule the highlight overlay flush after layout; no-op when
            // the flag is off.
            _overlayWiring?.ScheduleHighlightFlush(_reconciler);

            double reconcileMs = _phaseSw.Elapsed.TotalMilliseconds;

            _phaseSw.Restart();

            failurePhase = RenderErrorSource.Effects;
            effectsRoot = _rootComponent;
            if (effectsRoot is not null)
                effectsRoot.Context.FlushEffects();
            else if (_funcContext is not null)
                _funcContext.FlushEffects();
            failurePhase = RenderErrorSource.Reconcile;

            double effectsMs = _phaseSw.Elapsed.TotalMilliseconds;

            // Feed RenderPriorityPolicy. Single writer (this UI-thread Render);
            // off-thread RequestRender readers use Volatile.Read, so a release
            // Volatile.Write suffices (issue #660 #185). See ReactorHost.Render.
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

            // Issue #660 (#186): integer ElapsedMilliseconds gate instead of a
            // per-frame Stopwatch.Elapsed.TotalSeconds TimeSpan + division.
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
            // A root effect failure belongs to the root component and is reported like a
            // child's effect-flush failure (issue #1321) — before the fallback, because the
            // app's handler may call Propagate(), which throws. A commit-phase failure has no
            // single owning component and is not a RenderError.
            if (failurePhase == RenderErrorSource.Effects)
            {
                Reconciler.EmitRenderError(
                    effectsRoot is not null
                        ? Microsoft.UI.Reactor.Core.Diagnostics.ComponentNames.For(effectsRoot, element: null)
                        : nameof(FuncElement),
                    ex);
            }
            ShowErrorFallback(ex, failurePhase,
                failurePhase == RenderErrorSource.Effects ? effectsRoot?.GetType().Name : null);
        }
        finally
        {
            _isRendering = false;
            if (hotReloadRetryPass) _reconciler.HotReloadRetryPending = false;
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
        if (control is not FrameworkElement fe || ReferenceEquals(fe, _themeListenerElement)) return;

        // Follow the current content root (issue #1291): an app-supplied error fallback
        // replaces the root after a successful render, and a listener left on the
        // detached old root would never see ActualThemeChanged again.
        if (_themeListenerElement is not null)
            _themeListenerElement.ActualThemeChanged -= OnActualThemeChanged;
        _themeListenerElement = fe;
        fe.ActualThemeChanged += OnActualThemeChanged;

        if (_themeListenerAttached) return;
        _themeListenerAttached = true;

        // Issue #660 (#86): also invalidate on high-contrast / accent / palette
        // changes, which don't raise ActualThemeChanged but can change a resolved
        // brush. ColorValuesChanged fires off the UI thread; ThemeRef cache clear
        // and RequestRender are both thread-safe.
        try
        {
            _uiSettings = new global::Windows.UI.ViewManagement.UISettings();
            _uiSettings.ColorValuesChanged += OnColorValuesChanged;
        }
        catch { /* headless / no UISettings projection — nothing to invalidate */ }
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        _logger?.LogDebug("Theme changed to {Theme} — re-rendering", sender.ActualTheme);
        // Issue #660 (#86): drop the (key,theme)->Brush cache so ThemeRef
        // resolves re-read the now-current ThemeDictionaries.
        Microsoft.UI.Reactor.Core.ThemeRef.InvalidateResolutionCache();
        RequestRender();
    }

    private void OnColorValuesChanged(global::Windows.UI.ViewManagement.UISettings sender, object args)
    {
        Microsoft.UI.Reactor.Core.ThemeRef.InvalidateResolutionCache();
        RequestRender();
    }

    private void ShowErrorFallback(Exception ex, RenderErrorSource source, string? componentName = null)
    {
        // The render that failed never reaches Reconcile, so its validation claims have
        // no consumer. Withdraw them here rather than waiting for a next render that may
        // never come (issue #1262).
        Controls.Validation.ValidationRenderScope.AbandonPendingClaims();

        // Issue #1291 — see ReactorHost.ShowErrorFallback.
        var error = new RenderError(ex, source, componentName, isHostLevel: true);
        var rerender = _requestRenderAction ??= RequestRender;
        // Replacing or releasing the old tree must finish even when one of its cleanups
        // throws: the host forgets that tree afterwards, so anything left registered would
        // stay alive. Every cleanup runs; failures are collected and reported below.
        // A swapped-out root's tree follows the disposal contract instead: its cleanup
        // failures are collected and offered to the handler once the outcome is installed.
        var releasedTreeFailures = _releaseReplacedTreeOnNullRender ? new List<Exception>() : null;
        var teardownErrors = new RenderErrorDispatch.TeardownErrors(_logger, releasedTreeFailures);
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
            // The previous root's tree is still shown after a root swap; like an app fallback,
            // it must be released rather than abandoned when the replacement fails.
            currentIsAppFallback: RenderErrorDispatch.IsAppFallback(_currentTree) || _releaseReplacedTreeOnNullRender);
        SetErrorContent(content, tree, replacesTree);
        // An app-supplied fallback tree stands in for the root's content: name the root on it.
        // An unknown root (a throwing ComponentFactory) is not attributed.
        if (tree is not null
            && global::Microsoft.UI.Reactor.Hosting.ReactorFeatures.DevtoolsSupported
            && Microsoft.UI.Reactor.Core.Diagnostics.ReactorSourcePublisher.IsEnabled
            && DiagnosticRootName() is { } fallbackRootName)
            _reconciler.PublishFallbackRootSource(
                _currentControl, fallbackRootName,
                DiagnosticRootHooks());
        // ComponentRendered bookkeeping; see ReactorHost.ShowErrorFallback.
        if (tree is null)
            _reconciler.ForgetComponentDiagnostics();
        _rootDiagnostics.TrackContent(_currentControl);
        teardownErrors.RethrowPropagated();
        if (releasedTreeFailures is not null)
            RenderErrorDispatch.ReportReleasedTreeCleanupFailures(
                releasedTreeFailures, () => EffectiveRenderErrorHandler, _logger)?.Throw();
        if (propagate)
            RenderErrorDispatch.RaiseUnhandled(ex);
    }

    private void SetErrorContent(UIElement? errorPanel, Element? errorTree, bool replacesTree)
    {
        // Whatever was shown has been replaced; nothing is left for ReleaseReplacedTree,
        // unless this is the outgoing attempt of a root replaced during this pass: the
        // content it leaves behind is the replacement's to release.
        _releaseReplacedTreeOnNullRender = RootReplacedDuringPass && (errorPanel is not null || errorTree is not null);
        if (_overlayWiring is not null && _overlayWiring.TryShowErrorInWrapper(errorPanel))
        {
            // shared overlay wrapper took it
        }
        else
        {
            Content = errorPanel;
        }
        _currentControl = errorPanel;
        _currentTree = errorTree;
        // See ReactorHost.SetErrorContent: when the handler's outcome replaced the tree, the
        // theme listener follows the new content, or is detached when there is none.
        if (replacesTree)
        {
            if (errorPanel is FrameworkElement)
                AttachThemeListener(errorPanel);
            else
                DetachThemeListener();
        }
    }

    private void DetachThemeListener()
    {
        if (_themeListenerElement is null) return;
        _themeListenerElement.ActualThemeChanged -= OnActualThemeChanged;
        _themeListenerElement = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Core.Diagnostics.ReactorHostRegistry.Unregister(this);

        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;

        if (_uiSettings is not null)
        {
            _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
            _uiSettings = null;
        }
        DetachThemeListener();

        // Issue #1291 — see ReactorHost.Dispose.
        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? pendingPropagation = null;
        // Resolved per failure (a cleanup may change the handler), not once per batch.
        Func<RenderErrorHandler?> cleanupHandler = () => EffectiveRenderErrorHandler;
        using (RenderErrorDispatch.EnterPropagationScope())
        {
            // Roots replaced re-entrantly by a pass that never got a successor.
            if (_deferredRetirements is { } deferred)
            {
                _deferredRetirements = null;
                foreach (var (component, funcContext) in deferred)
                {
                    // Cleans up and detaches, so a retained old root or setter does not pin
                    // this disposed host; the first failure escapes once disposal is done.
                    var failure = RetireContexts(component, funcContext);
                    pendingPropagation ??= failure;
                }
            }
            RenderErrorDispatch.RunCleanups(_rootComponent?.Context, cleanupHandler, _rootComponent?.GetType().Name,
                isHostLevel: true, _logger, ref pendingPropagation);
            RenderErrorDispatch.RunCleanups(_funcContext, cleanupHandler, componentName: null,
                isHostLevel: true, _logger, ref pendingPropagation);
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

        Content = null;
        pendingPropagation?.Throw();
    }
}
