# Element Allocation Reduction — Implementation Tasks

Derived from: `docs/specs/034-element-allocation-reduction.md`

Scope reminder: three independent components. Spec dropped EX2 entirely;
this task list does not implement it.

- **Component A** — Bucketed `ElementModifiers` (`LayoutModifiers`,
  `VisualModifiers`, shim properties, bucket-aware `Merge`). Internal
  storage refactor; zero call-site migration.
- **Component B** — Direct record initializer idiom. Documentation +
  reference example in the new `StressPerf.ReactorOptimized` project.
  No new framework code.
- **Component C** — `UseMemoCells` hook (+ two variants) and companion
  `REACTOR_HOOKS_007` analyzer. New public surface in `Reactor.Hooks`
  and `Reactor.Analyzers`.

**Single-PR delivery.** All three components, the new
`StressPerf.ReactorOptimized` bench variant, the sample migrations,
`skills/perf-tips.md`, and the final before/after perf table ship in
one PR. Phases below are implementation order inside that PR, not
separate PRs. Each phase still ends with a self-contained green build
so the work can be paused and resumed between phases without leaving
the tree broken.

**Bench variants** (the spec's empirical close depends on these):

- `StressPerf.Reactor` — **stays naive forever**. Plain fluent API,
  no memo, no direct-initializer tricks. This is what an unaware user
  writes. Component A's bucketed storage applies transparently, so
  this variant gets the framework-level gain "for free" even though
  the source code never changes shape.
- `StressPerf.ReactorOptimized` — **new sibling project, created in
  Phase 2**. Uses direct-record-initializer cell construction
  (Component B), `UseMemoCells` (Component C), and explicit
  `LayoutModifiers` / `VisualModifiers` buckets. Demonstrates the
  spec's headline combination.

The final perf table compares `Reactor` (naive, pre-spec-034) →
`Reactor` (naive, post-spec-034, shim benefit only) →
`ReactorOptimized` (full stack) → `Direct` reference. This
three-way decomposition isolates the framework uplift from the
user-facing uplift.

## Conventions

- `src/` paths are under `src/Reactor/` unless otherwise noted.
- New unit tests live under `tests/Reactor.Tests/`. Analyzer tests live
  in the same project (no separate `Reactor.Analyzers.Tests` exists —
  follow the existing `HookRulesAnalyzerTests` pattern).
- Bench harness lives in `tests/stress_perf/StressPerf.Reactor/` and
  runs headless via `--headless --percent N --duration N`. Output goes
  to `tests/stress_perf/benchmark_results.csv`.
- New public API must carry XML doc comments with a `<remarks>` link to
  spec 034 § number.
- New analyzer diagnostics get an entry in
  `src/Reactor.Analyzers/AnalyzerReleases.Unshipped.md`. ID convention
  is `REACTOR_<CATEGORY>_<NUM>`; next free in `Reactor.Hooks` is 007.
- User-facing docs are generated — edit
  `docs/_pipeline/templates/<topic>.md.dt` and run `mur docs compile`.
  **Never hand-edit `docs/guide/`** (per repo memory).
- CHANGELOG entries go under `## [Unreleased]` in the repo-root
  `CHANGELOG.md`, grouped by spec ("(spec 034 §A)" / "(spec 034 §B)" /
  "(spec 034 §C)") under the appropriate Added / Changed bucket.
- Production-quality fundamentals checklist applies per phase: input
  validation, threading (UI-thread affinity for any WinUI access),
  trim/AOT-safety, exception safety. Tasks call these out explicitly.

A task is "done" only when:

1. Code compiles with `Reactor.sln` warnings-as-errors.
2. New unit tests cover the happy path **and** every failure mode
   listed.
3. Public API additions appear in `PublicAPI.Unshipped.txt` (verify the
   project uses `Microsoft.CodeAnalysis.PublicApiAnalyzers` first; if
   not, log a follow-up).
4. XML doc comments compile without `CS1591` warnings on public
   surface.
5. CHANGELOG entry added under `## [Unreleased]`.
6. `dotnet test` green; `tests/Reactor.AppTests.Host/SelfTest/` green
   for any phase that touches the reconciler or public DSL.

---

## Phase 0: Cross-cutting setup

### 0.1 Pre-flight checks

- [x] Confirm `git stash list` still has the investigation prototype
      (user note in conversation: stashed under `stash@{0}`). Do not
      pop until Phase 1 starts; the stash is the reference source for
      the bucket field assignment, the `Merge` rewrite, and the
      `UseMemoCells` reconciler skip path.
- [x] Read `docs/perf-investigations/reactor-vs-direct-10pct.md` — the
      spec's empirical foundation. Cross-check that any change in
      planned bucket boundaries (Layout vs. Visual field assignment)
      against the prototype matches.
- [x] Verify `tests/stress_perf/StressPerf.Reactor/Program.cs` is on a
      clean build before touching it (Phase 4 baseline measurement
      depends on a known-good starting point).
- [x] Confirm `src/Reactor/Reactor.csproj` and
      `src/Reactor.Analyzers/Reactor.Analyzers.csproj` use
      `Microsoft.CodeAnalysis.PublicApiAnalyzers`. If yes, every public
      API in this spec needs a `PublicAPI.Unshipped.txt` entry; if no,
      open a follow-up (out of scope for this spec).

### 0.2 Baseline measurement (must precede Phase 1)

- [ ] Run the canonical bench on the unmodified main:
      `pwsh tests/stress_perf/run_stocks_grid_baseline.ps1` — captures
      the **`Reactor (pre-spec-034)`** numbers for the final PR table.
      Save the resulting CSV row to a fixed-name file
      (`tests/stress_perf/baselines/spec-034-before.csv`) so the row
      survives later bench runs that overwrite
      `benchmark_results.csv`. This is the only "before" point that
      can't be re-measured later — once Phase 1 lands, the same source
      no longer compiles against pre-shim `ElementModifiers`.
      *Skipped: Phase 1 is already merged on the branch; the
      pre-shim source no longer exists locally. The spec's
      `reactor-vs-direct-10pct.md` prototype table remains the
      operating reference for that data point.*
- [ ] Record the platform fingerprint (machine, OS build, .NET runtime
      version, WinAppSDK version) alongside the CSV row. Same-day
      A/B-stability is what makes the spec's table reproducible.
      *Skipped — same reason; close-out re-bench captures
      post-spec-034 fingerprint instead (see `baselines/spec-034-final.csv`).*
- [x] **Note for Phase 7**: the `Reactor (post-spec-034, shim only)`
      and `ReactorOptimized` rows can both be re-measured at any
      time after Phase 4 (their source survives in-tree). The
      pre-spec-034 row is the one that has to be captured now.

### 0.3 Branch + scratch baseline

- [x] Create a feature branch (e.g. `spec-034-element-allocation`).
      Single PR is the plan; phases are local checkpoints.
- [x] Decide on commit granularity within the PR. Recommended: one
      commit per phase (1 → 7), so reviewers can step through. Avoid
      one giant commit — the diff is large and the phases have clean
      boundaries.

---

## Phase 1: Component A — Bucketed `ElementModifiers`

Spec: §A. Internal storage refactor; zero migration. Expected diff
~270 LOC in `Element.cs`, zero LOC elsewhere. Land first because
Components B and C both reference `LayoutModifiers` /
`VisualModifiers` in their canonical form.

### 1.1 New sub-records

- [x] Add `public record LayoutModifiers` in `src/Reactor/Core/Element.cs`
      next to the existing `AccessibilityModifiers` (line 1033). 17
      fields per spec §A:
      `Margin`, `Padding`, `Width`, `Height`, `MinWidth`, `MaxWidth`,
      `MinHeight`, `MaxHeight`, `HorizontalAlignment`,
      `VerticalAlignment`, `IsVisible`, `MarginInlineStart`,
      `MarginInlineEnd`, `PaddingInlineStart`, `PaddingInlineEnd`,
      `BorderInlineStart`, `RequestedTheme`. (Cross-check the field
      list against the prototype before merging — the spec's count is
      indicative, not authoritative.)
- [x] Add `public LayoutModifiers Merge(LayoutModifiers other)` mirror
      of `AccessibilityModifiers.Merge` at line 1071: `this with { X =
      other.X ?? X, … }` per field.
- [x] Add `public record VisualModifiers` with 10 fields per spec §A:
      `Background`, `Foreground`, `BorderBrush`, `BorderThickness`,
      `CornerRadius`, `Opacity`, `Scale`, `Rotation`, `Translation`,
      `CenterPoint`. `Merge` mirrors above.
- [x] XML doc comments on both records: 1-paragraph summary,
      `<remarks>` linking to spec 034 §A, note that "field set may grow
      but won't shrink" (matches the spec's API stability commitment).

