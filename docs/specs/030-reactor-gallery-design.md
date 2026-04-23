# Reactor Gallery — Design Spec

## Overview

A first-party "gallery" system for Reactor: a runnable WinUI 3 app that showcases Reactor framework features (hooks, commanding, navigation, styling, data, input/gestures, devtools, etc.) through small, isolated, interactive samples backed by markdown documentation.

The design borrows heavily from the [Windows Community Toolkit sample-generator system](https://github.com/CommunityToolkit/Tooling-Windows-Submodule), which has powered the CT Labs and CT 8.x galleries for several years. We adopt its **compile-time attribute discovery + markdown frontmatter + generated registry** model, and adapt the pieces that are XAML-specific (XAML/`UserControl` validation, `ms-appx:///` XAML loading, `MarkdownTextBlock`-based rendering) to Reactor's pure-C# component model.

This gallery is **not** a replacement for `samples/ReactorGallery/` (the WinUI-controls-as-Reactor showcase stays put for now) or `docs/_pipeline/apps/` (the documentation screenshot pipeline stays separate). It replaces `samples/Reactor.TestApp/` as the canonical "show me what Reactor can do" destination.

---

## Goals

1. **Attribute-driven sample discovery.** A contributor adds one `.cs` file and edits one `.md` file; the sample appears in the gallery with zero registry edits, zero csproj edits, zero folder creation.
2. **One source of truth for sample code.** Live code and displayed source are the same `.cs` file — no string-literal duplication.
3. **Flat authoring.** All sample `.cs` files live in a single `gallery/src/` directory. No per-feature projects, no nested folders. Feature-area grouping is metadata (enum values in attributes), not directory structure.
4. **Rich, interactive docs.** One markdown doc per feature area (`Hooks.md`, `Commanding.md`, etc.) with YAML frontmatter and `> [!SAMPLE Id]` embeds that splice live samples into the prose.
5. **Interactive options without boilerplate.** `BoolOption` / `NumericOption` / `TextOption` / `MultiChoiceOption` attributes produce a typed options record passed to the sample — no manual bindings.
6. **Compile-time validation.** 17 diagnostic rules catching missing frontmatter, unreferenced samples, duplicate IDs, invalid options, missing source regions, etc.
7. **Free selftest smoke coverage.** Every sample auto-registers as a selftest fixture asserting it mounts without exception.
8. **Isolated infra vs. content.** `gallery/infra/` holds the framework pieces (attributes, generator, shell). `gallery/src/` holds only `.cs` samples + `.md` docs. Adding a sample never touches infra.

## Non-goals

- **Not** a replacement for `samples/ReactorGallery/` (WinUI control showcase). That may migrate later.
- **Not** merged with `docs/_pipeline/apps/`. The doc pipeline hosts full apps for screenshots; this hosts component-sized samples for direct interaction. Cross-pollination is a possible future follow-up.
- **No** multi-target heads. Reactor is WinUI 3 only; one gallery exe.
- **No** auto-generated prose. Markdown is human-authored.

---

## Architecture Overview

```
gallery/
  infra/
    Reactor.Gallery.SampleGen/              ← Roslyn incremental generator
      Attributes/
        ReactorSampleAttribute.cs
        ReactorSampleBoolOptionAttribute.cs
        ReactorSampleNumericOptionAttribute.cs
        ReactorSampleTextOptionAttribute.cs
        ReactorSampleMultiChoiceOptionAttribute.cs
        ReactorSampleOptionsPaneAttribute.cs
      Metadata/
        ReactorSampleMetadata.cs            ← runtime metadata record
        ReactorFrontMatter.cs
        ReactorSampleCategory.cs            ← enum
        ReactorSampleSubcategory.cs         ← enum
        ReactorSampleOptionMetadata.cs      ← base + 4 concrete subtypes
      Diagnostics/
        DiagnosticDescriptors.cs            ← RGAL0001..RGAL0017
      ReactorSampleMetadataGenerator.cs     ← main generator (single-pass)
      ReactorSampleMetadataGenerator.Sample.cs
      ReactorSampleMetadataGenerator.Documentation.cs
      Reactor.Gallery.SampleGen.csproj      ← netstandard2.0, IsRoslynComponent

    Reactor.Gallery.Shell/                  ← shared gallery UI (as a class library)
      GalleryShell.cs                       ← nav shell, routing, search
      Renderers/
        SampleRenderer.cs                   ← instantiates sample + options + source view
        DocumentationRenderer.cs            ← renders markdown + embedded samples
        OptionsPaneRenderer.cs              ← generic options pane for generated options
        SourceViewer.cs                     ← region-aware .cs loader + highlighter
      Helpers/
        NavTreeBuilder.cs                   ← category/subcategory grouping
        MarkdownParser.cs                   ← frontmatter strip + [!SAMPLE] tokenizer
        RegionExtractor.cs                  ← #region sample slicing
      Reactor.Gallery.Shell.csproj

    Reactor.Gallery.SelfTestBridge/         ← selftest fixture auto-registration
      GallerySelfTestFixtures.cs            ← enumerates registry, emits fixtures
      Reactor.Gallery.SelfTestBridge.csproj

    Reactor.Gallery.App/                    ← the runnable head exe
      App.cs                                ← ReactorApp.Run<GalleryShell>(...)
      Assets/
      Reactor.Gallery.App.csproj            ← globs ../src/**/*.cs directly

  src/                                      ← FLAT. No subdirectories, no csproj.
    UseStateSample.cs
    UseEffectSample.cs
    UseReducerSample.cs
    StandardCommandsSample.cs
    AcceleratorsSample.cs
    NavigationBasicSample.cs
    ThemeTokensSample.cs
    ...
    Hooks.md                                ← one .md per feature area;
    Commanding.md                           ←   prose + `> [!SAMPLE Id]` embeds
    Navigation.md
    Theming.md
    ...

  Build-Gallery.ps1                         ← one-shot build-and-run script
  ReadMe.md
```

**Flat-content model**: there are no per-feature `.Samples.csproj` files and no feature-area subdirectories under `gallery/src/`. Every sample is a single `.cs` file; every feature-area doc is a single `.md` file. The head app's `.csproj` globs `../src/**/*.cs` into its own compilation directly. Feature-area grouping comes from the `Category`/`Subcategory` values in `[ReactorSample]` — not from folder structure. Adding a sample = creating one `.cs` file (and optionally appending a `> [!SAMPLE Id]` to the relevant `.md`).

**Single-compilation generator**: because all samples live in the head app's compilation, the two-phase detection pattern from CT (sample project diagnostics-only vs. head project registry) is unnecessary here. The generator runs once, in the head project, and both emits diagnostics and generates registries in one pass:

- Scans syntax trees in the current `Compilation` for `[ReactorSample]`-decorated classes.
- Scans `AdditionalFiles` for `.md` files, parses frontmatter + `> [!SAMPLE Id]` references.
- Emits:
  - `ReactorSampleRegistry.g.cs` — `Dictionary<string, ReactorSampleMetadata>` with factory lambdas
  - `ReactorDocumentRegistry.g.cs` — `IEnumerable<ReactorFrontMatter>` from every doc
- Reports the full diagnostic set (RGAL0001–0017) in the same pass.

This is strictly simpler than CT's model (they needed two phases because components ship as separate NuGet packages; we don't).

