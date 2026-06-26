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
/// <remarks>
/// <para><b>Thread safety (issue #234).</b> The mutating methods
/// (<see cref="Navigate"/>, <see cref="GoBack"/>, <see cref="GoForward"/>,
/// <see cref="Replace"/>, <see cref="Reset"/>, <see cref="PopTo"/>,
/// <see cref="SetState"/>) are thread-safe by default. When called from the UI
/// thread the cost is a single thread-id compare. When called off-thread (e.g.
/// from <c>Task.Run</c> or after <c>await … ConfigureAwait(false)</c>) the whole
/// mutation &#8212; stack edit, <see cref="Navigated"/>/<c>RouteChanged</c>
/// events, and the component re-render &#8212; is auto-marshaled onto the captured
/// UI dispatcher, mirroring the <c>UseState</c>/<c>UseReducer</c> contract from
/// issue #212. An off-thread call throws <see cref="InvalidOperationException"/>
/// loudly &#8212; rather than corrupting the back/forward stacks &#8212; in two
/// cases: when no UI dispatcher is available (unit-test / headless contexts), and
/// when the captured dispatcher refuses the enqueue (its <c>TryEnqueue</c> returns
/// <see langword="false"/>, e.g. once it has begun shutting down near window
/// close). The bool-returning mutators
/// return <see langword="true"/> when an off-thread call is accepted and
/// scheduled; the actual guard/empty-stack outcome is then resolved on the UI
/// thread. Off-thread callers must therefore not treat that <see langword="true"/>
/// as confirmation that the navigation actually happened &#8212; only that it was
/// scheduled. (On the UI thread the bool keeps its original meaning: whether the
/// navigation succeeded.)</para>
/// </remarks>
public sealed class NavigationHandle<TRoute> : INavigationHandle where TRoute : notnull
{
    private readonly NavigationStack<TRoute> _stack;

    // Captured at construction. UseNavigation creates the handle during render on
    // the UI thread, so this is the render/UI thread id used by the marshal gate.
    private readonly int _uiThreadId = global::System.Environment.CurrentManagedThreadId;

    internal NavigationHandle(NavigationStack<TRoute> stack)
    {
        _stack = stack;
    }

    // True when the caller is off the UI thread captured at construction. Kept as a
    // tiny helper so the per-mutator gate reads cleanly; the marshaling closure is
    // only allocated when this returns true (see UIThreadMarshal remarks).
    private bool IsOffUIThread => Core.UIThreadMarshal.IsOffUIThread(_uiThreadId);

