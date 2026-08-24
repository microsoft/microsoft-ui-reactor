// Slim Reactor render host for Uno Skia targets.
//
// Adapted from src/Reactor/Hosting/ReactorHost.cs, keeping the coalescing render
// loop, the reconcile→Window.Content install, effects flush, perf stats, and the
// charting-activation seam. Dropped (vs. the Windows host): system backdrop,
// dev-overlay wiring, hot-reload state migration, focus-revalidation, and the
// in-render accessibility push — none are needed for the Skia port's core path.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Hosts a Reactor component tree inside an Uno <see cref="Window"/>. Drives the
/// render loop: on state change, re-renders the component and reconciles the
/// virtual tree against the real control tree.
/// </summary>
public sealed class ReactorHost : IDisposable
{
    private readonly Window _window;
    private readonly Reconciler _reconciler;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ILogger? _logger;

    private Component? _rootComponent;
    private Func<RenderContext, Element>? _rootRenderFunc;
    private RenderContext? _funcContext;

    private Element? _currentTree;
    private UIElement? _currentControl;
    private int _renderPending;
    private volatile bool _isRendering;
    private volatile bool _needsRerender;
    private FrameworkElement? _themeListenerElement;
    private volatile bool _disposed;

    private Curve? _pendingAnimationCurve;
    private Microsoft.UI.Reactor.Core.Internal.AmbientAnimation? _pendingAmbientAnimation;

    // ── charting accessibility seam (issue #498) ──
    private global::Windows.UI.ViewManagement.AccessibilitySettings? _accessibilitySettings;
    private global::Windows.UI.ViewManagement.UISettings? _uiSettings;
    private volatile bool _isForcedColors;
    private volatile bool _isReducedMotion;
    private object? _forcedColorsTheme;
    private int _chartingActiveFlag;
    private static IChartingHostBridge? s_chartingBridge;

    private readonly Stopwatch _phaseSw = new();
    private double _lastRenderMs;
    private RenderStats _stats;

    /// <summary>Live render performance snapshot, updated about once per second.</summary>
    public ref readonly RenderStats Stats => ref _stats;

    /// <summary>The underlying reconciler (for RegisterType calls).</summary>
    public Reconciler Reconciler => _reconciler;

    /// <summary>Optional per-render timing callback: (treeBuildMs, reconcileMs, effectsMs).</summary>
    public Action<double, double, double>? OnRenderComplete { get; set; }

    /// <summary>The Uno window hosting this Reactor tree.</summary>
    public Window Window => _window;

    /// <summary>The Reactor window wrapper that owns this host.</summary>
    public ReactorWindow? OwningWindow { get; internal set; }

    /// <summary>When set, Reactor renders into this Border instead of Window.Content.</summary>
    public Microsoft.UI.Xaml.Controls.Border? ContentTarget { get; set; }

    internal Component? RootComponent => _rootComponent;
    internal UIElement? CurrentControl => _currentControl;

    public ReactorHost(Window window, ILogger? logger = null)
    {
        _logger = logger ?? ReactorApp.AppLogger;
        _reconciler = new Reconciler(_logger);
        _window = window;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        if (ReactorApp.UIDispatcher is null)
            ReactorApp.UIDispatcher = _dispatcherQueue;
        ReactorApp.ActiveHostInternal = this;

        try
        {
            _window.Closed += (_, _) => Dispose();
        }
        catch { /* headless / windowless */ }
    }

    // ── charting activation seam ──

    internal static void RegisterChartingBridge(IChartingHostBridge bridge)
        => s_chartingBridge = bridge;

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
        catch { /* settings unavailable on this head */ }

        try
        {
            _uiSettings = new global::Windows.UI.ViewManagement.UISettings();
            _isReducedMotion = !_uiSettings.AnimationsEnabled;
        }
        catch { /* settings unavailable */ }