---

## Core Concepts

### Sample

A `Component` subclass annotated with `[ReactorSample]`. Its `Render()` returns the UI to show in the gallery. If the sample declares generated options, it takes a typed `SampleOptions` record as its single constructor argument (the options attributes shape this record; see below).

### Registry

A compile-time-generated static class keyed by sample id. Each entry stores display metadata, a `Func<TOptions?, Component>` factory, and the option metadata needed to render the options pane.

### Frontmatter

YAML header on a `.md` file. Declares title, description, category, subcategory, keywords, icon, discussion/issue ids, experimental flag. Same schema as CT with one addition (`feature` — the Reactor feature area, used in nav).

### Option

An interactive control surfaced in the options pane. Attribute declares name + default + UI hints; generator adds a property to the `SampleOptions` record. Sample reads the value; when the user changes it, the shell rebuilds the sample.

---

## Attributes

All attributes live in `Microsoft.UI.Reactor.Gallery` and target `AttributeTargets.Class` unless noted.

```csharp
// ── ReactorSampleAttribute ──────────────────────────────────────

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ReactorSampleAttribute : Attribute
{
    public ReactorSampleAttribute(string id, string displayName, string description)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }

    // Optional overrides. When unset, nav placement is inherited from the
    // first .md file that embeds this sample via `> [!SAMPLE Id]`.
    public ReactorSampleCategory    Category    { get; set; } = ReactorSampleCategory.Inherit;
    public ReactorSampleSubcategory Subcategory { get; set; } = ReactorSampleSubcategory.Inherit;

    // Skip the auto-generated selftest fixture for samples that need live
    // user interaction to do anything interesting (e.g., drag-drop demos).
    public bool SkipSelfTest { get; set; } = false;
}

// ── Option attributes (AllowMultiple = true) ────────────────────

public sealed class ReactorSampleBoolOptionAttribute : Attribute
{
    public ReactorSampleBoolOptionAttribute(string name, bool defaultValue) { ... }
    public string  Name          { get; }
    public bool    DefaultValue  { get; }
    public string? Title         { get; set; }   // UI label; defaults to Name
}

public sealed class ReactorSampleNumericOptionAttribute : Attribute
{
    public ReactorSampleNumericOptionAttribute(
        string name, double defaultValue, double min, double max, double step = 1) { ... }
    // + Title, ShowAsNumberBox
}

public sealed class ReactorSampleTextOptionAttribute : Attribute
{
    public ReactorSampleTextOptionAttribute(string name, string defaultValue) { ... }
    // + Title, PlaceholderText
}

public sealed class ReactorSampleMultiChoiceOptionAttribute : Attribute
{
    public ReactorSampleMultiChoiceOptionAttribute(string name, params string[] choices) { ... }
    // choices support "Label : Value" syntax
    // + Title
}

// ── Custom options pane ─────────────────────────────────────────

[AttributeUsage(AttributeTargets.Class)]
public sealed class ReactorSampleOptionsPaneAttribute : Attribute
{
    public ReactorSampleOptionsPaneAttribute(string sampleId) { SampleId = sampleId; }
    public string SampleId { get; }
}
```