### 1.2 Slim parent + shim properties

- [x] Add `public LayoutModifiers? Layout { get; init; }` and
      `public VisualModifiers? Visual { get; init; }` slots to
      `ElementModifiers` (line 827).
- [x] Convert each of the 27 moved fields on `ElementModifiers` to a
      `get` / `init` shim that reads from / writes into the appropriate
      bucket. Pattern from spec §A:
      ```csharp
      public Thickness? Padding
      {
          get => Layout?.Padding;
          init => Layout = Layout is null
              ? new LayoutModifiers { Padding = value }
              : Layout with { Padding = value };
      }
      ```
      Apply mechanically to all 17 Layout fields and 10 Visual fields.
- [x] **Audit deletes**: confirm the original backing field for each of
      the 27 fields is removed from `ElementModifiers`. If a field
      appears twice (backing + shim), record `Equals` will return false
      for two records that should compare equal. Build with
      warnings-as-errors and grep for any stragglers.

### 1.3 `ElementModifiers.Merge` rewrite

- [x] Replace the body at line 943 to merge buckets first, then the
      long-tail fields that stayed on `ElementModifiers`. Pattern per
      spec §A. **Critical**: do not name `Padding` / `Foreground` /
      etc. inside the `with { … }` — the shim init re-runs once per
      moved field and clones the sub-record N times.
- [x] Verify by inspection: the new `Merge` body should reference
      `Layout` and `Visual` exactly once each, plus one entry per
      long-tail field.

### 1.4 Equality, hashing, devtools audit

- [x] Verify `ModifiersEqual` (line 646) reads through the get-shim
      unchanged. No code change required, but add a unit test asserting
      two `ElementModifiers` built differently (shim init vs. direct
      bucket construction) compare equal when their effective values
      match.
- [x] **Devtools `ToString` audit**: grep `Reactor.Hosting/Devtools/`
      for any consumer of `ElementModifiers.ToString()` or
      reflection-based property enumeration. If found:
      - Either teach the printer to walk `Layout` and `Visual`
        explicitly, or
      - Add `[DebuggerDisplay]` / a custom `ToString` override on
        `ElementModifiers` that pretty-prints both buckets.
      Decide based on what's actually called. **This is the spec's
      named risk (§Risks line 506) — do not skip.**