        PushChartingState();
    }

    private void PushChartingState()
        => s_chartingBridge?.PushAccessibilityState(_isForcedColors, _isReducedMotion, _forcedColorsTheme);

    // ── mount + render loop ──

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

    internal void RequestRender(bool force = false)
    {
        if (_disposed) return;

        if (force)
            _reconciler.ForceFullRenderPending = true;

        if (AnimationScope.HasScope)
            _pendingAnimationCurve = AnimationScope.Current;

        var captured = Microsoft.UI.Reactor.Core.Internal.AnimationAmbient.Current;
        if (captured is not null)
            _pendingAmbientAnimation = captured;

        if (_isRendering)
        {
            _needsRerender = true;
            return;
        }

        if (Interlocked.CompareExchange(ref _renderPending, 1, 0) != 0)
        {
            _needsRerender = true;
            return;
        }

        // First render is synchronous on the UI thread so content attaches
        // before window.Activate() returns.
        if (_currentControl is null && _dispatcherQueue.HasThreadAccess)
        {
            RenderLoop();
            return;
        }

        _dispatcherQueue.TryEnqueue(
            RenderPriorityPolicy.PickPriority(Volatile.Read(ref _lastRenderMs)),
            RenderLoop);
    }

    private void RenderLoop()
    {
        if (_disposed) return;

        _needsRerender = false;
        Render();

        Interlocked.Exchange(ref _renderPending, 0);

        if (_needsRerender)
        {
            if (Interlocked.CompareExchange(ref _renderPending, 1, 0) == 0)
                _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, RenderLoop);
        }
    }

    /// <summary>
    /// Hot Reload state migration entry point (spec 049 §6). Runs once at the
    /// start of a hot-reload render pass, before any component re-renders, and
    /// asks every live <see cref="RenderContext"/> to value-swap hook cells
    /// whose stored type was edited. The root component/function context is not
    /// registered with the reconciler, so it is migrated explicitly. Never
    /// throws out — a migration failure must not abort the reload render.
    /// </summary>
    private void MigrateHotReloadState()
    {
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

        // Hot Reload (spec 049). HotReloadService is source-shared from the
        // Windows framework and its [assembly: MetadataUpdateHandler] is active
        // in this assembly too, so `dotnet watch` edits land here identically:
        // atomic capture-and-clear gives at-most-once recovery per
        // UpdateApplication call.
        bool hotReloadRender = HotReloadService.ConsumeUpdatePending();

        // Open a tree-wide pass so the reconciler can recover hook-order changes
        // in non-root children (Reconciler.UpdateComponent reads WithinUpdatePass).
        using IDisposable? hotReloadPass = hotReloadRender
            ? HotReloadService.BeginUpdatePass()
            : null;

        // Value-swap hook cells whose stored type was edited, so adding or
        // removing a field on a record held in UseState/UseReducer preserves the
        // surviving values instead of resetting to the initializer.
        if (hotReloadRender)
            MigrateHotReloadState();

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

            Action rerender = () => RequestRender();

            if (_rootComponent is not null)
            {
                _rootComponent.Context.BeginRender(rerender);
                try { newTree = _rootComponent.Render(); }
                catch (HookOrderException ex) when (hotReloadRender)
                {
                    // An edit that adds/removes/reorders hooks changes the hook
                    // shape the context was built with. Under hot reload that is
                    // an expected consequence of the edit, not a user bug: drop
                    // the hook state and re-mount rather than falling through to
                    // the error overlay.
                    RecoverFromHookOrder(ex, _rootComponent.Context, "component");
                    return;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Component Render() threw");
                    ShowErrorFallback(ex);
                    return;
                }
            }
            else if (_rootRenderFunc is not null && _funcContext is not null)
            {
                _funcContext.BeginRender(rerender);
                try { newTree = _rootRenderFunc(_funcContext); }
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

            var capturedCurve = Interlocked.Exchange(ref _pendingAnimationCurve, null);
            if (capturedCurve is not null)
                AnimationScope.PushScope(capturedCurve);

            var capturedAmbient = Interlocked.Exchange(ref _pendingAmbientAnimation, null);
            using var ambientRestore = capturedAmbient is not null
                ? new Microsoft.UI.Reactor.Core.Internal.AnimationAmbient.Scope(capturedAmbient)
                : default;

            UIElement? newControl;
            try
            {
                newControl = _reconciler.Reconcile(_currentTree, newTree, _currentControl, rerender);
            }
            finally
            {
                if (capturedCurve is not null)
                    AnimationScope.PopScope();
            }

            if (newControl != _currentControl)
            {
                if (ContentTarget is not null)
                {
                    ContentTarget.Child = newControl;
                    AttachThemeListener(newControl);
                    OwningWindow?.OnContentAttached(newControl);
                }
                else
                {
                    // Mount through a themed root rather than assigning the tree
                    // to Window.Content directly. A Skia window paints nothing of
                    // its own, so anything the app leaves transparent — a
                    // NavigationView pane, the gutters around a smaller root —
                    // shows through as black. WinUI gets a system-painted window
                    // background for free; this is the Uno equivalent, and it
                    // follows the light/dark theme because the brush is resolved
                    // from the theme dictionary.
                    var root = EnsureWindowRoot();
                    root.Child = newControl;
                    AttachThemeListener(newControl);
                    // The XamlRoot (and hence RasterizationScale) only exists once
                    // content is attached — let the window (re)bind its DPI
                    // listener against the element that is actually in the tree.
                    OwningWindow?.OnContentAttached(root);
                }
            }

            _currentControl = newControl;
            _currentTree = newTree;

            _reconciler.FlushConnectedAnimations();

            double reconcileMs = _phaseSw.Elapsed.TotalMilliseconds;
            _phaseSw.Restart();

            if (_rootComponent is not null)
                _rootComponent.Context.FlushEffects();
            else
                _funcContext?.FlushEffects();

            double effectsMs = _phaseSw.Elapsed.TotalMilliseconds;

            Interlocked.Exchange(ref _lastRenderMs, treeBuildMs + reconcileMs + effectsMs);
            OnRenderComplete?.Invoke(treeBuildMs, reconcileMs, effectsMs);
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
        // The window root's brush was resolved from the theme dictionary at the
        // time it was applied, so re-resolve it for the new theme before the
        // re-render (a plain Background assignment is not a live ThemeResource).
        ApplyWindowRootBackground();
        RequestRender();
    }

    // Root container for the window-hosted case. A Skia window paints nothing of
    // its own, so without this every region the app leaves transparent renders
    // black. WinUI apps get this for free: a Page carries
    // ApplicationPageBackgroundThemeBrush from its template, and the stock Uno
    // template sets exactly that on its MainPage. Reactor hands the reconciled
    // tree straight to Window.Content, so the host has to supply it.
    private Microsoft.UI.Xaml.Controls.Border? _windowRoot;

    private Microsoft.UI.Xaml.Controls.Border EnsureWindowRoot()
    {
        if (_windowRoot is not null) return _windowRoot;

        // Built from markup rather than `new Border()` so Background is a real
        // {ThemeResource} binding. Resolving the brush in code (reading
        // Application.Current.Resources) is NOT theme-aware — it returns whichever
        // variant the app dictionary happens to hold, which produced a dark
        // background under a light theme.
        try
        {
            _windowRoot = (Microsoft.UI.Xaml.Controls.Border)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                """
                <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                        Background="{ThemeResource ApplicationPageBackgroundThemeBrush}" />
                """);
        }
        catch
        {
            // XamlReader unavailable on this head — fall back to an unpainted root
            // rather than failing the render.
            _windowRoot = new Microsoft.UI.Xaml.Controls.Border();
        }

        _window.Content = _windowRoot;
        return _windowRoot;
    }

    // The {ThemeResource} binding above re-resolves itself on theme change, so
    // nothing to re-apply here; kept as a seam for heads where the markup path
    // fell back to a plain Border.
    private void ApplyWindowRootBackground() { }

    /// <summary>True when no render is pending, in-flight, or queued.</summary>
    public bool IsIdle =>
        _disposed ||
        (Volatile.Read(ref _renderPending) == 0 && !_isRendering && !_needsRerender);

    /// <summary>Awaits until the render loop is idle. Used by test harnesses.</summary>
    public Task WaitForIdleAsync(int maxYields = 50)
    {
        if (_disposed) return Task.CompletedTask;
        if (_renderPending == 0 && !_isRendering && !_needsRerender)
            return Task.CompletedTask;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int yields = 0;
        void CheckIdle()
        {
            if (_disposed || (_renderPending == 0 && !_isRendering && !_needsRerender))
            {
                tcs.TrySetResult();
                return;
            }
            if (++yields > maxYields)
            {
                tcs.TrySetResult();
                return;
            }
            if (!_dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, CheckIdle))
                tcs.TrySetResult();
        }
        if (!_dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, CheckIdle))
            tcs.TrySetResult();
        return tcs.Task;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_themeListenerElement is not null)
                _themeListenerElement.ActualThemeChanged -= OnActualThemeChanged;
            _themeListenerElement = null;
        });

        _rootComponent?.Context.RunCleanups();
        _funcContext?.RunCleanups();

        _reconciler.Dispose();
        _rootComponent = null;
        _rootRenderFunc = null;
        _funcContext = null;
        _currentTree = null;
        _currentControl = null;

        if (ReferenceEquals(ReactorApp.ActiveHostInternal, this))
            ReactorApp.ActiveHostInternal = null;
    }

    private void ShowErrorFallback(Exception ex)
    {
        var errorPanel = Microsoft.UI.Reactor.Core.ErrorFallback.BuildPanel(ex);
        if (ContentTarget is not null)
            ContentTarget.Child = errorPanel;
        else
            EnsureWindowRoot().Child = errorPanel;
        _currentControl = errorPanel;
        _currentTree = null;
    }
}
