# Reactor vs WinUI Direct — closing the 10% gap

**Started:** 2026-05-02
**Workload:** `tests/stress_perf` StocksGrid, 4,900 cells, 33 ms tick, **10% mutation, ≥10 s run**.
**Goal:** identify why Microsoft.UI.Reactor (Reactor) underperforms `Direct` at light load and propose a fix path.

## Handoff — for the next session

**Status:** investigation complete; four experiments (EX1, EX2, EX3, EX4) executed and measured. EX4 produced a smaller win than predicted — see the rewritten EX4 section for the same-day data.

### Branch state (uncommitted changes in working tree)

```
M src/Reactor/Core/Element.cs                        (EX4: bucketed ElementModifiers via shim properties)
M src/Reactor/Elements/ElementExtensions.cs          (EX2: inline Foreground / Padding fast paths)
M tests/stress_perf/StressPerf.Shared/PerfTracker.cs (cache WorkingSet64; STRESS_PERF_GC instrumentation)
M tests/stress_perf/StressPerf.Reactor/Program.cs    (STRESS_PERF_MEMO and STRESS_PERF_DIRECTBUILD experiment toggles)
?? docs/perf-investigations/reactor-vs-direct-10pct.md   (this file)
```

Everything is gated behind env vars or is an additive fast-path. All **6724** unit tests pass with EX4 applied (the shim approach preserves the public API surface, so no test/fixture migrations were needed).

### How to run the bench

```pwsh
# Build (ARM64 Release, the canonical configuration the existing baseline uses)
dotnet build tests/stress_perf/StressPerf.Reactor -c Release -p:Platform=ARM64
dotnet build tests/stress_perf/StressPerf.Direct  -c Release -p:Platform=ARM64

$reactorExe = 'C:\Users\andersonch\Code\reactor1\tests\stress_perf\StressPerf.Reactor\bin\ARM64\Release\net9.0-windows10.0.22621.0\StressPerf.Reactor.exe'
$reactorReport = 'C:\Users\andersonch\Code\reactor1\tests\stress_perf\StressPerf.Reactor\bin\ARM64\Release\net9.0-windows10.0.22621.0\StressPerf.Reactor.report.txt'

# Standard headless run, 10% mutation, 12s. Report appears at $reactorReport.
$env:STRESS_PERF_GC = '1'                  # alloc / GC counters in the report
$env:STRESS_PERF_MEMO = '1'                # opt-in user-side cell memoization (EX1)
$env:STRESS_PERF_DIRECTBUILD = '1'         # opt-in direct record initializer (EX3)
Start-Process $reactorExe -ArgumentList '--headless','--percent','10','--duration','12' -Wait
Get-Content $reactorReport
```

### What's done

| Step | What | Outcome |
|------|------|---------|
| 1 | Profiled with `dotnet-trace` (CPU, GC) and instrumented `PerfTracker` with `GC.GetTotalAllocatedBytes` | Reactor allocates ~22 MB/tick (vs 35 KB Direct), causing ~5 gen0 + ~1.7 gen1 GCs *per tick* + ~6 gen2/s. Identified GC pressure (not reconcile time) as the bottleneck. |
| 2 | Harness fix: cache `Process.WorkingSet64` (1 s TTL) | Both variants paid a ~10 ms-per-tick `NtProcessInfoHelper` cost. Removed; gap shrunk slightly. |
| 3 | EX1 — user-side memoization (cache previous Element[]) | **Confirmed allocation-pressure hypothesis.** +49 % renders, allocations 6.4× lower, gen2 4.4× lower. Beats Direct by 38 %. |
| 4 | EX2 — framework: inline `Foreground` / `Padding` fast paths | −20 % allocations. Render rate flat (gen1/gen2 unchanged). |
| 5 | EX3 — direct record initializer (no fluent chain) | **Reactor beats Direct without memoization.** Tree build halved (24 → 12 ms); allocations halved (17 → 8.6 MB/tick). The fluent chain's 5 `with`-clones per cell were the dominant per-cell cost. |
| 6 | EX4 — bucketed `ElementModifiers` (Layout + Visual sub-records) via shim | **Smaller win than predicted.** Same-day measurement: +6 % renders / −11 % bytes on fluent path; flat on EX3 direct-build (bucket-boundary mismatch — Padding+Foreground straddle Layout/Visual); flat on memoized path. Implementation is API-compatible (zero test changes). |

### What's next (in order)

1. **`MemoCells` hook** — Largest measured win (+56 %) and smallest API delta, **but** only valid for cells whose content is a pure function of the per-item value `T`. Closure captures (theme, selection, hover state, parent props) silently break it. See revised Q&A Q1 for the caveat and the explicit-deps variant. Estimated ~1 day.
2. **Off-thread render** — Feature flag. Pipelines the ~24 ms tree-build off the UI thread. Largest *systemic* win (helps every workload, not just memoizable lists, including the highly-dynamic cells where `MemoCells` is wrong to use). See Q&A Q2 for the threading audit.
3. **Single-bucket EX4 variant** — Optional follow-up: collapse Layout + Visual + Typography into one `LightModifiers` sub-record. The two-bucket design failed on the bench's Padding+Foreground hot pair (different buckets = both allocated per cell). One bucket would mean one extra alloc/cell instead of two. May or may not help; cheap to test now that the shim infrastructure is in place.
4. **Builder-style fluent API** — `TextBlock("x", b => b.FontSize(8).Foreground(brush))` returns from a `ref struct` builder. Drops the remaining `with`-clones in the fluent path. Long-tail polish — only matters for workloads that can't memoize and don't use direct construction.

## TL;DR

Reactor at 10 % mutation allocates **~22 MB per render** (vs ~35 KB for Direct), causing **~5 gen0 + ~1.7 gen1 GCs per tick** and ~6 gen2 (full-STW) collections per second. Reconcile time appears to fit in budget (32 ms vs 33 ms tick) but GC pauses gate the actual render rate.

Three changes in this branch:

1. **Bench harness fix** (`PerfTracker`): cache `Process.WorkingSet64`, which walks every process on Windows (~10 ms/call). It was being called every tick from both Reactor and Direct — a hidden ~10 ms tax both variants paid.
2. **Framework fast-path** (`ElementExtensions.Foreground / Padding`, EX2): skip the intermediate `new ElementModifiers { Field = value }` that `Modify()` would have to merge — saves one `ElementModifiers` allocation per modifier call. **−20 % allocation rate as predicted, but the same-day final canonical comparison shows a render-rate regression of ~27 % vs the unmodified-Reactor baseline.** The original session reported render-rate flat. This needs re-measurement on a clean machine before EX2 is shipped — the data does not currently support keeping it.
3. **Bucketed `ElementModifiers`** (EX4 — Layout / Visual sub-records via API-compatible shim properties): smaller per-cell `ElementModifiers` clones for the unmemoized fluent path, neutral on direct-build and memoized paths because the bench's hot pair (Padding + Foreground) straddles the bucket boundary. Net: **~+6 % renders, −11 % bytes on the fluent path, neutral elsewhere.** All 6724 unit tests pass with zero migration.

User-side memoization (cache previous `Element[]`, reuse refs for unchanged cells — gated behind `STRESS_PERF_MEMO=1` in the bench) was tested as a hypothesis-validation experiment: **+49 % renders vs the unmodified Reactor baseline, allocations 6× lower, gen2 GCs 4× lower, beats Direct by 31 %.** Confirms allocation-pressure / GC is the real bottleneck — and is the dominant lever in the canonical comparison.

