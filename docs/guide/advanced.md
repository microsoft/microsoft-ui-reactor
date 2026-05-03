
# Advanced Patterns

This page covers escape hatches and performance tools for when the standard
declarative API is not enough: error boundaries for resilience, `Memo` for
render skipping, `.Set()` for raw WinUI access, and observable
[hooks](hooks.md) for bridging to MVVM view models. For hosting Reactor
components inside WinForms applications, see
[WinForms Interop](winforms-interop.md). For data grids with sort, filter,
and editing, see [Data System](data-system.md).

## Error Boundary

`ErrorBoundary` wraps a subtree and catches exceptions during rendering.
Instead of crashing the app, it displays a fallback element:

```csharp
class ErrorBoundaryDemo : Component
{
    public override Element Render()
    {
        return VStack(12,
            SubHeading("Error Boundary"),
            ErrorBoundary(
                Component<BuggyComponent>(),
                (Exception ex) => VStack(8,
                    TextBlock("Something went wrong").Bold()
                        .Foreground("#d13438"),
                    TextBlock(ex.Message).FontSize(12).Opacity(0.7)
                ).Padding(12)
                 .Background("#fde7e9")
                 .CornerRadius(8)
            )
        ).Padding(24);
    }
}

class BuggyComponent : Component
{
    public override Element Render()
    {
        var (crash, setCrash) = UseState(false);
        if (crash) throw new InvalidOperationException("Oops!");
        return Button("Click to crash", () => setCrash(true));
    }
}
```

![Error boundary with fallback](images/advanced/error-boundary.png)

The first argument is the child subtree. The second is a fallback — either a
static element or a function that receives the `Exception`. When the
`ErrorBoundary` re-renders (e.g., its parent updates), it retries the child.
This gives the user a chance to recover by changing state that caused the
crash.

## Memo

`Memo` skips re-rendering an entire subtree when its dependencies have not
changed. The parent can re-render freely — the memoized subtree stays frozen:

```csharp
class MemoSubtreeDemo : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        var (label, setLabel) = UseState("Expensive");

        return VStack(12,
            SubHeading("Memo"),
            TextBlock($"Parent renders: click count = {count}"),
            Button("Increment", () => setCount(count + 1)),
            Memo(ctx =>
            {
                // This subtree only re-renders when label changes
                return Border(
                    VStack(4,
                        TextBlock($"Memoized: {label}").Bold(),
                        TextBlock("Skips re-render when deps unchanged")
                            .FontSize(12).Opacity(0.6)
                    ).Padding(12)
                ).Background("#f0f0f0").CornerRadius(8);
            }, label)
        ).Padding(24);
    }
}
```

![Memo skipping re-render](images/advanced/memo-subtree.png)

`Memo` takes a render function and a list of dependencies. The subtree only
re-renders when at least one dependency changes (by reference equality).
Clicking "Increment" re-renders the parent, but the memoized border stays
untouched because `label` has not changed.

Use `Memo` for expensive subtrees — large [collections](collections.md),
complex charts, or deeply nested [component](components.md) trees that do not
depend on frequently changing state.

## .Set() Escape Hatch

`.Set()` gives you direct access to the underlying WinUI control. Use it
when Reactor does not expose a property you need:

```csharp
class SetEscapeHatchDemo : Component
{
    public override Element Render()
    {
        return VStack(12,
            SubHeading(".Set() Escape Hatch"),
            Button("Custom Tooltip", () => { })
                .Set(btn =>
                {
                    Microsoft.UI.Xaml.Controls.ToolTipService
                        .SetToolTip(btn, "This is a native tooltip");
                    btn.Padding = new Thickness(20, 10, 20, 10);
                }),
            TextBlock("Styled via .Set()")
                .Set(tb =>
                {
                    tb.TextWrapping = TextWrapping.WrapWholeWords;
                    tb.CharacterSpacing = 80;
                    tb.IsTextSelectionEnabled = true;
                })
        ).Padding(24);
    }
}
```

![.Set() escape hatch](images/advanced/set-escape-hatch.png)

Each element type has a typed `.Set()` overload. `Button` receives an
`Action<Microsoft.UI.Xaml.Controls.Button>`, `Text` receives
`Action<TextBlock>`, and so on. The callback runs at both mount and update,
so set properties idempotently — do not accumulate event handlers. Use
`.OnMount()` instead when you need to attach handlers exactly once.

## UseObservableTree

`UseObservableTree` bridges `INotifyPropertyChanged` view models into the
declarative render cycle. It subscribes to property changes at every depth
and re-renders the component when any nested property fires.

Define a standard INPC view model:

