# 018 — Namespace Rename: Duct → Microsoft.UI.Reactor

**Status:** Draft  
**Date:** 2026-04-16

## Overview

Rename the root namespace from `Duct` to `Microsoft.UI.Reactor` and eliminate
every occurrence of the word "Duct" from the codebase. In the same pass, merge
the DuctD3 charting library into the main assembly.

In documentation, the first mention uses the full name **Microsoft.UI.Reactor**;
subsequent references use **Reactor** for brevity.

The CLI tool is renamed to **mur** (Microsoft.UI.Reactor).

---

## 1. Namespace Mappings

| Old namespace | New namespace |
|---|---|
| `Duct` | `Microsoft.UI.Reactor` |
| `Duct.Animation` | `Microsoft.UI.Reactor.Animation` |
| `Duct.Controls.AutoSuggest` | `Microsoft.UI.Reactor.Controls` (flattened — see Section 9a) |
| `Duct.Controls.Formatting` | `Microsoft.UI.Reactor.Controls` (flattened) |
| `Duct.Controls.MaskedTextBox` | `Microsoft.UI.Reactor.Controls` (flattened) |
| `Duct.Core` | `Microsoft.UI.Reactor.Core` |
| `Duct.Core.Localization` | `Microsoft.UI.Reactor.Localization` (promoted — WinUI keeps functional areas at top level) |
| `Duct.Core.Navigation` | `Microsoft.UI.Reactor.Navigation` (promoted — matches `Microsoft.UI.Xaml.Navigation`) |
| `Duct.D3` | `Microsoft.UI.Reactor.Charting.D3` (D3 port — see Section 9b) |
| `Duct.D3.Charts` | `Microsoft.UI.Reactor.Charting` (controls & DSL — see Section 9b) |
| *(new)* | `Microsoft.UI.Reactor.Hosting` (ReactorHost, ReactorHostControl, PageHelper — matches `Microsoft.UI.Xaml.Hosting`) |
| `Duct.Data` | `Microsoft.UI.Reactor.Data` |
| `Duct.Data.Providers` | `Microsoft.UI.Reactor.Data.Providers` |
| `Duct.DataGrid` | `Microsoft.UI.Reactor.Controls` (flattened — see Section 9a) |
| `Duct.Elements` | `Microsoft.UI.Reactor.Elements` |
| `Duct.Flex` | `Microsoft.UI.Reactor.Layout` (merged — see Section 10) |
| `Duct.Hooks` | `Microsoft.UI.Reactor.Hooks` |
| `Duct.Layout` | `Microsoft.UI.Reactor.Layout` (Yoga types made internal — see Section 10) |
| `Duct.Localization.Generator` | `Microsoft.UI.Reactor.Localization.Generator` |
| `Duct.Markdown` | `Microsoft.UI.Reactor.Markdown` |
| `Duct.Monaco` | `Microsoft.UI.Reactor.Monaco` |
| `Duct.PropertyGrid` | `Microsoft.UI.Reactor.Controls` (flattened — see Section 9a) |
| `Duct.Validation` | `Microsoft.UI.Reactor.Controls.Validation` (under Controls — WPF pattern) |
| `Duct.Validation.Validators` | `Microsoft.UI.Reactor.Controls.Validation` (flattened into parent) |
| `Duct.Virtualization` | `Microsoft.UI.Reactor.Controls` (flattened — see Section 9a) |
| `Duct.Analyzers` | `Microsoft.UI.Reactor.Analyzers` |
| `Duct.Cli` | `Microsoft.UI.Reactor.Cli` |
| `Duct.Cli.Docs` | `Microsoft.UI.Reactor.Cli.Docs` |
| `Duct.Cli.Loc` | `Microsoft.UI.Reactor.Cli.Loc` |
| `Duct.Accessibility` | `Microsoft.UI.Reactor.Accessibility` |
| `Duct.Interop.WinForms` | `Microsoft.UI.Reactor.Interop.WinForms` |

### Test / infrastructure namespaces

| Old | New |
|---|---|
| `Duct.Tests` | `Microsoft.UI.Reactor.Tests` |
| `Duct.Tests.*` | `Microsoft.UI.Reactor.Tests.*` |
| `Duct.D3.Tests` | `Microsoft.UI.Reactor.Charting.D3.Tests` |
| `Duct.AppTests.*` | `Microsoft.UI.Reactor.AppTests.*` |
| `CmdPerf.Duct` | `CmdPerf.Reactor` |
| `StressPerf.Duct` | `StressPerf.Reactor` |
| `StressPerf.DuctGrid` | `StressPerf.ReactorGrid` |
| `Duct.WinFormsTests.Host` | `Reactor.WinFormsTests.Host` |
| `PerfBench.*.Duct` (all variants) | `PerfBench.*.Reactor` |

### Sample app namespaces

| Old | New |
|---|---|
| `DuctFiles` / `DuctFiles.*` | `ReactorFiles` / `ReactorFiles.*` |
| `DuctOutlook` / `DuctOutlook.*` | `ReactorOutlook` / `ReactorOutlook.*` |
| `DuctRegedit` / `DuctRegedit.*` | `ReactorRegedit` / `ReactorRegedit.*` |
| `DuctD3.Gallery` | `ReactorCharting.Gallery` |
| `DuctHostControlDemo` | `ReactorHostControlDemo` |
| `WinUIGalleryDuct` | `WinUIGalleryReactor` |
| `Duct.TestApp` | `Reactor.TestApp` |

---

## 2. Type Renames

### Public API types (prefix dropped — the namespace provides disambiguation)

| Old | New | Notes |
|---|---|---|
| `DuctCommand` | `Command` | Record in `Microsoft.UI.Reactor.Core` |
| `DuctCommand<T>` | `Command<T>` | Record in `Microsoft.UI.Reactor.Core` |
| `DuctContext<T>` | `Context<T>` | Sealed class in `Microsoft.UI.Reactor.Core` |
| `DuctContextBase` | `ContextBase` | Abstract class in `Microsoft.UI.Reactor.Core` |
| `DuctElementFactory<T>` | `ElementFactory<T>` | Sealed class in `Microsoft.UI.Reactor.Core` |
| `DuctPageHelper` | `PageHelper` | Static helper in `Microsoft.UI.Reactor.Hosting` |

> **Logger types removed:** `IDuctLogger`, `DuctLogLevel`, `DebugDuctLogger`,
> and `NullDuctLogger` are deleted — replaced by `Microsoft.Extensions.Logging`
> (see Section 2a).

### Public API types (keep prefix — protect generic names)

| Old | New | Notes |
|---|---|---|
| `DuctApp` | `ReactorApp` | Static entry point — stays in root `Microsoft.UI.Reactor` |
| `DuctApplication` | `ReactorApplication` | WinUI Application subclass — stays in root (like `Microsoft.UI.Xaml.Application`) |
| `DuctHost` | `ReactorHost` | Core render host — moves to `Microsoft.UI.Reactor.Hosting` |
| `DuctHostControl` | `ReactorHostControl` | WinUI ContentControl — moves to `Microsoft.UI.Reactor.Hosting` |

