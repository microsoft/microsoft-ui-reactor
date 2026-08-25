using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Hooks;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Passed to function components and provides access to hooks.
/// Each component instance gets its own RenderContext which tracks hook call order.
/// </summary>
public sealed class RenderContext
{
    private readonly List<HookState> _hooks = new(8);
    private int _hookIndex;
    private Action? _requestRerender;
    private ContextScope? _contextScope;
    private int _uiThreadId;

    /// <summary>
    /// Time source used by time-based hooks (currently <see cref="UseCommand(Command)"/>'s
    /// <see cref="Command.DebounceMs"/> window). Defaults to <see cref="TimeProvider.System"/>;
    /// tests inject a fake provider so debounce timing isn't wall-clock-flaky.
    /// </summary>
    internal global::System.TimeProvider TimeProvider { get; set; } = global::System.TimeProvider.System;

    internal void BeginRender(Action requestRerender)
    {
        _hookIndex = 0;
        _requestRerender = requestRerender;
        _uiThreadId = Environment.CurrentManagedThreadId;
    }

    internal void BeginRender(Action requestRerender, ContextScope contextScope)
    {
        _hookIndex = 0;
        _requestRerender = requestRerender;
        _contextScope = contextScope;
        _uiThreadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>
    /// Auto-marshals <paramref name="work"/> to the captured UI dispatcher when the
    /// caller is not on the UI thread. Returns <c>true</c> if the work was
    /// scheduled cross-thread (caller should return without executing inline).
    /// Returns <c>false</c> when the caller is already on the UI thread (hot path).
    /// <para>
    /// When no <see cref="Microsoft.UI.Reactor.ReactorApp.UIDispatcher"/> has been
    /// captured (unit-test / headless render contexts) or when the dispatcher has
    /// already begun shutting down, a cross-thread call throws instead of silently
    /// racing or dropping the update. The dispatcher is captured during host
    /// bootstrap (<c>ReactorApp</c>'s <c>OnLaunched</c> for packaged apps, or
    /// <c>ReactorHost</c>/<c>ReactorHostControl</c> initialization for embedded /
    /// test scenarios), so production code reaches this method only after at
    /// least one render has happened.
    /// </para>
    /// </summary>
    // <snippet:ui-thread-invariant>
    private bool MarshalIfOffUIThread(string hookName, Action work)
    {
        // Hot path — same thread that ran BeginRender. ~1ns TLS read + cmp + branch.
        if (Environment.CurrentManagedThreadId == _uiThreadId) return false;
        // </snippet:ui-thread-invariant>

        // Off-thread: funnel through the shared marshal-or-throw primitive so this
        // and NavigationHandle's mutator gate (issue #234) share one implementation.
        // The exact diagnostic wording — including the "threadSafe: true" remedy — is
        // preserved here because callers/tests depend on it.
        var dq = Microsoft.UI.Reactor.ReactorApp.UIDispatcher;
        return UIThreadMarshal.EnqueueOrThrow(
            dq is null ? null : w => dq.TryEnqueue(w.Invoke),
            work,
            () =>
                // Test/headless context with no captured UI dispatcher AND off-thread.
                // The legacy [Conditional("DEBUG")] assert at this spot swallowed silently
                // in RELEASE; surface loudly in both flavors so the call is visible.
                $"{hookName} setter was called from thread {Environment.CurrentManagedThreadId}, " +
                $"but the captured UI thread is {_uiThreadId}, and no UI dispatcher is " +
                $"available to marshal the call. Run the setter on the UI thread, " +
                $"or pass threadSafe: true to the hook.",
            () =>
                // TryEnqueue returns false when the dispatcher has begun shutting down
                // (queue closed, owning thread exiting). Silently swallowing that case
                // would lose the state update with no diagnostic; throw so the caller
                // sees the same loud failure mode as the no-dispatcher path.
                $"{hookName} setter was called from thread {Environment.CurrentManagedThreadId}, " +
                $"but the UI dispatcher refused the marshaled call (TryEnqueue returned " +
                $"false — typically because the dispatcher is shutting down). The state " +
                $"update was dropped. Stop scheduling background setters past window/app " +
                $"shutdown (cancel the producing task in the effect cleanup).");
    }

    /// <summary>
    /// DEBUG ONLY: Directly set a UseState hook value by index and trigger re-render.
    /// Used for testing state changes without event handlers.
    /// </summary>
    internal void UseStateSetterByIndex<T>(int index, T newValue)
    {
        if (index < _hooks.Count && _hooks[index] is ValueHookState<T> hook)
        {
            hook.Value = newValue;
            _requestRerender?.Invoke();
        }
    }

    /// <summary>
    /// Declares a piece of state. Returns (currentValue, setter).
    /// Must be called in the same order every render (just like React hooks).
    /// <para>
    /// When <paramref name="threadSafe"/> is true, reads and writes are synchronized
    /// with a per-hook lock so concurrent setter calls from many threads serialize.
    /// When false (default), cross-thread setter calls are auto-marshaled onto the
    /// captured UI dispatcher — the write and the rerender both run on the UI thread,
    /// so background-thread setters from <c>Task.Run</c>, <c>PeriodicTimer</c>, or
    /// callbacks after <c>await … ConfigureAwait(false)</c> work correctly without
    /// any extra opt-in. Use <paramref name="threadSafe"/>: <c>true</c> when you need
    /// many concurrent setters to apply in-place (i.e., without an intervening UI
    /// thread hop) or when the setter result must be visible to its caller before
    /// the next UI tick.
    /// </para>
    /// </summary>
    // <snippet:use-state-slot>
    public (T Value, Action<T> Set) UseState<T>(T initialValue, bool threadSafe = false)
    {
        if (_hookIndex >= _hooks.Count)
        {
            _hooks.Add(new ValueHookState<T>(initialValue, threadSafe));
        }

        var currentIndex = _hookIndex;
        _hookIndex++;

        if (_hooks[currentIndex] is not ValueHookState<T> hook)
            throw new HookOrderException(
                $"Hook at index {currentIndex} is {_hooks[currentIndex].GetType().Name}, expected ValueHookState<{typeof(T).Name}> (UseState). " +
                "Hooks must be called in the same order every render.");
        // </snippet:use-state-slot>

        T current;
        if (hook.ThreadSafe)
            lock (hook.Lock!) { current = hook.Value; }
        else
            current = hook.Value;

        // Issue #659 (#43): reuse the ref-stable setter built on the first
        // render. The cached delegate captures the (identity-stable) hook cell
        // and `this`, so it always reads the live value and the current
        // re-render callback — identical observable behavior, zero per-render
        // closure allocation. The kind guard (review LOW) re-materializes if the
        // slot was (invalidly) used by a different hook flavour with a
        // coincidentally same-typed delegate.
        if (hook.SetterKind != SetterKindState || hook.Setter is not Action<T> setter)
        {
            setter = MakeStateSetter(hook);
            hook.Setter = setter;
            hook.SetterKind = SetterKindState;
        }

        return (current, setter);
    }

    private const byte SetterKindState = 1;
    private const byte SetterKindReducer = 2;
    private const byte SetterKindDispatch = 3;

    // <snippet:set-state>
    private Action<T> MakeStateSetter<T>(ValueHookState<T> h)
    {
        void Setter(T newValue)
        {
            bool changed;
            if (h.ThreadSafe)
            {
                lock (h.Lock!)
                {
                    changed = !EqualityComparer<T>.Default.Equals(h.Value, newValue);
                    if (changed) h.Value = newValue;
                }
                if (Diagnostics.ReactorEventSource.Log.IsEnabled(
                        global::System.Diagnostics.Tracing.EventLevel.Verbose,
                        Diagnostics.ReactorEventSource.Keywords.State))
                    Diagnostics.ReactorEventSource.Log.StateChange("UseState", typeof(T).Name, changed);
                if (changed) _requestRerender?.Invoke();
            }
            else
            {
                if (MarshalIfOffUIThread("UseState", () => Setter(newValue))) return;
                changed = !EqualityComparer<T>.Default.Equals(h.Value, newValue);
                if (changed) h.Value = newValue;
                if (Diagnostics.ReactorEventSource.Log.IsEnabled(
                        global::System.Diagnostics.Tracing.EventLevel.Verbose,
                        Diagnostics.ReactorEventSource.Keywords.State))
                    Diagnostics.ReactorEventSource.Log.StateChange("UseState", typeof(T).Name, changed);
                if (changed) _requestRerender?.Invoke();
            }
        }
        return Setter;
    }
    // </snippet:set-state>

    /// <summary>
    /// Declares a piece of state with a functional updater variant.
    /// The updater receives the previous value and returns the next.
    /// Cross-thread updater calls are auto-marshaled onto the captured UI dispatcher
    /// (same semantics as <see cref="UseState{T}(T, bool)"/>); pass
    /// <paramref name="threadSafe"/>: <c>true</c> for locked in-place updates that
    /// serialize many concurrent writers without an intervening UI tick.
    /// </summary>
    public (T Value, Action<Func<T, T>> Update) UseReducer<T>(T initialValue, bool threadSafe = false)
    {
        if (_hookIndex >= _hooks.Count)
        {
            _hooks.Add(new ValueHookState<T>(initialValue, threadSafe));
        }

        var currentIndex = _hookIndex;
        _hookIndex++;

        if (_hooks[currentIndex] is not ValueHookState<T> hook)
            throw new HookOrderException(
                $"Hook at index {currentIndex} is {_hooks[currentIndex].GetType().Name}, expected ValueHookState<{typeof(T).Name}> (UseReducer). " +
                "Hooks must be called in the same order every render.");

        T current;
        if (hook.ThreadSafe)
            lock (hook.Lock!) { current = hook.Value; }
        else
            current = hook.Value;

        // Issue #659 (#44): reuse the ref-stable updater. The updater receives
        // its reducer as a call-time argument (not a captured render value), so
        // caching it has no stale-capture risk.
        if (hook.SetterKind != SetterKindReducer || hook.Setter is not Action<Func<T, T>> updater)
        {
            updater = MakeReducerUpdater(hook);
            hook.Setter = updater;
            hook.SetterKind = SetterKindReducer;
        }

        return (current, updater);
    }

    private Action<Func<T, T>> MakeReducerUpdater<T>(ValueHookState<T> h)
    {
        void Updater(Func<T, T> reducer)
        {
            bool changed;
            if (h.ThreadSafe)
            {
                lock (h.Lock!)
                {
                    var prev = h.Value;
                    var next = reducer(prev);
                    changed = !EqualityComparer<T>.Default.Equals(prev, next);
                    if (changed) h.Value = next;
                }
                if (Diagnostics.ReactorEventSource.Log.IsEnabled(
                        global::System.Diagnostics.Tracing.EventLevel.Verbose,
                        Diagnostics.ReactorEventSource.Keywords.State))
                    Diagnostics.ReactorEventSource.Log.StateChange("UseReducer", typeof(T).Name, changed);
                if (changed) _requestRerender?.Invoke();
            }
            else
            {
                if (MarshalIfOffUIThread("UseReducer", () => Updater(reducer))) return;
                var prev = h.Value;
                var next = reducer(prev);
                changed = !EqualityComparer<T>.Default.Equals(prev, next);
                if (changed) h.Value = next;
                if (Diagnostics.ReactorEventSource.Log.IsEnabled(
                        global::System.Diagnostics.Tracing.EventLevel.Verbose,
                        Diagnostics.ReactorEventSource.Keywords.State))
                    Diagnostics.ReactorEventSource.Log.StateChange("UseReducer", typeof(T).Name, changed);
                if (changed) _requestRerender?.Invoke();
            }
        }
        return Updater;
    }

    /// <summary>
    /// Declares a piece of state managed by a reducer function (like Redux).
    /// The reducer takes (currentState, action) and returns the next state.
    /// Returns (currentState, dispatch) where dispatch sends an action through the reducer.
    /// Cross-thread dispatch calls are auto-marshaled onto the captured UI dispatcher
    /// (same semantics as <see cref="UseState{T}(T, bool)"/>); pass
    /// <paramref name="threadSafe"/>: <c>true</c> for locked in-place dispatch that
    /// serializes concurrent writers without an intervening UI tick.
    /// </summary>
    public (TState Value, Action<TAction> Dispatch) UseReducer<TState, TAction>(
        Func<TState, TAction, TState> reducer, TState initialValue, bool threadSafe = false)
    {
        if (_hookIndex >= _hooks.Count)
        {
            _hooks.Add(new ValueHookState<TState>(initialValue, threadSafe));
        }

        var currentIndex = _hookIndex;
        _hookIndex++;

        if (_hooks[currentIndex] is not ValueHookState<TState> hook)
            throw new HookOrderException(
                $"Hook at index {currentIndex} is {_hooks[currentIndex].GetType().Name}, expected ValueHookState<{typeof(TState).Name}> (UseReducer). " +
                "Hooks must be called in the same order every render.");

        TState current;
        if (hook.ThreadSafe)
            lock (hook.Lock!) { current = hook.Value; }
        else
            current = hook.Value;

        // Issue #659 (#44): the dispatch closes over the reducer, which is a
        // per-render argument. Store the latest reducer on the cell every render
        // and have the cached dispatch read it, so dispatch identity is stable
        // across renders while still using the current render's reducer.
        hook.Reducer = reducer;
        if (hook.SetterKind != SetterKindDispatch || hook.Setter is not Action<TAction> dispatch)
        {
            dispatch = MakeReducerDispatch<TState, TAction>(hook);
            hook.Setter = dispatch;
            hook.SetterKind = SetterKindDispatch;
        }

        return (current, dispatch);
    }

    private Action<TAction> MakeReducerDispatch<TState, TAction>(ValueHookState<TState> h)
    {
        void Dispatch(TAction action)
        {
            var reducer = (Func<TState, TAction, TState>)h.Reducer!;
            if (h.ThreadSafe)
            {
                bool changed;
                lock (h.Lock!)
                {
                    var prev = h.Value;
                    var next = reducer(prev, action);
                    changed = !EqualityComparer<TState>.Default.Equals(prev, next);
                    if (changed) h.Value = next;
                }
                if (changed) _requestRerender?.Invoke();
            }
            else
            {
                if (MarshalIfOffUIThread("UseReducer", () => Dispatch(action))) return;
                var prev = h.Value;
                var next = reducer(prev, action);
                if (!EqualityComparer<TState>.Default.Equals(prev, next))
                {
                    h.Value = next;
                    _requestRerender?.Invoke();
                }
            }
        }
        return Dispatch;
    }

    /// <summary>
    /// Runs a side effect after render. The effect re-runs when any dependency changes.
    /// Pass an empty array for "run once on mount" semantics.
    /// Returns a cleanup action that runs before the next effect or on unmount.
    /// </summary>
    // <snippet:effect-schedule>
    public void UseEffect(Action effect, params object[] dependencies)
    {
        var hook = AcquireEffectSlot();
        // Snapshot the deps on store (SnapshotDeps): a caller can pass — and then
        // reuse and mutate in place — the same array instance across renders, so the
        // stored copy must be isolated or DepsEqual would alias prev/next and skip a
        // real change (Issue #659 review #3). Only the deps-CHANGED path stores, so
        // this adds no steady-state allocation.
        if (hook.Dependencies is null || !DepsEqual(hook.Dependencies, dependencies))
            ScheduleEffect(hook, effect, null, SnapshotDeps(dependencies));
    }
    // </snippet:effect-schedule>

    /// <summary>
    /// Like UseEffect but the effect returns a cleanup function.
    /// </summary>
    public void UseEffect(Func<Action> effectWithCleanup, params object[] dependencies)
    {
        var hook = AcquireEffectSlot();
        if (hook.Dependencies is null || !DepsEqual(hook.Dependencies, dependencies))
            ScheduleEffect(hook, null, effectWithCleanup, SnapshotDeps(dependencies));
    }

    // #688: arity 1-3 overloads so the common "a couple of deps" call avoids the
    // params-array allocation AND the value-type boxing on the steady-state
    // (deps-unchanged) path. Storage stays object[] for hot-reload / snapshot /
    // DepsEqual compatibility; the array is only allocated when deps actually
    // change. Comparison unboxes the stored value (no new allocation) and uses
    // the typed EqualityComparer so the incoming dep is never boxed.
    /// <summary>
    /// Single-dependency <c>UseEffect</c> overload that avoids the
    /// <c>params object[]</c> allocation (and value-type boxing) on the
    /// deps-unchanged path. Semantically identical to the params overload called
    /// with one dependency: the effect re-runs only when <paramref name="d1"/>
    /// changes.
    /// </summary>
    /// <remarks>
    /// If <paramref name="d1"/>'s compile-time type is an array of reference types
    /// (e.g. <c>string[]</c>), it is treated as a dependency <em>list</em> and compared
    /// element-wise — matching the <c>params object[]</c> overload — not as a single
    /// reference-compared value. A dependency whose static type is not an array is
    /// always compared as one value, even if its runtime value happens to be an array.
    /// </remarks>
    public void UseEffect<T1>(Action effect, T1 d1)
    {
        var hook = AcquireEffectSlot();
        // A lone dependency whose compile-time type is a reference-type array (e.g. a
        // covariant string[]) binds this generic but historically went through the
        // params overload and was compared element-wise — preserve that so an array
        // re-allocated each render with equal contents does not spuriously re-run.
        if (AsParamsArrayDep(d1) is { } arr)
        {
            if (hook.Dependencies is null || !DepsEqual(hook.Dependencies, arr))
                ScheduleEffect(hook, effect, null, SnapshotDeps(arr));
            return;
        }
        if (!DepsEqual1(hook.Dependencies, d1)) ScheduleEffect(hook, effect, null, PackDeps(d1));
    }

    /// <summary>
    /// Two-dependency <c>UseEffect</c> overload that avoids the
    /// <c>params object[]</c> allocation on the deps-unchanged path. Re-runs when
    /// either dependency changes.
    /// </summary>
    public void UseEffect<T1, T2>(Action effect, T1 d1, T2 d2)
    {
        var hook = AcquireEffectSlot();
        if (!DepsEqual2(hook.Dependencies, d1, d2)) ScheduleEffect(hook, effect, null, PackDeps(d1, d2));
    }

    /// <summary>
    /// Three-dependency <c>UseEffect</c> overload that avoids the
    /// <c>params object[]</c> allocation on the deps-unchanged path. Re-runs when
    /// any dependency changes.
    /// </summary>
    public void UseEffect<T1, T2, T3>(Action effect, T1 d1, T2 d2, T3 d3)
    {
        var hook = AcquireEffectSlot();
        if (!DepsEqual3(hook.Dependencies, d1, d2, d3)) ScheduleEffect(hook, effect, null, PackDeps(d1, d2, d3));
    }

    /// <summary>
    /// Single-dependency cleanup-flavor <c>UseEffect</c> overload that avoids the
    /// <c>params object[]</c> allocation on the deps-unchanged path. Semantically
    /// identical to the params overload called with one dependency.
    /// </summary>
    /// <remarks>
    /// If <paramref name="d1"/>'s compile-time type is an array of reference types
    /// (e.g. <c>string[]</c>), it is treated as a dependency <em>list</em> and compared
    /// element-wise — matching the <c>params object[]</c> overload — not as a single
    /// reference-compared value. A dependency whose static type is not an array is
    /// always compared as one value, even if its runtime value happens to be an array.
    /// </remarks>
    public void UseEffect<T1>(Func<Action> effectWithCleanup, T1 d1)
    {
        var hook = AcquireEffectSlot();
        if (AsParamsArrayDep(d1) is { } arr)
        {
            if (hook.Dependencies is null || !DepsEqual(hook.Dependencies, arr))
                ScheduleEffect(hook, null, effectWithCleanup, SnapshotDeps(arr));
            return;
        }
        if (!DepsEqual1(hook.Dependencies, d1)) ScheduleEffect(hook, null, effectWithCleanup, PackDeps(d1));
    }

    /// <summary>
    /// Two-dependency cleanup-flavor <c>UseEffect</c> overload that avoids the
    /// <c>params object[]</c> allocation on the deps-unchanged path.
    /// </summary>
    public void UseEffect<T1, T2>(Func<Action> effectWithCleanup, T1 d1, T2 d2)
    {
        var hook = AcquireEffectSlot();
        if (!DepsEqual2(hook.Dependencies, d1, d2)) ScheduleEffect(hook, null, effectWithCleanup, PackDeps(d1, d2));
    }

    /// <summary>
    /// Three-dependency cleanup-flavor <c>UseEffect</c> overload that avoids the
    /// <c>params object[]</c> allocation on the deps-unchanged path.
    /// </summary>
    public void UseEffect<T1, T2, T3>(Func<Action> effectWithCleanup, T1 d1, T2 d2, T3 d3)
    {
        var hook = AcquireEffectSlot();
        if (!DepsEqual3(hook.Dependencies, d1, d2, d3)) ScheduleEffect(hook, null, effectWithCleanup, PackDeps(d1, d2, d3));
    }

    /// <summary>
    /// Memoizes a computed value, recomputing only when dependencies change.
    /// </summary>
    public T UseMemo<T>(Func<T> factory, params object[] dependencies)
    {
        var hook = AcquireMemoSlot<T>();
        if (hook.Dependencies is null || !DepsEqual(hook.Dependencies, dependencies))
        {
            hook.Value = factory();
            // Issue #659 review (#3): snapshot deps (see UseEffect).
            hook.Dependencies = SnapshotDeps(dependencies);
        }
        return hook.Value;
    }

    // #688: arity overloads mirror UseEffect — no params array / boxing on the
    // deps-unchanged path; factory only runs (and array only allocates) on change.

    /// <summary>
    /// Single-dependency <c>UseMemo</c> overload that avoids the
    /// <c>params object[]</c> allocation (and value-type boxing) on the
    /// deps-unchanged path. Recomputes only when <paramref name="d1"/> changes.
    /// </summary>
    /// <remarks>
    /// If <paramref name="d1"/>'s compile-time type is an array of reference types
    /// (e.g. <c>string[]</c>), it is treated as a dependency <em>list</em> and compared
    /// element-wise — matching the <c>params object[]</c> overload — not as a single
    /// reference-compared value. A dependency whose static type is not an array is
    /// always compared as one value, even if its runtime value happens to be an array.
    /// </remarks>
    public T UseMemo<T, T1>(Func<T> factory, T1 d1)
    {
        var hook = AcquireMemoSlot<T>();
        // See UseEffect<T1>: a compile-time array dep keeps element-wise semantics.
        if (AsParamsArrayDep(d1) is { } arr)
        {
            if (hook.Dependencies is null || !DepsEqual(hook.Dependencies, arr))
            {
                hook.Value = factory();
                hook.Dependencies = SnapshotDeps(arr);
            }
            return hook.Value;
        }
        if (!DepsEqual1(hook.Dependencies, d1))
        {
            hook.Value = factory();
            hook.Dependencies = PackDeps(d1);
        }
        return hook.Value;
    }

    /// <summary>
    /// Two-dependency <c>UseMemo</c> overload that avoids the
    /// <c>params object[]</c> allocation on the deps-unchanged path.
    /// </summary>
    public T UseMemo<T, T1, T2>(Func<T> factory, T1 d1, T2 d2)
    {
        var hook = AcquireMemoSlot<T>();
        if (!DepsEqual2(hook.Dependencies, d1, d2))
        {
            hook.Value = factory();
            hook.Dependencies = PackDeps(d1, d2);
        }
        return hook.Value;
    }

    /// <summary>
    /// Three-dependency <c>UseMemo</c> overload that avoids the
    /// <c>params object[]</c> allocation on the deps-unchanged path.
    /// </summary>
    public T UseMemo<T, T1, T2, T3>(Func<T> factory, T1 d1, T2 d2, T3 d3)
    {
        var hook = AcquireMemoSlot<T>();
        if (!DepsEqual3(hook.Dependencies, d1, d2, d3))
        {
            hook.Value = factory();
            hook.Dependencies = PackDeps(d1, d2, d3);
        }
        return hook.Value;
    }

    /// <summary>
    /// Returns a stable callback reference that doesn't change between renders.
    /// </summary>
    public Action UseCallback(Action callback, params object[] dependencies)
    {
        // Issue #659 (#46): store the callback directly in a memo slot instead of
        // UseMemo(() => callback, deps). The old form allocated a `() => callback`
        // wrapper closure every render (even when deps were unchanged, because the
        // factory is materialized before the deps short-circuit). The slot is still
        // MemoHookState<Action> so hook-order checks / devtools labels are unchanged.
        var hook = AcquireMemoSlot<Action>();
        if (hook.Dependencies is null || !DepsEqual(hook.Dependencies, dependencies))
        {
            hook.Value = callback;
            hook.Dependencies = SnapshotDeps(dependencies);
        }
        return hook.Value;
    }

    /// <summary>
    /// Single-dependency <c>UseCallback</c> overload that avoids the
    /// <c>params object[]</c> allocation (and value-type boxing) on the
    /// deps-unchanged path. Returns a stable reference until <paramref name="d1"/>
    /// changes.
    /// </summary>
    /// <remarks>
    /// If <paramref name="d1"/>'s compile-time type is an array of reference types
    /// (e.g. <c>string[]</c>), it is treated as a dependency <em>list</em> and compared
    /// element-wise — matching the <c>params object[]</c> overload — not as a single
    /// reference-compared value. A dependency whose static type is not an array is
    /// always compared as one value, even if its runtime value happens to be an array.
    /// </remarks>
    public Action UseCallback<T1>(Action callback, T1 d1)
    {
        var hook = AcquireMemoSlot<Action>();
        // See UseEffect<T1>: a compile-time array dep keeps element-wise semantics.
        if (AsParamsArrayDep(d1) is { } arr)
        {
            if (hook.Dependencies is null || !DepsEqual(hook.Dependencies, arr))
            {
                hook.Value = callback;
                hook.Dependencies = SnapshotDeps(arr);
            }
            return hook.Value;
        }
        if (!DepsEqual1(hook.Dependencies, d1))
        {
            hook.Value = callback;
            hook.Dependencies = PackDeps(d1);
        }
        return hook.Value;
    }

    /// <summary>
    /// Two-dependency <c>UseCallback</c> overload that avoids the
    /// <c>params object[]</c> allocation on the deps-unchanged path.
    /// </summary>
    public Action UseCallback<T1, T2>(Action callback, T1 d1, T2 d2)
    {
        var hook = AcquireMemoSlot<Action>();
        if (!DepsEqual2(hook.Dependencies, d1, d2))
        {
            hook.Value = callback;
            hook.Dependencies = PackDeps(d1, d2);
        }
        return hook.Value;
    }

    /// <summary>
    /// Three-dependency <c>UseCallback</c> overload that avoids the
    /// <c>params object[]</c> allocation on the deps-unchanged path.
    /// </summary>
    public Action UseCallback<T1, T2, T3>(Action callback, T1 d1, T2 d2, T3 d3)
    {
        var hook = AcquireMemoSlot<Action>();
        if (!DepsEqual3(hook.Dependencies, d1, d2, d3))
        {
            hook.Value = callback;
            hook.Dependencies = PackDeps(d1, d2, d3);
        }
        return hook.Value;
    }

    // ── Hook-slot + deps helpers (shared by the effect / memo / callback hooks) ──

    private EffectHookState AcquireEffectSlot()
    {
        if (_hookIndex >= _hooks.Count)
            _hooks.Add(new EffectHookState { Dependencies = null });

        if (_hooks[_hookIndex] is not EffectHookState hook)
            throw new HookOrderException(
                $"Hook at index {_hookIndex} is {_hooks[_hookIndex].GetType().Name}, expected EffectHookState. " +
                "Hooks must be called in the same order every render.");
        _hookIndex++;
        return hook;
    }

    private MemoHookState<T> AcquireMemoSlot<T>()
    {
        if (_hookIndex >= _hooks.Count)
            _hooks.Add(new MemoHookState<T> { Dependencies = null });

        if (_hooks[_hookIndex] is not MemoHookState<T> hook)
            throw new HookOrderException(
                $"Hook at index {_hookIndex} is {_hooks[_hookIndex].GetType().Name}, expected MemoHookState<{typeof(T).Name}>. " +
                "Hooks must be called in the same order every render.");
        _hookIndex++;
        return hook;
    }

    // The caller is responsible for passing an isolated deps array — either a
    // freshly packed array (PackDeps, arity overloads) or a defensive clone
    // (SnapshotDeps, params/AsParamsArrayDep paths). Storing it by reference here
    // is therefore safe: no caller-owned array is ever aliased onto the hook slot,
    // so an in-place mutation of a reused caller array cannot make prev/next deps
    // alias and wrongly short-circuit DepsEqual.
    private static void ScheduleEffect(EffectHookState hook, Action? effect, Func<Action>? withCleanup, object[] dependencies)
    {
        hook.PendingCleanup = hook.Cleanup;
        hook.Cleanup = null;
        hook.Dependencies = dependencies;
        hook.Effect = effect;
        hook.EffectWithCleanup = withCleanup;
        hook.Pending = true;
    }

    private static object[] PackDeps<T1>(T1 d1) => new object[] { d1! };
    private static object[] PackDeps<T1, T2>(T1 d1, T2 d2) => new object[] { d1!, d2! };
    private static object[] PackDeps<T1, T2, T3>(T1 d1, T2 d2, T3 d3) => new object[] { d1!, d2!, d3! };

    // Snapshot a caller-supplied deps array before persisting it on a hook slot.
    // Callers can pass — and then reuse and mutate in place — the same array
    // instance across renders (a params target, or a covariant array routed through
    // AsParamsArrayDep). Storing it by reference would alias prev/next so an in-place
    // mutation is invisible to DepsEqual and would wrongly skip the effect or memo
    // recompute. Cloning isolates the stored copy. This runs only on the
    // deps-CHANGED store path (the deps-equal path short-circuits before storing), so
    // it adds no steady-state allocation; the empty "run once" case reuses the shared
    // empty array. Always returns a genuine object[] even when the source is a
    // covariant array (e.g. string[]).
    private static object[] SnapshotDeps(object[] dependencies)
    {
        if (dependencies.Length == 0) return Array.Empty<object>();
        var copy = new object[dependencies.Length];
        Array.Copy(dependencies, copy, dependencies.Length);
        return copy;
    }

    // A lone arity-1 dependency whose COMPILE-TIME type is an array of reference types
    // — e.g. a covariant string[]/Element[] — is treated as a dependency LIST and
    // compared element-wise, matching how the params overload behaved before these
    // generics existed. The gate is on the *static* type (typeof(T1)) on purpose: a
    // value whose static type is object/Array/an interface but whose runtime value
    // happens to be an object[] historically bound the params overload in EXPANDED
    // form (wrapped as new object[]{ dep }) and was compared as ONE reference dep, so
    // it must stay a single dep here too — unwrapping it would be a silent semantic
    // change (observable for UseMemo, whose two overloads are both generic so the
    // arity-1 wins overload resolution). typeof(T1).IsArray is a JIT-time constant per
    // instantiation, so value-type and non-array deps fold this to null and never
    // reach the IsAssignableFrom check or box.
    private static object[]? AsParamsArrayDep<T1>(T1 d1)
        => typeof(T1).IsArray && typeof(object[]).IsAssignableFrom(typeof(T1)) && d1 is object[] arr ? arr : null;

    // Returns true when the stored deps already match. Each element is compared via
    // DepEquals, which unboxes the stored value (no allocation) and compares through
    // the typed comparer so the incoming dep is never boxed. A null/wrong-arity stored
    // array counts as "changed".
    private static bool DepsEqual1<T1>(object[]? prev, T1 d1)
        => prev is { Length: 1 } && DepEquals(prev[0], d1);

    private static bool DepsEqual2<T1, T2>(object[]? prev, T1 d1, T2 d2)
        => prev is { Length: 2 }
           && DepEquals(prev[0], d1)
           && DepEquals(prev[1], d2);

    private static bool DepsEqual3<T1, T2, T3>(object[]? prev, T1 d1, T2 d2, T3 d3)
        => prev is { Length: 3 }
           && DepEquals(prev[0], d1)
           && DepEquals(prev[1], d2)
           && DepEquals(prev[2], d3);

    // Compare a stored (boxed) dependency against the current typed value without an
    // unconditional (T)stored cast. A hot-reload edit or a dynamic call site can change
    // a dependency's runtime type at the same hook slot across renders; an
    // InvalidCastException while COMPARING deps is worse than simply treating the slot
    // as changed, so a type mismatch returns false ("changed"). Like the non-generic
    // params DepsEqual, this never throws on a type change.
    //
    // Equality is split by kind so typed-arity stays behaviorally identical to params:
    //   • Reference-type T compares via object.Equals (stored.Equals(current)), EXACTLY
    //     like the params path's Equals(prev[i], next[i]). References don't box, so this
    //     costs nothing, and it avoids a divergence for a reference type that implements
    //     IEquatable<T> but does not override Equals(object) — EqualityComparer<T>.Default
    //     would compare such a type by value, while params compares it by reference.
    //   • Value-type T uses EqualityComparer<T>.Default — the allocation-free, no-boxing
    //     fast path this overload exists for, and the same comparer UseState/UseReducer use.
    //     It unboxes the stored value through the `is T` pattern with no extra allocation.
    // typeof(T).IsValueType is a JIT-time constant, so each instantiation keeps only its
    // branch with no runtime type check.
    //
    // Nullable value-type deps (T = U?) are handled correctly. Boxing a non-null
    // Nullable<U> stores a boxed U, but the GENERIC `stored is T` test still succeeds for
    // that boxed U and binds the nullable-wrapped value — the CLR special-cases nullable
    // type-tests (the symmetric counterpart of `(U?)(object)u` unboxing). So an unchanged
    // nullable dep compares equal, a null nullable boxes to a null reference handled by
    // the `stored is null` guard above, and value<->null transitions are detected. This
    // matches the pre-existing `(T)prev[i]` behavior exactly (verified across
    // int?/long?/double?/enum? incl. null transitions) — it is NOT a per-render re-run.
    private static bool DepEquals<T>(object? stored, T current)
    {
        if (stored is null) return current is null;
        // Reference-type deps: object.Equals, matching the params path exactly (no boxing).
        if (!typeof(T).IsValueType)
            return stored.Equals(current);
        // Value-type deps: typed comparer, allocation-free and boxing-free.
        return stored is T t && EqualityComparer<T>.Default.Equals(t, current);
    }

    /// <summary>
    /// Returns a mutable ref object that persists across renders.
    /// </summary>
    public Ref<T> UseRef<T>(T initialValue = default!)
    {
        if (_hookIndex >= _hooks.Count)
        {
            _hooks.Add(new ValueHookState<Ref<T>>(new Ref<T>(initialValue)));
        }

        var currentIndex = _hookIndex;
        _hookIndex++;

        if (_hooks[currentIndex] is not ValueHookState<Ref<T>> hook)
            throw new HookOrderException(
                $"Hook at index {currentIndex} expected ValueHookState<Ref<{typeof(T).Name}>>, got {_hooks[currentIndex].GetType().Name}. " +
                "Hooks must be called in the same order every render.");
        return hook.Value;
    }

    // ════════════════════════════════════════════════════════════════
    //  Persisted state hooks
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Like UseState, but the value survives unmount/remount via an in-memory cache.
    /// On first mount, uses cached value if available, otherwise uses initialValue.
    /// Value is saved to cache on unmount.
    /// </summary>
    /// <remarks>
    /// Spec 033 §2. The cache is currently process-wide
    /// (<see cref="ApplicationPersistedScope.Default"/>) and bounded by an LRU
    /// policy. The two-arg form is flagged by <c>REACTOR_PERSIST_001</c>
    /// (<c>UsePersistedScopeAnalyzer</c>); new code should use the three-arg
    /// overload to make the intended scope explicit.
    /// </remarks>
    public (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue)
        => UsePersisted(key, initialValue, PersistedScope.Application);

    /// <summary>
    /// Persisted-state hook with explicit scope (spec 033 §2). Use
    /// <see cref="PersistedScope.Window"/> for state that should be bounded by
    /// the host's lifetime; <see cref="PersistedScope.Application"/> for
    /// process-wide state.
    /// </summary>
    /// <remarks>
    /// <see cref="PersistedScope.Window"/> resolves to the active host's
    /// <see cref="Microsoft.UI.Reactor.ReactorWindow.PersistedScope"/> when the
    /// host has an owning window; otherwise it falls back to the process-wide
    /// scope so unit-test contexts (which never construct a window) keep their
    /// existing semantics. Two windows of the same component class therefore
    /// hold independent state under <see cref="PersistedScope.Window"/>.
    /// (spec 036 §3.4 / §4.4 — closes spec 033 §7.5.)
    /// </remarks>
    public (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue, PersistedScope scope)
    {
        if (_hookIndex >= _hooks.Count)
        {
            var resolvedScope = ResolvePersistedScope(scope);
            T initial = (resolvedScope is not null && resolvedScope.TryGet<T>(key, out var cached))
                ? cached
                : initialValue;
            _hooks.Add(new PersistedHookState<T>(initial) { PersistKey = key, Scope = resolvedScope });
        }

        var currentIndex = _hookIndex;
        _hookIndex++;

        if (_hooks[currentIndex] is not PersistedHookState<T> hook)
            throw new HookOrderException(
                $"Hook at index {currentIndex} is {_hooks[currentIndex].GetType().Name}, expected PersistedHookState<{typeof(T).Name}> (UsePersisted). " +
                "Hooks must be called in the same order every render.");

        T current = hook.Value;

        // Issue #659 (#53): reuse the ref-stable persisted setter built once.
        if (hook.CachedSetter is not Action<T> setter)
        {
            setter = MakePersistedSetter(hook);
            hook.CachedSetter = setter;
        }

        return (current, setter);
    }

    private Action<T> MakePersistedSetter<T>(PersistedHookState<T> h)
    {
        void Setter(T newValue)
        {
            if (MarshalIfOffUIThread("UsePersisted", () => Setter(newValue))) return;
            if (!EqualityComparer<T>.Default.Equals(h.Value, newValue))
            {
                h.Value = newValue;
                _requestRerender?.Invoke();
            }
        }
        return Setter;
    }

    // ════════════════════════════════════════════════════════════════
    //  Observable interop hooks
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Observes an object and all nested INotifyPropertyChanged values
    /// reachable through its properties. Re-renders when any property
    /// at any depth changes. Automatically subscribes/unsubscribes as
    /// property values change.
    /// </summary>
    public T UseObservableTree<T>(T source) where T : global::System.ComponentModel.INotifyPropertyChanged
    {
        var (_, forceRender) = UseReducer(false);
        var trackerRef = UseRef<ObservableTreeTracker?>(null);

        UseEffect(() =>
        {
            var tracker = new ObservableTreeTracker(() => forceRender(v => !v));
            trackerRef.Current = tracker;
            tracker.SyncSubscriptions(source);
            return () => tracker.Dispose();
        }, source);

        return source;
    }

    /// <summary>
    /// Subscribes to an INotifyPropertyChanged source and re-renders when any property changes.
    /// Returns the same source object.
    /// </summary>
    public T UseObservable<T>(T source) where T : global::System.ComponentModel.INotifyPropertyChanged
    {
        var (_, forceRender) = UseReducer(false);
        UseEffect(() =>
        {
            void handler(object? s, global::System.ComponentModel.PropertyChangedEventArgs e)
                => forceRender(v => !v);
            source.PropertyChanged += handler;
            return () => source.PropertyChanged -= handler;
        }, source);
        return source;
    }

    /// <summary>
    /// Subscribes to an external store that exposes a current snapshot getter.
    /// Re-renders only when a change notification yields a different snapshot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="subscribe"/> is the effect dependency that decides whether the
    /// subscription is torn down and re-established, so it must be a <b>stable</b> delegate
    /// across renders. Pass a method group (e.g. <c>store.Subscribe</c>) or a
    /// <see cref="UseCallback(Action, object[])"/>-memoized delegate. A fresh capturing lambda
    /// (<c>onChanged =&gt; store.Subscribe(onChanged)</c>) is a new delegate every render and
    /// will unsubscribe/resubscribe on each one. This mirrors React's guidance for
    /// <c>useSyncExternalStore</c>.
    /// </para>
    /// <para>
    /// <paramref name="getSnapshot"/> must return a <b>cached/stable</b> value that only changes
    /// identity when the underlying data changes. Returning a fresh, never-equal value on every
    /// call (e.g. <c>() =&gt; items.ToArray()</c>) combined with an unstable <paramref name="subscribe"/>
    /// can spin: re-render re-runs the effect, the immediate re-check observes a "change", and that
    /// forces another render. Memoize the snapshot or return a value the supplied comparer treats as
    /// equal when nothing changed.
    /// </para>
    /// </remarks>
    public TSnapshot UseExternalStore<TSnapshot>(
        Func<Action, Action> subscribe,
        Func<TSnapshot> getSnapshot,
        IEqualityComparer<TSnapshot>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(getSnapshot);

        var snapshot = getSnapshot();
        var effectiveComparer = comparer ?? EqualityComparer<TSnapshot>.Default;
        var state = UseRef<ExternalStoreState<TSnapshot>?>(null);
        var (_, forceRender) = UseReducer(0, threadSafe: true);

        state.Current ??= new ExternalStoreState<TSnapshot>(snapshot, getSnapshot, effectiveComparer);

        lock (state.Current.Gate)
        {
            state.Current.Snapshot = snapshot;
            state.Current.GetSnapshot = getSnapshot;
            state.Current.Comparer = effectiveComparer;
        }

        UseEffect(() =>
        {
            void OnChanged()
            {
                bool changed;
                lock (state.Current.Gate)
                {
                    var nextSnapshot = state.Current.GetSnapshot();
                    changed = !state.Current.Comparer.Equals(state.Current.Snapshot, nextSnapshot);
                    if (changed)
                        state.Current.Snapshot = nextSnapshot;
                }

                if (changed)
                    forceRender(revision => revision + 1);
            }

            var unsubscribe = subscribe(OnChanged);

            // Close the render-to-subscribe race: if the store changed after the
            // render-time snapshot read but before the subscription became active,
            // re-read once immediately and schedule a rerender if needed.
            OnChanged();

            return unsubscribe;
        }, subscribe);

        return snapshot;
    }

    /// <summary>
    /// Subscribes to a specific property on an INotifyPropertyChanged source.
    /// Re-renders only when that property changes.
    /// </summary>
    public TProp UseObservableProperty<T, TProp>(T source, Func<T, TProp> selector, string propertyName)
        where T : global::System.ComponentModel.INotifyPropertyChanged
    {
        var (_, forceRender) = UseReducer(false);
        UseEffect(() =>
        {
            void handler(object? s, global::System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == propertyName || string.IsNullOrEmpty(e.PropertyName))
                    forceRender(v => !v);
            }
            source.PropertyChanged += handler;
            return () => source.PropertyChanged -= handler;
        }, source, propertyName);
        return selector(source);
    }

