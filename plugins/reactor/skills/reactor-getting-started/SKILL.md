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
| callback ref / `ref={node => …}` | `UseElementRef<T>()` + `.Ref(r)` |
| `useContext(Ctx)` | `UseContext(Ctx)` |
| `<Provider value={x}>{c}</Provider>` | `c.Provide(Ctx, x)` |
| `<div>` (linear layout) | `FlexColumn(...)` / `FlexRow(...)` (CSS-flexbox semantics) |
| `<div>` (shrink-wrap) | `VStack(...)` / `HStack(...)` |
| `<span>text</span>` | `TextBlock("text")` |
| `<h1>` / `<h2>` / small caption | `Heading(...)` / `SubHeading(...)` / `Caption(...)` |
| `<button onClick={fn}>` | `Button("label", fn)` |
| `<input value={v} onChange={e=>…}>` | `TextBox(v, setV)` |
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


> **Controlled prop note:** the factories shown here still take plain values
> (`TextBox(text, setText)`, `Slider(value, ...)`) and wrap them for you.
> Element-record properties behind those factories use `Optional<T>` so
> `Optional<T>.Unset` can mean "the control owns this value". See
> [`migration/050-optional-t.md`](../../../../docs/guide/migration/050-optional-t.md).

## Starting a new app

`dotnet new reactorapp -n <Name>` scaffolds the canonical shape: `App.cs` (entry point + initial component, with the `using` block at the top) plus `<Name>.csproj`. See the anti-probe + `mur check` notes under "Use a `.csproj` …" below for what comes out of the scaffold.

For a single-file `dotnet run App.cs` demo (no `.csproj`), prepend the file-level `#:package` / `#:property` headers — see `reactor-build-and-check`'s single-file-scripts section.

## Use a `.csproj` when you need …

