# Element Allocation Reduction for High-Frequency Lists — Design Spec

## Status

- **Drafted** — 2026-05-02.
- **Investigation complete** — see `docs/perf-investigations/reactor-vs-direct-10pct.md`
  for the full analysis, hypothesis log, and same-day measured A/B data that
  motivates this spec. That document is reference material; this spec is the
  forward-looking design.
- **Three components locked in** for production: `MemoCells` hook (EX1),
  direct-record-initializer idiom (EX3), bucketed `ElementModifiers` (EX4).
- **One experiment dropped** as a measured regression: inline-fluent fast
  paths on `Foreground` / `Padding` (EX2). May return after re-measurement
  on dedicated hardware.

---

## Overview

Reactor's reconciler is fast; its allocator throughput is not. At a 10 %
mutation 4,900-cell grid running on a 33 ms tick, unmodified Reactor
allocates ~22 MB per render — driving ~5 gen0 + ~1.7 gen1 GCs **per tick**
and gen2 (full-STW) collections every ~6 s. Reconcile time fits the budget
on paper (~32 ms vs 33 ms tick); GC pauses are what gate actual render rate.

This spec lands three independent allocation cuts that together reduce
unmemoized fluent-chain bytes/tick by ~58 % and lift the strongest
combination (memoized + bucketed + direct-build) to **+51 % renders vs
unmodified Reactor and +32 % vs WinUI Direct**, without breaking any
public API.

The investigation also confirms what a declarative framework's perf story
looks like in practice: the dominant lever is **avoiding work**
(memoization), with framework allocation cuts as a secondary multiplier.
Each component below is independently shippable; they compound when used
together.

---

## Motivation

The `docs/perf-investigations/reactor-vs-direct-10pct.md` document
established same-day, machine-stable medians for every combination. The
relevant rows for this spec:

| Variant                                | Renders | Δ vs base | Alloc/tick | gen2 |
|----------------------------------------|--------:|----------:|-----------:|-----:|
| Reactor (no experiments — baseline)    | 142     | —         | 21,640 KB  | 71   |
| Reactor + EX3 alone                    | 153     | +8 %      | 8,591 KB   | 52   |
| Reactor + EX3 + EX4                    | 159     | +12 %     | 8,860 KB   | 54   |
| Reactor + EX1 alone (memo)             | 212     | +49 %     | 3,448 KB   | 17   |
| **Reactor + EX1 + EX3 + EX4**          | **214** | **+51 %** | **2,210 KB** | **17** |
| Direct (reference)                     | 162     | —         | 35 KB      | 1    |

Three takeaways:

1. **Memoization is the dominant lever** (+49 % alone). It accounts for
   essentially all of Reactor's gain over Direct on this workload.
2. **Direct construction is a clean framework win** (+8 % alone, −60 %
   bytes), and it requires no user reasoning about dependencies.
3. **Bucketed modifiers are a small but consistent multiplier** (+4–6 %
   on top of EX3 or fluent), and the implementation is fully API-compatible.

---

## Goals

1. **Land all three components without breaking public API.** All three are
   API-additive: bucketed modifiers ship as a transparent storage shim, the
   direct-construction idiom is a recommendation, `MemoCells` is a new hook.
2. **Make the closure-dependency footgun loud, not silent.** The investigation
   surfaced a real correctness risk in naive memo usage (cells silently render
   stale when the builder closes over external state). `MemoCells` must take
   an explicit dependency parameter so users can't accidentally use it wrong.
3. **Document the perf-critical-path-only nature of EX3.** Direct record
   initializers bypass the fluent chain's ergonomics. The recommendation is
   explicitly "use this in inner cell loops; keep using fluent everywhere else."
   Future C# language improvements (see `008-csharp-language-improvements.md`)
   may make this dichotomy disappear.
4. **Preserve all 6,724 existing unit tests with zero migration.** Achieved
   in the prototype via shim properties on `ElementModifiers`; this spec
   commits to that approach.
5. **Surface the gen2 trade-off in user docs.** Memo retains snapshots across
   renders. Worst-case-mutation A/B showed gen2 collections rise +67 % under
   memo even when bytes drop. Workloads with many memoized lists need to know.

---

## Non-Goals

- **EX2 (inline-fluent fast paths) is dropped, not adopted.** The same-day
  data showed a −27 % render regression despite the predicted byte savings
  landing. The original "render rate flat" reading did not reproduce.
  EX2 may return as a separate proposal after re-measurement on dedicated
  hardware, but it is not part of this spec.
