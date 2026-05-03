# Changelog

All notable changes to Reactor will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
once a `1.0.0` release is cut. While the project is pre-1.0 and labeled experimental,
the public API surface may change between releases without notice.

<!--
Conventions for contributors:

  * Use the standard Keep-a-Changelog buckets: Added / Changed / Deprecated /
    Removed / Fixed / Security. Group entries under those buckets, not under
    per-spec or per-phase headings.
  * Cross-reference the originating spec on every line, e.g. "(spec 033 §1)",
    so readers can navigate from changelog → design rationale.
  * Within a bucket, prefer ordering by spec/section number for predictable
    reading.
  * Cutting a release: rename `## [Unreleased]` to `## [x.y.z] — YYYY-MM-DD`
    and add a fresh empty `## [Unreleased]` block (with all six bucket
    sub-headings) above it.

Spec 033 (WinUI/XAML reviewer feedback response) is the first set of entries
to land under these conventions; subsequent specs follow this shape.
-->

## [Unreleased]

### Added

- `Microsoft.UI.Reactor.Hooks.UseMemoCells` /
  `UseMemoCellsByKey` / `UseMemoCellsByIndex` — cell-level memoization
  hooks (extension methods on `RenderContext`, plus matching `Component`
  shims) for high-frequency list/grid bodies. Cells whose item value
  (and declared deps) haven't changed since the previous render are
  reused by reference; the reconciler short-circuits on
  `ReferenceEquals` and skips diffing entirely. (spec 034 §C)
- `REACTOR_HOOKS_007` analyzer + codefix — warns when a `UseMemoCells`
  builder lambda closes over a value that isn't declared in the
  `params deps` list, which would silently render stale. The codefix
  appends the missing capture to the deps slot. Indirect captures
  through helper methods are a documented blind spot. (spec 034 §C)
- "Memoizing list cells" section in `docs/guide/advanced.md` covering
  the three overloads, when each is the right hammer, the gen2
  trade-off, and the analyzer-as-safety-net story. (spec 034 §C)
- `tests/stress_perf/StressPerf.ReactorOptimized` — sibling bench
  variant that demonstrates the spec-034 §B direct-record-initializer
  idiom for inner-loop cell construction. The naive `StressPerf.Reactor`
  variant stays unchanged and remains the framework-level baseline; the
  new optimized sibling is the reference implementation of the perf-tips
  skill. Wired into `run_stocks_grid_baseline.ps1`,
  `run_bench_aot_publish.sh`, `run_benchmark.sh`, and
  `run_sweep_arm64.ps1`. (spec 034 §B)
- "Hot loops" section in `docs/guide/advanced.md` documenting when to
  reach for direct record initializers, the trade-offs vs the fluent
  chain, and a side-by-side worked example. Source template at
  `docs/_pipeline/templates/advanced.md.dt`. (spec 034 §B)
- `Expr(Func<Element?>)` factory in `Microsoft.UI.Reactor.Factories` for inline
  block-expression bodies inside a DSL tree, removing the
  `((Func<Element?>)(() => …))()` cast ceremony. Pure composition — no hooks,
  no memoization, no reconciler boundary. (spec 033 §5)
- `IPersistedStateScope` interface, `PersistedScope` enum (`Window` /
  `Application`), `ApplicationPersistedScope` (process-wide singleton at
  `ApplicationPersistedScope.Default`, capacity 4096), and
  `WindowPersistedScope` (per-host instance, capacity 1024). All backed by an
  internal `LruCache<TKey,TValue>`. New `RenderContext.UsePersisted<T>(key,
  initial, PersistedScope)` overload makes the scope explicit. (spec 033 §2)
- `Microsoft.UI.Reactor.Factories.RenderEachTime(Func<RenderContext, Element>)` —
  explicit factory for "inline component with own hooks that re-renders every
  parent render". Replaces the soft-deprecated `Func(...)` for the rare cases
  that genuinely want always-re-render semantics. (spec 033 §4)
- `Microsoft.UI.Reactor.GridSize` value type with `Auto` / `Star(weight)` /
  `Px(pixels)` smart constructors, implicit conversion to
  `Microsoft.UI.Xaml.GridLength`, and a strict invariant-culture string
  parser (`Parse`). New typed `Grid(GridSize[], GridSize[], …)` factory
  overload. (spec 033 §1)
- `samples/InteropFirst` — XAML-window-hosts-Reactor demonstration with
  shared `ObservableCollection<Order>`, shared `ICommand`s bridged through
  `CommandInterop.FromCommand`, and shared `App.xaml` brush resources flowing
  through props into a Reactor `Component<TProps>`. (spec 033 §7)
- `BackdropKind` enum and `.Backdrop(BackdropKind)` / `.Backdrop(Func<SystemBackdrop?>)`
  modifier on the root tree for declarative Mica / Acrylic on Reactor-hosted
  windows. `ReactorHost` applies the modifier at the end of each reconcile
  pass and resets the window's backdrop on dispose; `ReactorHostControl` that
  does not own its window no-ops with a one-shot debug log. (spec 033 §6)