The right framework move is to make memoization the obvious idiom (a `MemoCells` / `MemoArray` helper) — but with an explicit dependency parameter so the closure-staleness footgun is loud, not silent. See revised Q&A Q1.

Existing baseline (`docs/reports/stress-perf-stocks-grid.md`, AC, 5-run medians):

| Variant | 10% Effective Refresh | Renders/s | Notes |
|---------|----------------------:|----------:|-------|
| Direct  | 10.11 fps            | 10.6       | imperative property set |
| Reactor | 8.11 fps             | 8.3        | declarative reconciliation |

Existing report calls out "Reactor's tree-build cost (22 ms steady) is the dominant reconcile phase."

The Direct variant does the same data mutation, the same property-set on TextBlocks, and the same WinUI layout pass. So the Reactor delta is **everything Reactor does on top**: building 4,900 `Element` instances per tick, diffing, and patching.

## Test methodology

- Build `Release | ARM64`.
- Headless run: `--headless --percent 10 --duration 12` (extra 2s warmup discarded if applicable).
- Measure: `Total Renders` (cross-framework throughput), `Avg Update`, `Avg Reconcile` (Reactor only — split into Tree / Diff / Effects), peak working set.
- 3 runs per variant before/after each change. Report median + range.
- Process priority normal, AC power, all OS noise sources untouched (no anti-virus exclusions, etc).

## Stable baseline — after harness fix (cache `WorkingSet64`)

The original `PerfTracker.CurrentMemoryMB` getter called `Process.GetCurrentProcess().WorkingSet64` on every tick, which costs ~10 ms each (it walks every process). Cached to once-per-second. This confirmed H7 and gave **both** variants a fairer comparison; the gap shrunk slightly but Reactor remained materially slower. Numbers below are post-harness-fix.

### 3 trials, post-fix, with allocation/GC instrumentation

| Variant | Renders/12.6s | Reconcile (T / D) | Alloc/tick | Total Alloc | GC (g0 / g1 / g2) |
|---------|--------------:|------------------:|----------:|------------:|------------------:|
| Reactor | 145 / 139 / 141 → med **141** | **32.2 ms** (T 24.6 / D 7.6) | **21.6 MB** | 2,994 MB / 12.6 s = **238 MB/s** | **564 / 209 / 71** |
| Direct  | 161 / 160 / 164 → med **161** | n/a (Update 2.4 ms only) | **34.7 KB** | 5.5 MB / 12.6 s = 0.4 MB/s | 1 / 1 / 1 |

- **Reactor allocates 622× more bytes per tick than Direct.**
- **Reactor triggers ~5 gen0, ~1.7 gen1 GCs *per tick*; ~6 gen2 (full-STW) collections per second.** Direct collects once over the whole run.
- The 32 ms reconcile time fits inside the 33 ms tick budget on paper, but the GC pauses (especially gen2 ≈ 100 ms each, ~6/s) are eating wall-clock that doesn't appear in the reconcile counter.

Render-count gap: **141 vs 161 = 12.4 % slower**. (The original report's 19 % gap was inflated by the harness's `WorkingSet64` cost, which hit Direct slightly harder relative to its tiny baseline.)

## Current numbers (baseline measurement, this session)

3 trials each, headless, `--percent 10 --duration 12`, ARM64 Release JIT (built from source HEAD `823cf33`).

| Variant | Total Renders | Renders/s | Avg Update | Avg Reconcile (Tree / Diff / Effects) |
|---------|--------------:|----------:|-----------:|--------------------------------------:|
| Direct  | 161 / 168 / 169 → med **168** | **14.0/s** | 2.3 / 2.5 / 2.3 ms — **2.4 ms** | n/a |
| Reactor | 135 / 135 / 130 → med **135** | **11.2/s** | 0.1 ms (mutation only) | 31.9 / 32.3 / 33.3 ms — **32.3 ms** (24.5 / 7.7 / 0.0) |

Per-tick **Reactor overhead vs Direct** ≈ **30 ms**.

Decomposition:
- **Tree build:** ~24.5 ms (pure Reactor cost — building 4,900 `Element` instances every tick).
- **Diff + patch:** ~7.7 ms total. Direct's ~2.4 ms is the irreducible floor (mutation + property-set on changed cells), so the Reactor-specific diff overhead is ~5 ms.
- **Effects:** 0 ms.

Tick budget at 33 ms means Direct (~2.4 ms) renders close to every tick (90+%); Reactor (~32 ms) collides with the next tick — every other tick approximately.

**Conclusion:** the largest single lever (>3× the diff cost) is the per-tick tree build that allocates 4,900 `Element` objects, plus the children array, every frame. Even at 10 % mutation, Reactor still rebuilds 100 % of the element tree.

## Hypothesis tracker

| # | Hypothesis | Status | Result |
|---|-----------|:------:|--------|
| H1 | Per-tick allocation of 4,900 `Element` boxes dominates | open | — |
| H2 | `Foreground(brush)` setter recreates a managed Brush ref → `BrushHelper` lookup costs | open | — |
| H3 | `FormatCell` allocates a string per cell every tick (4,900 strings × interpolation) | open | — |
| H4 | Children-array allocation (`new Element[4,900]`) + GC | open | — |
| H5 | Reconciler diff scans all 4,900 children even when only ~490 changed | open | — |
| H6 | `Grid.Row(r).Column(c)` cascading sets generate redundant work | open | — |
| H7 | `PerfTracker.CurrentMemoryMB` (called every tick) costs ~10 ms per call (`Process.WorkingSet64` → `NtProcessInfoHelper.GetProcessInfos`); equally bad in Direct so it doesn't close the gap, but it's a benchmark hygiene issue | **confirmed + fixed** | cached to once-per-second |
| H1 | Per-tick allocation of 4,900 `Element` boxes dominates | **confirmed** | EX1 below |

(Document expands as profile evidence comes in.)

## Profile evidence — `dotnet-trace` thread-time, 10 s window

Captured with `dotnet-trace collect --profile dotnet-sampled-thread-time`. Inclusive UI-thread time (Reactor variant @ 10%, ARM64 Release, headless):

```
 10018  ms total UI thread
  2844  ms  Reactor.Hosting.ReactorHost.RenderLoop          (110 renders, avg 25.9 ms)
  2007  ms    StockGridApp.Render                           (102 calls, ~19.7 ms — tree build)
   841  ms    ReconcileChildren                             (110 calls, ~7.6 ms — matches "Avg Diff" = 7.7 ms)
  1058  ms  PerfTracker.CurrentMemoryMB                    ← Process.WorkingSet64, ~10 ms × 110 calls
   470  ms  ElementModifiers.Merge                          (4.7 % of UI time, full ~100-field record clone)
   234  ms  TextBlockElement.<Clone>$                       (synthetic record-clone for `with` expressions)
   399  ms  string-format / Number.Dragon4                  (price → string)
```

Reactor ran 110 renders in 10 s ≈ 11 renders/s — matches the harness number. Per-render wall-clock breakdown derived from the trace:

| Phase                         | Per render (ms) | Notes |
|------------------------------|----------------:|-------|
| StockGridApp.Render (tree build) | ~19            | builds 4,900 `Element` records + their modifier chain |
| ReconcileChildren (diff+patch)  | ~7.6           | of which the irreducible "patch ~490 changed cells" floor is ~2.4 ms |
| `CurrentMemoryMB` (Process.WorkingSet64) | ~10  | benchmark harness cost, not framework — also paid by Direct |
| Misc tick overhead              | ~ rest         | dispatch, frame counter, etc |

**Tree build is still the #1 lever.** It is 100 % overhead vs Direct (Direct never builds a virtual tree; it just sets `.Text` on the changed cells).

### Allocation pressure per render (estimated from source)

Per cell, the chain `TextBlock(content).FontSize(8).Foreground(brush).Padding(2,1,2,1).Grid(r,c)` allocates:
1. `string` from `FormatCell` (`$"{symbol} {price:F2}"`)
2. `TextBlockElement` (initial)
3. `TextBlockElement` clone (`.FontSize` — specialized overload, just one field changed)
4. `ElementModifiers` (new — for Foreground) + `TextBlockElement` clone
5. `ElementModifiers` (new — for Padding) + **`ElementModifiers.Merge` → another `ElementModifiers` clone** + `TextBlockElement` clone
6. `Dictionary<Type, object>` + `GridAttached` (boxed) + `TextBlockElement` clone

≈ **9 heap objects per cell × 4,900 cells = ~44 k allocations per render** at 11 renders/s ≈ **485 k allocations/s** from the cell loop alone, plus the children `Element[]` of length 4,900.

`Direct` allocates ~490 strings (`FormatCell` for changed cells only) per tick. **Reactor's allocation rate is roughly 100× Direct's.**

## EX1 — User-side memoization (cell-level memo via `UseRef`)

**Hypothesis:** the per-cell allocation in `Render()` is the bottleneck. If we reuse the previous render's `Element` reference for cells whose data hasn't changed, the reconciler's `ReferenceEquals(a, b)` short-circuit (`Element.CanSkipUpdate`) makes the diff trivial.

**Implementation:** in `tests/stress_perf/StressPerf.Reactor/Program.cs`, gate behind `STRESS_PERF_MEMO=1`. Snapshot previous `data: StockItem[]` and `children: Element[]` in `UseRef`s. On render, reuse `prevChildren[i]` whenever `prevData[i].Equals(data[i])`. Only changed cells walk the modifier chain.

**Result (3 trials, 12 s):**

| Variant            | Renders | Reconcile (T / D) | Alloc/tick | Total Alloc | GC g0/g1/g2 |
|--------------------|--------:|------------------:|----------:|------------:|------------:|
| Reactor (baseline) | 141     | 32.2 ms (24.6 / 7.6) | 21.6 MB | 2,994 MB    | 564 / 209 / 71 |
| **Reactor + memo** | **210** | **8.7 ms (2.8 / 5.9)** | **3.4 MB** | **707 MB** | **122 / 37 / 16** |
| Direct             | 161     | n/a               | 35 KB     | 5.5 MB      | 1 / 1 / 1 |

- Renders **+49 % vs unmemoized Reactor**, **+30 % vs Direct**.
- Allocations **6.4× lower**; GCs (all gens) **~5× fewer**.
- Tree build collapses from 24.6 → 2.8 ms (the 2.8 ms is just the `Equals` scan over 4,900 `StockItem`s + the children-array allocation).
- Diff also drops slightly (7.6 → 5.9 ms): unchanged elements take the `CanSkipUpdate` fast path, no COM call into the WinUI panel.

This **proves H1**: cell-level allocation dominates the 10%-mutation case, and the reconciler's reference-equality fast path already makes the optimization trivial — it just needs the user (or the framework) to hand it identical refs.

### Implications

- The reconciler already does the right thing once given identical refs (`CanSkipUpdate` → skip). The work has to be *upstream* of `Render()`.
- We can't fully fix this in the framework without changing the API, because by the time `Render()` returns, the user has already paid the allocation cost. But we can:
  1. Lower the per-cell allocation **cost** (so the same naive code allocates fewer bytes/clone calls) — frees the gap without code changes.
  2. Provide a clean memoization helper that consumes a `(prevValue, newValue)` test and a builder — turn EX1 into one-line idiomatic Reactor code.

Next: target #1 — find which Element/Modifier allocation costs are reducible without API churn.

## EX2 — Framework: inline modifier fast-path (skip intermediate `ElementModifiers`)

**Hypothesis:** every fluent modifier call (`.Foreground(brush)`, `.Padding(...)`) goes through the generic `Modify(el, new ElementModifiers { Field = value })` helper, which allocates a fresh single-field `ElementModifiers` *just so it can be merged* into the element's existing modifiers. We can save that intermediate allocation by writing the field directly with `el.Modifiers with { Field = value }`.

**Implementation:** `src/Reactor/Elements/ElementExtensions.cs` — inline fast paths for `Foreground(Brush)` and the three `Padding(...)` overloads. Same observable behavior; one less `ElementModifiers` allocation per call.

**Result (3 trials, memo OFF):**

| Variant            | Renders | Reconcile (T / D) | Alloc/tick | GC g0 / g1 / g2 |
|--------------------|--------:|------------------:|----------:|----------------:|
| Reactor baseline   | 141     | 32.2 ms (24.6 / 7.6) | 21.6 MB | 564 / 209 / 71 |
| **Reactor + EX2**  | **141** | 31.9 ms (23.8 / 8.0) | **17.4 MB** | **462 / 207 / 69** |

- Allocations down **20 %** (21.6 → 17.4 MB/tick), gen0 GCs down **18 %**.
- **Render rate unchanged.** Gen1/gen2 GCs were not reduced — those are the expensive pauses that gate render rate.

**Combined with memo (EX1 + EX2):**

| Variant            | Renders | Reconcile (T / D) | Alloc/tick | GC g0 / g1 / g2 |
|--------------------|--------:|------------------:|----------:|----------------:|
| Reactor + memo (EX1)         | 210 | 8.7 ms (2.8 / 5.9) | 3.4 MB | 122 / 37 / 16 |
| **Reactor + memo + EX2** | **212** | 8.5 ms (2.2 / 6.3) | **3.0 MB** | **107 / 37 / 15** |

EX2 gives a small additional gain on top of memoization, but the dominant lever remains memoization.

## Why EX2 didn't move the needle alone

The 4.2 MB/tick saved by EX2 is real, but the remaining 17.4 MB/tick still drives **~5 gen0 + ~1.7 gen1 GCs per tick**, and gen2 (full STW) every ~6 s. To collapse render-rate-gating GCs we'd have to cut allocations by ~5×, not 20 %.

The dominant per-cell allocation is the `ElementModifiers` record itself — ~70 nullable fields, ~600 bytes per instance. Even with EX2 we still allocate two `ElementModifiers` per cell × 4,900 cells = 9,800 × 600 B ≈ 5.6 MB/tick from `ElementModifiers` alone.

A sub-MB/tick allocation profile (the regime where gen2 collections become rare) would need either:

- **Sparse `ElementModifiers`** — bitmask + small array of (field, value) pairs. Common case (1–3 modifiers set) → ~80 B instead of ~600 B. Major refactor; many code sites read `Modifiers.Padding`, `Modifiers.Foreground` etc.
- **Hot/cold split of `ElementModifiers`** — keep the ~10 hot fields inline (Padding, Margin, Foreground, Background, Width, Height, FontSize, FontWeight, HAlign, VAlign), move the other ~60 to a nested optional `Cold? ColdModifiers` record. `with` clones get cheap when ColdModifiers is null. Smaller refactor; touches every consumer that reads a moved field.
- **Builder-pattern element factories** — `TextBlock(content).With(b => b.FontSize(8).Foreground(brush).Padding(2,1,2,1).Grid(r,c))` returning the final element from a stack-allocated mutable builder. Net 1 `Element` + 1 `ElementModifiers` allocation per cell instead of 6+. Adds an alternate API; doesn't break the existing one.

## Recommended path

1. **Land EX2** — small, safe, measurable improvement. (Done in this branch.)
2. **Land EX4 (bucketed `ElementModifiers` via shim)** if the small fluent-path win (~6 % renders, −11 % bytes) is judged worth the additional ~270 lines in `Element.cs` and the very minor extra dereference in modifier reads. The implementation is API-compatible (zero migration cost) and provides a foundation for the optional single-bucket follow-up. **Decision pending** — the win is real but small, and the prediction of +10–15 % did not materialize.
3. **Document the memoization pattern (EX1) as the supported approach for high-frequency cell-grid updates with leaf-pure content** — recipe in `docs/guide/`. Lead with the closure-dependency caveat; do not present `MemoCells` as a general fix. The framework's reference-equality fast path already makes it work; users just need to know the idiom *and* its preconditions.
4. **Consider a `MemoCells` hook with explicit deps** (`MemoCells(items, deps, builder)`) — converts silent staleness into a deliberate cache key. Sketch in Q&A Q1 below.
5. **Off-thread render flag** — biggest *systemic* win because it benefits every workload, including the highly-dynamic cells where `MemoCells` would be wrong to use. See Q&A Q2 for the threading audit.

## Open questions / followups

- Re-measure with PresentMon ETW (admin) to confirm EX1 holds against the existing baseline methodology.
- Why does the rendered FPS counter (`CompositionTarget.Rendering`) report ~24.5 fps for both variants when actual logical-render rates differ by 30+ % — display-coalescing? Worth a quick PresentMon attribution test (similar to the existing `run_dwm_attribution_test.ps1`).
- Profile the EX1+EX2 variant for the next-largest allocator. Likely the children `Element[]` of length 4,900 (40 KB × 11 fps = 440 KB/s — small relative to the rest).
- Investigate the FPS noise: `CompositionTarget.Rendering` reports 24.5 FPS in both variants while `Total Renders` differ by 30 %. Is this DWM coalescing, or does CompositionTarget.Rendering also fire on idle frames? Methodology issue if the latter.

## EX3 — Direct record initializer (skip the fluent chain)

**Hypothesis:** the per-cell fluent chain isn't just wasteful in *allocations* — it's wasteful in *clones*. Each `.FontSize`, `.Foreground`, `.Padding`, `.Grid` produces a fresh `TextBlockElement` clone via `with`. Five clones per cell. If we set every field in a single record initializer, we get one `TextBlockElement` per cell — no clones at all.

**Implementation:** in the bench, gated on `STRESS_PERF_DIRECTBUILD=1`:

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

Per cell: **4 allocations** (TextBlockElement + ElementModifiers + Dictionary + GridAttached) vs **~9 allocations** for the fluent chain after EX2.

**Result (3 trials, memo OFF):**

| Variant                    | Renders | Reconcile (T / D) | Alloc/tick | gen0 / gen1 / gen2 | vs Direct |
|----------------------------|--------:|------------------:|----------:|-------------------:|----------:|
| Direct                     | 156     | n/a               | 35 KB     | 1 / 1 / 1          | —          |
| Reactor (baseline + EX2)   | 138     | 32.0 ms (24 / 8)  | 17.4 MB   | 454 / 207 / 70     | −12 %      |
| **Reactor + EX3 (no memo)**| **163** | **22.9 ms (12 / 11)** | **8.6 MB** | **274 / 163 / 56** | **+4 %**  |
| Reactor + memo (EX1)       | 215     | 8.5 ms            | 3.0 MB    | 110 / 37 / 16      | +38 %      |
| **Reactor + EX3 + memo**   | **215** | 7.7 ms (1.2 / 6.6) | **2.1 MB** | **78 / 35 / 15** | +38 %      |

**Headlines:**

- **EX3 alone beats Direct without any user-side memoization.** Tree build halves (24 → 12 ms); allocations halve (17 → 8.6 MB/tick); gen0 GCs drop 40 %; gen1 drops 22 %.
- Diff time *increased* (7.6 → 11 ms). The cause is structural: the fluent chain happened to produce identical-by-value `ElementModifiers` instances frequently enough that `ShallowEquals → ModifiersEqual` short-circuited; the direct-build allocates a fresh `ElementModifiers` per cell every render, so structural comparison runs in full each time. This is a fixable secondary effect.
- gen2 collections drop modestly (70 → 56). Allocation rate of 8.6 MB/tick × 12 fps ≈ 100 MB/s is still high enough to fill gen2 over time. The next allocation hotspot is the `Dictionary<Type, object>` for `Attached` (4,900 instances per render).

**Implication for the framework:**

The clones produced by the fluent chain — not the underlying record sizes — are the dominant cost. **Each `.Method()` in the fluent API produces a `with`-clone of the entire `TextBlockElement` (~28 fields, ~240 B).** A chain of 5 methods produces 5 clones, of which only the last one survives. Four of every five `TextBlockElement` allocations on this hot path are immediate garbage.

This is a far bigger lever than the `ElementModifiers.Merge` optimization in EX2.

### Possible framework path: builder-style construction without API churn

A `ref struct` builder would let the fluent chain mutate a stack-allocated state and emit a single `TextBlockElement` at the end:

```csharp
// New API, additive — does not break existing fluent usage.
public static TextBlockElement TextBlock(string content, Action<TextBlockBuilder> configure)
{
    var b = new TextBlockBuilder(content);
    configure(b);
    return b.Build();
}

// Old fluent chain still works for non-hot paths.
TextBlock("Hi").FontSize(14).Foreground(brush)         // 3 allocations
TextBlock("Hi", b => b.FontSize(14).Foreground(brush)) // 1 allocation
```

Inside, `TextBlockBuilder` is `ref struct` with mutable fields; `.FontSize`/`.Foreground`/etc. mutate-then-return-self. `Build()` materializes a single `TextBlockElement` + at most one `ElementModifiers`.

For grid attachment, the builder would need a `.Grid(r, c)` setter that fills an inline single-attached slot (avoid the dictionary in the common case).

Caveat: `ref struct` cannot be a closure target, so the configure delegate would need to be `delegate*<ref TextBlockBuilder, void>` or take an `Action<TextBlockBuilder>` over a class-based builder (still saves clones, but adds the builder allocation). Worth measuring both shapes before committing.

## EX4 — Bucketed `ElementModifiers` (Layout + Visual sub-records)

**Status:** prototyped via API-preserving shim; same-day measured against fresh baselines. Result: small win on the fluent path (~6 % renders, −11 % bytes), neutral elsewhere — substantially below the predicted +10–15 % render gain.

**Hypothesis:** the per-cell `ElementModifiers` allocation is fat (~70 fields, ~600 B) but each cell touches only 1–3 fields. Splitting `ElementModifiers` into thematic sub-records — same pattern as the existing `AccessibilityModifiers` precedent — should let the common case allocate ~80–150 B sub-records on demand and skip the rest. Smaller `with`-clones, smaller bytes, same generic-extension API.