- **Single-bucket EX4 variant.** The two-bucket design's bucket-boundary
  mismatch (bench's hot pair `Padding` + `Foreground` straddles Layout/Visual)
  was not the catastrophic regression the analysis hedged against — EX4 still
  delivered measurable gains. A single-bucket follow-up is a possible future
  optimization but not in this spec's scope.
- **Off-thread render.** Largest pending systemic win, but a much larger
  change with WinUI thread-affinity audit work. Tracked separately.
- **Builder-pattern element factories.** Long-tail polish, dependent on C#
  language work tracked in `008-csharp-language-improvements.md`.
- **Truly-100 % deterministic mutation bench mode.** The investigation
  surfaced that `StressPerf.Update(100)` only achieves ~63 % effective
  per-cell mutation due to sampling with replacement. Logged as an open
  follow-up for the bench harness.

---

## Component A — Bucketed `ElementModifiers` (EX4)

### What

`ElementModifiers` today is a ~70-field record (~600 B per allocation). At
~10 % mutation, 4,900 cells × ~2 modifier records per cell × 600 B = ~5.6 MB
of `ElementModifiers` allocations per render. Splitting into thematic
sub-records — same pattern as the existing `AccessibilityModifiers`
precedent — shrinks the parent record and allocates only the buckets the
cell touches.

The investigation prototype validates a specific shape:

- **`LayoutModifiers`** (17 fields): `Margin`, `Padding`, `Width`, `Height`,
  `Min/Max{Width,Height}`, `Horizontal/VerticalAlignment`, `IsVisible`,
  `Margin/PaddingInline{Start,End}`, `BorderInlineStart`, `RequestedTheme`.
- **`VisualModifiers`** (10 fields): `Background`, `Foreground`,
  `BorderBrush`, `BorderThickness`, `CornerRadius`, `Opacity`, `Scale`,
  `Rotation`, `Translation`, `CenterPoint`.
- **Slim `ElementModifiers`** (~40 fields): typography (3),
  ToolTip family (4), `IsEnabled`, Automation\*, ElementSoundMode,
  OnMountAction, all ~20 input handlers, gesture configs (Pan/Pinch/Rotate/
  LongPress), drag/drop, accessibility ref, ElementRef, Backdrop.

### How — shim properties (zero migration)

The 27 moved fields stay on `ElementModifiers` as `get`/`init` shim
properties that read from / write into the appropriate bucket:

```csharp
public LayoutModifiers? Layout { get; init; }
public VisualModifiers? Visual { get; init; }

public Thickness? Padding
{
    get => Layout?.Padding;
    init => Layout = Layout is null
        ? new LayoutModifiers { Padding = value }
        : Layout with { Padding = value };
}
// …same shape for the remaining 26 moved fields.
```

This keeps every existing call site working unchanged:

- Reconciler reads (`m.Padding`, `oldM?.Foreground`) — flow through the
  get-shim. No source changes in `Reconciler.cs` / `Reconciler.Update.cs` /
  `Reconciler.Mount.cs` / `Reconciler.DragDrop.cs` / `Reconciler.Gestures.cs`.
- Existing extension methods (`Modify(el, new ElementModifiers { Padding = X })`)
  — flow through the init-shim. No changes needed in
  `src/Reactor/Elements/ElementExtensions.cs`.
- Test fixtures (`new ElementModifiers { Padding = X }`, `m.Padding == ...`)
  — work unchanged. **Prototype passed all 6,724 unit tests with zero
  migration.**

### One method does need updating: `ElementModifiers.Merge`

Naming `Padding`/`Margin`/etc. inside a `with { … }` block re-runs each
shim init, cloning the `LayoutModifiers` sub-record once per moved field.
Replace with bucket-level merge:

```csharp
public ElementModifiers Merge(ElementModifiers other)
{
    var mergedLayout = other.Layout is not null
        ? (Layout is not null ? Layout.Merge(other.Layout) : other.Layout)
        : Layout;
    var mergedVisual = other.Visual is not null
        ? (Visual is not null ? Visual.Merge(other.Visual) : other.Visual)
        : Visual;
    return this with
    {
        Layout = mergedLayout,
        Visual = mergedVisual,
        // …all the long-tail fields that stayed on ElementModifiers,
        //   merged with other.X ?? X as before.
    };
}
```

Each sub-record gets its own `Merge` (~10–17-field `this with { Field = other.Field ?? Field }`).

### Public API — opt-in direct construction

`LayoutModifiers` and `VisualModifiers` are public records. Consumers in
inner loops can bypass the shim and construct buckets directly:

```csharp
new ElementModifiers
{
    Layout = new LayoutModifiers { Padding = new Thickness(2, 1, 2, 1) },
    Visual = new VisualModifiers { Foreground = brush },
}
```

This produces identical allocation cost to the shim path on the bench
(3 records: slim parent + Layout + Visual). The benefit is clarity for
perf-critical call sites — combine with Component B's direct-construction
idiom to get the strongest per-cell allocation profile.

### Equality, hashing, ToString, reflection

- **`ModifiersEqual`** (the reconciler's structural-equality helper) reads
  through the get-shim — works unchanged.
- **Auto-generated record `Equals`** compares actual backing fields (Layout,
  Visual, …). `LayoutModifiers` and `VisualModifiers` are records so their
  `Equals` is structural. End-to-end equality is preserved.
- **`GetHashCode`** is computed from the same backing fields — preserved.
- **`ToString`** loses the moved fields (no backing field → not in auto-
  generated string). Devtools / snapshot tools that pretty-print
  `ElementModifiers` will need to read `Layout` / `Visual` explicitly. Audit
  required: any consumer of `ElementModifiers.ToString()` in
  `Reactor.Hosting/Devtools/`.
- **Reflection** that invokes the property getter returns the value —
  works unchanged.

### Measured impact

- Fluent path: +6 % renders, −11 % bytes/tick.
- EX3 direct-build path: +4 % renders, +3 % bytes (the +3 % is sub-record
  header overhead; the per-cell field count drop offsets it via the
  combined-with-EX3 result).
- Memoized path: ~flat renders, −36 % bytes (compounds with EX3 to give
  the lowest measured allocation profile: 2.21 MB/tick).

---

## Component B — Direct record initializer idiom (EX3)

### What

The fluent extension chain `TextBlock(content).FontSize(8).Foreground(brush).Padding(2,1,2,1).Grid(r,c)`
allocates ~9 heap objects per cell — five `TextBlockElement` clones
(one per `.Method()` call), two `ElementModifiers`, one `Dictionary`, one
`GridAttached`. Four of every five `TextBlockElement` allocations are
immediate garbage.

A single record initializer collapses this to **4 allocations per cell**:
one `TextBlockElement`, one `ElementModifiers`, one `Dictionary`, one
`GridAttached`. No clones at all.

```csharp
children[i] = new TextBlockElement(StockDataSource.FormatCell(in item))
{
    FontSize = 8,
    Modifiers = new ElementModifiers
    {
        Foreground = item.IsUp ? GreenBrush : RedBrush,
        Padding    = new Thickness(2, 1, 2, 1),
    },
    Attached = new Dictionary<Type, object>(1)
    {
        [typeof(GridAttached)] = new GridAttached(r, c, 1, 1),
    },
};
```

This is a usage pattern, not a code change. **No new framework code. No API
changes.** The spec's contribution is to officially document the idiom and
its scope.

### Where to use it

**Recommended only in performance-critical inner loops** — typically the
body of a list/grid `for` loop that produces hundreds-to-thousands of
similar elements per render. The bench's `for (int i = 0; i < 4900; i++)`
cell construction is the canonical case.

**Not recommended for ordinary UI code.** The fluent chain is more
readable, easier to refactor, and supports cross-cutting concerns
(theme refs, modifier helpers, etc.) that direct construction does not.
For a 5-element form with a `Button` and a few `TextBlock`s, the per-cell
allocation cost is invisible.

### Documentation deliverable

Add a "Hot loops" section to `docs/guide/perf.md` (or similar — to be
generated through the existing `_pipeline/templates`) that:

1. Names the workload shape: high-frequency lists/grids with hundreds-plus
   elements per render.
2. Shows the fluent chain → record-initializer translation as a worked
   example, side-by-side.
3. States the trade-off: ~halves cell allocations, loses fluent ergonomics.
4. Mentions the future direction: builder-pattern factories (see
   "Future Work" below) would let the fluent chain match this allocation
   profile, making the dichotomy temporary.

### Why not just ship a faster fluent API?

Investigated as Q3 in the analysis doc. Mutating the underlying record
in place breaks several framework invariants — cross-render diffing,
cell-level memoization (Component C), aliasing-safe captures, off-thread
render — because each invariant relies on element references being
content-stable. The right shape for "fluent and fast" is a `ref struct`
builder that materializes a single immutable record at `.Build()`. That's
"Future Work" below; until it lands, document the manual escape hatch.

### Measured impact

- +8 % renders alone (153 vs 142 baseline).
- −60 % bytes/tick alone (8.6 MB vs 21.6 MB).
- Compounds cleanly with EX1 and EX4: the measured strongest combination
  is `EX1 + EX3 + EX4 = 214 renders / 2.21 MB/tick`.

---

## Component C — `MemoCells` hook with explicit dependencies (EX1)

### What

The reconciler already has a reference-equality short-circuit
(`Element.CanSkipUpdate` via `ReferenceEquals(a, b)`) that makes cell-level
memoization trivial **once** the user hands it identical refs. The
investigation prototype proved this experimentally: a `UseRef`-backed
manual loop reusing `prevChildren[i]` for unchanged cells gave +49 %
renders.

`MemoCells` ships that pattern as a one-line idiom:

```csharp
public static Element[] MemoCells<T>(
    IReadOnlyList<T> items,
    object?[] dependencies,
    Func<T, int, Element> builder,
    IEqualityComparer<T>? comparer = null) where T : notnull;
```

**The `dependencies` parameter is mandatory and explicit.** This is the
spec's most important point and is detailed below.

### The closure-dependency footgun

`MemoCells` caches its output keyed only on the per-item value `T`. If
the `builder` lambda closes over anything besides the item — theme,
selection state, hover state, drag overlays, sort order, a parent
component's `UseState` value — those captures are not part of the cache
key. A change to a captured value will **not** invalidate the cell. The
cell silently renders stale.

This is the same trap as `React.memo` and `useMemo` with a forgotten
dependency. The compiler can't see closure captures, so the framework
can't catch it automatically.

The chosen mitigation is **mandatory explicit deps**:

```csharp
var theme = UseTheme();
var selection = UseSelection();

var children = MemoCells(
    items,
    [theme, selection],     // ← deps; framework invalidates on change
    (item, i) => Cell(item, theme, selection));
```

Behaviorally:

- The hook compares `dependencies` against the prior render's `dependencies`
  using element-wise `Equals`. **If any dep changed, the entire memo is
  invalidated** and every cell rebuilds via `builder`.
- If deps are unchanged, the per-item `Equals` check decides which cells
  reuse vs rebuild.
- Empty deps array (`[]`) means "no closure captures to track" — pure
  function of `T`. Allowed but documented as the sharp-knife case.
- Null `dependencies` is **disallowed** (compile-time `[]` is fine).
  Forcing the user to write `[]` explicitly is the design's whole point —
  silent memo at non-pure call sites is what we are protecting against.

### Variants

Two helpers ship alongside the base hook for common shapes:

- **`MemoCellsByKey<T>(items, keySelector, deps, builder)`** — for items
  with stable identity but mutable interior (`record Person(int Id, string Name)`).
  Hashes by key, value-compares for content. Also lets the reconciler key
  the children for reorder stability.
- **`MemoCellsByIndex<T>(items, changedIndices, deps, builder)`** — for the
  case where the data source already knows which indices changed (the
  `StressPerf.StockDataSource.Update()` return value is exactly this).
  Skips the per-cell equality scan entirely; only the named indices run the
  builder.

### Documentation deliverable — preconditions and gen2 caveat

User-facing docs must lead with two warnings:

1. **`MemoCells` is the right hammer for cells whose content is a pure
   function of their item plus a small set of declared deps.** It is the
   wrong hammer the moment cell content depends on shared state that you
   aren't capturing in `deps`. Examples that *would* work: tickers, log
   tables, file lists, large readonly grids. Examples that *wouldn't*:
   list rows whose chrome depends on focus / drag / selection / hover not
   passed through deps.
2. **Memo trades short-lived gen0 churn for long-lived gen1/gen2
   retention.** The worst-case A/B in the investigation showed gen2
   collections rise +67 % under memo even when bytes/tick drop. Workloads
   with many memoized lists across the app should be aware of this trade.

### Measured impact

- +49 % renders alone (212 vs 142 baseline).
- −84 % bytes/tick alone (3.4 MB vs 21.6 MB).
- Compounds with EX3 + EX4 to give the measured strongest combination
  (214 renders, 2.21 MB/tick).

---

## Implementation order

The three components are independent and can ship in any order, but the
recommended sequence is:

1. **Component A (Bucketed `ElementModifiers`)** first. It's the
   foundation — the `LayoutModifiers` / `VisualModifiers` types are
   prerequisites for documenting Component B's strongest direct-build
   form. Lands as a single PR; expected diff ~270 LOC in `Element.cs`,
   zero LOC elsewhere. All tests pass with no migration.
2. **Component B (Direct record initializer idiom)** next. Documentation-
   only; shipped as a `docs/guide/perf-hot-loops.md` template plus an
   updated `tests/stress_perf/StressPerf.Reactor` example showing the
   pattern in context. No source changes.
3. **Component C (`MemoCells`)** last. Most surface-area-additive work
   — new public hook in `Microsoft.UI.Reactor.Hooks`, three variants,
   docs, tests. Requires the explicit-deps API discussion to land in
   review before implementation.

Each PR can ship independently. Component A is internal and lowest-risk;
Component B is documentation only; Component C is the largest user-facing
addition and benefits from B's documentation context.

---

## Test plan

### Component A

- All 6,724 existing unit tests pass with no migration. (Verified in the
  prototype.)
- New unit test: `LayoutModifiers.Merge` and `VisualModifiers.Merge`
  produce expected results for null / partial / full overlap cases.
- New unit test: `ElementModifiers.Equals` returns true for two records
  built with different code paths (shim init vs direct bucket
  construction) when their effective field values match.
- New unit test: `ElementModifiers.GetHashCode` is consistent across the
  same two paths.
- Devtools snapshot: write a small fixture that round-trips an
  `ElementModifiers` through any reflection/serialization in
  `Reactor.Hosting/Devtools/`. Audit any `ToString`-based pretty-printing
  before merge.
- Bench: re-run `StressPerf.Reactor` on the canonical workload (10 % /
  12 s / 4,900 cells) and confirm the +6 % fluent-path render gain
  reproduces (within noise).

### Component B

- No code changes; deliverable is a `docs/guide/perf-hot-loops.md`
  template that compiles via the existing `mur docs compile` pipeline.
- Add a worked example in `StressPerf.Reactor` (already there from the
  investigation under `STRESS_PERF_DIRECTBUILD=1`) — clean it up into a
  documented inline mode (no env var) so the bench can be read as the
  reference.

### Component C

- Unit tests for `MemoCells` covering: deps-unchanged + items-unchanged
  (full reuse), deps-unchanged + items-partial (per-cell decisions),
  deps-changed (full invalidation), null deps disallowed, empty deps
  allowed.
- Unit tests for `MemoCellsByKey` and `MemoCellsByIndex`.
- Property test: a fuzz over (deps, items) sequences confirms that for
  any input that should produce a different render output, `MemoCells`
  produces it.
- Bench: re-run `StressPerf.Reactor` with `MemoCells` (replacing the
  current `STRESS_PERF_MEMO=1` manual loop) and confirm the +49 %
  render gain reproduces.
- Worst-case bench: re-run at `--percent 100` and confirm the
  measured −5 % render / +67 % gen2 trade matches the investigation
  numbers. If gen2 regression worsens past +100 %, escalate.

### Cross-component bench

After all three land, re-establish the canonical comparison table on the
production code (no env vars) and confirm `MemoCells + EX3 idiom + EX4
storage = 214 renders / 2.21 MB/tick` reproduces.

---

## Risks

- **Component A — Devtools `ToString` audit.** If anything in
  `Reactor.Hosting/Devtools/` pretty-prints an `ElementModifiers` and
  the result is consumed (live tree viewer, snapshot diff tooling), the
  output will silently lose the moved fields after Component A lands.
  Audit before merge; either teach the printer to read `Layout` / `Visual`,
  or add a `DebugDisplay` attribute that walks both buckets.
- **Component A — Public API surface growth.** `LayoutModifiers` and
  `VisualModifiers` become public records. They are additive (no removed
  types), but any future bucket-boundary change is now a breaking change
  for any consumer that has built a `new LayoutModifiers { … }` directly.
  Mitigation: document the buckets as "stable surface for direct
  construction; field set may grow but won't shrink" and pin via API tests.
- **Component C — Closure footgun even with explicit deps.** Users will
  forget to add a dep. The mandatory-deps API makes this visible at the
  call site (`[]` is a deliberate choice, not a default), but the failure
  is still silent. Long-term mitigation options not in this spec:
  (a) a Roslyn analyzer that warns when a closure capture is not in `deps`;
  (b) the `Render Method Compiler Transform` from
  `008-csharp-language-improvements.md` §4, which would let us see
  closure captures statically.
- **Component C — gen2 retention.** The +67 % gen2 finding under
  worst-case mutation is real and could compound across many memoized
  lists. Watch for this in real apps; if it becomes a problem, consider
  shipping a `MemoCells.Compact()` API that lets users drop the snapshot
  on demand (e.g., when the list scrolls off screen).
- **EX2 dropped.** If a future production workload depends on the EX2
  inline fast path, dropping it now is a regression vs the in-tree
  prototype. The data does not currently support keeping it. Accepted risk.

---

## Future Work

The three components above are a complete, independently-valuable
package. Several adjacent ideas extend them:

### Builder-pattern element factories (subsumes Component B's "perf-only" caveat)

Currently the fluent chain produces five `TextBlockElement` clones per
cell because each `.FontSize`, `.Foreground`, etc. is a `with`-clone.
A `ref struct` builder would let the chain mutate a stack frame and
materialize a single immutable record at `.Build()`:

```csharp
TextBlock("Hi", b => b
    .FontSize(8)
    .Foreground(brush)
    .Padding(2, 1, 2, 1)
    .Grid(r, c));
```

This collapses the fluent chain's allocation profile to match Component
B's direct-construction profile — without losing the fluent ergonomics.
**If this lands, Component B's "perf-critical only" recommendation
becomes obsolete: every fluent chain is already optimal.**

The shape depends on choices in `008-csharp-language-improvements.md`
(particularly §6 Trailing Lambdas, §7 Result Builders, and §8 Scoped
Extension Receivers). Track there.

### Single-bucket EX4 follow-up

The two-bucket boundary (`Padding` → Layout, `Foreground` → Visual)
forced the bench's hot pair into different buckets, costing a sub-record
allocation. A single `LightModifiers` bucket containing Layout + Visual +
Typography (~30 fields) would mean one extra alloc per cell instead of
two. Cheap to test once Component A is in place: the shim infrastructure
makes it a one-record split. May not help on this workload (Component A
already showed neutral-to-positive across all paths) but worth the
afternoon if a different workload surfaces a Layout-only or Visual-only
hot pair where the two-bucket cost is more visible.

### Off-thread render

Largest pending systemic win for non-memoizable workloads. Pipelines the
~24 ms tree-build off the UI thread, freeing UI-thread cycles for COM
calls into WinUI. Tracked as Q&A Q2 in the analysis doc; needs a
WinUI-thread-affinity audit on framework-internal `Brush` /
`FontFamily` / `CornerRadius` allocations. **The cells where `MemoCells`
is unsafe** (closure-dependent content) are exactly the cells where
off-thread render still helps.

### Truly-100 % deterministic mutation in `StressPerf`

The investigation surfaced that `StockDataSource.Update(100)` only
achieves ~63 % effective per-cell mutation due to sampling with
replacement. A truly-100 % mode would isolate the equality-check
overhead in `MemoCells` from the partial-reuse benefit, giving cleaner
worst-case numbers. Bench-only change; logged for Component C's bench
test plan.

### Roslyn analyzer for `MemoCells` closure captures

If the gen2 retention or silent staleness becomes a real-world problem,
a custom analyzer that walks the `builder` lambda's captures and warns
when one is not in `deps` would let us catch the footgun at compile
time. Compelling but not blocking for the initial ship.

---

## References

- `docs/perf-investigations/reactor-vs-direct-10pct.md` — the analysis doc.
  Hypothesis log, profile evidence, same-day measurements, EX1–EX4 details,
  Q&A on memoization / off-thread render / fluent-mutation hazards.
- `docs/specs/007-perf-experiments.md` — earlier perf-experiment tracking.
  Baselines and several distinct hypotheses (dirty subtree tracking,
  property diff bitmasks, etc.) that are not in scope here.
- `docs/specs/008-csharp-language-improvements.md` — the C# language
  proposals that, if landed, would make Component B's "use only in
  perf-critical loops" caveat obsolete.
- `docs/reports/stress-perf-stocks-grid.md` — the canonical
  `StressPerf.Reactor` workload definition (4,900 cells, 33 ms tick,
  10 % default mutation).
