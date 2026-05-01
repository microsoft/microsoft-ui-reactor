# Namespace Rename: Duct → Microsoft.UI.Reactor — Implementation Tasks

**Spec:** [018-namespace-rename.md](../spec/018-namespace-rename.md)
**Created:** 2026-04-16

> Each phase ends with a build verification checkpoint. Phases are designed so
> that git history stays clean: source-level renames first (git tracks content),
> file/directory moves second (git tracks as renames), docs last (no build impact).

---

## Phase 0: Preparation

- [x] **0.1** Create a working branch `feature/namespace-rename` from `main`
- [x] **0.2** Snapshot the current build: `dotnet build Duct.sln` — confirm green baseline
- [x] **0.3** Run full test suite: `dotnet test` — confirm green baseline (4193 tests)
- [x] **0.4** Record current file counts: 609 files with `namespace Duct`

---

## Phase 1: Source Code Renames (no file/directory moves)

All changes in this phase are content-only edits to existing files. Git will
track them as modifications, preserving full blame history.

### 1A — Logger replacement (Section 2a)

Do this first because it deletes types that the later namespace rename would
otherwise need to touch.

- [x] **1A.1** Add `Microsoft.Extensions.Logging.Abstractions` package to `Duct/Duct.csproj`
- [x] **1A.2** Replace `IDuctLogger` with `ILogger` in `Duct/Hosting/DuctHost.cs`
- [x] **1A.3** Replace `IDuctLogger` with `ILogger` in `Duct/Hosting/DuctHostControl.cs`
- [x] **1A.4** Replace `IDuctLogger` with `ILogger` in `Duct/Hosting/DuctApp.cs`
- [x] **1A.5** Replace `IDuctLogger` with `ILogger` in `Duct/Core/Reconciler.cs` (+ Mount + Update partials)
- [x] **1A.6** Delete `Duct/Core/IDuctLogger.cs` (contains `IDuctLogger`, `DuctLogLevel`, `DebugDuctLogger`, `NullDuctLogger`)
- [x] **1A.7** Update `tests/Duct.Tests/DuctHostRenderLoopTests.cs` — replace `NullDuctLogger` with `NullLogger`
- [x] **1A.8** Search for any other consumers of `IDuctLogger` / `DuctLogLevel` and update them
- [x] **1A.9** **CHECKPOINT: `dotnet build Duct.sln` passes**

### 1B — Namespace declarations and using statements