### Internal types

| Old | New |
|---|---|
| `DuctAppOptions` | `ReactorAppOptions` |
| `DuctFilesApp` | `ReactorFilesApp` |
| `DuctFilesEvents` | `ReactorFilesEvents` |
| `DuctComponentTypeConverter` | `ReactorComponentTypeConverter` |

### Test fixture classes

| Old | New |
|---|---|
| `DuctCommandTests` | `CommandTests` |
| `DuctHostControlTests` | `HostControlTests` |
| `DuctHostRenderLoopTests` | `HostRenderLoopTests` |
| `DuctHostControlMountComponent` | `HostControlMountComponent` |
| `DuctHostControlMountFunc` | `HostControlMountFunc` |
| `DuctHostControlFactory` | `HostControlFactory` |
| `DuctHostControlRenderCallback` | `HostControlRenderCallback` |
| `DuctHostRenderStats` | `HostRenderStats` |
| `DuctHostDispose` | `HostDispose` |
| `DuctPageHelperExercise` | `PageHelperExercise` |
| `TestDuctComponent` | `TestReactorComponent` |
| `SampleDuctComponent` | `SampleReactorComponent` |

### DSL entry point class

| Old | New | Notes |
|---|---|---|
| `UI` (static class in `Duct.Elements.Dsl.cs`) | `Factories` | `using static Microsoft.UI.Reactor.Factories;` |

---

## 2a. Replace Custom Logger with Microsoft.Extensions.Logging

The custom logger (`IDuctLogger`, `DuctLogLevel`, `DebugDuctLogger`,
`NullDuctLogger`) is deleted and replaced with
`Microsoft.Extensions.Logging.ILogger` — the standard .NET logging abstraction.

### Why

- The custom logger is a near-duplicate of `ILogger` with fewer features.
- Only 3 internal consumers: `ReactorHost`, `ReactorHostControl`, `Reconciler`.
- Eliminates 4 public types from the API surface.
- Consumers can wire Reactor logging into whatever system they already use
  (Serilog, NLog, ETW, console, etc.) with zero adapter code.

### New dependency

Add `Microsoft.Extensions.Logging.Abstractions` to `Reactor.csproj`. This is a
~50KB package with no transitive dependencies — it defines `ILogger`,
`ILoggerFactory`, `LogLevel`, `NullLogger`, and the `Log*()` extension methods.

### Migration

| Old | New |
|---|---|
| `IDuctLogger` | `Microsoft.Extensions.Logging.ILogger` |
| `DuctLogLevel.Error` | `Microsoft.Extensions.Logging.LogLevel.Error` |
| `DuctLogLevel.Debug` | `Microsoft.Extensions.Logging.LogLevel.Debug` |
| `DuctLogLevel.Warning` | `Microsoft.Extensions.Logging.LogLevel.Warning` |
| `DuctLogLevel.Info` | `Microsoft.Extensions.Logging.LogLevel.Information` |
| `DuctLogLevel.Trace` | `Microsoft.Extensions.Logging.LogLevel.Trace` |
| `DebugDuctLogger` | *(deleted — use `ILoggerFactory` to create a logger)* |
| `NullDuctLogger.Instance` | `Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance` |

### Call-site changes

```csharp
// Before:
_logger.Log(DuctLogLevel.Error, "Render FAILED", ex);
_logger.Log(DuctLogLevel.Debug, $"Theme changed to {theme}");

// After:
_logger.LogError(ex, "Render FAILED");
_logger.LogDebug("Theme changed to {Theme}", theme);
```

### Constructor changes

```csharp
// Before (ReactorHost):
public ReactorHost(Window window, IDuctLogger? logger = null)
{
    _logger = logger ?? new DebugDuctLogger();
}

// After:
public ReactorHost(Window window, ILogger<ReactorHost>? logger = null)
{
    _logger = logger ?? NullLogger<ReactorHost>.Instance;
}
```

### Files changed

| File | Change |
|---|---|
| `Duct/Core/IDuctLogger.cs` | **Delete entire file** |
| `Duct/Hosting/DuctHost.cs` | Replace `IDuctLogger` field/ctor with `ILogger<ReactorHost>` |
| `Duct/Hosting/DuctHostControl.cs` | Replace `IDuctLogger` field/ctor with `ILogger<ReactorHostControl>` |
| `Duct/Hosting/DuctApp.cs` | Replace `IDuctLogger` field with `ILogger<ReactorApplication>` |
| `Duct/Core/Reconciler.cs` | Replace `IDuctLogger` field/ctor with `ILogger<Reconciler>` |
| `Duct/Duct.csproj` | Add `<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />` |
| `tests/Duct.Tests/DuctHostRenderLoopTests.cs` | Replace `NullDuctLogger` with `NullLogger` |

---

## 3. MSBuild Properties

| Old | New |
|---|---|
| `DuctLocDefaultLocale` | `ReactorLocDefaultLocale` |
| `DuctLocStringsPath` | `ReactorLocStringsPath` |
| `DuctLocMissingKeySeverity` | `ReactorLocMissingKeySeverity` |

These appear in:
- `Duct/Duct.csproj` (property defaults and `AdditionalFiles` glob)
- `Duct.Localization.Generator/LocSourceGenerator.cs` (reads `build_property.*`)
- `tests/Duct.Tests/Localization/LocSourceGeneratorTests.cs` (test data)
- Any consumer .csproj that sets these properties

---

## 4. Analyzer Diagnostic Category

| Old | New |
|---|---|
| `"Duct.Style"` | `"Reactor.Style"` |

Used in: `UseThemeRefAnalyzer.cs`, `UseLightweightStylingAnalyzer.cs`,
`RequestedThemeSetAnalyzer.cs`

---

## 5. Source Generator Attribution

| Old | New |
|---|---|
| `[GeneratedCode("Duct.Localization.Generator", "1.0.0")]` | `[GeneratedCode("Reactor.Localization.Generator", "1.0.0")]` |

---

## 6. String Literals