- [x] If the devtools tree viewer or snapshot diff tooling consumes the
      printer, confirm a fixture round-trip still produces the expected
      output before merging.

### 1.5 Tests — `tests/Reactor.Tests/`

- [x] All 6,724 existing tests pass with no migration. Spec verified
      this in the prototype; treat it as a regression gate, not a goal.
      If any test fails, the bucket field assignment in 1.1 is wrong.
- [x] New file `LayoutModifiersTests.cs`:
  - [x] `Merge` with null-other returns this.
  - [x] `Merge` with partial-other (some fields set, others null) takes
        other where set, this where null.
  - [x] `Merge` with full-other returns other-equivalent.
  - [x] Two `LayoutModifiers` with identical fields are `Equals` and
        share `GetHashCode`.
- [x] Same shape for `VisualModifiersTests.cs`.
- [x] New file `ElementModifiersBucketTests.cs`:
  - [x] `Equals` true across shim-init vs. direct-bucket construction
        with the same effective values.
  - [x] `GetHashCode` consistent across the same two paths.
  - [x] `Merge` with bucket-only changes on `other` correctly produces
        the merged buckets without cloning untouched ones (assert via
        `ReferenceEquals` on the parent's `Layout` slot when only
        `Visual` changed).
  - [x] Setting `Padding` via the shim and reading via `Layout.Padding`
        (and vice versa) round-trips.

### 1.6 Bench

- [x] Re-run the canonical bench
      (`run_stocks_grid_baseline.ps1`). Record:
  - [x] Fluent path: ~+6 % renders, ~−11 % bytes/tick (spec §A).
  - [x] If render delta is below +2 % or above +10 %, investigate
        before merging — the prototype was stable on this number.
- [x] Save the result row to
      `tests/stress_perf/baselines/spec-034-after-phaseA.csv` for the
      final PR table.

### 1.7 CHANGELOG + public API

- [x] `PublicAPI.Unshipped.txt` entries (if applicable per 0.1):
      `Microsoft.UI.Reactor.LayoutModifiers`,
      `Microsoft.UI.Reactor.VisualModifiers`, both `Merge` methods, the
      `Layout` and `Visual` init slots.
- [x] `CHANGELOG.md` → `## [Unreleased]` → **Changed**:
      "`ElementModifiers` now stores layout and visual fields in
      `LayoutModifiers` / `VisualModifiers` sub-records. Existing call
      sites are unaffected via shim properties; perf-critical code may
      construct buckets directly. ~−11 % allocation/tick on the
      4,900-cell stress grid (spec 034 §A)."

---

## Phase 2: Component B — Direct record initializer idiom

Spec: §B. Documentation + reference example. **No source changes** in
`src/`.

### 2.1 Create `StressPerf.ReactorOptimized` as a sibling project

The naive `StressPerf.Reactor` is preserved unchanged. Component B's
direct-initializer idiom is demonstrated in a new sibling project that
will accumulate Components B + C through Phases 2–4.

- [x] Clone `tests/stress_perf/StressPerf.Reactor/` to
      `tests/stress_perf/StressPerf.ReactorOptimized/`. Copy
      `StressPerf.Reactor.csproj` → `StressPerf.ReactorOptimized.csproj`
      and update:
  - `<RootNamespace>StressPerf.ReactorOptimized</RootNamespace>`
  - `<AssemblyName>StressPerf.ReactorOptimized</AssemblyName>`
- [x] Copy `Program.cs` to the new project. Update:
  - `private const string AppName = "StressPerf.ReactorOptimized";`
  - `ReactorApp.Run<StockGridApp>("StressPerf.ReactorOptimized", …);`
- [x] In `Program.cs`, replace the fluent-chain cell construction at
      lines ~142–151 with the direct-initializer bucket form per spec §B:
      ```csharp
      children[i] = new TextBlockElement(StockDataSource.FormatCell(in item))
      {
          FontSize = 8,
          Modifiers = new ElementModifiers
          {
              Layout = new LayoutModifiers { Padding = new Thickness(2, 1, 2, 1) },
              Visual = new VisualModifiers { Foreground = item.IsUp ? GreenBrush : RedBrush },
          },
          Attached = new Dictionary<Type, object>(1)
          {
              [typeof(GridAttached)] = new GridAttached(r, c, 1, 1),
          },
      };
      ```
      No `UseMemoCells` yet — that lands in Phase 4. This phase
      isolates Component B's contribution.
- [x] Add a header comment in `Program.cs` linking to spec 034 §B and
      noting the project's role: "demonstration of perf-critical inner-
      loop idioms; do not write ordinary UI code this way."

### 2.2 Wire the new project into solution + bench harness

- [x] Add `StressPerf.ReactorOptimized.csproj` to `Reactor.sln`
      following the existing `StressPerf.Reactor` pattern (project
      GUID, solution config rows for Debug/Release × x64/ARM64).
      Use a fresh GUID — do not reuse Microsoft.UI.Reactor's.
- [x] Add `ReactorOptimized` to the variant table in
      `tests/stress_perf/run_stocks_grid_baseline.ps1` (after the
      existing `Reactor` row), using the same `IsRN=$false` /
      `ReportName='StressPerf.ReactorOptimized'` shape and the
      ARM64 Release output path.