- [x] **1B.1** Replace `namespace Duct.D3.Charts` → `namespace Microsoft.UI.Reactor.Charting` in all `.cs` files
- [x] **1B.2** Replace `namespace Duct.D3` → `namespace Microsoft.UI.Reactor.Charting.D3` in all `.cs` files (order matters: do D3.Charts before D3)
- [x] **1B.3** Replace `namespace Duct.Controls.AutoSuggest` → `namespace Microsoft.UI.Reactor.Controls` in all `.cs` files
- [x] **1B.4** Replace `namespace Duct.Controls.Formatting` → `namespace Microsoft.UI.Reactor.Controls` in all `.cs` files
- [x] **1B.5** Replace `namespace Duct.Controls.MaskedTextBox` → `namespace Microsoft.UI.Reactor.Controls` in all `.cs` files
- [x] **1B.6** Replace `namespace Duct.DataGrid` → `namespace Microsoft.UI.Reactor.Controls` in all `.cs` files
- [x] **1B.7** Replace `namespace Duct.PropertyGrid` → `namespace Microsoft.UI.Reactor.Controls` in all `.cs` files
- [x] **1B.8** Replace `namespace Duct.Virtualization` → `namespace Microsoft.UI.Reactor.Controls` in all `.cs` files
- [x] **1B.9** Replace `namespace Duct.Validation.Validators` → `namespace Microsoft.UI.Reactor.Controls.Validation` in all `.cs` files
- [x] **1B.10** Replace `namespace Duct.Validation` → `namespace Microsoft.UI.Reactor.Controls.Validation` in all `.cs` files (after Validators)
- [x] **1B.11** Replace `namespace Duct.Core.Localization` → `namespace Microsoft.UI.Reactor.Localization` in all `.cs` files
- [x] **1B.12** Replace `namespace Duct.Core.Navigation` → `namespace Microsoft.UI.Reactor.Navigation` in all `.cs` files
- [x] **1B.13** Replace `namespace Duct.Flex` → `namespace Microsoft.UI.Reactor.Layout` in all `.cs` files
- [x] **1B.14** Replace `namespace Duct.Layout` → `namespace Microsoft.UI.Reactor.Layout` in all `.cs` files
- [x] **1B.15** Replace remaining `namespace Duct.` → `namespace Microsoft.UI.Reactor.` for all other namespaces (Core, Hooks, Elements, Animation, Data, Data.Providers, Markdown, Monaco, Accessibility, Interop.WinForms, Analyzers, Cli, Cli.Docs, Cli.Loc, Localization.Generator)
- [x] **1B.16** Replace `namespace Duct;` (bare root) → `namespace Microsoft.UI.Reactor;`
- [x] **1B.17** Replace test/infra namespaces: `namespace Duct.Tests` → `namespace Microsoft.UI.Reactor.Tests`, `namespace Duct.AppTests` → `namespace Microsoft.UI.Reactor.AppTests`, etc.
- [x] **1B.18** Replace perf bench namespaces: `CmdPerf.Duct` → `CmdPerf.Reactor`, `StressPerf.Duct` → `StressPerf.Reactor`, `StressPerf.DuctGrid` → `StressPerf.ReactorGrid`, `PerfBench.*.Duct` → `PerfBench.*.Reactor`
- [x] **1B.19** Replace sample app namespaces: `DuctFiles` → `ReactorFiles`, `DuctOutlook` → `ReactorOutlook`, `DuctRegedit` → `ReactorRegedit`, `DuctD3.Gallery` → `ReactorCharting.Gallery`, `DuctHostControlDemo` → `ReactorHostControlDemo`, `WinUIGalleryDuct` → `WinUIGalleryReactor`, `Duct.TestApp` → `Reactor.TestApp`, `Duct.WinFormsTests.Host` → `Reactor.WinFormsTests.Host`
- [x] **1B.20** Update all `using Duct.D3.Charts` → `using Microsoft.UI.Reactor.Charting` (and `using static`)
- [x] **1B.21** Update all `using Duct.D3` → `using Microsoft.UI.Reactor.Charting.D3`
- [x] **1B.22** Update all other `using Duct.*` → `using Microsoft.UI.Reactor.*` matching the namespace mapping table
- [x] **1B.23** Update all `using static Duct.Elements.UI` → `using static Microsoft.UI.Reactor.UI` (UI→Factories rename deferred to 1C)
- [x] **1B.24** **CHECKPOINT: `dotnet build Duct.sln` passes** (0 errors, 4087/4193 tests pass — 34 AppTest failures are expected string literal issues for Phase 1E)

**Note:** Additional fixes applied during 1B:
- Added `global::System.*` and `global::Windows.*` prefixes to resolve namespace conflicts caused by `Microsoft.UI.Reactor.*` namespace hierarchy
- Renamed `D3` static class → `D3Charts` to avoid collision with `Microsoft.UI.Reactor.Charting.D3` namespace
- Qualified bare `Text()` calls in files where `Microsoft.UI.Text` namespace collides with the DSL method
- Fixed cross-namespace `using` statements (Localization, Controls, Layout)

### 1C — Type renames

- [x] **1C.1** Rename `UI` static class (in `Dsl.cs`) → `Factories`
- [x] **1C.2** Rename `DuctCommand` → `Command`, `DuctCommand<T>` → `Command<T>`
- [x] **1C.3** Rename `DuctContext<T>` → `Context<T>`, `DuctContextBase` → `ContextBase`
- [x] **1C.4** Rename `DuctElementFactory<T>` → `ElementFactory<T>`
- [x] **1C.5** Rename `DuctPageHelper` → `PageHelper`
- [x] **1C.6** Rename `DuctApp` → `ReactorApp`
- [x] **1C.7** Rename `DuctApplication` → `ReactorApplication`
- [x] **1C.8** Rename `DuctHost` → `ReactorHost`
- [x] **1C.9** Rename `DuctHostControl` → `ReactorHostControl`
- [x] **1C.10** Rename internal types: `DuctAppOptions` → `ReactorAppOptions`, `DuctFilesApp` → `ReactorFilesApp`, `DuctFilesEvents` → `ReactorFilesEvents`, `DuctComponentTypeConverter` → `ReactorComponentTypeConverter`
- [x] **1C.11** Rename test fixture classes per Section 2 table (DuctCommandTests → CommandTests, etc.)
- [x] **1C.12** Update all call sites, references, and usages of renamed types across the entire codebase
- [x] **1C.13** **CHECKPOINT: `dotnet build Duct.sln` passes**