… multiple files, **analyzers** (single-file `.cs` builds don't load them), or shared project references. `dotnet new reactorapp` scaffolds the canonical csproj — you don't need to author one from scratch.

`UseWinUI` MUST be `true`. **No XAML files of any kind.**

**Don't change the packaging mode of a project you didn't scaffold — keep whatever the scaffold
produced.** Reactor works in both shapes:

- **Unpackaged** — `<WindowsPackageType>None</WindowsPackageType>`, no `Package.appxmanifest`.
  `None` wins over `EnableMsixTooling`, so a project carrying both still builds unpackaged. Runs
  from any folder, no MSIX registration. This is what `dotnet new reactorapp` produces today.
- **Packaged** — has a `Package.appxmanifest`. Either omit `WindowsPackageType` entirely (the
  default produces a packaged app) or set it to `MSIX`; pair it with `EnableMsixTooling=true` for
  the single-project MSIX tooling. Launches with package identity, which is what identity-gated
  APIs require.

Neither is "the" Reactor shape, and neither is signalled by one property alone. **If a project has a
`Package.appxmanifest`, it is packaged on purpose — adding
`<WindowsPackageType>None</WindowsPackageType>` to it silently strips its identity, and the build
still succeeds, so nothing surfaces the mistake.** Switch modes only when the user asks, and switch
the whole property *set*, not a single flag — the `packaging` guide has both shapes in full,
including the explicit-`MSIX` form.

**After `dotnet new reactorapp -n <Name>`, the workspace contains `App.cs` (entry point + initial component) and `<Name>.csproj`, plus a `Properties/launchSettings.json` for F5 — and nothing else you need to touch.** Other Reactor templates scaffold more files (a packaged one adds MSIX packaging inputs); those are not source — leave them alone. There is no `Program.cs` and no `GlobalUsings.cs` — modify `App.cs` in place. `App.cs` has its own `using` directives at the top — see the *Required imports* section below — and that is where you add new namespaces (e.g. `using System.Linq;` when you reach for `.Select(...)`). Some templates enable implicit usings, so a namespace may already be in scope. Don't probe the `.csproj` after scaffolding unless you're adding a `PackageReference` or changing a property — `Restore succeeded.` in the scaffold stdout is the only confirmation you need.

**The scaffolded csproj ships with `WindowsAppSDKSelfContained=true` and a Debug-only ItemGroup that adds `Microsoft.UI.Reactor.Devtools` + `Reactor.DevtoolsSupport=true`.** Together they make `dotnet watch run` (and the very rough, experimental Visual Studio embedded-preview extension) hot-reload safe and F5 (which passes `--devtools` from `Properties/launchSettings.json`) bring up the devtools menu. The VS extension is currently the roughest Reactor surface; do not present it as stable. Release builds drop the devtools package and host-config switch so trim / AOT analyzers stay quiet — see the `packaging` guide for the full rationale before flipping either knob.

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
var toggle = Memo(ctx =>
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

**App entry point.** `ReactorApp.Run<MyRoot>("Title", width: W, height: H)` against a component class (the scaffolded form). `width`/`height` are optional DIPs — omit them to let the OS choose the initial window size. For a tiny demo without a class, the inline form: `ReactorApp.Run("Title", ctx => { var (m, setM) = ctx.UseState("hi"); return TextBlock(m); })`.

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
| `UseExternalStore<TSnapshot>(subscribe, getSnapshot)` | `TSnapshot` | Subscribe to an external store (stable `subscribe`, cached snapshot) |
| `UseCollection<T>(coll)` | `IReadOnlyList<T>` | Track `ObservableCollection` |
| `UseContext<T>(ctx)` | `T` | Read tree-scoped ambient state |
| `UsePersisted<T>(key, initial)` | `(T, Action<T>)` | State that survives unmount |
| `UseResource<T>` / `UseInfiniteResource` / `UseMutation` | (see `reactor-async`) | Async data |
| `UseValidationContext()` | `ValidationContext` | (see `reactor-forms`) |
| `UseNavigation<TRoute>(initial)` | `NavigationHandle<TRoute>` | (see `reactor-navigation`) |

### UseState

<!-- index:use-state -->
`UseState<T>(initial)` returns `(value, setValue)`. Calling the setter with a
value that differs from the current one schedules a re-render; the new value is
visible on the *next* render, not on the line after the call.

```csharp
var (count, setCount) = UseState(0);

return VStack(8,
    TextBlock($"Count: {count}"),
    Button("Increment", () => setCount(count + 1)));
```

Two rules decide whether `UseState` is the right hook:

- **Hook order is constant.** Never call a hook inside `if`, `for`, or a
  nested lambda — call them all unconditionally and use the result
  conditionally. `REACTOR_HOOKS_001` enforces this.
- **Reference types need a new instance.** `UseState(new List<T>())` followed
  by `list.Add(item)` will not re-render: the reference is unchanged, so the
  setter never sees a difference. Reach for `UseReducer` instead.

Pass `threadSafe: true` when the setter is called from a background thread.
<!-- /index:use-state -->

### UseReducer

<!-- index:use-reducer -->
`UseReducer` is the hook for state derived from the previous value — above all
collections, where `UseState` silently fails to re-render because the mutated
list is the same reference.

```csharp
var (items, updateItems) = UseReducer(new List<string>());

updateItems(list => [.. list, "New item"]);   // always a NEW list
```

The updater receives the current value and returns the next one, so it is
correct even when several updates are queued in one tick — unlike
`setItems(items.Concat(...))`, which reads a captured `items` that may
already be stale.

The two-type-parameter overload takes an explicit reducer for action-style
state machines:

```csharp
var (state, dispatch) = UseReducer<BoardState, BoardAction>(Board.Reduce, BoardState.Initial);
```
<!-- /index:use-reducer -->

### UseEffect

<!-- index:use-effect -->
`UseEffect(action, deps)` runs *after* the render commits. Return an `Action`
to register cleanup — it runs before the next execution of the effect and once
more on unmount, which is what makes timers, subscriptions, and event handlers
safe to own from a component.

```csharp
var (ticks, bumpTicks) = UseReducer(0);

UseEffect(() =>
{
    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    timer.Tick += (_, _) => bumpTicks(t => t + 1);
    timer.Start();
    return () => timer.Stop();     // cleanup on dep change and on unmount
}, Array.Empty<object>());
```

Dependency rules:

- No deps argument → the effect runs after **every** render.
- `Array.Empty<object>()` → runs **once**, on mount.
- One or more deps → runs whenever any of them changes.
- **Never pass a freshly allocated object, array, or lambda as a dep.** Deps go
  through `EqualityComparer<T>.Default`, which for a reference type means
  `Equals` — so a new `new[] { … }` or lambda each render is never equal to the
  last and the effect never reaches its stable path. (A type with value-based
  `Equals`, such as a `record`, is the exception: two fresh instances carrying
  the same values do compare equal.) Use a string key such as `$"{x}|{y}"`, or
  pass the values as separate deps: `UseEffect(fn, x, y)`.
- **Tuple deps are rejected by the analyzer, not by the runtime.** A
  `ValueTuple` of value types does compare by value, so `(x, y)` would work —
  but `REACTOR_HOOKS_004` classifies every tuple expression as an unstable dep
  and fails the build. Use a string key or separate deps here too.
<!-- /index:use-effect -->

### UseMemo and UseCallback

<!-- index:use-memo -->
`UseMemo<T>(factory, deps)` caches the result of an expensive computation and
recomputes it only when a dependency changes. `UseCallback(action, deps)` does
the same for a delegate, so a child that compares handlers by reference is not
re-rendered by a freshly allocated lambda.

```csharp
var sorted = UseMemo(() => items.OrderBy(i => i.Name).ToList(), items);
var onReset = UseCallback(() => setQuery(""), Array.Empty<object>());
```

Both obey the same dependency rule as `UseEffect`: a **reference type** allocated
during render is compared with `Equals`, so a fresh array or lambda is never equal
to the previous one and defeats the cache entirely (a `record` or other value-based
`Equals` is the exception). A tuple expression — though value-equal at runtime — is
rejected outright by `REACTOR_HOOKS_004`. Memoize the computation, not the render —
`UseMemo` is for work that is measurably expensive, not for every projection.
<!-- /index:use-memo -->

### UseRef

<!-- index:use-ref -->
`UseRef<T>(initial)` returns a `Ref<T>` — a mutable box that survives re-renders
and, unlike `UseState`, **never triggers one**. Use it for values a render does
not read: a timer handle, a subscription token, a "did I already do this" flag,
or a high-frequency value written during a gesture.

```csharp
var timerRef = UseRef<DispatcherTimer?>(null);

timerRef.Current = new DispatcherTimer();   // .Current is the property — NOT .Value
timerRef.Current.Start();
```

`Ref<T>` is Reactor's own box and exposes `.Current`. Do not confuse it with
`ElementRef` / `UseElementRef<T>`, which points at a realized WinUI element and
is populated by the reconciler rather than by you.
<!-- /index:use-ref -->

### UseContext

<!-- index:context -->
A `Context<T>` passes ambient state down the tree without threading it through
every intermediate component. Declare the context once as a static, provide a
value on an ancestor with `.Provide(...)`, and read it with `UseContext`.

```csharp
public static readonly Context<string> ThemeCtx = new("light");

// Provide — every descendant sees "dark"
VStack(8, Component<Toolbar>(), Component<Body>()).Provide(ThemeCtx, "dark")

// Consume, anywhere below
var theme = UseContext(ThemeCtx);
```

The value passed to `new Context<T>(...)` is the default a consumer reads when
no ancestor provides one, so `UseContext` never returns an unexpected `null`.
Providing a new value re-renders the consumers below it, not the whole tree.
<!-- /index:context -->

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
TextBox(value, setValue, placeholderText: "...")
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
.Ref(targetRef)                 // bind an ElementRef<T>; Compose FocusRequester / React callback-ref shape
.Target(targetRef)              // reference prop, e.g. TeachingTip.Target; also .LabeledBy/.XYFocusRight
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
9. **Don't pass freshly-allocated objects/arrays/lambdas as hook deps.** They compare unequal every render → hook never hits its stable path. The `REACTOR_HOOKS_004` analyzer catches this. **Tuples are also rejected** — not because `(x, y)` compares unequal (a `ValueTuple` of value types is value-equal), but because the analyzer classifies every tuple expression as an unstable dep. Instead, use a string key: `$"{x}|{y}"`, or pass individual values as separate deps: `UseEffect(fn, x, y)`.
10. **`UseResource` is reads-only.** Never call `Post*`/`Create*`/`Delete*`/`Save*` from a `UseResource` fetcher — it can re-run on deps change, retry, and focus revalidation. Use `UseMutation` for writes.

### Element refs and reference props

Use `UseElementRef<T>()` when one element needs to point at another real
WinUI element. Attach the cell with `.Ref(...)`, then pass it to a
reference prop such as `TeachingTip.Target`, `.LabeledBy(...)`, or
`.XYFocusRight(...)`. This is Reactor's callback-ref / Compose
`FocusRequester` shape: the graph updates when the target mounts late,
unmounts, or is recreated.

```csharp
var target = this.UseElementRef<FrameworkElement>();

return HStack(
    Button("Show tip", () => setOpen(true)).Ref(target),
    TeachingTip("Hint", target: target) with { IsOpen = open });
```

Do **not** set relationship properties by reading `target.Current` in a
handler; use the reference prop so Reactor can clear and re-resolve it.

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

> ⚠️ **Platform flag required when working *inside this repo* (selfhost)**: always build samples / in-repo projects with an explicit platform: `dotnet build -p:Platform=x64` (or `ARM64`). Omitting `-p:Platform=...` causes `WindowsAppSDKSelfContained` errors. This applies to `dotnet build`, `dotnet run`, and `mur check` invocations alike.
>
> The `dotnet new reactorapp` template auto-resolves `RuntimeIdentifier` from the host SDK when `Platform`/`RuntimeIdentifier` aren't explicit, so consumer projects scaffolded outside the repo build with bare `dotnet build` / F5 — the rule above only applies in the selfhost tree.

## Where the skill content comes from (and the api index)

You're reading this through the **`reactor` plugin** — the most efficient channel. The plugin SDK preloads `reactor-getting-started`; topical skills (`reactor-async`, `reactor-design`, `reactor-forms`, `reactor-navigation`, `reactor-input`, `reactor-recipes`, `reactor-wrapping` for turning any WinUI, vendor, or custom control into a declarative element via the `[GenerateReactorWrapper]` source generator, `reactor-advanced` for Win2D canvases via the optional `Microsoft.UI.Reactor.Advanced` package, etc.) load only when the task explicitly needs them.

Full API index (every factory, modifier, hook, theme token, with parameter lists): `references/reactor.api.txt` inside the `reactor-dsl` skill (also bundled into the Microsoft.UI.Reactor nupkg's `agentkit/` tree if you need to find it from a consumer).

**Read the api index once** when you need to confirm an unusual signature — it's ~12K tokens, but cheaper than the equivalent number of grep+read cycles. Don't re-read pages of it; cache what you need in your working memory.

## Build output

`dotnet run` exits with code 1 on build failure. **Always read the output** — don't assume success. After non-trivial edits, run `mur check <path>` for one-line diagnostics with skill-file pointers (see `reactor-build-and-check`).

> Remember: always pass `-p:Platform=x64` (or `ARM64`). Without it, the first build always fails.
