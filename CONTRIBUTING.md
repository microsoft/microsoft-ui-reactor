# Contributing to Reactor

Reactor lives at **[github.com/microsoft/microsoft-ui-reactor](https://github.com/microsoft/microsoft-ui-reactor)**. Reactor is an experimental project — the API surface, DSL, and layering are all subject to change as we iterate in the open. Contributions and feedback are welcome from day one.

- **Report a bug or propose a feature:** [open an issue](https://github.com/microsoft/microsoft-ui-reactor/issues/new/choose)
- **Ask a question or float an idea:** [start a discussion](https://github.com/microsoft/microsoft-ui-reactor/discussions)
- **Submit a change:** open a PR against `main` — please link the issue it addresses, keep the change focused, and include tests

When filing an issue, include the platform (`x64` / `ARM64`), .NET SDK version, and a minimal repro. For bugs that involve real WinUI controls, a selfhost fixture (see below) is the ideal repro format.

---

## Contributor License Agreement

This project welcomes contributions and suggestions. Most contributions require you to agree to a Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us the rights to use your contribution. For details, visit [https://cla.opensource.microsoft.com](https://cla.opensource.microsoft.com).

When you submit a pull request, a CLA bot will automatically determine whether you need to provide a CLA and decorate the PR appropriately (e.g., status check, comment). Follow the instructions provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows App SDK 2.0 — restored automatically from NuGet, no manual install required
- Visual Studio 2022 (17.8+) or VS Code with C# Dev Kit

> **Package version:** All projects reference `Microsoft.WindowsAppSDK` **2.0.1** (public NuGet). The version is centralized in `Directory.Build.props` — update it there to change the version for every project at once.

---

## Building

### From the command line

```bash
# Restore packages (pulls experimental WinUI 3 from NuGet)
dotnet restore Reactor.slnx

# Build the entire solution (framework, tests, test app, samples)
dotnet build Reactor.slnx

# Build just the framework
dotnet build src/Reactor/Reactor.csproj
```

### From Visual Studio

1. Open `Reactor.slnx` in Visual Studio 2022 (17.8+)
2. Select the **x64** or **ARM64** platform from the toolbar
3. Build the solution (Ctrl+Shift+B)

Visual Studio will restore NuGet packages on first load, pulling the experimental Windows App SDK.

### Platforms

Library projects (`Reactor`, `Reactor.Interop.WinForms`) are architecture-neutral (`AnyCPU`). Application projects (samples, tests, CLI) target `x64` and `ARM64`.

When building via the solution (`dotnet build Reactor.slnx`), the platform is selected automatically. When building a single app project directly, pass `-p:Platform=x64` (or `ARM64`):

```bash
dotnet build tests/Reactor.Tests -p:Platform=x64
dotnet test  tests/Reactor.Tests -p:Platform=x64
```

---

## Running tests

See [`TESTING.md`](TESTING.md) for the full guide — three suites (unit / selftest / E2E), how to pick a tier, NativeAOT runs, and the code-coverage workflow.

Quick reference:

```bash
dotnet test tests/Reactor.Tests       # unit (xUnit, headless, fast)
dotnet test tests/Reactor.SelfTests   # selftest (real WinUI, in-process)
dotnet test tests/Reactor.AppTests    # E2E (Appium/WinAppDriver)
```

---

## Running the demo app

The interactive demo app exercises every built-in control:

```bash
dotnet run --project samples/Reactor.TestApp
```

---

## Project layout

```
src/Reactor/                      Core framework library
  Core/
    Component.cs                  Base Component class, hook methods
    Element.cs                    40+ virtual element record types
    RenderContext.cs              Hook state storage, effect tracking
    Reconciler.cs                 Tree diff orchestration
    Reconciler.Mount.cs           Mount handlers for each element type
    Reconciler.Update.cs          Update handlers for each element type
    ChildReconciler.cs            Keyed child list reconciliation
    ElementPool.cs                Control reuse pool
    PropValueRegistry.cs          Property value caching/hashing
  Elements/
    Dsl.cs                        200+ static factory methods (TextBlock, Button, VStack, Flex, etc.)
    ElementExtensions.cs          Fluent modifiers (.Bold(), .Margin(), .Width(), etc.)
    FlexExtensions.cs             .Flex() attached property modifier for flex children
  Flex/
    FlexPanel.cs                  CSS Flexbox panel backed by Yoga layout engine
  Yoga/
    YogaAlgorithm.cs              Pure C# port of Meta's Yoga layout algorithm
    YogaNode.cs                   Yoga node tree structure
    YogaStyle.cs                  Style properties (direction, justify, align, etc.)
    YogaEnums.cs                  Yoga enum types (YogaFlexDirection, YogaJustify, etc.)
  Hosting/
    ReactorApp.cs                 Static entry point — ReactorApp.Run<T>()
    ReactorHost.cs                Render loop, state batching, dispatcher scheduling
    ReactorHostControl.cs         Embeddable host for existing WinUI apps
    HotReloadService.cs           .NET Hot Reload integration for Visual Studio
src/Reactor.Cli/                  CLI scaffolding tool
tests/
  Reactor.Tests/                  1. Unit tests — xUnit (no UI window; includes D3 charting tests)
  Reactor.SelfTests/              2. Selftest runner — MSTest wrapper that subprocess-launches the Host and parses TAP
  Reactor.AppTests.Host/          2. Host app — hosts selftest fixtures and the Appium fixture navigator
  Reactor.AppTests/               3. E2E tests — MSTest + Appium/WinAppDriver
  stress_perf/                    Performance benchmarks
samples/
  Reactor.TestApp/                Interactive control showcase / demo app
  apps/                           Sample apps (wordpuzzle, ductfiles, regedit, etc.)
  TodoApp/                        Todo app sample
```

---

## How to add a new element type

Adding a new WinUI control to Reactor requires changes in four places (plus optional modifiers and tests).

### 1. Define the element record (`src/Reactor/Core/Element.cs`)

```csharp
public record MyControlElement(
    string Label,
    Action? OnClick = null
) : Element;
```

### 2. Add a DSL factory method (`src/Reactor/Elements/Dsl.cs`)

```csharp
public static MyControlElement MyControl(string label, Action? onClick = null)
    => new(label, onClick);
```

### 3. Add a mount handler (`src/Reactor/Core/Reconciler.Mount.cs`)

```csharp
private FrameworkElement MountMyControl(MyControlElement el)
{
    var control = new WinUI.MyControl();
    control.Label = el.Label;
    if (el.OnClick != null)
    {
        SetElementTag(control, el);
        control.Click += (s, _) => GetElementTag<MyControlElement>(s)?.OnClick?.Invoke();
    }
    return control;
}
```

Register it in the mount dispatch switch in `Mount()`.

### 4. Add an update handler (`src/Reactor/Core/Reconciler.Update.cs`)

```csharp
private void UpdateMyControl(WinUI.MyControl control, MyControlElement old, MyControlElement @new)
{
    if (old.Label != @new.Label) control.Label = @new.Label;
    SetElementTag(control, @new);
}
```

Register it in the update dispatch switch in `Update()`.

### 5. (Optional) Add modifiers (`src/Reactor/Elements/ElementExtensions.cs`)

If the control has properties that make sense as fluent modifiers, add extension methods.

### 6. Add tests

Add unit tests in `tests/Reactor.Tests/` for element creation, mount, and update. If the control has user-facing behavior, add a selfhost fixture in `tests/Reactor.AppTests.Host/SelfTest/Fixtures/`.

---

## How to add a new hook

Hooks live in `src/Reactor/Core/Component.cs` (public API) and `src/Reactor/Core/RenderContext.cs` (implementation).

1. Add the hook method to `Component` (delegates to `RenderContext`)
2. Implement the logic in `RenderContext`, using `GetOrCreateHook<T>()` to manage state
3. Follow the convention: hooks must be called in the same order every render, no conditional calls
4. Add tests in `tests/Reactor.Tests/`

---

## Documenting changes

New public surface in `src/Reactor/` lands with a doc page — no merge without doc. The bar is **Solid tier or higher** (spec [041 §11](docs/specs/041-docs-comprehensive-uplift.md)); Comprehensive is preferred when the surface is top-traffic or has non-obvious mental-model implications.

What "lands with a doc page" means in practice depends on the change:

| Change | Doc obligation |
|---|---|
| New element factory in `Dsl.cs` | Extend the matching topic page (e.g. add to the controls catalog under `text-and-media`, `forms`, `collections`, etc.) with at least a row in the reference table + a snippet ref. Add a `<!-- ref:Member -->` marker so the generated reference page backlinks the topic. |
| New hook on `RenderContext` / `Component` | Add to `hooks.md` reference table + a Pattern section showing real usage. Add a `<!-- ref:UseX -->` marker. |
| New public type that doesn't fit an existing topic | Author a new template under `docs/_pipeline/templates/`. Solid tier is the minimum for net-new pages. Add an entry to `reference-map.yaml` so reference generation routes its members. |
| Internal refactor with no public-API change | No doc page required, but if you renamed a public symbol that templates reference, update those templates in the same PR. |

The CI tier-drift gate (`docs-check-tier` in `.github/workflows/ci.yml`, spec [041 §5.2](docs/specs/041-docs-comprehensive-uplift.md)) blocks merges that knock a template's declared tier out of compliance with its §11 structural shape. The doc pipeline also emits `REACTOR_DOC_REGISTRY_W002` when a registry-declared guide page has no inbound `<!-- ref:Member -->` markers — that warning surfaces public API that has slipped through the doc-coverage gate.

For the full doc-pipeline workflow (compile, check-tier, render-diagrams), see [`docs/contributing/doc-pipeline.md`](docs/contributing/doc-pipeline.md).

---

## Code style

- **Elements are immutable records.** Use `with` expressions for variations.
- **Hooks follow React conventions.** Same order every render, no conditional hooks.
- **Factory methods over constructors.** `TextBlock("hello")` not `new TextBlockElement("hello")`.
- **Fluent modifiers for layout.** `.Margin(16).Bold()` not constructor parameters.
- **Tag-based event dispatch.** Event handlers are wired once at mount; the current element is stored in `Tag` so handlers always read the latest closure.
- **No XAML.** Everything is C#.

### Diagnostics: audience, not severity, decides the channel

`Debug.WriteLine` exists for the framework contributor reading the Output window in Visual Studio; it disappears in Release builds. `ReactorEventSource` (the `Microsoft-UI-Reactor` provider) exists for the app developer, SRE, and support engineer — it's release-visible, keyword-gated, and zero-allocation when no consumer is attached. New code that reports an error, a swallowed exception, or a failing HRESULT belongs on the EventSource side; new code that traces internal framework state for a contributor's benefit (reconciler bookkeeping, scheduler queue depth) stays on `Debug.WriteLine`.

For swallowed exceptions and HRESULT-return diagnostics, route through the `DiagnosticLog` helper:

```csharp
catch (COMException ex) when (ex.HResult is HResults.RPC_E_DISCONNECTED or HResults.E_FAIL)
{
    DiagnosticLog.SwallowedError(LogCategory.Hosting, "AppWindow.Close", ex);
}
```

`DiagnosticLog.SwallowedError` and `DiagnosticLog.HResultFailed` emit the typed ETW event under `Keywords.Errors` at `Warning` in Release **and** mirror a richer line (including `ex.Message`) to `Debug.WriteLine` in Debug builds via a `[Conditional("DEBUG")]` helper. The exception message never reaches the ETW payload — see [`docs/guide/diagnostics.md`](docs/guide/diagnostics.md) for the PII discipline and the full capture workflow.

---

## Hot reload

Reactor supports .NET Hot Reload via `HotReloadService.cs`. When you edit code in Visual Studio and save, the framework re-renders with your changes while preserving hook state. No special setup needed — it hooks into the standard `MetadataUpdateHandler` mechanism.