### Generated options record

For a sample like:

```csharp
[ReactorSampleBoolOption("ShowBorder", true, Title = "Show border")]
[ReactorSampleNumericOption("FontSize", 14, 8, 48, 1)]
[ReactorSample("BasicText", "Basic text", "A minimal text sample with styling options.")]
public sealed class BasicTextSample : Component<BasicTextSample.Options>
{
    public record Options(bool ShowBorder, double FontSize);

    public override Element Render() {
        var o = Props;   // typed options
        var text = TextBlock("Hello Reactor").FontSize(o.FontSize);
        return o.ShowBorder ? Border(text).WithBorder(Theme.SurfaceStroke) : text;
    }
}
```

The generator emits (into the head project):

```csharp
// ReactorSampleRegistry.g.cs (excerpt)
["BasicText"] = new ReactorSampleMetadata(
    id: "BasicText",
    displayName: "Basic text",
    description: "...",
    sampleComponentType: typeof(BasicTextSample),
    sampleFactory: options => new BasicTextSample((BasicTextSample.Options)options!),
    defaultOptionsFactory: () => new BasicTextSample.Options(
        ShowBorder: true,
        FontSize: 14),
    optionsType: typeof(BasicTextSample.Options),
    optionDescriptors: new ReactorSampleOptionDescriptor[] {
        new BoolOptionDescriptor(name: "ShowBorder", title: "Show border", defaultValue: true),
        new NumericOptionDescriptor(name: "FontSize", title: "FontSize",  defaultValue: 14, min: 8, max: 48, step: 1),
    }),
```

The **convention**: a sample author declares a nested `record Options(...)` whose property names + types match the attribute set exactly. The generator validates this correspondence (diagnostic `RGAL0005`). Samples with no options declare no record and inherit directly from `Component`.

> **Rationale — why not auto-generate the record?** We considered generator-authored partial records. It's viable but introduces the "invisible property" problem (IntelliSense shows properties that don't exist in source), and clashes with the AOT-compatible default Reactor targets. An author-written record is 1 line of code and keeps the code discoverable. The generator validates the shape, so forgetting to update it after adding an option is a compile error, not a runtime bug.

---

## Markdown Documentation Format

### Frontmatter schema