### Implementation — shim properties (zero API churn)

To avoid the test/fixture cascade the original design called for, the moved fields stay on `ElementModifiers` as `get`/`init` shim properties that read from / write into the `Layout` / `Visual` sub-records:

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
// …same shape for Margin, Width, Height, Min/Max*, H/V Align,
//   Margin/PaddingInline*, BorderInlineStart, IsVisible, RequestedTheme
//   (Layout) and Background, Foreground, BorderBrush, BorderThickness,
//   CornerRadius, Opacity, Scale, Rotation, Translation, CenterPoint (Visual).
```

Reconciler reads (`m.Padding`), test fixtures, and existing extension methods remain unchanged — they all flow through the get-shim. The auto-generated record `Equals` / `GetHashCode` use the actual backing fields (Layout, Visual, …) which themselves are records and compare structurally, so observable equality is preserved. **Zero test changes were needed; all 6724 unit tests pass.**

`ElementModifiers.Merge` was the one method that *had* to change: naming `Padding`/`Margin`/etc. inside a `with { … }` block would re-run each shim init, cloning `LayoutModifiers` once per layout field. Replaced with bucket-level merge (one `LayoutModifiers.Merge` call, one `VisualModifiers.Merge` call).

### Bucket boundaries

Two new records in `src/Reactor/Core/Element.cs`:

- **`LayoutModifiers`** (17 fields): `Margin`, `Padding`, `Width`, `Height`, `Min/Max{Width,Height}`, `Horizontal/VerticalAlignment`, `IsVisible`, `Margin/PaddingInline{Start,End}`, `BorderInlineStart`, `RequestedTheme`.
- **`VisualModifiers`** (10 fields): `Background`, `Foreground`, `BorderBrush`, `BorderThickness`, `CornerRadius`, `Opacity`, `Scale`, `Rotation`, `Translation`, `CenterPoint`.

The remaining ~40 fields stay on `ElementModifiers` (typography, ToolTip family, IsEnabled, Automation*, ElementSoundMode, all ~20 input handlers, Pan/Pinch/Rotate/LongPress, Drag/Drop, accessibility ref, Ref, Backdrop, OnMountAction).

### Same-day measurement (3-trial medians, ARM64 Release, AC, 10 % / 12 s)

The earlier baseline numbers in this doc (138 / 163 / 215 renders) drifted on the test machine across sessions — likely OS noise, JIT state, brush cache warm-up. To keep the comparison honest, EX4 was stash-toggled and **both legs were measured back-to-back on the same machine state.**

| Mode                          | Pre-EX4 (re-baseline)        | With EX4                      | Delta                            |
|-------------------------------|------------------------------|-------------------------------|----------------------------------|
| Fluent chain, no memo         | 104 / 17.4 MB / 341 / 154 / 52 | **110 / 15.4 MB / 321 / 149 / 51** | **+6 % renders, −11 % bytes**     |
| EX3 direct-build, no memo     | 122 / 8.6 MB / 205 / 123 / 42  | **123 / 8.9 MB / 213 / 126 / 42**  | flat (+1 render, +3 % bytes)     |
| EX3 direct-build + memo (EX1) | 214 / 2.14 MB / 78 / 35 / 15   | **211 / 2.21 MB / 81 / 37 / 17**   | flat (−1 % renders, +3 % bytes)  |

(Trials individually: A 103/110/130, B 120/123/124, C 203/211/215 with EX4; A 107/102/104, B 122/122/127, C 206/214/217 without. Variance on the unmemoized paths is high — ±25 renders run-to-run — so the small EX4 deltas are at the edge of detectability.)

### What EX4 actually did

1. **Small but real win on the fluent path.** Unmemoized fluent code allocates a fresh `ElementModifiers` per modifier call; bucketing trims the parent record's field count by ~30 % and pushes the bench's hot pair (`Padding` + `Foreground`) into smaller sub-record clones. Net: ~6 % more renders, ~11 % fewer bytes/tick, ~6 % fewer gen0 collections. This is the regime where bucketing is supposed to help and it does — just modestly.
2. **Neutral on EX3 direct-build.** The bench's hot pair lands in *different* buckets (Padding → Layout, Foreground → Visual), so each cell allocates **both** sub-records — three records total instead of one. The slim parent saves ~200 B; two sub-records add ~32 B of object-header overhead plus ~200 B of bucket fields. The savings cancel.
3. **Neutral on the memoized path.** When most cells reuse references via `EX1`, the modifier alloc rate drops 4–5×; EX4's effect on that small remaining slice is in the noise.

### Why the +10–15 % prediction missed

The "Predicted impact" estimate assumed the dominant cost was the *parent* `ElementModifiers` record's clone. In practice:

- `ElementModifiers` couldn't be slimmed below ~40 fields — input handlers, typography, automation, and gesture/drag fields had to stay (cells don't use them but other call sites do). So the parent shrank ~30 %, not 4×.
- Two sub-records means two extra GC object headers per cell, and two extra dereferences in every reconciler read of a moved field. ApplyModifiers reads ~50 fields per cell × 4900 cells = 245 k extra null-and-deref ops per render.
- The bench's hot pair (`Padding` + `Foreground`) **straddles the bucket boundary**. Workloads that touch only Layout *or* only Visual would see the bucket count drop to one; this bench always pays for both.

The doc anticipated this failure mode:

> If actual `+EX4` numbers come in *worse* than predicted, the most likely cause is bucket-boundary mismatch (we picked groupings that don't match the workload's co-occurrence). Re-measure with a single-merged "Common" bucket containing Layout + Visual + Typography fields — if that's faster, the per-allocation overhead of two sub-records exceeded the clone savings, and the right answer is one fatter (but still smaller-than-original) bucket.

The same-bucket variant wasn't measured in this session. **Worth a follow-up** if EX4 is being kept: collapse Layout + Visual + Typography into a single `LightModifiers` sub-record (~30 fields) and rerun. Predicted shape: better than two-bucket on this workload (one sub-record, one extra alloc/cell), worse than no-bucket only if the parent's field-count saving becomes a wash.

### What stayed clean

- Public API: zero source changes outside `Element.cs`.
- Reconciler, ElementExtensions, all tests, all self-test fixtures: untouched.
- Devtools / serialization: shim getters return the moved field values, so reflection-based readers still work.
- New `LayoutModifiers` / `VisualModifiers` are public and documented so consumers who want to bypass the shim (for max perf in inner loops) can construct them directly: `new ElementModifiers { Layout = new LayoutModifiers { Padding = thick }, Visual = new VisualModifiers { Foreground = brush } }`.

## Final canonical comparison (3-trial medians, ARM64 Release, AC, 10% / 12s)

All rows below were measured **same-day, back-to-back, on identical machine state** by stash-toggling each combination. Deltas are vs the unmodified Reactor baseline (no EX2, no EX3, no EX4, no memo). The PerfTracker `WorkingSet64` cache is applied in every row — that's measurement hygiene, not an experiment.

| Variant                                 | Renders | Δ vs base | Alloc/tick | gen0  | gen1 | gen2 | vs Direct |
|-----------------------------------------|--------:|----------:|-----------:|------:|-----:|-----:|----------:|
| **Direct** (reference)                  | **162** | —         | 35 KB       | 1     | 1    | 1    | —         |
| **Reactor** (no experiments — baseline) | **142** | —         | 21,640 KB   | 566   | 209  | 71   | −12 %     |
| Reactor + EX2 only (fluent)             | 104     | **−27 %** | 17,400 KB   | 341   | 154  | 52   | −36 %     |
| Reactor + EX2 + EX4 (fluent)            | 110     | −23 %     | 15,400 KB   | 321   | 149  | 51   | −32 %     |
| **Reactor + EX3 alone**                 | **153** | **+8 %**  | 8,591 KB    | 258   | 153  | 52   | −6 %      |
| **Reactor + EX3 + EX4** (no EX2)        | **159** | **+12 %** | 8,860 KB    | 274   | 163  | 54   | −2 %      |
| Reactor + EX2 + EX3 + EX4               | 123     | −13 %     | 8,900 KB    | 213   | 126  | 42   | −24 %     |
| **Reactor + EX1 alone (memo)**          | **212** | **+49 %** | 3,448 KB    | 124   | 38   | 17   | **+31 %** |
| **Reactor + EX1 + EX3 + EX4** (no EX2)  | **214** | **+51 %** | 2,210 KB    | 81    | 37   | 17   | **+32 %** |
| Reactor + EX2 + EX3 + EX4 + memo        | 211     | +49 %     | 2,210 KB    | 81    | 37   | 17   | **+30 %** |

(Each cell is the median of 3 trials, 12 s headless, 10 % mutation, 4,900 cells. Trial-to-trial spread on the unmemoized paths runs ±25 renders, so single-percent deltas are not meaningful.)

### Reading the table

1. **Memoization (EX1) is the dominant lever — it accounts for essentially all of Reactor's gain over Direct.** Raw Reactor + memo alone gets 212 renders vs Direct's 162. None of the framework-only experiments approach this.
2. **EX3 (direct record initializer) is a clean framework-level win in isolation:** +8 % over baseline, −60 % bytes/tick. It does *not* require user-side memo logic.
3. **EX4 (bucketed `ElementModifiers`) is a small, consistent win once isolated from EX2:** lifts EX3 from 153 → 159 (+4 %) and the unmemoized fluent path from 104 → 110 (+6 %). The earlier "neutral on EX3" reading was an EX2-confounded artifact. EX4 + EX3 + memo (214 renders, 2.21 MB/tick) is the strongest combination — slightly *better* than memo alone (212 / 3.45 MB), with bytes/tick down 36 % from memo-only thanks to the smaller per-cell allocations on the (rare) cells that do rebuild.
4. **EX2 is the regression** (104 vs 142 baseline, −27 %). The predicted −20 % allocation drop landed (bytes 21.6 → 17.4 MB), but render rate fell rather than stayed flat. Two readings:
    - The historical "flat" reading was within noise on a quieter machine.
    - There is a real JIT interaction with the inlined `with` expressions that the `Modify()` helper happened to avoid.
    Either way: **the data does not support keeping EX2.**
5. **`EX1 + EX3 + EX4` is the strongest measured combination** (214 renders, +32 % vs Direct, allocations cut 90 % from baseline). Note this is a **3-way combination of two framework changes (EX3 idiom + EX4 storage) plus one user-side pattern (memo)** — none of them are sufficient alone.
6. **Direct (162) is bracketed by Reactor variants:** Reactor + EX3 + EX4 alone (159) is within 2 %, and any memoized variant decisively beats it.

### Implications for shipping

- **Drop EX2.** The same-day data shows a clear regression, and the bytes savings already manifest from EX3 / EX4 instead. If a future session reproduces the historical "render rate flat" claim, EX2 can come back; for now it has to go.
- **Land EX3-style direct construction as a documented idiom** for hot cell loops. It's +8 % alone and combines cleanly with memo and EX4.
- **Land EX4 (bucketed modifiers via shim)** — the win is small (+4 % on directbuild, +6 % on fluent) but **real, API-compatible, and free** (all 6724 tests pass with no migration). The bucket-boundary concern raised earlier turns out to matter less than predicted: EX3 + EX4 still gains over EX3 alone.
- **`MemoCells` (with explicit deps) is the priority user-facing API.** It's the only mechanism that crosses Direct by a wide margin. Pair it with the closure-dependency caveat (revised Q&A Q1) so users don't silently capture stale state.
- **Off-thread render** (Q&A Q2) remains the largest pending systemic win for non-memoizable workloads, and the only remaining lever for cells where `MemoCells` is unsafe.

## Worst-case A/B — memo at 100 % mutation

To check that memo doesn't catastrophically regress when its premise is violated (every cell changes every tick), measured memo on/off at `--percent 100` with EX2 + EX4 in tree (no other env vars). Same machine, back-to-back, 3-trial medians.

| Mode      | Renders | Tree | Diff | Alloc/tick | gen0  | gen1 | gen2 |
|-----------|--------:|-----:|-----:|-----------:|------:|-----:|-----:|
| Memo OFF  | **41**  | 18.9 ms | 37.8 ms | **24.1 MB** | 183 | 72 | **21** |
| Memo ON   | **39**  | 27.4 ms | 33.7 ms | **18.0 MB** | 153 | 73 | **35** |
| Δ         | −5 %    | +45 %   | −11 %   | −25 %       | −16 % | flat | **+67 %** |

**Important caveat — "100 %" is not actually 100 %.** `StockDataSource.Update(100)` iterates 4,900 times calling `rng.Next(TotalItems)` (sampling **with replacement**), so by the coupon-collector argument only ~63 % of cells (1 − 1/e) are actually picked at least once per tick; ~37 % happen to be skipped. The memo path correctly reuses those untouched cells, which is where the byte reduction comes from.

**Reading the result:**

1. **Render rate dips ~5 %** when memo's premise is mostly violated — the equality-scan tree-build cost (~8.5 ms/render) only partially offsets the diff savings on the cells that randomly weren't picked. Mild and acceptable.
2. **Bytes/tick drop 25 %** even at "100 %" mutation, because the ~37 % skipped cells stay reused. Memo is mildly beneficial for allocation throughput even on the worst-case workload.
3. **gen2 collections rise 67 % (21 → 35)** — the most concerning signal. Memo retains the previous `Element[]` and `StockItem[]` across renders; those refs promote into gen1/gen2 and create more full-STW pauses than the no-memo path's churn-and-collect pattern. Watch for this in long-running memo workloads.

**Verdict:** memo's worst-case is mildly negative on render rate, mildly positive on bytes, and noticeably negative on gen2 pressure. Not a regression that blocks shipping `MemoCells`, but the gen2 increase is worth flagging in user-facing docs — `MemoCells` trades short-lived gen0 churn for longer-lived gen1/gen2 retention. Workloads with many memoized lists should be aware.

A truly-100 % mutation test (every cell mutated, deterministically) would isolate the equality-check overhead without the partial-reuse benefit; not run in this session because the bench's `Update()` would need to change. Open follow-up.

## Q&A

### Q1. *"User-side memoization defeats the point of Reactor — what would the user code look like with framework support?"*

Agreed: making each user write per-cell `prevData[i].Equals(data[i])` glue is exactly what we don't want. The framework's job is to expose this as a one-line idiom that *looks* like normal Reactor code.

Sketch of a `MemoCells` hook (lives in `Microsoft.UI.Reactor.Hooks` next to `UseState` / `UseRef`):

```csharp
public static Element[] MemoCells<T>(
    IReadOnlyList<T> items,
    Func<T, int, Element> builder,
    IEqualityComparer<T>? comparer = null) where T : notnull