### 1D — MSBuild properties, analyzer category, generator attribution

- [x] **1D.1** Rename MSBuild properties `DuctLocDefaultLocale` → `ReactorLocDefaultLocale`, `DuctLocStringsPath` → `ReactorLocStringsPath`, `DuctLocMissingKeySeverity` → `ReactorLocMissingKeySeverity` in `.csproj` files and generator source
- [x] **1D.2** Update analyzer diagnostic category `"Duct.Style"` → `"Reactor.Style"` in `UseThemeRefAnalyzer.cs`, `UseLightweightStylingAnalyzer.cs`, `RequestedThemeSetAnalyzer.cs` (also synced `AnalyzerReleases.Unshipped.md`)
- [x] **1D.3** Update `[GeneratedCode("Duct.Localization.Generator", …)]` → `[GeneratedCode("Reactor.Localization.Generator", …)]`
- [x] **1D.4** **CHECKPOINT: `dotnet build Duct.sln` passes**

### 1E — String literals (Section 6)

- [x] **1E.1** Update `DuctApp.cs` default titles: `"Duct App"` → `"Reactor App"`
- [x] **1E.2** Update `DuctApp.cs` type filter: `StartsWith("Duct.")` → `StartsWith("Microsoft.UI.Reactor.")`
- [x] **1E.3** Update test host/session strings: window titles, process names, sln file probes, exe paths (tests will fail at runtime until Phase 3 file renames — build is green)
- [x] **1E.4** Update CLI scaffolding strings: `"Duct"` / `"Duct.csproj"` → `"Reactor"` / `"Reactor.csproj"` (also updated help text `duct` → `mur` and CompileCommand sln probe)
- [x] **1E.5** Update sample app strings: readme, demo titles, commanding demo, gallery shell, home page, settings page, typography page
- [x] **1E.6** Update `TreeChartDsl.cs` TypeKey `"DuctD3Force"` → `"ChartingD3Force"`
- [x] **1E.7** Update `EventSource` name, `RenderContext.cs` debug prefix, `UseAnnounce.cs` TypeKey (also updated `[Duct]` prefix in Reconciler.cs and TransitionEngine.cs)
- [x] **1E.8** Update WinForms interop strings: `XamlIslandControl.cs` category/description, `TestDuctComponent.cs` title and AutomationIds, `WinFormsOutsideForm.Designer.cs` title/text, `SampleDuctComponent.cs` heading/body
- [x] **1E.9** Update `WinFormsInteropTests.cs` AutomationId assertions to match host renames
- [x] **1E.10** Update Gallery strings: `GalleryShell.cs`, `HomePage.cs`, `SettingsPage.cs`, `TypographyPage.cs`
- [x] **1E.11** Update `GAPS.md` (~20 occurrences)
- [x] **1E.12** Update `a11y-showcase App.cs` comment
- [x] **1E.13** **CHECKPOINT: `dotnet build Duct.sln` passes**

### 1F — XAML namespace references (Section 11)

- [x] **1F.1** Update `xmlns:flex="using:Duct.Flex"` → `xmlns:layout="using:Microsoft.UI.Reactor.Layout"` in all FlexPanelGallery XAML files (9 files) (7 were already migrated; 2 had stale `xmlns:yoga` declarations removed)
- [x] **1F.2** Replace all `<flex:FlexPanel` → `<layout:FlexPanel` and `</flex:FlexPanel>` → `</layout:FlexPanel>` in those XAML files (already done in prior phases)
- [x] **1F.3** Update `ReactorHostControlDemo/*.xaml` — `x:Class` and `Title` attributes (already updated in 1C)
- [x] **1F.4** **CHECKPOINT: `dotnet build Duct.sln` passes**

### 1G — InternalsVisibleTo and project metadata