    /// <summary>
    /// Subscribes to an ObservableCollection and re-renders on Add/Remove/Reset.
    /// Returns the collection as IReadOnlyList.
    /// </summary>
    public IReadOnlyList<T> UseCollection<T>(global::System.Collections.ObjectModel.ObservableCollection<T> collection)
    {
        var (_, forceRender) = UseReducer(false);
        UseEffect(() =>
        {
            void handler(object? s, global::System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
                => forceRender(v => !v);
            collection.CollectionChanged += handler;
            return () => collection.CollectionChanged -= handler;
        }, collection);
        return collection;
    }

    // ════════════════════════════════════════════════════════════════
    //  Navigation hooks
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Root mode: creates a navigation stack with the given initial route.
    /// Returns a stable <see cref="Navigation.NavigationHandle{TRoute}"/> across re-renders.
    /// Wire this handle to a <c>NavigationHost</c> in the DSL to render route content.
    /// The handle is automatically provided to descendants via context so child components
    /// can call <c>UseNavigation&lt;TRoute&gt;()</c> (parameterless) to access it.
    /// </summary>
    public Navigation.NavigationHandle<TRoute> UseNavigation<TRoute>(TRoute initial) where TRoute : notnull
    {
        var stackRef = UseRef<Navigation.NavigationStack<TRoute>?>(null);
        if (stackRef.Current is null)
            stackRef.Current = new Navigation.NavigationStack<TRoute>(initial);

        var handleRef = UseRef<Navigation.NavigationHandle<TRoute>?>(null);
        if (handleRef.Current is null)
            handleRef.Current = new Navigation.NavigationHandle<TRoute>(stackRef.Current);

        // Capture the latest rerender callback every render so navigation mutations
        // that originate from event handlers always trigger a re-render of this component.
        stackRef.Current.OnChanged = _requestRerender;

        return handleRef.Current;
    }

    /// <summary>
    /// Child mode: retrieves an ancestor's <see cref="Navigation.NavigationHandle{TRoute}"/>
    /// from context. Throws if no ancestor provides one (i.e., no root <c>UseNavigation</c>
    /// with a <c>NavigationHost</c> exists above this component in the tree).
    /// </summary>
    public Navigation.NavigationHandle<TRoute> UseNavigation<TRoute>() where TRoute : notnull
    {
        var handle = UseContext(Navigation.NavigationContext<TRoute>.Instance);
        if (handle is null)
            throw new InvalidOperationException(
                $"UseNavigation<{typeof(TRoute).Name}>() (child mode) found no ancestor NavigationHost " +
                $"providing NavigationContext<{typeof(TRoute).Name}>. " +
                "Ensure a parent component calls UseNavigation<T>(initialRoute) and renders a NavigationHost.");
        return handle;
    }

    // ════════════════════════════════════════════════════════════════
    //  Navigation system back button
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Subscribes to Alt+Left and VirtualKey.GoBack keyboard events on the given window's content
    /// to call <see cref="Navigation.NavigationHandle{TRoute}.GoBack"/>. Unsubscribes on unmount.
    /// </summary>
    public void UseSystemBackButton<TRoute>(
        Navigation.NavigationHandle<TRoute> nav,
        Microsoft.UI.Xaml.Window window) where TRoute : notnull
    {
        UseEffect(() =>
        {
            void handler(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
            {
                if (e.Key == global::Windows.System.VirtualKey.GoBack ||
                    (e.Key == global::Windows.System.VirtualKey.Left &&
                     Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(global::Windows.System.VirtualKey.Menu)
                         .HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down)))
                {
                    if (nav.CanGoBack)
                    {
                        nav.GoBack();
                        e.Handled = true;
                    }
                }
            }

            if (window.Content is Microsoft.UI.Xaml.UIElement rootElement)
            {
                rootElement.KeyDown += handler;
                return () => rootElement.KeyDown -= handler;
            }
            return () => { };
        }, nav, window);
    }

    // ════════════════════════════════════════════════════════════════
    //  Navigation lifecycle hooks
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Registers lifecycle callbacks that fire during navigation events.
    /// <list type="bullet">
    /// <item><c>onNavigatedTo</c> — fires after this page becomes active.</item>
    /// <item><c>onNavigatingFrom</c> — fires before navigating away. Call <c>ctx.Cancel()</c> to block.</item>
    /// <item><c>onNavigatedFrom</c> — fires after this page is no longer active.</item>
    /// </list>
    /// Callbacks are always updated to the latest references on every render.
    /// </summary>
    public void UseNavigationLifecycle(
        Action<Navigation.NavigatingToContext>? onNavigatingTo = null,
        Action<Navigation.NavigatedToContext>? onNavigatedTo = null,
        Action<Navigation.NavigatingFromContext>? onNavigatingFrom = null,
        Action<Navigation.NavigatedFromContext>? onNavigatedFrom = null)
    {
        if (_hookIndex >= _hooks.Count)
        {
            _hooks.Add(new NavigationLifecycleHookState());
        }

        if (_hooks[_hookIndex] is not NavigationLifecycleHookState hook)
            throw new HookOrderException(
                $"Hook at index {_hookIndex} is {_hooks[_hookIndex].GetType().Name}, expected NavigationLifecycleHookState. " +
                "Hooks must be called in the same order every render.");
        _hookIndex++;

        // Always update to latest callbacks so closures capture current state
        hook.OnNavigatingTo = onNavigatingTo;
        hook.OnNavigatedTo = onNavigatedTo;
        hook.OnNavigatingFrom = onNavigatingFrom;
        hook.OnNavigatedFrom = onNavigatedFrom;
    }

    /// <summary>
    /// Returns the navigation lifecycle hook state if one was registered, or null.
    /// Used by the reconciler to collect lifecycle callbacks from a component tree.
    /// </summary>
    internal NavigationLifecycleHookState? GetNavigationLifecycleHook()
    {
        for (int i = 0; i < _hooks.Count; i++)
        {
            if (_hooks[i] is NavigationLifecycleHookState hook)
                return hook;
        }
        return null;
    }

    // ════════════════════════════════════════════════════════════════
    //  Context hooks
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads the nearest ancestor's provided value for the given context.
    /// Returns the context's DefaultValue if no provider exists in the ancestor chain.
    /// Follows hook rules — must be called in the same order every render.
    /// </summary>
    public T UseContext<T>(Context<T> context)
    {
        if (_hookIndex >= _hooks.Count)
        {
            _hooks.Add(new ContextHookState { Context = context });
        }

        if (_hooks[_hookIndex] is not ContextHookState hook)
            throw new HookOrderException(
                $"Hook at index {_hookIndex} is {_hooks[_hookIndex].GetType().Name}, expected ContextHookState (UseContext). " +
                "Hooks must be called in the same order every render.");
        _hookIndex++;

        var value = _contextScope is not null
            ? _contextScope.Read(context)
            : context.DefaultValue;
        hook.LastValue = value;
        return value;
    }

    /// <summary>
    /// Enumerates ContextHookState entries for memo change detection (Phase 3).
    /// </summary>
    /// <remarks>
    /// Issue #659 (#51): returns a struct enumerable instead of a
    /// <c>yield</c>-generated iterator. The reconciler iterates this every
    /// render for every component, so the old <c>IEnumerable</c> path heap-
    /// allocated an enumerator per component per render. <c>foreach</c> binds to
    /// the public struct <c>GetEnumerator</c> (zero allocation); the
    /// <see cref="IEnumerable{T}"/> implementation is retained only so test
    /// helpers that call <c>.ToList()</c> keep working.
    /// </remarks>
    internal ContextHookEnumerable ContextHooks => new(_hooks);

    internal readonly struct ContextHookEnumerable : IEnumerable<ContextHookState>
    {
        private readonly List<HookState> _hooks;
        public ContextHookEnumerable(List<HookState> hooks) => _hooks = hooks;

        public Enumerator GetEnumerator() => new(_hooks);
        IEnumerator<ContextHookState> IEnumerable<ContextHookState>.GetEnumerator() => GetEnumerator();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<ContextHookState>
        {
            private readonly List<HookState> _hooks;
            private int _index;
            private ContextHookState? _current;

            public Enumerator(List<HookState> hooks)
            {
                _hooks = hooks;
                _index = 0;
                _current = null;
            }

            public ContextHookState Current => _current!;
            object global::System.Collections.IEnumerator.Current => _current!;

            public bool MoveNext()
            {
                while (_index < _hooks.Count)
                {
                    if (_hooks[_index++] is ContextHookState ctx)
                    {
                        _current = ctx;
                        return true;
                    }
                }
                _current = null;
                return false;
            }

            public void Reset()
            {
                _index = 0;
                _current = null;
            }

            public void Dispose() { }
        }
    }

    private sealed class ExternalStoreState<TSnapshot>
    {
        public ExternalStoreState(
            TSnapshot snapshot,
            Func<TSnapshot> getSnapshot,
            IEqualityComparer<TSnapshot> comparer)
        {
            Snapshot = snapshot;
            GetSnapshot = getSnapshot;
            Comparer = comparer;
        }

        public object Gate { get; } = new();
        public TSnapshot Snapshot { get; set; }
        public Func<TSnapshot> GetSnapshot { get; set; }
        public IEqualityComparer<TSnapshot> Comparer { get; set; }
    }

    // ════════════════════════════════════════════════════════════════
    //  Color scheme hooks
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the effective <see cref="ColorScheme"/> at this component's
    /// position in the tree. Automatically reflects the current system theme,
    /// per-element <c>RequestedTheme</c> overrides, and High Contrast mode.
    /// <para>
    /// The value is re-evaluated on every render — when the theme changes,
    /// <see cref="Microsoft.UI.Reactor.Hosting.ReactorHost"/> triggers a re-render so this hook
    /// naturally picks up the new value.
    /// </para>
    /// </summary>
    public ColorScheme UseColorScheme()
    {
        // Read effective theme from the application. On re-render after theme
        // change, this returns the updated value. Components inside a
        // RequestedTheme(Dark) subtree see the correct variant because the
        // FrameworkElement.ActualTheme is read at reconcile time.
        var theme = Microsoft.UI.Xaml.Application.Current?.RequestedTheme;
        var elementTheme = theme switch
        {
            Microsoft.UI.Xaml.ApplicationTheme.Dark => Microsoft.UI.Xaml.ElementTheme.Dark,
            Microsoft.UI.Xaml.ApplicationTheme.Light => Microsoft.UI.Xaml.ElementTheme.Light,
            _ => Microsoft.UI.Xaml.ElementTheme.Default,
        };
        return ColorSchemeContext.FromActualTheme(elementTheme);
    }

    /// <summary>
    /// Convenience wrapper — returns <c>true</c> when the effective color
    /// scheme is <see cref="ColorScheme.Dark"/>.
    /// </summary>
    public bool UseIsDarkTheme() => UseColorScheme() == ColorScheme.Dark;

    // ════════════════════════════════════════════════════════════════
    //  High contrast / accessibility display hooks
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns <c>true</c> when the system is in a High Contrast (forced colors) theme.
    /// Automatically re-renders the component when high contrast is toggled.
    /// <para>
    /// Use this to conditionally override custom styling (hardcoded backgrounds,
    /// foregrounds, border colors) that would ignore forced-colors mode.
    /// WinUI controls using ThemeResource brushes adapt automatically — this hook
    /// is for Reactor components that use explicit color values.
    /// </para>
    /// </summary>
    public bool UseHighContrast() => UseHighContrastState().IsHighContrast;

    /// <summary>
    /// Returns the high contrast scheme name (e.g., "High Contrast Black",
    /// "High Contrast White") or <c>null</c> if not in high contrast mode.
    /// Automatically re-renders the component when the scheme changes.
    /// <para>
    /// Must be called instead of (not in addition to) <see cref="UseHighContrast"/>
    /// because each consumes the same hook slots. Use one or the other.
    /// </para>
    /// </summary>
    public string? UseHighContrastScheme() => UseHighContrastState().HighContrastScheme;

    private HighContrastState UseHighContrastState()
    {
        var (state, _) = UseState(new HighContrastState());

        // Seeded during render for the same reason as UseReducedMotionState above: a value
        // first read inside UseEffect is not available to the frame the user sees, so the
        // first paint reported IsHighContrast=false / scheme=null to every caller —
        // including callers running under high contrast. Guarded so the WinRT settings
        // objects are constructed once per component rather than once per render.
        // Guarded for the same reason as the reduced-motion seed, and broad for the same
        // reason: the try holds only interop and field assignment.
        if (!state.Seeded)
        {
            try
            {
                state.A11ySettings ??= new global::Windows.UI.ViewManagement.AccessibilitySettings();
                state.IsHighContrast = state.A11ySettings.HighContrast;
                state.HighContrastScheme = state.A11ySettings.HighContrast ? state.A11ySettings.HighContrastScheme : null;
            }
            catch (global::System.Exception) { state.A11ySettings = null; }
            state.Seeded = true;
        }

        // AccessibilitySettings.HighContrastChanged throws ERROR_NOT_FOUND
        // (0x80070490) in WinUI 3 desktop apps because it requires a CoreWindow.
        // Instead, use UISettings.ColorValuesChanged which fires reliably for
        // system theme changes including high contrast toggles.
        UseEffect(() =>
        {
            try
            {
                state.A11ySettings ??= new global::Windows.UI.ViewManagement.AccessibilitySettings();
                state.UiSettings ??= new global::Windows.UI.ViewManagement.UISettings();
            }
            catch (global::System.Exception) { state.UiSettings = null; }

            // No projection for the settings objects: report the default and skip the
            // subscription rather than throwing out of the effect flush, which is wrapped
            // in try/finally and would propagate.
            if (state.A11ySettings is not { } a11ySettings || state.UiSettings is not { } uiSettings)
                return () => { };

            var rerender = _requestRerender;

            void OnColorValuesChanged(global::Windows.UI.ViewManagement.UISettings sender, object args)
            {
                state.IsHighContrast = a11ySettings.HighContrast;
                state.HighContrastScheme = a11ySettings.HighContrast ? a11ySettings.HighContrastScheme : null;
                rerender?.Invoke();
            }

            uiSettings.ColorValuesChanged += OnColorValuesChanged;

            // Re-read after subscribing: the window between the seed above and this
            // subscription has no listener, so a toggle inside it would otherwise persist
            // until an unrelated theme notification arrived.
            var hc = a11ySettings.HighContrast;
            var scheme = hc ? a11ySettings.HighContrastScheme : null;
            if (hc != state.IsHighContrast || scheme != state.HighContrastScheme)
            {
                state.IsHighContrast = hc;
                state.HighContrastScheme = scheme;
                rerender?.Invoke();
            }
            return () => uiSettings.ColorValuesChanged -= OnColorValuesChanged;
        });

        return state;
    }

    private sealed class HighContrastState
    {
        public global::Windows.UI.ViewManagement.AccessibilitySettings? A11ySettings;
        public global::Windows.UI.ViewManagement.UISettings? UiSettings;
        public bool IsHighContrast;
        public string? HighContrastScheme;
        public bool Seeded;
    }

    // ════════════════════════════════════════════════════════════════
    //  Reduced-motion hook
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns <c>true</c> when the user or system prefers reduced motion
    /// (e.g., Windows "Show animations" is off, or <c>SPI_GETCLIENTAREAANIMATION</c>
    /// returns false). Automatically re-renders the component when the preference changes.
    /// <para>
    /// Use this to skip entrance/exit animations, disable pan inertia, terminate
    /// force-graph simulations immediately, and keep only ≤ 150 ms opacity fades
    /// (WCAG 2.3.3).
    /// </para>
    /// <para>
    /// The value is seeded during the first render and then tracked live through
    /// <c>UISettings.AnimationsEnabledChanged</c>. That event needs Windows 10 2004 (19041);
    /// on older builds the preference is re-read whenever a theme or palette change arrives
    /// instead, so it updates on the next such notification rather than immediately.
    /// </para>
    /// </summary>
    public bool UseReducedMotion() => UseReducedMotionState().IsReducedMotion;

    private ReducedMotionState UseReducedMotionState()
    {
        var (state, _) = UseState(new ReducedMotionState());

        // Seed during render, not in the effect below. UseEffect does not run until after
        // the first render commits, so a value read there is invisible to the frame the
        // user actually sees: every consumer rendered its first frame with
        // IsReducedMotion=false, and a component that never re-renders keeps that value
        // forever. For an accessibility hook that is the worst possible direction — the
        // first frame animates for precisely the users who asked it not to. Guarded so
        // UISettings is constructed once per component, not once per render.
        //
        // UseExternalStore also closes the render-to-subscribe race, but it is a public hook
        // for app-authored stores with zero internal callers, it requires a stable subscribe
        // delegate (so it would need extra memoization here), and it re-reads the snapshot on
        // every render. Seed-then-subscribe is what every sibling environment hook in this
        // file does — UseWindowPosition seeds via UseState(win.Position) — so this stays
        // consistent with them.
        // Constructing the WinRT settings object is fallible — ReactorHost guards the same
        // constructor because a host may have no projection for it. Seeding during render
        // widens where that runs: the effect below only executes under a live reconciler.
        // Degrade to the default rather than failing the frame, and set Seeded either way so
        // a host that cannot provide it does not throw once per render.
        //
        // The catches are deliberately broad. What bounds them is the scope of the try —
        // only interop and field assignment, no application logic whose bug could be masked.
        // A narrower list would have to enumerate every way a projection can be absent, which
        // is not something this repo can test: measured in the headless host, both
        // constructors and every property read off them succeed.
        if (!state.Seeded)
        {
            try
            {
                state.Settings ??= new global::Windows.UI.ViewManagement.UISettings();
                state.IsReducedMotion = !state.Settings.AnimationsEnabled;
            }
            catch (global::System.Exception) { state.Settings = null; }
            state.Seeded = true;
        }

        UseEffect(() =>
        {
            try { state.Settings ??= new global::Windows.UI.ViewManagement.UISettings(); }
            catch (global::System.Exception) { state.Settings = null; }

            // No projection for the settings object: report the default and skip the
            // subscription rather than throwing out of the effect flush, which is wrapped
            // in try/finally and would propagate.
            if (state.Settings is not { } settings) return () => { };

            var rerender = _requestRerender;
            void OnChanged(global::Windows.UI.ViewManagement.UISettings sender, object args)
            {
                var value = !sender.AnimationsEnabled;
                if (value == state.IsReducedMotion) return;
                state.IsReducedMotion = value;
                rerender?.Invoke();
            }

            // AnimationsEnabledChanged is the only event raised for this preference.
            // ColorValuesChanged is NOT — measured with a live subscription to both while
            // toggling Settings > Accessibility > Visual effects > Animation effects, it
            // fired zero times in either direction. It is kept as the pre-19041 fallback
            // because OnChanged re-reads the current value, so a theme or palette change
            // picks up a missed animation flip as a side effect.
            settings.ColorValuesChanged += OnChanged;
            if (UiSettingsCapabilities.HasAnimationsEnabledChanged)
                settings.AnimationsEnabledChanged += OnChanged;

            // Re-read after subscribing: the preference can flip between the seed above and
            // this subscription, and that window has no listener, so the change would
            // otherwise be missed until some unrelated notification happened to arrive.
            var current = !settings.AnimationsEnabled;
            if (current != state.IsReducedMotion)
            {
                state.IsReducedMotion = current;
                rerender?.Invoke();
            }
            return () =>
            {
                settings.ColorValuesChanged -= OnChanged;
                // Called rather than captured: CA1416's flow analysis does not carry a
                // guard's outcome across a closure boundary.
                if (UiSettingsCapabilities.HasAnimationsEnabledChanged)
                    settings.AnimationsEnabledChanged -= OnChanged;
            };
        });

        return state;
    }

    private sealed class ReducedMotionState
    {
        public global::Windows.UI.ViewManagement.UISettings? Settings;
        public bool IsReducedMotion;
        public bool Seeded;
    }

    // ════════════════════════════════════════════════════════════════
    //  Localization hooks
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns an IntlAccessor for the current locale. Re-renders this component
    /// when the locale changes via a parent LocaleProvider.
    /// If no LocaleProvider is present, returns a default accessor using the OS locale.
    /// Uses Context internally — the context system handles re-renders automatically.
    /// </summary>
    public Localization.IntlAccessor UseIntl()
    {
        var contextAccessor = UseContext(Localization.IntlContexts.Locale);
        return contextAccessor ?? _defaultAccessor.Value;
    }

    private static readonly Lazy<Localization.IntlAccessor> _defaultAccessor = new(() =>
    {
        var osLocale = global::System.Globalization.CultureInfo.CurrentUICulture.Name;
        if (string.IsNullOrEmpty(osLocale)) osLocale = "en-US";
        var cache = new Localization.MessageCache();
        var provider = new Localization.ReswResourceProvider(osLocale);
        return new Localization.IntlAccessor(osLocale, provider, cache, osLocale);
    });

    // ════════════════════════════════════════════════════════════════
    //  Command hooks
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Processes a Command for use in a component. Always consumes a <b>stable hook shape</b>
    /// (independent of whether the command is sync/async or debounced), so a command at a given
    /// call site can flip between sync↔async or <see cref="Command.DebounceMs"/> 0↔N across
    /// renders without ever reordering hook slots. For a pure sync command with no
    /// <see cref="Command.DebounceMs"/> the original command is returned unchanged (identity
    /// preserved). For async commands, wraps ExecuteAsync with automatic IsExecuting tracking and
    /// re-entrance guards. When <see cref="Command.DebounceMs"/> &gt; 0, wraps the dispatch with a
    /// leading-edge debounce: a fire within the window of the prior accepted fire is dropped and
    /// <see cref="Command.IsDebouncing"/> (hence <see cref="Command.IsEnabled"/>=false) reflects
    /// the window so the bound control disables, re-enabling when the window elapses. The returned
    /// command has a sync Execute action, ExecuteAsync = null, and preserves the authored
    /// DebounceMs value (re-passing it through UseCommand is a no-op — debounce is never applied
    /// twice).
    /// </summary>
    public Command UseCommand(Command command)
    {
        var (guardRef, debounceRef, isExecuting, setIsExecuting, isDebouncing, setIsDebouncing)
            = UseCommandState();

        var asyncAction = command.ExecuteAsync;
        var syncAction = command.Execute;
        int debounceMs = command.DebounceMs;

        var wrappedExecute = UseMemo<Action>(() => () =>
            DispatchCommand(asyncAction, syncAction, debounceMs, guardRef, debounceRef, setIsExecuting, setIsDebouncing),
            (object?)command.ExecuteAsync ?? NullDep, (object?)command.Execute ?? NullDep, debounceMs);

        // Branch on VALUES, never on hook calls: a pure sync, non-debounced, not-yet-wrapped
        // command is returned unchanged (preserves identity / Assert.Same and today's behavior).
        if (!CommandNeedsWrapping(command.ExecuteAsync, command.DebounceMs, command.DebounceHandled))
            return command;

        return command with
        {
            Execute = wrappedExecute,
            ExecuteAsync = null,
            IsExecuting = isExecuting,
            IsDebouncing = isDebouncing,
            DebounceHandled = true,
        };
    }

    /// <summary>
    /// Processes a parameterized Command for use in a component. Consumes the same
    /// <b>stable hook shape</b> as the non-generic <see cref="UseCommand(Command)"/> and applies
    /// the same async tracking and leading-edge <see cref="Command{T}.DebounceMs"/> debounce.
    /// </summary>
    public Command<T> UseCommand<T>(Command<T> command)
    {
        var (guardRef, debounceRef, isExecuting, setIsExecuting, isDebouncing, setIsDebouncing)
            = UseCommandState();

        var asyncAction = command.ExecuteAsync;
        var syncAction = command.Execute;
        int debounceMs = command.DebounceMs;

        var wrappedExecute = UseMemo<Action<T>>(() => (arg) =>
            DispatchCommand(
                asyncAction is null ? null : () => asyncAction(arg),
                syncAction is null ? null : () => syncAction(arg),
                debounceMs, guardRef, debounceRef, setIsExecuting, setIsDebouncing),
            (object?)command.ExecuteAsync ?? NullDep, (object?)command.Execute ?? NullDep, debounceMs);

        if (!CommandNeedsWrapping(command.ExecuteAsync, command.DebounceMs, command.DebounceHandled))
            return command;

        return command with
        {
            Execute = wrappedExecute,
            ExecuteAsync = null,
            IsExecuting = isExecuting,
            IsDebouncing = isDebouncing,
            DebounceHandled = true,
        };
    }

    /// <summary>A command's execution needs wrapping when it is async, or it declares a debounce
    /// window that <see cref="UseCommand(Command)"/> hasn't already consumed. Pure sync,
    /// non-debounced commands (and already-wrapped commands) pass through untouched.</summary>
    private static bool CommandNeedsWrapping(object? executeAsync, int debounceMs, bool debounceHandled)
        => !debounceHandled && (executeAsync is not null || debounceMs > 0);

    /// <summary>
    /// Allocates the stable hook shape shared by both <see cref="UseCommand(Command)"/> overloads:
    /// the IsExecuting / IsDebouncing state, the re-entrance guard and debounce-slot refs, and an
    /// unmount effect that disposes any live re-enable timer so it cannot fire
    /// <c>setIsDebouncing</c> / request a re-render against a torn-down context. These hooks are
    /// allocated unconditionally so the hook order is identical regardless of the command's shape.
    /// </summary>
    private (Ref<AsyncReentryGuard?> GuardRef, Ref<DebounceSlot?> DebounceRef,
             bool IsExecuting, Action<bool> SetIsExecuting,
             bool IsDebouncing, Action<bool> SetIsDebouncing) UseCommandState()
    {
        var (isExecuting, setIsExecuting) = UseState(false, threadSafe: true);
        var (isDebouncing, setIsDebouncing) = UseState(false, threadSafe: true);
        var guardRef = UseRef<AsyncReentryGuard?>(null);
        var debounceRef = UseRef<DebounceSlot?>(null);

        // Mount-once effect: on unmount, dispose the live re-enable timer (if any) so a pending
        // window can't fire its callback against a context that no longer exists.
        UseEffect(() => () =>
        {
            var slot = debounceRef.Current;
            if (slot is null) return;
            global::System.Threading.ITimer? timer;
            lock (slot.Gate)
            {
                timer = slot.Timer;
                slot.Timer = null;
                slot.InWindow = false;
            }
            timer?.Dispose();
        });

        return (guardRef, debounceRef, isExecuting, setIsExecuting, isDebouncing, setIsDebouncing);
    }

    /// <summary>
    /// Shared dispatch core for both <see cref="UseCommand(Command)"/> overloads. Applies the
    /// re-entrance guard (async only, checked BEFORE arming the window so a fire blocked because
    /// the async action is still running doesn't arm the window for nothing) and the leading-edge
    /// debounce, then runs the action. Returns true if the fire was accepted.
    /// </summary>
    private bool DispatchCommand(
        Func<Task>? asyncAction,
        Action? syncAction,
        int debounceMs,
        Ref<AsyncReentryGuard?> guardRef,
        Ref<DebounceSlot?> debounceRef,
        Action<bool> setIsExecuting,
        Action<bool> setIsDebouncing)
    {
        if (asyncAction is not null)
        {
            var guard = guardRef.Current ??= new AsyncReentryGuard();
            // Atomic test-and-set re-entrance guard, checked BEFORE arming the window so a fire
            // blocked because the async action is still running doesn't arm the window for nothing.
            // The guard is acquired here (the dispatcher/UI thread) and released in the Task.Run
            // finally (a threadpool thread); Interlocked.CompareExchange + Volatile.Write give the
            // cross-thread release proper visibility, so the next invoke always observes a completed
            // action's release (a plain field could in principle read a stale "busy").
            if (global::System.Threading.Interlocked.CompareExchange(ref guard.Busy, 1, 0) != 0) return false;
            if (!TryEnterDebounceWindow(debounceMs, debounceRef, setIsDebouncing))
            {
                global::System.Threading.Volatile.Write(ref guard.Busy, 0);
                return false;
            }
            setIsExecuting(true);
            // try/finally — NOT try/catch. The user's command throw becomes a faulted Task; the
            // framework's reentry-guard and IsExecuting state still get restored. We deliberately
            // let the exception surface via Task.UnobservedTaskException rather than swallowing,
            // so a buggy command is visible to the developer (matches the dispose / lifecycle
            // policy elsewhere — don't hide user bugs).
            _ = Task.Run(async () =>
            {
                try
                {
                    await asyncAction();
                }
                finally
                {
                    global::System.Threading.Volatile.Write(ref guard.Busy, 0);
                    setIsExecuting(false);
                }
            });
            return true;
        }

        // Sync command + debounce: dispatch stays synchronous (no Task.Run hop), so the action
        // keeps running on the UI thread.
        if (!TryEnterDebounceWindow(debounceMs, debounceRef, setIsDebouncing)) return false;
        syncAction?.Invoke();
        return true;
    }

    /// <summary>
    /// Per-command leading-edge debounce state: the in-window flag, the absolute window deadline,
    /// an epoch token, and the one-shot re-enable timer. Lives in a <see cref="UseRef{T}"/> slot so
    /// it persists across renders (the <see cref="Command"/> record itself is reconstructed every
    /// render).
    /// </summary>
    private sealed class DebounceSlot
    {
        public readonly object Gate = new();
        public bool InWindow;
        /// <summary>Absolute time (per the context's <see cref="TimeProvider"/>) at which the
        /// current window expires. Acceptance is decided against this deadline rather than purely on
        /// <see cref="InWindow"/>, so a delayed re-enable timer callback can never drop a fire that
        /// is genuinely past <c>DebounceMs</c>.</summary>
        public DateTimeOffset WindowEndsAt;
        /// <summary>Incremented for every accepted fire. The re-enable timer captures the epoch
        /// it was created for and only clears the window if it still owns the current epoch — so a
        /// stale timer (a newer accepted fire already re-armed the window) is a no-op.</summary>
        public long Epoch;
        public global::System.Threading.ITimer? Timer;
    }

    /// <summary>Atomic re-entrance guard for an async command's in-flight window. Stored in a
    /// <see cref="UseRef{T}"/> slot; <see cref="Busy"/> is mutated via <c>Interlocked</c>/
    /// <c>Volatile</c> because it is acquired on the dispatcher thread and released on the Task.Run
    /// worker thread.</summary>
    private sealed class AsyncReentryGuard
    {
        public int Busy;
    }

    /// <summary>Stable non-null stand-in for a null delegate in a <see cref="UseMemo{T}"/>
    /// dependency array (keeps the comparison meaningful without tripping nullable warnings).</summary>
    private static readonly object NullDep = new();

    /// <summary>
    /// Leading-edge gate. Returns true if this fire is accepted; arms the window for
    /// <paramref name="debounceMs"/> and schedules a re-enable that clears
    /// <see cref="DebounceSlot.InWindow"/> and re-renders so the control comes back. Returns
    /// false only while a prior accepted fire is genuinely still inside its window.
    /// <para>Acceptance is <b>time-based</b>: it is decided against the absolute
    /// <see cref="DebounceSlot.WindowEndsAt"/> deadline (via the injected <see cref="TimeProvider"/>),
    /// not purely on the <see cref="DebounceSlot.InWindow"/> flag. The one-shot timer only exists to
    /// trigger the re-render/re-enable; if it is delayed (threadpool starvation/suspension) a fire
    /// arriving after the deadline is still accepted and re-arms, honoring the fixed-duration
    /// semantics rather than dropping until the callback eventually runs.</para>
    /// </summary>
    private bool TryEnterDebounceWindow(int debounceMs, Ref<DebounceSlot?> slotRef, Action<bool> setIsDebouncing)
    {
        if (debounceMs <= 0) return true;

        var slot = slotRef.Current ??= new DebounceSlot();
        var now = TimeProvider.GetUtcNow();
        global::System.Threading.ITimer? prior;
        long epoch;
        lock (slot.Gate)
        {
            // Drop only while we are genuinely still inside the window. If the re-enable timer
            // callback is delayed past the deadline, InWindow may still be set — but the window has
            // logically expired, so fall through and accept + re-arm rather than wrongly drop.
            if (slot.InWindow && now < slot.WindowEndsAt) return false;
            slot.InWindow = true;
            slot.WindowEndsAt = now + TimeSpan.FromMilliseconds(debounceMs);
            epoch = ++slot.Epoch;
            prior = slot.Timer;
            slot.Timer = null;
            // setIsDebouncing(true) is called under the gate so it is atomic with arming the
            // window: a stale timer's setIsDebouncing(false) (below) can never interleave between
            // here and the InWindow check, so the control can't be left wrongly enabled.
            setIsDebouncing(true);
        }
        // Dispose the superseded timer (the re-enable callback normally disposes its own when it
        // clears the window, so on a clean re-arm this is already null; covers the expiry/race path).
        prior?.Dispose();

        var timer = TimeProvider.CreateTimer(
            _ =>
            {
                lock (slot.Gate)
                {
                    // Only the timer that still owns the current epoch may clear the window.
                    // A stale timer whose window was superseded by a newer accepted fire is a
                    // no-op. setIsDebouncing(false) runs under the gate so it is atomic with the
                    // clear and cannot race a concurrent accepted fire's setIsDebouncing(true).
                    if (slot.Epoch != epoch || !slot.InWindow) return;
                    slot.InWindow = false;
                    setIsDebouncing(false);
                    // Dispose this now-inert one-shot timer immediately (atomic with the clear, so
                    // slot.Timer is still us) rather than retaining it until the next accepted fire
                    // or unmount — otherwise a fire-once debounced command on a long-lived component
                    // would keep a dead timer object alive indefinitely.
                    slot.Timer?.Dispose();
                    slot.Timer = null;
                }
            },
            null,
            TimeSpan.FromMilliseconds(debounceMs),
            global::System.Threading.Timeout.InfiniteTimeSpan);

        lock (slot.Gate)
        {
            // Install only if we still own the window; otherwise this timer is already stale.
            if (slot.Epoch == epoch)
                slot.Timer = timer;
            else
                timer.Dispose();
        }
        return true;
    }

    // ════════════════════════════════════════════════════════════════
    //  Responsive layout hooks
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns (width, height) of the given window and re-renders when the window resizes.
    /// </summary>
    public (double Width, double Height) UseWindowSize(Microsoft.UI.Xaml.Window window)
    {
        var (size, setSize) = UseState((window.Bounds.Width, window.Bounds.Height));

        UseEffect(() =>
        {
            void handler(object sender, Microsoft.UI.Xaml.WindowSizeChangedEventArgs args)
            {
                setSize((args.Size.Width, args.Size.Height));
            }
            window.SizeChanged += handler;
            return () => window.SizeChanged -= handler;
        }, window);

        return size;
    }

    /// <summary>
    /// Parameterless overload — resolves the current host's window via the
    /// active host's back-pointer and re-renders on resize. Returns
    /// <c>(0, 0)</c> when called outside a window (e.g. tray-flyout content);
    /// no implicit fallback to <c>PrimaryWindow</c>. (spec 036 §5.2 / §7.1)
    /// </summary>
    public (double Width, double Height) UseWindowSize()
    {
        var hostWindow = Microsoft.UI.Reactor.ReactorApp.ActiveHostInternal?.OwningWindow;
        if (hostWindow is null)
        {
            // Reserve the same hook slots as the live branch (UseState +
            // UseEffect) and use the same tuple-shaped state so a render that
            // crosses the no-window → window transition doesn't trip the
            // hook-state type check.
            _ = UseState((0.0, 0.0));
            UseEffect(() => { /* no-op */ }, this);
            return (0, 0);
        }
        return UseWindowSize(hostWindow.NativeWindow);
    }

    /// <summary>
    /// Resolves the current host's <see cref="Microsoft.UI.Reactor.ReactorWindow"/>
    /// (or <c>null</c> when called outside a window — e.g. tray-flyout content).
    /// O(1) field read; no subscription, no re-render trigger. (spec 036 §7 / §7.1)
    /// </summary>
    public Microsoft.UI.Reactor.ReactorWindow? UseWindow()
        => Microsoft.UI.Reactor.ReactorApp.ActiveHostInternal?.OwningWindow;

    /// <summary>
    /// Subscribes to the host window's <c>PositionChanged</c> event and re-renders
    /// on change. Returns <c>(0, 0)</c> outside a window. (spec 054 §5.5)
    /// </summary>
    public (double X, double Y) UseWindowPosition()
    {
        var win = Microsoft.UI.Reactor.ReactorApp.ActiveHostInternal?.OwningWindow;
        if (win is null)
        {
            _ = UseState((0.0, 0.0));
            UseEffect(() => { /* no-op */ }, this);
            return (0, 0);
        }

        var (position, setPosition) = UseState(win.Position);
        UseEffect(() =>
        {
            void handler(object? sender, Microsoft.UI.Reactor.WindowDipPositionChangedEventArgs args)
                => setPosition(args.Position);
            win.PositionChanged += handler;
            return () => win.PositionChanged -= handler;
        }, win);
        return position;
    }

    /// <summary>
    /// Returns a covered hint from the host window's z-order transitions and
    /// re-renders on change. The value is not pixel-accurate occlusion state.
    /// </summary>
    public bool UseIsCovered()
    {
        var win = Microsoft.UI.Reactor.ReactorApp.ActiveHostInternal?.OwningWindow;
        if (win is null)
        {
            _ = UseState(false);
            UseEffect(() => { /* no-op */ }, this);
            return false;
        }

        var (isCovered, setIsCovered) = UseState(false);
        UseEffect(() =>
        {
            void handler(object? sender, Microsoft.UI.Reactor.WindowZOrderChangedEventArgs args)
            {
                if (args.MovedToTop) setIsCovered(false);
                else if (args.IsCovered) setIsCovered(true);
            }
            win.ZOrderChanged += handler;
            return () => win.ZOrderChanged -= handler;
        }, win);
        return isCovered;
    }

    /// <summary>
    /// Returns the current display snapshot and re-renders when the display layout changes.
    /// </summary>
    public IReadOnlyList<Microsoft.UI.Reactor.DisplayInfo> UseDisplays()
    {
        var (displays, setDisplays) = UseState(Microsoft.UI.Reactor.ReactorDisplay.Displays);
        UseEffect(() =>
        {
            void handler(object? sender, EventArgs args)
                => setDisplays(Microsoft.UI.Reactor.ReactorDisplay.Displays);
            Microsoft.UI.Reactor.ReactorDisplay.DisplayLayoutChanged += handler;
            return () => Microsoft.UI.Reactor.ReactorDisplay.DisplayLayoutChanged -= handler;
        }, this);
        return displays;
    }

    /// <summary>
    /// Applies a lifetime-bound width/height aspect lock to the owning window.
    /// Last mounted hook wins; unmounting restores the previous writer.
    /// </summary>
    public void UseWindowAspectRatio(double? widthOverHeight)
    {
        var win = Microsoft.UI.Reactor.ReactorApp.ActiveHostInternal?.OwningWindow;
        UseEffect(() =>
        {
            if (win is null) return () => { };
            var token = win.RegisterAspectRatioOverride(widthOverHeight);
            return () => token.Dispose();
        }, win!, widthOverHeight ?? double.NaN);
    }

    /// <summary>
    /// Returns a stable callback that starts a framework-managed window
    /// drag/move loop (cursor polling + <c>AppWindow.Move</c> at ~60Hz
    /// until the left mouse button is released).
    /// </summary>
    /// <remarks>
    /// The loop is framework-managed (not OS-managed) because WinUI 3
    /// routes pointer input through a child input-site HWND, so
    /// synthesizing <c>WM_NCLBUTTONDOWN</c> against the top-level HWND
    /// silently falls back to keyboard/cursor-track Move mode rather
    /// than mouse-driven click-drag. The polling approach is reliable
    /// but trades away OS Aero Snap during the drag. See spec 054 §5.3.
    /// </remarks>
    public Action UseWindowDragMove()
    {
        var win = Microsoft.UI.Reactor.ReactorApp.ActiveHostInternal?.OwningWindow;
        return UseMemo<Action>(() => win is null ? static () => { } : win.BeginDragMove, win!);
    }

    internal static IPickerService PickerService { get; set; } = new DefaultPickerService();

    /// <summary>
    /// Opens a file picker initialized with the owning window HWND. Must be called on the UI thread.
    /// </summary>
    public Task<global::Windows.Storage.StorageFile?> UseFilePickerAsync(FilePickerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var win = Microsoft.UI.Reactor.ReactorApp.ActiveHostInternal?.OwningWindow
            ?? throw new InvalidOperationException("UseFilePickerAsync requires an owning ReactorWindow.");
        EnsurePickerOnUiThread(win, nameof(UseFilePickerAsync));
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(win.NativeWindow);
        return PickerService.PickFileAsync(hwnd, options);
    }

    /// <summary>
    /// Opens a folder picker initialized with the owning window HWND. Must be called on the UI thread.
    /// </summary>
    public Task<global::Windows.Storage.StorageFolder?> UseFolderPickerAsync(FolderPickerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var win = Microsoft.UI.Reactor.ReactorApp.ActiveHostInternal?.OwningWindow
            ?? throw new InvalidOperationException("UseFolderPickerAsync requires an owning ReactorWindow.");
        EnsurePickerOnUiThread(win, nameof(UseFolderPickerAsync));
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(win.NativeWindow);
        return PickerService.PickFolderAsync(hwnd, options);
    }

    private static void EnsurePickerOnUiThread(Microsoft.UI.Reactor.ReactorWindow win, string hookName)
    {
        if (!win.NativeWindow.DispatcherQueue.HasThreadAccess)
            throw new InvalidOperationException($"{hookName} must be called on the owning window's UI thread.");
    }

    /// <summary>
    /// Subscribes to the host window's <c>StateChanged</c> event and re-renders
    /// on change. Returns <see cref="Microsoft.UI.Reactor.WindowState.Normal"/>
    /// outside a window. (spec 036 §7)
    /// </summary>
    public Microsoft.UI.Reactor.WindowState UseWindowState()
    {
        var win = Microsoft.UI.Reactor.ReactorApp.ActiveHostInternal?.OwningWindow;
        if (win is null) { _ = UseState(Microsoft.UI.Reactor.WindowState.Normal); return Microsoft.UI.Reactor.WindowState.Normal; }
        var (state, setState) = UseState(win.State);
        UseEffect(() =>
        {
            void handler(object? sender, Microsoft.UI.Reactor.WindowState newState) => setState(newState);
            win.StateChanged += handler;
            return () => win.StateChanged -= handler;
        }, win);
        return state;
    }

    /// <summary>
    /// Subscribes to the host window's activation events and re-renders on
    /// change. Returns <c>true</c> outside a window (the surface is "active"
    /// while shown). (spec 036 §7)
    /// </summary>
    public bool UseIsActive()
    {
        var win = Microsoft.UI.Reactor.ReactorApp.ActiveHostInternal?.OwningWindow;
        if (win is null) { _ = UseState(true); return true; }
        var (active, setActive) = UseState(win.IsActive);
        UseEffect(() =>
        {
            void onAct(object? s, EventArgs e) => setActive(true);
            void onDeact(object? s, EventArgs e) => setActive(false);
            win.Activated += onAct;
            win.Deactivated += onDeact;
            return () =>
            {
                win.Activated -= onAct;
                win.Deactivated -= onDeact;
            };
        }, win);
        return active;
    }

    /// <summary>
    /// Registers a synchronous "can the window close right now?" predicate.
    /// Multiple guards stack — any returning <c>false</c> cancels the close.
    /// Runs on the UI thread; for async confirmation, return <c>false</c> and
    /// re-trigger <see cref="Microsoft.UI.Reactor.ReactorWindow.Close"/> from
    /// the dialog callback. No-op outside a window. (spec 036 §7 / §13.4)
    /// </summary>
    public void UseClosingGuard(Func<bool> canClose)
    {
        ArgumentNullException.ThrowIfNull(canClose);
        var win = Microsoft.UI.Reactor.ReactorApp.ActiveHostInternal?.OwningWindow;
        if (win is null) { UseEffect(() => { /* no-op */ }, canClose); return; }
        UseEffect(() =>
        {
            var token = win.RegisterClosingGuard(canClose);
            return () => token.Dispose();
        }, win, canClose);
    }

    /// <summary>
    /// Returns the current per-window DPI (<see cref="Microsoft.UI.Reactor.ReactorWindow.Dpi"/>)
    /// and re-renders on DPI change. Returns the system primary-monitor DPI when
    /// called outside a window. (spec 036 §5.2)
    /// </summary>
    public uint UseDpi()
    {
        var win = Microsoft.UI.Reactor.ReactorApp.ActiveHostInternal?.OwningWindow;
        if (win is null)
        {
            _ = UseState((uint)0);
            return DpiHelpers.GetSystemDpiSafe();
        }

        var (dpi, setDpi) = UseState(win.Dpi);

        UseEffect(() =>
        {
            void handler(object? sender, uint newDpi) => setDpi(newDpi);
            win.DpiChanged += handler;
            return () => win.DpiChanged -= handler;
        }, win);

        return dpi == 0 ? win.Dpi : dpi;
    }

    /// <summary>
    /// Open or reuse a secondary window keyed by <paramref name="key"/>. Renders
    /// that pass the same <paramref name="key"/> share the same
    /// <see cref="Microsoft.UI.Reactor.ReactorWindow"/>; if the spec changes
    /// across renders the live window is updated via
    /// <see cref="Microsoft.UI.Reactor.ReactorWindow.Update"/>. The returned
    /// handle is identity-stable across renders so long as the key is stable.
    /// </summary>
    /// <remarks>
    /// <para>Unmount semantics: when the calling component unmounts, the opened
    /// window stays open. Components that want the inverse behavior must close
    /// the window explicitly — e.g. by registering a <c>UseEffect</c> cleanup
    /// that calls <see cref="Microsoft.UI.Reactor.ReactorWindow.Close"/> on the
    /// returned handle. (spec 036 §4.3 / §15.6)</para>
    /// <para><b>Note — asymmetry with <see cref="UseTrayIcon"/>:</b> tray icons
    /// are component-scoped and close on unmount; opened windows are app-scoped
    /// and survive unmount. The asymmetry is deliberate (a window is normally
    /// expected to outlive the menu item that opened it, while a tray icon
    /// belongs to the component that declared it), so the two hooks behave
    /// inversely despite the matching naming.</para>
    /// <para>Returns <c>null</c> when no UI dispatcher has been captured —
    /// happens in unit-test contexts where no <c>ReactorApp.Run</c> is in
    /// flight. In production this is unreachable.</para>
    /// </remarks>
    public Microsoft.UI.Reactor.ReactorWindow? UseOpenWindow(
        Microsoft.UI.Reactor.WindowKey key,
        Microsoft.UI.Reactor.WindowSpec spec,
        Func<Component> factory)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(factory);

        // Capture an identity-stable handle slot. Renders that pass the same
        // key reuse this slot; rekeying clears the handle so a fresh window
        // opens for the new key.
        var handleRef = UseRef<Microsoft.UI.Reactor.ReactorWindow?>(null);
        var lastKeyRef = UseRef<Microsoft.UI.Reactor.WindowKey?>(null);

        // Resolve the live window for this key. Three cases:
        //   1. Slot already holds a non-disposed window with the matching key — reuse.
        //   2. The key changed since last render — drop the slot (the old window
        //      stays open per spec §15.6) and look up by new key.
        //   3. No window matches — open one, save the handle.
        if (handleRef.Current is { } prior && lastKeyRef.Current is { } priorKey && priorKey.Equals(key))
        {
            // Reuse — but if the underlying window has been closed externally,
            // drop the stale reference so the next branch reopens.
            var snapshot = Microsoft.UI.Reactor.ReactorApp.Windows;
            bool stillOpen = false;
            for (int i = 0; i < snapshot.Count; i++)
                if (ReferenceEquals(snapshot[i], prior)) { stillOpen = true; break; }
            if (!stillOpen) handleRef.Current = null;
        }
        else
        {
            // Key changed (or first render) — clear the slot. We do NOT close
            // the previous window; the spec calls for explicit close-on-cleanup.
            handleRef.Current = null;
        }

        // Slot is empty — try a process-wide lookup by key first (the user may
        // have already opened a window with this key from elsewhere) and only
        // fall back to OpenWindow when no live window owns it.
        if (handleRef.Current is null)
        {
            var existing = Microsoft.UI.Reactor.ReactorApp.FindWindow(key);
            if (existing is not null)
            {
                handleRef.Current = existing;
            }
            else if (Microsoft.UI.Reactor.ReactorApp.UIDispatcher is not null)
            {
                // Stamp the key onto the spec so FindWindow / FindTrayIcon /
                // shutdown-policy bookkeeping work without an explicit
                // WindowSpec.Key on the caller.
                var stamped = spec.Key is null ? spec with { Key = key } : spec;
                try
                {
                    handleRef.Current = Microsoft.UI.Reactor.ReactorApp.OpenWindow(stamped, factory);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is global::System.Runtime.InteropServices.COMException)
                {
                    // No XAML application or UI dispatcher available. Hooks
                    // must not crash the calling render; the live multi-
                    // window path is exercised in selftest fixtures.
                    handleRef.Current = null;
                }
            }
            else
            {
                // No UI dispatcher captured — unit-test contexts. Hook slot
                // count must stay stable so the hook-order check passes on
                // subsequent renders.
                handleRef.Current = null;
            }
        }
        lastKeyRef.Current = key;

        // If the spec changed since the last render, push it through Update so
        // chrome stays in sync. Effect dependency is the spec record's
        // value-equality so we only call Update on real changes.
        var win = handleRef.Current;
        if (win is not null)
        {
            UseEffect(() =>
            {
                try { win.Update(spec.Key is null ? spec with { Key = key } : spec); }
                catch { /* best effort — disposed windows / threading races */ }
            }, win, spec);
        }
        else
        {
            // Keep the hook slot count stable across the no-window branch.
            UseEffect(() => { /* no-op */ }, key, spec);
        }

        return win;
    }

