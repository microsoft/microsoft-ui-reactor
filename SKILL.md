---
name: reactor-app
description: >
  Create WinUI 3 desktop applications using the Reactor framework — a React-inspired
  declarative C# projection over WinUI 3. No XAML, no data binding, no templates.
  This root skill teaches the core mental model and common patterns; load a
  sub-skill from /skills when the task calls for it.
---

# Reactor — Getting Started

Reactor is a **React-inspired functional projection for WinUI 3**. You write
functions that return lightweight element descriptions; a reconciler diffs
old vs new trees and patches real WinUI controls. State changes trigger
re-renders automatically. No XAML. No data binding. No ViewModels.

## Sub-skills — load when the task calls for them

| Skill | When to load |
|---|---|
| [`skills/async.md`](skills/async.md) | Fetching data, caching, pagination, optimistic writes. `UseResource`, `UseMutation`, `UseInfiniteResource`, `Pending`. |
| [`skills/design.md`](skills/design.md) | Any visual-styling work. Windows 11 design rules — theme tokens, High Contrast, typography, 4px grid, acrylic surfaces, accessibility. |
| [`skills/commanding.md`](skills/commanding.md) | Actions that appear in multiple surfaces (menu + toolbar), need keyboard shortcuts, or need `CanExecute`. `Command`, `StandardCommand`, `UseCommand`, `CommandHost`. |
| [`skills/devtools.md`](skills/devtools.md) | Drive a running app via `mur devtools` — screenshot, inspect visual tree, click/type/scroll, read hook state. Load when diagnosing visible bugs (layout, contrast) or verifying a change landed. |
| [`skills/navigation.md`](skills/navigation.md) | Multi-page apps, sidebar/tab navigation, routes, deep linking, page transitions, caching. `UseNavigation`, `NavigationHost`, `NavigationView`, `TabView`. |
| [`skills/forms.md`](skills/forms.md) | Data-entry screens, validation, masked/formatted input. `UseValidationContext`, `FormField`, `MaskEngine`, `InputFormatter`. |
| [`skills/input.md`](skills/input.md) | Gestures, pointer events, drag-and-drop, focus management. `OnPan`, `OnPinch`, `OnRotate`, `OnDragStarting`, `UseElementFocus`. |
| [`skills/dsl-reference.md`](skills/dsl-reference.md) | Look up signatures — every factory, modifier, and enum in the DSL. |

## Project Setup

### .csproj (copy exactly)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
    <Platforms>x64;ARM64</Platforms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWinUI>true</UseWinUI>
    <WindowsPackageType>None</WindowsPackageType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="2.0.0-preview2" />
    <ProjectReference Include="..\Reactor\Reactor.csproj" />
  </ItemGroup>
</Project>
```

`WindowsPackageType` MUST be `None` (unpackaged, no App.xaml). `UseWinUI`
MUST be `true`. No XAML files of any kind.

### Required imports

```csharp
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;   // FlexDirection, FlexJustify, ... (if using Flex)
using Microsoft.UI.Xaml;             // Thickness, HorizontalAlignment, VerticalAlignment
using Microsoft.UI.Xaml.Controls;    // Orientation, InfoBarSeverity, etc.
using static Microsoft.UI.Reactor.Factories;   // TextBlock(), Button(), VStack() bare calls
```

### App entry point

```csharp
// Component root
ReactorApp.Run<MyRoot>("Title", width: 1024, height: 768);

// Inline render function
ReactorApp.Run("Title", ctx =>
{
    var (msg, setMsg) = ctx.UseState("Hello!");
    return VStack(TextBlock(msg), Button("Change", () => setMsg("Changed!")));
});
```

## Components

### Class component (primary pattern)

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
// Embed:
VStack(Component<MyWidget>(), Component<AnotherWidget>())

// Typed props — use records for free structural equality:
record UserCardProps(string Name, string Role);
class UserCard : Component<UserCardProps> { ... }
Component<UserCard, UserCardProps>(new UserCardProps("Alice", "Admin"))
```

### Memoized function component

```csharp
Memo(ctx => TextBlock("Stable"))           // render once + own state
Memo(ctx => TextBlock($"Hi, {name}"), name) // re-render when deps change
```

Propless `Component` skips parent-triggered re-renders by default.
`Component<TProps>` skips when `Equals(oldProps, newProps)`.

## Hooks

Rules: same order every render (no hooks in `if`/`for`), only from `Render()`
or function-component body.

| Hook | Returns | Use for |
|---|---|---|
| `UseState<T>(initial)` | `(T, Action<T>)` | Primary state |
| `UseReducer<T>(initial)` | `(T, Action<Func<T,T>>)` | State derived from previous (lists) |
| `UseEffect(action, deps)` | — | Side effects + cleanup |
| `UseMemo<T>(factory, deps)` | `T` | Memoized computation |
| `UseCallback(action, deps)` | `Action` | Stable callback reference |
| `UseRef<T>(initial)` | `Ref<T>` | Mutable ref across renders |
| `UseObservable<T>(source)` | `T` | Track `INotifyPropertyChanged` |
| `UseCollection<T>(coll)` | `IReadOnlyList<T>` | Track `ObservableCollection` |
| `UseContext<T>(ctx)` | `T` | Read tree-scoped ambient state |
| `UsePersisted<T>(key, initial)` | `(T, Action<T>)` | State that survives unmount |
| `UseResource<T>`, `UseInfiniteResource`, `UseMutation` | See `skills/async.md` | Async data |

### UseState / UseReducer