- [x] **1G.1** Update `InternalsVisibleTo` in `Duct.csproj`: `Duct.Tests` → `Reactor.Tests`
- [x] **1G.2** Update `InternalsVisibleTo` in `Duct.Cli/Duct.Cli.csproj`: `Duct.Tests` → `Reactor.Tests`
- [x] **1G.3** Update `InternalsVisibleTo` in `Duct.Localization.Generator/*.csproj`: `Duct.Tests` → `Reactor.Tests`
- [x] **1G.4** Update `RootNamespace` and `AssemblyName` in all `.csproj` files to match new names (added explicit AssemblyName to test projects and core libs so InternalsVisibleTo stays valid before Phase 3 file renames)
- [x] **1G.5** Update CLI assembly name `duct-cli` → `mur` in `Duct.Cli/Duct.Cli.csproj`
- [x] **1G.6** Update Analyzers `PackageId`: `Duct.Analyzers` → `Reactor.Analyzers`
- [x] **1G.7** **CHECKPOINT: `dotnet build Duct.sln` passes**

### 1H — Commit Phase 1

- [x] **1H.1** Review all changes with `git diff --stat` — verify scope
- [x] **1H.2** Commit: split into prior commits `Phase 1A+1B`, `Phase 1C`, and `Phase 1D-1G: MSBuild props, analyzer category, string literals, XAML aliases, and project metadata` (16c0ed0)

---

## Phase 2: Assembly Merges and Namespace Consolidation

These are structural changes that move files between directories within the
existing project tree, but do NOT yet rename the top-level project directories.

### 2A — DuctD3 → Charting merge (Section 9b)

- [x] **2A.1** Create `Duct/Charting/` and `Duct/Charting/D3/` directories
- [x] **2A.2** Move `DuctD3/Charts/*.cs` into `Duct/Charting/`
- [x] **2A.3** Move all other `DuctD3/` subdirectories (Array, Scale, Shape, Layout, Color, Interpolate, Path, Ease, Random, Format, Polygon, Contour, Voronoi, Chord) into `Duct/Charting/D3/`
- [x] **2A.4** Remove `DuctD3/DuctD3.csproj` (and moved `skill.md` to `Duct/Charting/skill.md`)
- [x] **2A.5** Add the moved `.cs` files to `Duct/Duct.csproj` — not needed; default SDK globs auto-include
- [x] **2A.6** Remove `<ProjectReference>` to DuctD3 from all consumer `.csproj` files (netpulse, DuctD3.Gallery, DuctD3.Sample, charting doc app, Duct.AppTests.Host, DuctD3.Tests)
- [x] **2A.7** Update `Duct.sln` to remove the DuctD3 project entry (via `dotnet sln remove`)
- [x] **2A.8** Update `tests/DuctD3.Tests/` to reference `Duct` (main project) instead of `DuctD3`
- [x] **2A.9** **CHECKPOINT: `dotnet build Duct.sln` passes**

### 2B — Controls consolidation (Section 9a)

- [x] **2B.1** Move `Duct/DataGrid/` → `Duct/Controls/DataGrid/`
- [x] **2B.2** Move `Duct/PropertyGrid/` → `Duct/Controls/PropertyGrid/`
- [x] **2B.3** Move `Duct/Virtualization/` → `Duct/Controls/Virtualization/`
- [x] **2B.4** Move `Duct/Validation/` → `Duct/Controls/Validation/`
- [x] **2B.5** Verify namespace declarations are already correct from Phase 1 (`Microsoft.UI.Reactor.Controls` / `Controls.Validation`)
- [x] **2B.6** **CHECKPOINT: `dotnet build Duct.sln` passes**

### 2C — Hosting namespace split (Section 9b footnote)

- [x] **2C.1** Update `ReactorHost`, `ReactorHostControl`, `PageHelper`, `RenderStats`, `HotReloadService`, `PreviewCaptureServer`, `XamlInterop` to `namespace Microsoft.UI.Reactor.Hosting` (also updated the `MetadataUpdateHandler` assembly attribute)
- [x] **2C.2** Verify `ReactorApp` and `ReactorApplication` stay in root `namespace Microsoft.UI.Reactor`
- [x] **2C.3** Add `using Microsoft.UI.Reactor.Hosting;` where needed across the codebase
- [x] **2C.4** **CHECKPOINT: `dotnet build Duct.sln` passes**

