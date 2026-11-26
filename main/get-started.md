---
title: "Reactor"
description: "Microsoft.UI.Reactor (Reactor) is a declarative UI framework for building native Windows desktop apps in pure C#. No XAML. No data binding. No view models. You describe your UI as a function of state, and Reactor keeps the screen in sync."
sidebar:
  label: "Overview"
---

| If you want to... | Start here |
|-------------------|------------|
| Build your first Reactor app | [Getting Started](/microsoft-ui-reactor/main/get-started/quickstart) |
| Understand the mental model | [Thinking in Reactor](/microsoft-ui-reactor/main/get-started/thinking-in-reactor) |
| Port a XAML/WinUI/WPF app | [Reactor for XAML Developers](/microsoft-ui-reactor/main/get-started/xaml-developers) |
| Look up a hook or modifier | [API Reference](/microsoft-ui-reactor/main/reference/hooks) |
| Read how the runtime works | [Architecture Overview](/microsoft-ui-reactor/main/how-it-works/architecture-overview) |

## Why Reactor? [#why-reactor]

**Pure C# from top to bottom.** Your entire app — layout, styling, state,
logic — lives in `.cs` files. No markup languages, no code-behind split, no
designer files.

**Declarative rendering.** You write a `Render()` method that returns your UI.
When state changes, Reactor diffs the old and new element trees and patches only
what changed in the native WinUI controls.

**Hooks for state management.** `UseState`, `UseReducer`, `UseEffect`, and
friends give you React-style state management without the JavaScript.

**Native performance.** Reactor renders to real WinUI 3 controls. Your app is a
standard Windows desktop app — no web views, no Electron, no interpretation
layer.

## Quick Look [#quick-look]

A complete Reactor app in one file:

```csharp
class HelloWorld : Component
{
    public override Element Render()
    {
        return VStack(12,
            TextBlock("Hello from Reactor!").FontSize(24).Bold(),
            TextBlock("No XAML. No data binding. Just C#.")
        ).Padding(24);
    }
}
```

![Hello world component](/microsoft-ui-reactor/main/images/readme/hello-world.png)

State and interactivity in a few lines:

```csharp
class QuickCounter : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return HStack(8,
            Button("- 1", () => setCount(count - 1)),
            TextBlock($"{count}").FontSize(20).SemiBold().Width(40)
                .HAlign(HorizontalAlignment.Center),
            Button("+ 1", () => setCount(count + 1))
        ).Padding(24);
    }
}
```

![Counter demo](/microsoft-ui-reactor/main/images/readme/counter.png)

Built-in text styling:

```csharp
class StyledText : Component
{
    public override Element Render()
    {
        return VStack(8,
            Heading("Heading element"),
            SubHeading("SubHeading element"),
            TextBlock("Regular text with modifiers")
                .FontSize(14).Foreground(Theme.Accent),
            Caption("Caption for fine print")
        ).Padding(24);
    }
}
```

![Styled text elements](/microsoft-ui-reactor/main/images/readme/styled-text.png)

## How It Works [#how-it-works]

1. You define [**components**](/microsoft-ui-reactor/main/guides/components) — classes with a `Render()` method that returns
   an element tree.
2. You manage state with [**hooks**](/microsoft-ui-reactor/main/guides/hooks) — `UseState`, `UseReducer`, `UseEffect`,
   and more — called inside `Render()`.
3. When state changes, Reactor calls `Render()` again, diffs the result, and
   updates only the WinUI controls that changed.

That's it. No event subscriptions to manage, no property-changed notifications
to wire up, no dispatcher threading to worry about.

## Documentation [#documentation]

The docs follow the site's tabs, working from "first app on screen" to "how
the runtime is built". XAML developers should start with Coming from XAML and
Reactor vs XAML; everyone else can follow the order.

### Get started [#get-started]

- **[Quickstart](/microsoft-ui-reactor/main/get-started/quickstart)** — Create your first app, manage state, build a todo list
- **[Thinking in Reactor](/microsoft-ui-reactor/main/get-started/thinking-in-reactor)** — Mental-model essay: UI as a function of state
- **[Reactor for XAML Developers](/microsoft-ui-reactor/main/get-started/xaml-developers)** — Migration cookbook: XAML, bindings, MVVM, navigation
- **[Reactor vs XAML](/microsoft-ui-reactor/main/get-started/reactor-vs-xaml)** — Architectural essay: DependencyProperty → modifier, Binding → closure
- **[Cheat Sheet](/microsoft-ui-reactor/main/get-started/cheat-sheet)** — Single-page reference card
- **[Rules of Reactor](/microsoft-ui-reactor/main/get-started/rules-of-reactor)** — Hook rules, render purity, anti-patterns