| Location | Old | New |
|---|---|---|
| `DuctApp.cs` default title | `"Duct App"` | `"Reactor App"` |
| `DuctApp.cs` Run method default | `"Duct App"` | `"Reactor App"` |
| `DuctApp.cs` type filter | `!t.FullName!.StartsWith("Duct.")` | `!t.FullName!.StartsWith("Microsoft.UI.Reactor.")` |
| Test host window title | `"Duct Test Host"` | `"Reactor Test Host"` |
| Self-test window title | `"Duct Self-Test"` | `"Reactor Self-Test"` |
| TestSession.cs process name | `"Duct.AppTests.Host"` | `"Reactor.AppTests.Host"` |
| TestSession.cs / SelfTestBatch.cs | `"Duct.sln"` file probe | `"Reactor.sln"` |
| TestSession.cs exe path | `"Duct.AppTests.Host.exe"` | `"Reactor.AppTests.Host.exe"` |
| TestSession.cs window name | `"Duct Test Host"` | `"Reactor Test Host"` |
| CompileCommand.cs sln probe | `"Duct.sln"` | `"Reactor.sln"` |
| CLI scaffolding (Program.cs) | `"Duct"` / `"Duct.csproj"` in template | `"Reactor"` / `"Reactor.csproj"` |
| readme sample | `"Duct Showcase"` | `"Reactor Showcase"` |
| readme sample | `"Duct Framework"` | `"Reactor Framework"` |
| Duct.TestApp | `"Duct Demo"` | `"Reactor Demo"` |
| CommandingDemo display text | `"DuctCommand"` references in UI text | `"Command"` |
| TreeChartDsl.cs TypeKey | `"DuctD3Force"` | `"ChartingD3Force"` |
| EventSource name | `[EventSource(Name = "DuctFiles")]` | `[EventSource(Name = "ReactorFiles")]` |
| DuctHostControlDemo XAML | `Title="DuctHostControl Demo"` | `Title="ReactorHostControl Demo"` |
| flex-layout sample tags | `"Duct"` in tag list | `"Reactor"` |
| UseAnnounce.cs TypeKey | `"DuctAnnounce"` | `"ReactorAnnounce"` |
| RenderContext.cs debug prefix | `"[Duct] "` in Debug.WriteLine calls | `"[Reactor] "` |
| XamlIslandControl.cs category | `[Category("Duct")]` | `[Category("Reactor")]` |
| XamlIslandControl.cs description | `"The Duct Component type to host. Creates a DuctHostControl…"` | `"The Reactor Component type to host. Creates a ReactorHostControl…"` |
| TestDuctComponent.cs title | `"Duct Island Content"` | `"Reactor Island Content"` |
| TestDuctComponent.cs AutomationIds | `"Duct_Title"`, `"Duct_TextField1"`, `"Duct_Button1"`, `"Duct_TextDisplay"`, `"Duct_CountDisplay"`, `"Duct_TextField2"`, `"Duct_LiveRegion"`, `"Duct_RenderProof"` | `"Reactor_Title"`, `"Reactor_TextField1"`, etc. |
| WinFormsInteropTests.cs | Same AutomationId strings in test assertions | Match test host renames |
| WinFormsOutsideForm.Designer.cs title | `"WinForms hosts Duct"` | `"WinForms hosts Reactor"` |
| WinFormsOutsideForm.Designer.cs text | `"hosting a Duct component tree"` | `"hosting a Reactor component tree"` |
| SampleDuctComponent.cs heading | `"Duct Component (via XAML Island)"` | `"Reactor Component (via XAML Island)"` |
| SampleDuctComponent.cs body | `"This Duct/WinUI component…"` | `"This Reactor/WinUI component…"` |
| GalleryShell.cs title | `"Duct WinUI Gallery"` | `"Reactor WinUI Gallery"` |
| HomePage.cs heading | `"Duct WinUI Gallery"` | `"Reactor WinUI Gallery"` |
| SettingsPage.cs label | `"Duct (declarative C# DSL)"` | `"Reactor (declarative C# DSL)"` |
| TypographyPage.cs body | `"Duct provides shorthand helpers…"` | `"Reactor provides shorthand helpers…"` |
| GAPS.md (Gallery) | ~20 occurrences of `"Duct"` in prose and table | `"Reactor"` throughout |
| a11y-showcase App.cs comment | `"Duct's accessibility tooling"` | `"Reactor's accessibility tooling"` |

---

## 7. Directory & File Renames

### Core framework

| Old path | New path |
|---|---|
| `Duct/` | `Reactor/` |
| `Duct/Duct.csproj` | `Reactor/Reactor.csproj` |
| `Duct/DataGrid/` | `Reactor/Controls/DataGrid/` (see Section 9a) |
| `Duct/PropertyGrid/` | `Reactor/Controls/PropertyGrid/` (see Section 9a) |
| `Duct/Validation/` | `Reactor/Controls/Validation/` (see Section 9a) |
| `Duct/Virtualization/` | `Reactor/Controls/Virtualization/` (see Section 9a) |
| `Duct/Flex/` | *(merged into `Reactor/Layout/`)* |
| `Duct/Accessibility/` | `Reactor/Accessibility/` |
| `Duct/Yoga/` | `Reactor/Layout/` |
| `Duct.Analyzers/` | `Reactor.Analyzers/` |
| `Duct.Interop.WinForms/` | `Reactor.Interop.WinForms/` |
| `Duct.Cli/` | `Reactor.Cli/` |
| `Duct.Localization.Generator/` | `Reactor.Localization.Generator/` |
| `DuctD3/` | *(merged into `Reactor/Charting/` — see Section 9b)* |
| `Duct.sln` | `Reactor.sln` |

### Test projects

| Old path | New path |
|---|---|
| `tests/Duct.Tests/` | `tests/Reactor.Tests/` |
| `tests/Duct.AppTests/` | `tests/Reactor.AppTests/` |
| `tests/Duct.AppTests.Host/` | `tests/Reactor.AppTests.Host/` |
| `tests/DuctD3.Tests/` | `tests/ReactorCharting.Tests/` |
| `tests/cmd_perf/CmdPerf.Duct/` | `tests/cmd_perf/CmdPerf.Reactor/` |
| `tests/stress_perf/StressPerf.Duct/` | `tests/stress_perf/StressPerf.Reactor/` |
| `tests/stress_perf/StressPerf.DuctGrid/` | `tests/stress_perf/StressPerf.ReactorGrid/` |
| `tests/Duct.WinFormsTests.Host/` | `tests/Reactor.WinFormsTests.Host/` |

### Perf bench projects (`tests/perf_bench/`)

Every `*.Duct/` directory becomes `*.Reactor/`:

- `Allocation.Duct/` → `Allocation.Reactor/`
- `DeferredMount.Duct/` → `DeferredMount.Reactor/`
- `DirtyTracking.Duct/` → `DirtyTracking.Reactor/`
- `InteractivePool.Duct/` → `InteractivePool.Reactor/`
- `Journal.Duct/` → `Journal.Reactor/`
- `OffThread.Duct/` → `OffThread.Reactor/`
- `Priorities.Duct/` → `Priorities.Reactor/`
- `PropertyDiff.Duct/` → `PropertyDiff.Reactor/`
- `StructuralSharing.Duct/` → `StructuralSharing.Reactor/`
- `TimeSlice.Duct/` → `TimeSlice.Reactor/`

### Sample projects