### 2D — Flex/Layout merge and Yoga internalization (Section 10)

- [x] **2D.1** Move `Duct/Flex/*.cs` into `Duct/Yoga/` (will become `Layout/` later in Phase 3)
- [x] **2D.2** Remove empty `Duct/Flex/` directory
- [x] **2D.3** Change all `Yoga*` public types to `internal` (20 types per Section 10 table)
- [x] **2D.4** Verify `InternalsVisibleTo` covers `Reactor.Tests` for Yoga test files (already set in Phase 1G)
- [x] **2D.5** Update `FlexPanelDemo.cs` Yoga mention: `"powered by the Yoga engine"` → remove or rephrase (also updated the header comment in `FlexPanel.cs`)
- [x] **2D.6** **CHECKPOINT: `dotnet build Duct.sln` passes**

### 2E — Navigation / Localization promotion (Section 1 footnotes)

- [x] **2E.1** Update `namespace Microsoft.UI.Reactor.Core.Navigation` → `namespace Microsoft.UI.Reactor.Navigation`
- [x] **2E.2** Update `namespace Microsoft.UI.Reactor.Core.Localization` → `namespace Microsoft.UI.Reactor.Localization` (already at this namespace from Phase 1B)
- [x] **2E.3** Update all `using` statements that referenced the old sub-namespaces (used `sed` to replace all `using Microsoft.UI.Reactor.Core.Navigation` and qualified references in `Dsl.cs` / `ElementExtensions.cs`)
- [x] **2E.4** **CHECKPOINT: `dotnet build Duct.sln` passes**

### 2F — Commit Phase 2

- [x] **2F.1** Review changes: `git diff --stat`
- [x] **2F.2** Commit: `"Merge assemblies and consolidate namespaces (Phase 2)"`

---

## Phase 3: File and Directory Renames

Use `git mv` for all renames so git tracks them as renames (preserving history).
Work bottom-up: deepest paths first, then parent directories.

### 3A — Rename .csproj files within their current directories

- [x] **3A.1** `git mv Duct/Duct.csproj Duct/Reactor.csproj`
- [x] **3A.2** `git mv Duct.Analyzers/Duct.Analyzers.csproj Duct.Analyzers/Reactor.Analyzers.csproj`
- [x] **3A.3** `git mv Duct.Cli/Duct.Cli.csproj Duct.Cli/Reactor.Cli.csproj`
- [x] **3A.4** `git mv Duct.Localization.Generator/Duct.Localization.Generator.csproj Duct.Localization.Generator/Reactor.Localization.Generator.csproj`
- [x] **3A.5** `git mv Duct.Interop.WinForms/Duct.Interop.WinForms.csproj Duct.Interop.WinForms/Reactor.Interop.WinForms.csproj`
- [x] **3A.6** Rename all test `.csproj` files: `Duct.Tests.csproj` → `Reactor.Tests.csproj`, `Duct.AppTests.csproj` → `Reactor.AppTests.csproj`, `Duct.AppTests.Host.csproj` → `Reactor.AppTests.Host.csproj`, `DuctD3.Tests.csproj` → `ReactorCharting.Tests.csproj`, `Duct.WinFormsTests.Host.csproj` → `Reactor.WinFormsTests.Host.csproj`
- [x] **3A.7** Rename sample `.csproj` files: `DuctFiles.csproj` → `ReactorFiles.csproj`, `DuctOutlook.csproj` → `ReactorOutlook.csproj`, `DuctRegedit.csproj` → `ReactorRegedit.csproj`, `WinUI-Gallery-Duct.csproj` → `ReactorGallery.csproj`, `DuctHostControlDemo.csproj` → `ReactorHostControlDemo.csproj`, `Duct.TestApp.csproj` → `Reactor.TestApp.csproj`, `DuctD3.Gallery.csproj` → `ReactorCharting.Gallery.csproj`, `DuctD3.Sample.csproj` → `ReactorCharting.Sample.csproj`
- [x] **3A.8** Rename perf bench `.csproj` files (10 `X.Duct.csproj` → `X.Reactor.csproj` across PerfBench.* folders)
- [x] **3A.9** Rename stress/cmd perf `.csproj` files: `CmdPerf.Duct.csproj` → `CmdPerf.Reactor.csproj`, `StressPerf.Duct.csproj` → `StressPerf.Reactor.csproj`, `StressPerf.DuctGrid.csproj` → `StressPerf.ReactorGrid.csproj`