### Guides [#guides]

**Core concepts**

- **[Components](/microsoft-ui-reactor/main/guides/components)** — Component classes, props, function components, composition
- **[Hooks](/microsoft-ui-reactor/main/guides/hooks)** — [UseState](/microsoft-ui-reactor/main/reference/hooks/UseState), [UseReducer](/microsoft-ui-reactor/main/reference/hooks/UseReducer), [UseEffect](/microsoft-ui-reactor/main/reference/hooks/UseEffect), [UseMemo](/microsoft-ui-reactor/main/reference/hooks/UseMemo), [UseRef](/microsoft-ui-reactor/main/reference/hooks/UseRef), [UseCallback](/microsoft-ui-reactor/main/reference/hooks/UseCallback)
- **[Effects and Lifecycle](/microsoft-ui-reactor/main/guides/effects)** — UseEffect patterns, cleanup, async work, timers
- **[Context](/microsoft-ui-reactor/main/guides/context)** — Share state across the component tree without prop drilling
- **[Commanding](/microsoft-ui-reactor/main/guides/commanding)** — Commands, keyboard shortcuts, async actions
- **[Advanced Patterns](/microsoft-ui-reactor/main/guides/advanced)** — ErrorBoundary, Memo, observable binding, performance tuning

**Layout and styling**

- **[Layout](/microsoft-ui-reactor/main/guides/layout)** — VStack, HStack, Grid, ScrollView, Border, responsive patterns
- **[Flex Layout](/microsoft-ui-reactor/main/guides/flex-layout)** — Flexible box layout for adaptive UIs
- **[Styling and Theming](/microsoft-ui-reactor/main/guides/styling)** — Colors, typography, dark/light themes, custom styles
- **[Theming Tokens](/microsoft-ui-reactor/main/guides/theming-tokens)** — Full token catalog with swatches
- **[Animation](/microsoft-ui-reactor/main/guides/animation)** — Transitions, keyframes, interaction states, choreography
- **[Input and Gestures](/microsoft-ui-reactor/main/guides/input-and-gestures)** — Pointer events, taps, gestures, access keys

**Building apps**

- **[Navigation](/microsoft-ui-reactor/main/guides/navigation)** — NavigationView, TabView, multi-page apps, routing
- **[Windows](/microsoft-ui-reactor/main/guides/windows)** — Top-level windows, tray icons, jump lists, `OpenWindow`, shutdown policy
- **[Advanced Windowing](/microsoft-ui-reactor/main/guides/windowing-advanced)** — Layered overlays, window regions, and other interop-heavy recipes
- **[Docking Windows](/microsoft-ui-reactor/main/guides/docking)** — Rearrangeable dock panes: splits, tabs, side pins, floating tear-outs, persistence
- **[Async Resources](/microsoft-ui-reactor/main/guides/async-resources)** — `UseResource`, `UseInfiniteResource`, `UseMutation`, `Pending`
- **[Persistence](/microsoft-ui-reactor/main/guides/persistence)** — [UsePersisted](/microsoft-ui-reactor/main/reference/hooks/UsePersisted), scopes, migration

**Ship your app**

- **[Accessibility](/microsoft-ui-reactor/main/guides/accessibility)** — Screen readers, keyboard navigation, focus trapping, runtime scanning
- **[Localization](/microsoft-ui-reactor/main/guides/localization)** — Multi-language support, resource strings, RTL layouts
- **[Testing](/microsoft-ui-reactor/main/guides/testing)** — Headless renderer, structural assertions, async test patterns
- **[Performance](/microsoft-ui-reactor/main/guides/performance)** — ETW, EventDispatch, flame graphs
- **[Packaging](/microsoft-ui-reactor/main/guides/packaging)** — MSIX, single-file, ARM64, AOT considerations

**Developer tools**

- **[Dev Tooling](/microsoft-ui-reactor/main/guides/dev-tooling)** — `mur` CLI, MCP server, VS Code panel, dotnet watch, in-app dev menu
- **[VS Extension](/microsoft-ui-reactor/main/guides/vs-extension)** — Rough experimental embedded preview for Visual Studio 2026 (18.x)
- **[Diagnostics](/microsoft-ui-reactor/main/guides/diagnostics)** — Framework warnings, ETW events, and how to read them

