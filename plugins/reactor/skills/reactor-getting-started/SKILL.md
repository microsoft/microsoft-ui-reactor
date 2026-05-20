---
name: reactor-getting-started
description: "Reactor essentials in one place — React-to-Reactor mental model, minimal app shape, hooks, the most-used factories, the critical gotchas, and project setup. This is the only skill you need loaded for typical Reactor work; load topical skills (`reactor-async`, `reactor-design`, etc.) only when the task explicitly calls for them."
---

## Coming from React? Read this first.

**Reactor concepts are React's, with one C# spelling.** If you know React, you already know how Reactor works — components render an element tree, hooks manage state and effects, lists need keys, lifting state up is the same. Trust your React intuition for *shape*; verify the *names* against the table below or `references/reactor.api.txt`.

| React | Reactor (C#) |
|---|---|
| `function App() { … }` | `class App : Component { override Element Render() { … } }` |
| `useState(0)` | `var (count, setCount) = UseState(0);` |
| `useReducer(reduce, init)` | `var (state, dispatch) = UseReducer<TState,TAction>(reduce, init);` |
| `useEffect(fn, [dep])` | `UseEffect(fn, dep);` |
| `useMemo(() => v, [dep])` | `UseMemo(() => v, dep)` |
| `useCallback(fn, [dep])` | `UseCallback(fn, dep)` |
| `useRef(v)` | `UseRef(v)` |
| `useContext(Ctx)` | `UseContext(Ctx)` |
| `<Provider value={x}>{c}</Provider>` | `c.Provide(Ctx, x)` |
| `<div>` (linear layout) | `FlexColumn(...)` / `FlexRow(...)` (CSS-flexbox semantics) |
| `<div>` (shrink-wrap) | `VStack(...)` / `HStack(...)` |
| `<span>text</span>` | `TextBlock("text")` |
| `<h1>` / `<h2>` / small caption | `Heading(...)` / `SubHeading(...)` / `Caption(...)` |
| `<button onClick={fn}>` | `Button("label", fn)` |
| `<input value={v} onChange={e=>…}>` | `TextField(v, setV)` |
| `<select>` | `ComboBox(items, index, setIndex)` |
| `<input type="checkbox">` | `CheckBox(checked, setChecked)` |
| `{cond && <X/>}` | `cond ? X() : null` (null children are filtered) |
| `{a ? <X/> : <Y/>}` | `a ? X() : Y()` |
| `{items.map(i => <Card key={i.id} … />)}` | `items.Select(i => Component<Card,…>(…).WithKey(i.id)).ToArray()` |
| `<Card {...props} />` | `Component<Card, CardProps>(new CardProps(...))` |
| `key={i.id}` | `.WithKey(i.id)` |
| `className="..."` (style) | `.Background(...)`, `.Padding(...)`, `.Margin(...)`, etc. |
| `style={{margin: 10}}` | `.Margin(10)` |
| `display: flex; flex: 1` | `FlexRow(...)` / `.Flex(grow: 1, basis: 0)` |
| `gap: 8` | `VStack(8, …)`, `FlexRow(...) with { ColumnGap = 8 }` (FlexColumn → `RowGap`) |
| React Query `useQuery` / `useMutation` | `UseResource` / `UseMutation` (see `reactor-async`) |
| JSX | C# method calls + `using static Microsoft.UI.Reactor.Factories` |

**One important difference:** in Reactor, `UseState` with a `List<T>` won't re-render on `.Add()` — same reference. Use `UseReducer` for lists (just like `useReducer` is preferred in React for complex state).

## Starting a new app

`dotnet new reactorapp -n <Name>` scaffolds the canonical shape: `App.cs` (entry point + initial component, with the seven-line using block at the top) plus `<Name>.csproj`. See the anti-probe + `mur check` notes under "Use a `.csproj` …" below for what comes out of the scaffold.

For a single-file `dotnet run App.cs` demo (no `.csproj`), prepend the file-level `#:package` / `#:property` headers — see `reactor-build-and-check`'s single-file-scripts section.

## Use a `.csproj` when you need …

… multiple files, **analyzers** (single-file `.cs` builds don't load them), or shared project references. `dotnet new reactorapp` scaffolds the canonical csproj — you don't need to author one from scratch.

`WindowsPackageType` MUST be `None` (unpackaged, no App.xaml). `UseWinUI` MUST be `true`. **No XAML files of any kind.**

**After `dotnet new reactorapp -n <Name>`, the workspace contains exactly two source files: `App.cs` (entry point + initial component) and `<Name>.csproj`.** There is no `Program.cs` and no `GlobalUsings.cs` — modify `App.cs` in place. The `.csproj` does **not** enable implicit usings; `App.cs` has its own `using` directives at the top — the same set listed in the *Required imports* section below — which is the only place you add new namespaces (e.g. `using System.Linq;` when you reach for `.Select(...)`). Don't probe the `.csproj` after scaffolding unless you're adding a `PackageReference` or changing a property — `Restore succeeded.` in the scaffold stdout is the only confirmation you need.

**Verify your edits with `mur check`** before declaring done. From the project directory: `mur check` (no arguments) runs `dotnet build` and emits one compressed line per diagnostic with a `→ try:` suggestion when the engine recognizes the mistake; `mur check --final` is the explicit "I am done iterating" sweep that emits the full diagnostic set including suppressed iteration-mode warnings. For anything more involved than the build/fix loop — strict-mode failures, custom diagnostic gating, MSBuild passthrough flags — load the `reactor-build-and-check` skill.

## Required imports

```csharp
using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;   // FlexDirection, FlexJustify, FlexAlign
using Microsoft.UI.Xaml;             // Thickness, HorizontalAlignment, VerticalAlignment
using Microsoft.UI.Xaml.Controls;    // Orientation, InfoBarSeverity, etc.
using static Microsoft.UI.Reactor.Factories;
```

## Components

```csharp
// Class component (primary)
class Counter : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        return VStack(TextBlock($"Count: {count}"),
                      Button("+1", () => setCount(count + 1)));
    }
}

// Function component (inline, small reusable pieces)
var toggle = Func(ctx =>
{
    var (on, setOn) = ctx.UseState(false);
    return ToggleSwitch(on, setOn);
});

// Typed props — use records for free structural equality
record UserCardProps(string Name, string Role);
class UserCard : Component<UserCardProps>
{
    public override Element Render() =>
        VStack(TextBlock(Props.Name), Caption(Props.Role));
}
Component<UserCard, UserCardProps>(new UserCardProps("Alice", "Admin"))

// Memoized function component
Memo(ctx => TextBlock("Stable"))               // render once + own state
Memo(ctx => TextBlock($"Hi, {name}"), name)    // re-render when deps change
```

`Component` skips parent-triggered re-renders by default. `Component<TProps>` skips when `Equals(oldProps, newProps)`.

**App entry point.** `ReactorApp.Run<MyRoot>("Title", width: W, height: H)` against a component class (the scaffolded form). For a tiny demo without a class, the inline form: `ReactorApp.Run("Title", ctx => { var (m, setM) = ctx.UseState("hi"); return TextBlock(m); })`.

## Hooks

**Rules:** same order every render (no hooks in `if`/`for`), only from `Render()` or function-component body.

| Hook | Returns | Use for |
|---|---|---|
| `UseState<T>(initial)` | `(T, Action<T>)` | Primary state |
| `UseReducer<T>(initial)` | `(T, Action<Func<T,T>>)` | State derived from previous (lists) |
| `UseReducer<TState,TAction>(reduce, initial)` | `(TState, Action<TAction>)` | Action-style reducer |
| `UseEffect(action, deps)` | — | Side effects + cleanup |
| `UseMemo<T>(factory, deps)` | `T` | Memoized computation |
| `UseCallback(action, deps)` | `Action` | Stable callback reference |
| `UseRef<T>(initial)` | `Ref<T>` | Mutable ref across renders (access via `.Current`) |
| `UseObservable<T>(source)` | `T` | Track `INotifyPropertyChanged` |
| `UseCollection<T>(coll)` | `IReadOnlyList<T>` | Track `ObservableCollection` |
| `UseContext<T>(ctx)` | `T` | Read tree-scoped ambient state |
| `UsePersisted<T>(key, initial)` | `(T, Action<T>)` | State that survives unmount |
| `UseResource<T>` / `UseInfiniteResource` / `UseMutation` | (see `reactor-async`) | Async data |
| `UseValidationContext()` | `ValidationContext` | (see `reactor-forms`) |
| `UseNavigation<TRoute>(initial)` | `NavigationHandle<TRoute>` | (see `reactor-navigation`) |

```csharp
// UseState
var (count, setCount) = UseState(0);

// UseReducer for lists (UseState won't re-render on .Add — same reference!)
var (items, updateItems) = UseReducer(new List<Todo>());
updateItems(list => [.. list, new Todo("New", false)]);

// Action-style reducer
var (state, dispatch) = UseReducer<BoardState, BoardAction>(Board.Reduce, BoardState.Initial);

// UseEffect
UseEffect(() => { /* mount */ });                      // empty deps → once
UseEffect(() => { /* on count change */ }, count);
UseEffect(() =>
{
    var timer = new Timer(...);
    return () => timer.Dispose();                      // cleanup
}, deps);

// UseContext
public static readonly Context<string> ThemeCtx = new("light");
VStack(...).Provide(ThemeCtx, "dark")                  // provide
var theme = UseContext(ThemeCtx);                      // consume

// UseRef — mutable value that persists across renders without triggering re-render
var timerRef = UseRef<DispatcherTimer?>(null);
timerRef.Current = new DispatcherTimer();              // .Current is the property (NOT .Value)
timerRef.Current.Start();
```

## Common factories — the 90% cases

The full catalog (every factory, modifier, enum) is in `references/reactor.api.txt`. The signatures below cover most apps; consult the index when you need a control not listed here.

```csharp
// Layout
VStack(spacing, children...)                  HStack(spacing, children...)
FlexColumn(children...)                       FlexRow(children...)
// Prefer FlexRow/FlexColumn for linear layout — CSS Flexbox semantics
// (grow/shrink/gap/wrap, justify-content, align-items). VStack/HStack
// remain for StackPanel's shrink-wrap behavior.
Border(child).CornerRadius(8).Background(Theme.CardBackground).Padding(16)
ScrollView(VStack(...))      // modern (default); ScrollViewer(...) is the classic Control
Grid(columns: [GridSize.Star(), GridSize.Px(200)],
     rows:    [GridSize.Auto,   GridSize.Star()],
    TextBlock("Header").Grid(row: 0, column: 0, columnSpan: 2),
    ListView(items, ...).Grid(row: 1, column: 0),
    Sidebar().Grid(row: 1, column: 1))
TitleBar("App") with { Subtitle = "Home", Content = ..., RightHeader = ... }
ContentDialog(string title, Element content, string primaryButtonText = "OK")
Flyout(Element target, Element content)
NavigationView(items, content) with { SelectedTag = "home", IsPaneOpen = true }

// Text
TextBlock("hi")        Heading("Title")        SubHeading("Section")        Caption("note")
// Strings auto-convert to TextBlockElement: VStack("A", "B") works.

// Controls
Button("Click", () => ...)                                    // positional: (label, onClick)
Button("Save", onClick: handler).Background(Theme.Accent)     // named-arg form before modifiers
// `onClick` is a Button ctor parameter — NOT a chained `.OnClick(...)` /
// `.OnTapped(...)`. `.OnTapped` is a gesture event with different input
// semantics (long-press, touch, pen) and is the wrong fix for click intent.
TextField(value, setValue, placeholder: "...")
CheckBox(isChecked, onChanged: setChecked, label: "label")
ToggleSwitch(on, setOn)
Slider(v, 0, 100, setV)
ComboBox(items, selectedIndex, setIndex)
ProgressIndeterminate()                  ProgressRing()
InfoBar("Title", "message").Severity(InfoBarSeverity.Error)

// Lists / templated
ListView<T>(items, keySelector, viewBuilder)
GridView<T>(items, keySelector, viewBuilder)
DataGrid<T>(source, columns, ...)        // see reactor.api.txt for full signature

// Conditional / iteration
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
items.Select(i => Component<Card, CardProps>(new CardProps(i)).WithKey(i.Id)).ToArray()

// Common modifiers
.Margin(10)            .Margin(left: 8, top: 4, right: 8, bottom: 4)
.Padding(16)           .Background(Theme.CardBackground)
.Foreground(Theme.PrimaryText)
.CornerRadius(8)       .WithBorder(Theme.CardStroke, 1)
.Flex(grow: 1, basis: 0)        // CSS `flex: 1` equivalent
.WithKey("id")                  // dynamic list items — see gotcha #6
.OnTapped((s, e) => ...)        // tap on non-Button surfaces — Border, Image, ScrollView, …
                                // (Button click → ctor arg, see Controls section)
                                // ⚠️ For UIA/automation: always pair with .AutomationName("...")
.AutomationName("Submit")       // a11y — sets AutomationProperties.Name
.Set(native => native.MaxWidth = 400)   // native escape hatch (lambda receives the WinUI control)
```

## Theme tokens (always)

Use `Theme.*` for all themed colors — never hardcoded hex on themed surfaces. The full token list with WinUI keys is in the `reactor-design` skill.

> ⚠️ **`Theme.Error`, `Theme.Success`, `Theme.Warning`, `Theme.ErrorText` do NOT exist.**
> Use `Theme.SystemCritical` (red/error), `Theme.SystemSuccess` (green), `Theme.SystemCaution` (yellow).

```csharp
TextBlock("Hi").Foreground(Theme.PrimaryText)
Border(child).Background(Theme.CardBackground).WithBorder(Theme.CardStroke, 1)
Button("Action").Background(Theme.Accent)
TextBlock("Error!").Foreground(Theme.SystemCritical)       // NOT Theme.Error
TextBlock("Saved").Foreground(Theme.SystemSuccess)         // NOT Theme.Success
```

## Critical gotchas

1. **Hook order is constant.** No hooks inside `if`/`for`. Call them all unconditionally; conditionally use the result.
2. **Type-specific sugar before generic modifiers.**
   `TextBlock("Hi").Bold().Margin(10)` ✓ — `.Bold()` needs `TextBlockElement`.
   `TextBlock("Hi").Margin(10).Bold()` ✗ — `.Margin()` returns `Element`.
3. **List mutations need `UseReducer`.** `UseState(new List<T>())` + `list.Add()` won't re-render — same reference. Use `UseReducer(list => [.. list, item])`.
4. **Null children are filtered.** `VStack(a, condition ? b : null, c)` is safe.
5. **Records with `with` for init-only properties.** `NavigationView(items, content) with { SelectedTag = "home", IsPaneOpen = true }`.
6. **`.WithKey("id")` on dynamic list items.** Without keys, the reconciler matches by position and re-mounts everything on insert/reorder — losing focus, animation state, ElementRef identity. The `REACTOR_DSL_001` analyzer catches this in `.csproj` builds.
7. **Memoize expensive computations.** `UseMemo(() => items.OrderBy(...).ToList(), items)`.
8. **`.Flex(grow: 1)` is `flex-grow`, not the CSS `flex: 1` shorthand.** Default basis is `auto` (content size), so a growing child with large intrinsic content overflows the container. Pass `.Flex(grow: 1, basis: 0)` (matches CSS `flex: 1`) or add `.Flex(shrink: 0)` to each fixed-size sibling.
9. **Don't pass freshly-allocated objects/arrays/lambdas as hook deps.** They compare unequal every render → hook never hits its stable path. The `REACTOR_HOOKS_004` analyzer catches this. **Tuples also trigger this** — `(x, y)` allocates a new `ValueTuple` each render. Instead, use a string key: `$"{x}|{y}"`, or pass individual values as separate deps: `UseEffect(fn, x, y)`.
10. **`UseResource` is reads-only.** Never call `Post*`/`Create*`/`Delete*`/`Save*` from a `UseResource` fetcher — it can re-run on deps change, retry, and focus revalidation. Use `UseMutation` for writes.

## Common patterns (paste-ready)

These cover the bulk of "stateful app with lists, dialogs, and per-row actions" — copy and adapt rather than re-deriving from the api index.

### Drag and drop (typed payload between two lists)

```csharp
using Microsoft.UI.Reactor.Input;   // DragOperations, DragData

sealed record Item(string Id, string Title);

Element RenderList(string title,
                   IReadOnlyList<Item> items,
                   Action<IReadOnlyList<Item>> setThis)
{
    var children = new List<Element> { TextBlock(title).SemiBold() };

    foreach (var item in items)
    {
        var captured = item;                                         // capture for the lambda
        children.Add(
            Border(TextBlock(captured.Title))
                .Background(Theme.CardBackground).CornerRadius(6).Padding(10)
                .OnDragStart<BorderElement, Item>(
                    getPayload: () => captured,
                    allowedOperations: DragOperations.Move,
                    onEnd: ctx =>
                    {
                        // Move-on-confirmation: only remove after a confirmed Move
                        // (not on cancel or Copy). Avoids the source losing data
                        // if the drop target rejects.
                        if (!ctx.WasCancelled && ctx.CompletedOperation == DragOperations.Move)
                            setThis(items.Where(i => i.Id != captured.Id).ToList());
                    })
        );
    }

    return VStack(6, children.ToArray())
        .OnDrop<StackElement, Item>(
            onDrop: dropped =>
            {
                if (!items.Any(i => i.Id == dropped.Id))
                    setThis(items.Append(dropped).ToList());
            },
            acceptedOps: DragOperations.Move);
}
```

For cross-process text drag (drop into Notepad/Word): use `.OnDragStart<BorderElement>(() => new DragData().WithText("..."))` and `.OnDrop<BorderElement>(args => args.Data.TryGetText(out var t))`.

### ContentDialog (modal — confirm, edit, alert)

`ContentDialog` is a render-tree element with `IsOpen`/`OnClosed` driven by component state — same pattern as React. Don't try to imperatively `.ShowAsync()`; let the reconciler manage it.

```csharp
var (showConfirm, setShowConfirm) = UseState(false);
var (lastResult, setLastResult) = UseState("(none)");

VStack(8,
    Button("Delete item", () => setShowConfirm(true)),
    TextBlock($"Last result: {lastResult}").Foreground(Theme.SecondaryText),

    ContentDialog("Confirm delete",
                  TextBlock("Are you sure? This cannot be undone."),
                  primaryButtonText: "Delete") with
    {
        IsOpen = showConfirm,
        SecondaryButtonText = "Cancel",
        OnClosed = result =>
        {
            setLastResult(result.ToString());
            setShowConfirm(false);
        },
    }
)
```

For an "edit existing item" dialog: lift the item being edited into state (`UseState<Item?>(null)`); when non-null, render the dialog with `IsOpen = editing != null`; in `OnClosed` either commit the edit and clear, or just clear.

### Flyout / context menu (right-click, dropdown menu)

```csharp
// Right-click context menu on any element
Border(TextBlock("Right-click me"))
    .Padding(12).Background(Theme.SubtleFill).CornerRadius(6)
    .WithContextFlyout(MenuItems(
        MenuItem("Edit",      () => beginEdit(item)),
        MenuItem("Duplicate", () => duplicate(item)),
        MenuSeparator(),
        MenuItem("Delete",    () => delete(item))
    ))

// Dropdown button with a menu
DropDownButton("Sort", flyout: MenuItems(
    MenuItem("By name", () => setSort(Sort.Name)),
    MenuItem("By date", () => setSort(Sort.Date)),
    MenuSeparator(),
    MenuItem("Reverse", () => setSort(s => s.Reversed()))
))

// Click flyout with custom content (vs. menu items)
Button("Open flyout", null).WithFlyout(ContentFlyout(
    VStack(8,
        TextBlock("Custom content here").SemiBold(),
        Slider(value, 0, 100, setValue)),
    placement: Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom
))
```

### Context (dispatch / theme passed without prop drilling)

When many descendants need `dispatch(action)` or a theme value, define `Context<T>` once and `.Provide(...)` from a parent. Same shape as React's `createContext` + `useContext`.

```csharp
// At module/class scope
sealed record AppAction(string Name);
static readonly Context<Action<AppAction>> DispatchCtx = new(_ => { });

// At the root
class App : Component
{
    public override Element Render()
    {
        var (state, dispatch) = UseReducer<AppState, AppAction>(App.Reduce, AppState.Initial);

        return VStack(
            Heading("My app"),
            Component<ChildView>()
        ).Provide(DispatchCtx, dispatch);     // descendants can read this
    }
}

// Anywhere in the subtree
class ChildView : Component
{
    public override Element Render()
    {
        var dispatch = UseContext(DispatchCtx);
        return Button("Do thing", () => dispatch(new AppAction("ThingClicked")));
    }
}
```

Nested `.Provide()` overrides the outer for its subtree only. If no provider is present, `UseContext` returns the `Context<T>` default.

## Bootstrap

`mur pack-local` (selfhost) and `nuget.config` (consumer outside the source clone) — see the top-level `SKILL.md`'s "Which mode are you in?" section. If selfhost restore fails with "package Microsoft.UI.Reactor 0.0.0-local was not found", run `mur pack-local`.

> ⚠️ **Platform flag required**: Always build with an explicit platform:
> `dotnet build -p:Platform=x64` (or `ARM64`). Omitting `-p:Platform=...`
> causes `WindowsAppSDKSelfContained` errors. This applies to `dotnet build`,
> `dotnet run`, and `mur check` invocations alike.

## Where the skill content comes from (and the api index)

You're reading this through the **`reactor` plugin** — the most efficient channel. The plugin SDK preloads `reactor-getting-started`; topical skills (`reactor-async`, `reactor-design`, `reactor-forms`, `reactor-navigation`, `reactor-input`, `reactor-recipes`, etc.) load only when the task explicitly needs them.

Full API index (every factory, modifier, hook, theme token, with parameter lists): `references/reactor.api.txt` inside the `reactor-dsl` skill (also bundled into the Microsoft.UI.Reactor nupkg's `agentkit/` tree if you need to find it from a consumer).

**Read the api index once** when you need to confirm an unusual signature — it's ~12K tokens, but cheaper than the equivalent number of grep+read cycles. Don't re-read pages of it; cache what you need in your working memory.

## Build output

`dotnet run` exits with code 1 on build failure. **Always read the output** — don't assume success. After non-trivial edits, run `mur check <path>` for one-line diagnostics with skill-file pointers (see `reactor-build-and-check`).

> Remember: always pass `-p:Platform=x64` (or `ARM64`). Without it, the first build always fails.
