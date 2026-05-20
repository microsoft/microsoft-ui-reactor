namespace Microsoft.UI.Reactor.Navigation;

/// <summary>
/// Options for a single navigation action.
/// </summary>
public sealed record NavigateOptions
{
    /// <summary>
    /// Transition override for this navigation. Null uses the host default.
    /// </summary>
    public NavigationTransition? Transition { get; init; }

    /// <summary>
    /// When true (default), the current route is pushed onto the back stack.
    /// When false, the current route is replaced (no back stack entry created).
    /// </summary>
    public bool PushToBackStack { get; init; } = true;
}

/// <summary>
/// Event args fired after a successful navigation.
/// </summary>
public sealed record NavigationEventArgs<TRoute>(
    TRoute Route,
    TRoute PreviousRoute,
    NavigationMode Mode,
    NavigateOptions? Options = null
) where TRoute : notnull;

/// <summary>
/// Non-generic interface for NavigationHost reconciler integration.
/// Allows subscribing to route changes without knowing TRoute.
/// </summary>
internal interface INavigationHandle
{
    object CurrentRoute { get; }
    bool CanGoBack { get; }
    bool GoBack();
    event Action? RouteChanged;

    /// <summary>
    /// Lifecycle guard set by NavigationHost to invoke component-level
    /// <c>onNavigatingFrom</c> callbacks before stack mutation.
    /// </summary>
    Action<NavigatingFromContext>? LifecycleGuard { get; set; }

    /// <summary>
    /// Detaches all delegates from the underlying stack, breaking strong references
    /// to component render infrastructure. Called during unmount.
    /// </summary>
    void Detach();

    /// <summary>
    /// Per-navigation transition override set by <see cref="NavigationHandle{TRoute}.Navigate"/>
    /// when <see cref="NavigateOptions.Transition"/> is provided. Read and cleared by the reconciler
    /// during content swap to select the transition. Null means use host default.
    /// </summary>
    NavigationTransition? PendingTransitionOverride { get; set; }
}

/// <summary>
/// Public API for controlling navigation. Wraps a <see cref="NavigationStack{TRoute}"/>
/// with a safe, read-heavy interface. Obtained via <c>UseNavigation</c> hook.
/// </summary>
public sealed class NavigationHandle<TRoute> : INavigationHandle where TRoute : notnull
{
    private readonly NavigationStack<TRoute> _stack;

    internal NavigationHandle(NavigationStack<TRoute> stack)
    {
        _stack = stack;
    }

    /// <summary>
    /// Non-generic route change notification for NavigationHost.
    /// Fires after every successful navigation (alongside typed <see cref="Navigated"/> event).
    /// </summary>
    internal event Action? RouteChanged;

    event Action? INavigationHandle.RouteChanged
    {
        add => RouteChanged += value;
        remove => RouteChanged -= value;
    }

    object INavigationHandle.CurrentRoute => _stack.Current;
    bool INavigationHandle.CanGoBack => _stack.CanGoBack;
    bool INavigationHandle.GoBack() => GoBack();

    Action<NavigatingFromContext>? INavigationHandle.LifecycleGuard
    {
        get => _stack.LifecycleGuard;
        set => _stack.LifecycleGuard = value;
    }

    void INavigationHandle.Detach() => _stack.Detach();

    private NavigationTransition? _pendingTransitionOverride;
    NavigationTransition? INavigationHandle.PendingTransitionOverride
    {
        get => _pendingTransitionOverride;
        set => _pendingTransitionOverride = value;
    }

    /// <summary>The currently active route.</summary>
    public TRoute CurrentRoute => _stack.Current;

    /// <summary>True if there are entries in the back stack.</summary>
    public bool CanGoBack => _stack.CanGoBack;

    /// <summary>True if there are entries in the forward stack.</summary>
    public bool CanGoForward => _stack.CanGoForward;

    /// <summary>Readonly view of the back stack.</summary>
    public IReadOnlyList<TRoute> BackStack => _stack.BackStack;

    /// <summary>Readonly view of the forward stack.</summary>
    public IReadOnlyList<TRoute> ForwardStack => _stack.ForwardStack;

    /// <summary>Total depth: back stack count + 1 (current).</summary>
    public int Depth => _stack.Depth;

    /// <summary>
    /// Fired after every successful navigation with details about the transition.
    /// </summary>
    public event Action<NavigationEventArgs<TRoute>>? Navigated;