| Old path | New path |
|---|---|
| `samples/apps/ductfiles/` | `samples/apps/reactorfiles/` |
| `samples/apps/outlook/DuctOutlook.csproj` | `samples/apps/outlook/ReactorOutlook.csproj` |
| `samples/apps/regedit/DuctRegedit.csproj` | `samples/apps/regedit/ReactorRegedit.csproj` |
| `samples/Duct.TestApp/` | `samples/Reactor.TestApp/` |
| `samples/DuctHostControlDemo/` | `samples/ReactorHostControlDemo/` |
| `samples/DuctD3.Gallery/` | `samples/ReactorCharting.Gallery/` |
| `samples/DuctD3.Sample/` | `samples/ReactorCharting.Sample/` |
| `samples/WinUI-Gallery-Duct/` | `samples/ReactorGallery/` |

### .csproj file renames (within directories)

Each project's `.csproj` filename changes to match, e.g.:
- `Duct.Tests.csproj` → `Reactor.Tests.csproj`
- `DuctFiles.csproj` → `ReactorFiles.csproj`
- `DuctD3.Tests.csproj` → `ReactorD3.Tests.csproj`
- `Duct.Interop.WinForms.csproj` → `Reactor.Interop.WinForms.csproj`
- `Duct.WinFormsTests.Host.csproj` → `Reactor.WinFormsTests.Host.csproj`
- `WinUI-Gallery-Duct.csproj` → `ReactorGallery.csproj`
- etc.

---

## 8. Assembly Names

| Old | New |
|---|---|
| `duct-cli` | `mur` |
| `Duct.Analyzers` (PackageId) | `Reactor.Analyzers` |

---

## 9a. Controls Namespace Consolidation

Six separate namespaces flatten into one `Microsoft.UI.Reactor.Controls`,
matching WinUI's pattern where `Microsoft.UI.Xaml.Controls` is a single flat
namespace containing all controls (Button, TextBox, NavigationView, TreeView,
ItemsRepeater, etc.).

### Type counts per source namespace

| Old namespace | Public types | Internal | What moves |
|---|---|---|---|
| `Duct.Controls.AutoSuggest` | 4 | 0 | `SearchState`, `AutoSuggestElement<T>`, `AutoSuggestDsl`, `SearchManager<T>` |
| `Duct.Controls.Formatting` | 3 | 10 | `FormatResult`, `InputFormatter`, `FormatterPipeline` (+ 10 internal formatters) |
| `Duct.Controls.MaskedTextBox` | 4 | 2 | `MaskedTextFieldElement`, `MaskedTextFieldDsl`, `MaskEngine`, `MaskPreset` |
| `Duct.DataGrid` | 11 | 3 | `DataGridComponent<T>`, `DataGridElement<T>`, `DataGridState<T>`, `Factories`, `Factories`, `ColumnBuilder<T>`, `SelectionMode`, `EditMode`, `CellContext<T>`, `RowContext<T>`, `HeaderContext` |
| `Duct.PropertyGrid` | 18 | 3 | `PropertyGridComponent`, `PropertyGridElement`, `Factories`, `PropertyGridDefaults`, 7 attributes, 4 delegates, `TypeMetadata`, `ArrayTypeMetadata`, `TypeRegistry`, `EditorTier`, `ReflectionTypeMetadataProvider` |
| `Duct.Virtualization` | 4 | 0 | `VirtualListComponent`, `Factories`, `VirtualListElement`, `VirtualListRef` |
| **Total in Controls** | **44** | **18** | |

44 public types in `Controls` is well within WinUI norms
(`Microsoft.UI.Xaml.Controls` has 200+).

**Validation** goes under `Controls.Validation` (WPF-style sub-namespace):

| Old namespace | Public types | Internal | What moves |
|---|---|---|---|
| `Duct.Validation` | 23 | 0 | `ValidationContext`, `ValidationReconciler`, `ValidationMessage`, `Severity`, `ValidationRuleElement`, `ValidationRuleDsl`, `ValidationAttached`, `ValidateExtensions`, `ValidationContexts`, `ValidationContextHookExtensions`, `ValidationContextComponentExtensions`, `FormFieldElement`, `FormFieldDsl`, `FormFieldHelpers`, `ErrorStyling`, `ErrorStylingExtensions`, `ErrorStylingAttached`, `ShowWhen`, `ValidationVisualizerElement`, `ValidationVisualizerDsl`, `VisualizerStyle`, `ShowErrorsExtension`, `ErrorBubbling` |
| `Duct.Validation.Validators` | 3 | 10 | `IValidator`, `IAsyncValidator`, `Validate` (factory — flattened into `Controls.Validation`) |
| **Total in Controls.Validation** | **26** | **10** | |

### Directory layout after merge

```
Reactor/Controls/
  AutoSuggest/           ← files stay grouped by feature subdirectory
  Formatting/
  MaskedTextBox/
  DataGrid/              ← moved from Duct/DataGrid/
  PropertyGrid/          ← moved from Duct/PropertyGrid/
  Validation/            ← moved from Duct/Validation/ (namespace: Controls.Validation)
  Virtualization/        ← moved from Duct/Virtualization/
```

Files keep their subdirectories for organization — only the namespace changes.
All files in `Controls/` declare `namespace Microsoft.UI.Reactor.Controls;`
except `Validation/` which declares `namespace Microsoft.UI.Reactor.Controls.Validation;`.

### Not moved into Controls

| Namespace | Reason |
|---|---|
| `Duct.Markdown` → `Microsoft.UI.Reactor.Markdown` | Parser/renderer library (~24 public Md4c types), not a single control |
| `Duct.Monaco` → `Microsoft.UI.Reactor.Monaco` | WebView2-based editor integration, separate concern |

---

## 9b. DuctD3 → Merge into Main Assembly (Charting namespace)

The `DuctD3/` project (separate assembly, ~108 types) merges into the main
`Reactor/` assembly. The two old namespaces are replaced by a new hierarchy:

- **`Microsoft.UI.Reactor.Charting`** — high-level chart controls and DSL that
  most developers use directly.
- **`Microsoft.UI.Reactor.Charting.D3`** — the D3 algorithm port (scales, shapes,
  layouts, color, interpolation, etc.) for custom/advanced charting.

### Directory layout after merge

```
Reactor/Charting/
  Charts.cs               ← Charts/Charts.cs
  TreeChartDsl.cs            ← Charts/TreeChartDsl.cs
  D3Charts.cs                  ← Charts/D3Charts.cs
  PathDataParser.cs          ← Charts/PathDataParser.cs
  D3/
    Array/                   ← from DuctD3/Array/
    Scale/                   ← from DuctD3/Scale/
    Shape/                   ← from DuctD3/Shape/
    Layout/                  ← from DuctD3/Layout/
    Color/                   ← from DuctD3/Color/
    Interpolate/             ← from DuctD3/Interpolate/
    Path/                    ← from DuctD3/Path/
    Ease/                    ← from DuctD3/Ease/
    Random/                  ← from DuctD3/Random/
    Format/                  ← from DuctD3/Format/
    Polygon/                 ← from DuctD3/Polygon/
    Contour/                 ← from DuctD3/Contour/
    Voronoi/                 ← from DuctD3/Voronoi/
    Chord/                   ← from DuctD3/Chord/
```

