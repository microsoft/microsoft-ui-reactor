using Microsoft.UI.Reactor.Hooks;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Base class for stateful components (like React class components, but using hooks).
/// Components hold a RenderContext that tracks their hook state across re-renders.
/// </summary>
public abstract class Component
{
    internal RenderContext Context { get; } = new();

    /// <summary>
    /// Override to describe the UI. Use UseState, UseEffect, etc. from the context.
    /// Must call hooks in the same order every render.
    /// </summary>
    public abstract Element Render();

    /// <summary>
    /// Controls whether this propless component should re-render when its parent re-renders.
    /// Default: false — propless components only re-render from their own state changes or context changes.
    /// Override and return true to always re-render when the parent re-renders.
    /// </summary>
    protected internal virtual bool ShouldUpdate() => false;

    // ── Hook convenience methods (delegate to Context) ─────────────

    protected (T Value, Action<T> Set) UseState<T>(T initialValue, bool threadSafe = false)
        => Context.UseState(initialValue, threadSafe);

    protected (T Value, Action<Func<T, T>> Update) UseReducer<T>(T initialValue, bool threadSafe = false)
        => Context.UseReducer(initialValue, threadSafe);

    /// <summary>
    /// Redux-style reducer: takes a reducer function (state, action) => newState.
    /// Returns (currentState, dispatch) where dispatch sends an action through the reducer.
    /// </summary>
    protected (TState Value, Action<TAction> Dispatch) UseReducer<TState, TAction>(
        Func<TState, TAction, TState> reducer, TState initialValue, bool threadSafe = false)
        => Context.UseReducer(reducer, initialValue, threadSafe);

    protected void UseEffect(Action effect, params object[] dependencies)
        => Context.UseEffect(effect, dependencies);

    protected void UseEffect(Func<Action> effectWithCleanup, params object[] dependencies)
        => Context.UseEffect(effectWithCleanup, dependencies);

    protected T UseMemo<T>(Func<T> factory, params object[] dependencies)
        => Context.UseMemo(factory, dependencies);

    protected Action UseCallback(Action callback, params object[] dependencies)
        => Context.UseCallback(callback, dependencies);

    protected Ref<T> UseRef<T>(T initialValue = default!)
        => Context.UseRef(initialValue);

    protected (double Width, double Height) UseWindowSize(Microsoft.UI.Xaml.Window window)
        => Context.UseWindowSize(window);

    /// <summary>
    /// Parameterless overload — resolves the host window from the current
    /// host's owning window. Returns <c>(0, 0)</c> outside a window.
    /// (spec 036 §5.2)
    /// </summary>
    protected (double Width, double Height) UseWindowSize()
        => Context.UseWindowSize();

    protected bool UseBreakpoint(Microsoft.UI.Xaml.Window window, double minWidth)
        => Context.UseBreakpoint(window, minWidth);

    /// <summary>
    /// Parameterless overload — resolves the host window. Returns
    /// <c>false</c> outside a window. (spec 036 §5.2)
    /// </summary>
    protected bool UseBreakpoint(double minWidth)
        => Context.UseBreakpoint(minWidth);

    /// <summary>
    /// Per-monitor DPI of the host window; re-renders on DPI change. Returns
    /// the system primary-monitor DPI when called outside a window.
    /// (spec 036 §5.2)
    /// </summary>
    protected uint UseDpi()
        => Context.UseDpi();

    /// <summary>
    /// Returns the host window or <c>null</c> outside a window. (spec 036 §7)
    /// </summary>
    protected Microsoft.UI.Reactor.ReactorWindow? UseWindow()
        => Context.UseWindow();

    /// <summary>Re-renders on window state changes. (spec 036 §7)</summary>
    protected Microsoft.UI.Reactor.WindowState UseWindowState()
        => Context.UseWindowState();

    /// <summary>Re-renders on window activation changes. (spec 036 §7)</summary>
    protected bool UseIsActive()
        => Context.UseIsActive();

    /// <summary>
    /// Register a synchronous "can the window close right now?" predicate.
    /// (spec 036 §7 / §13.4)
    /// </summary>
    protected void UseClosingGuard(Func<bool> canClose)
        => Context.UseClosingGuard(canClose);

    /// <summary>
    /// Open or reuse a secondary window keyed by <paramref name="key"/>. Stable
    /// identity across re-renders. (spec 036 §4.3)
    /// </summary>
    protected Microsoft.UI.Reactor.ReactorWindow? UseOpenWindow(
        Microsoft.UI.Reactor.WindowKey key,
        Microsoft.UI.Reactor.WindowSpec spec,
        Func<Component> factory)
        => Context.UseOpenWindow(key, spec, factory);

    /// <summary>
    /// Component mirror of <see cref="RenderContext.UseTrayIcon"/>. Opens
    /// (or reuses by key) a system-tray icon scoped to this component;
    /// closes on unmount. (spec 036 §11.4)
    /// </summary>
    protected Microsoft.UI.Reactor.ReactorTrayIcon? UseTrayIcon(Microsoft.UI.Reactor.TrayIconSpec spec)
        => Context.UseTrayIcon(spec);

    protected T UseObservableTree<T>(T source) where T : global::System.ComponentModel.INotifyPropertyChanged
        => Context.UseObservableTree(source);

    protected T UseObservable<T>(T source) where T : global::System.ComponentModel.INotifyPropertyChanged
        => Context.UseObservable(source);

    protected TProp UseObservableProperty<T, TProp>(T source, Func<T, TProp> selector, string propertyName)
        where T : global::System.ComponentModel.INotifyPropertyChanged
        => Context.UseObservableProperty(source, selector, propertyName);