    private bool MarshalOff(string operation, global::System.Action work)
        => Core.UIThreadMarshal.MarshalOffUIThread(_uiThreadId, $"NavigationHandle.{operation}", work);

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
    /// <returns>
    /// On the UI thread, <see langword="true"/> if the navigation succeeded, or
    /// <see langword="false"/> if a guard cancelled it. When called off the UI thread,
    /// <see langword="true"/> means the call was marshaled and scheduled onto the UI
    /// dispatcher &#8212; not that it succeeded; the real outcome is resolved later on the
    /// UI thread (see the type-level thread-safety remarks).
    /// </returns>
    public bool Navigate(TRoute route, NavigateOptions? options = null)
    {
        if (IsOffUIThread && MarshalOff(nameof(Navigate), () => Navigate(route, options))) return true;

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
    /// Go back to the previous route.
    /// </summary>
    /// <returns>
    /// On the UI thread, <see langword="true"/> if navigation succeeded, or
    /// <see langword="false"/> if the back stack is empty or a guard cancelled it. When
    /// called off the UI thread, <see langword="true"/> means the call was marshaled and
    /// scheduled onto the UI dispatcher &#8212; not that it succeeded; the empty-stack/guard
    /// outcome is resolved later on the UI thread (see the type-level thread-safety remarks).
    /// </returns>
    public bool GoBack()
    {
        if (IsOffUIThread && MarshalOff(nameof(GoBack), () => GoBack())) return true;

        var previous = _stack.Current;
        if (!_stack.Pop())
            return false;

        Navigated?.Invoke(new NavigationEventArgs<TRoute>(_stack.Current, previous, NavigationMode.Pop));
        RouteChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Go forward to the next route in the forward stack.
    /// </summary>
    /// <returns>
    /// On the UI thread, <see langword="true"/> if navigation succeeded, or
    /// <see langword="false"/> if the forward stack is empty or a guard cancelled it. When
    /// called off the UI thread, <see langword="true"/> means the call was marshaled and
    /// scheduled onto the UI dispatcher &#8212; not that it succeeded; the empty-stack/guard
    /// outcome is resolved later on the UI thread (see the type-level thread-safety remarks).
    /// </returns>
    public bool GoForward()
    {
        if (IsOffUIThread && MarshalOff(nameof(GoForward), () => GoForward())) return true;

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
    /// <returns>
    /// On the UI thread, <see langword="true"/> if the route was replaced, or
    /// <see langword="false"/> if a guard cancelled it. When called off the UI thread,
    /// <see langword="true"/> means the call was marshaled and scheduled onto the UI
    /// dispatcher &#8212; not that it succeeded; the real outcome is resolved later on the
    /// UI thread (see the type-level thread-safety remarks).
    /// </returns>
    public bool Replace(TRoute route)
    {
        if (IsOffUIThread && MarshalOff(nameof(Replace), () => Replace(route))) return true;

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
    /// <returns>
    /// On the UI thread, <see langword="true"/> if the stack was reset, or
    /// <see langword="false"/> if a guard cancelled it. When called off the UI thread,
    /// <see langword="true"/> means the call was marshaled and scheduled onto the UI
    /// dispatcher &#8212; not that it succeeded; the real outcome is resolved later on the
    /// UI thread (see the type-level thread-safety remarks).
    /// </returns>
    public bool Reset(TRoute route)
    {
        if (IsOffUIThread && MarshalOff(nameof(Reset), () => Reset(route))) return true;

        var previous = _stack.Current;
        if (!_stack.Reset(route))
            return false;

        Navigated?.Invoke(new NavigationEventArgs<TRoute>(route, previous, NavigationMode.Reset));
        RouteChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Pop entries from the back stack until the predicate matches.
    /// </summary>
    /// <returns>
    /// On the UI thread, <see langword="true"/> if a matching entry was popped to, or
    /// <see langword="false"/> if no back-stack entry matches the predicate or a guard
    /// cancelled it. When called off the UI thread, <see langword="true"/> means the call
    /// was marshaled and scheduled onto the UI dispatcher &#8212; not that it succeeded; the
    /// real outcome is resolved later on the UI thread (see the type-level thread-safety remarks).
    /// </returns>
    public bool PopTo(Func<TRoute, bool> predicate)
    {
        if (IsOffUIThread && MarshalOff(nameof(PopTo), () => PopTo(predicate))) return true;

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
        // Validate the state shape up front, on the caller's thread, so an invalid
        // snapshot fails fast at the call site rather than being marshaled and only
        // throwing later on the UI dispatcher where the caller can't observe it.
        if (state.Current is null)
            throw new ArgumentException("Navigation state must include a non-null Current route.", nameof(state));
        // RestoreState calls AddRange on both lists; a null list would otherwise throw deep on
        // the UI dispatcher after marshaling, where the caller can't observe it. Validate here.
        if (state.BackStack is null)
            throw new ArgumentException("Navigation state must include a non-null BackStack.", nameof(state));
        if (state.ForwardStack is null)
            throw new ArgumentException("Navigation state must include a non-null ForwardStack.", nameof(state));
        if (IsOffUIThread)
        {
            // Freeze the caller-supplied lists into arrays before the dispatcher hop. The
            // marshaled restore runs later on the UI thread; without this copy a caller could
            // mutate their original List<T> in the meantime and the applied history would no
            // longer match the snapshot validated here. Symmetric with GetState, which hands
            // out arrays so a snapshot can't alias — or be aliased into — the live stack.
            var frozen = new NavigationState<TRoute>(
                state.BackStack.ToArray(), state.Current, state.ForwardStack.ToArray());
            if (MarshalOff(nameof(SetState), () => SetState(frozen))) return;
        }

        var previous = _stack.Current;
        _stack.RestoreState(state.BackStack, state.Current, state.ForwardStack);

        Navigated?.Invoke(new NavigationEventArgs<TRoute>(state.Current, previous, NavigationMode.Reset));
        RouteChanged?.Invoke();
    }
}
