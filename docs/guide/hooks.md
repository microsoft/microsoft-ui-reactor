
Hooks are the reactivity contract for a [component](components.md). A hook is a
positional slot — when `Render()` runs, Microsoft.UI.Reactor (Reactor) walks each `Use*` call in order
and looks up the matching cell in a per-component slot table held on the
[`RenderContext`](hooks-internals.md). The first [UseState](reference/hooks/UseState.md) call is
slot 0, the second is slot 1, and so on; the setter the hook returns closes
over its slot index and writes back to that cell when you call it. Hooks
replace what classic XAML / WPF apps build with `DependencyProperty`,
`INotifyPropertyChanged`, view models, and lifecycle methods — instead of
those four mechanisms, you keep state with `UseState`, derive values with
`UseMemo`, run side effects with [`UseEffect`](effects.md), share data
without prop drilling via [`UseContext`](context.md), and persist values
across launches with [`UsePersisted`](persistence.md). Every hook on this
page reads as a function call inside `Render()` and writes back through a
setter closure; understanding that single shape makes the rest of Reactor
fall out as composition.

# Hooks

Hooks are functions you call inside [`Render()`](components.md) to manage state, side effects,
and memoization. They replace the need for view models, event handlers, and
lifecycle methods.

## Reference

| Hook | Returns | Purpose |
|------|---------|---------|
| [UseState](reference/hooks/UseState.md) | `(T value, Action<T> set)` | Reactive state — re-renders on `set`. |
| [UseReducer](reference/hooks/UseReducer.md) | `(T value, Action<Func<T,T>> update)` *or* `(TState, Action<TAction>)` | Functional or Redux-style updates. |
| [UseEffect](reference/hooks/UseEffect.md) | `void` | Side effects after commit. With `Func<Action>` overload, runs the cleanup before the next effect and on unmount. |
| [UseMemo](reference/hooks/UseMemo.md) | `T` | Cached computation; re-runs when any `deps` entry compares unequal. |
| [UseRef](reference/hooks/UseRef.md) | `Ref<T>` with mutable `.Current` | Persists across renders **without** re-rendering on change. |
| [UseCallback](reference/hooks/UseCallback.md) | `Action` | Stable delegate identity across renders. |
| [UseContext](reference/hooks/UseContext.md) | `T` | Read the ambient [Context](context.md) value. |
| [UseObservable](reference/hooks/UseObservable.md) | `T` | Re-render when a tracked `INotifyPropertyChanged` source raises a change. |
| [UseResource](reference/hooks/UseResource.md) | `AsyncValue<T>` | Cached async read (see [Async Resources](async-resources.md)). |
| [UsePersisted](reference/hooks/UsePersisted.md) | `(T, Action<T>)` | `UseState` that survives app launches (see [Persistence](persistence.md)). |

Every hook on this page is summarized again in the auto-generated
[hooks reference](reference/hooks/index.md); the rest of the page is the
narrative.

## UseState

The most common hook. Returns the current value and a setter function:

```csharp
class StateDemo : Component
{
    public override Element Render()
    {
        var (color, setColor) = UseState("#0078D4");
        var (size, setSize) = UseState(20.0);

        return VStack(12,
            SubHeading("UseState"),
            TextBlock("Sample text").FontSize(size).Foreground(color),
            TextBox(color, setColor, placeholderText: "#hex color")
                .Width(150),
            HStack(8,
                TextBlock("Size:"),
                Slider(size, 10, 48, setSize).Width(200)
            )
        );
    }
}
```

![UseState demo](images/hooks/usestate.png)

Call `setColor("#FF0000")` and Reactor re-renders the component with the new
value. The setter also accepts a function: `setSize(s => s + 1)` — this is
safer when the update depends on the previous value.

## UseReducer (Functional)

When your new state depends on the old state, `UseReducer` is cleaner than
`UseState`. The updater receives a `Func<T, T>` — a function that transforms
the previous value:

```csharp
class ReducerDemo : Component
{
    public override Element Render()
    {
        var (items, updateItems) = UseReducer(new List<string>());
        var (input, setInput) = UseState("");

        return VStack(12,
            SubHeading("UseReducer"),
            HStack(8,
                TextBox(input, setInput, placeholderText: "Add item")
                    .Width(180),
                Button("Add", () =>
                {
                    if (string.IsNullOrWhiteSpace(input)) return;
                    updateItems(list =>
                        new List<string>(list) { input });
                    setInput("");
                }),
                Button("Clear", () =>
                    updateItems(_ => new List<string>()))
            ),
            ForEach(items, item => TextBlock($"  - {item}"))
        );
    }
}
```

![UseReducer demo](images/hooks/usereducer.png)

`updateItems(list => new List<string>(list) { input })` appends to the list
by creating a new copy. This avoids mutation bugs — you always produce a new
value from the old one.

## UseReducer (Redux-Style)

For complex state with multiple action types, use the Redux-style overload.
Define a state record, action types, and a reducer function:

```csharp
record CounterState(int Count, string LastAction);
abstract record CounterAction;
record Increment : CounterAction;  record Decrement : CounterAction;
record Reset : CounterAction;

class ReduxReducerDemo : Component
{
    public override Element Render()
    {
        var (state, dispatch) = UseReducer(
            (CounterState s, CounterAction a) => a switch {
                Increment => s with { Count = s.Count + 1, LastAction = "+" },
                Decrement => s with { Count = s.Count - 1, LastAction = "-" },
                Reset => new(0, "reset"), _ => s
            }, new CounterState(0, "none"));

        return VStack(8,
            SubHeading("UseReducer (Redux-style)"),
            TextBlock($"Count: {state.Count}  (last: {state.LastAction})")
                .FontSize(18).Bold(),
            HStack(8,
                Button("-", () => dispatch(new Decrement())),
                Button("Reset", () => dispatch(new Reset())),
                Button("+", () => dispatch(new Increment()))
            )
        );
    }
}
```

![Redux-style reducer](images/hooks/usereducer-redux.png)

The reducer `(state, action) => newState` is a pure function. Each action type
maps to a state transformation. This pattern scales well — adding new actions
doesn't change existing logic.

## UseEffect

Run side effects (timers, subscriptions, async work) after a render. The
dependencies array controls when the effect re-runs:

```csharp
class EffectDemo : Component
{
    public override Element Render()
    {
        var (seconds, updateSeconds) = UseReducer(0);
        var (running, setRunning) = UseState(false);

        UseEffect(() =>
        {
            if (!running) return () => { };
            var cts = new CancellationTokenSource();
            var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            _ = Task.Run(async () =>
            {
                while (await timer.WaitForNextTickAsync(cts.Token))
                    updateSeconds(s => s + 1);
            });
            return () => { cts.Cancel(); timer.Dispose(); };
        }, running);

        return VStack(8,
            SubHeading("UseEffect"),
            TextBlock($"Elapsed: {seconds}s").FontSize(18),
            HStack(8,
                Button(running ? "Stop" : "Start", () => setRunning(!running)),
                Button("Reset", () => updateSeconds(_ => 0))
            )
        );
    }
}
```

![UseEffect timer](images/hooks/useeffect.png)

Key details:

- The effect runs **after** the render completes, not during.
- Return a **cleanup function** to dispose resources. Reactor calls it before
  re-running the effect and when the component unmounts.
- **Empty dependencies** `UseEffect(() => { ... })` — runs once on mount.
- **With dependencies** `UseEffect(() => { ... }, running)` — runs when
  `running` changes.

## UseMemo

Cache an expensive computation so it only recalculates when its inputs change:

```csharp
class MemoDemo : Component
{
    public override Element Render()
    {
        var (input, setInput) = UseState("Hello, Reactor!");

        var stats = UseMemo(() => new
        {
            Chars = input.Length,
            Words = input.Split(' ',
                StringSplitOptions.RemoveEmptyEntries).Length,
            Upper = input.ToUpperInvariant()
        }, input);

        return VStack(8,
            SubHeading("UseMemo"),
            TextBox(input, setInput).Width(250),
            TextBlock($"Characters: {stats.Chars}, Words: {stats.Words}"),
            Caption($"Uppercased: {stats.Upper}")
        );
    }
}
```