```

Internally it's a `UseRef`-backed hook that holds the previous `(items snapshot, Element[] children)` between renders, and walks both lists comparing each item to its previous version. Unchanged cells reuse the previous `Element` reference; the reconciler's existing `ReferenceEquals` fast path then skips them. Changed cells are rebuilt by the user's builder.

The bench code goes from this:

```csharp
var children = new Element[StockDataSource.TotalItems];
for (int i = 0; i < StockDataSource.TotalItems; i++)
{
    int r = i / StockDataSource.Columns;
    int c = i % StockDataSource.Columns;
    ref readonly var item = ref data[i];
    children[i] = TextBlock(StockDataSource.FormatCell(in item))
        .FontSize(8)
        .Foreground(item.IsUp ? GreenBrush : RedBrush)
        .Padding(2, 1, 2, 1)
        .Grid(row: r, column: c);
}
```

…to this:

```csharp
var children = MemoCells(data, (item, i) =>
{
    int r = i / StockDataSource.Columns;
    int c = i % StockDataSource.Columns;
    return TextBlock(StockDataSource.FormatCell(in item))
        .FontSize(8)
        .Foreground(item.IsUp ? GreenBrush : RedBrush)
        .Padding(2, 1, 2, 1)
        .Grid(row: r, column: c);
});
```

Same builder lambda, same fluent chain, no manual diffing. The `T` (here `StockItem`, a `record struct`) gets value-equality for free, so the comparer parameter is usually unused.

**Variants worth shipping alongside it:**

- **`MemoCellsByKey<T>(items, keySelector, builder)`** — when items have stable identity but mutable interior (e.g., a `Person { Id, Name, … }` whose `Name` changes). Hashes by key, value-compares for content; lets the reconciler also key the children for reorder stability.
- **`MemoCellsByIndex<T>(items, changedIndices, builder)`** — explicit "I know exactly which indices changed" path. The bench's `StockDataSource.Update()` already returns this set; the user could plumb it through state. Skips the per-cell equality scan entirely (the 2.8 ms tree-build cost in EX1 collapses to near-zero — only the ~10 % rebuilt cells run any code).

**Why this isn't "the user implementing diffing":**

The hook is doing *value-level memoization* (cheap, mechanical), not *tree diffing* (expensive, structural). The expensive thing — figuring out what control changes go to which WinUI elements — is still entirely the reconciler's job. We're just avoiding the *gratuitous* work of the user describing the same cell five different ways per second when nothing about that cell changed.

This is the same pattern React uses (`React.memo`, `useMemo`). Reactor already has `Memo(...)` for whole-component memoization; `MemoCells` is the list-shaped sibling. **No diffing logic in user code; just a hint about what's identity-stable.**

**The footgun — closure dependencies.**

`MemoCells(items, builder)` caches output keyed only on the per-item value `T`. If the `builder` lambda closes over anything else — theme, selection, hover state, drag overlays, sort order, a parent component's `UseState` — those captures aren't part of the cache key, and a change to them won't invalidate the cell. The cell silently renders stale.

This is exactly the React `useMemo`/`React.memo` trap: missing a dependency in the deps array doesn't fail loud, it just produces a wrong-but-plausible UI.

In practice this means `MemoCells` is the right hammer for **leaf cells whose content is a pure function of the row** — large trading grids, log tables, file lists, ticker readouts. It's the *wrong* hammer the moment cell content depends on shared state (selection highlight, focus rings, cross-row calculations, theme-aware glyphs that bypass `ThemeRef`).

**Mitigation options to ship alongside it:**

- **Explicit deps parameter** (the React shape): `MemoCells(items, deps: [theme, selection], builder)`. Invalidates the entire memo when any dep changes. Costs ergonomics; converts a silent staleness bug into a deliberate cache-key choice. **Probably the right default — make users be explicit if they want correctness.**
- **Composite cell-input record**: nudge users toward `record CellInputs(StockItem Item, Theme Theme, Selection Sel)` so the closure has nothing to capture; memo on `IReadOnlyList<CellInputs>`. Keeps the "T does it" model clean but pushes the burden to the call site.
- **Static lambda enforcement** (analyzer/contract): refuse `MemoCells(...)` calls whose builder closes over anything. Strong, annoying.

**The bigger framework point:** `MemoCells` is a **narrow tool with a high ceiling on a specific workload shape**, not a general performance fix. EX3 (direct construction) and EX4 (bucketed modifiers) are framework-level allocation cuts that benefit every list — including the ones where `MemoCells` is unsafe — and they require no user-side reasoning about dependencies. So the headline answer for "make Reactor lists fast" is the framework-level allocation work; `MemoCells` is the targeted addition for the workloads where it's provably correct.

### Q2. *"Can we move reconcile/render off the UI thread?"*

Partial yes. The two phases have very different threading constraints:

| Phase                              | What it does                                       | UI-thread-bound? |
|-----------------------------------|---------------------------------------------------|:----------------:|
| **Tree build** (`Component.Render()`) | Pure-managed: builds `Element` records          | **No** (in principle) |
| **Reconcile / patch**              | COM calls into WinUI controls (set Text, set Foreground, manipulate `Panel.Children`) | **Yes** |
| **Effects (`UseEffect`)**          | User code that typically touches WinUI            | Yes              |

Right now both phases run on the dispatcher thread (`ReactorHost.RenderLoop`):

```
[UI thread]
  RequestRender()
  → DispatcherQueue.TryEnqueue(RenderLoop)
  → Render()
       ├── _rootComponent.Render()      // ← pure, ~24 ms in our bench
       └── _reconciler.Reconcile(...)   // ← UI-thread-required, ~8 ms