    /// <summary>
    /// Open (or reuse-by-<see cref="Microsoft.UI.Reactor.TrayIconSpec.Key"/>) a
    /// system-tray icon scoped to the calling component. The icon closes
    /// automatically on unmount — that's the only difference from
    /// <see cref="Microsoft.UI.Reactor.ReactorApp.OpenTrayIcon"/>, which is
    /// app-scoped and keeps the icon alive until explicit
    /// <see cref="Microsoft.UI.Reactor.ReactorTrayIcon.Close"/>.
    /// </summary>
    /// <remarks>
    /// Returns <c>null</c> when no UI dispatcher has been captured (test
    /// contexts) or when the spec change cannot be reconciled — callers
    /// should null-check before subscribing to events. Identity-stable across
    /// re-renders so the same handle wires through subsequent
    /// <c>UseEffect</c> dependencies.
    /// (spec 036 §11.4)
    /// </remarks>
    public Microsoft.UI.Reactor.ReactorTrayIcon? UseTrayIcon(Microsoft.UI.Reactor.TrayIconSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var handleRef = UseRef<Microsoft.UI.Reactor.ReactorTrayIcon?>(null);

        // Open on first render; reuse on subsequent renders if a key match
        // already exists. The hook owns the lifetime — UseEffect cleanup
        // closes the icon on unmount.
        if (handleRef.Current is null)
        {
            if (spec.Key is { } key)
            {
                handleRef.Current = Microsoft.UI.Reactor.ReactorApp.FindTrayIcon(key);
            }

            if (handleRef.Current is null && Microsoft.UI.Reactor.ReactorApp.UIDispatcher is not null)
            {
                try
                {
                    handleRef.Current = Microsoft.UI.Reactor.ReactorApp.OpenTrayIcon(spec);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is global::System.Runtime.InteropServices.COMException)
                {
                    // Shell COM unavailable in test/headless contexts.
                    handleRef.Current = null;
                }
            }
        }

        var icon = handleRef.Current;

        // Spec-change diff: re-apply only when the value-equal record
        // signature changes. Effect dependency carries the spec record.
        if (icon is not null)
        {
            UseEffect(() =>
            {
                try { icon.Update(spec); }
                catch { /* best effort */ }
            }, icon, spec);
        }
        else
        {
            // Keep slot count stable so the hook-order check passes on
            // subsequent renders even when the open failed.
            UseEffect(() => { /* no-op */ }, spec);
        }

        // Component-scoped lifetime — close on unmount. UseEffect with
        // empty deps runs once. Read the icon via handleRef inside the
        // cleanup so a late-bound icon (e.g. UIDispatcher captured after the
        // first render) still gets closed.
        UseEffect(() =>
        {
            return () =>
            {
                try { handleRef.Current?.Close(); } catch { /* best effort */ }
            };
        });

        return icon;
    }

