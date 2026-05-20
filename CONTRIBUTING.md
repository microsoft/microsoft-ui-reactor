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

Reactor has three test suites. Each lives in its own project, so there are no filters to remember — one command per suite.

| # | Suite | Project | Runner | What it tests |
|---|-------|---------|--------|---------------|
| 1 | **Unit** | `tests/Reactor.Tests` | xUnit | Algorithms, reconciliation, Yoga layout, hooks, D3 — no WinUI window |
| 2 | **Selftest** | `tests/Reactor.SelfTests` | MSTest (wraps TAP subprocess) | Full reconciler pipeline against real WinUI controls, in-process |
| 3 | **E2E** | `tests/Reactor.AppTests` | MSTest + Appium/WinAppDriver | Cross-process UIA validation, real user input |

### Three commands

```bash
# 1. Unit
dotnet test tests/Reactor.Tests

# 2. Selftest (in-process WinUI, ~10s; no filter needed)
dotnet test tests/Reactor.SelfTests

# 3. E2E (requires WinAppDriver)
dotnet test tests/Reactor.AppTests

# All three
dotnet test tests/Reactor.Tests && dotnet test tests/Reactor.SelfTests && dotnet test tests/Reactor.AppTests
```

Both `Reactor.SelfTests` and `Reactor.AppTests` declare a `ProjectReference` to `Reactor.AppTests.Host` with `ReferenceOutputAssembly="false"`, so `dotnet test` rebuilds the Host first. No stale binaries.

### When to write which test

| If you're testing… | Write a… |
|---|---|
| An algorithm, pure function, record equality, hook bookkeeping, D3 math — anything that doesn't need a WinUI window | **Unit test** in `tests/Reactor.Tests/` |
| How an element mounts/updates against a real WinUI control, layout math against real Yoga+XAML, reconciler behavior end-to-end, assertions via `VisualTreeHelper` | **Selftest fixture** in `tests/Reactor.AppTests.Host/SelfTest/Fixtures/` (registered in `SelfTestFixtureRegistry`, wrapped by a `[TestMethod]` in `SelfTestBatch`) |
| Real user input (clicks, keystrokes, tab navigation), UIA properties as seen by assistive tech, cross-process behavior, XAML Island interop | **E2E test** in `tests/Reactor.AppTests/Tests/` |

Rule of thumb: start with a unit test. Drop to selftest only when you need a live control. Reach for E2E only when you need cross-process UIA — E2E is the slowest and flakiest tier.

### 1. Unit tests (`tests/Reactor.Tests`) — xUnit

xUnit tests covering framework internals **without a WinUI window**: element creation, reconciliation algorithms (LIS, keyed/positional), Yoga layout, localization, property hashing, control pooling, hooks, D3 charting math.

**When to run:** after any code change. Fast, no prerequisites beyond the .NET SDK.

```bash
dotnet test tests/Reactor.Tests

# Run a specific test class
dotnet test tests/Reactor.Tests --filter "FullyQualifiedName~ReconcilerMountUpdateTests"
```

### 2. Selftest (`tests/Reactor.SelfTests`) — MSTest + TAP

In-process checks that run inside a real WinUI window at CPU speed. Each fixture (in `tests/Reactor.AppTests.Host/SelfTest/Fixtures/`) mounts UI via `ReactorHost`, runs assertions through `VisualTreeHelper`, and emits TAP to stdout. `SelfTestBatch` launches the Host subprocess, parses TAP, and maps each fixture to a `[TestMethod]` so MSTest reports them individually. This is the **only** way to test the reconciler end-to-end against real WinUI controls.

**When to run:** after reconciler, control mount/update, or any UI-related changes.

```bash
dotnet test tests/Reactor.SelfTests
```

For faster iteration with raw TAP output, you can bypass MSTest and run the Host directly:

```bash
# Raw TAP output
dotnet run --project tests/Reactor.AppTests.Host -- --self-test

# Filter by fixture name prefix
dotnet run --project tests/Reactor.AppTests.Host -- --self-test --filter "Flex"
```

#### Running selftests under NativeAOT

The Host app supports an AOT-published build so the selftest suite doubles as Reactor's primary AOT regression gate. The framework itself is AOT-clean (see [`docs/aot-support.md`](docs/aot-support.md)) but a meaningful slice of selftest *fixtures* still trip over reflection paths the AOT compiler can't preserve. Those fixtures are pre-skipped via a baked-in pattern list so the run completes and the remaining failures are visible.

**1. Publish the Host with AOT.** The publish step shells out to MSVC's `link.exe`, so it must run inside a Visual Studio Developer environment. From a Developer Command Prompt / Developer PowerShell (or after sourcing `Launch-VsDevShell.ps1`):

```powershell
dotnet publish tests/Reactor.AppTests.Host `
    -c Release -p:Platform=x64 -r win-x64 `
    -p:PublishAotInternal=true --self-contained `
    -o artifacts/aot-host
```

`PublishAotInternal=true` is the internal opt-in property that flips `PublishAot` on for the Host (kept opt-in so an ordinary `dotnet build Reactor.slnx` doesn't pay the AOT compile cost). Swap `-r win-x64` / `-p:Platform=x64` for `win-arm64` / `ARM64` on ARM machines.

`-o artifacts/aot-host` pins the publish output to a stable, predictable path. Without it, the binary lands under the default-shape `tests/Reactor.AppTests.Host/bin/<Platform>/<Config>/<TFM>/<RID>/publish/Reactor.AppTests.Host.exe` — fine, but the TFM/RID/SDK-version segments drift over time, so the explicit `-o` is friendlier for scripts and docs.

**2. Run the suite.** Same `--self-test` flag as the JIT build:

```bash
./artifacts/aot-host/Reactor.AppTests.Host.exe --self-test
```

Output is the same TAP stream as a normal selftest run. The runner detects AOT at startup (`RuntimeFeature.IsDynamicCodeSupported == false`) and emits `# SKIP crashes/hangs under NativeAOT` lines for known-bad fixtures.

