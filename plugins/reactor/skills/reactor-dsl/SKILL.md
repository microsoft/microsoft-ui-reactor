---
name: reactor-dsl
description: "Reactor DSL essentials — components, hooks, factories (TextBlock, Button, VStack, FlexRow, …), fluent modifiers (.Margin, .Padding, .Background, .Flex, …), conditional rendering, theme tokens, and the critical gotchas that bite new agents (modifier order, hook order, missing .WithKey, .Flex grow without basis 0). Includes a pointer to the FULL api signatures index — load that whenever you need to confirm a method exists or check a parameter list."
---

## How to use this skill

Reactor is novel — your training data doesn't cover it. **Don't guess signatures.** When you need to check whether a factory, modifier, hook, or theme token exists, read `references/reactor.api.txt` from this skill (or from the package cache — see the cache map in `reactor-getting-started`). It's the source of truth.

This `SKILL.md` covers the 90% case so you usually don't need to look anything up.

## Components

### Class component (primary)

```csharp
class Counter : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        return VStack(
            TextBlock($"Count: {count}"),
            Button("+1", () => setCount(count + 1)));
    }
}
```

### Function component (inline, small reusable pieces)

```csharp
var toggle = Func(ctx =>
{
    var (on, setOn) = ctx.UseState(false);
    return ToggleSwitch(on, setOn);
});
```

### Embedding & props

```csharp
VStack(Component<MyWidget>(), Component<AnotherWidget>())

// Typed props — use records for free structural equality:
record UserCardProps(string Name, string Role);
class UserCard : Component<UserCardProps> { ... }
Component<UserCard, UserCardProps>(new UserCardProps("Alice", "Admin"))
```

### Memoized function component

```csharp
Memo(ctx => TextBlock("Stable"))               // render once + own state
Memo(ctx => TextBlock($"Hi, {name}"), name)    // re-render when deps change
```

`Component` skips parent-triggered re-renders by default. `Component<TProps>` skips when `Equals(oldProps, newProps)`.

## Hooks

Rules: same order every render (no hooks in `if`/`for`), only from `Render()` or function-component body.

| Hook | Returns | Use for |
|---|---|---|
| `UseState<T>(initial)` | `(T, Action<T>)` | Primary state |
| `UseReducer<T>(initial)` | `(T, Action<Func<T,T>>)` | State derived from previous (lists) |
| `UseReducer<TState,TAction>(reduce, initial)` | `(TState, Action<TAction>)` | Action-style reducer |
| `UseEffect(action, deps)` | — | Side effects + cleanup |
| `UseMemo<T>(factory, deps)` | `T` | Memoized computation |
| `UseCallback(action, deps)` | `Action` | Stable callback reference |
| `UseRef<T>(initial)` | `Ref<T>` | Mutable ref across renders |
| `UseObservable<T>(source)` | `T` | Track `INotifyPropertyChanged` |
| `UseCollection<T>(coll)` | `IReadOnlyList<T>` | Track `ObservableCollection` |
| `UseContext<T>(ctx)` | `T` | Read tree-scoped ambient state |
| `UsePersisted<T>(key, initial)` | `(T, Action<T>)` | State that survives unmount |
| `UseResource<T>`, `UseInfiniteResource`, `UseMutation` | See `reactor-async` | Async data |
| `UseValidationContext()` | `ValidationContext` | See `reactor-forms` |
| `UseNavigation<TRoute>(initial)` | `NavigationHandle<TRoute>` | See `reactor-navigation` |

### UseState / UseReducer

```csharp
var (count, setCount) = UseState(0);
var (items, updateItems) = UseReducer(new List<Todo>());

// List mutation — UseState with List<T> WON'T re-render on .Add() (same reference!).
// Use UseReducer instead:
updateItems(list => [.. list, new Todo("New", false)]);
```

### UseEffect

```csharp
UseEffect(() => { /* mount */ });                      // empty deps → once
UseEffect(() => { /* on count change */ }, count);
UseEffect(() =>
{
    var timer = new Timer(...);
    return () => timer.Dispose();                      // cleanup
}, deps);
```

### UseContext

```csharp
public static readonly Context<string> ThemeCtx = new("light");

VStack(...).Provide(ThemeCtx, "dark")                  // provide
var theme = UseContext(ThemeCtx);                      // consume
```

## DSL — the 90% cases

The full catalog (every factory, modifier, enum) is in `references/reactor.api.txt`. Read it when you need to confirm a signature.

```csharp
// Text + layout — prefer FlexRow/FlexColumn for linear layout (CSS Flexbox semantics:
// grow/shrink/gap/wrap, justify-content, align-items). VStack/HStack remain for
// StackPanel's shrink-wrap behavior.
FlexColumn(children...)         FlexRow(children...)
VStack(spacing, children...)    HStack(spacing, children...)
TextBlock("hi")  Heading("Title")  SubHeading("Section")  Caption("note")
Border(child).CornerRadius(8).Background(Theme.CardBackground).Padding(16)
ScrollView(VStack(...))
Grid(columns: ["*", "200"], rows: ["Auto", "*"], cells.Grid(row, column))
TitleBar("App") with { Subtitle = "Home", Content = ..., RightHeader = ... }

// Controls
Button("Click", () => ...)      TextField(value, setValue, placeholder)
CheckBox(isChecked, setChecked) ToggleSwitch(on, setOn)
Slider(v, 0, 100, setV)         ComboBox(items, index, setIndex)

// Strings auto-convert to TextBlockElement: VStack("A", "B") works.
```