    /// <summary>
    /// Returns true when the given window's width is >= minWidth.
    /// Re-renders when the window resizes across the breakpoint.
    /// </summary>
    public bool UseBreakpoint(Microsoft.UI.Xaml.Window window, double minWidth)
    {
        var (width, _) = UseWindowSize(window);
        return width >= minWidth;
    }

    /// <summary>
    /// Window-inferring overload — takes only <paramref name="minWidth"/> and
    /// resolves the current host's window. Returns false when called outside a
    /// window. (spec 036 §5.2)
    /// </summary>
    public bool UseBreakpoint(double minWidth)
    {
        var (width, _) = UseWindowSize();
        return width >= minWidth;
    }

    internal void FlushEffects()
    {
        // Phase 1: Run all pending cleanups from previous effects
        for (int i = 0; i < _hooks.Count; i++)
        {
            if (_hooks[i] is EffectHookState hook && hook.PendingCleanup is not null)
            {
                hook.PendingCleanup();
                hook.PendingCleanup = null;
            }
        }

        // Phase 2: Run all pending new effects
        for (int i = 0; i < _hooks.Count; i++)
        {
            if (_hooks[i] is not EffectHookState hook || !hook.Pending) continue;
            hook.Pending = false;

            if (hook.EffectWithCleanup is not null)
            {
                hook.Cleanup = hook.EffectWithCleanup();
                hook.EffectWithCleanup = null;
            }
            else if (hook.Effect is not null)
            {
                hook.Effect();
                hook.Effect = null;
            }
        }
    }