```csharp
var (count, setCount) = UseState(0);
var (items, updateItems) = UseReducer(new List<Todo>());

// List mutation via UseReducer (UseState with List<T> won't re-render on mutate!):
updateItems(list => [.. list, new Todo("New", false)]);
```

### UseEffect

```csharp
UseEffect(() => { /* mount */ });                // empty deps → once
UseEffect(() => { /* on count change */ }, count);
UseEffect(() =>
{
    var timer = new Timer(...);
    return () => timer.Dispose();                // cleanup
}, deps);
```

### UseContext

```csharp
public static readonly Context<string> ThemeCtx = new("light");

// Provide:
VStack(...).Provide(ThemeCtx, "dark")

// Consume:
var theme = UseContext(ThemeCtx);
```

## DSL — the essentials

For the complete catalog (every factory, modifier, enum) see
[`skills/dsl-reference.md`](skills/dsl-reference.md). The 90% cases:

```csharp
// Text + layout — prefer FlexRow/FlexColumn for linear layout; they use CSS Flexbox
// semantics (grow/shrink/gap/wrap, justify-content, align-items) so the model matches
// the web. VStack/HStack remain available when you specifically want StackPanel's
// shrink-wrap behavior.
FlexColumn(children...)         FlexRow(children...)
VStack(spacing, children...)    HStack(spacing, children...)
TextBlock("hi")  Heading("Title")    SubHeading("Section")  Caption("note")
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
VStack(TextBlock("always"), showExtra ? TextBlock("maybe") : null) // null filtered
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

## Critical gotchas

1. **Hook order is constant.** No hooks inside `if`/`for`. Call them all
   unconditionally; conditionally use the result.
2. **Type-specific sugar before generic modifiers.**
   `TextBlock("Hi").Bold().Margin(10)` ✓ — `.Bold()` needs `TextBlockElement`.
   `TextBlock("Hi").Margin(10).Bold()` ✗ — `.Margin()` returns `Element`.
3. **List mutations use `UseReducer`.** `UseState(new List<T>())` + `list.Add()`
   won't re-render — same reference. Use `UseReducer(list => [.. list, item])`.
4. **Null children are filtered.** `VStack(a, condition ? b : null, c)` is safe.
5. **Records with `with` for init-only properties.**
   `NavigationView(items, content) with { SelectedTag = "home", IsPaneOpen = true }`.
6. **`.WithKey("id")` on dynamic list items.** Without keys, the reconciler
   matches by position and re-mounts everything on insert/reorder.
7. **Memoize expensive computations.** `UseMemo(() => items.OrderBy(...).ToList(), items)`.

## Starter template

```csharp
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("My App", width: 1200, height: 800);

class App : Component
{
    public override Element Render()
    {
        var (page, setPage) = UseState("Home");

        return Grid(
            rows: "Auto,*",
            Border(
                HStack(12,
                    Heading("My App").VAlign(VerticalAlignment.Center),
                    NavBtn("Home", page, setPage),
                    NavBtn("Settings", page, setPage))
            ).Background("#f0f0f0").Padding(24, 12).Grid(row: 0),

            Border(page switch
            {
                "Home"     => Component<HomePage>(),
                "Settings" => Component<SettingsPage>(),
                _ => TextBlock("Not found")
            }).Padding(24).Grid(row: 1));
    }

    static Element NavBtn(string label, string current, Action<string> set) =>
        Button(label, () => set(label)).Disabled(label == current);
}
```

## Testing

Reactor has three test suites. Run the one that matches what you changed.

```bash
# Unit tests — fast, no UI window (~3s)
dotnet test tests/Reactor.Tests

# Selfhost tests — real WinUI controls, in-process (~15s)
dotnet test tests/Reactor.AppTests --filter "ClassName=Reactor.AppTests.Tests.SelfTestBatch"

# Appium / E2E — cross-process UI Automation (~30s, needs WinAppDriver)
dotnet test tests/Reactor.AppTests --filter "ClassName=Reactor.AppTests.Tests.InteractiveTests"

# Everything
dotnet test Reactor.sln
```

`samples/Reactor.TestApp` is the interactive demo, not a test runner.

## Single-file apps with `dotnet run`

For lightweight demos, skip the `.csproj` entirely. Add a file-level header:

```csharp
#:project ./src/Reactor
#:package Microsoft.WindowsAppSDK@2.0.0-preview2
#:property OutputType=WinExe
#:property TargetFramework=net9.0-windows10.0.22621.0
#:property UseWinUI=true
#:property WindowsPackageType=None
#:property WindowsAppSDKSelfContained=true
#:property RuntimeIdentifier=win-arm64

using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run("Hello", ctx =>
{
    var (count, setCount) = ctx.UseState(0);
    return VStack(TextBlock($"Count: {count}"), Button("+1", () => setCount(count + 1)));
});
```

Run with `dotnet run MyApp.cs`. Adjust `#:project` to your clone's
`src/Reactor` directory. Use `win-arm64` on ARM, `win-x64` on x64 — check
with `dotnet --info`.

> **Always capture `dotnet run` output.** Build errors exit with code 1.
> Read compiler output, fix, retry. Don't assume success without checking.

## Comparison to React

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
| React Query `useQuery` / `useMutation` | `UseResource` / `UseMutation` — see `skills/async.md` |
| `className="..."` | `.Set(el => ...)` for native access |
| `display: flex` / `flex-grow: 1` | `Flex()` / `.Flex(grow: 1)` |
| `style={{margin: 10}}` | `.Margin(10)` |
| JSX | C# calls + `using static Factories` |