### Class-by-class mapping: `Microsoft.UI.Reactor.Charting`

These are the controls and DSL factories that most developers import.

| Type | Kind | Description |
|---|---|---|
| `Charts` | `public static partial class` | Factory methods: `LineChart()`, `BarChart()`, `AreaChart()`, `PieChart()`, `TreeChart()`, `ForceGraph()` |
| `ChartType` | `public enum` | Line, Bar, Area |
| `ChartElement<T>` | `public sealed class` | Line/bar/area chart builder (data, axes, grid, colors, margins) |
| `ChartHandle<T>` | `public sealed class` | Handle from OnReady; exposes underlying Canvas |
| `PieChartElement<T>` | `public sealed class` | Pie/donut chart builder |
| `PieChartHandle<T>` | `public sealed class` | Pie chart handle |
| `TreeChartElement<T>` | `public sealed class` | Tree diagram builder |
| `TreeChartHandle` | `public sealed class` | Tree chart handle |
| `ForceGraphElement` | `public sealed class` | Force-directed graph renderer |
| `ForceGraphHandle` | `public sealed class` | Force graph handle (drag/animation) |
| `D3` | `public static class` | Declarative drawing primitives: rect, circle, line, path, text, axes, grid, legend |
| `PathDataParser` | `public static class` | SVG path data → WinUI PathGeometry |

### Class-by-class mapping: `Microsoft.UI.Reactor.Charting.D3`

Everything below is the D3 algorithm port. Grouped by subdirectory.

#### Array utilities

| Type | Kind | Description |
|---|---|---|
| `BinGenerator<T>` | `public sealed class` | Histogram binning (d3.bin) |
| `Bin<T>` | `public sealed class` | Individual histogram bin |
| `BinGenerator` | `public static class` | Factory methods |
| `D3Bisect` | `public static class` | Binary search on sorted arrays |
| `D3Extent` | `public static class` | Min/max from collections |
| `D3Group` | `public static class` | Group and rollup by key |
| `D3Range` | `public static class` | Evenly-spaced numeric ranges |
| `D3Statistics` | `public static class` | Mean, median, quantile, variance, deviation |
| `D3Ticks` | `public static class` | Human-readable tick generation |

#### Scales

| Type | Kind | Description |
|---|---|---|
| `LinearScale` | `public sealed class` | Continuous linear mapping |
| `LogScale` | `public sealed class` | Logarithmic mapping |
| `PowScale` | `public sealed class` | Power/exponent mapping |
| `BandScale<T>` | `public sealed class` | Categorical band mapping |
| `BandScale` | `public static class` | Factory |
| `PointScale<T>` | `public sealed class` | Categorical point mapping |
| `PointScale` | `public static class` | Factory |
| `OrdinalScale<T>` | `public sealed class` | Discrete-to-discrete mapping |
| `OrdinalScale` | `public static class` | Factory |
| `QuantizeScale` | `public sealed class` | Continuous → discrete bins |
| `QuantileScale` | `public sealed class` | Equal-probability bins |
| `ThresholdScale` | `public sealed class` | Threshold-based binning |

#### Shape generators

| Type | Kind | Description |
|---|---|---|
| `ArcGenerator` | `public sealed class` | Arc/pie-slice paths |
| `LineGenerator<T>` | `public sealed class` | Line path from data points |
| `LineGenerator` | `public static class` | Factory |
| `AreaGenerator<T>` | `public sealed class` | Filled area between baselines |
| `AreaGenerator` | `public static class` | Factory |
| `PieGenerator<T>` | `public sealed class` | Compute arc data from values |
| `PieGenerator` | `public static class` | Factory |
| `StackGenerator<T>` | `public sealed class` | Stacked series offsets |
| `StackPoint` | `public record struct` | Stacked data point (Y0/Y1) |
| `StackSeries` | `public sealed class` | Stacked series container |
| `StackGenerator` | `public static class` | Factory |
| `RadialLineGenerator<T>` | `public sealed class` | Polar line paths |
| `RadialLineGenerator` | `public static class` | Factory |
| `RadialAreaGenerator<T>` | `public sealed class` | Polar area paths |
| `RadialAreaGenerator` | `public static class` | Factory |
| `RadialLinkGenerator<T>` | `public sealed class` | Polar tree link paths |
| `RadialLinkGenerator` | `public static class` | Factory |
| `ISymbolType` | `public interface` | Symbol shape contract |
| `SymbolGenerator<T>` | `public sealed class` | Data point markers |
| `SymbolGenerator` | `public static class` | Factory |
| `D3Symbol` | `public static class` | Predefined symbols (circle, cross, diamond, square, star, triangle, wye) |
| `ICurve` | `public interface` | Curve interpolation contract |
| `CurveFactory` | `public delegate` | Curve factory delegate |
| `D3Curve` | `public static class` | Curve factories (linear, step, basis, cardinal, catmull-rom, monotone-x) |

#### Layout algorithms

| Type | Kind | Description |
|---|---|---|
| `TreeNode<T>` | `public sealed class` | Tree hierarchy node |
| `TreeLayout<T>` | `public sealed class` | Reingold-Tilford tree positioning |
| `TreeLayout` | `public static class` | Factory |
| `ClusterLayout<T>` | `public sealed class` | Dendrogram layout |
| `ClusterLayout` | `public static class` | Factory |
| `TreemapLayout<T>` | `public sealed class` | Rectangular partitioning |
| `TreemapNode<T>` | `public sealed class` | Treemap node with bounds |
| `TreemapTiling` | `public enum` | Squarified, Binary, Slice, Dice, SliceDice |
| `TreemapLayout` | `public static class` | Factory |
| `PackLayout<T>` | `public sealed class` | Circle packing |
| `PackNode<T>` | `public sealed class` | Pack node with radius |
| `PackLayout` | `public static class` | Factory |
| `PartitionLayout<T>` | `public sealed class` | Sunburst/icicle layout |
| `PartitionNode<T>` | `public sealed class` | Partition node with bounds |
| `PartitionLayout` | `public static class` | Factory |
| `Stratify<T>` | `public sealed class` | Flat data → hierarchy |
| `Stratify` | `public static class` | Factory |
| `SankeyLayout` | `public sealed class` | Sankey flow diagram |
| `SankeyGraph` | `public sealed class` | Sankey result container |
| `SankeyNode` | `public sealed class` | Flow node with bounds |
| `SankeyLink` | `public sealed class` | Flow connection |
| `SankeyNodeAlign` | `public enum` | Top, Middle, Bottom, Justify |
| `ForceNode` | `public sealed class` | Simulation particle |
| `ForceLink` | `public record struct` | Link constraint |
| `ForceSimulation` | `public sealed class` | N-body force simulation |

#### Color, interpolation, path