    internal void RunCleanups()
    {
        // Phase 1: Run effect cleanups. Drain BOTH the committed cleanup and any
        // staged-but-not-yet-flushed cleanup: when a render changes an effect's
        // deps it moves the old cleanup into PendingCleanup (to run at the next
        // FlushEffects). If teardown/hot-reload-reset happens before that flush
        // (e.g. a later hook threw HookOrderException), the pending cleanup would
        // otherwise leak its subscription/timer/handle. Each is null-guarded and
        // cleared so it cannot run twice.
        for (int i = 0; i < _hooks.Count; i++)
        {
            if (_hooks[i] is EffectHookState hook)
            {
                hook.PendingCleanup?.Invoke();
                hook.PendingCleanup = null;
                hook.Cleanup?.Invoke();
                hook.Cleanup = null;
            }
        }

        // Phase 2: Save persisted state to cache
        for (int i = 0; i < _hooks.Count; i++)
        {
            if (_hooks[i] is PersistedHookStateBase persisted)
            {
                persisted.SaveToCache();
            }
        }
    }

    /// <summary>
    /// Hot Reload recovery: run effect cleanups and discard the entire hook
    /// list so the next BeginRender starts with a fresh hook sequence. Used
    /// by the host when a HookOrderException surfaces during a hot-reload-
    /// triggered render — an edit that reorders or changes hook types is
    /// the expected outcome of a developer change, so we trade away state
    /// (which we cannot reliably re-key onto a new hook shape) to keep the
    /// dev loop alive instead of leaving the user staring at an error
    /// fallback.
    /// </summary>
    internal void ResetForHotReload()
    {
        RunCleanups();
        _hooks.Clear();
        _hookIndex = 0;
    }

