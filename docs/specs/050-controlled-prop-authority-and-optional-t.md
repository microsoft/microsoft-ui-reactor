# `Optional<T>` and the controlled-prop authority model — Design Proposal

## Status

**Proposed — design converged from issue [#494](https://github.com/microsoft/microsoft-ui-reactor/issues/494) discussion (2026-05-31).** Not yet implemented. Single-PR scope; no phasing.

This spec supersedes the original "switch `.Controlled` to lazy-assert" proposal embedded in #494. The design discussion went further and landed on **enforce-by-API**: rather than tweaking the runtime gate, the descriptor system stops accepting plain-`T` getters for `.Controlled`. Authors must use `Optional<T>` to express controlled-prop authority. The customer's #494 bug class becomes structurally impossible.

### Resolved decisions (2026-05-31)

| # | Question | Resolution |
|---|---|---|
| Q1 | Implicit `T → Optional<T>` conversion? | **Ship.** Required for `with { Prop = value }` ergonomics; documented gotcha for ref-type Optionals receiving explicit `null`. |
| Q2 | Pre-implementation selftest baseline? | **Required.** §11.4. Establishes a regression-detection baseline against today's force-assert behavior; any selftest that fails post-migration is investigated as either a real fix or a test that depended on a footgun. |
| Q3 | `dp:` parameter on OneWay+Optional required? | **Optional at call site, public analyzer `REACTOR0050` warns when omitted.** §6.3. |
| Q4 | `Prop.InitialOnly` shipping window? | **Ship in same PR.** §7. Used sparingly — `Optional<T>` covers most cases. |
| Q5 | AOT closed-generic cap on `Optional<T>`? | **No cap.** §8.5. ~25 instantiations is well within ordinary generic-struct usage. |
| Q6 | `Optional<T>.Unset` vs `.None`? | **`Unset`**, with `HasValue` / `Value` properties matching `Nullable<T>` muscle memory. §4.1. |
| Q7 | Plain-`T` `.Controlled` overload — keep, `[Obsolete]`, or delete? | **Delete outright.** §5. No third-party descriptor authors exist today; no migration aid needed. |
| Q8 | Text-input props (`TextBox.Value` etc.) — `Optional<T>` or `Prop.InitialOnly`? | **`Optional<T>`** for uniformity. §6.2. The "type and finish" idiom is expressed via `Optional<string>.Unset` + a `defaultValue:` factory shortcut, keeping the entry-shape surface minimal. |
| Q9 | Customer-bug fix coordination | **Single PR.** No phasing, no Tier 1/2/3 split. The "right" model lands once. |

---

## Table of Contents

- [§1 Motivation — the customer bug and what it reveals](#1-motivation--the-customer-bug-and-what-it-reveals)
- [§2 Why React's force-assert default doesn't translate without help](#2-why-reacts-force-assert-default-doesnt-translate-without-help)
- [§3 WinUI's `ClearValue` primitive and what it lets us express](#3-winuis-clearvalue-primitive-and-what-it-lets-us-express)
- [§4 The `Optional<T>` primitive](#4-the-optionalt-primitive)
- [§5 The enforce-by-API design](#5-the-enforce-by-api-design)
- [§6 `.Controlled` with `Optional<T>` — the controlled-prop model](#6-controlled-with-optionalt--the-controlled-prop-model)
- [§7 `Prop.InitialOnly` — the `defaultValue` analog](#7-propinitialonly--the-defaultvalue-analog)
- [§8 Memory footprint analysis](#8-memory-footprint-analysis)
- [§9 Migration scope — counts and built-in record changes](#9-migration-scope--counts-and-built-in-record-changes)
- [§10 Hand-coded handler alignment](#10-hand-coded-handler-alignment)
- [§11 Testing](#11-testing)
- [§12 Documentation](#12-documentation)
- [§13 Implementation plan](#13-implementation-plan)
- [§14 Alternatives considered](#14-alternatives-considered)
- [§15 Open questions](#15-open-questions)
- [§16 References](#16-references)

---

## §1 Motivation — the customer bug and what it reveals

A customer porting `BasemapGallery` to the `ControlDescriptor` shape filed [#494](https://github.com/microsoft/microsoft-ui-reactor/issues/494):

```csharp
public static readonly ControlDescriptor<BasemapGalleryElement, BasemapGallery> Descriptor =
    new ControlDescriptor<BasemapGalleryElement, BasemapGallery>
    {
        // ...
    }
    .Controlled<BasemapGalleryItem?, BasemapGalleryItem?>(
        get:      e => e.SelectedBasemap,            // <-- plain T
        set:      (c, v) => c.SelectedBasemap = v,
        callback: e => e.OnBasemapSelected,
        readBack: c => c.SelectedBasemap,
        // ...
    );
```

Customer component:

```csharp
var (gridStyle, setGridStyle) = UseState(false);
return Grid(/*...*/,
    BasemapGallery(map) with {
        GalleryViewStyle  = gridStyle ? Grid : List,
        OnBasemapSelected = (b) => { },        // empty body — NO state binding
        // SelectedBasemap intentionally not set
    },
    ToggleSwitch(gridStyle, b => setGridStyle(b), ...)
);
```

**Repro:** user picks a basemap → toggles the switch → selection silently clears.

### 1.1 Root cause

Both `ControlledPropEntry.Update` (`src/Reactor/Core/V1Protocol/Descriptor/PropEntry.cs:292-322`) and `HandCodedControlledPropEntry.Update` (`PropEntry.cs:478-516`) gate writes on **control-vs-element drift** and force-write whenever they differ:

```csharp
var nv      = _get(newEl);
var current = _readBack(ctrl);
if (_comparer.Equals(current, nv)) return;
// arm echo + write
```

When the toggle re-renders the subtree:

- `nv = newEl.SelectedBasemap = null` (record default — never set in `with { }`)
- `current = readBack(ctrl) = X` (user's actual selection)
- They differ → writes `null` → user's selection clobbered.

The descriptor never has the information to distinguish "the author meant to assert null" from "the author didn't set this prop." Both look identical at the call site (`with { /* not set */ }` and `with { SelectedBasemap = null }` produce the same record).

### 1.2 The deeper observation

This is not a bug in the gate logic. Force-assert *is* the right semantic for "this descriptor controls this value." The bug is that the descriptor system has no way to express *"this prop might be unset, and unset means control owns it."* The customer's record has `BasemapGalleryItem? SelectedBasemap { get; init; }`, where `null` carries two contradictory meanings:

1. "I'm explicitly asserting null" (clear selection).
2. "I haven't touched this prop" (control owns it).

C# records can't distinguish these at runtime. We need a runtime primitive that *can* — that's `Optional<T>` (§4).

---

## §2 Why React's force-assert default doesn't translate without help

React solved the analogous problem by force-asserting `value` on every render and warning loudly when `value` is passed without `onChange`. That works in React because **JSX distinguishes "prop omitted" from "prop set to default."** `<input value={x}>` is explicit; `<input>` is meaningfully different.

**C# records make no such distinction.** `BasemapGallery(map) with { GalleryViewStyle = Grid }` produces an instance whose `SelectedBasemap` is `null`, indistinguishably from a hypothetical `with { SelectedBasemap = null }`. The framework cannot detect "the author meant to set this to null" the way React can.

The fix is not a different gate — it's a different type. `Optional<T>` gives Reactor the runtime primitive that React's JSX gives React for free. With `Optional<T>` in the type system, force-assert remains the right default for `.Controlled` (it's what authors mean when they declare a value as "controlled"), and the customer's #494 bug becomes structurally impossible because unset has its own bit pattern.

---

## §3 WinUI's `ClearValue` primitive and what it lets us express

Verified against `microsoft-ui-xaml-lift`:

- **`DependencyObject.ClearValue(DependencyProperty)`** — public, on every `IDependencyObject` (`dxaml/xcp/dxaml/lib/DependencyObject.cpp`, `DependencyObject.h`). Removes the *local* value, letting the property system fall back through the precedence chain (animations → local → templated parent → style setters → implicit style → inherited → registered default).
- **`DependencyProperty.UnsetValue`** — the framework's sentinel for "no value at this precedence level."

Reactor today has **no way to express ClearValue from the descriptor layer.** Once a `set:` lambda has written a value, the framework owns that DP slot until the next write. The customer-visible consequence: a theme-aware control with `Background` set in a XAML resource dictionary gets clobbered the moment any Reactor descriptor with a `Background` OneWay entry mounts it, because the descriptor writes `Background = null` for unset.

The 141 `OneWayConditional` entries in the descriptor codebase (§9.1) are descriptor authors hand-rolling around this — "if `Background is not null`, write it." That guard skips the *write* but can't actively *clear* either. A user who actually wants the local value released to the style chain has no path through Reactor today.

This is the second use case that justifies `Optional<T>` and gives it value beyond just the controlled-prop fix.

---

## §4 The `Optional<T>` primitive

### 4.1 Shape

```csharp
namespace Microsoft.UI.Reactor;

public readonly struct Optional<T>
{
    private readonly bool _hasValue;
    private readonly T _value;

    public bool HasValue => _hasValue;
    public T Value => _hasValue ? _value : throw new InvalidOperationException("Optional<T> is Unset");
    public T GetValueOrDefault() => _value;
    public T GetValueOrDefault(T fallback) => _hasValue ? _value : fallback;

    /// <summary>
    /// The unset value. Equivalent to <c>default(Optional&lt;T&gt;)</c>. Use this when
    /// initializing a property that must explicitly start unset, especially to
    /// document intent at the declaration site:
    /// <code>
    /// public Optional&lt;Brush&gt; Background { get; init; } = Optional&lt;Brush&gt;.Unset;
    /// </code>
    /// Or to reset a previously-set Optional back to unset:
    /// <code>
    /// element with { SelectedIndex = Optional&lt;int&gt;.Unset }
    /// </code>
    /// </summary>
    public static Optional<T> Unset => default;

    public static Optional<T> Of(T value) => new(value);

    private Optional(T value) { _hasValue = true; _value = value; }

    public static implicit operator Optional<T>(T value) => new(value);
}
```

Both `Optional<T>.Unset` and `default(Optional<T>)` produce the same bit pattern (all zeros). The named static is provided purely for readability — `Optional<int>.Unset` reads more intentionally than `default` in a `with { }` expression or property initializer.

### 4.2 Naming rationale

Considered and rejected: `Asserted<T>`, `Controlled<T>`, `Authority<T>`. Considered and chosen: `Optional<T>`.

The case for the generic name rests on three distinct first-class uses:
1. **`.Controlled` props** — `Unset` means "control owns it" (§6).
2. **`.OneWay` props with `dp:`** — `Unset` calls `ClearValue` (§6.3).
3. **Latent: the existing 141 `OneWayConditional` workarounds** — three competing sentinels (`null`, `HasValue`, `NaN`) that all express "unset" but without a unifying primitive (§9.1).

Three real use sites makes the type general-purpose, not domain-specific. Matching ecosystem convention (F# `Option<T>`, LanguageExt `Option<T>`, Java/Swift `Optional<T>`) keeps the discoverability cost low.

The pattern `HasValue` / `Value` matches `Nullable<T>` deliberately — it's the C# convention for "may or may not have a value," and authors will reach for `.HasValue` reflexively.

### 4.3 Implicit conversion `T → Optional<T>`

Included for ergonomics. `with { Background = brush }` Just Works without explicit `Optional.Of(brush)`. The cost is one subtle case worth documenting:

> `with { Background = null }` for a reference-type `Optional<Brush>` silently becomes `Optional.Of(null)` — semantically "I'm explicitly asserting null," distinct from `Unset`. This is the right default (it matches how authors intuitively read the line) but must be called out in docs.

To express the unset case explicitly: `with { Background = Optional<Brush>.Unset }`.

### 4.4 What `Optional<T>` deliberately is not

- **Not a discriminated union.** No "Loading | Loaded | Error" semantics; use a separate type for that.
- **Not a replacement for `Nullable<T>` / NRT.** Plain `T?` remains the right choice for any prop that doesn't participate in controlled assertion or `ClearValue`-style fallback.
- **Not a monad.** No `.Map`, no `.Bind`, no LINQ integration. If those grow organically later, fine, but they're not part of the primitive.

---

## §5 The enforce-by-API design

The previously-considered "lazy-assert as a new runtime default" approach (now §14.1) is rejected. Instead the API surface enforces correctness at compile time:

### 5.1 Core rule

**`.Controlled` and `.HandCodedControlled` require an `Optional<T>` getter.** Plain-`T` overloads are removed. The compiler rejects:

```csharp
public record FooElement(int SelectedIndex = -1) : Element;

descriptor.Controlled<int, FooEvt>(
    get: e => e.SelectedIndex,   // ❌ compile error — get must return Optional<int>
    ...);
```

…and accepts:

```csharp
public record FooElement(Optional<int> SelectedIndex = default) : Element;

descriptor.Controlled<int, FooEvt>(
    get: e => e.SelectedIndex,   // ✅ Optional<int>
    ...);
```

The compile error replaces the runtime footgun. Authors cannot ship the #494 bug class because the API does not let them express it.

### 5.2 Mode menu (final)

| Authoring shape | Element-record prop type | Semantic |
|---|---|---|
| `.OneWay(get, set)` | `T` | Write on element change. No callback. Today's OneWay semantics, unchanged. |
| `.OneWayConditional(get, set, shouldWrite)` | `T` | Write on element change *and* `shouldWrite(newEl)`. Today's semantics, unchanged. Use for non-trivial predicates that don't reduce to "set vs unset." |
| `.OneWay(get, set, dp:)` | `Optional<T>` | `HasValue` writes; `Unset` calls `ClearValue(dp)`. Replaces ~110 of the existing `OneWayConditional` entries that hand-roll "set vs unset" predicates. |
| `.Controlled(get, set, callback, readBack, ...)` | `Optional<T>` | `HasValue` force-asserts on every render (drift gate only); `Unset` skips, control owns it. **The only shape for controlled props post-PR.** |
| `.HandCodedControlled(...)` | `Optional<T>` | Same semantic as `.Controlled`; used when the trampoline / payload shape needs hand-wiring. |
| `Prop.InitialOnly(get, set)` | `T` | Write on Mount, never on Update. React's `defaultValue`. Rare — used for genuinely mount-only props. |

Four authoring shapes, each meaning exactly one thing. No third "lazy-assert" semantic to learn or document.

### 5.3 Why this is strictly better than the alternatives

- **vs runtime lazy-assert default for plain-T (`#494`'s original proposal):** No new third semantic. Snap-back works as today (force-assert + `bump`). Author intent visible at the element-record declaration site, not buried in a runtime gate.
- **vs analyzer warning on plain-T `.Controlled`:** Compile error is louder than warning. No suppression escape hatch. No "warning-fatigue" bug class.
- **vs keeping both plain-T and `Optional<T>` overloads:** Two ways to do the same thing is the API-design anti-pattern this change exists to remove. The plain-T overload was, in retrospect, the bug.

---

## §6 `.Controlled` with `Optional<T>` — the controlled-prop model

### 6.1 Update gate

```csharp
public override void Update(TControl ctrl, TElement oldEl, TElement newEl)
{
    var nv_opt = _get(newEl);                      // Optional<TValue>
    if (!nv_opt.HasValue) return;                  // Unset → control owns it
    var nv = nv_opt.Value;
    var current = _readBack(ctrl);
    if (_comparer.Equals(current, nv)) return;     // already there — avoid stranding echo arm
    // arm echo + write
}
```

Same shape on Mount: `Unset` → skip the bare initial write (control retains WinUI default).

The `current == nv` short-circuit is preserved (not removed) — it's still needed to avoid stranding the echo-suppression arm when element value happens to match reality, per the existing comment at `PropEntry.cs:296-300, :482-486`.

### 6.2 Customer's #494 — structurally fixed

Customer's `BasemapGallery` descriptor:

```csharp
// Before (broken):
public record BasemapGalleryElement(...) : Element {
    public BasemapGalleryItem? SelectedBasemap { get; init; }
}
descriptor.Controlled<BasemapGalleryItem?, ...>(
    get: e => e.SelectedBasemap,
    ...);

// After (compile error forces migration):
public record BasemapGalleryElement(...) : Element {
    public Optional<BasemapGalleryItem?> SelectedBasemap { get; init; }
}
descriptor.Controlled<BasemapGalleryItem?, ...>(
    get: e => e.SelectedBasemap,
    ...);
```

Customer's component is unchanged:

```csharp
BasemapGallery(map) with {
    GalleryViewStyle  = gridStyle ? Grid : List,
    OnBasemapSelected = (b) => { },
    // SelectedBasemap intentionally not set → defaults to Optional<...>.Unset
}
```

`SelectedBasemap` defaults to `Optional<BasemapGalleryItem?>.Unset`. The toggle's re-render produces a new element whose `SelectedBasemap` is still `Unset`. The descriptor's Update sees `HasValue == false` and returns. **User's selection survives.** Bug fixed structurally — no runtime gate to misconfigure, no analyzer to suppress.

### 6.3 OneWay + `Optional<T>` + `dp:` (the `ClearValue` channel)

```csharp
.OneWay(
    get: e => e.Background,
    set: (c, v) => c.Background = v,
    dp:  Control.BackgroundProperty)
```

Rules:
- `Optional<T>` get + `dp:` provided → `HasValue` calls `set`; `Unset` calls `ClearValue(dp)`. Local value released; WinUI value-precedence chain (style → template → inherited → default) wins.
- `Optional<T>` get + no `dp:` → `HasValue` calls `set`; `Unset` skips write (degenerate fallback for non-DP-backed setters; equivalent to today's `OneWayConditional`). **Warned by analyzer** — see §6.4.
- Plain `T` get → today's `OneWay` semantics. Unchanged.

### 6.4 Analyzer rule `REACTOR0050` (public)

Roslyn diagnostic in `Reactor.Analyzers` fires on any `.OneWay(get: e => e.X, set: ..., /* no dp: */)` where the `get:` lambda returns `Optional<T>`. Severity: warning.

Message: *"OneWay entry for `X` uses `Optional<T>` without a `dp:` parameter — `Unset` will skip the write rather than call `ClearValue`. Provide `dp:` to enable WinUI value-precedence fallback, use `.OneWayConditional` if skip-write was the intent, or suppress this warning with `[NoClearValue]` to acknowledge the missing `dp:` is deliberate."*

Public severity matters: future third-party descriptor authors (the spec-048 audience) get the same diagnostic without needing `InternalsVisibleTo` to `Reactor.Analyzers`. Diagnostic ID slot `REACTOR0050` is reserved by this spec.

### 6.5 Snap-back recipe

A pattern that worked accidentally today and continues to work deliberately under this design: clamp a control to a fixed value regardless of user interaction.

```csharp
public Element Render()
{
    // UseReducer(false) + bump(b => !b) is the canonical "force re-render with same data" idiom.
    // Already used at RenderContext.cs:490, 510, 528 for UseObservable* hooks.
    var (_, bump) = UseReducer(false);

    return Slider(
        value:     Optional.Of(5.0),           // HasValue → force-assert path
        onChanged: _ => bump(b => !b)          // any user move guarantees a re-render
    );
}
```

Flow:
1. User drags slider to 7 → WinUI fires `ValueChanged(7)` → trampoline invokes `bump(b => !b)`.
2. `UseReducer` sees a different value (toggled `bool`) → schedules re-render.
3. Re-render produces a new `SliderElement` with `Value = Optional.Of(5.0)`.
4. Descriptor sees `HasValue == true`, `readBack(ctrl) == 7`, `nv == 5` → drift detected → arm echo + write 5.
5. Slider snaps to 5.

Works because the `Optional<T>` HasValue arm uses the same force-assert gate today's plain-T `.Controlled` does. The `UseReducer(false) + bump(b => !b)` toggle is the existing idiom for "guarantee re-render" — already in active use in `UseObservable*` hooks. Not new API, not exotic.

---

## §7 `Prop.InitialOnly` — the `defaultValue` analog

Distinct semantic from `Optional<T>`: write on Mount, never on Update.

```csharp
.InitialOnly(
    get: e => e.InitialSearchText,
    set: (c, v) => c.Text = v)
```

Use case: seed a control with an initial value from a route parameter / app settings / etc., then hand authority to the user forever, with no subsequent re-write even if the element value changes.

### 7.1 Where it differs from `Optional<T>.Unset`

- `Optional<T>.Unset` on `.Controlled` — control owns it from the start. Element value (if it later becomes `HasValue`) *does* re-write the control.
- `Prop.InitialOnly` — seed once on Mount, then *never* re-write, even if the element value changes.

### 7.2 Expected usage

Sparingly. `Optional<T>` covers most cases including "type and finish" text-input idioms (the author binds to state or uses `Unset`). `Prop.InitialOnly` exists for genuinely mount-only props — typically things that *cannot* be re-applied to a live control without breaking it (e.g., one-shot initialization knobs on third-party controls).

None of the 25 built-in controlled props in §9 use `Prop.InitialOnly` in the initial migration. It ships for downstream authoring use.

---

## §8 Memory footprint analysis

### 8.1 Per-field cost

CLR struct layout, x64 (8-byte alignment, 1-byte `bool` natural alignment):

```csharp
public readonly struct Optional<T>
{
    private readonly bool _hasValue;   // 1 byte
    private readonly T    _value;
}
```

| `T` | `sizeof(T)` | `sizeof(Optional<T>)` | Delta vs plain `T` | Delta vs `T?` |
|---|---|---|---|---|
| `bool` | 1 | 2 | +1 | 0 (`bool?` is also 2) |
| `int` | 4 | 8 | +4 | 0 (`int?` is also 8) |
| `double` | 8 | 16 | +8 | 0 (`double?` is also 16) |
| `DateTimeOffset` | ~12 (16 aligned) | 24 | +8 | 0 |
| `TimeSpan` | 8 | 16 | +8 | 0 |
| `Color` | 4 | 8 | +4 | 0 |
| **Reference type** (`string`, `Brush`, etc.) | 8 (pointer) | 16 (1 byte + 7 pad + 8 ptr) | **+8** | n/a (no `string??`) |

**Two key takeaways:**

1. **Migrations *from* `Nullable<T>` to `Optional<T>` are net-zero on memory.** Layout is identical. ~70+ of the 141 `OneWayConditional` migrations (§9.1) fall in this bucket.
2. **Migrations *from* plain reference types** cost **+8 bytes per field** because we add a `_hasValue` boolean.

### 8.2 Per-element-instance cost

A representative `TextBoxElement` today (`src/Reactor/Core/Element.cs:2322`) has roughly:

- Record header + `EqualityContract`: ~24 bytes
- `string Value` (migrated → `Optional<string>`): 8 → 16 (+8)
- ~12 other fields (mix of `string?`, `bool?`, `int?`, enums): ~70 bytes
- `Action<>` callback slots (3 nullable): 24 bytes
- `Setters[]` reference: 8 bytes
- Inherited `Element` base (`Key`, `Modifiers`, `Extensions`): ~24 bytes

**Total today: ~180–200 bytes per instance.** Controlled-prop migration adds **+8 bytes (Text)** = ~4% per instance.

For a styling-heavy element with 5 OneWay-migrations (`Background`, `Foreground`, `BorderBrush`, `Margin`, `Padding`): **+40 bytes per instance.** Combined: +48 bytes (~20–25%) — but this comes later, opportunistically per record (§9.3).

### 8.3 Aggregate retained-set cost

Elements are short-lived: every `Render()` allocates a fresh tree, prior tree becomes GC-eligible once reconciliation completes. **Steady-state retention is one tree.**

| App size | Elements visible | Controlled-only cost | Controlled + heavy OneWay |
|---|---|---|---|
| Small (login form) | ~30 | +0.2 KB | +1.4 KB |
| Medium (settings page) | ~150 | +1.2 KB | +7 KB |
| Large (dashboard) | ~500 | +4 KB | +24 KB |
| Pathological (data grid w/ 5000 visible cells) | ~5000 | +40 KB | +240 KB |

**Verdict:** retained-set growth is sub-1% even on pathological UIs. Not a concern.

### 8.4 Allocation throughput cost

Heavy re-render pattern (60 FPS animation driving a 500-element subtree):

- Controlled-only: 500 × 8 bytes × 60 Hz = **240 KB/sec** added GC pressure
- Controlled + OneWay: 500 × 48 bytes × 60 Hz = **1.4 MB/sec** added GC pressure

At realistic re-render rates (~2 Hz for typical interactive apps, gated by `UseState` short-circuit), these drop by 30× to **8 KB/sec and 50 KB/sec** respectively — well below Gen 0 allocation noise floor.

The 60 Hz pathological case warrants a benchmark sweep (§11.3) before merge, but is not expected to be a problem given Yoga layout and WinUI render cost already dominate at that rate.

### 8.5 Per-class metadata cost

`Optional<T>` is a generic struct; the JIT/ILC will instantiate one closed-generic copy per `T` actually used:

- Controlled migrations (§9.2): ~9 closed generics (`Optional<bool>`, `Optional<bool?>`, `Optional<int>`, `Optional<double>`, `Optional<string>`, `Optional<DateTimeOffset>`, `Optional<DateTimeOffset?>`, `Optional<TimeSpan>`, `Optional<Color>`).
- Future OneWay migrations: ~10–15 more (`Optional<Brush>`, `Optional<Thickness>`, etc.).

**~25 closed-generic instantiations in steady state.** Each adds a small EE type structure (~200 bytes), method tables (~1 KB), JIT-compiled code (~200 bytes/method). Total static footprint: **~30–50 KB** added to image size. Negligible vs. the ~30 MB Reactor.dll baseline. `Nullable<T>` has hundreds of instantiations across .NET BCL with no AOT problems; this is well within ordinary generic-struct usage.

No cap on `T`. Validate via existing AOT publish test in CI.

### 8.6 Summary

| Cost dimension | Controlled migration only | + OneWay opportunistic (future) |
|---|---|---|
| Per-instance bytes (avg) | +8 | +48 |
| Steady-state retention (500-element UI) | +4 KB | +24 KB |
| Allocation throughput @ 2 Hz | +8 KB/s | +50 KB/s |
| Static image size | +30 KB | +50 KB |

Memory cost is not a blocker.

---

## §9 Migration scope — counts and built-in record changes

Surveyed directly against `src/Reactor/Core/V1Protocol/Descriptor/`:

| Pattern | Count | Action in this PR |
|---|---|---|
| `.Controlled` entries | 9 | **Migrate to `Optional<T>` — required.** |
| `.HandCodedControlled` entries | 16 | **Migrate to `Optional<T>` — required.** |
| Hand-coded handler controlled writes (`ListView`, `GridView`, `CheckBox`, `Slider`, `TextBox`, `ToggleSwitch`) | ~6 | **Align with new contract — see §10.** |
| `.OneWayConditional` entries | **141** | Leave as-is; opportunistic future migration to `Optional<T>` + `dp:` over time. |
| `.OneWay` entries | 213 | Leave as-is; no migration needed. |

### 9.1 The `OneWayConditional` finding (context for future work)

Sample `shouldWrite:` predicates encountered:

```
shouldWrite: static e => e.Header is not null              // ← reference-type "unset"
shouldWrite: static e => e.MinDate.HasValue                // ← Nullable<T> "unset"
shouldWrite: static e => e.CornerRadius.HasValue
shouldWrite: static e => e.Background is not null
shouldWrite: static e => !double.IsNaN(e.MaxDropDownHeight) // ← NaN sentinel "unset"
shouldWrite: static e => e.PasswordChar is not null
shouldWrite: static e => e.Source is not null
shouldWrite: static e => e.QueryIcon is not null
shouldWrite: static e => e.FirstDayOfWeek.HasValue
shouldWrite: static e => e.Width.HasValue
shouldWrite: static e => e.Height.HasValue
shouldWrite: static e => e.Flyout is not null
shouldWrite: static e => e.Fill is not null
shouldWrite: static e => e.Stroke is not null
shouldWrite: static e => e.NineGrid.HasValue
shouldWrite: static e => e.IconSource is not null
shouldWrite: static e => e.MapServiceToken is not null
shouldWrite: static e => !double.IsNaN(e.OpenPaneLength)
shouldWrite: static e => !double.IsNaN(e.CompactModeThresholdWidth)
shouldWrite: static e => e.Suggestions.Length > 0
shouldWrite: static e => !e.IsDisabledFocusable
shouldWrite: static e => e.ContentElement is null
shouldWrite: static e => e.StrokeDashArray is not null
```

Of the 141 entries, eyeball estimate:

- **~110** are mechanical patterns (`is not null`, `.HasValue`, `IsNaN`) that map cleanly to `Optional<T>`.
- **~30** have non-trivial predicates that stay as `OneWayConditional`.

These three competing sentinels (`null` for ref types, `HasValue` for nullables, `NaN` for doubles) **already encode the "unset" concept** the codebase needs. `Optional<T>` consolidates them rather than introducing a new concept. **Migration of these is out of scope for this PR** — done opportunistically per descriptor as future PRs.

### 9.2 The 25 element-record properties migrated in this PR

Every descriptor-side `.Controlled` / `.HandCodedControlled` entry's element-record property is lifted from `T` to `Optional<T>`:

| Element record | Property | From | To |
|---|---|---|---|
| `CalendarDatePickerElement` | `Date` | `DateTimeOffset?` | `Optional<DateTimeOffset?>` |
| `CheckBoxElement` | `IsChecked` | `bool?` (tri-state) | `Optional<bool?>` |
| `ColorPickerElement` | `Color` | `Color` | `Optional<Color>` |
| `DatePickerElement` | `Date` | `DateTimeOffset` | `Optional<DateTimeOffset>` |
| `RadioButtonElement` | `IsChecked` | `bool` | `Optional<bool>` |
| `RatingControlElement` | `Value` | `double` | `Optional<double>` |
| `SliderElement` | `Value` | `double` | `Optional<double>` |
| `TimePickerElement` | `Time` | `TimeSpan` | `Optional<TimeSpan>` |
| `ToggleSplitButtonElement` | `IsChecked` | `bool` | `Optional<bool>` |
| `ToggleSwitchElement` | `IsOn` | `bool` | `Optional<bool>` |
| `AutoSuggestBoxElement` | `Text` | `string` | `Optional<string>` |
| `ComboBoxElement` | `SelectedIndex` | `int` | `Optional<int>` |
| `ExpanderElement` | `IsExpanded` | `bool` | `Optional<bool>` |
| `FlipViewElement` | `SelectedIndex` | `int` | `Optional<int>` |
| `GridViewElement` | `SelectedIndex` | `int` | `Optional<int>` |
| `ListBoxElement` | `SelectedIndex` | `int` | `Optional<int>` |
| `ListViewElement` | `SelectedIndex` | `int` | `Optional<int>` |
| `NumberBoxElement` | `Value` | `double` | `Optional<double>` |
| `PasswordBoxElement` | `Password` | `string` | `Optional<string>` |
| `PipsPagerElement` | `SelectedPageIndex` | `int` | `Optional<int>` |
| `PivotElement` | `SelectedIndex` | `int` | `Optional<int>` |
| `RadioButtonsElement` | `SelectedIndex` | `int` | `Optional<int>` |
| `RichEditBoxElement` | `Text` | `string` | `Optional<string>` |
| `SelectorBarElement` | `SelectedIndex` | `int` | `Optional<int>` |
| `TabViewElement` | `SelectedIndex` | `int` | `Optional<int>` |
| `TemplatedFlipViewElement` | `SelectedIndex` | `int` | `Optional<int>` |
| `TextBoxElement` | `Value` | `string` | `Optional<string>` |

**26 props total** (added `ListView.SelectedIndex` to the original list of 25 — it's a hand-coded handler bug that gets the same migration treatment per §10).

### 9.3 Text-input shapes — `Optional<T>`, not `Prop.InitialOnly`

Text-input props (`TextBox.Value`, `PasswordBox.Password`, `RichEditBox.Text`, `AutoSuggestBox.Text`) migrate to `Optional<string>` like everything else. Rationale:

- **Uniformity** — one entry shape (`.Controlled` with `Optional<T>`) for all controlled props, no per-prop authoring divergence.
- **"Type and finish" idiom is expressible** — author seeds `Optional<string>.Unset` (or `Optional.Of("initial")` with state binding) and the WinUI control owns user typing. No re-write when the element value is `Unset` on subsequent renders.
- **Programmatic reset stays possible** — `with { Value = Optional.Of("") }` forces clear; `with { Value = Optional.Of(newText) }` programmatic update. `Prop.InitialOnly` would force a key-bump to remount, which is worse ergonomics.

### 9.4 App-author impact

App code is largely insulated by the implicit `T → Optional<T>` conversion:

- `with { SelectedIndex = 3 }` — still compiles via implicit conversion. Becomes `Optional.Of(3)`.
- `with { SelectedIndex = -1 }` — still compiles, becomes `Optional.Of(-1)`. Note: customers using `-1` as a "deselect" sentinel should now use `Optional<int>.Unset` for "control owns it" or `Optional.Of(-1)` for "force-assert no selection" depending on intent. **Documented in the migration guide.**
- `var idx = el.SelectedIndex;` — **breaks.** Return type changed from `int` to `Optional<int>`. Mechanical fix: `el.SelectedIndex.Value` (assert HasValue) or `el.SelectedIndex.GetValueOrDefault(-1)`. Rare in app code; common only in tests reading element state.

Factory shorthand: existing factory functions like `Slider(double value, Action<double> onChanged)` keep their plain-`double` signatures and internally wrap the value in `Optional.Of(value)` before constructing the element. No app-level break for factory callers.

---

## §10 Hand-coded handler alignment

Outside the descriptor system, ~6 handlers do controlled writes by hand:

| File | Line | Issue today | Action |
|---|---|---|---|
| `Handlers/ListViewHandler.cs` | 92, 126 | Unconditional write, **no echo suppression at all** | Migrate `ListViewElement.SelectedIndex` to `Optional<int>`; handler uses Optional-aware gate |
| `Handlers/GridViewHandler.cs` | 100, 143 | Same shape as ListView | Same fix |
| `Handlers/CheckBoxHandler.cs` | 31, 35, 106 | Force-asserts; dual-pathed with descriptor | Drop the duplicate path; let descriptor own it after migration |
| `Handlers/SliderHandler.cs` | 59, 95 | Dual-pathed | Same |
| `Handlers/TextBoxHandler.cs` | 73, 186 | Dual-pathed | Same |
| `Handlers/ToggleSwitchHandler.cs` | 54, 80 | Dual-pathed | Same |

The ListView/GridView fix is *not* a separate carve-out: it's a consequence of migrating `ListViewElement.SelectedIndex` and `GridViewElement.SelectedIndex` to `Optional<int>`. The handler code that reads those props now sees `Optional<int>` and must check `HasValue` before writing — which is exactly the right gate.

For the four dual-pathed handlers (CheckBox/Slider/TextBox/ToggleSwitch), the descriptor path becomes the sole path post-migration. The hand-coded handler files are simplified to delegate to the descriptor or removed entirely if the descriptor covers all behavior.

---

## §11 Testing

### 11.1 New unit tests

`tests/Reactor.Tests/`:

- **`OptionalTests.cs`** — equality, implicit conversion, `Unset` default, `Of`, `Value` throws when `!HasValue`, `GetValueOrDefault` overloads, struct layout sanity (size assertions per §8.1).
- **Per-descriptor tests** for the 26 migrated records: `Mount` with `Optional<T>.Unset` does not write; `Mount` with `Optional.Of(value)` writes; `Update` with `Unset → Unset` no-op; `Update` with `Unset → Of(x)` writes; `Update` with `Of(x) → Of(x)` no-op (current==nv short-circuit); `Update` with `Of(x) → Of(y)` writes; `Update` with `Of(x) → Unset` no-op (control owns it).
- **Echo-strand regression tests** for each controlled descriptor: arm survives only when the write actually fires; cleared on coercion/skip paths.

### 11.2 New selftests

`tests/Reactor.AppTests.Host/SelfTest/Fixtures/`:

- **`ControlledOptionalCustomerRepro.cs`** — exact #494 reproduction. Component with `UseState`-bound sibling toggle, controlled descriptor not bound to state (`Optional<T>.Unset` default), assert user selection survives sibling re-render.
- **Per-control variants** for each of the 26 migrated records covering: (a) unbound `Unset` survives sibling re-render; (b) bound `Optional.Of(state)` updates control when state changes; (c) snap-back recipe (force constant value via bump pattern) works.
- **`OneWayClearValueFixture.cs`** — verifies `.OneWay(get, set, dp:)` with `Optional<T>.Unset` actually calls `ClearValue` and visual fallback to a known XAML-resource brush occurs.

### 11.3 Performance

Microbenchmark suite (BenchmarkDotNet under `tests/`):

- Element-record allocation throughput (baseline vs migrated) for `TextBoxElement` and a representative selection-heavy element.
- Reconciler `Update` hot-path with controlled prop: cycle count under the new Optional-aware gate.

Goal: confirm no >5% regression on element allocation; no measurable change on reconciler `Update` hot-path.

### 11.4 Pre-migration baseline sweep

Before this PR merges:

1. Run `dotnet test tests/Reactor.Tests/Reactor.Tests.csproj -p:Platform=x64` against `main` HEAD; record the pass/fail set.
2. Run full selftest suite (`dotnet run --project tests/Reactor.AppTests.Host -- --self-test`); record results.
3. Land migration; re-run both. Any newly-failing test gets triaged into:
   - **Genuine regression** — fix the implementation.
   - **Test depended on plain-T force-assert footgun** — update the test to express the intent it actually wanted (typically: bind to state via `UseState`, or use `Optional.Of(value)` for intentional force-assert). Document the change in the PR.
4. PR is not mergeable until every newly-failed test is categorized and resolved.

### 11.5 Analyzer tests

`tests/Reactor.Compile.Analyzer.Tests`:

- `REACTOR0050` fires on `.OneWay(get: e => e.X, set: ...)` where `e.X` is `Optional<T>` and no `dp:` argument is supplied.
- Does not fire when `dp:` is supplied.
- Does not fire on `.OneWayConditional`.
- Does not fire on `.OneWay` with plain-`T` `get:`.
- Suppression via `[NoClearValue]` (or whichever opt-out shape we land on) silences the diagnostic.

End-to-end: build `samples/scenarios/` with the analyzer enabled; confirm zero unexpected warnings.

---

## §12 Documentation

### 12.1 Sources to update

- **`docs/_pipeline/templates/extending-reactor-controls.md.dt`** — rewrite the `.Controlled` section around `Optional<T>` requirement. Decision tree: "controlled with user authority" → `Optional<T>`; "one-way with WinUI fallback" → `Optional<T>` + `dp:`; "mount-only" → `Prop.InitialOnly`; "always write, no callback" → plain-`T` `.OneWay`. Cite React analogy. Document implicit-conversion gotcha (§4.3).
- **`docs/_pipeline/templates/control-reconciler-protocol.md.dt`** — note the Optional-aware gate semantics in the Update protocol section.
- **`docs/guide/advanced.md.dt`** — new "Optional&lt;T&gt;" subsection.
- **Migration guide (new doc)** — for app authors of the 26 records in §9.2: rare read-site breaks and how to fix them; sentinel-value migration (e.g., `SelectedIndex = -1` → `Optional<int>.Unset` vs `Optional.Of(-1)`).
- **Snap-back recipe** in advanced docs (§6.5 content).
- **`Reactor.Analyzers` rule docs** — document `REACTOR0050`.

---

## §13 Implementation plan

Single PR. Suggested commit-level order to keep each commit reviewable:

1. **Add `Optional<T>` primitive** + unit tests (§4.1, §11.1).
2. **Add `Prop.InitialOnly` entry shape** + unit tests (§7).
3. **Update `ControlledPropEntry` / `HandCodedControlledPropEntry` to require `Optional<TValue>` get-return**; delete plain-`T` overloads on `ControlDescriptor<,>` (§5.1).
4. **Update OneWay overload set** to accept `Optional<T>` + optional `dp:` (§6.3).
5. **Migrate 26 element records** to `Optional<T>` properties (§9.2). One commit per control family (selection, toggle, numeric, date/time, text) keeps the diff readable.
6. **Update descriptors** for the 26 records to use new gate-aware Update (§6.1). Most diffs are mechanical — descriptor entries already use the right call shape; only the generic argument changes.
7. **Migrate hand-coded handlers** (§10). Simplify ListView/GridView. Drop dual-paths.
8. **Add `REACTOR0050` analyzer** + tests (§6.4, §11.5).
9. **Update factory shortcuts** so existing app-level call sites continue to compile (e.g., `Slider(value, onChanged)` internally maps `value → Optional.Of(value)`).
10. **Selftests** for #494 reproduction, per-control coverage, snap-back recipe, ClearValue (§11.2).
11. **Docs** (§12).
12. **Performance microbenchmarks + baseline sweep** (§11.3, §11.4) — gate merge on no regression.

---

## §14 Alternatives considered

### 14.1 Runtime lazy-assert default for plain-`T` `.Controlled` (issue #494's original proposal)

Change `ControlledPropEntry.Update` gate from `current == nv` to `oldEl_value == newEl_value`. Plain-`T` keeps working but defaults to lazy-assert. Optional<T> is the opt-in for force-assert.

**Rejected** because:

- Introduces a third runtime semantic ("lazy-assert") that doesn't exist anywhere else in Reactor. Plain-T `.Controlled` and `Optional<T>.Of(x)` `.Controlled` would behave differently despite the implicit conversion making them look identical at call sites. Confusing.
- Loses the snap-back capability for the plain-T case (recovered only by migrating to `Optional<T>` + bump).
- Doesn't structurally prevent the bug class — a third-party author who declares a plain-`T` controlled prop is still encoding the wrong intent; the runtime just hides the consequence. The right fix is at the type level.

### 14.2 Analyzer warning on plain-`T` `.Controlled` (keep both overloads)

Keep both overloads. Add `REACTOR0051` warning on plain-`T` `.Controlled`. Author can suppress.

**Rejected** because:

- Suppression escape hatch defeats the purpose. Suppressed warnings rot into bugs.
- No existing third-party descriptor authors to protect with backward compat — there's no audience for "warn but allow."
- Compile error is louder, simpler, and exactly as expressive.

### 14.3 `ControlledAuthority` enum escape hatch

Add `.Controlled(..., authority: ControlledAuthority.ForceAssert)` to opt into force-assert per call site.

**Rejected.** The right axis to vary is the element-record prop type (`Optional<T>` vs plain `T`), not a descriptor-side enum. Authoring intent belongs at the declaration site where readers expect it.

### 14.4 Source-generated descriptors that track which `with { }` keys were touched

Use a source generator to wrap every `with { }` expression in tracking metadata so we know which keys the author set vs let default.

**Rejected.** Massive complexity (interceptors, generator over user code, hot-reload interactions). Doesn't compose with constructor initialization. Doesn't help the `ClearValue` case at all. `Optional<T>` is strictly simpler and more general.

### 14.5 Drop plain-`T` `.Controlled` but keep React-parity analyzer for plain-`T` `.OneWay` controlled-equivalents

Push everyone toward `Optional<T>` for `.Controlled` (this spec's choice) *and* warn on plain-`T` `.OneWay` for any prop that overlaps with controlled semantics elsewhere.

**Rejected as out of scope.** This is a `OneWay`-side question, addressed in the future Tier-3-equivalent work (the 110 mechanical `OneWayConditional` migrations). Not required for the customer-bug fix.

---

## §15 Open questions

**Resolved for merge.** All design-level open questions are resolved in the
**Status** section, and the implementation sweep did not surface any new
unresolved design questions. Follow-up work is tracked as ordinary backlog
items (expanded pool eligibility, interactive `Optional<T>` PropertyGrid
editing, and future `OneWayConditional` migrations), not as spec blockers.

---

## §16 References

- Issue [#494 — .Controlled descriptor entries silently clobber user input on sibling re-renders](https://github.com/microsoft/microsoft-ui-reactor/issues/494)
- `src/Reactor/Core/V1Protocol/Descriptor/PropEntry.cs:292-322` — `ControlledPropEntry.Update`
- `src/Reactor/Core/V1Protocol/Descriptor/PropEntry.cs:478-516` — `HandCodedControlledPropEntry.Update`
- `src/Reactor/Core/V1Protocol/Descriptor/ControlDescriptor.cs` — descriptor entry-adding API
- `src/Reactor/Core/V1Protocol/Descriptor/Descriptors/` — the 25 descriptor files migrated
- `src/Reactor/Core/V1Protocol/Handlers/ListViewHandler.cs:97-128` — ListView hand-coded Update
- `src/Reactor/Core/V1Protocol/Handlers/GridViewHandler.cs:97-145` — GridView hand-coded Update
- `src/Reactor/Core/RenderContext.cs:490, 510, 528` — existing `UseReducer(false) + bump(b => !b)` snap-back idiom
- `docs/_pipeline/templates/extending-reactor-controls.md.dt:150-166` — current `.Controlled` documentation
- `docs/specs/047-extensible-control-model.md` §8 — echo suppression contract this interacts with
- `microsoft-ui-xaml-lift/dxaml/xcp/dxaml/lib/DependencyObject.cpp` — `DependencyObject::ClearValue` implementation
- React reference: https://react.dev/reference/react-dom/components/input#controlling-an-input-with-a-state-variable