![UseMemo demo](images/hooks/usememo.png)

`UseMemo` compares the dependency values between renders. If they haven't
changed, it returns the cached result. Use it for string processing, filtering
large lists, or any computation you don't want to repeat every render.

## UseRef

Store a mutable value that persists across renders without triggering
re-renders:

```csharp
class RefDemo : Component
{
    public override Element Render()
    {
        var (value, setValue) = UseState("");
        var renderCount = UseRef(0);
        renderCount.Current++;

        return VStack(8,
            SubHeading("UseRef"),
            TextBlock($"Render count: {renderCount.Current}").SemiBold(),
            TextBox(value, setValue, placeholderText: "Type to trigger renders")
                .Width(250),
            Caption("UseRef persists across renders without causing them")
        );
    }
}
```

![UseRef demo](images/hooks/useref.png)

`UseRef` returns a `Ref<T>` with a `.Current` property. Changing `.Current`
does **not** cause a re-render. This is useful for:

- Counting renders
- Storing previous values for comparison
- Holding references to timers or cancellation tokens

## UseCallback

Stabilize a callback reference so child components don't re-render
unnecessarily:

```csharp
class CallbackDemo : Component
{
    public override Element Render()
    {
        var (count, updateCount) = UseReducer(0);
        var (label, setLabel) = UseState("Click me");

        var stableIncrement = UseCallback(
            () => updateCount(c => c + 1), Array.Empty<object>());

        return VStack(8,
            SubHeading("UseCallback"),
            TextBlock($"Count: {count}").FontSize(18),
            TextBox(label, setLabel, placeholderText: "Button label")
                .Width(200),
            Button(label, stableIncrement),
            Caption("The callback identity stays stable across renders")
        );
    }
}
```

![UseCallback demo](images/hooks/usecallback.png)

Without `UseCallback`, the lambda `() => setCount(c => c + 1)` would be a new
object every render. `UseCallback` returns the same delegate instance as long
as the dependencies haven't changed. This matters when passing callbacks to
memoized [child components](components.md).

## Updating State From Background Work

Once the host is bootstrapped, `UseState` and `UseReducer` setters are safe to
call from any thread. When you invoke a setter from a background task — inside
`Task.Run`, from a `PeriodicTimer` loop, from a network callback, or after
`await ... ConfigureAwait(false)` — the setter automatically marshals the write
and the resulting re-render onto the UI dispatcher. You write the same code
you'd write on the UI thread:

<!-- ai:lock -->
```csharp
public override Element Render()
{
    var (seconds, setSeconds) = UseState(0);

    UseEffect(() =>
    {
        var cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(cts.Token))
                setSeconds(s => s + 1);   // auto-marshals to the UI thread
        });
        return () => cts.Cancel();
    });

    return TextBlock($"Elapsed: {seconds}s");
}
```
<!-- /ai:lock -->

Each cross-thread setter call costs one `DispatcherQueue.TryEnqueue` —
microseconds, not free, but vastly cheaper than the bugs you'd hit writing the
field directly from a worker. If you need many concurrent setters to apply
in-place rather than serialize through the UI thread (typical for ingest loops
that hammer the same hook from multiple producers), pass `threadSafe: true` to
the hook:

<!-- ai:lock -->
```csharp
var (count, setCount) = UseState(0, threadSafe: true);
var (sum, addToSum) = UseReducer(0, threadSafe: true);
```
<!-- /ai:lock -->

`threadSafe: true` switches the hook to a per-cell lock: concurrent writers
serialize on the lock instead of queuing through the UI dispatcher, and reads
inside the setter (the `prev` argument of a reducer) see the latest committed
write rather than a snapshot from the last UI tick.