```yaml
---
title: Hooks
author: andersonch
description: Stateful logic in functional components via hooks.
keywords: hooks, state, useState, useEffect, lifecycle
feature: Hooks              # NEW vs. CT — the Reactor feature area (maps to nav category)
category: Core              # ReactorSampleCategory enum value
subcategory: State          # ReactorSampleSubcategory enum value
discussion-id: 0
issue-id: 0
icon: Assets/hooks.png
experimental: false
---
```

`category` and `subcategory` are enum-constrained (`RGAL0010` fires for invalid values). `feature` is free-form for now; nav groups by `feature` → `category` → `subcategory` → sample.

Enums (initial set, extensible):

```csharp
public enum ReactorSampleCategory
{
    Inherit = 0,   // sentinel: use the owning doc's category
    Core,          // reconciler, components, hooks, rendering
    Data,          // data system, async resources, collections
    Input,         // pointer, gesture, keyboard, drag-drop
    Layout,        // stacks, grids, flex, canvas
    Navigation,    // routing, back-stack, lifecycle
    Commanding,    // commands, accelerators
    Styling,       // themes, tokens, style groups
    Animation,     // transitions, springs
    Localization,  // .resw, Loc.g, runtime switching
    Devtools,      // inspection, agent tools
    Charting,
    Misc,
}

public enum ReactorSampleSubcategory
{
    Inherit = 0,   // sentinel: use the owning doc's subcategory
    None,
    State, Effects, Memoization, Context, Refs,
    Pointer, Gesture, Keyboard, DragDrop,
    StackLayout, GridLayout, FlexLayout, CanvasLayout,
    Routing, Lifecycle, BackStack,
    StandardCommands, Accelerators, Menus,
    Themes, Tokens, StyleGroups, Typography,
    Transitions, Springs, Keyframes,
    DataGrid, TreeGrid, Forms, Validation,
    // ... extensible
}
```

### Sample embed syntax

Same as CT:

```markdown
# Hooks

Hooks let you attach stateful logic to a functional component…

> [!SAMPLE UseStateSample]

Effects run after render and clean up on unmount:

> [!SAMPLE UseEffectSample]
```

Regex (matches CT exactly for parser reuse):

```
^>\s*\[!SAMPLE\s+(?<sampleid>[A-Za-z_][A-Za-z0-9_]*)\s*\]\s*$
```

Case-insensitive, multiline, one sample per line.

### Doc-to-sample pairing

Because all samples live in a single compilation, pairing is purely by id: each `> [!SAMPLE Id]` in any `.md` file must match a `[ReactorSample(id: Id, ...)]` somewhere in the head app's compilation. Mismatch → diagnostic `RGAL0012`. The `.md` file's `feature`/`category`/`subcategory` frontmatter fields drive nav placement of the doc itself; samples inherit their nav placement from their own `[ReactorSample]` category values.

---

## Source Code Display

### Region markers — the one-source-of-truth rule

Every `[ReactorSample]` class **must** contain exactly one `#region sample` / `#endregion` pair inside its `Render()` method, bracketing the body that the gallery displays as source.

```csharp
public override Element Render()
{
    var (count, setCount) = UseState(0);

    return SampleCard("Counter",
        #region sample
        VStack(8,
            TextBlock($"Count: {count}").FontSize(18),
            Button("Increment", () => setCount(count + 1)))
        #endregion
    );
}
```

### Loader behavior

At runtime, `SourceViewer`:

1. Loads the sample's `.cs` file from app content (the `.csproj` props include `.cs` as `Content` with `CopyToOutput`).
2. Finds the first `#region sample` and its matching `#endregion`.
3. Extracts the inner text, computes the minimum common leading indent across non-empty lines, strips it.
4. Renders with syntax highlighting (`ColorCode` or equivalent already available in Reactor samples infra).

### Diagnostics

- `RGAL0016` (error): `[ReactorSample]` class missing `#region sample` / `#endregion`.
- `RGAL0017` (error): more than one `#region sample` in a single sample class.

The generator runs a simple Roslyn `SyntaxTrivia` pass to verify the region exists at declaration time; no runtime-only failures.

### Why regions, not whole-file