- `ElementRef<T>` typed-ref wrapper (`Microsoft.UI.Reactor.Input`),
  `UseElementRef<T>()` hook (`Microsoft.UI.Reactor.Hooks`), and a strongly-typed
  `.Ref<T,TElement>(...)` modifier overload. The typed surface removes the
  `(Button)ref.Current` cast at consumers and adds a DEBUG-only assertion when
  a typed ref is bound to an element of the wrong concrete type. AOT-safe and
  reflection-free at the public surface. (spec 033 §3)
- `Component.UsePersisted<T>(key, initial, PersistedScope)` three-arg overload
  so component subclasses can declare the persisted-state scope (Window vs
  Application) explicitly at the call site, matching the
  `RenderContext.UsePersisted` overload added earlier. (spec 033 §2)

### Changed

- **Spec 034 — Element allocation reduction.** Three independent
  allocation cuts in one PR: bucketed `ElementModifiers` (transparent
  storage shim, ~−11% bytes/tick on the 4,900-cell stress grid),
  direct-record-initializer idiom for inner cell loops (~−60% bytes
  per cell), and `UseMemoCells` cell-level memoization. Verified at
  PR-close on ARM64 Release with full ETW Present-tracking across
  10/20/50/100% mutation, all eight stress_perf variants:
  **ReactorOptimized at 10% mutation reaches 17.1 Effective Refresh/s
  — within noise of DirectX (17.2) and Wpf (17.9), and +66% over
  naive Reactor (10.3).** Reconcile-time win on the same A/B: −76% at
  10% (32.5 ms → 7.9 ms), −61% at 20%, −31% at 50%, −12% at 100% —
  memo's win tracks the partial-reuse opportunity exactly as
  predicted. DirectX runs away at saturation (50%+) — no allocating
  framework can keep up there. Component A in isolation (naive
  Reactor pre-shim vs post-shim, same source, no app-code changes)
  shows renders/sec within run-to-run noise at 20/50/100% — its win
  is allocation-side, not renders-side, on this hardware. See
  `docs/specs/034-element-allocation-reduction.md` § "Verified
  close-out — 2026-05-03" for the full eight-variant matrix and
  reads. (spec 034)
- `ElementModifiers` now stores layout and visual fields in
  `LayoutModifiers` / `VisualModifiers` sub-records. Existing call sites are
  unaffected — public properties (`Padding`, `Margin`, `Foreground`,
  `Background`, …) shim through to the appropriate bucket on read and write.
  Perf-critical inner loops may construct buckets directly via the new
  `Layout = …` / `Visual = …` initializer slots to avoid a fat
  `ElementModifiers` clone per fluent step. (spec 034 §A)
- `PersistedStateCache` rewritten over an LRU cache with eviction-on-full
  semantics. The previous "refuse new keys when 4096 entries are present"
  policy is replaced — later, hotter keys are no longer starved by the
  first 4096 keys ever recorded. Application-scope registers an
  `Windows.System.MemoryManager.AppMemoryUsageIncreased` handler and trims
  to 25% of capacity when the OS reports `OverLimit` / `High`. Best-effort:
  hosting models that do not expose the event log a notice and carry on.
  Key validation now requires non-empty keys ≤ 256 chars. (spec 033 §2)
- `GridDefinition` gains a strongly-typed constructor accepting `GridSize[]`
  for columns and rows. The legacy string-array constructor is preserved for
  backward compatibility. (spec 033 §1)
- `ApplicationPersistedScope` and `WindowPersistedScope` now emit one-line
  `Debug.WriteLine` diagnostics on construction, disposal, and (for the
  application scope) memory-pressure trim. Logs only counts and capacity —
  never keys or values, since keys may be derived from user-controlled
  identifiers in apps. (spec 033 §7.10)
- `samples/Reactor.TestApp/Demos/PersistedDemo`, `NavigationDemo`, and
  `samples/apps/regedit` migrated to the explicit
  `UsePersisted(key, initial, PersistedScope.Window)` overload to document
  per-window intent at the call site. (spec 033 §2)

### Deprecated

- `Microsoft.UI.Reactor.Factories.Grid(string[], string[], params Element?[])`
  is marked `[Obsolete]`. Use the strongly-typed
  `Grid(GridSize[], GridSize[], params Element?[])` overload with
  `GridSize.Auto` / `GridSize.Star(weight)` / `GridSize.Px(pixels)` instead.
  Slated for removal in the next minor release. (spec 033 §1)
- `Microsoft.UI.Reactor.Factories.Func(Func<RenderContext, Element>)` is
  marked `[Obsolete]`. Replace with `Memo(ctx => …)` (render once + state
  changes) or `RenderEachTime(ctx => …)` (always re-render). Slated for
  removal in the next minor release. (spec 033 §4)

### Removed

### Fixed

### Security