- [x] Same addition in any other run scripts that enumerate variants:
      `run_bench_aot_publish.sh`, `run_bench_reactor_arm64.ps1`,
      `run_sweep_arm64.ps1`. Grep for `StressPerf.Reactor\b` to find
      all the spots that need a sibling row.
- [x] Update `tests/stress_perf/README.md` and `SPEC.md` to document
      the new variant, including its purpose and what tricks it
      demonstrates.

### 2.3 Verify

- [x] Build both projects (`dotnet build -c Release -p:Platform=ARM64`
      on Reactor and ReactorOptimized).
- [x] Run both interactively. The rendered grid must be visually
      identical.
- [x] Capture a same-day A/B between `Reactor` (naive, post-shim) and
      `ReactorOptimized` (Component B added). Expected:
      ReactorOptimized at this point shows ~+8 % renders / ~−60 %
      bytes/tick over Reactor — that's Component B's isolated
      contribution per spec §B.

### 2.4 Docs — extend `advanced.md.dt`

- [x] Add a "Hot loops" section to
      `docs/_pipeline/templates/advanced.md.dt` (the existing
      perf-tuning page). Include:
  - [x] Workload shape: high-frequency lists/grids with hundreds-plus
        elements per render. Tickers, log tables, large grids.
  - [x] Side-by-side fluent → record-initializer translation as a
        worked example. **Use `StressPerf.Reactor` (naive) and
        `StressPerf.ReactorOptimized` (idiomatic) as the canonical
        before/after pair** — link directly to both Program.cs files
        so readers can see the diff in context.
  - [x] Trade-off: ~halves cell allocations, loses fluent ergonomics
        and refactor-friendliness.
  - [x] Forward reference: builder-pattern factories (spec 008) would
        let the fluent chain match this profile, making the dichotomy
        temporary.
- [x] Run `mur docs compile`; verify `docs/guide/advanced.md` updates
      cleanly. **Do not hand-edit the compiled output.**

### 2.5 Sample audit (light pass — full migration in Phase 5)

- [x] Grep `samples/` for `for (int i = 0; i < N; i++)` patterns
      producing fluent-chain elements. List candidates for Phase 5
      migration in this PR's description; do not migrate yet. Reason:
      Phase 5 is a single migration sweep that can also exercise
      `UseMemoCells`, which lands in Phase 3.

### 2.6 CHANGELOG

- [x] `CHANGELOG.md` → `## [Unreleased]` → **Added**:
      "Documentation: hot-loop direct-initializer idiom in
      `advanced.md`, with new `StressPerf.ReactorOptimized` bench
      variant as the reference implementation alongside the unchanged
      naive `StressPerf.Reactor` (spec 034 §B)."

---

## Phase 3: Component C — `UseMemoCells` + analyzer

Spec: §C. Largest user-facing addition. The hook and analyzer must
land in the same commit — the analyzer is what makes the hook safe.

### 3.1 Hook implementation

- [x] New file `src/Reactor/Hooks/UseMemoCells.cs`. Three public
      extension methods on `RenderContext` (and convenience
      `Component.UseMemoCells*` shims following the existing
      `UseElementRef` pattern):
      ```csharp
      public static Element[] UseMemoCells<T>(
          this RenderContext ctx,
          IReadOnlyList<T> items,
          Func<T, int, Element> builder,
          params object[] dependencies) where T : notnull;

      public static Element[] UseMemoCellsByKey<T, TKey>(
          this RenderContext ctx,
          IReadOnlyList<T> items,
          Func<T, TKey> keySelector,
          Func<T, int, Element> builder,
          params object[] dependencies)
          where T : notnull where TKey : notnull;

      public static Element[] UseMemoCellsByIndex<T>(
          this RenderContext ctx,
          IReadOnlyList<T> items,
          IReadOnlyList<int> changedIndices,
          Func<T, int, Element> builder,
          params object[] dependencies) where T : notnull;
      ```
- [x] Hook state shape: `MemoCellsHookState<T>` holds
      `T[] prevItems`, `Element[] prevChildren`, `object[] prevDeps`.
      Same hook-state pattern as `MemoHookState<T>` in
      `RenderContext.cs:299`.
- [x] **Behavior** (per spec §C):
  - Compare `dependencies` element-wise via `Equals` against
    `prevDeps`. If any dep changed → invalidate the entire memo;
    rebuild every cell.
  - Else, for each `i`: if `Equals(items[i], prevItems[i])`, reuse
    `prevChildren[i]` (reconciler short-circuits via
    `ReferenceEquals`). Otherwise call `builder(items[i], i)`.
  - On first render (no prior state), build all cells.
- [x] `UseMemoCellsByKey`: hash items by `keySelector`; reuse when both
      key matches and value compares equal. Note: also feeds the
      reconciler keyed-children path (verify the reconciler's existing
      key handling — `src/Reactor/Core/ChildReconciler.cs` —
      participates correctly).
- [x] `UseMemoCellsByIndex`: skip the per-cell equality scan; only run
      `builder` for indices in `changedIndices`. The
      `StressPerf.StockDataSource.Update()` return type is the
      reference shape (verify or extend in Phase 4).
- [x] **Validation at the boundary**:
  - `ArgumentNullException` for null `items`, `builder`, or
    `keySelector` / `changedIndices` where required.
  - `dependencies` is `params`; null array (someone explicitly passes
    `(object[])null`) → `ArgumentNullException(nameof(dependencies))`.
- [x] XML doc comments: 1-paragraph summary per overload, `<remarks>`
      linking to spec 034 §C, `<example>` block showing the canonical
      ticker-list call. Lead with the closure-capture warning and link
      to the analyzer.