```csharp
class SettingsViewModel : INotifyPropertyChanged
{
    private string _userName = "Alice";
    private bool _darkMode;
    private int _fontSize = 14;

    public string UserName
    {
        get => _userName;
        set { _userName = value; Notify(nameof(UserName)); }
    }
    public bool DarkMode
    {
        get => _darkMode;
        set { _darkMode = value; Notify(nameof(DarkMode)); }
    }
    public int FontSize
    {
        get => _fontSize;
        set { _fontSize = value; Notify(nameof(FontSize)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify(string n) =>
        PropertyChanged?.Invoke(this, new(n));
}
```

Then bind it in a component with `UseObservableTree`:

```csharp
class ObservableTreeDemo : Component
{
    private static readonly SettingsViewModel _vm = new();

    public override Element Render()
    {
        var vm = UseObservableTree(_vm);

        return VStack(12,
            SubHeading("UseObservableTree"),
            TextField(vm.UserName, v => vm.UserName = v,
                header: "User Name"),
            ToggleSwitch(vm.DarkMode, v => vm.DarkMode = v,
                header: "Dark Mode"),
            Slider(vm.FontSize, 10, 32, v => vm.FontSize = (int)v),
            TextBlock($"Preview: {vm.UserName}")
                .FontSize(vm.FontSize).Bold()
        ).Padding(24);
    }
}
```

![Observable view model](images/advanced/observable-viewmodel.png)

The view model is a plain C# class with `INotifyPropertyChanged`. You mutate
it directly (`vm.UserName = v`) and Reactor re-renders. This is the bridge for
teams migrating from MVVM — existing view models work without modification.

`UseObservable(source)` is the shallow variant: it only subscribes to the
source object's own `PropertyChanged` events, not nested objects.

## UseCollection

`UseCollection` tracks an `ObservableCollection<T>` and re-renders when
items are added, removed, or the collection is reset:

```csharp
class ObservableCollectionDemo : Component
{
    private static readonly ObservableCollection<string> _tasks = new()
        { "Review pull request", "Update documentation" };

    public override Element Render()
    {
        var tasks = UseCollection(_tasks);
        var (input, setInput) = UseState("");

        return VStack(12,
            SubHeading("UseCollection"),
            HStack(8,
                TextField(input, setInput, placeholder: "New task")
                    .Width(200),
                Button("Add", () => {
                    if (!string.IsNullOrWhiteSpace(input))
                    { _tasks.Add(input.Trim()); setInput(""); }
                })
            ),
            TextBlock($"{tasks.Count} tasks:").SemiBold(),
            VStack(4, tasks.Select((task, i) =>
                HStack(8,
                    TextBlock($"{i + 1}. {task}"),
                    Button("Remove", () => _tasks.RemoveAt(i))
                ).WithKey($"task-{i}-{task}")
            ).ToArray())
        ).Padding(24);
    }
}
```

![Observable collection](images/advanced/observable-collection.png)

The hook returns `IReadOnlyList<T>` — you read from it in render, and
mutate the original `ObservableCollection` in event handlers. Reactor
subscribes to `CollectionChanged` and triggers a re-render on any
modification.

## Hot loops

The fluent modifier chain is ergonomic but allocates an `ElementModifiers`
clone per `with`-step. For ordinary UI that is invisible — a button has
five modifiers, the cost is two extra small records on a click handler.
For inner-loop cell construction in a 4,900-cell grid that re-renders
30× per second, those clones dominate the allocation profile.

The escape hatch is to construct the `Element` and its `ElementModifiers`
record directly, skipping the fluent chain entirely. The
`LayoutModifiers` and `VisualModifiers` sub-records are public types
specifically so perf-critical code can build them once instead of having
the fluent chain rebuild them step-by-step.

```csharp
// Fluent — five clones per cell. Right tool for ordinary UI.
var cell = TextBlock(label)
    .FontSize(8)
    .Foreground(item.IsUp ? GreenBrush : RedBrush)
    .Padding(2, 1, 2, 1)
    .Grid(row: r, column: c);

// Direct record initializer — one TextBlockElement, one ElementModifiers,
// two bucket sub-records, one Attached dictionary. Use only when the
// allocation cost shows up in profiles.
var cell = new TextBlockElement(label)
{
    FontSize = 8,
    Modifiers = new ElementModifiers
    {
        Layout = new LayoutModifiers { Padding = new Thickness(2, 1, 2, 1) },
        Visual = new VisualModifiers { Foreground = item.IsUp ? GreenBrush : RedBrush },
    },
    Attached = new Dictionary<Type, object>(1)
    {
        [typeof(GridAttached)] = new GridAttached(r, c, 1, 1),
    },
};
```