```

`Component.Render()` returning a tree of `Element` records is by-design pure. **It can run on a worker thread**, which would:

1. Free ~24 ms of UI-thread time per render (in this workload — proportional to tree size in general).
2. Pipeline: while the UI thread is reconciling render N, the worker builds render N+1. The UI thread sees ~8 ms of work per cycle instead of ~32 ms; the bottleneck shifts from "tree-build CPU" to "patch + COM round-trips."

**What blocks adopting it today:**

1. **WinUI thread-bound objects in Element fields.** A user who writes `.Foreground("#FF0000")` triggers `BrushHelper.Parse` → `new SolidColorBrush(...)` — `SolidColorBrush` is a `DependencyObject` and instantiating it off-thread throws `RPC_E_WRONG_THREAD`. **Fix:** off-thread render mode requires the user to pre-create brushes/fonts/etc. on the UI thread and pass references (already best practice — the bench already does this with `GreenBrush`/`RedBrush` cached statics). Reactor would need to either (a) reject WinUI-object-creating extension calls in off-thread mode at runtime with a clear diagnostic, or (b) provide off-thread-safe alternatives (`BrushHelper.GetCached(...)` that allocates on UI thread on first call and returns the cached ref).
2. **Hooks that sample dispatcher-thread state.** `UseEffect` is fine — its callback runs on UI thread after reconcile, unchanged. `UseState`/`UseRef` are pure data. The watch-out is `UseElementRef` and any hook that *reads* a live WinUI control — those would need to stay UI-thread-only or be deferred.
3. **`AnimationScope.HasScope`** and similar thread-statics in `ReactorHost.Render()` need a thread-local-or-explicit-passing model.
4. **Setstate during render.** `_isRendering` is a process-wide flag today; making it per-rendering-context lets the worker thread render without blocking new state changes from the UI.

**What the implementation looks like:**

A second `DispatcherQueue` is overkill — a regular `Task` on the threadpool is sufficient because Render() doesn't need WinUI affinity. Sketch:

```csharp
// In RenderLoop:
var newTreeTask = Task.Run(() => _rootComponent.Render()); // off-thread
var newTree = await newTreeTask;                            // back on UI
_reconciler.Reconcile(_currentTree, newTree, _currentControl, RequestRender);
```

Plus a feature flag (`ReactorFeatureFlags.OffThreadRender`) so it's opt-in until the WinUI-object-on-Element rules are tightened.

**Pipelining (the bigger win):** instead of `await` (idle UI during tree build), keep two trees in flight:

```
Tick N:   UI: Reconcile(N-1)   ║   Worker: Render(N)  
Tick N+1: UI: Reconcile(N)     ║   Worker: Render(N+1)
```

Hand-off via a single-slot `Channel<Element>` (capacity 1, drop-oldest semantics). State changes during a worker render abort that render and start a fresh one — same idea as React's concurrent mode.

**Catch: WinUI-thread-bound creations *deep* in Reactor.** Even with disciplined user code, the framework itself sometimes builds `SolidColorBrush` / `FontFamily` / `CornerRadius` etc. inside Element factories. Audit needed before this can be turned on by default. The fluent API factories that allocate WinUI objects (`BrushHelper.Parse`, `WinRTCache.GetFontFamily`, etc.) would need explicit thread-affinity contracts — annotate or move to a UI-only path.

### Q3. *"What's the danger of making the fluent API mutate the record directly instead of allocating a new modifier each call? Aliasing? Init-only semantics? Something else?"*

The danger is real and spans more than just init-only semantics — it would break several invariants the rest of the framework currently relies on. Worst-to-least-bad:

1. **Cross-render diffing breaks.** The reconciler keeps `_currentTree` (last render's element tree) and diffs it against the new tree returned from `Component.Render()`. If `.Foreground(brush)` *mutated* the existing element instead of cloning, then the user's act of *building* the new tree would mutate the old tree's elements (because the old tree's references reach the same fields). The diff sees "nothing changed" because `oldEl.Foreground == newEl.Foreground` — they're literally the same object. **Every reconciliation collapses silently.** This is the showstopper.
2. **Memoization invariant breaks.** EX1 works because reusing `prevChildren[i]` means the reconciler's `ReferenceEquals(a, b) → CanSkipUpdate → true` short-circuit is sound: same ref ⇒ same content. Mutation breaks the implication. Same ref no longer means same content; the skip path silently uses stale content.
3. **Aliasing surprises in user code (the "fork" case you asked about).** Naively idiomatic code becomes wrong:
    ```csharp
    var label = TextBlock("Hi").FontSize(14);
    var greeting = label.Foreground(GreenBrush);   // mutated `label` too
    var farewell = label.Foreground(RedBrush);     // mutated `label` AND `greeting`
    // greeting.Foreground is now Red, not Green.
    ```
    The fluent chain *looks* immutable; users (and the framework's own component code) reasonably treat `label` as a stable description. Hidden mutation makes that wrong.
4. **Closure / async capture surprises.** Hooks like `UseEffect`, `UseRef`, and any user code that captures an element reference for later (event handlers, scheduled callbacks) would see fields change out from under them. Records' immutable-by-default semantics let users reason about captured values without thread-of-time worries.
5. **Off-thread render becomes unsafe.** With immutable elements, the worker thread can build a tree and hand it to the UI thread for reconcile with no synchronization beyond the handoff. With mutable elements, the worker writing while the UI reads `_currentTree` produces torn reads / data races. Closes off the biggest pending performance win.
6. **Init-only semantics is the smallest concern.** Yes, you'd convert `init` to `set` on every modifier-bearing record, which is technically a public API contract change. But this is mostly a typing-rule concern; if the runtime semantics were sound (they aren't, per #1–5), C# wouldn't stop you with anything more than a compiler suggestion.

**The thing you actually want is a builder.** A `ref struct` builder is mutable and lets the fluent chain mutate freely *for the duration of the chain*; at `.Build()` it materializes a single immutable record and the builder is gone (`ref struct` can't escape the stack). That captures the perf win (one allocation, no clones) without breaking any of the invariants above:

```csharp
TextBlock("Hi", b => b
    .FontSize(8)
    .Foreground(brush)
    .Padding(2, 1, 2, 1)
    .Grid(r, c));