> **When auto-marshal can't help.** The setter needs a captured
> `ReactorApp.UIDispatcher` to marshal onto. In unit-test / headless contexts
> that drive `RenderContext` directly, or before the first host has been
> bootstrapped, a cross-thread setter call throws `InvalidOperationException`
> instead of silently racing. The setter also throws if the dispatcher refuses
> the marshaled call (e.g., during shutdown). Cancel background producers in
> your effect cleanup so they stop before the window closes.

## Hook Rules

Hooks must be called **in the same order** every render. Reactor tracks hooks by
their position in the call sequence — the first `UseState` call always maps to
the first state slot, the second to the second, and so on. The internal walk
of `_hookIndex` against `_hooks[currentIndex]` is described in
[Hooks Internals](hooks-internals.md).

**Do:**
<!-- ai:lock -->
```csharp
public override Element Render()
{
    var (a, setA) = UseState(0);     // always first
    var (b, setB) = UseState("");    // always second
    UseEffect(() => { ... }, a);     // always third
    return Text($"{a} {b}");
}
```
<!-- /ai:lock -->

**Don't:**
<!-- ai:lock -->
```csharp
public override Element Render()
{
    var (a, setA) = UseState(0);
    if (a > 0)
        UseEffect(() => { ... }, a);  // WRONG: conditional hook
    return Text($"{a}");
}
```
<!-- /ai:lock -->

Put the condition **inside** the hook instead:
<!-- ai:lock -->
```csharp
UseEffect(() => { if (a > 0) { /* ... */ } }, a);
```
<!-- /ai:lock -->

> **Caveat:** Calling a hook inside an `if`, `for`, `while`, `switch`, or `try` changes the
> slot index for every hook that follows on any render that takes the branch.
> The next render then asks slot `N` for the type the *unbranched* call shape
> expects — `ValueHookState<int>` vs. `EffectHookState`, say — and the slot
> table guard at `RenderContext.UseState` throws
> `HookOrderException("Hook at index N is EffectHookState, expected
> ValueHookState<Int32> (UseState). Hooks must be called in the same order
> every render.")`. The Roslyn analyzer `REACTOR_HOOKS_001` flags the literal
> pattern — `Use*` inside a control-flow construct in a `Render` override or
> a `Use*`-prefixed custom hook — at compile time as a Warning. The analyzer
> can't see calls through lambdas, helper functions whose names don't start
> with `Use`, or pattern-matched dispatch, so the runtime guard is the
> backstop. When you hit the exception, look for a `Use*` call that's
> conditionally reached — typically the new one you just added.

## Patterns

### Custom hooks

A custom hook is any method whose name starts with `Use` that calls other
hooks inside it. The analyzer treats `Use*` methods as legitimate hook
contexts, so you can compose `UseState`, `UseEffect`, and friends into a
named, reusable bundle without losing the rules.

```csharp
static (string Value, Action<string> Set) UseDebouncedText(string initial, int ms)
{
    var ctx = RenderContext.Current;
    var (value, setValue) = ctx.UseState(initial);
    var (debounced, setDebounced) = ctx.UseState(initial);

    ctx.UseEffect(() =>
    {
        var cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(ms, cts.Token); setDebounced(value); }
            catch (OperationCanceledException) { }
        });
        return () => cts.Cancel();
    }, value);

    return (debounced, setValue);
}
```

The hook owns three slots — two `UseState` and one `UseEffect` — and the
caller still gets the simple `(value, setter)` shape they'd get from
`UseState`. The compiled [Rules of Reactor](rules-of-reactor.md) page
catalogs the full set of custom-hook conventions.

### Lifted state

When a parent and child both need to read the same value, hoist the
`UseState` to the parent and pass `(value, setter)` down as a prop. The
[recipes/master-detail](recipes/master-detail.md) walkthrough shows the
classic shape — the master list and the detail panel both react to the
shared selection state. This is the same pattern XAML developers reach
for with shared view models; here the state lives in the parent
[component](components.md), the children are reactive consumers.