    /// <summary>
    /// Hot Reload state migration (spec 049 §6). Called once per live context at
    /// the <em>start</em> of a hot-reload pass, before any <c>Render()</c> runs.
    /// For each value-carrying hook cell (<c>UseState</c> / <c>UseReducer</c> /
    /// <c>UseRef</c> / <c>UseMemo</c> / <c>UsePersisted</c>) whose stored value's
    /// type was reported as updated by the runtime, constructs a fresh instance
    /// of the (current) type and copies the surviving fields by name via
    /// <see cref="Microsoft.UI.Reactor.Hosting.ReactorHotReloadCopier"/>, then value-swaps it into the cell.
    /// This is a value swap, not a hook reset: <c>_hookIndex</c> is untouched and
    /// no cleanups run, so hook identity/order is preserved while the data shape
    /// catches up to the edited record/class. Effects whose deps referenced the
    /// migrated value re-run on the following render because the new instance is
    /// a different reference (spec §11 Q1) — matching normal SetState semantics.
    ///
    /// <para>Within a hot-reload pass it always resets every cell's
    /// <see cref="HookState.Migrated"/> flag first so the devtools annotation
    /// reflects only the most recent pass, even when there is nothing to
    /// migrate. Outside a pass it is a complete no-op (the guard is checked
    /// before the reset) so a stray call never wipes the prior pass's
    /// annotations. Reflection-bearing; reachable only inside a hot-reload
    /// pass, so it is statically dead under NativeAOT (spec §8).</para>
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reachable only inside a hot-reload pass; dead under NativeAOT (spec 049 §8).")]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Reachable only inside a hot-reload pass; dead under NativeAOT (spec 049 §8).")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reachable only inside a hot-reload pass; dead under NativeAOT (spec 049 §8).")]
    internal void MigrateHooksForHotReload(IReadOnlySet<Type>? updatedTypes)
    {
        // Outside a hot-reload pass this is a complete no-op so a stray call
        // never disturbs the devtools Migrated annotations from the last pass.
        if (!Microsoft.UI.Reactor.Hosting.HotReloadService.WithinUpdatePass) return;

        // Within a pass, clear stale per-pass annotations regardless of work.
        for (int i = 0; i < _hooks.Count; i++)
            _hooks[i].Migrated = false;

        if (updatedTypes is null || updatedTypes.Count == 0) return;

        for (int i = 0; i < _hooks.Count; i++)
        {
            var h = _hooks[i];
            var t = h.GetType();
            if (!t.IsGenericType) continue;
            var def = t.GetGenericTypeDefinition();
            if (def != typeof(ValueHookState<>) &&
                def != typeof(MemoHookState<>) &&
                def != typeof(PersistedHookState<>))
                continue;

            var valueField = t.GetField("Value");
            if (valueField is null) continue;

            object? oldValue = valueField.GetValue(h);
            if (oldValue is null) continue;

            Type valueType = oldValue.GetType();
            if (!FullNameMatches(updatedTypes, valueType)) continue;

            object? newInstance = Microsoft.UI.Reactor.Hosting.ReactorHotReloadCopier.CreateInstance(valueType);
            if (newInstance is null) continue; // no usable ctor — keep old value.

            Microsoft.UI.Reactor.Hosting.ReactorHotReloadCopier.TryMigrate(
                oldValue, newInstance,
                new HashSet<object>(ReferenceEqualityComparer.Instance));

            try
            {
                valueField.SetValue(h, newInstance);
                h.Migrated = true;
                Diagnostics.ReactorEventSource.Log.HotReloadStateMigrated(
                    valueType.FullName ?? valueType.Name);
            }
            catch (ArgumentException)
            {
                // The cell's generic argument is a distinct Type that merely
                // shares a FullName with the edited shape (the runtime minted a
                // new Type token rather than editing in place). The new instance
                // is not assignable to the old cell — leave the old value rather
                // than corrupt the hook. Tracked as a known headless-irreproducible
                // limitation in spec 049 §6.
            }
        }
    }

    private static bool FullNameMatches(IReadOnlySet<Type> updatedTypes, Type candidate)
    {
        if (updatedTypes.Contains(candidate)) return true;
        foreach (var u in updatedTypes)
            if (u.FullName is not null && u.FullName == candidate.FullName) return true;
        return false;
    }

    private static bool DepsEqual(object[] prev, object[] next)
    {
        if (prev.Length != next.Length) return false;
        for (int i = 0; i < prev.Length; i++)
        {
            if (!Equals(prev[i], next[i])) return false;
        }
        return true;
    }

    /// <summary>
    /// Devtools-only: returns a snapshot of this context's hook table for
    /// <c>reactor.state</c>. Private hook-cell types are unpacked here where
    /// we have access; devtools code consumes the boxed values and does the
    /// JSON shaping. Must be called on the UI dispatcher.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "SnapshotHooks uses reflection on internal hook state types.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "SnapshotHooks uses reflection on internal hook state types.")]
    internal IReadOnlyList<HookSnapshot> SnapshotHooks()
    {
        var list = new List<HookSnapshot>(_hooks.Count);
        for (int i = 0; i < _hooks.Count; i++)
        {
            var h = _hooks[i];
            var t = h.GetType();
            string hookName;
            Type? valueType = null;
            object? value = null;

            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                if (def == typeof(ValueHookState<>))
                {
                    valueType = t.GetGenericArguments()[0];
                    value = t.GetField("Value")!.GetValue(h);
                    // UseRef uses the same cell, but its value is a Ref<T>.
                    hookName = valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(Ref<>)
                        ? "useRef"
                        : "useState";
                }
                else if (def == typeof(MemoHookState<>))
                {
                    valueType = t.GetGenericArguments()[0];
                    value = t.GetField("Value")!.GetValue(h);
                    hookName = "useMemo";
                }
                else if (def == typeof(PersistedHookState<>))
                {
                    valueType = t.GetGenericArguments()[0];
                    value = t.GetField("Value")!.GetValue(h);
                    hookName = "usePersisted";
                }
                else
                {
                    hookName = t.Name;
                }
            }
            else if (h is EffectHookState)
            {
                hookName = "useEffect";
            }
            else if (h is ContextHookState ch)
            {
                hookName = "useContext";
                value = ch.LastValue;
                valueType = value?.GetType();
            }
            else if (h is NavigationLifecycleHookState)
            {
                hookName = "useNavigationLifecycle";
            }
            else
            {
                hookName = t.Name;
            }

            list.Add(new HookSnapshot(i, hookName, valueType, value, h.Migrated));
        }
        return list;
    }

    internal abstract class HookState
    {
        // Q3 (spec 049 §3.6): set true by MigrateHooksForHotReload when this
        // cell's value was value-swapped during the most recent hot-reload
        // pass; surfaced by SnapshotHooks -> reactor.state. Reset to false at
        // the start of every hot-reload pass so it reflects only that pass.
        public bool Migrated;
    }

    private class ValueHookState<T> : HookState
    {
        public T Value;
        public readonly bool ThreadSafe;
        // Issue #659 (#42): only allocate the lock when threadSafe was requested.
        // The default (false) path never touches Lock, so most state cells now
        // carry no per-hook Lock object.
        public readonly object? Lock;
        // Issue #659 (#43/#44): the ref-stable setter/updater/dispatch delegate,
        // built once on first render and reused every render thereafter (was a
        // fresh closure per render). Typed as Delegate so one field serves
        // UseState (Action<T>), UseReducer<T> (Action<Func<T,T>>), and the
        // two-arg UseReducer dispatch (Action<TAction>).
        public Delegate? Setter;
        // Issue #659 review (LOW): discriminates which hook flavour owns Setter.
        // UseState<int> and UseReducer<int,int> both produce an Action<int>, so a
        // type-only `is` check could reuse the wrong delegate across an (invalid,
        // rules-of-hooks-forbidden) same-slot swap. Mismatched kind re-materializes
        // the correct delegate rather than returning the wrong one.
        public byte SetterKind;
        // Issue #659 (#44): latest reducer for the two-arg UseReducer, refreshed
        // every render so the cached dispatch reads the current reducer.
        public object? Reducer;
        public ValueHookState(T value, bool threadSafe = false)
        {
            Value = value;
            ThreadSafe = threadSafe;
            Lock = threadSafe ? new object() : null;
        }
    }

    private class EffectHookState : HookState
    {
        public object[]? Dependencies;
        public Action? Effect;
        public Func<Action>? EffectWithCleanup;
        public Action? Cleanup;
        public Action? PendingCleanup;
        public bool Pending;
    }

    private class MemoHookState<T> : HookState
    {
        public T Value = default!;
        public object[]? Dependencies;
    }

    internal class ContextHookState : HookState
    {
        public ContextBase Context = default!;
        public object? LastValue;
    }

    internal class NavigationLifecycleHookState : HookState
    {
        public Action<Navigation.NavigatingToContext>? OnNavigatingTo;
        public Action<Navigation.NavigatedToContext>? OnNavigatedTo;
        public Action<Navigation.NavigatingFromContext>? OnNavigatingFrom;
        public Action<Navigation.NavigatedFromContext>? OnNavigatedFrom;
    }

    internal abstract class PersistedHookStateBase : HookState
    {
        public string PersistKey = default!;
        // Resolved at hook-construction time. Null is valid: indicates "no
        // backing store available" (e.g. PersistedScope.Window outside a
        // window in a unit-test context). When null, save-on-cleanup is a
        // no-op.
        public IPersistedStateScope? Scope;
        public abstract void SaveToCache();
    }

    private class PersistedHookState<T> : PersistedHookStateBase
    {
        public T Value;
        // Issue #659 (#53): ref-stable setter cached on first render.
        public Action<T>? CachedSetter;
        public PersistedHookState(T value) => Value = value;
        public override void SaveToCache()
        {
            if (Scope is null) return;
            Scope.Set(PersistKey, Value);
        }
    }

    /// <summary>
    /// Resolve a <see cref="PersistedScope"/> selector to a concrete
    /// <see cref="IPersistedStateScope"/>. <see cref="PersistedScope.Window"/>
    /// prefers the active host's window scope; falls back to the application
    /// scope when no window owns the host (test fixtures, headless renders).
    /// </summary>
    private static IPersistedStateScope? ResolvePersistedScope(PersistedScope scope)
    {
        switch (scope)
        {
            case PersistedScope.Window:
                var win = Microsoft.UI.Reactor.ReactorApp.ActiveHostInternal?.OwningWindow;
                if (win is not null)
                    return win.PersistedScope;
                // Fall back to the process-wide scope so unit tests that
                // exercise UsePersisted without a Window keep working. The
                // legacy two-arg overload defaults to PersistedScope.Application
                // anyway — only callers that explicitly opted into Window
                // scope land here.
                return ApplicationPersistedScope.Default;
            case PersistedScope.Application:
            default:
                return ApplicationPersistedScope.Default;
        }
    }
}

/// <summary>
/// Per-slot snapshot of a <see cref="RenderContext"/>'s hook table, produced by
/// <see cref="RenderContext.SnapshotHooks"/> for devtools inspection. The
/// <c>Value</c> is the live boxed hook value; serialization shaping happens in
/// the devtools state tool.
/// </summary>
internal readonly record struct HookSnapshot(int Index, string Hook, Type? ValueType, object? Value, bool Migrated = false);

/// <summary>
/// A mutable reference that persists across renders (like React's useRef).
/// </summary>
public class Ref<T>
{
    public T Current { get; set; }
    public Ref(T initial) => Current = initial;
}
