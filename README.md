<a href="https://github.com/microsoft/microsoft-ui-xaml">
  <img src="docs/images/header.png" alt="WinUI hero image"/>
</a>

<h1 align="center">Microsoft.UI.Reactor</h1>

<p align="center">
  <strong>A declarative, component-based C# framework for building <a href="https://github.com/microsoft/microsoft-ui-xaml">WinUI 3</a> desktop apps.</strong>
</p>

<p align="center">
  <strong>Status:</strong> Experimental · April 2026
</p>

---

## What Reactor is — and isn't

Reactor is **not** a new UI platform. It is a new way to *describe* WinUI content. Every control you render is a real WinUI control — `Button`, `TextBox`, `NavigationView`, `TreeView` — just authored differently. Apps built with Reactor interop freely with XAML, MVVM, existing controls, and the rest of the WinUI ecosystem.

What Reactor adds on top of WinUI:

- A virtual element tree and reconciler that diff old vs. new descriptions and patch only what changed on real WinUI controls
- Hooks-based state (`UseState`, `UseEffect`, `UseReducer`, …) co-located with render logic
- A C# DSL that replaces XAML markup with typed factory methods and fluent modifiers
- Higher-level building blocks — flex layout, charting, commanding, navigation, data grid, theming, localization — many of which are candidates to migrate back into WinUI itself

```csharp
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<MyApp>("Hello Reactor");

class MyApp : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return VStack(
            Heading($"Count: {count}"),
            HStack(8,
                Button("-", () => setCount(count - 1)),
                Button("+", () => setCount(count + 1))
            )
        );
    }
}
```

No `App.xaml`. No `MainWindow.xaml`. No code-behind. No ViewModels. Just C# with full IntelliSense, refactoring, and compile-time type safety.

---

## XAML and MVVM are not going anywhere

WinUI is a powerful native platform, and the XAML + MVVM programming model is a great fit for a large portion of the WinUI user base. Tooling, designer support, templating, data binding, and years of guidance, samples, and third-party controls are built around it. Reactor does **not** try to replace any of that.

What Reactor does target is a specific gap: developers coming from React, SwiftUI, or Jetpack Compose expect co-located state, declarative composition, and type-safe UI construction. Reactor brings those patterns to WinUI without asking anyone to abandon the platform they already ship on. The two models coexist:

- **If you're shipping XAML/MVVM today**, keep shipping XAML/MVVM. The investments we're making in Reactor — improved accessibility APIs, richer theming, better commanding, a stronger data stack — are designed to flow back into WinUI so you benefit too.
- **If you want a functional MVU model** with no XAML and no ViewModels, Reactor is a path to get there on the same runtime, with the same controls, in the same process.
- **If you want both**, you can host Reactor content inside a XAML app, or drop XAML content inside a Reactor app. They share the visual tree.

Many of the experiments in this repo — the charting stack, accessibility validators, commanding, flex layout, theming tokens — are likely to land in WinUI directly, where they'll work for XAML and MVVM users too. Others may continue to ship through Reactor. Either way, **WinUI is the foundation**.

---

## What's included

Reactor spans a core framework and a set of higher-level features. Each area below is labeled by its current maturity — *Preview* is the most mature, then *Draft*, then *Early*. **All areas are pre-1.0 and the public API surface may change without notice while the project is labeled experimental.**

| Area | What it does | Maturity |
|---|---|---|
| **Core reconciler** | Virtual element tree, keyed diffing, element pooling, render coalescing, skip-unchanged optimization | Preview |
| **DSL & elements** | Factory methods covering WinUI controls, fluent modifier chains, attached properties | Preview |
| **Hooks & state** | UseState, UseReducer, UseEffect, UseMemo, UseRef, UseObservable, UseCollection | Preview |
| **Flex layout** | C# port of facebook/yoga with FlexPanel, 590 ported test fixtures | Preview |
| **Commanding** | Command records bundling label, icon, shortcut, and action; 16 standard commands; focus-scoped accelerators | Preview |
| **Charting (D3)** | Full D3 algorithm port plus declarative chart DSL — line, bar, area, pie, tree, force-directed graphs | Preview |
| **Markdown** | Native md4c parser with `Markdown()` element builder | Preview |
| **Navigation** | Type-safe declarative routing, GPU composition transitions, lifecycle guards, back-stack serialization | Draft |
| **Accessibility** | `AutomationProperties` modifiers, WCAG 2.1 AA target, compile- and runtime-validation | Draft |
| **WinForms interop** | Simple hosting of WinUI content inside WinForms apps | Draft |
| **Theming & styling** | `ThemeRef` tokens, dark / light / high-contrast, style caching, per-control overrides, Roslyn analyzers | Early |
| **Animation** | Compositor-layer transitions, keyframes, stagger, scroll-linked and connected animations | Early |
| **Localization** | ICU message format, source generator, CLI tooling (extract, translate, validate), RTL/BiDi | Early |
| **Lists & virtualization** | Virtualized `ListView`, `GridView`, `ItemsRepeater`, `LazyStack` with recycling | Early |
| **Data system** | `DataGrid`, `PropertyGrid`, `FormField`, metadata model, async validation, inline editing | Early |
| **Preview / hot reload** | `MetadataUpdateHandler` hot reload, CLI `--preview` flag, VS Code live preview | Early |

> Maturity labels reflect the breadth of testing and the likelihood of API churn, not feature completeness. *Preview* areas have selftest and e2e coverage and are the most stable. *Draft* areas work on the golden path but may have rough edges or partial AOT support. *Early* areas are usable but expect bigger shape changes as we iterate.

---

## What's in it for you

### Fundamentals — for everyone