### Deferred value via UseRef

`UseRef` is the right tool when a value needs to survive renders but
must **not** trigger them. Storing the previous prop value for diffing,
holding a `CancellationTokenSource`, or counting renders for diagnostics
all belong in a ref:

```csharp
var prev = UseRef<int?>(null);
UseEffect(() => { /* compare prev.Current to current */ prev.Current = current; }, current);
```

The setter writes to `.Current` immediately without scheduling a render —
contrast with `UseState`, where every setter call queues a re-render
through the dispatcher.

## Common Mistakes

### Hooks inside conditionals

```csharp
// Don't:
public override Element Render()
{
    var (open, setOpen) = UseState(false);
    if (open)
    {
        UseEffect(() => Subscribe(), Array.Empty<object>()); // REACTOR_HOOKS_001
    }
    return ...;
}
```

The effect's slot index moves by one on every render where `open` flips.
The next render finds `EffectHookState` where it expected `ValueHookState`
and throws `HookOrderException`. The fix is to call the hook
unconditionally and put the condition **inside** it:

```csharp
UseEffect(() => { if (!open) return () => { }; return Subscribe(); }, open);
```

### Stale closures

```csharp
// Don't:
var (count, setCount) = UseState(0);
UseEffect(() =>
{
    var t = new Timer(_ => setCount(count + 1), null, 0, 1000);
    return () => t.Dispose();
}, Array.Empty<object>()); // captured `count` is forever 0
```

The effect's empty deps array means it captures `count` once at mount.
The timer fires forever with the stale closure, so the counter sticks at
1. The fix is the functional-setter pattern — `setCount(c => c + 1)` —
which reads the live cell value instead of the captured variable. The
`Func<T,T>` overload of `UseState` and the entire `UseReducer` API are
built around this.

### Setter chain that should be `set(prev => ...)`

```csharp
// Don't:
Button("+3", () => { setCount(count + 1); setCount(count + 1); setCount(count + 1); });
```

The three setter calls all read the same captured `count` and all write
`count + 1` — the button advances by one, not three. Use the functional
form so each call sees the previous setter's result:

```csharp
Button("+3", () => { setCount(c => c + 1); setCount(c => c + 1); setCount(c => c + 1); });
```

This is the same Reactor-wide rule as the [stale closure](#stale-closures)
pattern: when an update derives from the previous value, the functional
setter is the right shape. The auto-marshal path for cross-thread setters
described above relies on the same overload — every queued write reads
the latest committed value, not a snapshot.

## Tips

**Use the functional setter for derived updates.** `setCount(c => c + 1)` is
safer than `setCount(count + 1)` when multiple updates might batch together.

**Always return cleanup from effects that create resources.** Timers,
subscriptions, and event handlers must be disposed. The cleanup function is
your only chance to do it.

**Don't overuse UseMemo.** Simple expressions like `$"{first} {last}"` are
cheap. Only memoize when the computation is genuinely expensive or the result
is passed as a dependency elsewhere.

**UseRef is not for UI values.** If changing a value should update the screen,
use `UseState`. `UseRef` is for bookkeeping that doesn't affect rendering.

**Keep effects focused.** One effect per concern. Don't combine a timer and an
API call in the same `UseEffect` — split them into separate hooks with their
own dependency arrays. See [Effects and Lifecycle](effects.md) for advanced patterns.

## Next Steps

- **[Layout](layout.md)** — Next: arrange your UI with VStack, HStack, Grid, and responsive patterns
- **[Components](components.md)** — Previous: component classes, props, and composition
- **[Effects and Lifecycle](effects.md)** — Advanced UseEffect patterns, cleanup, and async work
- **[Context](context.md)** — Share state across the tree without prop drilling
- **[Hooks Internals](hooks-internals.md)** — How the slot table actually works under the surface
- **[Persistence](persistence.md)** — `UsePersisted` for state that survives launches
- **[Rules of Reactor](rules-of-reactor.md)** — Hook rules, idioms, and anti-patterns in one place