| Type | Kind | Description |
|---|---|---|
| `D3Color` | `public readonly struct` | Color parsing, manipulation, palettes |
| `D3Interpolate` | `public static class` | Value interpolation |
| `D3InterpolateColor` | `public static class` | Color-space interpolation (RGB, HSL, Lab, LCh) |
| `PathBuilder` | `public sealed class` | SVG path command builder |

#### Utilities

| Type | Kind | Description |
|---|---|---|
| `D3Ease` | `public static class` | Easing functions (Quad, Cubic, Sine, Expo, Elastic, Bounce, etc.) |
| `D3Random` | `public static class` | Seedable PRNG |
| `D3Format` | `public static class` | D3 number formatting |
| `D3Polygon` | `public static class` | Polygon hull, centroid, area, contains |
| `ContourGenerator` | `public sealed class` | Marching-squares contours |
| `DensityContourGenerator` | `public sealed class` | KDE contours from point clouds |
| `ContourMultiPolygon` | `public sealed class` | Contour result |
| `Delaunay` | `public sealed class` | Delaunay triangulation |
| `Voronoi` | `public sealed class` | Voronoi diagram |
| `ChordLayout` | `public sealed class` | Chord diagram layout |
| `RibbonGenerator` | `public sealed class` | Chord ribbon paths |
| `ChordData` | `public record struct` | Chord result |
| `ChordGroup` | `public record struct` | Chord group arc |
| `ChordArc` | `public record struct` | Arc segment |
| `ChordEnd` | `public record struct` | Chord endpoint |

### Merge steps

1. Move `DuctD3/Charts/*.cs` into `Reactor/Charting/`.
2. Move all other `DuctD3/` subdirectories into `Reactor/Charting/D3/`.
3. Update namespaces: `Duct.D3.Charts` → `Microsoft.UI.Reactor.Charting`,
   `Duct.D3` → `Microsoft.UI.Reactor.Charting.D3`.
4. Remove `DuctD3/DuctD3.csproj`.
5. Remove `<ProjectReference>` to DuctD3 from all consumers — now part of
   the main library.
6. Update `Reactor.sln` to remove the DuctD3 project entry.
7. Update `tests/ReactorD3.Tests/` to reference `Reactor` instead of `DuctD3`.
8. Update `using static Duct.D3.Charts.Charts` →
   `using static Microsoft.UI.Reactor.Charting.Charts` everywhere.

---

## 10. Flex / Layout Namespace Merge & Yoga Internalization

The current codebase has two namespaces for layout:

- **`Duct.Flex`** (directory `Duct/Flex/`, 2 files) — public API: `FlexPanel`,
  `FlexEnums` (enums: `FlexAlign`, `FlexDirection`, `FlexJustify`,
  `FlexLayoutDirection`, `FlexPositionType`, `FlexWrap`).
- **`Duct.Layout`** (directory `Duct/Yoga/`, 9 files) — Yoga layout engine. Has
  types currently marked `public` (`YogaNode`, `YogaConfig`, `YogaValue`,
  13 enums, 3 delegates) but **no external consumer actually references them**.
  The only code-level consumers are `FlexPanel.cs` (same assembly) and the
  generated Yoga test suite (covered by `InternalsVisibleTo`).

### Yoga types → all made internal

Since no code outside the main assembly or test project uses the Yoga types,
**every `Yoga*` public type becomes `internal`**. This eliminates the "Yoga"
name from the public API entirely.

| Type | Current visibility | New visibility |
|---|---|---|
| `YogaNode` | `public sealed` | `internal sealed` |
| `YogaConfig` | `public sealed` | `internal sealed` |
| `YogaValue` | `public readonly record struct` | `internal readonly record struct` |
| `YogaSize` | `public struct` | `internal struct` |
| `YogaMeasureFunc` | `public delegate` | `internal delegate` |
| `YogaBaselineFunc` | `public delegate` | `internal delegate` |
| `YogaDirtiedFunc` | `public delegate` | `internal delegate` |
| `YogaBoxSizing` | `public enum` | `internal enum` |
| `YogaDimension` | `public enum` | `internal enum` |
| `YogaDisplay` | `public enum` | `internal enum` |
| `YogaEdge` | `public enum` | `internal enum` |
| `YogaPhysicalEdge` | `public enum` | `internal enum` |
| `YogaErrata` | `public enum` | `internal enum` |
| `YogaExperimentalFeature` | `public enum` | `internal enum` |
| `YogaGutter` | `public enum` | `internal enum` |
| `YogaLogLevel` | `public enum` | `internal enum` |
| `YogaMeasureMode` | `public enum` | `internal enum` |
| `YogaNodeType` | `public enum` | `internal enum` |
| `YogaOverflow` | `public enum` | `internal enum` |
| `YogaUnit` | `public enum` | `internal enum` |

> The test project `Reactor.Tests` already has `InternalsVisibleTo` access, so
> the ~20 generated Yoga test files (`tests/Reactor.Tests/YogaGenerated/`)
> continue to compile. The one sample string reference in `FlexPanelDemo.cs`
> ("powered by the Yoga engine") will be updated to remove the Yoga mention.

### Directory layout after merge

All files move into `Reactor/Layout/`. No sub-namespace needed — the types are
either public (`FlexPanel`, `FlexEnums`) or internal (everything from Yoga).

```
Reactor/Layout/
  FlexPanel.cs              ← from Flex/  (public, namespace: M.UI.Reactor.Layout)
  FlexEnums.cs              ← from Flex/  (public, namespace: M.UI.Reactor.Layout)
  YogaNode.cs               ← from Yoga/  (internal, namespace: M.UI.Reactor.Layout)
  YogaConfig.cs             ← from Yoga/  (internal, namespace: M.UI.Reactor.Layout)
  YogaValue.cs              ← from Yoga/  (internal, namespace: M.UI.Reactor.Layout)
  YogaEnums.cs              ← from Yoga/  (internal, namespace: M.UI.Reactor.Layout)
  YogaStyle.cs              ← from Yoga/  (internal, namespace: M.UI.Reactor.Layout)
  YogaAlgorithm.cs          ← from Yoga/  (internal, namespace: M.UI.Reactor.Layout)
  AlgorithmUtils.cs         ← from Yoga/  (internal, namespace: M.UI.Reactor.Layout)
  LayoutResults.cs          ← from Yoga/  (internal, namespace: M.UI.Reactor.Layout)
  FlexDirectionHelper.cs    ← from Yoga/  (internal, namespace: M.UI.Reactor.Layout)
```

### Impact on consumers

- `using Duct.Flex;` → `using Microsoft.UI.Reactor.Layout;`
- `using Duct.Layout;` → no longer needed (was only for Yoga types, now internal)
- XAML: `xmlns:flex="using:Duct.Flex"` → `xmlns:layout="using:Microsoft.UI.Reactor.Layout"`

### Old directories removed