The core framework has been through 13 days of continuous reconciler iteration, a competitive review against React, SwiftUI, and Compose, and a 275-finding code review. It targets the basics every WinUI developer cares about:

- **Performance.** Element pooling, render coalescing, skip-unchanged optimization, native interop that bypasses CsWinRT overhead on hot paths.
- **Stability.** Built as a system component: high reliability bar, structured logging, stress testing.
- **Developer experience.** Full IntelliSense, refactoring, and compile-time type safety — no XAML string-typing, no binding errors at runtime.
- **Localization.** ICU message format with pluralization, CLI extraction, and AI-assisted translation.
- **Accessibility.** Full exposure of WinUI's built-in accessibility through a simple API, with inline dev-time linting and runtime validation.

### Frontier developers — a new programming model

If you're excited about declarative UI and functional patterns:

- **Functional MVU.** Describe UI as pure C# expressions. Familiar to React, SwiftUI, and Compose users, and a natural fit for AI-agent-assisted authoring.
- **Hooks-based state** co-located with render logic — no ViewModel boilerplate, no binding expressions.
- **Immutable element trees** with automatic reconciliation — describe what the UI *should* look like, not how to mutate it.
- **Flex layout** via a faithful Yoga port — familiar to anyone who has used CSS flexbox.
- **Declarative animation** driven by compositor-layer features — transitions, keyframes, and connected animations.

### Enterprise developers — data and productivity

If you're building line-of-business applications:

- **DataGrid** with headless state management, column DSL, sort, selection, keyboard navigation, inline editing, column resize, and async validation.
- **PropertyGrid** with metadata-driven type-to-editor registry, recursive decomposition, and INPC integration.
- **Forms & validation** pipeline with `FormField`, `ValidationRule`, and auto-validation.
- **Charting.** Composable services for line, bar, area, pie, tree, and force-directed graphs, built on an industry-standard D3 port.
- **Commanding** that bundles label, icon, keyboard shortcut, and action into a single definition surfaced across menus, toolbars, and context menus.
- **WinForms migration.** Tools for incrementally adopting WinUI inside an existing WinForms app.

---

## Quick start

See **[docs/guide/getting-started.md](docs/guide/getting-started.md)** for the full walkthrough — prerequisites, building the framework and `mur` CLI from source, scaffolding your first app, and running through hello-world, a todo list, and a calculator with screenshots at each step.

---

## Live preview

Reactor includes a built-in preview mode — mount a single component in isolation with hot reload, from the CLI or VS Code.

### CLI preview

Any Reactor app that passes `preview: true` to `ReactorApp.Run` supports the `--preview` flag:

```csharp
ReactorApp.Run<App>("My App", preview: true);
```

```bash
# Preview a specific component with hot reload
dotnet watch run --project MyApp -- --preview CounterDemo

# Preview the first component in the project
dotnet watch run --project MyApp -- --preview

# List available components
dotnet run --project MyApp -- --preview-list
```

### VS Code extension

The **Reactor Preview** extension adds a live preview panel to VS Code. It runs a single preview process and switches between components instantly over HTTP — no process restart when you change components.

```bash
cd src/vscode-reactor
npm install
npm run compile
code --install-extension vscode-reactor
```

Open a C# file with a Reactor `Component`, run **Reactor: Preview Component** from the command palette, and a preview panel opens beside your editor. It auto-detects components in the current file, hot-reloads on save via `dotnet watch`, follows your editor between files in the same project, and only relaunches when you cross project boundaries.

---

## How we're releasing it

Reactor is on GitHub as an **experimental** project. The label will stay on for 3–6 months as we iterate on the design in the open.

We are not launching with a big public announcement. Instead we're starting with MVPs and trusted community members — gathering feedback, pressure-testing the API surface, and refining the programming model before broadening adoption. We want the design shaped by real developer experience, not just our internal usage.

Everything in the repository — framework code, specs, sample apps, test suites — is available for anyone to read, build, and experiment with. Contributions and feedback are welcome from day one.

Expect change. Every line of code in this project is fair game. The DSL syntax may shift as we work with the C# language team, controls may be added or removed, layering may be reorganized. This is your chance to shape the design while the sausage is getting made.

---

## Project structure

```
src/Reactor/                Core framework
  Core/                     Reconciler, components, hooks, elements
  Elements/                 DSL factory methods + fluent modifiers
  Flex/                     FlexPanel — CSS Flexbox via Yoga
  Yoga/                     Pure C# port of Meta's Yoga layout engine
  Hosting/                  App bootstrap, render loop, hot reload, preview capture server
src/Reactor.Cli/            CLI scaffolding tool
src/vscode-reactor/         VS Code extension — live preview panel
tests/
  Reactor.Tests/            xUnit unit tests — 2,200+ tests incl. 590 Yoga fixtures
  Reactor.AppTests/         MSTest runner — orchestrates selfhost + Appium
  Reactor.AppTests.Host/    Selfhost test app — 60+ in-process fixtures
  stress_perf/              Performance benchmarks
samples/
  apps/                     Sample apps (wordpuzzle, ductfiles, regedit, etc.)
  TodoApp/                  Todo app sample
```

---

## Sample apps

### WordPuzzle — word search game

A word search game demonstrating grid layout, timers, and interactive state.

```bash
dotnet run --project samples/apps/wordpuzzle
```

---

## Learn more

| Doc | Description |
|-----|-------------|
| [Guide](docs/guide/) | Documentation on how to use Reactor |
| [Design Specs](docs/specs/) | Numbered specs covering theming, navigation, animation, data, accessibility |
| [AOT support matrix](docs/aot-support.md) | Which subsystems work under `PublishAot=true` and which throw at runtime |
| [Contributing](CONTRIBUTING.md) | Build, test, add features, code style |

---

## License

[MIT](LICENSE)