### Conditional rendering

```csharp
isLoggedIn ? TextBlock($"Hi, {name}") : Button("Log in", onLogin)
VStack(TextBlock("always"), showExtra ? TextBlock("maybe") : null)   // null filtered
When(items.Any(), () => TextBlock($"{items.Count} items"))
If(isError, () => InfoBar("Error", msg).Severity(InfoBarSeverity.Error),
            () => TextBlock("OK"))
status switch {
    Status.Loading => ProgressIndeterminate(),
    Status.Error   => TextBlock("Oops"),
    Status.Success => Component<SuccessView>(),
    _ => Empty()
}
ForEach(items, item => TextBlock(item.Name))
// Or LINQ: VStack(items.Select(i => TextBlock(i.Name)).ToArray())
```

### Theme tokens (always)

Use `Theme.*` for all themed colors — never hardcoded hex on themed surfaces. The full token list with WinUI keys is in the api index.

```csharp
TextBlock("Hi").Foreground(Theme.PrimaryText)
Border(child).Background(Theme.CardBackground).WithBorder(Theme.CardStroke, 1)
Button("Action").Background(Theme.Accent)
```

## Critical gotchas

1. **Hook order is constant.** No hooks inside `if`/`for`. Call them all unconditionally; conditionally use the result.
2. **Type-specific sugar before generic modifiers.**
   `TextBlock("Hi").Bold().Margin(10)` ✓ — `.Bold()` needs `TextBlockElement`.
   `TextBlock("Hi").Margin(10).Bold()` ✗ — `.Margin()` returns `Element`.
3. **List mutations need `UseReducer`.** `UseState(new List<T>())` + `list.Add()` won't re-render — same reference. Use `UseReducer(list => [.. list, item])`.
4. **Null children are filtered.** `VStack(a, condition ? b : null, c)` is safe.
5. **Records with `with` for init-only properties.**
   `NavigationView(items, content) with { SelectedTag = "home", IsPaneOpen = true }`.
6. **`.WithKey("id")` on dynamic list items.** Without keys, the reconciler matches by position and re-mounts everything on insert/reorder — losing focus, animation state, ElementRef identity. The `REACTOR_DSL_001` analyzer catches this in `.csproj` builds.
7. **Memoize expensive computations.** `UseMemo(() => items.OrderBy(...).ToList(), items)`.
8. **`.Flex(grow: 1)` is `flex-grow`, not the CSS `flex: 1` shorthand.** Default basis is `auto` (content size), so a growing child with large intrinsic content (e.g. `ListView` with many items) overflows the container and Yoga shrinks every sibling proportionally — heading/buttons/inputs all collapse. Pass `.Flex(grow: 1, basis: 0)` (matches CSS `flex: 1`) or add `.Flex(shrink: 0)` to each fixed-size sibling.
9. **Don't pass freshly-allocated objects/arrays/lambdas as hook deps.** They compare unequal every render → hook never hits its stable path. The `REACTOR_HOOKS_004` analyzer catches this.
10. **`UseResource` is reads-only.** Never call `Post*`/`Create*`/`Delete*`/`Save*` from a `UseResource` fetcher — it can re-run on deps change, retry, and focus revalidation. Use `UseMutation` for writes (the `REACTOR_HOOKS_006` analyzer catches the common name patterns).

## Comparison to React (mental-model bridge)

| React | Reactor |
|---|---|
| `function App() {}` | `class App : Component { Render() }` |
| `useState(0)` | `UseState(0)` |
| `useReducer` | `UseReducer(initial)` — updater is `Func<T,T>` |
| `useEffect(() => {}, [dep])` | `UseEffect(() => {}, dep)` |
| `useMemo(() => val, [dep])` | `UseMemo(() => val, dep)` |
| `<div>` | `FlexColumn() / FlexRow() / Border()` (prefer over `VStack`/`HStack`) |
| `<span>text</span>` | `TextBlock("text")` |
| `<button onClick={fn}>` | `Button("label", fn)` |
| `<input value={v} onChange={fn}>` | `TextField(v, fn)` |
| `{cond && <X/>}` | `cond ? X() : null` |
| `{items.map(i => <X/>)}` | `items.Select(i => X()).ToArray()` |
| `<Component />` | `Component<MyComponent>()` |
| `createContext` + `useContext` | `Context<T>` + `.Provide()` + `UseContext()` |
| React Query `useQuery` / `useMutation` | `UseResource` / `UseMutation` — see `reactor-async` |
| `className="..."` | `.Set(el => ...)` for native access |
| `display: flex` / `flex-grow: 1` | `Flex()` / `.Flex(grow: 1)` |
| `style={{margin: 10}}` | `.Margin(10)` |
| JSX | C# calls + `using static Factories` |

## References

- `references/reactor.api.txt` — full alphabetized signatures index. Read before grepping. ~12K tokens, but cheaper than the equivalent number of grep+read cycles.
