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

## Key Features

- **Declarative components** — describe UI as immutable records via factory methods (`TextBlock`, `Button`, `VStack`, `HStack`, …); never construct or mutate WinUI controls directly.
- **Hooks** — `UseState`, `UseEffect`, `UseReducer`, `UseMemo`, `UseRef`, and more, with cross-thread state updates via `threadSafe: true`.
- **Fluent modifiers** — chain `.FontSize()`, `.SemiBold()`, `.Padding()`, `.Width()`, `.Foreground()`, `.IsEnabled()`, and others while preserving the concrete element type.
- **Flexbox layout** — `FlexPanel` brings CSS Flexbox to WinUI via an embedded pure-C# port of Meta's Yoga engine.
- **Efficient reconciliation** — a diffing reconciler plus control pooling keep updates minimal and allocation-light.
- **Trim & Native AOT support** — ship small, fast, self-contained native binaries.

## Main Types

| Type | Description |
|------|-------------|
| `ReactorApp` | Entry point — `Run<TRoot>(...)` creates the window and starts the render loop. |
| `Component` | Base class for your components; override `Render()` to return an `Element` tree. |
| `Element` | Immutable record describing a piece of UI. |
| `Factories` | Static factory methods (`TextBlock`, `Button`, `VStack`, `HStack`, `Slider`, …) — the DSL entry point. |
| `RenderContext` | Hosts the hooks (`UseState`, `UseEffect`, …) available inside `Render()`. |

## Additional Documentation

- [Getting started guide](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/guide/getting-started.md)
- [User guide](https://github.com/microsoft/microsoft-ui-reactor/tree/main/docs/guide)
- [Samples](https://github.com/microsoft/microsoft-ui-reactor/tree/main/samples)
- [Windows App SDK documentation](https://learn.microsoft.com/windows/apps/windows-app-sdk/)
- [WinUI 3 documentation](https://learn.microsoft.com/windows/apps/winui/winui3/)

## Related Packages

- [`Microsoft.UI.Reactor.Advanced`](https://www.nuget.org/packages/Microsoft.UI.Reactor.Advanced) — optional components with heavier native/graphics dependencies (Win2D canvas, charts).
- [`Microsoft.UI.Reactor.Devtools`](https://www.nuget.org/packages/Microsoft.UI.Reactor.Devtools) — optional developer-loop devtools host (live tree inspection, hot reload, preview).
- [`Microsoft.UI.Reactor.ProjectTemplates`](https://www.nuget.org/packages/Microsoft.UI.Reactor.ProjectTemplates) — `dotnet new` templates for scaffolding Reactor apps.

## Feedback & Contributing

`Microsoft.UI.Reactor` is an open-source project. File issues, ask questions, and contribute on [GitHub](https://github.com/microsoft/microsoft-ui-reactor). See [CONTRIBUTING.md](https://github.com/microsoft/microsoft-ui-reactor/blob/main/CONTRIBUTING.md) to get started.

## Support Policy

This package is currently released as a preview and is provided under the [MIT License](https://github.com/microsoft/microsoft-ui-reactor/blob/main/LICENSE). APIs may change between preview releases.
