# Copilot Instructions — Microsoft.UI.Reactor

Reactor is a declarative, component-based C# framework for building WinUI 3 desktop apps. It renders real WinUI controls via a virtual element tree and reconciler — similar to React's programming model but targeting native Windows UI.

## Build, Test, Lint

```bash
# Build (platform defaults to machine arch for apps; libraries are AnyCPU)
dotnet build Reactor.slnx

# Unit tests — xUnit, headless, fast (~2200 tests incl. 590 Yoga fixtures)
dotnet test tests/Reactor.Tests

# Single test class
dotnet test tests/Reactor.Tests --filter "FullyQualifiedName~ReconcilerMountUpdateTests"

# Selftests — real WinUI window, in-process (~10s)
dotnet test tests/Reactor.SelfTests

# Raw TAP output (faster iteration, supports --filter prefix)
dotnet run --project tests/Reactor.AppTests.Host -- --self-test --filter "Flex"

# E2E — Appium/WinAppDriver (requires WinAppDriver installed)
dotnet test tests/Reactor.AppTests

# Single E2E class
dotnet test tests/Reactor.AppTests --filter "ClassName=Reactor.AppTests.Tests.AccessibilityTests"
```

CI runs unit tests + selftests + full solution build on every PR. .NET 10 SDK, `windows-latest` runner.

Full testing guide — tier selection, NativeAOT runs, code coverage — in [`TESTING.md`](TESTING.md).

## Architecture

### Virtual DOM model

UI is described as **immutable C# records** (`Element` subclasses), not WinUI controls. The reconciler diffs old vs. new element trees and patches only what changed on real controls.

```
Component.Render() → Element tree (records)
                        ↓
                   Reconciler
                   ├── Mount  → creates WinUI controls
                   └── Update → diffs & patches controls
```

### Reconciler is split across partial classes

- `Reconciler.cs` — orchestration, child reconciliation, unmount, helpers
- `Reconciler.Mount.cs` — `MountXxx()` handler per control type
- `Reconciler.Update.cs` — `UpdateXxx()` handler per control type

### Hooks follow React rules

Hooks (`UseState`, `UseEffect`, `UseReducer`, `UseMemo`, etc.) are tracked by call order in `RenderContext`. They must be called unconditionally, in the same order every render — no conditional hooks. Pass `threadSafe: true` for cross-thread state updates.

### Echo suppression for value controls

Value-bearing controls (TextBox, Slider, ColorPicker) use `ChangeEchoSuppressor` + `SetElementTag`-first to prevent programmatic writes from re-firing user callbacks. Any new value control must follow this pattern.

### Element pooling

`ElementPool` recycles WinUI controls. Poolable types track one-time event wiring via `ConditionalWeakTable<FrameworkElement, PoolableWireFlags>` to avoid double-subscribing across rent/return cycles.

### Per-element state via attached DP

`ReactorAttached.StateProperty` stores `ReactorState` (Element pointer + EventHandlerState) on native elements — not `FrameworkElement.Tag` or a CWT.

## Key Conventions

### Elements are immutable records

```csharp
public record MyControlElement(string Label, Action? OnClick = null) : Element;
```

Use `with` expressions for variations. Never mutate.

### Factory methods over constructors

The DSL entry point is `using static Microsoft.UI.Reactor.Factories;`. Factory methods return Element records, never WinUI controls:

```csharp
TextBlock("hello")       // not new TextBlockElement("hello")
Button("+", () => ...)   // not new ButtonElement(...)
VStack(child1, child2)   // layout containers
```

`Factories` is `public static partial class` — factory methods can be added from multiple files.

### Fluent modifiers preserve concrete types

Extension methods use `<T> where T : Element` to maintain the concrete type through chains:

```csharp
Text("Hello").Bold().Margin(16).Set(tb => tb.TextWrapping = TextWrapping.Wrap)
// Still TextBlockElement throughout the chain
```

### Adding a new WinUI control requires four touch points

1. **Element record** in `src/Reactor/Core/Element.cs`
2. **Factory method** in `src/Reactor/Elements/Dsl.cs`
3. **Mount handler** in `src/Reactor/Core/Reconciler.Mount.cs` (+ register in dispatch switch)
4. **Update handler** in `src/Reactor/Core/Reconciler.Update.cs` (+ register in dispatch switch)

Optionally: fluent modifiers in `ElementExtensions.cs`, tests in `Reactor.Tests/` and/or selftest fixture in `Reactor.AppTests.Host/SelfTest/Fixtures/`.

### Test tier selection

| Testing… | Write a… | Location |
|---|---|---|
| Algorithm, pure function, hook bookkeeping, D3 math | Unit test (xUnit) | `tests/Reactor.Tests/` |
| Element mount/update against real WinUI controls | Selftest fixture | `tests/Reactor.AppTests.Host/SelfTest/Fixtures/` |
| Real user input, UIA properties, cross-process | E2E test (Appium) | `tests/Reactor.AppTests/Tests/` |

Start with unit tests. Use selftests only when you need a live WinUI control. E2E is the slowest tier.

### Console-mutating tests need collection isolation

Tests that write to `Console.Out`/`Console.Error` must be grouped with `[Collection("ConsoleTests")]` to prevent cross-test interference.

### AOT compatibility

`IsAotCompatible=true` is set for all net10.0+ projects. The core Reactor library promotes IL trimming/AOT warnings to errors — new reflection usage must be annotated before merging. Non-Reactor projects (tests, samples) suppress these warnings.

### WinUI library projects

Class libraries must set `WindowsAppSDKSelfContained=false`. Only app executables own Windows App SDK self-contained packaging.

### No XAML

Everything is C#. No `.xaml` files for UI (except `ReactorApplication.xaml` which loads `XamlControlsResources` for AOT compatibility).

### User guide docs are generated

Docs under `docs/guide/` are compiled from `docs/_pipeline/templates/*.md.dt` via `mur docs compile`. Edit the templates, not the compiled output.

## Project Layout

```
src/Reactor/              Core framework
  Core/                   Reconciler, Component, Element, Hooks, RenderContext
  Elements/               DSL factories (Dsl.cs) + fluent modifiers (ElementExtensions.cs)
  Flex/                   FlexPanel — CSS Flexbox via Yoga
  Yoga/                   Pure C# port of Meta's Yoga layout engine
  Hosting/                ReactorApp entry point, render loop, hot reload
src/Reactor.Cli/          CLI tool (scaffolding, localization, preview)
src/Reactor.Analyzers/    Roslyn analyzers (theming, accessibility)
src/vscode-reactor/       VS Code live preview extension
tests/
  Reactor.Tests/          Unit tests (xUnit, headless)
  Reactor.SelfTests/      Selftest runner (MSTest, wraps TAP subprocess)
  Reactor.AppTests.Host/  Selftest host app + Appium fixture navigator
  Reactor.AppTests/       E2E tests (MSTest + Appium/WinAppDriver)
samples/                  Demo apps and samples
docs/
  guide/                  User documentation (generated from templates)
  specs/                  Numbered design specs
  reference/              API and subsystem reference
```