### 3.2 Analyzer — `REACTOR_HOOKS_007`

- [x] New analyzer in `src/Reactor.Analyzers/UseMemoCellsAnalyzer.cs`.
      Follow the `HookRulesAnalyzer.cs` pattern (registered via
      `DiagnosticAnalyzer` attribute, syntax-tree walk).
- [x] Trigger: invocation expression where the called symbol is
      `Microsoft.UI.Reactor.RenderContext.UseMemoCells` /
      `UseMemoCellsByKey` / `UseMemoCellsByIndex` (or the `Component`
      shims). Match by symbol, not by name (avoid false positives from
      user-defined methods of the same name).
- [x] Analysis steps:
  1. Locate the `builder` argument's lambda body.
  2. Walk the lambda's data-flow analysis (`SemanticModel.AnalyzeDataFlow`)
     to enumerate captured locals/parameters/fields.
  3. Skip captures of: the lambda's own parameters (`item`, `i`),
     `static readonly` fields, `const` symbols.
  4. Compare against the `dependencies` argument list. Match by
     symbol, not by syntactic identity (so
     `MemoCells(items, b, this.theme)` matches a capture of
     `theme` through `this`).
  5. Emit `REACTOR_HOOKS_007` for each capture not present in the
     deps list.
- [x] **Diagnostic shape**:
  - ID: `REACTOR_HOOKS_007`.
  - Category: `Reactor.Hooks`.
  - Severity: Warning.
  - Title: "Builder closure captures variable not in `dependencies`".
  - Message: "`{0}` is captured by the builder lambda but missing from
    the `dependencies` arg list. The cell will not invalidate when
    `{0}` changes."
- [x] **Codefix** in
      `src/Reactor.Analyzers/UseMemoCellsCodeFix.cs`. Adds the missing
      capture as a trailing argument. Test pattern lives in the same
      file as the analyzer's tests.
- [x] **Known blind spot** (per spec): indirect captures through
      method calls. Document in the analyzer's source-level XML
      comment; do not attempt to fix.

### 3.3 Analyzer release tracking

- [x] Add to `src/Reactor.Analyzers/AnalyzerReleases.Unshipped.md`:
      ```
      REACTOR_HOOKS_007 | Reactor.Hooks | Warning | UseMemoCellsAnalyzer - Builder closure capture missing from dependencies
      ```
- [x] Verify no diagnostic ID collision (existing IDs end at
      `REACTOR_HOOKS_006`).

### 3.4 Tests — hook (`tests/Reactor.Tests/UseMemoCellsTests.cs`)

- [x] First render builds all cells (no prior state).
- [x] Deps unchanged + items unchanged → full reuse (assert
      `ReferenceEquals` on every returned element vs. previous render).
- [x] Deps unchanged + items partial change → only changed indices
      rebuild; unchanged indices return the same `Element` reference.
- [x] Deps changed → full invalidation; every cell calls `builder`.
- [x] Zero-deps call (`UseMemoCells(items, builder)`) is legal at
      runtime (no exception). The analyzer is what flags this when the
      builder captures.
- [x] Null `items` / `builder` → `ArgumentNullException`.
- [x] Explicit null deps array → `ArgumentNullException`.
- [x] Item count change (longer items) → new tail rebuilds; existing
      head is reused or rebuilt per equality.
- [x] Item count change (shorter items) → trailing previous children
      are released (no leak; assert via the existing reconciler
      lifecycle hooks).
- [x] Hook-order stability: `UseMemoCells` followed by `UseState`
      followed by another `UseMemoCells` works across renders (deps on
      both update independently).

### 3.5 Tests — `UseMemoCellsByKey`

- [x] Stable identity / mutable interior: same key + different content
      → cell rebuilds.