- `Duct/Flex/` (files move to `Reactor/Layout/`)
- `Duct/Yoga/` (files move to `Reactor/Layout/`)

---

## 11. XAML Namespace References

| Old | New |
|---|---|
| `xmlns:flex="using:Duct.Flex"` | `xmlns:layout="using:Microsoft.UI.Reactor.Layout"` |

Affected files (all in `samples/FlexPanelGallery/Pages/`):
- `AbsolutePositionPage.xaml`
- `AlignItemsPage.xaml`
- `DirectionPage.xaml`
- `GapPage.xaml`
- `GrowShrinkPage.xaml`
- `JustifyContentPage.xaml`
- `NestedFlexPage.xaml`
- `OverviewPage.xaml`
- `WrapPage.xaml`

> **Note:** The XAML alias changes from `flex:` to `layout:` to match the new
> namespace. All `<flex:FlexPanel>` usages become `<layout:FlexPanel>`.

Also: `ReactorHostControlDemo/*.xaml` — x:Class and Title attributes.

---

## 12. InternalsVisibleTo

| Old | New |
|---|---|
| `InternalsVisibleTo: Duct.Tests` (in Duct.csproj) | `InternalsVisibleTo: Reactor.Tests` |
| `InternalsVisibleTo: Duct.Tests` (in Duct.Cli.csproj) | `InternalsVisibleTo: Reactor.Tests` |
| `InternalsVisibleTo: Duct.Tests` (in Duct.Localization.Generator.csproj) | `InternalsVisibleTo: Reactor.Tests` |

---

## 13. VS Code Extension (`vscode-duct/`)

| Item | Old | New |
|---|---|---|
| Directory | `vscode-duct/` | `vscode-reactor/` |
| `package.json` name | `duct-preview` | `reactor-preview` |
| `package.json` displayName | `Duct Preview` | `Reactor Preview` |
| `package.json` publisher | `duct` | `reactor` |
| Command IDs | `duct.preview`, `duct.previewConnect`, `duct.previewStop`, `duct.previewFocus` | `reactor.preview`, `reactor.previewConnect`, `reactor.previewStop`, `reactor.previewFocus` |

---

## 14. Config / Metadata Files

| File | Changes |
|---|---|
| `es-metadata.yml` | `"Duct/"` → `"Reactor/"` in scope includes |
| `reviewer/manifest.json` | All `"Duct*"` paths → `"Reactor*"` paths |
| `selfhost/duct-cli.deps.json` | Rename to `selfhost/mur.deps.json`, update internal refs |

---

## 15. Documentation

All `.md` files in the repo will be updated:

- **First occurrence** in each document: `Microsoft.UI.Reactor` (full name)
- **Subsequent references**: `Reactor`
- `"Duct"` as a standalone word → `"Reactor"`
- `"DuctD3"` → `"Reactor D3"` or `"Reactor.D3"`
- Code samples updated with new namespaces and type names

### Spec file renames (docs/spec/)

Drop `-duct` / `duct` from filenames rather than replacing with `-reactor`:

| Old | New |
|---|---|
| `001-duct-theming-design.md` | `001-theming-design.md` |
| `002-duct-winui3-gap-analysis.md` | `002-winui3-gap-analysis.md` |
| `004-duct-property-grid.md` | `004-property-grid.md` |
| `005-duct-localization-design.md` | `005-localization-design.md` |
| `006-duct-accessibility-design.md` | `006-accessibility-design.md` |
| `007-duct-perf-experiments.md` | `007-perf-experiments.md` |
| `009-duct-state-and-components-design.md` | `009-state-and-components-design.md` |
| `010-duct-source-mapping-design.md` | `010-source-mapping-design.md` |
| `011-duct-navigation-design.md` | `011-navigation-design.md` |
| `012-duct-commanding-design.md` | `012-commanding-design.md` |
| `013-duct-doc-system-design.md` | `013-doc-system-design.md` |
| `014-duct-animation-design.md` | `014-animation-design.md` |
| `015-duct-styling-design.md` | `015-styling-design.md` |
| `016-ductd3-native-chart-migration.md` | `016-native-chart-migration.md` |
| `017-duct-data-system-design.md` | `017-data-system-design.md` |
| `archived/ductcpp-*.md` | `archived/cpp-*.md` |

### Other docs

| File | Action |
|---|---|
| `README.md` | Full rewrite of framework name references |
| `CONTRIBUTING.md` | Update paths, project names, build instructions |
| `SKILL.md` | Update all code references |
| `docs/duct-critical-review.md` | Rename file → `docs/critical-review.md`, update content |
| `docs/flux-ui-analysis.md` | Update Duct references |
| `docs/winui3-integration-proposals.md` | Update references |
| `docs/compare/*.md` | Update references |
| `docs/output/*.md` | Update references |
| `docs/tasks/*.md` | Update references |
| `docs/investigation/winforms-interop.md` | Update references (~27 occurrences) |
| `design-skill/SKILL.md` and `design-skill/docs/*.md` | Update references (~39 occurrences across 7 files) |
| `samples/WinFormsInterop/README.md` | Update references (~24 occurrences) |
| `docs/worksummary/work-summary.md` | Update references (~13 occurrences) |
| `docs/worksummary/*.svg` | Update embedded "Duct" text labels (~7 occurrences across 2 SVGs) |
| `reviewer/reports/fix-list.md` | Update references |
| `docs/reactor-pitch.md` | Already uses "Reactor" naming — verify no stale "Duct" refs |
| `docs/apps/data-system/App.cs` | Update `using Duct` / `using static Duct.UI` / ProjectReference paths |
| `docs/apps/winforms-interop/App.cs` | Update `using Duct` / `using static Duct.UI` / ProjectReference paths |
| `samples/WinUI-Gallery-Duct/GAPS.md` | Update ~20 occurrences of "Duct" in prose and table |
| `Duct/Docs/Architecture.md` | Move to `Reactor/Docs/`, update content |
| `Duct/Docs/GettingStarted.md` | Move to `Reactor/Docs/`, update content |

---

## 16. Execution Order

The rename should be executed in this order to keep the build green at each step:

### Phase 1: Source code renames (no file/directory moves)

1. **Namespaces** — Find-replace all `namespace Duct` → `namespace Microsoft.UI.Reactor` and all `using Duct` → `using Microsoft.UI.Reactor` across all `.cs` files.
2. **`using static Duct.UI`** → `using static Microsoft.UI.Reactor.Factories` and rename the `UI` class to `Factories` in `Dsl.cs`.
3. **Type renames** — Apply all type renames from Section 2.
4. **MSBuild properties** — Rename `DuctLoc*` → `ReactorLoc*` in `.csproj` and generator.
5. **String literals** — Update all string literals from Section 6.
6. **Analyzer category** — `"Duct.Style"` → `"Reactor.Style"`.
7. **Generator attribution** — Update `GeneratedCode` attribute string.
8. **XAML namespaces** — Update `xmlns` references.
9. **InternalsVisibleTo** — Update assembly friend names.
10. **RootNamespace / AssemblyName** — Update all `.csproj` files (including
    `WinUI-Gallery-Duct.csproj` RootNamespace `WinUIGalleryDuct` → `WinUIGalleryReactor`,
    and `docs/apps/` project ProjectReference paths).