**Interop and migration**

- **[WinForms Interop](/microsoft-ui-reactor/main/guides/winforms-interop)** — Host Reactor components inside WinForms apps via XAML Islands
- **[WPF Interop](/microsoft-ui-reactor/main/guides/wpf-interop)** — Host Reactor components inside WPF apps
- **[Optional&lt;T&gt; controlled props](/microsoft-ui-reactor/main/guides/optional-t)** — Migrating to `Optional<T>` controlled props
- **[Windowing evolution](/microsoft-ui-reactor/main/guides/windowing-evolution)** — Migrating to the current windowing API

### Controls [#controls]

- **[Controls](/microsoft-ui-reactor/main/controls)** — Thumbnail-indexed catalog of every control
- **[Forms and Input](/microsoft-ui-reactor/main/controls/forms)** — Text fields, checkboxes, sliders, validation, data entry
- **[Collections](/microsoft-ui-reactor/main/controls/collections)** — ListView, LazyVStack, VirtualList for large datasets
- **[Text and Media](/microsoft-ui-reactor/main/controls/text-and-media)** — TextBlock, MarkdownTextBlock, Image, MediaPlayerElement, WebView2, InkCanvas
- **[Status and Info](/microsoft-ui-reactor/main/controls/status-and-info)** — InfoBar, InfoBadge, ProgressBar, TeachingTip, PipsPager
- **[Dialogs and Flyouts](/microsoft-ui-reactor/main/controls/dialogs-and-flyouts)** — ContentDialog, MenuFlyout, CommandBarFlyout, Popup
- **[Data System](/microsoft-ui-reactor/main/controls/data-system)** — DataGrid with sort, filter, search, inline editing
- **[Charting](/microsoft-ui-reactor/main/controls/charting)** — Line, bar, area, pie charts with the ReactorCharting library
- **[Win2D Canvas](/microsoft-ui-reactor/main/controls/win2d-canvas)** — Immediate-mode drawing surfaces from the optional `Microsoft.UI.Reactor.Advanced` package (manual, animated, virtual)

### Recipes [#recipes]

- **[Recipes](/microsoft-ui-reactor/main/recipes)** — Gallery of end-to-end patterns (login, master-detail, paginated list, command palette, …)

### API reference [#api-reference]

Auto-generated from XML doc comments in `src/Reactor*/`. Each member gets a
uniform Summary / Parameters / Returns / Discussion / Examples / See Also page.

- **[Hooks](/microsoft-ui-reactor/main/reference/hooks)** — every `Use*` hook
- **Factories** — every element factory (coming soon)
- **Modifiers** — every chainable modifier (coming soon)
- **Elements** — every Element record type (coming soon)
- **System** — App, Window, Navigation, Context, Command (coming soon)

### How it works [#how-it-works_1]

The runtime explained — for readers who want to know *why* it works the way it
does, not just how to use it.

- **[Architecture Overview](/microsoft-ui-reactor/main/how-it-works/architecture-overview)** — Declarative shell → element records → reconciler → WinUI tree
- **[Reactivity Model](/microsoft-ui-reactor/main/how-it-works/reactivity-model)** — setState → re-render; why hooks not INotifyPropertyChanged
- **[Hooks Internals](/microsoft-ui-reactor/main/how-it-works/hooks-internals)** — Hook slot table, dispatcher, closure capture
- **[Reconciliation](/microsoft-ui-reactor/main/how-it-works/reconciliation)** — Element-record diff, identity, the three phases
- **[Effects Scheduling](/microsoft-ui-reactor/main/how-it-works/effects-scheduling)** — When effects run; dep semantics; cleanup ordering
- **[Threading and Dispatch](/microsoft-ui-reactor/main/how-it-works/threading-and-dispatch)** — UI-thread invariants, trampoline, batched renders
- **[Element Pool](/microsoft-ui-reactor/main/how-it-works/element-pool)** — Allocation reduction under scroll-heavy lists
- **[Control Reconciler Protocol](/microsoft-ui-reactor/main/how-it-works/control-reconciler-protocol)** — The per-control handler protocol: `IElementHandler`, descriptors, children strategies, pooling
- **[Extending Reactor Controls](/microsoft-ui-reactor/main/how-it-works/extending-reactor-controls)** — Cookbook: add a native control end-to-end with a descriptor
- **[Modifier System](/microsoft-ui-reactor/main/how-it-works/modifier-system)** — How `.FontSize(24).Bold()` actually works
- **[Animation Pipeline](/microsoft-ui-reactor/main/how-it-works/animation-pipeline)** — Composition API end-to-end; 4 animation systems
- **[Focus and Input Internals](/microsoft-ui-reactor/main/how-it-works/focus-and-input-internals)** — `UseFocus` dispatcher, `FocusTrap` container, pointer event flow
- **[Source Mapping](/microsoft-ui-reactor/main/how-it-works/source-mapping)** — How stack traces and devtools attribute work back to user source
- **[Analyzer Architecture](/microsoft-ui-reactor/main/how-it-works/analyzer-architecture)** — Roslyn analyzers shipped with Reactor; authoring your own
- **[DevTools Internals](/microsoft-ui-reactor/main/how-it-works/devtools-internals)** — Dev menu, reconcile-highlight overlay, MCP protocol
- **[Perf Instrumentation](/microsoft-ui-reactor/main/how-it-works/perf-instrumentation)** — ETW sources, frame-aligned sampling

