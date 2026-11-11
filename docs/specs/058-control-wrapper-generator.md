# Control Wrapper Source Generator — Design Proposal

## Status

**API naming update:** references below to the Element-valued
`Expander.HeaderTemplate` use its historical name. This slot is now
`Expander.HeaderContent`, and still writes to native `Expander.Header`. See the
[migration notes](../guide/layout.md#migration-expanderheadertemplate-renamed-to-headercontent).

**Proposed — design v0 (2026-06-08). Prototype exists; design is open.** A working spike already lives in the tree (`src/Reactor.Wrappers.Generator`, consumed by `samples/apps/wct-controls`). This document exists so we can settle the *shape* of the feature before we keep iterating on the implementation.

**The decisions below are deliberately left open for the maintainer (@azchohfi).** Where this doc lists options it states a recommendation, but the recommendation is not a decision — §7 is the decision surface and every row is `TBD`.

### North star (the one fixed requirement)

> **Anything the hand-authored declarative API can express, a source generator should be able to express too.**

Concretely: the generated path must be able to reach full parity with the `ControlDescriptor<TElement, TControl>` authoring surface (one-way props, conditional props, **controlled/two-way props with echo suppression**, coercion, events, single-content slots, panel/items children, pooling, setters) — not just the leaf/content-control subset the prototype covers today. We get there in phases (§11), but no descriptor capability is declared permanently out of scope.

### Resolved decisions (2026-06-08, @azchohfi)

| # | Question | Resolution |
|---|---|---|
| Q2 | Trigger mechanism | **Partial-fill (5.1-C).** The author writes a partial `record {Control}Element` annotated with `[GenerateReactorWrapper(typeof(Control))]`; the generator fills every member the author didn't hand-write, so individual entries can be overridden in place. Generated members live in the author's own namespace, not a fixed `Reactor.Generated`. |
| Q3 | Two-way / controlled props | **Hybrid.** Auto-pair `{Prop}` + `{Prop}Changed` ⇒ controlled (emit `on{Prop}Changed`), but annotations can override the pairing or opt a prop out. |
| Q4 | Event coverage | **`RoutedEventHandler` + `TypedEventHandler<,>`** (covers the overwhelming majority of WinUI/WCT events, including the `*Changed` events two-way pairing relies on). |
| Q5 | Property selection | **Auto-discover + opt-out by default**, but a target can switch off autodiscovery and opt-in an explicit `Include` list. Selection is configured per-target on the attribute. |
| Q6 | Factory call shape | **Parameterized factory** mirroring Reactor's DSL: `SettingsCard(header: …, description: …, content: …, onClick: …)`. Init-props remain public so `with` still works as a secondary surface. |
| Q8 | Distribution | **Both** per-consumer generation *and* shippable wrapper packages for third-party control libraries. |
| Q7 | Inheritance cutoff | **Annotation-controlled per target, default stop above `Control`** (skip `Control`/`FrameworkElement` layout plumbing Reactor models via generic modifiers; a target can opt to go deeper). |
| Q1 | Factory host | **Static method on the annotated element type itself**, named after the control (`SettingsCard(...)` via `using static …SettingsCardElement`). Touching it triggers the element type's registration cctor — strengthens trim-rooting. |
| Q11 | Fluent modifiers | **Generic `.Set()`/`.Margin()`/`.Bold()` now** (already works); typed per-control helpers (e.g. `.Header(…)`) a later phase. |

Still open: Q9 (built-in migration), Q10 (project home — prototype already at `src/Reactor.Wrappers.Generator`). See §7.

---

## Table of Contents

- [§1 Motivation](#1-motivation)
- [§2 Goals and non-goals](#2-goals-and-non-goals)
- [§3 The prototype that exists today](#3-the-prototype-that-exists-today)
- [§4 Capability parity matrix](#4-capability-parity-matrix)
- [§5 The annotation surface — usage examples](#5-the-annotation-surface--usage-examples)
- [§6 Distribution model](#6-distribution-model)
- [§7 Open design decisions (maintainer's call)](#7-open-design-decisions-maintainers-call)
- [§8 Inference rules](#8-inference-rules)
- [§9 Trimming and AOT](#9-trimming-and-aot)
- [§10 Diagnostics](#10-diagnostics)
- [§11 Implementation phases](#11-implementation-phases)
- [§12 Testing](#12-testing)
- [§13 Open questions](#13-open-questions)
- [§14 Appendix — ToggleSwitch parity spike](#14-appendix--toggleswitch-parity-spike-first-built-in-smoke-test)
- [§15 Phase 5 scope — replacing the built-in catalog](#15-phase-5-scope--replacing-the-built-in-catalog-descriptor-only-attach-mode)

---

## §1 Motivation

The core idea is general: **turn any WinUI `FrameworkElement`-derived control into a first-class declarative Reactor element with no hand-written wrapper code.** Wrapping a control by hand today means writing four pieces (element record, descriptor, factory holder, registration) — the spec-048 Pattern-A shape. That is mechanical, repetitive, and easy to get subtly wrong (echo suppression, trim-rooting, content-slot reconciliation). A source generator collapses all four into a single annotation, and — per the north star — should eventually express every descriptor capability, so the generated path is a true peer of the hand-authored one rather than a convenience for simple cases.

The immediate motivating consumer is third-party control libraries. The open-source strategy discussion (2026) flagged Windows Community Toolkit ↔ Reactor integration with a **wrapper-first** approach, and *"sample code demonstrating how to wrap toolkit components"* as a practical first step — but the generator is not toolkit-specific; WCT is just one well-known control library it serves.

This unlocks two things at once:

1. **Any third-party control** (a WCT control, a control from another vendor, an app's own custom `Control`) becomes a one-line declarative citizen.
2. **Reactor's own ~50 built-in descriptors** become a candidate to *also* be generated, shrinking hand-maintained `Descriptors/*.cs` — though that migration is out of scope for v1 and gated on reaching parity (§11).

## §2 Goals and non-goals

**Goals**

- A source generator that turns any WinUI/third-party control type into a first-class Reactor element with no hand-written wrapper code.
- A path to **full `ControlDescriptor` parity** (§4) — controlled/two-way props, children strategies, events, coercion, pooling.
- Author ergonomics: the common case (a value/content control) is zero-config; advanced cases are reachable through opt-in annotations, never blocked.
- Correct trim/AOT story (the generated factory is the spec-048 trim-root chokepoint).

**Non-goals (for v1 — revisit later)**

- Migrating the built-in descriptor catalog to the generator.
- Generating items-control item-template machinery (ListView/GridView/TreeView typed item binding) — Phase 3+.
- Wrapping non-`FrameworkElement` types.

## §3 The prototype that exists today

`src/Reactor.Wrappers.Generator` is an `IIncrementalGenerator` (netstandard2.0, Roslyn 4.8, mirrors `Reactor.Analyzers`). The authoring attributes (`[GenerateReactorWrapper]`, `[WrapControlled]`, `[WrapConvert]`, …) live in a small companion assembly, `src/Reactor.Wrappers.Abstractions`, which every consumer references; the generator binds them by metadata name (it never emits them itself — see §15.7 "IVT attribute collision"). For each annotated partial element record (`[GenerateReactorWrapper(typeof(T))]`), it fills the rest of that same partial — element props, descriptor, Pattern-A registration, and a parameterized factory — implementing the resolved decisions above.

**Author writes:**

```csharp
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.SettingsCard),
    Exclude = new[] { "CommandParameter" })]
public partial record SettingsCardElement;
```

**App consumes** (the generated parameterized factory, via `using static`):

```csharp
using static MyApp.SettingsCardElement;

SettingsCard(
    header: "Wi-Fi",
    description: wifiOn ? "Connected" : "Disconnected",
    content: ToggleSwitch(isOn: wifiOn, onIsOnChanged: setWifiOn))  // Reactor child inside a WCT control
```

**Generator emits (abridged), into the same partial:**

```csharp
partial record SettingsCardElement : Element
{
    public string? Header { get; init; }
    public string? Description { get; init; }
    public bool? IsClickEnabled { get; init; }
    public Element? Content { get; init; }              // single-content child slot
    public System.Action? OnClick { get; init; }        // ButtonBase.Click
    public System.Action<SettingsCard>[] Setters { get; init; } = ...;

    public static readonly ControlDescriptor<SettingsCardElement, WCT.SettingsCard> Descriptor =
        new ControlDescriptor<...> { Children = new SingleContent<...>(...), GetSetters = ... }
            .OneWayConditional<string>(e => e.Header!, (c, v) => c.Header = v, e => e.Header is not null)
            // ... Description, IsClickEnabled, ... ...
            .HandCodedEvent<__EventPayload, RoutedEventHandler>(/* Click */);

    static SettingsCardElement() => ControlRegistry.Register<...>(static () => new DescriptorHandler<...>(Descriptor));

    public static SettingsCardElement SettingsCard(             // parameterized factory, named after the control
        string? header = default, string? description = default, /* … */
        Element? content = null, System.Action? onClick = null)
        => new() { Header = header, Description = description, /* … */ Content = content, OnClick = onClick };
}
```

**Prototype scope:** value props of `string`/`object` (as text), `bool`, `int`, `double`, enums; one `Content` child slot; `RoutedEventHandler` events. Everything else on the control is currently dropped (reachable via the `Setters` escape hatch). It builds and runs against WinAppSDK 2.0.1 with `CommunityToolkit.WinUI.Controls.SettingsControls` `8.3.260402-preview2`.

## §4 Capability parity matrix

Mapping each `ControlDescriptor` capability to how the generator could produce it, and whether it can be *inferred* from control metadata or needs an *author annotation*. This matrix is the backbone of the north-star; the **Strategy** column is itself a set of open questions (§7).

| Descriptor capability | What it's for | Auto-inferable? | Proposed strategy |
|---|---|---|---|
| `OneWayConditional<T>` | nullable value prop, skip when unset | ✅ yes | default for every settable value prop (prototype already does this) |
| `OneWay<T>` (+ dp `ClearValue`) | always-write / DP-fallback prop | ⚠️ partial | infer dp from `XxxProperty` static field; needs decision on when to prefer over conditional |
| `Initial` / `InitialOnly` | seed-once props | ❌ no | author annotation (`[WrapInitial]`) |
| **`Controlled<TValue,TArgs>`** | **two-way value + echo suppression (single OR multi-event)** | ✅ yes | pair a value prop `Foo` with `FooChanged` (auto), or name the event(s) via `[WrapControlled(ChangedEvent=…)]` / `[WrapControlled(Events=…)]` → emit one controlled entry + `On{Foo}Changed` callback. **The crux of parity** (§7 Q3). |
| `HandCodedControlled` | (was: multi-event controlled props) | ✅ superseded | **Not needed** — multi-event two-way is handled by the public `.Controlled<TValue,TArgs>` entry with a multi-event `subscribe` block + `readBack` (the value is read from the control, not the args), so the internal `ChangeEchoSuppressor` path is avoided. See `[WrapControlled(Events=…)]`. |
| `HandCodedEvent<TPayload,TDelegate>` | fire-and-forget events | ⚠️ partial | prototype handles `RoutedEventHandler`; generalize to `TypedEventHandler<,>` (§7 Q4) |
| `CoercingOneWay<T>` | min/max coercion (Slider) | ❌ no | author annotation declaring the coerced sibling |
| `SingleContent` | one content child | ✅ yes | by `Content` property (prototype) |
| `ImperativeBridged` (secondary element slot) | a 2nd single-element child written to a *dedicated* control property (e.g. `SettingsCard.HeaderIcon`, `TabView.TabStripHeader`/`TabStripFooter`) | ⚠️ partial | author annotation `[WrapElementSlot("Prop", ControlProperty=…)]` → surfaces an `Element?` slot (+ factory param in full-wrapper mode) and emits mount (`ctx.MountChild`) / state-preserving update (`ctx.ReconcileChild`). The public `ReconcileChild` on `MountContext`/`UpdateContext` lets *external* wrappers (not just built-in descriptors) host stateful secondary slots. Validated by `WrapElementSlotAnalyzer` (REACTORGEN013/014/015). Slots sharing a control prop with a string fallback (e.g. `Expander.HeaderTemplate`→`Header`) stay hand-written. |
| `Panel` children | StackPanel/Grid/Canvas | ✅ yes (collection) | auto-detected by a public `Children` of type `UIElementCollection` → `Panel<…>` strategy + `params Element[]` factory. **Per-child attached-prop hints (Grid.Row, Canvas.Left) are NOT generated** — a separate capability. |
| items-binder strategies | ListView/ComboBox/ListBox/RadioButtons/… | ✅ yes (collection) | auto-detected by a public `Items` getter whose type is/implements `IList<object>` (ItemsControl `ItemCollection` or bespoke `IList<object>`) → `ItemsHost<…>` strategy (`GetItems`/`GetCollection`) + `params object[] items` factory. Selection is the ordinary controlled-prop path (`[WrapControlled("SelectedIndex", Events=…)]`). **Typed/keyed virtualization (`ListView<T>` item templates) is NOT generated** — a separate capability. |
| `PoolPolicy` / `Factory` | pooling, custom ctor | ❌ no | annotation; default `new()` |
| `GetSetters` | fluent `.Set(...)` escape hatch | ✅ yes | always emitted (prototype) |
| `AfterChildrenMount` | post-children event wiring | ❌ no | annotation |

Legend: ✅ zero-config · ⚠️ inferable with a heuristic that has false-positive risk · ❌ needs an explicit author hint.

## §5 The annotation surface — usage examples

### 5.0 The chosen end-to-end shape (per §Resolved decisions)

**Author writes a partial, annotated with the control to wrap:**
```csharp
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.SettingsCard))]
public sealed partial record SettingsCardElement;   // generator fills props + descriptor + factory
```

**App consumes the generated parameterized factory** (`using static` the author's namespace):
```csharp
SettingsCard(
    header: "Wi-Fi",
    description: wifiOn ? "Connected" : "Disconnected",
    content: ToggleSwitch(isOn: wifiOn, onIsOnChanged: setWifiOn),
    isClickEnabled: true,
    onClick: () => setClicks(clicks + 1))
```

**Two-way props auto-pair** `Value` + `ValueChanged` ⇒ controlled with echo suppression:
```csharp
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.RatingControl))]
public sealed partial record RatingControlElement;

// →  RatingControl(value: rating, onValueChanged: setRating)
```

**Hand-tune a single entry** by writing it in your own partial; the generator skips members you declare:
```csharp
public sealed partial record SettingsCardElement
{
    // Override: treat Header as templated Element content instead of plain text.
    public Element? Header { get; init; }
}
```

**Selection opt-out / opt-in is per-target on the attribute:**
```csharp
[GenerateReactorWrapper(typeof(SettingsCard), Exclude = new[] { nameof(SettingsCard.CommandParameter) })]
// or switch to explicit opt-in:
[GenerateReactorWrapper(typeof(SettingsCard), AutoDiscover = false,
    Include = new[] { nameof(SettingsCard.Header), nameof(SettingsCard.Description) })]
```

The remaining subsections record the alternatives that were considered.

### 5.1 Trigger — where the annotation lives

**Option A — assembly attribute (prototype):**
```csharp
[assembly: GenerateReactorWrapper(typeof(SettingsCard))]
```

**Option B — partial-class hub the author owns:**
```csharp
[GenerateReactorWrapper(typeof(SettingsCard))]
[GenerateReactorWrapper(typeof(Shimmer))]
public static partial class ToolkitControls { }   // generated factories hang off here
```

**Option C — per-property fluent override via a partial:** author writes a partial `SettingsCardElement` and the generator fills the rest, letting the author hand-tune individual entries.

### 5.2 Factory & call-site shape

**Option A — `Create()` + `with` (prototype):**
```csharp
Gen.SettingsCard.Create() with { Header = "Wi-Fi", IsClickEnabled = true }
```

**Option B — parameterized factory mirroring Reactor's DSL** (`Button(label, onClick)` style):
```csharp
Gen.SettingsCard(header: "Wi-Fi", description: "…")
```

**Option C — `using static` DSL parity** so it reads exactly like a built-in:
```csharp
using static Reactor.Generated.Factories;
SettingsCard(header: "Wi-Fi") with { IsClickEnabled = true }
```

### 5.3 Two-way / controlled props (the parity crux)

**Status: prototype implemented (Phase 2 first cut).** A value property `P` paired with a sibling `PChanged` event (auto-pair, Option A — the resolved Q3 hybrid default) is emitted as a **controlled** prop, grounded in spec 050's `Optional<T>` authority model:

- The element field is `global::Microsoft.UI.Reactor.Optional<T>` (default `Unset`). `Unset` ⇒ the control owns the value and **user interaction survives unrelated re-renders** (exactly the uncontrolled `Expander.IsExpanded` semantic); an explicit value ⇒ force-assert with drift-gate + echo suppression.
- A sibling `On{P}Changed` callback (`Action<T>?`) is emitted.
- The factory exposes both: `Rating(value: r, onValueChanged: setR)`.

**Why `Optional<T>` is mandatory, not stylistic:** the public `ControlDescriptor.Controlled<TValue,TArgs>` / `.HandCodedControlled` getters are typed `Func<TElement, Optional<TValue>>` (spec 050 §5 — the plain-`T` overload was deleted). A generated controlled prop therefore *must* be `Optional<T>`, and defaulting to `Unset` is what makes the wrapped control behave as uncontrolled-but-user-modifiable unless the author opts in.

**Why `.Controlled`, not the Expander-style `.HandCodedControlled`:** echo suppression for the hand-coded path runs through the **internal** `ChangeEchoSuppressor`, which generated code (living in the consuming app, outside `Reactor.dll`) cannot call. `ControlDescriptor.Controlled<TValue,TArgs>` encapsulates all echo handling inside the entry and needs only public delegates — the generator emits:

```csharp
.Controlled<T, TArgs>(
    get:         static e => e.Value,                 // Optional<T>
    set:         static (c, v) => c.Value = v,
    subscribe:   static (fe, h) => ((Control)fe).ValueChanged += (s, e) => h(s, e),  // bridge native event → EventHandler<TArgs>
    unsubscribe: static (fe, h) => { },               // no-op: per-control payload gate subscribes once per lifetime
    callback:    static e => e.OnValueChanged,
    readBack:    static c => c.Value)
```

`TArgs` is read generically from the change event's delegate `Invoke` second parameter, so it covers `RoutedEventHandler`, `TypedEventHandler<,>`, and control-specific `*ChangedEventHandler` shapes alike.

**Limits of the current cut (future work):** auto-pairing keys strictly on the `{P}Changed` name, so controls whose change event breaks the convention (`ToggleSwitch.IsOn`↔`Toggled`, `Expander.IsExpanded`↔`Expanding`/`Collapsed`) stay one-way until the Q3 annotation override (`[WrapControlled(...)]`, Option B/C below) lands.

#### Alternatives considered

**Option B — explicit annotation, no magic:**
```csharp
[GenerateReactorWrapper(typeof(RatingControl),
    Controlled = new[] { nameof(RatingControl.Value) })]
```

**Option C — opt-in echo mode per controlled prop** (mirrors descriptor `valueDiffEcho` vs suppressor):
```csharp
[WrapControlled(nameof(RatingControl.Value), Echo = EchoMode.ValueDiff)]
```

### 5.4 Property selection

**Option A — auto-discover** all settable value props above `Control` (prototype; can be noisy — note `CommandParameter`, `ClickMode` leaked into the SettingsCard wrapper).

**Option B — allowlist:** only props the author names are surfaced.

**Option C — auto-discover + opt-out** (`Exclude = new[]{ "CommandParameter" }`).

## §6 Distribution model

Wrappers can be **owned by whoever owns the control** — an app, a vendor, or a control-library maintainer — under a `*.Reactor` companion-package convention. Two ways the generator supports this:

- **Per-consumer generation (prototype):** the app annotates and the wrapper is generated into the app. Zero shipping overhead; every app re-declares.
- **Shipped wrapper packages:** a `*.Reactor` companion package annotates once and ships the wrappers as public API. Needs the generated types to be `public` and stably named (§7 Q1).

These are not exclusive — the same generator serves both. The Windows Community Toolkit is the obvious first candidate for a shipped companion package, but the model is identical for any control library.

## §7 Open design decisions (maintainer's call)

Q1–Q8 and Q11 are **resolved** (see §Resolved decisions). Remaining:

| # | Decision | Resolution / options |
|---|---|---|
| Q9 | Built-in catalog migration | **Resolved: opt-in experiment in a later phase, once descriptor parity is proven** (Phase 5, §11). |
| Q10 | Project home | **Resolved: `src/Reactor.Wrappers.Generator`** (prototype already there). |

## §8 Inference rules

(Prototype rules — subject to §7.)

- **Member walk:** from the most-derived type up to, but excluding, `Microsoft.UI.Xaml.Controls.Control`. Captures control-specific members (`ButtonBase.Click`, `ContentControl.Content`, `SettingsCard.Header`) while skipping `Control`/`FrameworkElement` layout plumbing Reactor already models via modifiers.
- **Value props:** public instance, public get+set, non-indexer. `string`/`object` → text; `bool`/`int`/`double`/enum → nullable-backed `OneWayConditional`; **value-type structs** (`Thickness`, `CornerRadius`, `Color`, …) → `T?` nullable one-way (write `.Value`); **reference types** (`Brush`, `FontFamily`, …) → `T?` one-way (write when non-null); **`Nullable<U>` tri-state** (`bool?`, `DateTimeOffset?`, …) → **spec-050 `Optional<U?>`** (Unset ⇒ don't touch, `Of(null)` ⇒ write null, `Of(v)` ⇒ write v — `Optional<T>` mirrors `Nullable<T>`'s `.Value`/`.HasValue` so the same one-way/controlled emit handles it). Skipped: delegates, interfaces, arrays, collections (`IEnumerable`), templates/styles, and `UIElement`-derived types (those are content, not value props).
- **Content slot:** discovered from the control's WinUI `[ContentProperty]` attribute, with a `[WrapContent("Prop")]` override and a property-named-`Content` fallback. Auto-accepted only when unambiguously a child — a `UIElement`-derived property (`Border.Child`, `Viewbox.Child`) or an `object` named exactly `Content` (`ContentControl.Content`). An `object` `[ContentProperty]` with another name (WinUI declares `ToggleSwitch`'s as `Header`) is treated as a value prop, not a child slot; a `[WrapContent]` override forces any single-content-typed property. Collections (`Panel.Children`/`ItemsControl.Items`) are panel/items strategies (P3), not this. The `SingleContent` strategy writes the discovered property (`c.Child`), not a hardcoded `c.Content`.
- **Two-way props:** a value prop `P` paired with a `{P}Changed` event (auto-pair), a `[WrapControlled(P, ChangedEvent)]` override (single non-conventional event), or a `[WrapControlled(P, Events = new[]{…})]` override (multiple events, e.g. `Checked`+`Unchecked`) ⇒ `Optional<T>` + `On{P}Changed` + public `.Controlled<TValue,TArgs>` (spec 050; §5.3). For multi-event the value is read back from the control after any event fires; `TArgs` is taken from the first event. `[WrapOneWay(P)]` opts a prop out of auto-pairing (keeps it one-way despite a `{P}Changed` event — e.g. `ProgressBar.Value`).
- **Events:** fire-and-forget `RoutedEventHandler` and `TypedEventHandler<,>` events not consumed by a controlled prop ⇒ `Action On{Event}` + `HandCodedEvent` trampoline (typed to the event's delegate) that reads the live element via `Reconciler.GetElementTag`.
- **Name-mapping:** `[WrapAlias("ElementName", "ControlProperty")]` surfaces a control property under a friendly element-facing name — the generated init-property and factory parameter use `ElementName`, while the descriptor reads/writes `ControlProperty` (e.g. `Min`→`Minimum`, `Content`→`Text`). **Aliasing the content property opts it out of the child slot:** when the aliased `ControlProperty` is the control's discovered `[ContentProperty]`, the generator treats it as a value prop (the string form of polymorphic content) rather than a `SingleContent` child — e.g. `[WrapAlias("Label", "Content")]` on `CheckBox`/`ToggleButton` surfaces `string? Label` writing `c.Content`, with no `Content` element child slot.
- **One-way DP-backed props use spec-050 `Optional<T>` + `dp:` ClearValue.** When a one-way prop is backed by a public `{Prop}Property` dependency property, the element prop is `Optional<T>` and the descriptor uses `.OneWay(get, set, dp:)` — `Unset` ⇒ `ClearValue(dp)` releases the local value to the WinUI style/precedence chain (spec 050 §6.3), instead of the skip-write fallback. Props with no DP keep the nullable-`T?` `OneWayConditional` skip-write. Combined with controlled props (`Optional<T>`-enforced) and tri-state nullables (`Optional<U?>`), the generator is now fully Optional-native.
- **Setters:** always emitted for the `.Set(...)` escape hatch.

### §8.1 Silent-drop regression guard

`tests/Reactor.Tests/WrappersGenerator/DescriptorSilentDropGuardTests.cs` is an always-on CI gate
(`MigratedDescriptors_DoNotSilentlyDropUnsupportedTypeProps`). The generator only maps control properties
whose type it supports, so a `[GenerateReactorDescriptor]` record prop whose backing control property is an
**unsupported type** (e.g. `ParallaxView.Source : UIElement`) is dropped with no compile error and no
unit/selftest failure. The guard reflects every `[GenerateReactorDescriptor]` element, mirrors the
generator's type-support rules, and **fails** on any uncovered one-way drop not handled by `[WrapManual]`/
`Exclude`/`[WrapConvert]` (it caught the original `ParallaxView.Source` drop and self-validates that it
inspected ≥20 migrated controls so it can't pass vacuously).

> An earlier env-gated **comprehensive parity report** (a reflection dashboard + per-element `Patch` table
> that counted "N/75 controls the generator could replace today") lived alongside this guard during the
> built-in migration campaign. That migration is complete and the report was removed as spent scaffolding;
> the one remaining capability gap it tracked — keyed/templated/virtualized items + multi-select — is in
> §11. The WCT gallery (`samples/apps/wct-controls`) is the living proof that the generator handles
> arbitrary third-party controls.


## §9 Trimming and AOT

The generated factory holder is the spec-048 §6 Pattern-A trim-root: `ControlRegistry.Register` is called from the holder's static constructor, reachable only through `{Control}.Create()`. An app that never calls the factory lets the whole chain (element + descriptor + handler + control) be trimmed. The `static` lambda in `Register` keeps registration zero-closure. The generator must keep emitting this shape; the consuming app remains responsible for `IsAotCompatible` settings.

## §9a Control-library XAML metadata resolution (required)

A third-party control's default style/template lives in its assembly's `Themes/Generic.xaml`. When the WinUI loader parses that dictionary it resolves type references through `Application.Current`'s `IXamlMetadataProvider` chain. A Reactor app with no XAML of its own has no compiler-generated provider for the referenced control library, so the lookup fails and the app **crashes at first realization** with `0xC000027B` (E_FAIL in `Microsoft.UI.Xaml.dll`) — observed with `SettingsCard` before the fix. The cure is `ReactorApp.RegisterControlAssembly(controlAssembly)` (issue #142).

The generator emits this call automatically in the element type's **static constructor**, immediately before `ControlRegistry.Register`:

```csharp
static SettingsCardElement()
{
    global::Microsoft.UI.Reactor.ReactorApp.RegisterControlAssembly(typeof(SettingsCard).Assembly);
    global::…ControlRegistry.Register<SettingsCardElement, SettingsCard>(static () => new …(Descriptor));
}
```

Because the static cctor runs the first time the factory is called (during `Render`, before the control is mounted and its `Generic.xaml` loads), the provider is registered in time. Authors therefore don't need a manual registration step — wrapping is self-contained.

## §10 Diagnostics

The generator emits `REACTORGEN001` when a target isn't a non-static `FrameworkElement` with a public parameterless ctor. The companion analyzers emit errors for malformed attributes: **`REACTORGEN002`** (Include/Exclude name), **`REACTORGEN003`/`REACTORGEN004`** (WrapControlled property/change-event), **`REACTORGEN005`** (WrapAlias control property), **`REACTORGEN006`** (WrapOneWay property), **`REACTORGEN007`** (WrapContent property), and **`REACTORGEN008`** (WrapConvert property — must be a public settable property whose type has a public single-argument constructor; recognized for both `[GenerateReactorWrapper]` and `[GenerateReactorDescriptor]`). Candidates to add: ambiguous two-way pair, unsupported property type silently dropped (info-level), duplicate target.

## §11 Implementation phases

1. **P1 (prototype — done):** leaf/content controls; one-way value props; `Content`; `RoutedEventHandler` events; Pattern-A registration; auto control-library metadata registration (§9a); `REACTORGEN002` Include/Exclude analyzer; one sample.
2. **P2 (done):** controlled/two-way props via `{P}`+`{P}Changed` auto-pair → `Optional<T>` + public `.Controlled<TValue,TArgs>` (§5.3), with `TArgs` read generically from the change-event delegate; the `[WrapControlled(prop, ChangedEvent)]` override for non-conventional single change events (`Toggled`); fire-and-forget events for **both `RoutedEventHandler` and `TypedEventHandler<,>`** (Q4); `[WrapOneWay(prop)]` to opt a prop out of two-way auto-pairing (Q3, e.g. `ProgressBar.Value`); and Include/Exclude opt-out (Q5). Proven by the ToggleSwitch + Slider parity selftests (§14) and the ProgressBar parity-audit row. (Multi-event controlled — `Expander.Expanding`/`Collapsed`, `CheckBox.Checked`/`Unchecked` — is the §4 `HandCodedControlled` row → P3.)
3. **P3:** coercion; pooling/`Factory` annotations; `Initial`/`InitialOnly`. (**Done:** non-scalar value/reference types (§8); `[WrapAlias]` name-mapping; `Nullable<T>` tri-state → `Optional<U?>`; one-way `Optional<T>`+dp ClearValue; `[ContentProperty]`-driven content-by-other-name + `[WrapContent]` override; **multi-event controlled via `[WrapControlled(Events=…)]`** — emitted through the public `.Controlled<TValue,TArgs>` entry with a multi-event `subscribe` block + `readBack`, so `HandCodedControlled`/the internal echo suppressor is **not** required (proven by `RadioButtonWrapper_Parity`, `IsChecked ↔ Checked + Unchecked`); **panel children** — a control exposing a public `Children` of type `UIElementCollection` (StackPanel/Canvas/Grid/RelativePanel/WrapGrid/FlexPanel) gets a `Panel<TElement,TControl>` children strategy + a `params Element[] children` factory (proven by `StackPanelWrapper_Parity`, mount + reconcile-grow). **NOTE:** per-child *attached* layout props (`Grid.Row`, `Canvas.Left`) are a separate, not-yet-generated capability — the generated panel reproduces the children collection but not attached-DP placement.)
4. **P4 (done):** flat items controls. A control exposing a public `Items` getter whose type is or implements `IList<object>` (ItemsControl-derived `ItemCollection`, or a bespoke `IList<object>` as on `RadioButtons`/`SelectorBar`) gets an `ItemsHost<TElement,TControl>` children strategy (`GetItems: e => e.Items`, `GetCollection: c => c.Items`) + an `Items` element prop of `IReadOnlyList<object>` + a `params object[] items` factory parameter — mutually exclusive with panel-children and single-content. Selection is the ordinary controlled-prop path (`[WrapControlled("SelectedIndex", Events=…)]`). Proven by `ListBoxWrapper_Parity` (mount 3 items, reconcile-grow to 4, controlled `SelectedIndex` write). `Style` was also enabled as a general one-way reference prop. Parity audit → **43/75**. **NOTE:** typed/keyed virtualization (`ListView<T>` item templates / `ItemsSource` binding) remains a separate, not-yet-generated capability; controls keyed off `ItemsSource`/`TabItems`/name-mismatch selection (BreadcrumbBar/TabView/NavigationView) are not covered.
5. **P5 (in progress — see §15):** replace hand-written built-in descriptors/handlers with generated ones via a new **descriptor-only ("attach") generation mode**, preserving the public element-record API and global factories so the existing test suite (unit + selftest + **e2e**) stays green and unchanged. Full record/factory replacement is **out of scope** (see §15 for the evidenced blockers). **P5.1 done:** descriptor-only mode (`[GenerateReactorDescriptor]`, record-driven). **P5.2 done:** general `[WrapConvert]` scalar→struct conversion + `REACTORGEN008`. **P5.3 done for Viewbox:** generator wired onto `src/Reactor`, `ViewboxDescriptor` deleted, generated descriptor proven equivalent (unit 9269 + live Viewbox selftest green); the feared IVT/build-order risks were empirically disproved; Border deferred (its hand-coded handler is directly tested).

## §12 Testing

- **Generator unit tests** (`Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing`): assert emitted source for representative controls; assert diagnostics.
- **Selftest fixture** (`tests/Reactor.AppTests.Host/SelfTest/Fixtures/`): mount a generated wrapper against a real WCT control, exercise one-way + (P2) two-way round-trips.
- **Sample** (`samples/apps/wct-controls`): runnable end-to-end proof — wraps several WCT controls (SettingsCard, SettingsExpander, Segmented, RadialGauge, CameraPreview) with the generator.

## §13 Open questions

1. For two-way inference, is `{Prop}Changed` a strong enough convention, or do we require `[WrapControlled]` to avoid surprises? (Q3)
2. Should `object`-typed props (`Header`, `Description`) accept a Reactor `Element` (templated content) in addition to text? Toolkit `Header` is often a control.
3. How do we want shipped `*.Reactor` companion packages to version against the control library they wrap?
4. Do we surface a `mur` CLI command to scaffold the annotation + sample for a given control?
5. Resolved: yes — the partial-fill model (Q2) means authors can override a single generated entry by declaring it in their own partial, and the generator skips members it sees already written.

## §14 Appendix — ToggleSwitch parity spike (first built-in smoke test)

**Status: implemented and green (2026-06-09).** The `[WrapControlled]` override shipped (single- and multi-event), panel children shipped, items children shipped, and **five** live parity selftests pass (27 checks total, stable across repeated runs):
- `ToggleSwitchWrapper_Parity` — the **override** path (`IsOn ↔ Toggled`, `RoutedEventArgs`).
- `SliderWrapper_Parity` — the **auto-pair** path (`Value ↔ ValueChanged`, `RangeBaseValueChangedEventArgs`), needing no override and exercising a different `TArgs`.
- `RadioButtonWrapper_Parity` — the **multi-event** path (`IsChecked ↔ Checked + Unchecked` via `[WrapControlled(Events=…)]`), exercising both wired events (the snap-back is driven by `Unchecked`) and the faithful `Optional<bool?>` surface.
- `StackPanelWrapper_Parity` — the **panel-children** path (generated `Panel<…>` strategy), proving mount + reconcile-grow of the live `UIElementCollection`.
- `ListBoxWrapper_Parity` — the **items-children** path (generated `ItemsHost<…>` strategy), proving mount of a flat `params object[] items` collection, reconcile-grow of the live `Items`, and a controlled `SelectedIndex ↔ SelectionChanged` force-write.

These reproduce the hand-written descriptor's spec-050 controlled-prop semantics and panel/items-children reconcile against real WinUI controls. Fixtures: `tests/Reactor.AppTests.Host/SelfTest/Fixtures/{ToggleSwitchWrapper,SliderWrapper,RadioButtonWrapper,StackPanelWrapper,ListBoxWrapper}ParityFixture.cs`.

The §11 P5 gate (generating a slice of the built-in catalog) needs one proof that a generated descriptor is *functionally equivalent* to a hand-written one. `ToggleSwitch` is the chosen first target: it is small, already uses `Optional<T>` + `.Controlled`, and exercises the one capability the generator still lacks (a controlled prop whose change event breaks the `{P}Changed` convention).

### Target (hand-written today)

- Element: `ToggleSwitchElement(Optional<bool> IsOn, Action<bool>? OnIsOnChanged, string? OnContent, string? OffContent)` + `string? Header` + `Setters`.
- Descriptor (`ToggleSwitchDescriptor`): `.Controlled<bool, RoutedEventArgs>(IsOn ↔ Toggled, OnIsOnChanged)`, `.OneWay(OnContent)`, `.OneWay(OffContent)`, `.OneWayConditional(Header, is not null)`.
- Factory: `ToggleSwitch(isOn, onIsOnChanged, onContent, offContent, header)`.

### Gap vs. what the generator emits today

Auto-discovering `Microsoft.UI.Xaml.Controls.ToggleSwitch` (cutoff above `Control`) yields `IsOn`(bool), `Header`/`OnContent`/`OffContent`(object→text), templates (skipped). Two mismatches:

1. **`IsOn` comes out one-way** — there is no `IsOnChanged` event, so the `{P}Changed` auto-pair misses; the real change event is `Toggled`.
2. **`Toggled` comes out as a fire-and-forget `OnToggled`** instead of feeding `IsOn`.

`Header`/`OnContent`/`OffContent` already match (object→text `OneWayConditional`); the only built-in delta is `OnContent`/`OffContent` using unconditional `.OneWay` (cosmetic — conditional is safe).

### The one new capability required: controlled-event override (Q3)

```csharp
[GenerateReactorWrapper(typeof(ToggleSwitch))]
[WrapControlled("IsOn", ChangedEvent = "Toggled")]
public partial record ToggleSwitchElement;
```

Behavior: force `IsOn` controlled even without an `IsOnChanged` event; bind it to `Toggled` (`TArgs` = `RoutedEventArgs`, read generically from the delegate `Invoke`); remove `Toggled` from the fire-and-forget list; emit `Optional<bool> IsOn` + `OnIsOnChanged`.

### Work items

1. `WrapControlledAttribute(string property)` with `ChangedEvent` (+ future `Echo`); emit via post-init. Repeatable per target.
2. Parse overrides in `BuildModel`; thread into `CollectMembers`.
3. In `CollectMembers`, an overridden prop looks up the named event (any 2-arg delegate), is forced controlled, and consumes that event.
4. Analyzer rules: `WrapControlled` names an unknown property / unknown event / a non-2-arg delegate (sibling of `REACTORGEN002`).
5. Generator unit test asserting the emitted `.Controlled<bool, RoutedEventArgs>(... Toggled ...)` shape, and no `OnToggled`.
6. **Selftest fixture** (`tests/Reactor.AppTests.Host/SelfTest/Fixtures/`) mounting a *generated* ToggleSwitch and proving: `Unset` ⇒ user toggle survives a re-render (uncontrolled); a set value ⇒ snap-back (force-assert); `OnIsOnChanged` fires; `Header`/`OnContent`/`OffContent` render. This is the equivalence proof.

### Explicitly out of scope (needed only to *delete* the built-in, not to prove parity)

- Factory fidelity: param order/names/overloads to be a drop-in for the DSL `ToggleSwitch(...)` (Q1/Q6).
- DSL collision: generated `ToggleSwitch` vs `using static Factories` `ToggleSwitch`.
- Re-pointing the ~dozens of `ToggleSwitchElement` references + tests across the repo.

### Effort

Small–medium, no core-Reactor changes: ~1 attribute + ~30 lines in `CollectMembers` + 1–2 analyzer rules + 1 selftest fixture. Deliverable is a green selftest proving functional equivalence; the literal catalog swap remains a separate, factory-fidelity-gated follow-up.

## §15 Phase 5 scope — replacing the built-in catalog (descriptor-only "attach" mode)

**Status: scoped (2026-06-09, @azchohfi). Not yet implemented.** P5 turns the generator from an *additive* authoring tool (new wrappers for third-party controls) into a *replacement* for the **94 hand-written built-in implementations** (84 `…/Descriptor/Descriptors/*.cs` + 10 `…/Handlers/*.cs`), wired today from **117 `V1.Reg<…>.Done` touch-sites** in `Dsl.cs`.

### §15.1 The hard constraint that picks the mechanism

The user goal is "replace the implementations with generated ones, all tests green (incl. e2e), **without changing the tests**." The built-in element records are a deliberately *ergonomic, hand-curated public API* that diverges from raw control metadata. Evidence from `BorderElement` alone:

- **Positional ctors** — tests do `new BorderElement(null) { CornerRadius = 4 }` (16 sites). The generator emits a *parameterless* `partial record` with `init` props; it does not emit positional ctors.
- **Global factory location/signature** — tests call `Border(child)` (253 sites) resolved from `Factories`/`Dsl`, not the generated `BorderElement.Border(…)` static.
- **Ergonomic scalar→struct conversions** — the record exposes `double? CornerRadius` / `double? BorderThickness` but the descriptor writes `new CornerRadius(v!.Value)` / `new Thickness(v!.Value)`. The generator's by-name auto-map would target the control's `CornerRadius`/`Thickness` *struct* types, not `double`.

⇒ **Replacing the record + factory wholesale would change the public API and break thousands of call-sites/tests.** That is out of scope. P5 instead replaces *only the implementation behind the existing API*.

### §15.2 Chosen mechanism — descriptor-only ("attach") generation

A new trigger annotates the **existing** author-written `partial record` (which keeps its props *and* its hand-written global factory). The generator emits **only** the descriptor + handler — never record props, never a factory:

```csharp
// Element.cs — unchanged public record (props, positional ctor) stays hand-written
public partial record BorderElement(Element? Child) : Element { public double? CornerRadius { get; init; } … }

// New, beside it — the generator's whole output for this control:
[GenerateReactorDescriptor(typeof(WinUI.Border))]
[WrapConvert("CornerRadius", typeof(CornerRadius))]   // double → new CornerRadius(v)
[WrapConvert("BorderThickness", typeof(Thickness))]   // double → new Thickness(v)
public partial record BorderElement;                  // → emits Descriptor + DescriptorHandler
```

The hand-written `Border(child)` factory's one line flips from `V1.Reg<BorderElement, WinUI.Border, V1.Handlers.BorderHandler>.Done` to the generated handler — the **only** change outside the generator, and invisible to callers. The hand-written `BorderDescriptor.cs` / `BorderHandler.cs` are then deleted.

Why this shape: it touches **zero** public API. Records, ctors, factories, and the spec-050 `Optional<T>` surface tests depend on are all untouched; only the mount/update *logic* swaps from hand-coded to generated, and the existing per-control fixtures (`BorderPortTests`, `Spec047V1ProtocolFixtures`, …) become the equivalence proof for free.

### §15.3 New **general** generator capabilities required (none control-specific)

1. **Descriptor-only / "attach" mode** (`[GenerateReactorDescriptor(typeof(Control))]`): read the *existing* record's declared public properties (positional + `init`) and map each to the control by name, emitting `public static readonly ControlDescriptor<E,C> Descriptor` + `XxxDescriptorHandler` — and **suppress** record-prop and factory emission. Reuses the entire existing prop/event/children/controlled inference; only the *input* (existing record vs. synthesized props) and *output set* (no record body, no factory) differ.
2. **`[WrapConvert(prop)]`** — a general element-type↔control-type projection so the pervasive scalar→struct ergonomic pattern (`double`↔`CornerRadius`/`Thickness`/`GridLength`, …) is expressible once and reused across Border/Grid/StackPanel/etc. The element value type is **inferred from the struct's single public one-argument constructor** (so authors write just `[WrapConvert("CornerRadius")]`, no type argument). **General, multi-control — not a per-patch attribute** (per the user's "no attribute unique to one patch" rule). **(P5.2 — done.)**
3. **Naming alignment** so the generated `XxxDescriptor`/`XxxDescriptorHandler` drop into the existing `V1.Reg<>` touch-site with a trivial type-name swap.

Everything else (one-way/conditional/controlled/multi-event/panel/items/events/setters/`Optional<T>`/dp-ClearValue) is **already built** (P1–P4) and reused unchanged.

### §15.4 Candidate slice & order

Gate eligibility on the **43/75 parity-audit PASS set**, migrate control-by-control, each behind the full suite:

- **P5 slice 0 (mechanism proof — done for Viewbox):** **`Viewbox`** is the clean first migration — single-content (`Child`, name matches WinUI's `[ContentProperty]`), two conditional one-way enums (`Stretch`/`StretchDirection`), `Setters`, no events, no `[WrapConvert]` needed. **`Border` is NOT clean** despite being smaller: its hand-coded `BorderHandler` is directly instantiated/asserted in `BorderPortTests` (`new BorderHandler()`), so migrating it (deleting the handler) requires a test edit. Controls whose hand-written **handler/descriptor is referenced by name in tests** (the Spec-047 port tests instantiate the 10 hand-coded handlers; some bootstrap lines name descriptor handlers) must either keep the hand-written class as dead code or accept a minimal test edit — the per-control decision in §15.7.
- **P5 expansion:** walk outward through the audit PASS set (leaf/content/value controls, then panels, then flat items controls), deleting each hand-written descriptor as its generated twin goes green — preferring **descriptor-backed** controls with no by-name test coupling.
- **Deliberately retained (hand-written):** controls needing capabilities the generator still lacks or that are genuinely bespoke — echo-suppressor doubles/coercion/deferred strings (Slider/NumberBox value, AutoSuggest/Password/RichEdit), `CoercingOneWay`, pooling (`PoolPolicy`/`Factory`), `AfterChildrenMount`, `Initial`/`InitialOnly`, attached-layout placement (Grid.Row/Canvas.Left), and the bespoke-composite props with no underlying control property. These are the "extremely unique, few controls" the user accepts staying hand-written.

### §15.5 Validation gates (per the user's directive)

- **No test edits.** Unit + selftest suites green unchanged.
- **E2E (Appium/WinAppDriver) green** — the strict gate; these drive real user input against the real controls.
- **Behavioral equivalence** proven by the *existing* per-control fixtures passing unchanged against the generated descriptor (no new fixtures needed for migrated controls).
- **Echo-suppression parity check** per controlled control: generated controlled props use the public `.Controlled` value-diff arm; built-ins on the `ChangeEchoSuppressor` fallback (doubles/coercion/deferred) must be verified equivalent or retained hand-written.

### §15.6 P5 work breakdown

- **P5.30 (generic `OnUnmount` primitive + `[WrapLifecycle]` generator capability + lifecycle torture tests + 4 more WCT controls):** made imperative controls ergonomic and added stress coverage that found two real bugs. **(1) Core `.OnUnmount(control)` element modifier** — the teardown half of `.OnMount` (React `useEffect` cleanup / Compose `DisposableEffect { onDispose }`), captured at `ApplyModifiers` into a per-control table and invoked once at unmount. **(2) `[WrapLifecycle(onMounted, OnUnmounted=)]`** generator capability (general, not a per-control patch): the generated factory wires the named `static void M(TControl)` methods through `.OnMount`/`.OnUnmount`, so an imperative control auto-starts on mount / stops on unmount with **zero call-site boilerplate** — plus a **`WrapEventAnalyzer`-style `WrapLifecycleAnalyzer` (REACTORGEN011)** validating the method names/signatures. **CameraPreview migrated** to `[WrapLifecycle(nameof(StartPreview), OnUnmounted=nameof(StopPreview))]` — the gallery call site is now a plain declarative `CameraPreview(onPreviewFailed: …)` (the old `Setters`+`UseRef`+`StartAsync` dance is gone). **(3) `wct-controls` gallery** grew to a NavigationView with **9 controls** — added ColorPicker (two-way `Color`), ImageCropper (CropShape/AspectRatio), GridSplitter (in a resizable `Grid`), TokenizingTextBox (two-way `Text` + tokens), each its own page. *MarkdownTextBlock has no WinAppSDK-compatible package on nuget.org (only the legacy WinUI2/UWP `…UI.Controls.Markdown` 7.x) — omitted.* **(4) Mount/unmount/lifecycle torture tests** (`LifecycleTortureFixtures.cs`, 3 selftest fixtures, real WinUI controls) hammer the reconciler: they **found and fixed** a total-failure bug (the first `.OnUnmount` impl read `GetElementTag().Modifiers` which never matched → fired 0 times; fixed by capturing from the authoritative modifier set), and **surfaced a real rare race** — under pooled subtree teardown ~1–2% of descendant `.OnUnmount` callbacks are missed (mounts always exact; recorded as a known issue for dedicated follow-up). **Verified:** all builds 0/0; full unit **9422 pass** (+REACTORGEN011 analyzer tests + `WrapLifecycle` generator tests); the 3 torture fixtures pass **8/8 deterministically**; the gallery launches with all 9 controls (`MountAndActivate ok`).

- **P5.29 (`wct-controls` multi-control gallery + `[WrapEvent]` analyzer + nullable event-arg projection):** generalized the single-control `wct-settings-card` sample into `samples/apps/wct-controls`, a **NavigationView gallery** modelled on the WCT sample app — a nav pane lists the controls and **each control gets its own page** (its own Reactor `Component`, mounted/unmounted on navigation), rather than cramming everything onto one page. Five WCT controls are wrapped with the generator and no hand-written wrapper code: `SettingsCard` (content child + click), `SettingsExpander` (child cards via the items slot), `Segmented` (two-way `SelectedIndex` via `[WrapControlled("SelectedIndex", ChangedEvent="SelectionChanged")]`), `RadialGauge` (two-way `Value` driven live by a Reactor `Slider`), and `CameraPreview`. **CameraPreview is imperative** (`await StartAsync(...)` after mount + a `PreviewFailed` event) — bridged declaratively via the generated `Setters` escape hatch + a per-page `UseRef` "started" guard (each page Component owns its guard, so the preview restarts cleanly on re-navigation), with `PreviewFailed` projected to `OnPreviewFailed` through `[WrapEvent("PreviewFailed", Arg="Error")]`. **Two general generator improvements fell out:** (1) the projected event-arg type now preserves nullable-reference annotations (`NullableFqnFormat`), so `PreviewFailedEventArgs.Error` (`string?`) surfaces as `Action<string?>` instead of `Action<string>` + a CS8604 in the generated trampoline; (2) a new **`WrapEventAnalyzer`** (REACTORGEN009 — `EventName` is not a public event of the control; REACTORGEN010 — an `Arg`/`Args` entry is not a public property of the event's argument type) closes the last unvalidated Wrap\* attribute — every Wrap\* attribute now has an attribute-site analyzer (REACTORGEN002–010), so a typo'd event/arg is a clear diagnostic instead of a cryptic generated-code error. Also fixed two stale `<see cref>` doc-comments left by earlier migrations (`ToggleButtonDescriptor`→`ToggleButtonElement`, `FlipViewDescriptor`→`FlipViewElement`) so the core library builds 0-warning. **Verified:** sample builds **0 warnings / 0 errors** and launches (`MountAndActivate ok`, the gallery + first page mount; CameraPreview degrades gracefully via `PreviewFailed` when no camera is present); Reactor library builds 0/0; full unit **9415 pass** incl. 6 new `WrapEventAnalyzer` tests; 35 in the generator suite.
- **P5.28 (`[WrapDecorator]` monomorphic-decorator capability + XamlPage/XamlHost migration):** added a second decorator-emitting mode — the *monomorphic* sibling of `[WrapPolymorphic]` — and migrated the last two built-in decorators. `[WrapDecorator(create, OnUpdate=, OnUnmount=)]` (with `[GenerateReactorDescriptor(typeof(TControl))]`) emits an `IDecoratorElementHandler<TElement>` + Pattern-A `RegisterDecorator<TElement>` cctor for a control that is **created once and mutated in place** (never re-created/type-swapped). Mount calls `Create(element)` + tags; Update casts the existing control, runs the optional `OnUpdate(old,new,control)` in-place mutation, re-tags, and returns the **same** instance; Unmount runs the optional `OnUnmount(control)` teardown, then `DetachReactorState` + returns `SkipPool` (the author-owned interop-host disposition). Branches before `IsValidTarget`/`CollectMembers` (the `Create` method owns construction; the control type need not be generator-instantiable). This is the right shape for XamlPage/XamlHost — `[WrapPolymorphic]`'s re-resolve-every-update model would wrongly re-create their control. **Migrated** (`src/Reactor/Hosting/XamlInterop.cs`): `XamlPageElement` (Create=`new Frame()`+`Navigate`, OnUpdate=conditional re-`Navigate`, OnUnmount=`Content=null`) and `XamlHostElement` (Create=`Factory()`+`Updater`, OnUpdate=`Updater`); both records made `partial`, their hand-written static cctors removed (the generated cctor self-registers), and `XamlPageDescriptor.cs`/`XamlHostDescriptor.cs` **deleted**; bootstrap repointed to `RunClassConstructor`. The per-host `XamlInterop.Register(reconciler)` fallback (inlined `RegisterType`) is unchanged and still valid. **Verified:** builds clean; unit **9389 pass** (+2 `WrapDecorator_*` generator tests, 29 in the generator suite); generated `XamlPageElement.Decorator.g.cs`/`XamlHostElement.Decorator.g.cs` are line-for-line equivalent to the deleted handlers; selftests **0 failures** (`CoreCov2_XamlHostMount` real mount+update, `Hosting_XamlInteropRegister` per-host path, `Spec048_RegDecorator_XamlPage`/`_XamlHost`/`_Icon` registration). **All three spec-048 §3.4 single-control decorators (Icon via `[WrapPolymorphic]`, XamlPage+XamlHost via `[WrapDecorator]`) are now source-generated** — the only remaining hand-written decorators are the generic/templated base-derived ones (TemplatedList family etc., which need a base-derived-emission capability).
- **P5.27 (`[WrapPolymorphic]` generator capability + Icon migration):** added the first **decorator-emitting** generator mode, unblocking the `Icon` decorator that P5.26 flagged as fundamentally polymorphic. `[WrapPolymorphic(resolve, Reconcile=, EmptySentinel=)]` (alongside `[GenerateReactorDescriptor(typeof(TControlBase))]`, where `TControlBase` is the common base all resolved controls derive from) makes the generator emit an `IDecoratorElementHandler<TElement>` (NOT a `ControlDescriptor`) + a Pattern-A `ControlRegistry.RegisterDecorator<TElement>` cctor. **Mount** calls the author's `static TControlBase? Resolve(TElement)` (instead of `new TControl()`), falls back to the `EmptySentinel` placeholder (default empty `TextBlock`) on null, then `SetElementTag` + (if a `Setters` member exists) `ApplySetters`. **Update** re-resolves and rebuilds when the runtime control type changed **or** the optional `static bool Reconcile(old,new,TControlBase)` same-subtype patch returns false; otherwise it patches in place. The capability branches **before** `IsValidTarget`/`CollectMembers`, so the base control type need not be concrete/instantiable and no value props are surfaced (the resolver + reconcile do everything). **Icon migrated:** `IconElement` annotated in `src/Reactor/Core/Element.Icon.cs` with `ResolveIcon` (→ `IconResolver.ResolveIconForDescriptor`) + `PatchIcon` (the per-subtype switch); the hand-written `IconDescriptor.cs` **deleted**; the `Dsl.cs` `IconRegistration` shim removed (the generated cctor self-registers on `new`); the test bootstrap repointed to `RunClassConstructor(typeof(IconElement))`. **Verified:** generator + Reactor + host build clean; full unit **9385 pass** + 2 new generator unit tests (`WrapPolymorphic_*`, 27 total in the suite); Icon selftests **0 failures** (incl. `RBC_IconDataResolveVariants`, `RBC_CmdBarToggleIconUpdate`'s `TglIcon_UpdatedToFontIcon` **type-change rebuild**, and `Spec048_RegDecorator_Icon` registration); parity holds 67/75. **XamlPage/XamlHost remain hand-written** — they are *not* polymorphic (XamlPage is monomorphic `Frame` with a navigate side-effect; XamlHost is an arbitrary `Factory()` control); collapsing them would need a broader "custom-lifecycle decorator" mode, not `[WrapPolymorphic]`.
- **P5.0 (this scoping):** decide attach-mode trigger + `[WrapConvert]` + registration-naming swap. ✅
- **P5.1 (done):** descriptor-only generation mode. `[GenerateReactorDescriptor(typeof(Control))]` on an existing record emits **only** the `Descriptor` + Pattern-A registration cctor (no init-props, no factory) into the record's partial. The mode is **record-driven**: it discovers the full control surface but filters to the members the record actually declares (value props by name, the content/`Children`/`Items` slot, `On{Event}` callbacks, `Setters`) — so it never references a member the record lacks (e.g. it must not surface `FrameworkElement.Loaded` as `e.OnLoaded`). Registration is Pattern-A self-registration: the static cctor fires when the existing hand-written factory does `new()`, so the migration just deletes the factory's `V1.Reg<>` line. Proven by a generator unit test (record-driven shape) **and** a real compile-proof (`tests/Reactor.AppTests.Host/SelfTest/Fixtures/DescriptorOnlyProof.cs` — a hand-written record + `[GenerateReactorDescriptor]` whose emitted descriptor compiles against the real Reactor descriptor types). 38 generator unit tests green; full unit suite + wrapper selftests green.
- **P5.2 (done):** general `[WrapConvert("Prop")]` — surfaces a struct-typed control property (CornerRadius/Thickness/GridLength/…) through an ergonomic scalar element prop, written via the struct's **single public one-argument constructor** (the element value type is inferred from that ctor's parameter, e.g. `CornerRadius` ⇒ `double`). Always a skip-write `OneWayConditional` (no dp-ClearValue, matching the hand-written descriptors). General across controls — not a per-control patch. Analyzer `REACTORGEN008` validates the target. Proven by a generator unit test (normal mode) **and** the descriptor-only compile-proof extended to `new CornerRadius(v)` against the real WinUI struct.
- **P5.3 (Viewbox done; Border deferred):** first built-in migrated to a generated descriptor. **Viewbox** — the generator is wired onto `src/Reactor` (build-time analyzer, not packed); `ViewboxElement` is annotated `[GenerateReactorDescriptor(typeof(Viewbox))]`; the hand-written `ViewboxDescriptor`/`ViewboxDescriptorHandler` are **deleted**; the factory drops its `V1.Reg<>` line (the generated Pattern-A static cctor self-registers on `new`); the test registrar `tests/_shared/BuiltInHandlerBootstrap.cs` fires the cctor via `RunClassConstructor`. **Verified: full unit suite (9269) green; the live `CovBoost_ElementPoolCleanOnRent_Viewbox` selftest green (real WinUI mount + Stretch/StretchDirection + pool clean-on-rent — behavioural equivalence to the deleted descriptor).** Three generator refinements were required and are general: (a) descriptor-only mode **suppresses the dp-ClearValue channel** (records declare `T?`, not `Optional<T>`, and the built-ins use `OneWayConditional`); (b) it **omits `RegisterControlAssembly`** (built-in WinUI controls already have XAML metadata, and the call throws headless); (c) the emitted attribute XML docs were fixed (`<paramref>`→`<c>`, CS1734). **Border is deferred** — its hand-written `BorderHandler` is *directly instantiated and asserted* in `BorderPortTests` (`new BorderHandler()`) and named in `BuiltInHandlerBootstrap`, so deleting it would change tests; Border needs the per-control decision below. **E2E**: WinAppDriver is installed but the suite self-skips in a non-interactive session (and Viewbox has no e2e tests); the gate is environment-pending.
- **P5.26 (thorough analysis of the 12 non-migrated files — verdicts verified against the actual handlers/registrations):** confirmed each is genuinely non-migratable with today's generator (not dismissed — read the code). **GridView/ListView** are *confirmed* virtualizing: the registered hand-coded handlers use `ItemTemplate` + `ItemsSource = Enumerable.Range(0,N)` + `ContainerContentChanging` (mount-on-realize / unmount-on-recycle, `Children => null`), whereas the parallel `*Descriptor.cs` use an **eager** `ItemsHost` — migrating would lose virtualization. **Icon** is *fundamentally* polymorphic (`IconResolver` resolves & swaps between SymbolIcon/FontIcon/BitmapIcon/PathIcon/ImageIcon from `element.Data` — no single `TControl`); **XamlHost** = arbitrary `element.Factory()` control; **XamlPage** = always `Frame` (single-type but decorator-registered — the one that *could* fit a future "descriptor-backed decorator singleton" mode). **ItemsRepeater/ItemsView/LazyStack/TemplatedFlipView/TemplatedListView** are generic leaf records (`Element<T>`) over non-generic bases, registered via `RegBaseDecorator<TBase,THandler>` / `RegisterForDerivedTypes` with hand-coded templated/virtualized lifecycles (LazyStack is a **compound host** — ScrollViewer wrapping an ItemsRepeater — so no single `TControl`). **ElementReferenceComparer** is a helper; **CanvasDescriptor** is the strategy holder for the already-migrated Canvas. **Unblocking would require new generator capabilities** (decorator-emission + polymorphic resolution; generic/base-derived descriptor emission + `RegBaseDecorator`/`RegisterForDerivedTypes`; a virtualizing items-host strategy; compound/multi-control host descriptors) — none currently justified. **The `Descriptors\\` folder is exhausted of controls migratable without a substantial new capability or a behavioral regression.**
- **P5.25 (full Descriptors\\ sweep — migrated the last 7 migratable controls):** swept every remaining file in `Descriptors\` and migrated all 7 that were genuinely migratable (correcting earlier "composite/items-gap" mis-labels). **BreadcrumbBar** — Items projected to `ItemsSource` (label list) + `ItemClicked` (reads `el.Items[Index]`). **SelectorBar** — Items build `SelectorBarItem`s (structural comparer) + value-diff `SelectedIndex` (with the `Optional.Of(-1)` force-clear). **SwipeControl** — `Content` auto (SingleContent) + Left/Right swipe items via `.Imperative`. **SemanticZoom** — 2 NamedSlots (ZoomedIn/OutView, `ISemanticZoomInformation` cast). **FlexPanel** — `[WrapPanelChildren]` per-child flex attached props + 7 auto props + `FlexPadding` (non-nullable Thickness) Manual. **TreeView** — `TreeChildren` Nodes strategy + 3 auto props + `AllowDrop` (base UIElement prop) + ItemInvoked/Expanding events. **TabView** (the largest — Customize in `Element.TabView.cs`) — `TabItemsHost` (pinnable headers/icons), value-diff `SelectedIndex`, TabStripHeader/Footer `.ImperativeBridged` slots, and the close/add/drag-start/drag-complete events; 6 props auto. **The remaining 12 files are confirmed NON-migratable:** `ElementReferenceComparer` (helper), `CanvasDescriptor` (strategy holder for the already-migrated Canvas), `GridView`/`ListView` (hand-coded **virtualizing** handlers — would lose virtualization), `Icon`/`XamlHost`/`XamlPage` (**decorators** — `IDecoratorElementHandler`, spec-048 §3.4 blocker), and `ItemsRepeater`/`ItemsView`/`LazyStack`/`TemplatedFlipView`/`TemplatedListView` (generic/templated controls registered via `RegBaseDecorator<TBase,THandler>` with hand-coded lifecycle handlers). **Verified:** generator + Reactor + host build clean; full unit **9385 pass**; selftests **0 failures** — BreadcrumbBar (unit), `IdentityPreserve_SelectorBar_*`, `Swipe_*`, `SemZoom_*`, Flex (172 fixtures), `TreeView_*`/`TVHandlers_*`, `IdentityPreserve_TabView_*`/`TabViewSel_CallbackFired`/`NativeDock_TabView_*`. Parity holds 67/75 (all were already PASS — the win is real hand-written-code deletion). **The Descriptors\\ folder is now exhausted of migratable controls.**
- **P5.24 (SemanticElement — re-categorized from "composite" to migratable):** the accessibility `SemanticPanel` wrapper, earlier mis-filed as a genuine composite. It's actually a single-control descriptor: `Child` is a Children-backed `SingleContent` (the panel uses a `Children` collection not a `Content` prop → SetChild does `Children.Clear()`+`Add` → overwrite `d.Children`), and 6 control props are **projected from the nested `Semantics` (`SemanticDescription`) record** (`e.Semantics.Role`→`c.SemanticRole`, etc.) — both bespoke → `[WrapManual("Child")]`+`[WrapManual("Semantics")]` + Customize. Added a `Setters` prop; registration in `ElementExtensions.cs` (production) + bootstrap → `RunClassConstructor` (`global::System`). **Verified:** build clean; full unit **9385 pass** (one unrelated `PersistenceEtwBridgeTests` ETW flake, 9/9 isolated); Semantic selftests (`A11y_SemUpdate_RoleUpdated`/`_ValueUpdated`/`_RangeMaxUpdated`/`_IsReadOnlyUpdated`/`_SamePanelInstance`, `A11y_Modifier_*`) **0 failures**. Lesson: a control backed by a single `TControl` with **projected** props (read from a nested record) is migratable via Customize — NOT a multi-control composite, even when the audit shows a content-model mismatch.
- **P5.23 (moderate singles — all 8: Path, Expander, SplitView, CalendarView, TeachingTip, NavigationView, TitleBar, AnnounceRegion):** migrated every remaining moderate single-control. **Path** — Shape leaf; Fill/Stroke/thickness/transform/caps/join auto-map, the 3-strategy `Data` write (`.Imperative`: XamlReader→Geometry→PathDataParser), `FillRule` (onto inner PathGeometry) and `StrokeDashArray` (DoubleCollection) bespoke. **Expander** — `Content`+`HeaderTemplate` both Element so overwrite `d.Children` with `SingleContent`; `HeaderTemplate` `.ImperativeBridged`, gated string `Header`, `IsExpanded` counter-echo + `Collapsed`; `ContentTransitions` Excluded. **SplitView** — `Pane`+`Content` NamedSlots; `PaneBackground` ref-comparer; twin `PaneOpening`/`PaneClosing`. **CalendarView** — `SelectedDates` `.CollectionDiffControlled` (keyed by UtcTicks), `Language` IsWellFormed gate, `Min/MaxDate`/`FirstDayOfWeek` nullable→`.Value`. **TeachingTip** — `Content`+`HeroContent` NamedSlots, `Target` `.Reference` (ElementRef→FrameworkElement), `Subtitle` clear-on-null, IconSource ref-comparer. **NavigationView** — the big one: 5 NamedSlots (incl. the `AutoSuggestBox` typed slot), the ~150-line `MenuItems`+`SelectedTag` reconciler (`.Imperative` — in `Element.NavigationView.cs`), 3 NaN-sentinel pane widths, `SelectionChanged`/`BackRequested`. **TitleBar** — `Content`+`RightHeader` NamedSlots, `Icon`→`IconSource`, the `window.SetTitleBar`/`ExtendsContentIntoTitleBar` `.Imperative` (timing-sensitive, verified by `TBInset_RightPaddingColumnNonZero`), 2 events. **AnnounceRegion** — the internal hook-registered record (UseAnnounce.cs): made `partial` + added `Setters`, the whole control is one `.Imperative` live-region setup + `AnnounceHandle` wire; `Reg<>` → `RunClassConstructor`. 8 descriptors + handlers deleted, registrations repointed, trim/optional-test refs fixed; `D3Charts.cs`/`UseAnnounce.cs` `RunClassConstructor` needed `global::System` (their `Microsoft.UI.System` import shadows `System`). **Gotcha (now a memory):** a control with **2+ Element-typed slot members** must `[WrapManual]` each — auto single-content detection bails (ambiguous) and the members otherwise leak as mistyped value props (caught on NavigationView). **Verified:** Reactor + host build clean; full unit **9385 pass**; selftests **0 failures** — incl. `UpdatePath_*`, `ExpanderUpdate_HeaderTemplate`/`_CallbacksFire`, `IdentityPreserve_SplitView_*`, `CalendarView_*`, `TeachingTip_TargetReferenceEqualsButton`/`Issue343_Tip_ContentReconciled`, `NavViewSel_TargetItemFound`, `TBInset_*`/`TitleBar_ImplicitExtends`, `ChartA11y_LiveRegion_*`. Parity holds 67/75 (all were already PASS — the win is real hand-written-code deletion).
- **P5.22 (button/toggle family — all 6: Button, CheckBox, ToggleButton, DropDownButton, SplitButton, ToggleSplitButton):** migrated the full button cluster, following the migrated RadioButton precedent (`[WrapAlias("Label","Content")]` + `[WrapManual]` + Customize). **CheckBox** — multi-event `IsChecked` (`Checked`/`Unchecked`/`Indeterminate`) `.Controlled<bool?,…>` with a dual callback (`OnCheckedStateChanged` OR `OnIsCheckedChanged` when `HasValue`); `CheckedState` folds in; `IsThreeState` auto. **ToggleButton** (`WinPrim.ToggleButton`) — `IsThreeState` written BEFORE `IsChecked` (ordering invariant → both in Customize), `IsChecked` source is `CheckedState` in 3-state mode, `Click` fires both callbacks. **DropDownButton/SplitButton/ToggleSplitButton** — the `Label`→`Content` alias **suppresses the auto content slot** so the `Element? Flyout` (which would otherwise be mis-detected as content) is handled as a `[WrapManual]` `OneWayBridged` (`CreateFlyoutForDescriptor` + `ElementReferenceComparer`); SplitButton adds a `Click` HandCodedEvent, ToggleSplitButton a `.Controlled<bool,…>` `IsChecked`. **Button** — fully bespoke: a **guarded** `SingleContent` (overwrites the auto unguarded one so a string Label isn't clobbered) + `Label` gated on `ContentElement is null` + `IsEnabled` gated on `!IsDisabledFocusable` + the `IsDisabledFocusable` coercion (force IsEnabled=true + Opacity 0.4 / ClearValue) + `Click` suppressed when focusable-disabled. Events that would auto-surface (`Click`, `IsCheckedChanged`) are `Exclude`d. 6 descriptors + handlers deleted, `V1.Reg<>` dropped, trim entries removed, `*OptionalTests` repointed. **Verified:** Reactor + host build clean; full unit **9385 pass**; selftests **0 failures** — CheckBox (`EchoSuppress_CheckBox_*`, the full `OptionalTriStateCheckBox_Trans_Phase0..3` three-state matrix), Button (`DF_OnClickSuppressed`/`DFT_*` focusable-disabled), SplitButton/DropDownButton (`*_Command_*`, Flyout), ToggleSplitButton (`ValueDiff_ToggleSplitButton_*`, `Desc_…_SecondEventNotSwallowed`).
- **P5.21 (items controls — ComboBox, ListBox, Pivot; ListView/GridView intentionally skipped):** migrated the descriptor-registered items controls. Each overwrites or keeps the children strategy + handles its `SelectedIndex` echo in `Customize`. **ComboBox** — the prior-session revert risk (the dual-source `ItemsHost` where `ItemElements` Element[] takes precedence over `Items` string[]) is preserved by **overwriting `d.Children`** in Customize (the generator's auto single-source host is created-then-discarded, the Canvas-precedent pattern); SelectedIndex is the **value-diff** echo (`valueDiffEcho: true`); 2 DropDown events; PlaceholderText (`?? ""` clear-on-null) + MaxDropDownHeight (NaN-sentinel) `[WrapManual]`; Header/IsEditable/Description auto. The `IdentityPreserve_ComboBoxElements_*` selftests (which **caught the original revert**) pass. **ListBox** — single-source items auto-map (no overwrite); SelectedIndex is the **causal-counter** echo (`ShouldSuppress`, not value-diff) with a twin-invoke trampoline firing `OnSelectedIndexChanged` + the multi-select `OnSelectionChanged` snapshot + the `NoOpSelectedIndexChanged` gate sentinel. **Pivot** — overwrites `d.Children` with the typed `TabItemsHost<…, PivotItemData>` (each datum → a `PivotItem` container); value-diff SelectedIndex (reuses `FlipViewEventPayload`); Title auto-maps. **ListView + GridView were deliberately NOT migrated** — they register **hand-coded *virtualizing* handlers** (`ListViewHandler`/`GridViewHandler`); their `*Descriptor.cs` are non-registered simpler parallel variants, so migrating to the generated descriptor would **lose virtualization** (a real regression). 3 descriptors + handlers deleted, `V1.Reg<>` dropped, trim entries removed, `*OptionalTests` repointed to `XxxElement.Descriptor`. **Verified:** Reactor + host build clean; full unit **9385 pass**; ComboBox (incl. `IdentityPreserve_ComboBoxElements_*`, `ValueDiff_ComboBox_*`) + ListBox (incl. `IdentityPreserve_ListBox_*`, `ListBoxWrapper_SelectedIndexWritten`) + Pivot (`IdentityPreserve_Pivot_*`, `PivotSel_*`) selftests **0 failures**.
- **P5.20 (InfoBar):** Severity/IsOpen/IsClosable + the `Content` slot (auto `SingleContent`) auto-map; `Title`/`Message` stay `[WrapManual]` to preserve the `?? ""` clear-on-null `OneWay` (a `string?` would auto-emit `OneWayConditional`, leaving a **stale title on pooled reuse**); `IconSource` (`IconResolver` + reference comparer), `ActionButtonContent` (dynamic inner `Button` + `Click` resolved through the live element tag) and the `Closed` dismissal event are bespoke `Customize` entries; `Closed` is `Exclude`d (the record's `OnClosed` would otherwise auto-surface it). Descriptor + handler deleted, `V1.Reg<>` dropped, trim-assertion entry removed. **Verified:** Reactor + host build clean; full unit **9385 pass**; InfoBar selftests (`InfoBarAction_Mounted`/`_Title`/`_HasActionButton`/`_MessageUpdated`) **0 failures**. **Honest stop point:** the remaining hand-written-but-PASS controls each carry subtle-regression risk the parity audit (value-props only) doesn't reveal — items controls (documented prior ComboBox `ItemElements`-identity revert), the button/toggle family (Flyout-mis-detected-as-content + three-state echo), TitleBar (NamedSlots + window `SetTitleBar` interaction), NavigationView (NamedSlot + selection), CalendarView (`SelectedDates` echo collection), templated/lazy lists, and Icon (decorator, spec-048 §3.4 blocker). These warrant per-control supervised effort.
- **P5.19 (generator capability — attached-property panels, `[WrapPanelChildren]`):** new attribute that wires the generator's already-emitted `Panel` children strategy to a per-child or two-pass attached-property hook, eliminating the hand-written strategy-holder + `[WrapManual("Children")]` + Customize-`d.Children=…` boilerplate. `[WrapPanelChildren(PerChild = "M")]` → `PerChildAttached = M` (a `static void M(TControl, UIElement, Element)` on the record); `[WrapPanelChildren(AfterAll = "M")]` → `PerChildAttachedAfterAll = M` (`static void M(TControl, IReadOnlyList<(UIElement, Element)>)`, the two-pass sibling-name shape). **Migrated all three attached-prop panels:** **Grid** (`PerChild` Row/Column/Span + `[WrapManual("Definition")]` for the RowDefinitions/ColumnDefinitions rebuild; RowSpacing/ColumnSpacing auto), **VariableSizedWrapGrid** (`PerChild` RowSpan/ColumnSpan + `[WrapManual]` on the three sentinel-guarded props `MaximumRowsOrColumns≥0`/`ItemWidth`/`ItemHeight` non-NaN; Orientation auto), **RelativePanel** (`AfterAll` sibling-name resolution — **zero value props, so a full hand-written descriptor collapses to one attribute + the bespoke hook**). The bespoke hook bodies + Grid/WrapGrid Customize entries live in a new `src/Reactor/Core/PanelAttachedHooks.cs` as `partial record` members (keeping Element.cs lean), reproduced verbatim from the deleted descriptors. Three descriptors + their `*DescriptorHandler` deleted; `V1.Reg<>` dropped (Pattern-A cctor). Two new generator unit tests (`WrapPanelChildren_Wires_PerChild_And_AfterAll_Attached_Hooks`, `WrapPanelChildren_AfterAll_Wires_TwoPass_Hook`). **Verified:** generator + Reactor + host build clean; full unit **9385 pass**; Grid (306) + RelativePanel (incl. `RPNamed_*` sibling-name, `PDM_RelPanel_StaleRightOfCleared`) + WrapGrid + Canvas (regression) selftests **0 failures**. Parity 67/75 holds (all three were already PASS — the win is real hand-written-code deletion + a reusable attached-panel capability, retrofittable to Canvas/FlexPanel).
- **P5.18 (real migrations of parity-PASS controls — Border, MediaPlayerElement):** the parity number (67/75) measures *expressibility*, decoupled from actual migration — many PASS controls still had hand-written descriptors. Started deleting that real code. **Border** — the cleanest remaining win (deferred since P5.3 only because `BorderPortTests` did `new BorderHandler()`): `[WrapContent("Child")]` (single-content slot) + `[WrapConvert("CornerRadius")]`/`[WrapConvert("BorderThickness")]` (double→struct) + auto Background/BorderBrush (Brush references); deleted **both** the hand-coded `BorderHandler` *and* the parallel hand-written `BorderDescriptor` (they were verified-equivalent), repointed the port test to assert `BorderElement.Descriptor.Children is SingleContent`. **MediaPlayerElement** — `AreTransportControlsEnabled`/`AutoPlay` auto-map; `Source` is a mount-only `.Initial` (string→`MediaSource.CreateFromUri`) and the inner-`MediaPlayer` event wiring (`MediaOpened`/`MediaEnded`/`MediaFailed`, UI-thread marshalled) is a mount-only `.Imperative` — both bespoke, moved into a `Customize` hook + two private static helpers; the events live on the inner `MediaPlayer` (not the control) so the generator never auto-surfaces them. Both descriptors + their `*DescriptorHandler` deleted, `V1.Reg<>` dropped (Pattern-A cctor), trim-assertion entries removed. **Verified:** Reactor + host build clean; full unit **9383 pass**; Border selftests (`V1_Border_Mounted`/`_HasChild`/`_CornerRadius`/`_ChildSwapped`, `BorderBrush_*`, `BorderMod_*`) + MediaPlayer selftests (`Media_PlayerMounted`/`_Updated`) **0 failures**. **Remaining hand-written-but-PASS controls** are bespoke-dominated (items controls' HandCodedControlled echo; SplitView NamedSlots; Expander path-B echo; the button/toggle family's polymorphic content + Flyout-bridge + three-state; templated/lazy lists) — each a focused per-control effort, low net auto-mapping payoff.
- **P5.17 (parity push — 54 → 67/75, audit-modeling of bespoke-but-expressible controls):** raised the parity audit by adding **honest** patches for 13 controls whose bulk auto-maps but which carry a few genuinely-bespoke props/slots verified against each hand-written descriptor: **button family** — Button (`Manual:[IsEnabled,IsDisabledFocusable]` focusable-disabled gating + `Content:"Content"` polymorphic slot, since `Label`→`Content` alias suppresses the auto child-slot), DropDownButton/SplitButton/ToggleSplitButton (`Content:"Flyout"` single child slot), CheckBox/ToggleButton (`Manual:[CheckedState]` three-state second binding); **others** — Grid (`Manual:[Definition]` Row/ColumnDefinitions rebuild), Path (`Manual:[PathDataString,StrokeDashArray,FillRule]` SVG/geometry), InfoBar (`Manual:[ActionButtonContent]`), NavigationView (`Manual:[SelectedTag,AutoSuggestBox]` selection + NamedSlot), TitleBar (`Manual:[Icon]` IconResolver transform), CalendarView (`Manual:[Language,SelectedDates]`), TeachingTip (`Manual:[Target]` ElementRef→FrameworkElement resolution), AnnounceRegion (`Manual:[Handle]` mount-only wire, like FrameElement). Each `Manual`/`Content` was confirmed against the descriptor so the audit truthfully measures "expressible with realistic annotations." **The remaining 8 are NOT honestly patchable:** items-content controls whose backing control is not a panel/`ItemsControl` the generator detects (**BreadcrumbBar, SelectorBar, TreeView, SwipeControl** — a genuine items-host capability gap), and genuine multi-slot/composite controls (**NavigationHost** router on Grid, **Semantic**/**SemanticZoom** named-view composites, **TabView** tab-strip + drag + items). Patches are test-only (`ParityAudit.cs`); full unit **9383 pass**.
- **P5.16 (WebView2):** migrated WebView2 — `Source` (`Uri`) auto-maps, but all 4 events are bespoke typed-arg trampolines (`NavigationStarting` Uri-parse, `NavigationCompleted` reads `control.Source`, `WebMessageReceived` try/catch payload extraction, `CoreWebView2Initialized` parameterless), so they live in `Customize` as `HandCodedEvent`s on the shared `WebView2EventPayload`. The 4 control events are `Exclude`d (would auto-surface as fire-forget and mismatch the `Action<Uri>`/`Action<string>`/`Action` callback shapes); `Source` is `[WrapManual]` solely to trigger the Customize hook (it is the only value prop). Descriptor + `WebView2DescriptorHandler` deleted, `V1.Reg<>` dropped (Pattern-A cctor), `WebView2DescriptorHandler` trim-assertion entry removed. **Verified:** Reactor + host build clean; full unit **9383 pass**; WebView2 selftests (`MdHtml_WebView2Mounted`, `WV2_Updated`, …) **0 failures**.
- **P5.15 (generator capability — interface-typed value props):** generalised `IsSupportedReference` to **allow plain data interfaces** (`INumberFormatter2`, `ICommand`, …) as raw nullable one-way value props, instead of blanket-excluding every interface. The real exclusions are unchanged and now carry the full weight: delegates, arrays, data/control templates, anything implementing `IEnumerable` (collections), and UIElement-derived (content). Mirrored the relaxation in the ParityAudit's `Classify`/`IsSupportedReference`. **Subtle bug found + fixed by the new unit test:** the generator's `IEnumerable` guard checked `type.AllInterfaces` only — which does NOT include the type itself — so an `IEnumerable`-typed prop slipped through once interfaces were allowed (the audit's `IsAssignableFrom` correctly caught it, so the two diverged); added `type.Name == "IEnumerable"` to the guard. **Simplified NumberBox:** dropped `[WrapManual("NumberFormatter")]` and its hand-written `OneWayConditional` from the Customize hook — `NumberFormatter` (`INumberFormatter2`) now auto-maps. New generator unit test `Interface_Typed_Reference_Prop_Is_Surfaced_But_Collection_Interface_Is_Excluded` locks both arms (interface surfaced; collection interface excluded). **Verified:** generator + Reactor + host build clean; 47 generator/parity tests pass; full unit **9272 pass** (one unrelated `PersistenceEtwBridgeTests` ETW cross-test flake, 9/9 in isolation); NumberBox + numeric-family selftests **0 failures**. **Parity 53 → 54/75** (the relaxed interface support flipped one further control to full parity in addition to NumberBox keeping PASS via the now-auto-mapped formatter).
- **P5.14 (NumberBox — the hardest holdout, now done):** migrated NumberBox, which stacks every bespoke shape at once: a deferred suppress-counter `HandCodedControlled` Value echo (`ValueChanged`) PLUS a per-keystroke `.Immediate` observation (`NumberBox.TextProperty` change callback + a `Loaded` hook that finds the inner template TextBox and subscribes its `TextChanged`, both into `Reconciler.NumberBoxImmediateTextChanged`/`NumberBoxLoadedEnsureImmediateTextBox`), PLUS `CoercingOneWay` Min/Max (drop coercion-driven `ValueChanged` echoes), ordered Min/Max-before-Value. All four go in `Customize` (Customize-prepend preserves the coercion ordering invariant); `ValueChanged` is `Exclude`d (bespoke, would auto-surface as fire-forget and collide). `SpinButtonPlacement`→`SpinButtonPlacementMode` auto-maps via **`[WrapAlias]`** (first use of the alias attribute in a built-in migration); the other props auto-map. **The `MigratedDescriptors_DoNotSilentlyDropUnsupportedTypeProps` regression guard earned its keep:** it caught that `NumberFormatter` (`INumberFormatter2`) was not a generator-supported value type and would be silently dropped → moved it to `[WrapManual]`+`OneWayConditional` in Customize (**later auto-mapped — see P5.15**, which added interface support and removed that WrapManual). **Verified:** Reactor + host build clean; full unit **9272 pass**; all 20 NumberBox selftests (Echo Value + MinMax coercion, the per-keystroke `Immediate_*`, the `Desc_NumberBox_RealInput_SecondEventNotSwallowed` strand regression) + the `ControlledOptionalNumericFamily_NumberBox_*` Optional-gate/snap-back fixtures **0 failures**. **Parity 52 → 53/75.** This closes the deferred-text/numeric-echo bucket — the remaining unmigrated single-controls are decorator/composite or hand-coded-handler-port-test coupled.
- **P5.13 (deferred-text-echo bucket — RichEditBox, TextBox, AutoSuggestBox):** migrated the three remaining bespoke-text controls. None fit the simple `[WrapControlled(Deferred=true)]` flag (PasswordBox-style) cleanly: **RichEditBox.Text** is *document-based* (`Document.SetText`/`GetText`, not a property); **TextBox.Value** has a callback-name mismatch (`OnChanged`≠`OnValueChanged`), a `Value`→`Text` alias, an order dependency (AcceptsReturn/TextWrapping must precede Text for single-line `\r\n` stripping) and a 3-arg state-reading SelectionChanged; **AutoSuggestBox.Text** has a custom `args.Reason==UserInput` trampoline filter plus a *shared* `AutoSuggestBoxEventPayload` across all three of its events. So each keeps `[WrapManual]`+`Customize` for its bespoke Text/events while the generator auto-maps the rest (RichEditBox 8 props, TextBox 10, AutoSuggestBox the `Suggestions`/`QueryIcon`/`IsSuggestionListOpen`/etc.). **Key pattern (now a memory):** because a record's `On{Event}` callback makes the generator auto-surface that change event as a fire-forget event (emitting `__{Event}Trampoline`), you MUST `Exclude` the event when you handle it yourself in `Customize`, or you get a `__{Event}Trampoline` name collision (CS0102) and/or a parameterless-invoke type mismatch — the surface filter keys on `authorDeclared.Contains("On"+evt.Name)`, so TextBox.TextChanged is NOT auto-surfaced (its callback is `OnChanged`). Customize entries emit BEFORE the auto entries, exploited to preserve order dependencies (TextBox AcceptsReturn/TextWrapping → Text; AutoSuggestBox Suggestions → Text). Descriptors deleted; `V1.Reg<>` lines dropped (Pattern-A cctor self-registers); white-box `*OptionalTests` repointed to `XxxElement.Descriptor`. **Verified:** Reactor + host build clean; full unit **9272 pass**; TextBox/RichEdit/AutoSuggest/Echo (incl. the `OptionalEchoStrandRegression_*` for all three) selftests **0 failures**. **Parity 50 → 52/75** (RichEditBox `Manual=Text`, AutoSuggestBox `Manual=QueryIcon` both flip to PASS; AutoSuggestBox's `SelectedTag` self-type remains genuinely bespoke). NumberBox remains the hardest holdout (per-keystroke `.Immediate` stacking + Min/Max coercion + deferred echo).
- **P5.12 (PasswordBox + deferred-controlled capability):** new `[WrapControlled("Prop", Deferred = true)]` — emits the **suppress-counter** `HandCodedControlled` channel (a `ChangeEchoSuppressor.ShouldSuppress`-gated trampoline that re-reads the control value) instead of the synchronous value-diff `.Controlled`. **Migrated PasswordBox** (`Password` via Deferred; PlaceholderText/Header/MaxLength/PasswordRevealMode/PasswordChar auto-map). Full unit **9272 pass**; Password/Echo/ControlledOptional/TextInput selftests green (the suppress-counter echo behaviour is faithfully reproduced). Parity 50/75 (PasswordBox already a PASS). **Debugging note:** a self-inflicted edit bug (the deferred-branch edit accidentally dropped the value-diff branch's `.Controlled<{ValueType},{ArgsType}>(` opening line) produced malformed C# for *every* value-diff controlled control → `<invalid-global-code>`/CS0759 cascade; root-caused via the non-intermediatexaml `error CS` filter. This capability unlocks the deferred-text-echo bucket pattern (AutoSuggestBox/RichEditBox/TextBox deferred parts).

- **P5.11 (removed forwarding shims):** deleted the 12 `internal static class XxxDescriptor { Descriptor => XxxElement.Descriptor; }` forwarding shims (CalendarDatePicker, ColorPicker, DatePicker, FlipView, PipsPager, RadioButton, RadioButtons, RatingControl, RichTextBlock, Slider, TimePicker, ToggleSwitch) and repointed their white-box tests (the `*OptionalTests` + `RichTextBlockDescriptorTests`) directly at the public generated `XxxElement.Descriptor`. No more shim clutter; the generated `Descriptor` static is the single source. Full unit **9272 pass**; Reactor + host build clean. **Convention going forward:** descriptor-only migrations update white-box tests to `XxxElement.Descriptor` — never add a shim.

- **P5.10 (ScrollViewer; Icon ruled out):** migrated ScrollViewer — `Child`→`Content` (content-from-record), 5 non-nullable enum scroll props auto-map (unconditional), `ViewChanged` is a typed whole-args event (`[WrapEvent("ViewChanged")]` → `Action<ScrollViewerViewChangedEventArgs>`), and `Orientation` (a bespoke convenience with no ScrollViewer control property — never mapped by the hand-written descriptor) is `Exclude`d to preserve behavior. No Customize needed. Full unit **9272 pass**; Scroll selftests green. **Parity 49 → 50/75.** **IconElement ruled out** — it's a polymorphic *decorator* (`IDecoratorElementHandler`) resolving to 5 different WinUI control types (`SymbolIcon`/`FontIcon`/`BitmapIcon`/`PathIcon`/`ImageIcon`) from `Data` at runtime; it has no single `TControl`, and the generated Pattern-A registration can't register decorators (spec-048 §3.4). It stays hand-written.

- **P5.9 (RichTextBlock):** migrated via the `ClearValueOnUnset` capability — ~20 nullable styling props auto-map through the dp ClearValue channel (now reachable thanks to the P5.8 `FindDependencyPropertyMember` projection fix). The bespoke parts are a `Customize` hook: `ImperativeBridged` for the `Text`/`Paragraphs` block-list build/diff (`[WrapManual]`, preserves issue-#480 Route-A inline UI children) + `Padding` (the base `Element.Padding` modifier) on the dp ClearValue channel. A forwarding shim keeps the `RichTextBlockDescriptor.Descriptor` symbol the white-box `RichTextBlockDescriptorTests` binds against. Full unit **9272 pass** (incl. that white-box test + silent-drop guard); RichText/Markdown/Inline selftests green. **Parity 48 → 49/75** (RichTextBlock now PASS).

- **P5.8 (TextBlock + ClearValue capability):** new opt-in **`[GenerateReactorDescriptor(…, ClearValueOnUnset = true)]`** — in descriptor-only mode each NULLABLE record prop backed by a `{ControlProp}Property` DependencyProperty is routed through the `Optional<T>` + `dp.ClearValue` channel (Unset releases the local value to the theme/style chain) instead of the skip-write `OneWayConditional`. The generated get adapts the record's `T?` to `Optional<T>` (`HasValue ? Value : Unset` for value types, `is null ? Unset : prop` for references). **Migrated TextBlock** (14 props: `[WrapAlias]` Content→Text / Weight→FontWeight; 10 nullable styling props → ClearValue channel; Content/MaxLines/CharacterSpacing/TextDecorations unconditional). **Fixed a latent generator bug:** `FindDependencyProperty` only matched a `{Prop}Property` FIELD, but CsWinRT projects WinUI DependencyProperties as static PROPERTIES — so the dp lookup returned null for every built-in WinUI control. Added the property-aware `FindDependencyPropertyMember`, scoped to the ClearValue pass so full-wrapper Ellipse/Rectangle keep their existing `Brush?`/`double?` API (no Optional<T> churn). **Verified:** full unit suite **9272 pass**; the issue-#522 recycle-reset selftests (FontSize/FontWeight cleared on a reused control) + Markdown/Shape green. Parity audit stays 48/75 (TextBlock was already a patched PASS).

- **P5.7 (campaign round 2 + silent-drop guard):** migrated **AnimatedIcon** ([WrapManual] Source cast), **ItemContainer** ([WrapContent("Child")]), **ParallaxView** ([WrapContent("Child")] + [WrapManual] Source), **Frame** (multi-arg [WrapEvent]). New capability: **multi-arg `[WrapEvent(Args=new[]{…})]`** projects several event-args properties into a multi-parameter `Action<A,B>` callback (Frame.NavigationFailed → `Action<Type,Exception>`). **Latent bug caught by the parity audit:** `ParallaxView.Source : UIElement` was silently dropped (unsupported value type) — build/unit/selftests all passed; fixed with [WrapManual]. **New CI regression guard** `MigratedDescriptors_DoNotSilentlyDropUnsupportedTypeProps` fails when any `[GenerateReactorDescriptor]` record has a value-prop whose control property is an unsupported type not covered by `[WrapManual]`/`Exclude` (self-validating: asserts it inspected 20+ migrated controls). **Parity audit raised 43/75 → 48/75** by adding Exclude/Manual/Content fields to the audit's patch model (wired into the missing-prop + content checks) and patches reflecting the 6 migrated wrappers' real annotations. Full unit suite **9272 pass**; all affected selftests green.

- **P5.6 (typed events + more candidates):** added a new generator capability — **`[WrapEvent("EventName", Arg="ArgProperty")]`** — for typed fire-and-forget events. The generated trampoline projects `args.{Arg}` (or the whole args object when `Arg` is omitted) into the record's `Action<T>? On{Event}` callback, and it surfaces delegates the auto-discovery otherwise ignores (e.g. `ExceptionRoutedEventHandler`). Previously any typed callback failed to compile against the parameterless `On{Event}?.Invoke()` trampoline. **Migrated ImageElement** with it (`[WrapEvent("ImageFailed", Arg="ErrorMessage")]` for the `Action<string>` failure callback + `[WrapManual("Source")]` for the bespoke string→Uri→BitmapImage/SvgImageSource parsing + `Exclude="Stretch"` for the string-vs-enum gap; Width/Height/NineGrid + parameterless ImageOpened auto-map). **Also migrated InfoBadgeElement** (clean — one conditional `Value` prop; `Exclude="Icon"` for the string-vs-IconElement gap). All 9271 unit tests + Image/InfoBadge selftests green. This unlocks the broad class of composite controls whose primary blocker was typed events (NavigationView/TabView/AutoSuggestBox/BreadcrumbBar/…), though most of those carry additional bespoke aspects (items/selection) that still need per-control work. Remaining clean-ish candidate: **TextBlock** (~15 one-way props via `[WrapAlias]` Content→Text/Weight→FontWeight + the nullable→ClearValue DP path) — deferred pending verification that the generated descriptor reproduces the issue-#522 ClearValue-on-recycle behavior.

- **P5.5 (full-record generation):** the generator's full mode `[GenerateReactorWrapper]` can now generate a built-in's **entire** record (body + Setters), descriptor, registration cctor, and factory from one annotation — not just the descriptor. Two generator changes enabled it: (a) **`RegisterAssembly=false`** flag — skips the headless-unsafe `RegisterControlAssembly` call (built-in WinUI assemblies have no `IXamlMetadataProvider`, so the call throws at type-init in the headless host); (b) **event-gating fix** — `AutoDiscover=false` now suppresses auto-discovered *events* too (previously full-wrapping any `FrameworkElement` surfaced the whole `UIElement` event surface — ~20 `On{Event}` callbacks — which both bloated the API and broke `PublicApiSurfaceGuardTests`; this was a latent bug that would also have hit third-party WCT controls). **Converted EllipseElement + RectangleElement to full mode** (`AutoDiscover=false, RegisterAssembly=false, Include=[…]`); hand-written record bodies deleted; all 9271 unit tests + shape selftests green. **Viable only for pure-projection controls** (no curated defaults, no computed members, no bespoke props, no friendly-named content/positional params): LineElement is excluded by its curated `StrokeThickness=1` default; Viewbox by its friendly `Child` content param (full mode emits `Content`); ProgressRing by `IsIndeterminate => Value is null` + curated `Minimum=0/Maximum=100`. For curated/bespoke controls the hand-written record stays — it carries author intent (defaults, computed members, friendly names, bespoke props like `Button.IsDisabledFocusable`) the control surface cannot supply.

- **P5.4 (in progress):** expand the migration control-by-control. **Migrated (27):** [one-way 16] Viewbox, ProgressRing, ProgressBar, Ellipse, Rectangle, Line, AnnotatedScrollBar, AnimatedVisualPlayer, PersonPicture, StackPanel, MapControl, ScrollView, RefreshContainer, HyperlinkButton, RepeatButton, **Canvas** (attached-prop panel — bespoke `PerChildAttached` strategy retained in a `CanvasChildrenStrategy` holder, swapped in via `[WrapManual("Children")]`+Customize; Width/Height/Background auto); [controlled 8] ToggleSwitch, ColorPicker, CalendarDatePicker, DatePicker, TimePicker, RatingControl, RadioButton, Slider; [items 2] FlipView, RadioButtons; [handcoded 1] PipsPager (SelectedPageIndex via value-diff `[WrapControlled]`, replacing HandCodedControlled). **ComboBox reverted** (real `ItemElements` identity regression — caught by selftests); ListBox/Pivot deferred. **9 enablers proven:** record-type-driven one-way, `[WrapManual]`+Customize, `[WrapConvert]`, `Exclude`, demote-to-one-way (bug fix), forwarding shim, content-name-from-record, Customize-prepend, **settable `ControlDescriptor.Children`** (Customize can replace the children strategy — enables bespoke attached-prop panels). **Remaining ~15 are bespoke-dominated — generation yields little net reduction:** the other attached-prop panels do NOT pay off — RelativePanel generates **zero** value props (its whole descriptor is a bespoke two-pass sibling-name strategy), WrapGrid auto-generates only `Orientation` (its other 3 props use sentinel conditionals — `>=0` / `!IsNaN` — the record-type-driven channel can't express), FlexPanel/Grid are Yoga/definition-bespoke; SplitView/SemanticZoom would auto-gen ~5 trivial `.OneWay` props but need a ~40-line Customize re-hand-writing NamedSlots + twin HandCodedEvents; typed-arg events (Image/WebView2) / HandCodedControlled-deferred (TextBox/PasswordBox/NumberBox) / bespoke-composite / hand-coded-handler-port-test each need focused per-control effort for negligible generated output. Canvas was the last clean panel win (Children + 3 nullable value props). **Controlled coverage proven:** single-event override (`ToggleSwitch`), auto-pair (the 5 pickers/color/rating), multi-event + bool/bool? bridge via `[WrapManual]` (`RadioButton`) — all echo-suppression/value-diff selftests green. **Enablers/fixes:** record-type-driven one-way channel; `[WrapManual]` Customize hook (proven on real controls PersonPicture + RadioButton); `[WrapConvert]`; **demote-to-one-way** (latent `ProgressBar.Value` drop bug caught + fixed + guarded); `Exclude` for record-vs-control type-mismatched dead props (`TimePicker.ClockIdentifier`). **Coupling:** scan production `src/` (`D3Charts`) + the 26 `DescriptorOptionalCoverage` white-box tests (migrate via **forwarding shim**, zero test edits) — use the **grep tool**. **Deferred:** items controls (`ComboBox`/`ListBox`/`FlipView`/`Pivot` — HandCodedControlled echo path), `Slider`/`NumberBox` (coercion), hand-coded handlers (`Border`/`ListView`/`GridView` — port-test coupling), panel attached-props. Verified per batch: full unit suite 9271; per-control selftests green; full-suite flakes (TabView/window-placement) confirmed **environmental** via isolation passes.

- **P5.31 (authoring attributes extracted to `Reactor.Wrappers.Abstractions`):** the generator's marker attributes are no longer emitted via `RegisterPostInitializationOutput`; they now live in a dedicated assembly `src/Reactor.Wrappers.Abstractions` (public types, `IsPackable=false`, AOT-compatible) referenced by `src/Reactor`, `Reactor.AppTests.Host`, `WctControls`, and (transitively) `Reactor.Tests`. **Why:** the post-init copies leaked from `Reactor.dll` through `InternalsVisibleTo` into `Reactor.AppTests.Host` (which also runs the generator), colliding with the locally generated copy — **CS0436**, harmless-warning in Debug but a hard error under ADO Release `TreatWarningsAsErrors` (see §15.7 "IVT attribute collision"). The DLL is bundled into the `Microsoft.UI.Reactor` package's `lib/` via a `BuildOutputInPackage` target (no separate package, no NuGet dependency). The generator only binds the attributes by metadata name, so it needed no logic change beyond deleting the `AttributeSource` constant + post-init registration. **Tests:** new `WrapperGeneratorTests.Generator_DoesNotEmit_TheAuthoringAttribute` guards against re-introducing the post-init emission. A latent `CsWinRT1028` (non-`partial` `DescriptorOnlyProofControl`), previously masked by the CS0436 compile failure, was fixed in the same pass. **Verified:** `Reactor.AppTests.Host` builds clean in Release `x64` with `-p:TreatWarningsAsErrors=true` (the 13 CS0436 errors are gone).

### §15.7 Risks / open questions

- **Echo-suppression divergence** (§15.5) is the top behavioral risk; resolve per-control, retain where unequal.
- **Two implementations of `ControlDescriptor`/handler shape** (generated vs. the few retained hand-written) must keep registering identically through `V1.Reg<>` (no per-host duplicate-throw — `ControlRegistry` is first-wins idempotent, but the per-host `RegisterHandler` throws; the factory touch-site pattern already handles this).
- **Build-order / generator-on-core** — **RESOLVED (P5.3).** The generator runs as an analyzer on `src/Reactor` with no cycle (it has no runtime dependency on Reactor; it only emits source). Empirically validated: `src/Reactor` builds clean once the generator's emitted attribute XML docs were fixed (`<paramref>`→`<c>`, CS1734).
- **IVT attribute collision** — **RESOLVED (P5.31).** Originally the generator emitted the `internal` marker attributes via `RegisterPostInitializationOutput` into *every* compilation that ran it. Because `src/Reactor` also runs the generator, those copies landed in `Reactor.dll` and then leaked through `InternalsVisibleTo` into friend assemblies that *also* run the generator (`Reactor.AppTests.Host`), where the locally generated copy collided with the IVT-imported one — **CS0436**. This was latent in Debug (C# prefers the current-assembly type and emits only a *warning*, so consumers appeared to "build clean"), but it **broke ADO Release builds**, where `TreatWarningsAsErrors` promotes the warning to an error. **Fix:** the attributes were moved out of the generator into a single dedicated assembly, **`src/Reactor.Wrappers.Abstractions`** (public types in `Microsoft.UI.Reactor.Wrappers`), referenced by every consumer; the generator no longer emits them. With exactly one definition there is no duplicate to collide. The assembly is `IsPackable=false` but its DLL is bundled into the `Microsoft.UI.Reactor` package's `lib/` (via a `BuildOutputInPackage` target in `Reactor.csproj`), so it ships without a separate package and flows transitively to in-repo consumers. (The earlier feared code was **CS0436**, not CS0433 — a *source-vs-metadata* collision, not metadata-vs-metadata.)
- **Per-control test coupling** — the **decision for each migration.** Controls whose hand-written class is referenced *by name* in tests block clean deletion: (a) the 10 hand-coded handlers are `new`-ed in Spec-047 port tests (e.g. `new BorderHandler()`), and (b) `BuiltInHandlerBootstrap.cs` names every built-in's handler. (b) is the expected sync point (its own header says "keep in sync") — updating a migrated control's line to `RunClassConstructor(typeof(XxxElement).TypeHandle)` is infrastructure, not a coverage change. (a) is a real blocker: those controls must keep the hand-written handler as dead code, or accept a minimal port-test edit. **Prefer descriptor-backed, port-test-free controls first.**
- **`RegisterControlAssembly` headless throw** — **RESOLVED (P5.3).** The issue-#142 control-assembly registration in the generated static cctor throws for built-in WinUI assemblies in a headless host; descriptor-only mode omits it (built-ins already have XAML metadata).
- **dp-ClearValue vs the record's type** — **RESOLVED (P5.3).** Auto-discovery would route a dp-backed prop through `Optional<T>` + ClearValue, mismatching the existing record's `T?` and the built-in's `OneWayConditional`; descriptor-only mode suppresses the dp channel.

### §15.8 The parity ceiling and the `[WrapManual]` escape hatch

**`[WrapAlias]` is exhausted at 43/75.** Auditing all 32 non-passing rows: every remaining `name-mismatch` is **genuinely bespoke**, not a 1:1 rename, so no further `[WrapAlias]` patch is valid (verified against the hand-written descriptors):
- composite — one element prop writes **several** control props (`ScrollViewer.Orientation` → HorizontalScrollMode + VerticalScrollMode + ScrollBarVisibility);
- tuple/projection — `Frame.NavigationParameter` is projected with `SourcePageType` through `.Initial`;
- method-based — `RichEditBox.Text` ↔ `Document.SetText/GetText` (not a property);
- resolver-converted — `TitleBar.Icon` (string) → `IconSource` via `IconResolver.ResolveIconSource`;
- derived tri-state — `ToggleButton`/`CheckBox.CheckedState` (`bool?` with custom event handling);
- deliberately-unmapped — `InfoBadge.Icon` is a dead prop the hand-written descriptor never writes.

The remaining tail is **niche type gaps** (`IconElement`, `IAnimatedVisualSource2`, `IMediaPlaybackSource`, `DoubleCollection`). So 43/75 is the honest *auto-generation* ceiling; the existing 25 alias patches already capture every legitimate rename.

**`[WrapManual]` — author escape hatch for bespoke props (implemented).** Because those props can't be auto-inferred, the generator lets the author handle them in the partial while still generating everything else:

```csharp
[GenerateReactorDescriptor(typeof(ScrollViewer))]
[WrapManual("Orientation")]                 // exclude from auto-discovery
public partial record ScrollViewerElement
{
    // generator emits: private static partial ControlDescriptor<…> Customize(ControlDescriptor<…> d);
    private static partial ControlDescriptor<ScrollViewerElement, ScrollViewer> Customize(
        ControlDescriptor<ScrollViewerElement, ScrollViewer> d)
        => d.OneWayConditional<Orientation>(e => e.Orientation!.Value, (c, v) => { c.HorizontalScrollMode = …; c.VerticalScrollMode = …; }, e => e.Orientation.HasValue);
}
```

Mechanics: any `[WrapManual("Prop")]` (a) removes `Prop` from auto-discovery, and (b) routes the generated `Descriptor` through an author-implemented `partial Customize(d)` hook (mandatory when present), where the author chains the bespoke entries. `ControlDescriptor`'s fluent methods mutate-and-return-self, so `=> d.X().Y()` composes cleanly. This keeps **auto-parity honest (43/75)** while making **migration practical for the bespoke controls** — the generator does the regular props, the author does only the irreducibly-bespoke ones. Proven by a generator unit test + the `DescriptorOnlyProof.cs` compile-proof (a `[WrapManual]` prop handled in `Customize`, compiling against the real descriptor types).