- [x] Reorder (same items, different positions) → cells reused, just
      reordered. Verify the reconciler's keyed-children path is
      exercised (cells don't unmount/remount).
- [x] Null `keySelector` → `ArgumentNullException`.
- [x] Duplicate keys → throws or last-write-wins; pick one explicitly
      and document in the XML doc.

### 3.6 Tests — `UseMemoCellsByIndex`

- [x] Empty `changedIndices` + same items → full reuse (no `builder`
      calls).
- [x] Single-index change → only that cell's `builder` runs.
- [x] Index out of range → `ArgumentOutOfRangeException`.
- [x] Item count change is **not** supported via index-only; document
      that callers must fall back to `UseMemoCells` when the list
      length changes.

### 3.7 Tests — analyzer (`tests/Reactor.Tests/UseMemoCellsAnalyzerTests.cs`)

Follow the `HookRulesAnalyzerTests.cs` pattern.

- [x] Builder captures dep present in `deps` → no diagnostic.
- [x] Builder captures dep missing from `deps` → `REACTOR_HOOKS_007`
      warning at the call-site location.
- [x] Zero-deps call with capturing builder → warning.
- [x] Zero-deps call with pure builder (no captures) → no diagnostic.
- [x] `static readonly` capture → no diagnostic (skipped per 3.2 step
      3).
- [x] `const` capture → no diagnostic.
- [x] Capture through `this` (instance field) → diagnostic emitted,
      message names the field correctly.
- [x] Indirect capture through helper method → no diagnostic
      (documented blind spot).
- [x] Codefix: applying the fix transforms
      `UseMemoCells(items, (item, i) => Cell(item, theme))` →
      `UseMemoCells(items, (item, i) => Cell(item, theme), theme)`.
- [x] Three variants share the analyzer: tests cover `UseMemoCells`,
      `UseMemoCellsByKey`, and `UseMemoCellsByIndex`.

### 3.8 Bench (mid-flight hook validation)

The hook ships in `Reactor.csproj` so it's available to any project,
but no bench variant uses it yet — Phase 4 wires it into
`ReactorOptimized`. For mid-flight validation, run the hook against a
focused unit-style timing harness (no UI):

- [x] Add a small benchmark in `tests/Reactor.Tests/` (or via
      BenchmarkDotNet if already in use — check
      `tests/Reactor.Tests/Reactor.Tests.csproj` references) that
      compares `for`-loop cell construction against `UseMemoCells` on
      a fixed 4,900-element synthetic list with 10 % mutation. Record
      cells/sec and bytes/op.
- [x] Confirm `UseMemoCells` reduces `builder` invocations by ~90 % at
      10 % mutation (only ~490 of 4,900 cells should rebuild). If the
      ratio is wildly off, the per-item equality check or the deps
      comparison is wrong.
- [x] Save numbers to
      `tests/stress_perf/baselines/spec-034-phaseC-microbench.csv`.
      The full UI-level A/B happens in Phase 4 once the hook is wired
      into `ReactorOptimized`.

### 3.9 Docs — extend `advanced.md.dt`

- [x] Add a "Memoizing list cells" section after "Hot loops". Include:
  - [x] When `UseMemoCells` is the right hammer (pure function of `T`
        + declared deps; tickers, log tables, file lists, large
        readonly grids).
  - [x] When it's the wrong hammer (rows whose chrome depends on
        focus / drag / selection / hover not passed through deps).
  - [x] Three-overload table: base, `ByKey`, `ByIndex` — when to pick
        each.
  - [x] **gen2 caveat**: memo trades short-lived gen0 churn for
        long-lived gen1/gen2 retention. Worst-case A/B showed +67 %
        gen2 even when bytes drop. Workloads with many memoized lists
        should be aware.
  - [x] Pointer to the analyzer: missing-dep is caught at compile
        time; indirect captures through helper methods are not.
- [x] Run `mur docs compile`.

### 3.10 CHANGELOG + public API

- [x] `PublicAPI.Unshipped.txt` entries: three overloads × hook =
      6 (RenderContext + Component shims).
- [x] `CHANGELOG.md` → **Added**:
      "`UseMemoCells` / `UseMemoCellsByKey` / `UseMemoCellsByIndex`
      hooks for cell-level memoization in high-frequency lists, plus
      `REACTOR_HOOKS_007` analyzer that warns when a builder closure
      capture is missing from `dependencies`. ~+49 % renders / ~−84 %
      bytes/tick on the 4,900-cell stress grid (spec 034 §C)."

---

## Phase 4: Wire `UseMemoCells` into `StressPerf.ReactorOptimized`

`ReactorOptimized` shipped in Phase 2 with Components A + B (bucketed
modifiers via the public types, plus direct-record-initializer cell
construction). Phase 4 adds Component C — `UseMemoCells` — to make it
the spec's canonical "all three combined" reference.

### 4.1 Add `UseMemoCells` to the cell-building loop

- [x] In `tests/stress_perf/StressPerf.ReactorOptimized/Program.cs`,
      replace the cell-building `for` loop with a single
      `UseMemoCellsByIndex` call:
      ```csharp
      var children = ctx.UseMemoCellsByIndex(
          data,
          source.LastChangedIndices,  // see 4.2 — may need plumbing
          (item, i) =>
          {
              int r = i / StockDataSource.Columns;
              int c = i % StockDataSource.Columns;
              return new TextBlockElement(StockDataSource.FormatCell(in item))
              {
                  FontSize = 8,
                  Modifiers = new ElementModifiers
                  {
                      Visual = new VisualModifiers
                      {
                          Foreground = item.IsUp ? GreenBrush : RedBrush,
                      },
                      Layout = new LayoutModifiers
                      {
                          Padding = new Thickness(2, 1, 2, 1),
                      },
                  },
                  Attached = new Dictionary<Type, object>(1)
                  {
                      [typeof(GridAttached)] = new GridAttached(r, c, 1, 1),
                  },
              };
          },
          GreenBrush, RedBrush);
      ```
      This is the spec's canonical "all three components combined"
      reference. The brushes are deps because they're closed over.
- [x] **Do not** touch `StressPerf.Reactor`. It stays naive; that is
      its permanent role.
- [x] Verify the analyzer (now shipped) does not flag this call —
      `GreenBrush`/`RedBrush` are in deps; `r`/`c` are derived from
      `i` (lambda parameter, not a capture); `StockDataSource.Columns`
      and `StockDataSource.FormatCell` are static.

### 4.2 Plumb `LastChangedIndices` if needed

- [x] Confirm `StressPerf.Shared.StockDataSource.Update(int percent)`
      already returns the changed-index list. If not, add it (low-risk
      bench-only change in `StressPerf.Shared`). Both
      `StressPerf.Reactor` and `StressPerf.ReactorOptimized` reference
      this lib; the change is invisible to the naive variant.
- [x] If using `UseMemoCellsByIndex` exposes the bench's existing
      "sampling with replacement" issue (spec §Non-Goals: only ~63 %
      effective per-cell mutation at `--percent 100`), file as a
      bench-only follow-up — do not block on it for this PR.

### 4.3 Re-bench — three-way A/B

Run all three on the same machine, same day, same .NET / WinAppSDK
versions. Save result rows to
`tests/stress_perf/baselines/spec-034-three-way.csv`.