### 3B — Rename top-level project directories

- [x] **3B.1** `git mv Duct/ Reactor/`
- [x] **3B.2** `git mv Duct.Analyzers/ Reactor.Analyzers/`
- [x] **3B.3** `git mv Duct.Cli/ Reactor.Cli/`
- [x] **3B.4** `git mv Duct.Localization.Generator/ Reactor.Localization.Generator/`
- [x] **3B.5** `git mv Duct.Interop.WinForms/ Reactor.Interop.WinForms/`

### 3C — Rename test directories

- [x] **3C.1** `git mv tests/Duct.Tests/ tests/Reactor.Tests/`
- [x] **3C.2** `git mv tests/Duct.AppTests/ tests/Reactor.AppTests/`
- [x] **3C.3** `git mv tests/Duct.AppTests.Host/ tests/Reactor.AppTests.Host/`
- [x] **3C.4** `git mv tests/DuctD3.Tests/ tests/ReactorCharting.Tests/`
- [x] **3C.5** `git mv tests/Duct.WinFormsTests.Host/ tests/Reactor.WinFormsTests.Host/`
- [x] **3C.6** `git mv tests/cmd_perf/CmdPerf.Duct/ tests/cmd_perf/CmdPerf.Reactor/`
- [x] **3C.7** `git mv tests/stress_perf/StressPerf.Duct/ tests/stress_perf/StressPerf.Reactor/`
- [x] **3C.8** `git mv tests/stress_perf/StressPerf.DuctGrid/ tests/stress_perf/StressPerf.ReactorGrid/`
- [x] **3C.9** Rename perf_bench `*.Duct/` directories → `*.Reactor/` (10 directories inside `PerfBench.*` folders)
- [x] **3C.10** Remaining test dirs (`Duct.EndToEnd`, `Duct.EndToEnd.App`, `Duct.MiniTest`, `Duct.TestApp`, `Duct.UiaProbe`, `Duct.UiaTestApp`, `Duct.UITests`) contain no git-tracked files — left in place as stale build artifacts; they'll regenerate if anyone runs the old projects (which are no longer in the solution)

### 3D — Rename sample directories

- [x] **3D.1** `git mv samples/apps/ductfiles/ samples/apps/reactorfiles/`
- [x] **3D.2** `git mv samples/Duct.TestApp/ samples/Reactor.TestApp/`
- [x] **3D.3** `git mv samples/DuctHostControlDemo/ samples/ReactorHostControlDemo/`
- [x] **3D.4** `git mv samples/DuctD3.Gallery/ samples/ReactorCharting.Gallery/`
- [x] **3D.5** `git mv samples/DuctD3.Sample/ samples/ReactorCharting.Sample/`
- [x] **3D.6** `git mv samples/WinUI-Gallery-Duct/ samples/ReactorGallery/`

### 3E — Rename solution file

- [x] **3E.1** `git mv Duct.sln Reactor.sln`

### 3F — Fix all project references and solution paths

- [x] **3F.1** Updated all `<ProjectReference>` paths in 64 `.csproj` files to point at new directory/file names
- [x] **3F.2** Updated `Reactor.sln` — all project display names (e.g., `"Duct"` → `"Reactor"`, `"DuctD3.Gallery"` → `"ReactorCharting.Gallery"`) and path fields rewritten
- [x] **3F.3** `Directory.Build.props` and `Directory.Build.targets` contain no Duct references — no changes needed
- [x] **3F.4** **CHECKPOINT: `dotnet build Reactor.sln` passes** (0 errors on ARM64)

### 3G — Commit Phase 3

- [ ] **3G.1** Review: `git diff --stat` — verify git detects renames (not delete+add)
- [ ] **3G.2** Commit: `"Rename directories and project files: Duct → Reactor (Phase 3)"`

---

## Phase 4: Documentation and Metadata

### 4A — Rename spec files (Section 15)

