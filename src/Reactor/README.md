# Microsoft.UI.Reactor

**Functional, declarative UI for WinUI 3 — build native Windows desktop apps with a React-style programming model, in pure C#.**

## About

`Microsoft.UI.Reactor` lets you describe your UI as a tree of immutable C# records and lightweight components. A virtual element tree and reconciler diff your declared UI against what's on screen and patch only what changed on the real WinUI controls underneath — so you get the productivity of a declarative, state-driven model with the fidelity and performance of native WinUI 3.

- **No XAML.** Your whole UI is C# — composable, refactorable, and testable.
- **Real WinUI controls.** Reactor renders genuine WinUI 3 controls, not a custom render surface.
- **Hooks for state.** `UseState`, `UseEffect`, `UseReducer`, `UseMemo` and friends manage component state and side effects, following the familiar rules of hooks.
- **AOT-friendly.** The core library is trim- and Native AOT-compatible.

## How to Use

Install the package into a WinUI 3 desktop project:

```shell
dotnet add package Microsoft.UI.Reactor
```

Your project should target a Windows TFM with WinUI enabled:

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
  <Platforms>x64;ARM64</Platforms>
  <UseWinUI>true</UseWinUI>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Microsoft.WindowsAppSDK" />
  <PackageReference Include="Microsoft.UI.Reactor" />
</ItemGroup>
```

Then write a component and run it. The example below is a complete, working counter app — a `Component` with state, declarative layout, and event handlers:

```csharp
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<Counter>("Counter", width: 360, height: 220);

internal sealed class Counter : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return VStack(12,
            TextBlock($"Count: {count}").FontSize(24).SemiBold(),
            HStack(8,
                Button("-", () => setCount(count - 1)),
                Button("Reset", () => setCount(0)),
                Button("+", () => setCount(count + 1))
            )
        ).Padding(24);
    }
}
```

`ReactorApp.Run<TRoot>` opens a window, mounts your root component, and starts the render loop. When `setCount` updates state, Reactor re-renders the component and patches only the `TextBlock` that changed.

## Common Patterns

From here, the same model scales to real apps. The [getting started guide](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/guide/getting-started.md) walks through each with complete, copy-pasteable code:

- **Controlled inputs & keyed lists** — `TextBox`/`CheckBox` driven by state, with `.WithKey(...)` for stable list reconciliation.
- **Side effects with cleanup** — `UseEffect` for timers, subscriptions, and I/O, returning an `Action` that runs on unmount.
- **Async data loading** — kick off work from an effect, update UI thread-safely with `UseState(..., threadSafe: true)`, and cancel on unmount.
- **Composition** — embed components with `Component<T>()` so each child keeps its own state.

## Key Features

- **Declarative components** — describe UI as immutable records via factory methods (`TextBlock`, `Button`, `VStack`, `HStack`, `TextBox`, `CheckBox`, …); never construct or mutate WinUI controls directly.
- **Hooks** — `UseState`, `UseEffect`, `UseReducer`, `UseMemo`, `UseRef`, and more, with opt-in cross-thread state updates via `threadSafe: true`.
- **Fluent modifiers** — chain `.FontSize()`, `.SemiBold()`, `.Padding()`, `.Margin()`, `.Width()`, `.Foreground()`, `.Background()`, `.IsEnabled()`, `.HAlign()`, and others while preserving the concrete element type.
- **Theme tokens** — color with system-aware tokens (`Theme.PrimaryText`, `Theme.Accent`, `Theme.SystemCritical`, …) that track light/dark mode automatically.
- **Keyed reconciliation** — `.WithKey(...)` gives list items stable identity for minimal, correct updates; control pooling keeps updates allocation-light.
- **Flexbox layout** — `FlexPanel` brings CSS Flexbox to WinUI via an embedded pure-C# port of Meta's Yoga engine.
- **Trim & Native AOT support** — ship small, fast, self-contained native binaries.

## Main Types

- **`ReactorApp`** — entry point; `Run<TRoot>(...)` creates the window and starts the render loop.
- **`Component`** — base class for your components; override `Render()` to return an `Element` tree.
- **`Element`** — immutable record describing a piece of UI.
- **`Factories`** — static factory methods (`TextBlock`, `Button`, `VStack`, `HStack`, `TextBox`, `CheckBox`, `ForEach`, `Component<T>`, …); the DSL entry point.
- **`RenderContext`** — hosts the hooks (`UseState`, `UseEffect`, `UseReducer`, …) available inside `Render()`.
- **`Theme`** — system-aware color and brush tokens.

## Additional Documentation

- [Getting started guide](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/guide/getting-started.md)
- [User guide](https://github.com/microsoft/microsoft-ui-reactor/tree/main/docs/guide)
- [AOT support guide](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/aot-support.md)
- [Samples](https://github.com/microsoft/microsoft-ui-reactor/tree/main/samples)
- [Windows App SDK documentation](https://learn.microsoft.com/windows/apps/windows-app-sdk/)
- [WinUI 3 documentation](https://learn.microsoft.com/windows/apps/winui/winui3/)

## Related Packages

- [`Microsoft.UI.Reactor.Advanced`](https://www.nuget.org/packages/Microsoft.UI.Reactor.Advanced) — optional components with heavier native/graphics dependencies (Win2D canvas, charts).
- [`Microsoft.UI.Reactor.Devtools`](https://www.nuget.org/packages/Microsoft.UI.Reactor.Devtools) — optional developer-loop devtools host (live tree inspection, hot reload, preview).

## Feedback & Contributing

`Microsoft.UI.Reactor` is an open-source project. File issues, ask questions, and contribute on [GitHub](https://github.com/microsoft/microsoft-ui-reactor). See [CONTRIBUTING.md](https://github.com/microsoft/microsoft-ui-reactor/blob/main/CONTRIBUTING.md) to get started.

## Support Policy

This package is currently released as a preview and is provided under the [MIT License](https://github.com/microsoft/microsoft-ui-reactor/blob/main/LICENSE). APIs may change between preview releases.