**3. Filtering known-bad fixtures.** The skip list lives in `DefaultAotSkipPatterns` in `tests/Reactor.AppTests.Host/SelfTest/SelfTestRunner.cs`. Entries are either an exact fixture name or a prefix-wildcard ending in `*` — by convention these match a fixture family, e.g. `MyFamily_*`. When you discover a new AOT crasher, you have two choices:

- **Without rebuilding** (best for iteration): append patterns via the `REACTOR_AOT_SKIP` env var. They merge into the defaults — they do *not* replace them.

  ```bash
  REACTOR_AOT_SKIP="MyFixture_Crasher,SomeFamily_*" \
    ./.../Reactor.AppTests.Host.exe --self-test
  ```

- **Permanent**: add the pattern to `DefaultAotSkipPatterns` and re-publish. Leave a comment naming the family / observed crash mode so a future contributor can verify whether the underlying issue has been fixed and drop the entry.

A native crash terminates the AOT process — the per-fixture managed watchdog can't fire. Iterate by tailing the TAP output for the *last* `# Running: <name>` line before exit, add that name to the skip list, and re-run. Be conservative when wildcarding a family: many `Family_*` fixtures pass even when one member crashes.

**4. Expected pass count.** As of 2026-05-20, an AOT run of the suite produces roughly: 735 fixtures total → 193 skipped, ~542 passed, 0 failed. The skip list covers fixtures that exercise subsystems documented as not-yet-AOT-clean in [`docs/aot-support.md`](docs/aot-support.md) (PropertyGrid auto-discovery, devtools/MCP, UseObservable on POCOs, anonymous-type localization args, theme resource lookup, XAML-metadata-dependent control hosting). When you fix one of those subsystems, drop the corresponding entries from `DefaultAotSkipPatterns`. The non-AOT run on the same commit is 735/735 pass.

### 3. E2E tests (`tests/Reactor.AppTests`) — MSTest + WinAppDriver

End-to-end tests that use Appium/WinAppDriver to simulate real user input (clicks, keyboard, tab navigation) through the cross-process UI Automation pipeline. These verify the full input → render → output path and validate that UIA properties are visible to assistive technology.

**When to run:** before shipping. Slow, and requires WinAppDriver.

E2E test classes (across two host apps):

| Class | Host | What it tests |
|-------|------|---------------|
| `InteractiveTests` | WinUI | Counter clicks, observable mutation |
| `AccessibilityTests` | WinUI | WCAG property validation via UIA |
| `AccessibilityInteractionTests` | WinUI | Keyboard nav, live regions, headings, semantic panels |
| `EventHandlerTests` | WinUI | OnTapped, OnSizeChanged, OnPointerPressed, OnKeyDown, UseReducer |
| `DataGridTests` | WinUI | Click-to-edit, keyboard commit |
| `WinFormsInteropTests` | WinForms | XAML Island rendering, tab navigation, UIA across boundaries |

```bash
dotnet test tests/Reactor.AppTests

# A specific class
dotnet test tests/Reactor.AppTests --filter "ClassName=Reactor.AppTests.Tests.AccessibilityTests"
```

> **Requires:** [WinAppDriver](https://github.com/microsoft/WinAppDriver/releases) installed at `C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe`. Unit and selftest runs don't need it.
>
> **WinForms tests** also require `Reactor.WinFormsTests.Host` to build. It launches a separate WinForms app with a XAML Island.

### Code coverage

The canonical coverage metric is **unit + selftest merged**. Run both and merge:

```bash
# (install once: dotnet tool install -g dotnet-coverage)

# --- Unit tests ---
dotnet build tests/Reactor.Tests -c Debug -p:Optimize=false -p:DebugType=portable
dotnet-coverage collect -s coverage.settings.xml \
  --output unit.cobertura.xml --output-format cobertura \
  -- dotnet test tests/Reactor.Tests --no-build

# --- Selftest ---
# Step 1: Rebuild with explicit Debug settings (required for instrumentation)
dotnet build src/Reactor                      -c Debug -p:Optimize=false -p:DebugType=portable --no-incremental
dotnet build tests/Reactor.AppTests.Host      -c Debug -p:Optimize=false -p:DebugType=portable --no-incremental

# Step 2: Instrument Reactor.dll statically
#         (dynamic instrumentation skips referenced assemblies)
dotnet-coverage instrument \
  "tests/Reactor.AppTests.Host/bin/$(RuntimeIdentifier)/Debug/net10.0-windows10.0.22621.0/Reactor.dll" \
  -s coverage.settings.xml

# Step 3: Collect
dotnet-coverage collect -s coverage.settings.xml \
  --output selftest.cobertura.xml --output-format cobertura \
  -- dotnet run --project tests/Reactor.AppTests.Host --no-build -- --self-test

# --- Merge ---
dotnet-coverage merge unit.cobertura.xml selftest.cobertura.xml \
  --output merged.cobertura.xml --output-format cobertura
```

Replace `$(RuntimeIdentifier)` with `ARM64` or `x64`, or omit the platform segment if you used the default platform from `Directory.Build.props`. The `coverage.settings.xml` file in the repo root controls which modules are included and excludes generated code (`obj/`, `*.g.cs`) and test-host scaffolding exercised only by the Appium/E2E runner.

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

---

## Hot reload

Reactor supports .NET Hot Reload via `HotReloadService.cs`. When you edit code in Visual Studio and save, the framework re-renders with your changes while preserving hook state. No special setup needed — it hooks into the standard `MetadataUpdateHandler` mechanism.
