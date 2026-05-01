# State & Hooks Reference

Reactor uses a hooks system inspired by React. All state is managed through hook methods called during `Render()`. Hooks keep state co-located with render logic — no separate ViewModel classes, no `INotifyPropertyChanged` boilerplate.

## How hooks work internally

Each `Component` has a `RenderContext` that stores hook state in a `List<HookState>` indexed by **call order**. During each render:

1. The hook index resets to 0
2. Each hook call reads or creates the entry at the current index
3. The index advances
4. After render, effects are flushed

This is why **hook rules** must be followed:

- **Same order every render** — no conditional hooks, no hooks in loops with varying iteration counts
- **Only call hooks from Render()** — or from within a function component body (`Func(ctx => ...)`)

Violating these rules causes hooks to read the wrong state slot, producing subtle bugs.

## UseState\<T\>

The primary hook. Declares a reactive value with a setter that triggers re-render.

```csharp
var (count, setCount) = UseState(0);
var (name, setName) = UseState("Alice");
var (items, setItems) = UseState(new List<string>());
```

**How it works:**
- First render: creates a `HookState` entry storing the initial value
- Subsequent renders: returns the stored value (initial value is ignored)
- Calling the setter: stores the new value and calls `RequestRender()` on the host
- Multiple setter calls in one synchronous block are **batched** into a single re-render via `DispatcherQueue`

**Common patterns:**

```csharp
// Toggle
setVisible(!visible);

// Update from previous (use UseReducer if you need this often)
setCount(count + 1);

// Replace a collection
setItems(new List<string>(items) { "new item" });
```

## UseReducer\<T\>

Like `UseState`, but the setter takes a `Func<T, T>` — a function from the previous value to the next value. Useful for complex state updates.

```csharp
var (items, updateItems) = UseReducer(new List<string>());

// Add an item (receives previous list, returns new list)
updateItems(list => [.. list, "new item"]);

// Remove an item
updateItems(list => list.Where(x => x != "remove me").ToList());

// Toggle a boolean
var (open, toggleOpen) = UseReducer(false);
toggleOpen(prev => !prev);
```

**Why UseReducer over UseState:** When you need the *previous* value to compute the *next* value, UseReducer guarantees you get the latest state, even if multiple updates are batched. With UseState, the `count` variable is captured at render time — calling `setCount(count + 1)` twice in one handler only increments by 1.

## UseEffect

Runs side effects after render. Supports dependency tracking and cleanup.

```csharp
// Run once on mount (no dependencies = runs once)
UseEffect(() => {
    Console.WriteLine("Component mounted");
});

// Run when dependencies change
UseEffect(() => {
    Console.WriteLine($"Count changed to {count}");
}, count);

// With cleanup (returned action runs before next effect or on unmount)
UseEffect(() => {
    var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
    var cts = new CancellationTokenSource();
    _ = Task.Run(async () => {
        while (await timer.WaitForNextTickAsync(cts.Token))
            setCount(c => c + 1);
    });
    return () => {
        cts.Cancel();
        timer.Dispose();
    };
});
```

**Dependency tracking:** Effects compare dependencies using `object.Equals`. If all deps are the same as the previous render, the effect is skipped. If any dep changed, the cleanup from the previous effect runs first, then the new effect runs.

**Timing:** Effects run *after* the reconciler has patched the WinUI controls (during `FlushEffects()`). This means the UI is already updated when your effect runs.

## UseMemo\<T\>

Memoizes a computed value. Only recomputes when dependencies change.

```csharp
var sorted = UseMemo(() => items.OrderBy(x => x.Name).ToList(), items);
var filtered = UseMemo(() => items.Where(x => x.IsActive).ToList(), items, showActive);
```

**When to use:** For expensive computations (sorting, filtering, complex transforms) that would be wasteful to repeat on every render.

**When not to use:** For cheap operations. Adding `UseMemo` for something like string concatenation adds complexity without benefit.

## UseCallback

Returns a stable action reference that only changes when dependencies change.

```csharp
var onSubmit = UseCallback(() => {
    SubmitForm(name, email);
}, name, email);
```

**When to use:** When passing callbacks to child components where reference stability matters for preventing unnecessary re-renders.

## UseRef\<T\>

A mutable reference that persists across renders without triggering re-render when changed.

```csharp
var renderCount = UseRef(0);
renderCount.Value++;  // Does NOT trigger re-render

var previousName = UseRef("");
UseEffect(() => {
    previousName.Value = name;  // Track previous value
}, name);
```

**Key difference from UseState:** Mutating a `Ref<T>` does not trigger re-render. Use it for values you need to persist but that don't affect the UI.

## UseWindowSize

Tracks the window dimensions reactively.

```csharp
var (width, height) = UseWindowSize(window);

return width > 800
    ? HStack(Sidebar(), MainContent())
    : VStack(MainContent());
```

## UseBreakpoint

Breakpoint-driven responsive layouts.

```csharp
var isWide = UseBreakpoint(window, minWidth: 1024);
var isMedium = UseBreakpoint(window, minWidth: 768);
```

## UseObservable\<T\>

Bridges to MVVM — subscribes to `INotifyPropertyChanged` and re-renders on changes.

```csharp
var viewModel = UseRef(new MyViewModel());
var data = UseObservable(viewModel.Value);
```

## UseCollection\<T\>

Observes an `ObservableCollection<T>` and re-renders when items are added, removed, or changed.

```csharp
var collection = UseRef(new ObservableCollection<string>());
var items = UseCollection(collection.Value);
```

## The render cycle

Understanding the full render cycle helps when debugging:

```
1. State setter called (e.g., setCount(5))
   └─ RequestRender() enqueued via DispatcherQueue

2. DispatcherQueue fires (batched — multiple setters collapse into one render)
   └─ ReactorHost.RenderLoop()

3. Render phase
   └─ component.Render() called
   └─ Hooks execute in order, reading/creating state
   └─ Returns new Element tree

4. Reconciliation phase
   ├─ Reconciler diffs old vs. new Element trees
   └─ Patches applied to WinUI controls (mount/update/unmount)

5. Effect phase
   └─ RenderContext.FlushEffects()
   └─ Changed effects run (cleanup first, then new effect)
```

## State batching

Multiple state changes in one synchronous block produce a single re-render:

```csharp
// This triggers ONE re-render, not three
setName("Bob");
setAge(30);
setEmail("bob@example.com");
```

Batching works because `RequestRender()` enqueues via `DispatcherQueue.TryEnqueue()`. The render doesn't happen until the current synchronous block completes and the dispatcher processes the queue.

## Function components and hooks

Function components get their own `RenderContext`, so they have independent hook state:

```csharp
var counter = Func(ctx =>
{
    var (count, setCount) = ctx.UseState(0);  // Note: ctx.UseState, not UseState
    return Button($"Clicked {count}x", () => setCount(count + 1));
});

// Each instance has independent state
return VStack(counter, counter);
```

In class components, hooks are called directly (they're methods on `Component`, which delegates to `this.Context`):

```csharp
class MyWidget : Component
{
    public override Element Render()
    {
        var (value, setValue) = UseState(0);  // Direct call — uses this.Context
        return Text($"{value}");
    }
}
```