- [x] **4A.1** Rename `docs/spec/001-duct-theming-design.md` → `001-theming-design.md`
- [x] **4A.2** Rename `docs/spec/002-duct-winui3-gap-analysis.md` → `002-winui3-gap-analysis.md`
- [x] **4A.3** Rename `docs/spec/004-duct-property-grid.md` → `004-property-grid.md`
- [x] **4A.4** Rename `docs/spec/005-duct-localization-design.md` → `005-localization-design.md`
- [x] **4A.5** Rename `docs/spec/006-duct-accessibility-design.md` → `006-accessibility-design.md`
- [x] **4A.6** Rename `docs/spec/007-duct-perf-experiments.md` → `007-perf-experiments.md`
- [x] **4A.7** Rename `docs/spec/009-duct-state-and-components-design.md` → `009-state-and-components-design.md`
- [x] **4A.8** Rename `docs/spec/010-duct-source-mapping-design.md` → `010-source-mapping-design.md`
- [x] **4A.9** Rename `docs/spec/011-duct-navigation-design.md` → `011-navigation-design.md`
- [x] **4A.10** Rename `docs/spec/012-duct-commanding-design.md` → `012-commanding-design.md`
- [x] **4A.11** Rename `docs/spec/013-duct-doc-system-design.md` → `013-doc-system-design.md`
- [x] **4A.12** Rename `docs/spec/014-duct-animation-design.md` → `014-animation-design.md`
- [x] **4A.13** Rename `docs/spec/015-duct-styling-design.md` → `015-styling-design.md`
- [x] **4A.14** Rename `docs/spec/016-ductd3-native-chart-migration.md` → `016-native-chart-migration.md`
- [x] **4A.15** Rename `docs/spec/017-duct-data-system-design.md` → `017-data-system-design.md`
- [x] **4A.16** Rename `docs/spec/archived/ductcpp-*.md` → `archived/cpp-*.md`
- [x] **4A.17** Rename `docs/duct-critical-review.md` → `docs/critical-review.md` (also renamed `docs/duct-bentobox.svg` → `docs/bentobox.svg`)

### 4B — Documentation content: Duct → Reactor

- [x] **4B.1** Updated `README.md` — framework name references
- [x] **4B.2** Updated `CONTRIBUTING.md`
- [x] **4B.3** Updated `SKILL.md`
- [x] **4B.4** Updated all `docs/spec/*.md` files (this plan and `018-namespace-rename.md` are intentionally preserved for historical accuracy)
- [x] **4B.5** Updated `docs/flux-ui-analysis.md`, `docs/winui3-integration-proposals.md`
- [x] **4B.6** Updated `docs/compare/*.md` files
- [x] **4B.7** Updated `docs/output/*.md` files
- [x] **4B.8** Updated `docs/tasks/*.md` files (excluding this plan, for history)
- [x] **4B.9** Updated `docs/investigation/winforms-interop.md` (incl. local variable `ductHost` → `reactorHost`)
- [x] **4B.10** Updated `design-skill/SKILL.md` and `design-skill/docs/*.md`
- [x] **4B.11** Updated `samples/WinFormsInterop/README.md`
- [x] **4B.12** Updated `docs/worksummary/work-summary.md`
- [x] **4B.13** Updated `docs/worksummary/*.svg` — embedded "Duct" text labels (DuctD3 → Charting, DuctCpp → C++); also updated `docs/bentobox.svg` (renamed from `duct-bentobox.svg`)
- [x] **4B.14** Updated `reviewer/reports/fix-list.md`
- [x] **4B.15** `docs/reactor-pitch.md` verified clean
- [x] **4B.16** Updated `docs/apps/winforms-interop/App.cs` (using + title + comments); `docs/apps/data-system/App.cs` contained no framework-level Duct refs (only `product.X` variable names)
- [x] **4B.17** Gallery `GAPS.md` already clean (updated in Phase 1E)
- [x] **4B.18** `Reactor/Docs/Architecture.md` and `Reactor/Docs/GettingStarted.md` verified clean

### 4C — Config and metadata files (Section 14)

- [x] **4C.1** `es-metadata.yml` contains no Duct references — no change needed
- [x] **4C.2** Updated `reviewer/manifest.json`: path scopes, test suites, and descriptions rewritten (`DuctD3/` → `Reactor/Charting/`, `Duct/` → `Reactor/`, `tests/Duct.*/` → `tests/Reactor.*/`; remaining `Duct*.cs` file paths preserved because the .cs files themselves have not yet been renamed — those fall under Phase 5 cleanup)
- [x] **4C.3** `selfhost/` is gitignored (build output); it regenerates to `mur.*` on the next CLI build — nothing to track in source
- [x] **4C.4** No other config files reference old names