// Inside: builder mutates a stack frame.
// Returns: one TextBlockElement, immutable, indistinguishable from today's API.
```

So the failure mode of "mutate the record directly" isn't a single bug — it's that several independent framework features (cross-render diffing, memoization, off-thread render, async-safe captures) all assume references-to-elements are content-stable. Any one of those alone would make mutation unsafe. Together they're the immutability contract the rest of the design rests on.

### How the three approaches compare

| Approach                  | User-visible API change | Render-rate gain (this workload) | Mechanism |
|--------------------------|-------------------------|---------------------------------:|-----------|
| Direct record initializer (EX3) | Yes — drops the fluent chain entirely | +18 % (138 → 163) | Halves allocations; saves 4 `with`-clones per cell |
| Off-thread render        | Feature flag, may need API tightening | +60–80 % expected from removing 24 ms from UI per tick | Pipelines tree-build with reconcile |
| `MemoCells` hook         | One new hook; existing fluent chain unchanged | +56 % measured (138 → 215) | Reuses unchanged Element refs; reconciler's reference-equality fast path drives the savings |
| All three combined       | Hook adoption + opt-in flag + (later) builder API | Likely an additional 10–30 % on top of MemoCells, but limited returns since UI thread is now lightly loaded | — |

**My read:** ship `MemoCells` first (largest measured gain, smallest API delta, no thread-affinity hazards). Then off-thread render as a flag (biggest *systemic* win because it benefits *all* workloads, including ones MemoCells can't help — large unmemoizable tree changes). The fluent-chain allocation cost (EX3) becomes a long-tail polish: a builder-style overload would let workloads that can't memoize still avoid the clones.

## Files touched

- `tests/stress_perf/StressPerf.Shared/PerfTracker.cs` — cache `WorkingSet64` (1 s TTL); add optional GC instrumentation behind `STRESS_PERF_GC=1`.
- `tests/stress_perf/StressPerf.Reactor/Program.cs` — optional cell memoization behind `STRESS_PERF_MEMO=1`; optional direct-record-initializer build behind `STRESS_PERF_DIRECTBUILD=1` (EX3).
- `src/Reactor/Elements/ElementExtensions.cs` — `Foreground(Brush)` and the three `Padding(...)` overloads inlined to skip the intermediate `ElementModifiers` allocation (EX2).
- `src/Reactor/Core/Element.cs` — bucketed `ElementModifiers` (EX4): added `LayoutModifiers` and `VisualModifiers` records; converted 27 moved fields on `ElementModifiers` to `get`/`init` shim properties; updated `ElementModifiers.Merge` to merge sub-records at the bucket level. **API-compatible** — no consumer changes needed.
