# Spec 067 — Table-driven common modifiers

> Status: **proposed** (2026-11-25). Design only. No product code changes with this spec.
> Builds on #1706, which moved `ApplyModifiers` into `src/Reactor/Core/Reconciler.Modifiers.cs`.
> File and line references are to `azchohfi-split-reconciler-partials` @ `de6da655f` unless noted.
> Related: spec 034 (modifier buckets), spec 047 / 048 (control descriptors), spec 058 (wrapper
> generator), spec 060 / 061 (analyzer suite), spec 066 (composition-visual transforms).

## Table of contents

- [§1 Problem](#1-problem)
- [§2 Goals and non-goals](#2-goals-and-non-goals)
- [§3 Inventory: the shapes modifiers come in](#3-inventory-the-shapes-modifiers-come-in)
- [§4 Design](#4-design)
- [§5 Ordering](#5-ordering)
- [§6 Interactions with the rest of the reconciler](#6-interactions-with-the-rest-of-the-reconciler)
- [§7 Performance](#7-performance)
- [§8 AOT and trimming](#8-aot-and-trimming)
- [§9 What the drift tests become](#9-what-the-drift-tests-become)
- [§10 Test plan](#10-test-plan)
- [§11 Rollout](#11-rollout)
- [§12 Alternatives considered](#12-alternatives-considered)
- [§13 Open questions](#13-open-questions)
- [§14 Defects found while writing this spec](#14-defects-found-while-writing-this-spec)

---

## §1 Problem

A common-element modifier (`.Padding(...)`, `.IsEnabled(...)`, `.Foreground(...)`, …) is one
fact about the framework: *this record field is written to this WinUI property, on these control
types, cleared with `ClearValue` when it goes away*. Today that fact is hand-copied into a dozen
places, and `AGENTS.md:343` warns contributors that "a new common-element modifier touches every
seam".

### §1.1 The seams

| # | Seam | Where | What it encodes |
|---|---|---|---|
| 1 | Storage | `ElementModifiers` (`src/Reactor/Core/Element.cs:2142`), `LayoutModifiers` (`:2531`), `VisualModifiers` (`:2594`), `AccessibilityModifiers` (`:2636`) | the field, its bucket, the shim `init` that writes into the bucket |
| 2 | Merge | `ElementModifiers.Merge` (`Element.cs:2439`), `LayoutModifiers.Merge`, `VisualModifiers.Merge` | `other.X ?? X`, one line per field |
| 3 | Skip equality | `ModifiersEqualCore` (`Element.cs:1280`), `VisualModifiersEqual` (`:1401`), `ModifiersVisiblyEqual` | which fields block the shallow skip and how they compare (value, brush colour, reference, presence) |
| 4 | Apply | `ApplyModifiers` (`Reconciler.Modifiers.cs:237-780`), `ApplyAccessibilityModifiers` (`Reconciler.Accessibility.cs:167`), `ClearRemovedTransformModifiers` (`Reconciler.Modifiers.cs:212`), `ApplyModifierReferenceEdges` (`Reconciler.ReferenceEdges.cs:166`) | diff guard, receiver type gate, write, matching type-gated `ClearValue` |
| 5 | Fluent API | `ElementExtensions` (`src/Reactor/Elements/ElementExtensions.cs`) | the public overloads |
| 6 | Diagnostics | `AppliedModifierMap.Entries` (`src/Reactor/Core/Diagnostics/AppliedModifierMap.cs:82`) | modifier → owning WinUI property per control type |
| 7 | ThemeRef path | `GetDependencyPropertyName` (`Reconciler.ThemeBindings.cs:568`) | which receivers a `ThemeRef` brush overload reaches |
| 8 | Pool reset | `ElementPool.CleanElement` (`ElementPool.cs:279`), `PoolableTypes` (`:79`) | which modifier-backed DPs are cleared on pool return, on which receivers |
| 9 | Visual lane / restore | `DeclaresTransform` (`Reconciler.Pooling.cs:128`), `HasRestorableModifier` (`Reconciler.Modifiers.cs:101`) | which visual fields take the composition visual, and their identity values |
| 10 | Analyzer | `ModifierTable` (`src/Reactor.Analyzers/ModifierTable.cs:278`), `GateOnlyInReconciler` (`:612`), `NoOpModifierAnalyzer.DescriptorAppliedModifiers` (`NoOpModifierAnalyzer.cs:159`) | receiver gates, theme gates, pool-reset gates, poolable type names |
| 11 | Agent skill prose | `plugins/reactor/skills/reactor-build-and-check/SKILL.md` | the `REACTOR_MOD_003` gate list, in English |

The receiver list for `Padding` alone (`Control`, `Border`, `StackPanel`, `Grid`, `RelativePanel`,
`TextBlock`) appears in **eight** places: the write chain (`Reconciler.Modifiers.cs:332-337`), the
clear chain (`:345-350`), the inline-overlay base `switch` (`:309-317`), `AppliedModifierMap.PaddingOwner`
(`AppliedModifierMap.cs:40-47`), `ModifierTable.ControlBorderGridStackRelativeText` and its
pool-reset narrowing `ControlBorderGridStackText`, the `CleanElement` receiver chain
(`ElementPool.cs:394` onward), and the skill prose. `BorderBrush` adds a ninth copy in
`GetDependencyPropertyName`.

### §1.2 Drift is policed after the fact

Because nothing derives these copies from each other, the repo has grown an impressive set of
source-parsing tests that read the hand-written `if` chains with Roslyn and compare them against
each other:

| Test file | Lines | What it re-derives |
|---|---:|---|
| `tests/Reactor.Tests/AnalyzerTests/ModifierTableIntegrityTests.cs` | 1316 | `ControlGate` vs the `fe is T` tests in `ApplyModifiers`, descriptor `Customize` hooks vs `DescriptorAppliedModifiers`, attached setters vs DPs |
| `ModifierUnsetClearValueTests.cs` | 1443 | every unset arm uses `ClearValue` (#952), exists at all (#986), has a type gate matching its write; `CleanElement` releases every modifier-backed DP; pool-reset gates match the poolable intersection (#1051) |
| `CleanElementScan.cs` + `CleanElementScanIntegrityTests.cs` | 563 + 348 | every reset in `CleanElement`, attributed to its receiver (#1193) |
| `PoolResetSetConsistencyTests.cs` | 811 | `REACTOR_POOL_001` rows vs `CleanElement` |
| `ModifierGateIdentifierTests.cs` + `ModifierGateSource.cs` | 393 + 269 | the text matcher used where no typed property is reachable (#1062) |
| `ModifierGateProseParityTests.cs` | 161 | skill prose vs `ModifierTable` |

That is about 5,300 lines whose job is to prove that hand-copied lists agree. They work, and they
have caught real bugs, but each one is bounded by what it knows to parse. Nothing parses
`ModifiersEqualCore` for completeness, and the same class of defect keeps landing there:
`IsDragRegion` was missing from `Merge` and from `ModifiersEqual` (spec 059, regression tests in
`tests/Reactor.Tests/ElementModifiersCoverageTests.cs`), `OnUpdateAction` was missing from
`ModifiersEqual` (#976), and `XYFocusKeyboardNavigation` is missing from it today (§14).

### §1.3 Controls already solved this

Controls moved from hand-written `MountXxx` / `UpdateXxx` pairs to declarative
`ControlDescriptor<TElement, TControl>` (spec 047 §6, spec 048). The descriptor entry
`OneWayClearValuePropEntry` (`src/Reactor/Core/V1Protocol/Descriptor/PropEntry.cs:113-169`) is
*exactly* the common-modifier contract: write on set, skip on equal, `ClearValue(dp)` on unset,
never write a default. Modifiers are the last large hand-written diff in the reconciler.

---

## §2 Goals and non-goals

**Goals**

- **G1. One declaration per modifier.** Name, storage bucket, value type, receivers (ordered),
  target property and DP owner per receiver, clear policy, theme receivers, skip equality, write
  equality, merge policy, ordering phase, composition channel. Nothing else restates these.
- **G2. Derive, don't police.** Apply arms, unset arms, skip equality, `Merge`, `AppliedModifierMap`,
  the `ThemeRef` DP map, the modifier half of `CleanElement`, and the analyzer's derived gates
  come from the declaration.
- **G3. Zero hot-path cost.** The derived apply code has the same shape as today's: straight-line
  diff guards, `is` type tests, direct property sets. No delegates, closures, boxing or
  allocations are introduced on `ApplyModifiers`.
- **G4. AOT and trim clean.** No reflection in product code. `IsAotCompatible` stays true and the
  core library's trim warnings-as-errors keep passing.
- **G5. Incremental and revertible.** Each migration step is one PR that changes no observable
  behaviour, is guarded by tests written *before* the step, and can be reverted alone.
- **G6. One source for the analyzer.** `src/Reactor.Analyzers` (`netstandard2.0`, cannot
  reference Reactor) reads the same declaration, not a copy.

**Non-goals**

- Changing any modifier's behaviour, receivers or apply order. Every layer in §11 is
  behaviour-preserving. Behaviour fixes (§14) ship separately.
- Changing public API. The fluent extensions, the modifier records and their properties stay.
  Records may gain `partial`, which is source- and binary-compatible.
- Third-party extensible modifiers. `ElementModifiers` is a closed record today and stays so.
- Rewriting the irregular modifiers (§3, rows E and F). They keep their hand-written logic and
  gain a declaration for their *metadata* only.
- Replacing `ControlDescriptor`. Descriptors describe per-control props. This spec describes the
  common bag every element carries.

---

## §3 Inventory: the shapes modifiers come in

Read from `ApplyModifiers` (`Reconciler.Modifiers.cs:237-780`). The unset-arm floors in
`ModifierUnsetClearValueTests.cs:60-63` record 37 set arms and 36 unset arms there, plus 12 / 12 in
`ApplyAccessibilityModifiers`.

| Row | Shape | Members | Notes |
|---|---|---|---|
| A | Simple, on `FrameworkElement` / `UIElement` | `RequestedTheme`, `Width`, `Height`, `MinWidth`, `MinHeight`, `MaxWidth`, `MaxHeight`, `HorizontalAlignment`, `VerticalAlignment`, `IsTabStop`, `IsHitTestVisible`, `AccessKey`, `XYFocusKeyboardNavigation` | `if (set && changed) fe.X = v; else if (!set && wasSet) fe.ClearValue(XProperty);` |
| B | Simple, type-gated | `IsEnabled`, `ElementSoundMode`, `TabIndex`, `HorizontalContentAlignment`, `VerticalContentAlignment` (`Control`); `CornerRadius` (5 receivers); `Foreground`, `FontFamily`, `FontSize`, `FontWeight` (`Control`, `TextBlock`) | same arms behind an ordered `is` chain, with a matching `is` chain on the clear side (#986) |
| C | Converted | `IsVisible` (`bool` → `Visibility`) | a pure value conversion before the write |
| D | Attached | `AutomationName`, `AutomationId`, `HeadingLevel` (`AutomationProperties`), `IsDragRegion` (`TitleBar`), `ToolTipPlacement` (`ToolTipService`) | static `SetX(fe, v)` and `ClearValue(Owner.XProperty)` |
| E | Composite | `Margin` + `MarginInlineStart/End`; `Padding` + `PaddingInlineStart/End`; `BorderThickness` + `BorderInlineStart` | several fields resolve, against `FlowDirection` and the previous overlay (#965), to one DP |
| F | Hand-coded with cross-system state | `Opacity`, `Scale`, `Rotation`, `Translation`, `CenterPoint`, `TransformOrigin` (composition, remaining-owner restore, spec 066); `BorderBrush` (interaction-state tracker, `:499-507`); `Background` (`keepRemovedBackground`, `:581`); `ToolTip` / `RichToolTip`, `AttachedFlyout`, `ContextFlyout` (mount child elements) | real logic, not a table row |
| G | Behaviour, not a property write | 20 routed-event handlers, 4 gestures, 2 drag-drop configs, `OnMountAction`, `OnUpdateAction`, `OnUnmountAction`, `Ref`, the four `XYFocus*Ref`, `ToolTipPlacementTargetRef`, `OnAccessKeyDisplayRequested`, `Backdrop` (read by the host) | these still need merge and skip-equality declarations |
| H | Accessibility sub-record | the 12 `AccessibilityModifiers` fields | same A/D shapes inside `ApplyAccessibilityModifiers` |

Rows A–D are 24 of the property-writing modifiers on the main bag, and all of them are mechanical.
Row E is mechanical around one hand-written resolver per family. Row F is not mechanical and this
spec does not pretend it is.

---

## §4 Design

### §4.1 Shape of the solution

```mermaid
flowchart LR
    S["ModifierSchema.cs<br/>(data only, netstandard2.0)"]
    T["tools/Reactor.ModifierGen"]
    G1["Reconciler.Modifiers.Generated.cs"]
    G2["Element.Modifiers.Generated.cs<br/>(ModifiersEqual, Merge)"]
    G3["AppliedModifierMap.Generated.cs"]
    G4["ElementPool.Generated.cs<br/>(modifier clears, layer 5)"]
    A["Reactor.Analyzers<br/>ModifierTable"]
    X["Reactor.Tests<br/>freshness + schema invariants"]
    S --> T
    T --> G1 & G2 & G3 & G4
    S -. linked source .-> A
    S -. linked source .-> X
    T -. emitter linked .-> X
```

1. **A schema file** declares every modifier as plain data: strings, enums and arrays. It is
   `netstandard2.0`-safe, so it compiles into the analyzer as well as everywhere else (§4.2).
2. **A generator tool** reads the schema and emits checked-in C# into `src/Reactor` (§4.3).
3. **A freshness test** in `Reactor.Tests` runs the same emitter in-process and byte-compares its
   output against the checked-in files, the way the search index is gated today (`AGENTS.md`,
   "ReactorGallery search index").
4. **The analyzer** links the schema file and derives its gates from it (§4.5).

### §4.2 The schema

Location: `src/Reactor.Analyzers/Modifiers/ModifierSchema.cs`. The analyzer owns it because the
analyzer is the strictest consumer (`netstandard2.0`, no Reactor reference). The tool and
`Reactor.Tests` link it with `<Compile Include="…" Link="…" />`, the same pattern
`Reactor.Wrappers.Generator.csproj:31` uses for `SourceMapShared.cs`.

Illustrative shape (names are not final):

```csharp
namespace Microsoft.UI.Reactor.Analyzers.Modifiers;

internal enum ModifierBucket { Root, Layout, Visual, Accessibility }

// Contiguous blocks in apply order. §5 lists them and the constraints between them.
internal enum ModifierPhase { Theme, Box, Size, Alignment, Composition, Visibility, ToolTip, Flyout,
    ControlState, Border, Brush, Automation, Focus, Accessibility, Typography, Behavior, Lifecycle }

internal enum ApplyShape { Simple, Converted, Attached, Composite, HandCoded, Behavior }

// How the shallow-skip gate compares the field (ModifiersEqualCore).
internal enum SkipEquality { Value, BrushValue, FontFamilyValue, Reference, PresenceOnly, NotCompared }

// How the apply arm decides a field changed. Brushes and FontFamily are compared by reference
// here today but by value in the skip gate (§6.6), and the schema has to say both.
internal enum WriteEquality { Value, Reference }

internal sealed class ModifierReceiver
{
    public string Type;          // "Microsoft.UI.Xaml.Controls.Control"
    public string Property;      // "Padding"
    public string DpOwner;       // "Microsoft.UI.Xaml.Controls.Control" → Control.PaddingProperty
}

internal sealed class ModifierSlot
{
    public string Name;                        // record property, and the fluent modifier name
    public ModifierBucket Bucket;
    public string ClrType;                     // "Microsoft.UI.Xaml.Thickness"
    public bool IsReferenceType;
    public ModifierPhase Phase;
    public ApplyShape Shape;
    public ModifierReceiver[] Receivers;       // ordered; empty for Behavior
    public string[] ThemeReceivers;            // ThemeRef overload reach, when it differs (§6.4)
    public string AttachedOwner;               // row D
    public string Converter;                   // row C: a static method in ModifierConverters
    public string[] CompositeFields;           // row E: "MarginInlineStart", "MarginInlineEnd"
    public string HandCodedMethod;             // rows E/F: the Reconciler method that owns the arm
    public SkipEquality Skip; public string SkipReason;   // reason required for NotCompared
    public WriteEquality Write;
    public bool CallbackSlot;                  // ignored by ModifiersVisiblyEqual
    public bool GateInherent;                  // §4.5: DP exists only on the sole receiver
    public string CompositionChannel;          // row F: AnimateProperty name, spec 066
    public string IdentityValue;               // row F: value that does not take the visual
}

internal static partial class ModifierSchema
{
    internal static readonly ModifierSlot[] Slots =
    {
        Simple("RequestedTheme", Layout, "Microsoft.UI.Xaml.ElementTheme", Phase.Theme).OnFrameworkElement(),
        Composite("Margin", Layout, Thickness, Phase.Box,
            fields: new[] { "MarginInlineStart", "MarginInlineEnd" }, method: "ApplyMarginFamily").OnFrameworkElement(),
        Composite("Padding", Layout, Thickness, Phase.Box,
            fields: new[] { "PaddingInlineStart", "PaddingInlineEnd" }, method: "ApplyPaddingFamily")
            .On(Control, Border, StackPanel, Grid, RelativePanel, TextBlock),
        Simple("Width", Layout, "double", Phase.Size).OnFrameworkElement(),
        // … Height … MaxHeight, alignments …
        Simple("IsEnabled", Root, "bool", Phase.ControlState).On(Control).GateInherent(),
        Converted("IsVisible", Layout, "bool", Phase.Visibility, property: "Visibility", converter: "ToVisibility"),
        Attached("AutomationName", Root, "string", Phase.Automation,
            owner: "Microsoft.UI.Xaml.Automation.AutomationProperties", property: "Name"),
        HandCoded("Opacity", Visual, "double", Phase.Composition, method: "ApplyOpacityModifier")
            .Composition(channel: "Opacity", identity: "1"),
        HandCoded("BorderBrush", Visual, Brush, Phase.Border, method: "ApplyBorderBrushModifier")
            .On(Control, Border, Grid, StackPanel, RelativePanel).Equality(skip: BrushValue, write: Reference),
        Behavior("OnMountAction").NotCompared("fires once at mount (oldM is null); a later change is inert"),
        Behavior("OnPointerPressed").Equality(skip: Reference).Callback(),
        // …
    };
}
```

Rules the schema enforces about itself (unit tests over the data, §10.1):

- Every public settable property of `ElementModifiers`, `LayoutModifiers`, `VisualModifiers` and
  `AccessibilityModifiers` is exactly one slot, or one slot's `CompositeFields` entry. Checked by
  reflection in `Reactor.Tests`, so a new record field with no declaration fails the build of the
  test suite rather than shipping half-wired.
- `NotCompared` requires a reason. That makes "this field does not block the skip" a decision
  someone wrote down, not an omission (§14).
- Every receiver type and DP owner resolves to a real type with a real `XProperty` (checked by
  reflection in `Reactor.Tests`; `typeof` of a WinUI projection type is safe headless).
- Phase order is monotonic in array order, and the explicit constraints in §5 hold.

### §4.3 Generated code, checked in

`tools/Reactor.ModifierGen` (`net10.0` console, alongside `tools/Reactor.SearchIndex`) emits:

| Output | Replaces | Layer |
|---|---|---|
| `src/Reactor/Core/Reconciler.Modifiers.Generated.cs` | rows A–D arms of `ApplyModifiers`, the call sites of rows E–F in phase order | 3 |
| `src/Reactor/Core/Element.Modifiers.Generated.cs` | `ModifiersEqualCore`, `VisualModifiersEqual`, the three `Merge` bodies | 2 |
| `src/Reactor/Core/Diagnostics/AppliedModifierMap.Generated.cs` | `AppliedModifierMap.Entries` and its owner helpers | 2 |
| `src/Reactor/Core/Reconciler.ThemeBindings.Generated.cs` | `GetDependencyPropertyName` | 2 |
| `src/Reactor/Core/ElementPool.Generated.cs` | the modifier-backed clears in `CleanElement`, and `PoolableTypes` | 5 |
| `src/Reactor/Core/Reconciler.Pooling.Generated.cs` | `DeclaresTransform`, `HasRestorableModifier`'s field list | 6 |

The emitter writes the code a careful human writes today. For a row A slot:

```csharp
if (m.Width.HasValue && m.Width != oldM?.Width) fe.Width = m.Width.Value;
else if (!m.Width.HasValue && oldM?.Width.HasValue == true) fe.ClearValue(FrameworkElement.WidthProperty);
```

For a row B slot the write chain and the clear chain come from the same receiver list, so they
cannot disagree (the #986 shape), and the clear side re-tests the type under its own pattern
variable as `ElementSoundMode` does today (`Reconciler.Modifiers.cs:618-627`):

```csharp
if (m.CornerRadius.HasValue && m.CornerRadius != oldM?.CornerRadius)
{
    if (fe is WinUI.Control c0) c0.CornerRadius = m.CornerRadius.Value;
    else if (fe is WinUI.Border c1) c1.CornerRadius = m.CornerRadius.Value;
    // … Grid, StackPanel, RelativePanel
}
else if (!m.CornerRadius.HasValue && oldM?.CornerRadius.HasValue == true)
{
    if (fe is WinUI.Control c0) c0.ClearValue(WinUI.Control.CornerRadiusProperty);
    // …
}
```

The emitter has exactly one unset template, and it emits `ClearValue`. "Unset writes a default"
(#952) becomes unrepresentable for rows A–D rather than something a test scans for.

Generated files are named `Reconciler.*.cs` on purpose. `ReconcilerSource`
(`tests/Reactor.Tests/AnalyzerTests/ReconcilerSource.cs:19-33`) parses every `Reconciler.*.cs`, so
the existing Roslyn drift tests keep running against the generated arms throughout the migration
(§9). That is the main reason this spec prefers checked-in output over an in-build source
generator (§12.3).

### §4.4 What stays hand-written

- **Fluent extensions.** They are public API with overloads, defaults, docs and `[Obsolete]`
  shims. The schema test asserts that each slot has at least one generic
  `T Name<T>(this T el, …)` in `ElementExtensions` (the check
  `ModifierTableIntegrityTests.DeclaredGenericModifiers` already does by reflection).
- **Record declarations.** The properties, shims and XML docs stay in `Element.cs`. The records
  become `partial` so `Merge` can be generated.
- **Rows E and F bodies.** `ApplyMarginFamily`, `ApplyPaddingFamily`, `ApplyBorderThicknessFamily`,
  the composition-channel methods, the tooltip / flyout arms, events, gestures, drag-drop and
  lifecycle callbacks. The generated `ApplyModifiers` calls them at their declared positions.
- **`ClearRemovedTransformModifiers`.** It runs before handlers, not inside `ApplyModifiers`
  (`Reconciler.Update.cs:316-317`), and its order is load-bearing (TransformOrigin after
  CenterPoint, `Reconciler.Modifiers.cs:229-232`). Its field list can be generated in layer 6; its
  body stays.

### §4.5 The analyzer reads the same data

`ModifierTable` keeps its editorial content: `DeliberatelyExcluded` with reasons, the attached
table, the type-specific `elementTypes` rows, and the `poolReset` severity choice, which is an
editorial claim and not a derived fact (`ModifierTable.cs:466-482` documents why `IsHitTestVisible`
and the fonts stay `REACTOR_MOD_002` per #1195 even though `CleanElement` clears them). What it
stops hand-copying:

| Analyzer fact | Derived from |
|---|---|
| `ModifierInfo.ControlGate` | slot receivers, unless `GateInherent` (then `null`) |
| `ModifierTable.GateOnlyInReconciler` | slots with `GateInherent`, plus composite fields, with their reason strings moved into the schema |
| `ModifierInfo.ThemeControlGate` | `ThemeReceivers` when it differs from the receivers |
| `ModifierInfo.PoolResetGate` | receivers ∩ the schema's poolable types, when narrower than the receivers |
| `ModifierTable.PoolableTypeNames` | the schema's poolable types (layer 5 makes `ElementPool.PoolableTypes` generated from the same list) |

`GateInherent` is the one new concept. Today `IsEnabled` and the content-alignment pair are
`Control`-gated in `ApplyModifiers` but carry a `null` gate in the analyzer, because in the `.Set`
direction WinUI only declares those DPs on `Control` (`ModifierTable.cs:612-620`). That fact lives
in two hand-maintained places, a `null` and a `GateOnlyInReconciler` row. The schema states it
once.

`ModifierTable` computes its derived arrays once in its static constructor. That is a one-time
allocation per analyzer load, the same order of cost as the dictionaries it builds today.

---

## §5 Ordering

Order inside `ApplyModifiers` is behaviour. The schema's array order **is** the emitted order, and
layer 0 (§11) pins today's order before anything is generated. The phases and the constraints
between them, each traced to the code that depends on it:

| Constraint | Why | Source |
|---|---|---|
| `RequestedTheme` first | `ThemeRef` bindings resolve against it, and `ApplyThemeBindings` runs after `ApplyModifiers` | `Reconciler.Modifiers.cs:244-249`, `Reconciler.Update.cs:491` vs `:533`, `Reconciler.Mount.cs:200` vs `:241` |
| `Margin` / `Padding` / `BorderThickness` families read `fe.FlowDirection` | `FlowDirection` comes from setters, which run before modifiers | `Reconciler.Modifiers.cs:262`, `Reconciler.Update.cs:476-481` |
| `TransformOrigin` before `CenterPoint` on apply | an origin that starts takes the current offset, then a `CenterPoint` change writes or animates it | `Reconciler.Modifiers.cs:417-427`, spec 066 §8.2 |
| `CenterPoint` reset before `TransformOrigin` deactivate on removal | the restored static pivot is the remaining offset | `Reconciler.Modifiers.cs:229-232` |
| transform removals before handlers and setters | a removal must not erase a value a setter or shared target writes in the same update | `Reconciler.Update.cs:316-317`, spec 066 §6.3 |
| `BorderBrush` after interaction-state tracking | a hovered control keeps its interaction brush and only updates the restore value | `Reconciler.Modifiers.cs:499-507` |
| `Background` unset honours `keepRemovedBackground` | `BorderElement`'s descriptor may already have released or replaced it | `Reconciler.Update.cs:486-490`, `Reconciler.Registration.cs:247` |
| event, gesture, drag-drop wiring after property writes | handlers see the applied state | `Reconciler.Modifiers.cs:701-709` |
| `OnMountAction` / `OnUpdateAction` after every property write | the callback observes a fully applied control, under `OpacityRestartProbe` | `:712-735` |
| `Ref` then reference edges last | a ref points at a fully applied control; edges may resolve against it | `:763-779` |

Phases are coarse blocks. Within a block the schema keeps today's line order exactly, because
"probably independent" is not something a behaviour-preserving refactor gets to assume. Reordering
inside a block, for example to group by receiver, is a follow-up with its own perf data, not part
of this spec.

---

## §6 Interactions with the rest of the reconciler

### §6.1 Echo suppression

Modifiers are written outside every echo mechanism. `ApplySetters` runs inside the descriptor
handler under its echo-suppression scope (`DescriptorHandler.cs:140`, `:228`), and `ApplyModifiers`
runs after the handler returns, unsuppressed (`Reconciler.Update.cs:476-491`). No common modifier targets a property that any built-in
descriptor declares `Controlled` or `HandCodedControlled` (the spec 047 §8.3 list is value props:
`Text`, `IsOn`, `SelectedIndex`, `Value`, …). The generated code keeps both facts: it introduces no
suppression scope and it targets only the DPs the hand-written arms target. A schema test can make
the second fact permanent once descriptors expose their target DP (§13, Q3).

Precedence with descriptor props is unchanged: modifier-after-prop, so a modifier wins over an
element-record prop writing the same DP (spec 047 §6.2). Two descriptors consume a common modifier
themselves, `RichTextBlockElement` reading `Padding` (`NoOpModifierAnalyzer.cs:159-163`) and
`BorderElement` releasing `Background` (`MarkBackgroundRemovalHandled`). The schema records both as
`DescriptorOwned` entries so `REACTOR_MOD_003` and `AppliedModifierMap` stop treating them as
special cases. The descriptor-hook scan in
`ModifierTableIntegrityTests.Descriptor_Applied_Common_Modifiers_Match_The_Analyzer_Exception_List`
stays as the check that the list is complete.

### §6.2 Pooling

The pool contract is: `CleanElement` returns a control to its fresh state with `ClearValue`
(`ElementPool.cs:296-300`), and mount calls `ApplyModifiers(fe, null, m)`, so no unset arm ever
runs at mount. The generated code keeps both halves. It never emits a clear when `oldM` is null,
and in layer 5 the modifier half of `CleanElement` is generated from *receivers ∩ poolable types*,
which is the intersection `Every_Poolable_Gated_Receiver_Is_Released_By_CleanElement` derives
today. The non-modifier resets in `CleanElement` (tag, refresh snapshots, event handlers, theme
bindings, facades, transitions, `Clip`, `FlowDirection`, `RenderTransform`) stay hand-written.

Event wiring on poolable controls goes through the one-time trampoline and
`ReactorState.ControlEventState` survives rent and return (issue #114). Row G slots are declared
for merge and equality only. Their apply code is untouched.

The visual lane (spec 066 §7) depends on `DeclaresTransform` (`Reconciler.Pooling.cs:128-133`)
knowing which visual fields take the composition visual and what their identity values are. The
schema's `CompositionChannel` and `IdentityValue` carry exactly that, so layer 6 can generate it
together with `HasRestorableModifier`'s field list (`Reconciler.Modifiers.cs:101-106`), which today
is a second copy of the same five-field set.

### §6.3 Shared targets and remaining owners

Decorators, keyed memos and flyout decorators can share one control. Composition modifiers
resolve the surviving declaration across that chain (`FindRemainingModifierOwner`,
`Reconciler.Modifiers.cs:136`, spec 066 §6). This is row F logic and stays hand-coded. Rows A–D
have no remaining-owner semantics today, and the generated arms keep it that way. Giving them
remaining-owner semantics would be a behaviour change.

### §6.4 Theme bindings

The `ThemeRef` overloads of `Background`, `Foreground` and `BorderBrush` go through
`GetDependencyPropertyName` (`Reconciler.ThemeBindings.cs:568-580`), not `ApplyModifiers`, and
their reach differs: `Foreground` reaches `RichTextBlock` on the theme path but not on the plain
brush path. The analyzer models this as `ThemeControlGate` (`ModifierTable.cs:304-305`). The
schema's `ThemeReceivers` states it once, and layer 2 generates `GetDependencyPropertyName`'s
modifier rows from it. `PaneBackground` (SplitView) is not a common modifier and stays
hand-written.

### §6.5 Interaction states

`BorderBrush` defers to the interaction tracker when a state override is active
(`Reconciler.Modifiers.cs:499-507`, `Reconciler.InteractionStates.cs:69-70`). That stays a
hand-coded arm. Only its receivers and equality come from the schema.

### §6.6 Equality: skip gate vs apply arm

The skip gate compares brushes by colour and opacity (`BrushesEqual`, `Element.cs:1197`) and
font families by value (`FontFamiliesEqual`, `Element.cs:1221`), because fluent helpers allocate a fresh brush per
render. The apply arms compare the same fields by reference
(`Reconciler.Modifiers.cs:575, 589, 667`). So when something else forces an update, a value-equal
but fresh brush is rewritten. That is a harmless extra write, but it is a real difference between
two seams, and the schema records it as `Skip = BrushValue, Write = Reference` instead of leaving
it implicit. Unifying them is a perf question for a later change (§13, Q5).

---

## §7 Performance

`ApplyModifiers` runs on every mount of an element with modifiers and on every update the shallow
skip declines (`Reconciler.Update.cs:150-156`, `:491`). That includes the common grid case where a
cell's text changed but its modifiers did not: `ModifiersEqual` is evaluated only when
`ShallowEquals` holds, so a props change runs every arm.

Today each arm is two nullable compares and a branch, inlined in one method. A runtime
interpreter, one `PropEntry`-style object per slot like `ControlDescriptor` uses, would add an
indirect call and two delegate reads per slot per update. Spec 047 §12.3 puts an interface
dispatch at 5–10 ns. With roughly 45 arms between the main bag and the accessibility bag, that is
about 0.2–0.45 µs per element per update before any WinUI write, multiplied by every visible cell
on every frame of the stress harness. That is why this spec generates straight-line code instead
(§12.2).

Requirements on every layer:

- **No new allocations, delegates, closures or boxing on `ApplyModifiers`.** The generated arms
  use the field types directly (`Thickness?`, `double?`, enums), so `EqualityComparer<T>` is never
  involved. Brush and string arms keep their existing `ReferenceEquals` / `==`.
- **Same shape as today.** The generated code for a migrated family is reviewed side by side with
  the hand-written arms it replaces in the same PR. The diff for that file is the review artifact.
- **Measured, not argued.** Each layer from 2 on runs the `perf-compare` skill
  (`.github/skills/perf-compare/SKILL.md`, `StressPerf.ReactorOptimized` vs a `main` worktree,
  interleaved) and records Renders/sec, Avg Reconcile ms, Avg Diff ms and Avg Memory MB in the PR.
  Note that the `/perf` workload uses a plain `Grid` with `UseMemoCellsByIndex`, not `VirtualList`
  (`tests/stress_perf/StressPerf.ReactorOptimized/Program.cs:176-203`), so it exercises the update path heavily.
  A regression outside noise blocks the layer.

Opportunities the schema makes cheap but that are **out of scope** here, because each changes the
cost model and needs its own numbers: a bucket-level early-out (`ReferenceEquals(oldM.Layout,
m.Layout)` skips the whole layout block), passing the already computed skip-gate result into
`ApplyModifiers`, and reordering arms by receiver so one `is` test serves several slots.

---

## §8 AOT and trimming

- Generated product code is ordinary C#: `is` patterns, property setters, static DP identifiers,
  static `AutomationProperties.SetX` calls. It is exactly what the hand-written arms contain, so
  `IsAotCompatible` and the trim-warnings-as-errors build are unaffected.
- No reflection is added to `src/Reactor`. Reflection lives in `Reactor.Tests` (schema invariants,
  §10.1), which already suppresses trim warnings, and in the generator tool, which never ships.
- The analyzer only gains a data file. No `netstandard2.0` restriction is touched: no
  `FrozenDictionary`, no newer BCL APIs. The analyzer already compiles collection expressions
  (`ModifierTable.cs:304-305`), so the shared file can use the same syntax.
- Headless tests cannot touch WinUI objects, and reading `FrameworkElement.MarginProperty`
  activates a WinUI factory. The schema therefore names DPs as strings, never as
  `DependencyProperty` values, so it is constructible in every consumer. Only the generated code
  touches real DPs, at the same points the hand-written code does.

---

## §9 What the drift tests become

The principle: a check that compares two hand-written copies is replaced by deriving one copy.
A check that guards *behaviour* stays, and moves from scanning source to running controls where
it can.

| Test | After the migration |
|---|---|
| `Every_ControlGate_Matches_The_Types_ApplyModifiers_Writes_To` | **Retargeted in layer 1**, compares the schema against `ApplyModifiers`. **Deleted in layer 3** for migrated families: the gate and the write come from one list. |
| `Every_ApplyModifiers_ControlGate_Is_Declared_Or_Explicitly_Recorded` | Becomes a schema test: every `GateInherent` slot is in `GateOnlyInReconciler`, by construction. |
| `Every_Unset_Arm_Clears_The_Dependency_Property` | **Kept** and keeps running, because the generated file is a `Reconciler.*.cs` and `ScannedMethods` gains the generated method names. For rows A–D it becomes a check of the emitter's single unset template. Rows E–F keep relying on it. |
| `Every_Unset_Arm_Actually_Resets_Something`, `Every_Diff_Guarded_Modifier_Has_An_Unset_Arm` | **Kept**, same reason. The per-method floors (`ModifierUnsetClearValueTests.cs:60-63`) are re-read from the assertion message as arms move between methods, as the file's own comment instructs. |
| `Every_Type_Gated_Write_Has_A_Matching_Type_Gated_Clear` | Redundant for rows A–D once generated. Kept for rows E–F. |
| `CleanElement_Releases_Every_Modifier_Backed_Dependency_Property`, `Every_Poolable_Gated_Receiver_Is_Released_By_CleanElement`, `Every_Pool_Reset_Gate_Matches_The_Poolable_Intersection` | **Layer 5** derives both sides from the schema, so these collapse into one schema test. `CleanElementScan` narrows to the hand-written non-modifier resets, which still need it. |
| `Analyzer_Poolable_Type_Mirror_Matches_ElementPool` | Deleted in layer 5. Both lists are the schema's. |
| `GetDependencyPropertyName_Agrees_With_ApplyModifiers_And_The_Table` | Deleted in layer 2. Both are generated. |
| `AppliedModifierMap_*` owner-resolution tests (`tests/Reactor.Tests/Diagnostics/ReactorDiagnosticsComponentsTests.cs:689-886`) | **Kept** as tests of the generated map. Its completeness becomes the §10.1 schema completeness test. |
| `Descriptor_Applied_Common_Modifiers_Match_The_Analyzer_Exception_List` | **Kept**. It scans descriptor hooks, which the schema does not own. |
| `ModifierGateProseParityTests` | **Kept**, compares the skill prose against the schema instead of `ModifierTable`. |
| `ModifierGateIdentifierTests` / `ModifierGateSource` | Shrinks with the number of places that still need a text matcher. Likely deleted once `ModifierTable.cs` no longer declares gate arrays by name. |
| Attached-property and type-specific `ModifierTable` checks | **Kept**. Those rows are editorial (§4.5). |

New checks replace what is deleted (§10). The net effect is fewer lines that re-derive copies and
more checks that run real controls.

---

## §10 Test plan

### §10.1 Schema invariants (headless, `Reactor.Tests`)

- **Completeness.** Reflection over the four modifier records: every public settable property is
  exactly one slot or one composite field, and vice versa.
- **Merge round trip.** For every slot, `a.Merge(b)` carries `b`'s value when set and keeps `a`'s
  when `b` is unset. Sample values per CLR type come from a small table in the test.
- **Skip equality.** For every slot not marked `NotCompared`, two bags differing only in that slot
  compare unequal under `ModifiersEqual`, and `ModifiersVisiblyEqual` agrees unless the slot is a
  callback. This is the check that would have caught `IsDragRegion` (spec 059), `OnUpdateAction`
  (#976) and `XYFocusKeyboardNavigation` (§14).
- **Receivers resolve.** Every receiver type, DP owner and `XProperty` name exists (`typeof` and
  static-property lookup by reflection, no activation).
- **Fluent coverage.** Every non-behaviour slot has a generic fluent modifier.
- **Ordering.** Phase monotonicity and the explicit §5 constraints.
- **Freshness.** The emitter, linked into the test project, regenerates every output in memory and
  byte-compares it with the checked-in file. Regenerate with
  `dotnet run --project tools/Reactor.ModifierGen`.

The completeness, merge and skip-equality tests do not need the schema to exist. Layer 0 adds them
against the hand-written records with an explicit exception list, which is the cheapest way to
close the §14 class of bug immediately.

### §10.2 Table-driven selftest (`Reactor.AppTests.Host`)

One fixture family, `ModifierSchema_*`, iterates the schema and, for every row A–D slot × every
receiver, mounts a representative concrete control and checks:

1. set → the control reports the value;
2. change → the new value;
3. unset → `ReadLocalValue` is `DependencyProperty.UnsetValue` (the #952 contract, behaviourally);
4. a style-provided value is visible again after unset (the `ModifierStyleUnsetRestore` shape,
   `tests/Reactor.AppTests.Host/SelfTest/Fixtures/ModifierEventFixtures.cs:784`);
5. on a poolable receiver, a rented control carries no local value from the previous renter (the
   `ModifierPoolClearValueOnRent` shape, `:999`);
6. on a non-receiver control type, the modifier writes nothing.

This fixture is written in **layer 1, against the hand-written code**, and must be green there
before any arm is generated. It is the characterization test that makes the later layers safe.
Its key oracles are mutation-checked once (turn one generated clear into a default write and
confirm step 3 fails; drop one receiver and confirm step 1 fails for it). Per the repo memory on
mutation testing, revert with `git restore`, not a backup copy, so MSBuild sees the change.

Fixtures are registered once in `SelfTestFixtureRegistry.Registrations`.

### §10.3 Existing suites

Every layer runs the unit tests, the selftests and the packaged selftests (CI does all three), plus
`perf-compare` from layer 2 on (§7). The existing `ModifierEvent_*`, `ModifierTransform_*`,
`XYFocus_*` (`ModifierReferenceFixtures`) and `PointerMod_*` selftests are unchanged and must stay green.

---

## §11 Rollout

One PR per layer, and within layer 3 one PR per family. Every PR is behaviour-preserving and
revertible on its own.

| Layer | Change | Product code touched | Gate |
|---|---|---|---|
| 0 | Behaviour-preserving extraction of `ApplyModifiers` into per-family private methods in today's order (proposed separately). Merge round-trip and skip-equality completeness tests against the hand-written records. Fix §14 in its own PR. | refactor only, plus the §14 fix | existing suites, new headless tests |
| 1 | Add `ModifierSchema.cs` and the generator tool emitting nothing yet. Schema invariant tests. Retarget `Every_ControlGate_Matches_…` to schema vs `ApplyModifiers`. Add the §10.2 selftest against hand-written code. Analyzer derives `ControlGate`, `ThemeControlGate`, `GateOnlyInReconciler` from the schema, with the analyzer test suite proving identical diagnostics. | analyzer only | analyzer tests unchanged, new selftest green |
| 2 | Generate the cold consumers: `ModifiersEqual(Core)`, `VisualModifiersEqual`, the three `Merge` bodies, `AppliedModifierMap`, `GetDependencyPropertyName`'s modifier rows. Records become `partial`. | `Element.cs`, diagnostics, theme bindings | freshness test, perf-compare |
| 3a–3e | Generate rows A–D one family per PR: sizing and alignment; automation, focus and tier-1 accessibility; typography; control state (`IsEnabled`, `ElementSoundMode`, `TabIndex`, content alignment, `CornerRadius`, `Foreground`); visibility and tooltip placement. | `Reconciler.Modifiers.cs` | §10.2 selftest, unset-arm scans, perf-compare |
| 4 | Composite families: the generated `ApplyModifiers` calls the hand-written resolvers. Only their receivers move to the schema. | `Reconciler.Modifiers.cs` | same |
| 5 | Generate `PoolableTypes` and the modifier half of `CleanElement`. Analyzer derives `PoolResetGate` and `PoolableTypeNames`. | `ElementPool.cs` | pool selftests, `CleanElementScan` narrowed |
| 6 | Composition metadata: generate `DeclaresTransform` and `HasRestorableModifier`'s field list. Apply stays hand-coded. | `Reconciler.Pooling.cs`, `Reconciler.Modifiers.cs` | `ModifierTransform_*`, visual-lane selftests |
| 7 | `AccessibilityModifiers` / `ApplyAccessibilityModifiers` get the layer 2–3 treatment. | `Reconciler.Accessibility.cs` | same as 3 |

After layer 3, the `AGENTS.md:343` field note changes from "touches every seam" to "add a schema
row, add the record property, add the fluent overload, run `tools/Reactor.ModifierGen`". The
contributor guide `docs/guide/extending-reactor-controls.md` gains a short "common modifiers" note
at the same time.

**Stop points.** Layers 0 and 1 are worth landing even if nothing after them does: layer 0 closes
the §14 bug class, and layer 1 gives the analyzer one source and adds the behavioural selftest.
If layer 2's perf numbers or review experience are bad, the project stops there with no loss.

---

## §12 Alternatives considered

### §12.1 Status quo plus more drift tests

Keep everything hand-written and add the §10.1 completeness tests. This is layer 0, and it is the
fallback if the rest is rejected. It closes the equality and merge holes but leaves the eight
receiver copies and the 5,300 lines of cross-checks in place, and every new modifier still needs
the same multi-file edit.

### §12.2 Runtime interpreter, like `ControlDescriptor`

One `PropEntry`-style object per modifier, a list walked by `ApplyModifiers`. Easiest to write and
debug, and the descriptor engine proves the pattern. Rejected for the apply path on cost (§7): it
adds an indirect call per slot per update on the hottest method in the reconciler, and its
equality would go through `EqualityComparer<T>`, whose non-boxing behaviour on CsWinRT-projected
structs such as `Thickness` would need verifying per type. It also makes the receiver chain a
loop over `Type` objects, where today it is a sequence of `isinst` instructions. Descriptors pay
this because per-control extensibility is their point. The common bag has no such need.

### §12.3 Roslyn incremental source generator

Emit the same code into `obj/` during the Reactor build. The repo already runs generators on
Reactor (`Reactor.Wrappers.Generator`, `Reactor.Localization.Generator`, `src/Reactor/Reactor.csproj:120-133`).
Not preferred, for four reasons:

- generated code is invisible in PR diffs, and §7 relies on reviewing the generated arms;
- `ReconcilerSource` reads `src/`, so every existing Roslyn drift test would go blind to the
  generated arms on day one, which removes the safety net exactly when it matters;
- the analyzer still could not consume it, so the shared data file is needed anyway;
- a generator adds build-time cost and an IDE-experience surface for a table that changes a few
  times a year.

The checked-in approach matches the repo's existing generated artifacts (search index,
`reactor.api.txt`, the doc pipeline), all of which are checked in and byte-compared. If the
project later wants in-build generation, the schema and emitter move into a generator with no
change to the declaration.

### §12.4 Attributes on the record properties as the declaration

`[Modifier(Phase.Box)] [Receiver(typeof(WinUI.Control))] public Thickness? Padding { get; init; }`.
Keeps the declaration next to the field, which is attractive. Rejected because the analyzer
cannot read attributes from an assembly it does not reference, so a second copy for the analyzer
would come back. It would also put internal attribute metadata on public records, which the API
index would have to filter.

### §12.5 Generate the fluent extensions too

Rejected. The overloads are hand-designed public API (`Margin(horizontal, vertical)`,
`Margin(left: 8)`, `Background(ThemeRef)`) with docs, defaults and obsolete shims. A completeness
test (§10.1) is the right amount of coupling.

### §12.6 Replace the records with a property bag

A `Dictionary<ModifierKey, object>` or similar would make the table the storage. Rejected. It
boxes every value, allocates per element, loses the typed public API, and undoes the spec 034
bucket design that keeps the common case to one or two small records.

---

## §13 Open questions

1. **Schema location.** `src/Reactor.Analyzers/Modifiers/` (owned by the strictest consumer, as
   proposed) or a neutral `src/Shared/` folder. Either works with linked source.
2. **Accessibility in scope for v1?** Layer 7 is optional. Its 12 arms are the same shapes, so it
   is cheap, but the spec can ship without it.
3. **Descriptor target DPs.** Descriptors do not expose the DP a `PropEntry` writes. Exposing it
   would let a schema test prove that no modifier targets a controlled prop (§6.1). Worth a small
   descriptor change, or is the current invariant documented enough?
4. **`poolReset` severity.** It stays editorial here. Whether the 18 existing `REACTOR_POOL_001`
   rows should keep the stronger claim is open on #1193, and the schema should not decide it.
5. **Skip vs write equality for brushes and fonts** (§6.6). Comparing by value on the apply side
   too would remove redundant writes but adds a colour read per brush arm. Needs perf data.
6. **Generator invocation.** A dedicated `tools/Reactor.ModifierGen`, or a `mur` subcommand next to
   `mur --regen-api`.
7. **Interaction with the parallel `ApplyModifiers` extraction.** Layer 0 assumes that refactor
   lands first, or is folded into layer 0. The schema's method names for rows E–F should match
   whatever names it picks.

---

## §14 Defects found while writing this spec

1. **`XYFocusKeyboardNavigation` does not block the shallow skip.** It is a root field on
   `ElementModifiers` (`Element.cs:2413`), merged (`:2503`) and applied with a set and an unset arm
   (`Reconciler.Modifiers.cs:655-658`), but `ModifiersEqualCore` (`Element.cs:1280-1390`) never
   compares it and it is not in a bucket the record equality covers. An element whose only change
   is `.XYFocusKeyboardNavigation(Enabled)` → `Disabled`, with otherwise equal props, takes the
   skip path at `Reconciler.Update.cs:150-156` and keeps the old mode on the control. A throwaway
   headless probe confirmed the gate half: `Element.ModifiersEqual` returned `true` for two bags
   differing only in `XYFocusKeyboardNavigation`, while the same probe on `IsTabStop` (the positive
   control) returned `false`. The end-to-end stale value on a live control is inferred from the skip
   path, not yet observed in a selftest. The fix is one `&&` line plus a regression test in the shape of
   `ModifiersEqual_DistinguishesIsDragRegion`
   (`tests/Reactor.Tests/ElementModifiersCoverageTests.cs`), and it should ship on its own,
   ahead of this spec. The §10.1 skip-equality test catches the whole class.
2. **Brush and font equality differ between the skip gate and the apply arms** (§6.6). Not a
   defect. It causes redundant writes only, and is recorded here so nobody "fixes" one side
   without the other.