Loading the whole `.cs` shows imports, the `[ReactorSample]` attribute, `class Foo : Component`, constructor, `Render()` signature, and closing braces around the ~5 interesting lines. Regions give one source of truth *and* a reader-focused slice. See Q&A in the brainstorm thread preceding this spec for the full tradeoff analysis.

---

## Source Generator

### Incremental pipeline (single-compilation)

```
AdditionalTexts (*.md) ──► FrontMatterParser ──► ReactorFrontMatter[]
                                                       │
                                                       ▼
                                         ReactorDocumentRegistry.g.cs

SyntaxNodes in Compilation (classes with
  [ReactorSample]/[ReactorSample*Option])
         │
         ▼
   AttributeReader ─► ReactorSampleDescriptor[]
         │
         ▼
   Pairing + Validation (all 17 RGAL diagnostics)
         │
         ▼
   ReactorSampleRegistry.g.cs
```

The generator runs once in the head project's compilation. No MSBuild property switches, no two-phase behavior — just standard `IIncrementalGenerator` pipeline stages. Because we don't crawl `ReferencedAssemblySymbols`, incrementality is clean: edit one sample, only that sample's node is recomputed.

### Port-vs-rewrite

| CT component | Action |
|---|---|
| `ToolkitSampleMetadataGenerator.Documentation.cs` | **Port** (regex, frontmatter parser) |
| `ToolkitSampleMetadataGenerator.Sample.cs` | **Rewrite** — drop XAML `UserControl`/`Page` base-type validation; replace with `Component` / `Component<T>` validation |
| `DiagnosticDescriptors.cs` | **Port** all rules, rename `TKSMPL` → `RGAL`, add `RGAL0016`/`RGAL0017` for regions |
| `ToolkitFrontMatter` / `ToolkitSampleMetadata` | **Port** shape; rename; drop XAML-specific `Type` handling |
| Option attribute generators | **Rewrite** — emit descriptor entries, not XAML `INotifyPropertyChanged` properties |

---

## Gallery Shell (Runtime)