11. **Verify build** — `dotnet build Duct.sln` (still old .sln name).

### Phase 2: Assembly merges + namespace consolidation

After Phase 1, namespaces are already renamed but files are in old directories.

**DuctD3 → Charting merge:**
1. Move `DuctD3/Charts/*.cs` into `Duct/Charting/`.
2. Move all other `DuctD3/` subdirectories into `Duct/Charting/D3/`.
3. Update namespaces: `Microsoft.UI.Reactor.D3.Charts` → `Microsoft.UI.Reactor.Charting`,
   `Microsoft.UI.Reactor.D3` → `Microsoft.UI.Reactor.Charting.D3`.
4. Remove `DuctD3/DuctD3.csproj`.
5. Remove `ProjectReference` to DuctD3 from all consumers.

**Controls consolidation:**
6. Move `Duct/DataGrid/`, `Duct/PropertyGrid/`, `Duct/Virtualization/` into
   `Duct/Controls/`.
7. Move `Duct/Validation/` into `Duct/Controls/Validation/`.
8. Flatten Controls sub-namespaces to `namespace Microsoft.UI.Reactor.Controls`.
   Validation gets `namespace Microsoft.UI.Reactor.Controls.Validation`.

**Hosting namespace split:**
9. Update `ReactorHost`, `ReactorHostControl`, `PageHelper`, `RenderStats`,
   `HotReloadService`, `PreviewCaptureServer`, `XamlInterop` to
   `namespace Microsoft.UI.Reactor.Hosting`. `ReactorApp` and
   `ReactorApplication` stay in root `Microsoft.UI.Reactor`.

**Flex/Layout merge:**
10. Move `Duct/Flex/*.cs` into `Duct/Yoga/`.
11. Remove `Duct/Flex/` directory.
12. Change all `Yoga*` public types to `internal`.
13. Update `namespace Microsoft.UI.Reactor.Flex` → `namespace Microsoft.UI.Reactor.Layout`.
14. Update XAML `xmlns:flex=…` → `xmlns:layout="using:Microsoft.UI.Reactor.Layout"`
    and all `flex:` element prefixes → `layout:`.

**Navigation / Localization promotion:**
15. Update `namespace Microsoft.UI.Reactor.Core.Navigation` →
    `namespace Microsoft.UI.Reactor.Navigation`.
16. Update `namespace Microsoft.UI.Reactor.Core.Localization` →
    `namespace Microsoft.UI.Reactor.Localization`.

**Finalize:**
17. Update solution file.
18. **Verify build.**

### Phase 3: File and directory renames

1. Rename directories bottom-up (deepest first to avoid path conflicts).
2. Rename `.csproj` files within directories.
3. Rename `.sln` file.
4. Update all `<ProjectReference>` paths in `.csproj` files.
5. Update solution file project paths.
6. **Verify build.**

### Phase 4: Documentation & metadata

1. Rename spec files.
2. Find-replace "Duct" → "Reactor" (with case sensitivity) across all `.md` files,
   including `samples/ReactorGallery/GAPS.md` (~20 occurrences).
3. Manual review pass: ensure first occurrence in each doc is "Microsoft.UI.Reactor".
4. Update `docs/apps/` project files (ProjectReference paths, using statements).
5. Update `reviewer/manifest.json`, `es-metadata.yml`.
6. Update VS Code extension (`vscode-duct/`).
7. Update `selfhost/` artifacts.

### Phase 5: Verification

1. `dotnet build Reactor.sln`
2. `dotnet test` — all unit tests pass.
3. Global text search for any surviving "Duct" references (case-insensitive).
4. Verify sample apps launch.

---

## WinUI Namespace Alignment

The final namespace tree compared to WinUI's pattern:

```
Microsoft.UI.Xaml                          Microsoft.UI.Reactor
Microsoft.UI.Xaml.Controls                 Microsoft.UI.Reactor.Controls
Microsoft.UI.Xaml.Controls.Primitives      Microsoft.UI.Reactor.Controls.Validation
Microsoft.UI.Xaml.Data                     Microsoft.UI.Reactor.Data
                                           Microsoft.UI.Reactor.Data.Providers
Microsoft.UI.Xaml.Hosting                  Microsoft.UI.Reactor.Hosting
Microsoft.UI.Xaml.Media.Animation          Microsoft.UI.Reactor.Animation
Microsoft.UI.Xaml.Navigation               Microsoft.UI.Reactor.Navigation
                                           Microsoft.UI.Reactor.Core (component model — Reactor-specific)
                                           Microsoft.UI.Reactor.Localization
                                           Microsoft.UI.Reactor.Elements (DSL factories)
                                           Microsoft.UI.Reactor.Hooks (React-style — Reactor-specific)
                                           Microsoft.UI.Reactor.Layout (FlexPanel + Yoga)
                                           Microsoft.UI.Reactor.Charting
                                           Microsoft.UI.Reactor.Charting.D3
                                           Microsoft.UI.Reactor.Markdown
                                           Microsoft.UI.Reactor.Monaco
```

---

## Open Questions

1. ~~**`Duct.Hosting` namespace**~~ — **Resolved.** Split: `ReactorApp` and
   `ReactorApplication` stay in root (like `Application` in `Microsoft.UI.Xaml`).
   `ReactorHost`, `ReactorHostControl`, `PageHelper`, `RenderStats`,
   `HotReloadService`, `PreviewCaptureServer`, `XamlInterop` move to
   `Microsoft.UI.Reactor.Hosting` (matches `Microsoft.UI.Xaml.Hosting`).

2. **`ElementFactory<T>` name conflict** — `Microsoft.UI.Xaml.Controls.ElementFactory`
   exists in WinUI. Our `DuctElementFactory<T>` is generic while theirs is not,
   so overload resolution handles it, but the name similarity may confuse
   IntelliSense. Consider keeping as `ReactorElementFactory<T>` instead.

3. **Doc samples referencing `DuctD3` as separate package** — After the merge,
   samples that previously had `<ProjectReference>` to DuctD3 will just reference
   the main library. Doc prose describing it as a "separate charting package"
   needs rewriting.

4. **`vscode-duct` publisher name** — Changing the VS Code marketplace publisher
   from `duct` to `reactor` may require a new marketplace registration. Flag for
   separate handling.

5. **Generated Yoga test files** — The `tests/Reactor.Tests/YogaGenerated/`
   directory contains ~20 auto-generated test files that heavily reference Yoga
   types. After internalization these still compile via `InternalsVisibleTo`, but
   the directory name still says "Yoga". Rename to `LayoutGenerated/`?