    protected IReadOnlyList<T> UseCollection<T>(global::System.Collections.ObjectModel.ObservableCollection<T> collection)
        => Context.UseCollection(collection);

    protected ColorScheme UseColorScheme()
        => Context.UseColorScheme();

    protected bool UseIsDarkTheme()
        => Context.UseIsDarkTheme();

    /// <summary>
    /// Re-renders when the user's OS reduced-motion preference changes; returns
    /// the current value. Pair with <c>Animations.Animate(...)</c> to opt out
    /// of structural transitions when accessibility settings request it
    /// (WCAG 2.3.3 — spec 042 §6).
    /// </summary>
    protected bool UseReducedMotion()
        => Context.UseReducedMotion();

    protected Localization.IntlAccessor UseIntl()
        => Context.UseIntl();

    protected T UseContext<T>(Context<T> context)
        => Context.UseContext(context);

    protected Navigation.NavigationHandle<TRoute> UseNavigation<TRoute>(TRoute initial) where TRoute : notnull
        => Context.UseNavigation(initial);

    protected Navigation.NavigationHandle<TRoute> UseNavigation<TRoute>() where TRoute : notnull
        => Context.UseNavigation<TRoute>();

    protected void UseNavigationLifecycle(
        Action<Navigation.NavigatingToContext>? onNavigatingTo = null,
        Action<Navigation.NavigatedToContext>? onNavigatedTo = null,
        Action<Navigation.NavigatingFromContext>? onNavigatingFrom = null,
        Action<Navigation.NavigatedFromContext>? onNavigatedFrom = null)
        => Context.UseNavigationLifecycle(onNavigatingTo, onNavigatedTo, onNavigatingFrom, onNavigatedFrom);

    protected void UseSystemBackButton<TRoute>(
        Navigation.NavigationHandle<TRoute> nav,
        Microsoft.UI.Xaml.Window window) where TRoute : notnull
        => Context.UseSystemBackButton(nav, window);

    protected (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue)
        => Context.UsePersisted(key, initialValue);

    /// <summary>
    /// Persisted-state hook with explicit scope (spec 033 §2). Prefer this over
    /// the two-arg overload in new code so the call site documents whether the
    /// state is per-window or process-wide.
    /// </summary>
    protected (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue, PersistedScope scope)
        => Context.UsePersisted(key, initialValue, scope);

    protected Command UseCommand(Command command)
        => Context.UseCommand(command);

    protected Command<T> UseCommand<T>(Command<T> command)
        => Context.UseCommand(command);

    protected Hooks.AnnounceHandle UseAnnounce()
        => Context.UseAnnounce();

    protected bool UseHighContrast()
        => Context.UseHighContrast();

    protected string? UseHighContrastScheme()
        => Context.UseHighContrastScheme();

    protected AsyncValue<T> UseResource<T>(
        Func<CancellationToken, Task<T>> fetcher,
        object[] deps,
        Hooks.ResourceOptions? options = null)
        => Context.UseResource(fetcher, deps, options);

    protected AsyncValue<T> UseResource<T>(
        Func<CancellationToken, Task<T>> fetcher,
        QueryCache cache,
        object[] deps,
        Hooks.ResourceOptions? options = null)
        => Context.UseResource(fetcher, cache, deps, options);

    protected InfiniteResource<TItem> UseInfiniteResource<TItem, TCursor>(
        Func<TCursor?, CancellationToken, Task<Page<TItem, TCursor>>> fetchPage,
        object[] deps,
        InfiniteResourceOptions? options = null)
        => Context.UseInfiniteResource(fetchPage, deps, options);

    protected InfiniteResource<TItem> UseDataSource<TItem>(
        Data.IDataSource<TItem> source,
        Data.DataRequest request,
        InfiniteResourceOptions? options = null)
        => Data.DataSourceResourceExtensions.UseDataSource(Context, source, request, options);

    protected Hooks.Mutation<TInput, TResult> UseMutation<TInput, TResult>(
        Func<TInput, CancellationToken, Task<TResult>> mutator,
        Hooks.MutationOptions<TInput, TResult>? options = null)
        => Context.UseMutation(mutator, options);
}

/// <summary>
/// Interface for setting props without reflection.
/// </summary>
internal interface IPropsReceiver
{
    void SetProps(object props);
}

/// <summary>
/// Interface for comparing props without reflection (avoids per-reconcile GetMethod/Invoke overhead).
/// </summary>
internal interface IPropsComparable
{
    bool CompareProps(object? oldProps, object? newProps);
}

/// <summary>
/// Base class for components that receive typed props (e.g., navigation parameters).
/// Props are set by the host before rendering.
/// </summary>
public abstract class Component<TProps> : Component, IPropsReceiver, IPropsComparable
{
    /// <summary>
    /// The typed props passed to this component by its parent or host.
    /// </summary>
    public TProps Props { get; internal set; } = default!;

    void IPropsReceiver.SetProps(object props) => Props = (TProps)props;

    bool IPropsComparable.CompareProps(object? oldProps, object? newProps)
        => ShouldUpdate((TProps?)oldProps, (TProps?)newProps);

    /// <summary>
    /// Controls whether this component should re-render when its parent re-renders with new props.
    /// Default: structural equality via record Equals — record props get auto-comparison for free;
    /// class props need an Equals override.
    /// Override for custom comparison logic.
    /// </summary>
    protected internal virtual bool ShouldUpdate(TProps? oldProps, TProps? newProps)
        => !Equals(oldProps, newProps);
}