**Workload shape.** Use this idiom in lists or grids with hundreds-plus
elements per render — tickers, log tables, observability dashboards. Don't
adopt it for ordinary screens. The fluent chain remains the right tool for
everything except the inner cell loop.

**Trade-offs.** Roughly halves the allocation cost of cell construction
on the 4,900-cell stress grid, but loses fluent ergonomics. The direct
form is more brittle to refactor — changing one field touches an explicit
initializer block instead of a chain step. Restrict it to the
identifiable hot loop and keep the rest of the file fluent.

**Reference implementation.** The canonical before/after pair lives in
`tests/stress_perf/StressPerf.Reactor` (naive — fluent chain, the shape
unaware users write) and `tests/stress_perf/StressPerf.ReactorOptimized`
(idiomatic perf-tuned variant). Same workload, side-by-side diffable.

**Forward reference.** Spec 008's builder-pattern element factories
would let the fluent chain match this allocation profile, eliminating
the dichotomy. Until then, treat direct-initializer as a targeted
optimization.

## Memoizing list cells

`UseMemoCells` skips the cell-build for indices whose item value (and
declared dependencies) haven't changed since the previous render. The
reconciler's `ReferenceEquals` shortcut means a reused cell allocates
nothing and skips diffing entirely.

```csharp
var theme = ctx.UseTheme();
var children = ctx.UseMemoCells(
    stocks,
    (item, i) => Cell(item, theme),
    theme);   // ← deps; framework invalidates on change
```

**When it's the right hammer.** Tickers, log tables, file lists, large
read-only grids — anywhere the cell content is a pure function of `T`
plus a small set of declared deps.

**When it's the wrong hammer.** Rows whose chrome depends on focus,
drag, selection, or hover state that you aren't capturing in deps.
Memo silently renders stale when an external state change isn't
declared as a dep — the analyzer below catches the obvious cases, but
indirect captures through helper methods aren't visible to it.

**Three overloads:**

| Overload | Use when |
|----------|----------|
| `UseMemoCells<T>` | Per-item value equality. Default choice. |
| `UseMemoCellsByKey<T, TKey>` | Items have stable identity but mutable interior (`record Person(int Id, string Name)`). Hashes by key, value-compares for content. Reordered keys reuse cells via the reconciler's keyed-children path. |
| `UseMemoCellsByIndex<T>` | Data source already knows which indices changed. Skips the per-cell equality scan; only the named indices run the builder. |

**gen2 caveat.** Memo trades short-lived gen0 churn for longer-lived
gen1/gen2 retention. Many memoized lists across an app can compound
gen2 pressure even when bytes-per-tick drops. Profile before adopting
across the board.

**Compile-time safety net.** The companion Roslyn analyzer
`REACTOR_HOOKS_007` warns when a builder closure captures a value that
isn't declared in the deps list. Codefix is "add the missing capture to
deps". Indirect captures through intermediate methods are a documented
blind spot — the analyzer can't see through a method call without
whole-program analysis (same blind spot as React's
`react-hooks/exhaustive-deps`).

## Tips

**Wrap third-party components in `ErrorBoundary`.** If a plugin or external
component throws, the boundary prevents it from taking down your entire app.
Combine with a "Retry" button in the fallback to let users recover.

**Profile before memoizing.** `Memo` adds complexity. Only use it when you
have measured a re-render bottleneck. Most subtrees are fast enough without
it.

**Prefer declarative modifiers over `.Set()`.** Every `.Set()` call is an
escape hatch that bypasses Reactor's diffing. Use it only for properties that
Reactor does not expose. If you find yourself using `.Set()` frequently,
consider filing an issue to add first-class support.

**Keep view models outside the component.** Store them as `static` fields or
inject them via [context](context.md). Creating a new view model inside
`Render()` would allocate a fresh object on every render and lose all state.

**Use `UseObservable` when depth is one level.** `UseObservableTree` walks
the entire object graph on every change. For flat view models with no nested
INPC objects, `UseObservable` is cheaper.

## Next Steps

- **[Data System](data-system.md)** — next topic: DataGrid with sort, filter, and inline editing
- **[WinForms Interop](winforms-interop.md)** — host Reactor components inside WinForms applications
- **[Charting](charting.md)** — previous topic: data visualization with line, bar, area, and pie charts
- **[Reactor](readme.md)** — back to the index: overview of the framework and full topic list
- **[Hooks](hooks.md)** — review the hook system that `UseObservableTree` and `UseCollection` build on
- **[Effects and Lifecycle](effects.md)** — manage side effects alongside observable data binding