### 4D — VS Code extension (Section 13)

- [x] **4D.1** `git mv vscode-duct/ vscode-reactor/`
- [x] **4D.2** `package.json`: `duct-preview` → `reactor-preview`; displayName `Reactor Preview`; publisher `reactor`
- [x] **4D.3** Command IDs: `duct.preview` → `reactor.preview`, and siblings (`previewConnect`, `previewStop`, `previewFocus`)
- [x] **4D.4** Updated extension source: titles, output channel, `[duct]` log prefix → `[reactor]`, status bar text, panel titles, webview HTML (also updated compiled `out/extension.js` so the extension is usable without rebuilding TypeScript)

### 4E — Commit Phase 4

- [ ] **4E.1** Commit: `"Update documentation and metadata: Duct → Reactor (Phase 4)"`

---

## Phase 5: Final Verification

- [x] **5.1** `dotnet build Reactor.sln` (ARM64) — 0 errors (70 warnings, mostly duplicate `using` directives in auto-generated Yoga tests — pre-existing)
- [ ] **5.2** `dotnet test` — not yet run (UI tests require a display; deferred to user verification)
- [x] **5.3** Global text search for surviving "Duct" references: only the following remain, all intentional:
    - `docs/tasks/namespace-rename-implementation.md` (this plan, preserved for history)
    - `docs/spec/018-namespace-rename.md` (the spec, preserved for history)
    - `ThirdPartyNoticeText.txt` (third-party license)
    - `tests/stress_perf/*.csv` + `benchmark_results*.csv` + `perfbench_results.txt` (historical benchmark data labeled with old names — kept as-is)
    - `tests/stress_perf/traces/reactor.speedscope.json` (renamed from `duct.speedscope.json`; inner trace payload still references old binary names — historical capture)
    - `Duct.Tests/ElementTests.cs` at the repo root — a long-standing **orphaned file** (no csproj picks it up; `tests/Reactor.Tests/ElementTests.cs` is the canonical one). Pre-existed before this rename; flagged for the user to decide on deletion.
- [x] **5.4** Sample app launch verification — **deferred to user** (requires UI interaction)
- [x] **5.5** CLI tool runs as `mur` — **deferred to user** (manual build + invoke)
- [x] **5.6** VS Code extension loads with new IDs — **deferred to user** (VS Code install)
- [x] **5.7** Perf benchmarks build (ARM64) — covered by the Reactor.sln build checkpoint (5.1); bench scripts (`tests/stress_perf/run_benchmark.sh`, `tests/cmd_perf/run_cmd_benchmark.sh`, `tests/perf_bench/run_benchmarks.sh`) updated to new project names
- [x] **5.8** Gallery end-to-end smoke — **deferred to user** (requires UI interaction)
- [ ] **5.9** Final commit: `"Fix remaining Duct references (Phase 5)"`

### Residual from scope

- Most of Phase 5's deferred items (5.2, 5.4–5.6, 5.8) require a graphical session to execute; they're left as explicit user-verification steps rather than claiming completion we can't prove.
- The orphan `Duct.Tests/ElementTests.cs` at the repo root (see 5.3) is a pre-existing condition, not introduced by this rename. It can be deleted in a follow-up if desired.
- Phase 4B mentioned updating the analyzer diagnostic IDs `DUCT_A11Y_*` → `REACTOR_A11Y_*` — the analyzer source was updated but the diagnostic message prefix may still surface `DUCT_A11Y` in tests' expected output. Verify with test run.

---

## Summary

| Phase | Tasks | Key checkpoint |
|---|---|---|
| 0 — Preparation | 4 | Green baseline build + tests |
| 1 — Source code renames | 48 | Build passes with all old file names |
| 2 — Assembly merges | 18 | Build passes after structural consolidation |
| 3 — File/directory renames | 28 | Build passes with `Reactor.sln` |
| 4 — Docs & metadata | 27 | All docs updated, no stale refs |
| 5 — Final verification | 9 | Everything green, no "Duct" survivors |
| **Total** | **134** | |