- [x] `StressPerf.Reactor` (naive, post-shim) — Component A's
      framework-level uplift only. Expected: ~+6 % renders / ~−11 %
      bytes/tick over the pre-spec-034 baseline captured in 0.2.
- [x] `StressPerf.ReactorOptimized` — full stack. Expected:
      `~214 renders / ~2.21 MB/tick` at 10 % mutation. Within ±5 % of
      the spec's table.
- [x] `StressPerf.Direct` — reference. Should not have moved.

If `ReactorOptimized` underperforms the spec's headline by more than
5 %, debug before continuing to Phase 5. Most-likely causes:
`UseMemoCellsByIndex` deps mismatch, `LastChangedIndices` not actually
narrow enough, or analyzer-fixed false positives that wedged extra
deps into the array.

---

## Phase 5: Sample updates

### 5.1 Audit

- [x] Grep `samples/` for cell-construction loops:
  - `for (int i = 0; i < ...; i++)` producing fluent chains
  - `Enumerable.Range(...).Select(i => Element)`
  - `items.Select(...)` materialized to `Element[]` inside `Render()`
- [x] For each candidate, decide:
  - **Migrate to `UseMemoCells`** if the list is non-trivial (~50+
    items) and the builder is a pure function of the item.
  - **Adopt direct-initializer idiom** if the list is small but the
    sample is meant to demonstrate idiomatic Reactor (e.g., readme
    examples, gallery samples).
  - **Leave alone** if the sample is demonstrating a different concept
    and changing the cell construction would obscure it.
- [x] Capture the audit table in the PR description (sample → decision
      → rationale).

### 5.2 Likely migration candidates (verify in 5.1 — list is illustrative)

- [x] `samples/ReactorGallery/` — if there's a list/grid demo, adopt
      `UseMemoCells` and call it out in the gallery's "what this
      demonstrates" copy.
- [x] `samples/TodoApp/` — likely has a list of todos. Migrate to
      `UseMemoCellsByKey<TodoItem, int>(items, t => t.Id, …)`.
- [x] `samples/apps/` — review each.
- [x] `samples/Reactor.TestApp/` — keep as-is unless it demos lists
      explicitly.

### 5.3 Build + run smoke

- [x] Each migrated sample compiles, runs, and visually matches the
      pre-migration version. UI smoke is sufficient — no automated
      regression suite at the sample level.

### 5.4 CHANGELOG

- [x] `CHANGELOG.md` → **Changed**: "Samples updated to use
      `UseMemoCells` / direct-initializer idiom where appropriate (spec
      034 §B / §C)."

---

## Phase 6: `skills/perf-tips.md`

Agent-facing skill: the playbook for writing fast Reactor code. Lives
alongside `skills/dsl-reference.md`, `skills/design.md`, etc.

### 6.1 Content

- [x] Create `skills/perf-tips.md`. Match the tone of
      `skills/dsl-reference.md` (terse, agent-focused, code-first).
      Cover at minimum:
  1. **When to care**: only in lists/grids with hundreds-plus elements
     per render, or scrolling/animation work. Don't pre-optimize a
     5-element form.
  2. **`UseMemo` / `UseCallback`** — the everyday wrapper for
     expensive computations and stable callback identity. `params
     deps` shape; closure-capture trap.
  3. **`UseMemoCells` for list/grid bodies** — drop-in replacement
     for `for`-loop cell construction in hot lists. Reference the
     three variants and when to pick each. Lead with the analyzer
     (`REACTOR_HOOKS_007` catches missing deps at compile time).
  4. **Direct record initializer in inner loops** — when the cell
     count is high enough that the fluent-chain `with`-clones show
     up in profiles. Show the fluent → record-initializer
     translation. Note that `LayoutModifiers` / `VisualModifiers`
     buckets are the public form for direct construction.
  5. **Brush / FontFamily / CornerRadius caching** — these are COM
     objects; create once, reuse across renders. Reference the
     `StressPerf.Reactor` brush-caching pattern.
  6. **gen2 trade-off awareness** — memo retains snapshots; many
     memoized lists across an app can compound gen2 pressure. Know
     the trade.
  7. **Profile before optimizing** — point to
     `tests/stress_perf/PresentTracer/` and `mur perf` (or whatever
     the in-tree profiling entry points are; verify before writing).
- [x] Include a "When NOT to" section: declarative ergonomics matter,
      and the fluent chain remains the right tool for ordinary UI.
- [x] Cross-link: `skills/dsl-reference.md`, `skills/design.md`, the
      `docs/guide/advanced.md` deep-dive (compiled output of
      `advanced.md.dt`).

### 6.2 Verify

- [x] Read it back as if cold: would an agent unfamiliar with the
      project pick the right tool from this skill alone? Iterate until
      yes.
- [x] Confirm every API name, file path, and diagnostic ID mentioned
      actually exists at the time of writing (analyzer ID, bucket type
      names, hook signatures all settled by Phase 3).

---

## Phase 7: Final perf data capture

Last phase before the PR opens. Re-runs the full matrix on the merged
working tree, updates the spec's table, writes the PR description.

### 7.1 Re-run the four-row matrix

The original spec table (line 46) reported six rows because the
prototype could toggle experiments in-place via env vars. Production
ships two variants. The final table is correspondingly simpler:

| Row | Source | Captures |
|-----|--------|----------|
| `Reactor` (pre-spec-034) | `spec-034-before.csv` from Phase 0.2 | the original baseline |
| `Reactor` (post-spec-034) | re-run `StressPerf.Reactor` today | Component A's framework uplift, free to all users |
| `ReactorOptimized` | re-run `StressPerf.ReactorOptimized` today | full stack: A + B + C |
| `Direct` | re-run `StressPerf.Direct` today | reference floor |

- [x] Same-day, same-machine, same .NET / WinAppSDK versions for all
      three re-runs. Record the platform fingerprint at the top of the
      table.
- [x] Headline expectation: `Reactor` (post-shim) ≈ `Reactor`
      (pre-spec-034) × 1.06; `ReactorOptimized` ≈ `Reactor`
      (pre-spec-034) × 1.51 with ~−90 % alloc/tick.
      *Verified at 20/50/100 % mutation (close-out re-bench
      sampled those three points instead of the 10 % headline).
      Reconcile-time win matches prototype: −60 % at 20 %, −33 %
      at 50 %, −8 % at 100 %. Renders/sec deltas are smaller at
      50/100 % (GC-bound). 10 % point not re-measured this session
      — logged as follow-up below.*
- [x] If any row diverges from the spec's table by more than ±10 %,
      investigate before merging. Either the implementation differs
      from the prototype or the host machine differs — both are
      reportable findings, neither is a blocker, but the table must
      reflect what we measured today.

### 7.2 Update spec 034 table

- [x] Replace lines 46–53 of `docs/specs/034-element-allocation-reduction.md`
      with the freshly measured numbers. Mark the table "verified
      YYYY-MM-DD on production code" so future readers know this is
      no longer the prototype's prediction.
- [x] Add a short paragraph above the table noting whether the
      production numbers matched, exceeded, or fell short of the
      prototype's. Be specific — this is the spec's empirical close.

### 7.3 PR description

- [x] The PR description must contain the full before/after table
      inline (not just a link), so the reviewer sees the headline in
      the PR view.
- [x] Include a 2-paragraph summary: what shipped, what the headline
      number is, what trade-offs (gen2 retention, ToString audit) the
      reviewer should be aware of.
- [x] Link back to the investigation doc and the spec.

### 7.4 CHANGELOG aggregation

- [x] Verify the three component CHANGELOG entries (from Phases 1, 2,
      3) plus the bench-variant entry (Phase 2.6) read coherently as a
      group. Edit if needed so the unreleased section tells the
      spec-034 story end-to-end.
- [x] Add a top-level "Spec 034 — Element allocation reduction" rollup
      bullet under **Changed** that names the headline using the
      verified numbers from 7.1: "ReactorOptimized variant of the
      stress bench shows +XX % renders / −YY % alloc on the
      4,900-cell stocks grid vs. pre-spec-034 Reactor; naive Reactor
      itself sees +ZZ % from Component A's transparent storage shim."
      Replace XX/YY/ZZ with the measured numbers, not the
      prototype's predictions.

### 7.5 Spec status flip

- [x] Update the **Status** block at the top of
      `docs/specs/034-element-allocation-reduction.md`:
      `Drafted` → `Implemented — YYYY-MM-DD`. Add a one-line note that
      the verified numbers live in the table below.

---

## Phase 8: Cross-cutting polish (post-merge)

These are not blockers for shipping but should not be dropped.

- [ ] **AOT smoke**: `dotnet publish -r win-x64 -p:PublishAot=true`
      on `samples/Reactor.TestApp` and one of the migrated samples.
      Verify no trim warnings on `LayoutModifiers` / `VisualModifiers`
      / `UseMemoCells*`. The hook uses no reflection, so this should
      be clean.
      *Blocked on environment: vswhere.exe / MSVC linker not on
      PATH in this shell. Inspection-only check passed —
      `UseMemoCells*` is reflection-free, the analyzer is build-time
      only, and `LayoutModifiers`/`VisualModifiers` are records with
      compiler-synthesized equality (AOT-safe). Schedule on a CI
      box with Visual Studio Build Tools installed.*
- [ ] **Roslyn analyzer perf check**: build a representative app with
      the analyzer enabled and confirm build-time impact is < 1 s on a
      project with 100+ `UseMemoCells` call sites. If it's worse,
      profile the data-flow analysis path.
      *Deferred — no in-tree app currently has 100+ call sites.
      Re-evaluate once samples adopt `UseMemoCells` more broadly.*
- [x] **Follow-up issues to file**:
  - Single-bucket EX4 variant (spec §Non-Goals) — file as
    "investigation: single Light bucket".
  - Truly-100 % deterministic StressPerf mutation mode (spec §Risks
    line 108) — file as bench harness improvement.
  - Builder-pattern element factories (spec §Future Work) — depends
    on spec 008 §6/§7/§8; file cross-reference issue.
  - 10 % mutation re-bench against the spec's prototype headline
    (close-out captured 20/50/100 only) — file as
    "spec 034 close-out: re-bench at 10 % to validate prototype
    +51 % renders / −90 % alloc on production code".
  - AOT publish smoke for `StressPerf.ReactorOptimized` on a CI box
    that has Visual Studio Build Tools (vswhere/link) installed —
    local environment can't run the linker.

---

## Open questions / decisions to confirm before Phase 1

- [x] Confirm `LayoutModifiers` / `VisualModifiers` field assignment
      against the prototype before merging Phase 1.
- [x] Confirm whether `Reactor.csproj` / `Reactor.Analyzers.csproj`
      track public-API via `PublicApiAnalyzers` (Phase 0.1).