## Minimal Project Setup [#minimal-project-setup]

The fastest path is [`dotnet new reactor`](/microsoft-ui-reactor/main/get-started/quickstart), which
scaffolds all of this for you. To wire it up by hand instead, create a console
project and edit the `.csproj`:

<!-- ai:lock -->
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
    <UseWinUI>true</UseWinUI>
    <WindowsPackageType>None</WindowsPackageType>
    <!-- Windows App SDK needs a concrete architecture; without this an
         unqualified `dotnet build` / `dotnet run` fails with
         "WindowsAppSDKSelfContained requires a supported Windows architecture". -->
    <RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == '' And ('$(Platform)' == '' Or '$(Platform)' == 'AnyCPU')">$(NETCoreSdkPortableRuntimeIdentifier)</RuntimeIdentifier>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.UI.Reactor" Version="0.1.0-preview.18" />
  </ItemGroup>
</Project>
```
<!-- /ai:lock -->

One package reference is enough — `Microsoft.UI.Reactor` brings the Windows App
SDK in transitively, so you only add `Microsoft.WindowsAppSDK` yourself when you
want to pin a specific WinUI patch or bundle the runtime with
`WindowsAppSDKSelfContained`. Pin WinUI through that full package, not
`Microsoft.WindowsAppSDK.WinUI` on its own, which fails the Windows App SDK's
version check. See [Packaging](/microsoft-ui-reactor/main/guides/packaging) for the
`<Platforms>`, ARM64, and self-contained shapes the project template uses.

Replace `App.cs` with a component and a `ReactorApp.Run<T>()` call, and run with
`dotnet run`. That's your first Reactor app.

## Tips [#tips]

**Start with function components.** For quick experiments, use
`ReactorApp.Run("Title", ctx => { ... })` — no class needed.

**Read the [hooks page](/microsoft-ui-reactor/main/guides/hooks).** Hooks are the core of Reactor. Understanding `UseState`
and `UseEffect` unlocks everything else.

**Keep components small.** Extract pieces into their own components early.
Composition is always easier to reason about than a single giant `Render()`.

## Prefer Rust? [#prefer-rust]

The idea isn't tied to C#. Over in [windows-rs](https://github.com/microsoft/windows-rs),
the Rust for Windows project, the same experiment is running in Rust:
[**windows-reactor**](https://github.com/microsoft/windows-rs/blob/HEAD/docs/crates/windows-reactor.md)
is a declarative WinUI library where components own their state, controls send
typed messages back, and only the WinUI controls that changed get updated. It's a
separate library with its own model, available on
[crates.io](https://crates.io/crates/windows-reactor). Different language, same
native controls underneath.

## Next Steps [#next-steps]

- **[Getting Started](/microsoft-ui-reactor/main/get-started/quickstart)** — Build your first app, manage state, ship a todo list.
- **[Hooks](/microsoft-ui-reactor/main/guides/hooks)** — `UseState`, `UseEffect`, `UseMemo`, and the rest of the core state primitives.
- **[Components](/microsoft-ui-reactor/main/guides/components)** — Component classes, props, and function components in detail.
- **[Reactor for XAML Developers](/microsoft-ui-reactor/main/get-started/xaml-developers)** — Migration cookbook for XAML, bindings, MVVM, and navigation.