The shell is a Reactor component tree (not XAML, unlike CT's `CommunityToolkit.App.Shared`):

```csharp
public sealed class GalleryShell : Component
{
    public override Element Render()
    {
        var (selectedSampleId, setSelected) = UseState<string?>(null);
        var (searchQuery,      setQuery)    = UseState("");

        var tree = NavTreeBuilder.Build(ReactorDocumentRegistry.All, searchQuery);

        return NavigationView(
            items: tree,
            onSelectionChanged: id => setSelected(id),
            content: selectedSampleId is null
                ? WelcomePage()
                : DocumentationRenderer(selectedSampleId)
        )
        .SearchBox(searchQuery, setQuery);
    }
}
```

Key renderers:

- **`DocumentationRenderer(docPath)`** — loads `.md`, strips frontmatter, tokenizes into `(MarkdownBlock | SampleReference)[]`, renders each block:
  - Markdown → `MarkdownRenderer` (Reactor native, leveraging the existing `MarkdownTextBlock` component under `components/MarkdownTextBlock/` if we absorb it — otherwise a minimal renderer for the subset we need).
  - Sample reference → `SampleRenderer(id)`.
- **`SampleRenderer(id)`** — looks up `ReactorSampleRegistry.All[id]`, manages an `UseState<TOptions>` for options, renders:
  ```
  ┌─ SampleCard ────────────────────────────────┐
  │ DisplayName                                 │
  │ Description                                 │
  │                                             │
  │ [live sample instance via sampleFactory]    │
  │                                             │
  │ ▶ Show source     [Options pane ▾]          │
  │ ┌─────────────────────┐ ┌─────────────────┐ │
  │ │ // region sample    │ │ ☑ ShowBorder    │ │
  │ │ VStack(8, ...)      │ │ FontSize [ 14 ] │ │
  │ └─────────────────────┘ └─────────────────┘ │
  └─────────────────────────────────────────────┘
  ```
- **`OptionsPaneRenderer`** — renders one control per `ReactorSampleOptionDescriptor`:
  - `BoolOptionDescriptor` → `CheckBox` or `ToggleSwitch`
  - `NumericOptionDescriptor` → `Slider` or `NumberBox` (per `ShowAsNumberBox`)
  - `TextOptionDescriptor` → `TextBox`
  - `MultiChoiceOptionDescriptor` → `ComboBox` or `RadioButtons`
  - Writes option deltas back to the sample's typed options record (via `with` expression on the record).
- **Custom options pane** — if a class with `[ReactorSampleOptionsPane(sampleId)]` exists, shell uses it instead of the generated pane.

---

## Build System

### The only sample-carrying csproj: `Reactor.Gallery.App.csproj`

No per-feature or per-sample csproj. The head app globs sample content directly from `gallery/src/` into its own compilation:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows10.0.22621.0</TargetFramework>
    <RootNamespace>Reactor.Gallery</RootNamespace>
    <UseWindowsAppSDK>true</UseWindowsAppSDK>
  </PropertyGroup>

  <ItemGroup>
    <!-- All samples compiled directly into the head app -->
    <Compile Include="..\..\src\**\*.cs" LinkBase="Samples" />

    <!-- .cs files also shipped as content so SourceViewer can load them at runtime
         for the region-based source display -->
    <Content Include="..\..\src\**\*.cs" LinkBase="Samples">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>

    <!-- .md files: content (renderer reads at runtime) + AdditionalFiles (generator) -->
    <Content Include="..\..\src\**\*.md" LinkBase="Docs">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <AdditionalFiles Include="..\..\src\**\*.md" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\src\Reactor\Reactor.csproj" />
    <ProjectReference Include="..\Reactor.Gallery.Shell\Reactor.Gallery.Shell.csproj" />
    <ProjectReference Include="..\Reactor.Gallery.SampleGen\Reactor.Gallery.SampleGen.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

Samples author nothing project-related. A new sample is just a new `.cs` file in `gallery/src/`. The glob picks it up on the next build.

### Sample file convention

Every sample `.cs` file uses the same namespace and set of `using`s (enforced by repo editorconfig + a one-time template):

```csharp
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Gallery;
using static Microsoft.UI.Reactor.Factories;
using static Reactor.Gallery.Shell.Helpers.SampleHost;   // SampleCard, PageHeader, etc.

namespace Reactor.Gallery.Samples;

[ReactorSample(
    id: nameof(UseStateSample),
    displayName: "useState",
    description: "Stateful counter using the useState hook.")]
public sealed class UseStateSample : Component { /* ... */ }
```

All samples live under a single flat namespace (`Reactor.Gallery.Samples`). No namespace collisions because sample class names are unique ids (`RGAL0015` enforces id uniqueness and class names match ids by convention).

### `gallery/Build-Gallery.ps1`

One-shot build & run:

```powershell
param([switch]$NoRun, [switch]$Release)

$cfg = if ($Release) { "Release" } else { "Debug" }
dotnet build gallery/infra/Reactor.Gallery.App/Reactor.Gallery.App.csproj -c $cfg
if (-not $NoRun) {
    dotnet run --project gallery/infra/Reactor.Gallery.App/Reactor.Gallery.App.csproj -c $cfg
}
```

---

## Diagnostics

Ported from CT (`TKSMPL` → `RGAL`), plus region-specific rules:

| Code | Severity | Trigger |
|---|---|---|
| RGAL0001 | Error | Generated option attribute on class without `[ReactorSample]` |
| RGAL0002 | Error | Option name empty, non-identifier, or C# keyword |
| RGAL0003 | Error | `[ReactorSampleOptionsPane]` references unknown sample id |
| RGAL0004 | Error | Duplicate option name on same sample class |
| RGAL0005 | Error | Sample options record shape does not match option attributes |
| RGAL0006 | Error | MultiChoice option has zero choices |
| RGAL0007 | Error | `[ReactorSample]` on non-public class or class that is not `Component` / `Component<T>` |
| RGAL0008 | Error | `[ReactorSampleOptionsPane]` on class that is not `Component` |
| RGAL0009 | Error | Option attribute on class without options record (i.e., not `Component<T>`) |
| RGAL0010 | Error | Invalid YAML: unknown category/subcategory enum value, malformed YAML |
| RGAL0011 | Error | Required frontmatter field missing (title / description / category / subcategory) |
| RGAL0012 | Error | `> [!SAMPLE id]` references a sample id that doesn't exist |
| RGAL0013 | Warning | Sample declared but not referenced in any markdown |
| RGAL0014 | Warning | Markdown file has zero `> [!SAMPLE]` tags |
| RGAL0015 | Error | Two `[ReactorSample]` attributes share the same id |
| RGAL0016 | Error | Sample class missing `#region sample` / `#endregion` in `Render()` |
| RGAL0017 | Error | Sample class has more than one `#region sample` block |

---

## Selftest Integration

The `Reactor.Gallery.SelfTestBridge` project emits a selftest fixture per sample. Pattern mirrors `tests/Reactor.SelfTests/SelfTestBatch.cs`:

```csharp
// GallerySelfTestFixtures.cs
public static class GallerySelfTestFixtures
{
    public static IEnumerable<SelfTestFixture> All =>
        ReactorSampleRegistry.All.Values.Select(meta =>
            new SelfTestFixture(
                name: $"Gallery/{meta.Id}",
                run: emit => {
                    try
                    {
                        var options = meta.DefaultOptionsFactory?.Invoke();
                        var component = meta.SampleFactory(options);
                        var el = component.Render();          // doesn't crash
                        Reconciler.Mount(el, TestHost.Root);   // mounts
                        emit.Ok($"Gallery/{meta.Id} mounts");
                    }
                    catch (Exception ex)
                    {
                        emit.NotOk($"Gallery/{meta.Id} mount failed: {ex.Message}");
                    }
                }));
}
```

`Reactor.AppTests.Host` picks these up via the existing fixture enumeration mechanism. Every merged sample becomes free CI smoke coverage (mounts without exception).

Opt-out: a sample can mark itself `[ReactorSample(..., SkipSelfTest = true)]` for cases where the sample needs live user interaction (e.g., a drag-drop demo that asserts nothing meaningful in a headless mount).

---

## Authoring Workflow (End-to-End)

Adding a sample for the `useReducer` hook:

1. **Create a single file**, `gallery/src/UseReducerSample.cs`:
   ```csharp
   using Microsoft.UI.Reactor;
   using Microsoft.UI.Reactor.Gallery;
   using static Microsoft.UI.Reactor.Factories;

   namespace Reactor.Gallery.Samples;

   [ReactorSampleBoolOption("ShowHistory", false, Title = "Show history")]
   [ReactorSample(
       id: nameof(UseReducerSample),
       displayName: "useReducer",
       description: "Managing complex state transitions with a reducer function.",
       Category = ReactorSampleCategory.Core,
       Subcategory = ReactorSampleSubcategory.State)]
   public sealed class UseReducerSample : Component<UseReducerSample.Options>
   {
       public record Options(bool ShowHistory);

       public override Element Render()
       {
           var (state, dispatch) = UseReducer(Reduce, new CounterState(0));
           return VStack(12,
               #region sample
               TextBlock($"Count: {state.Count}").FontSize(24),
               HStack(8,
                   Button("-", () => dispatch(Action.Decrement)),
                   Button("+", () => dispatch(Action.Increment)),
                   Button("Reset", () => dispatch(Action.Reset)))
               #endregion
               ,
               Props.ShowHistory ? HistoryView(state.History) : Empty());
       }
       // ... reducer, records ...
   }
   ```

2. **Append a sample reference to the Hooks doc** (`gallery/src/Hooks.md` — edit an existing file):
   ```markdown
   ## useReducer

   When state transitions get complex, a reducer keeps them in one place.

   > [!SAMPLE UseReducerSample]
   ```

3. **Build the gallery.** The head app's glob picks up the new `.cs`; the generator discovers the attribute, pairs it to the markdown reference, adds it to `ReactorSampleRegistry.g.cs` and (via selftest bridge) to the `Gallery/UseReducerSample` fixture.

4. **Run** `gallery/Build-Gallery.ps1`. Navigate to Core → State → useReducer, interact, toggle "Show history".

No csproj edits. No registry edits. No folder creation. No test edits (beyond the free smoke coverage). Two files touched (one new, one edited) to add a fully-documented, options-bearing, selftest-covered sample.

> **Note on the category fields**: `ReactorSampleAttribute` gains optional `Category` and `Subcategory` properties in the flat model (they were implicit in folder structure before). Samples that omit them inherit their category from the first `.md` file that embeds them — generator fills this in, so authors only specify `Category`/`Subcategory` on a sample if it needs to appear under a *different* heading than its owning doc.

---

## Open Questions

1. **Markdown renderer.** Labs-Windows has a Reactor-compatible `MarkdownTextBlock` component (`components/MarkdownTextBlock/`). Do we absorb that or write a minimal renderer for the subset we need (headings, code blocks, lists, inline code, sample embeds)? Leaning: minimal renderer in `Reactor.Gallery.Shell`, upgrade later.

2. **Syntax highlighting.** ColorCode (used by CT) is WinUI-compatible. Reasonable default; revisit if we hit a perf or AOT issue.

3. **Navigation grouping.** `feature` → `category` → `subcategory` → sample is four levels. Probably overkill for launch. Initial version: `category` → `subcategory` → sample, matching CT exactly. Add `feature` only if the sample count makes two-level nav unwieldy.

4. **Sample options beyond the four types.** CT's set (bool, numeric, text, multi-choice) has held up for years; ship with those. If we need more (color picker, file picker), `[ReactorSampleOptionsPane]` covers the custom case until a pattern emerges.

5. **Relationship to the `TestApp` retirement.** `samples/Reactor.TestApp/` should be retired once the gallery reaches feature parity for what it currently demonstrates. Need an audit pass (separate from this spec).

---

## Ship Plan

### Phase 1 — Infra skeleton (1–2 days)
- `gallery/infra/Reactor.Gallery.SampleGen/` project scaffolded (`netstandard2.0`, `IsRoslynComponent`).
- All attribute types defined and public.
- `ReactorSampleMetadata`, `ReactorFrontMatter`, enums, option descriptor types defined.
- `DiagnosticDescriptors.cs` with all 17 codes.
- **Exit criteria:** library compiles, attributes are `[Fact]`-testable from a dummy consumer.

### Phase 2 — Generator core (2–3 days)
- Port `ToolkitSampleMetadataGenerator.Documentation.cs` (frontmatter + `[!SAMPLE]` parser).
- Rewrite `Sample.cs` for `Component` / `Component<T>` detection (single-compilation pass — no two-phase, no `ReferencedAssemblySymbols` crawl).
- Registry emission (`ReactorSampleRegistry.g.cs`, `ReactorDocumentRegistry.g.cs`).
- Full diagnostic coverage (all 17 RGAL codes in one pass).
- **Exit criteria:** in a unit-test harness, a small compilation with 2 sample classes + 1 md file produces expected registry output and expected diagnostics.

### Phase 3 — Head app scaffold (½ day)
- `gallery/infra/Reactor.Gallery.App/Reactor.Gallery.App.csproj` with the glob-based `<Compile Include="..\..\src\**\*.cs" />` model.
- `gallery/src/UseStateSample.cs` + `gallery/src/Hooks.md` — one real sample + one real doc to prove the pipeline.
- **Exit criteria:** clean build from a clean clone; no per-feature project files needed.

### Phase 4 — Shell + renderers (4–5 days)
- `Reactor.Gallery.Shell` project, `GalleryShell`, routing, search.
- `DocumentationRenderer` (markdown + sample embeds).
- `SampleRenderer` with options pane.
- `OptionsPaneRenderer` for the four built-in option types.
- `SourceViewer` with `#region sample` slicing + highlighting.
- **Exit criteria:** app runs, shows one sample end-to-end, options work, source viewer displays the region.

### Phase 5 — Selftest bridge + initial content (2–3 days)
- `Reactor.Gallery.SelfTestBridge` + host integration.
- 6–10 samples covering Hooks, Commanding, Navigation — each a single `.cs` in `gallery/src/`.
- Feature-area docs (`Hooks.md`, `Commanding.md`, `Navigation.md`) with prose + sample embeds.
- **Exit criteria:** gallery launches with multi-section nav; every sample passes selftest mount; CI runs the fixtures.

### Phase 6 — Content fill (ongoing)
- Additional samples for Styling, Input, Theming, Animation, Data, Localization, Devtools — each one `.cs` file, optionally one new `.md` per feature area.
- `[ReactorSampleOptionsPane]` custom-pane demo.
- `Reactor.TestApp` retirement audit.
- **Exit criteria:** every spec-documented feature area has at least one gallery sample.

### Total ballpark
~1.5–2 weeks of focused work to Phase 5. Phase 6 is continuous with feature development.