    /// <summary>
    /// Navigate to a new route. By default pushes the current route onto the back stack.
    /// If <see cref="NavigateOptions.PushToBackStack"/> is false, replaces the current route instead.
    /// </summary>
    public bool Navigate(TRoute route, NavigateOptions? options = null)
    {
        var previous = _stack.Current;
        _pendingTransitionOverride = options?.Transition;
        bool success;

        if (options is { PushToBackStack: false })
        {
            success = _stack.Replace(route);
            if (success)
            {
                NavigationDiagnostics.OnNavigationCompleted(previous!, route!, NavigationMode.Replace);
                Navigated?.Invoke(new NavigationEventArgs<TRoute>(route, previous, NavigationMode.Replace, options));
                RouteChanged?.Invoke();
            }
        }
        else
        {
            success = _stack.Push(route);
            if (success)
            {
                NavigationDiagnostics.OnNavigationCompleted(previous!, route!, NavigationMode.Push);
                Navigated?.Invoke(new NavigationEventArgs<TRoute>(route, previous, NavigationMode.Push, options));
                RouteChanged?.Invoke();
            }
        }

        if (!success)
            _pendingTransitionOverride = null;

        return success;
    }

    /// <summary>
    /// Go back to the previous route. Returns false if back stack is empty or guard cancels.
    /// </summary>
    public bool GoBack()
    {
        var previous = _stack.Current;
        if (!_stack.Pop())
            return false;

        Navigated?.Invoke(new NavigationEventArgs<TRoute>(_stack.Current, previous, NavigationMode.Pop));
        RouteChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Go forward to the next route in the forward stack. Returns false if forward stack is empty or guard cancels.
    /// </summary>
    public bool GoForward()
    {
        var previous = _stack.Current;
        if (!_stack.Forward())
            return false;

        Navigated?.Invoke(new NavigationEventArgs<TRoute>(_stack.Current, previous, NavigationMode.Forward));
        RouteChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Replace the current route without modifying back/forward stacks.
    /// </summary>
    public bool Replace(TRoute route)
    {
        var previous = _stack.Current;
        if (!_stack.Replace(route))
            return false;

        Navigated?.Invoke(new NavigationEventArgs<TRoute>(route, previous, NavigationMode.Replace));
        RouteChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Reset the entire stack to a single root route. Clears back and forward stacks.
    /// </summary>
    public bool Reset(TRoute route)
    {
        var previous = _stack.Current;
        if (!_stack.Reset(route))
            return false;

        Navigated?.Invoke(new NavigationEventArgs<TRoute>(route, previous, NavigationMode.Reset));
        RouteChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Pop entries from the back stack until the predicate matches.
    /// Returns false if no match or guard cancels.
    /// </summary>
    public bool PopTo(Func<TRoute, bool> predicate)
    {
        var previous = _stack.Current;
        if (!_stack.PopTo(predicate))
            return false;

        Navigated?.Invoke(new NavigationEventArgs<TRoute>(_stack.Current, previous, NavigationMode.Pop));
        RouteChanged?.Invoke();
        return true;
    }

    // ════════════════════════════════════════════════════════════════
    //  State snapshot / restore
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns a snapshot of the full navigation state — back stack, current route,
    /// and forward stack — as a plain POCO. Persist it however you like (JSON,
    /// MessagePack, hand-rolled binary): Reactor intentionally does not pick a
    /// serialization format for you.
    /// </summary>
    /// <remarks>
    /// For JSON persistence, declare a <c>JsonSerializerContext</c> covering
    /// <see cref="NavigationState{TRoute}"/> and your route type so the call is
    /// AOT-safe. For polymorphic route hierarchies, annotate the base route type
    /// with <c>[JsonPolymorphic]</c> and <c>[JsonDerivedType]</c>.
    /// </remarks>
    public NavigationState<TRoute> GetState() => new(
        // Use arrays so the IReadOnlyList<TRoute> exposed on the snapshot is
        // truly immutable in length — a caller can't cast back to IList<TRoute>
        // and mutate the captured state via Add/Remove.
        BackStack: _stack.BackStack.ToArray(),
        Current: _stack.Current,
        ForwardStack: _stack.ForwardStack.ToArray());

    /// <summary>
    /// Restores a previously captured <see cref="NavigationState{TRoute}"/>. Replaces
    /// the back stack, current route, and forward stack, then fires
    /// <see cref="Navigated"/> with <see cref="NavigationMode.Reset"/>.
    /// </summary>
    public void SetState(NavigationState<TRoute> state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Current is null)
            throw new ArgumentException("Navigation state must include a non-null Current route.", nameof(state));

        var previous = _stack.Current;
        _stack.RestoreState(state.BackStack, state.Current, state.ForwardStack);

        Navigated?.Invoke(new NavigationEventArgs<TRoute>(state.Current, previous, NavigationMode.Reset));
        RouteChanged?.Invoke();
    }
}
