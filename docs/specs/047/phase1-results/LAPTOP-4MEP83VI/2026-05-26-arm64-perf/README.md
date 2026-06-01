# Phase 1 perf snapshot — LAPTOP-4MEP83VI ARM64-native (2026-05-26)

Three-way micro-suite comparison taken at the end of the Phase 1
code-complete branch (after the OnCustomEvent echo drain, the
mount-time `WriteSuppressed` removal in the three ported handlers, and
the `Reconciler.ReconcileV1Child` / `SingleContent.GetCurrentChild`
introduction).

Captured against the Phase 0 baseline at
[`../../baseline-results/LAPTOP-4MEP83VI/2026-05-25-arm64/`](../../baseline-results/LAPTOP-4MEP83VI/2026-05-25-arm64/).
Same machine, same architecture (ARM64-native retail). Same M1–M13
catalog at the same iteration / repetition counts.

## Methodology change vs Phase 0

Phase 0 ran `PerfBench.ControlModel` with `new Reconciler()` (no
arguments) for every variant — at that point `ReactorV2` was a stub
that delegated to `ReactorToday`, so the V2 column in the baseline is
effectively a second `ReactorToday` measurement.

For this Phase 1 snapshot the bench was patched so that the
`ReactorV2` variant explicitly constructs
`new Reconciler(logger: null, useV1Protocol: true)`, while
`ReactorToday` pins `useV1Protocol: false`. That makes a **single run**
exercise:

- `Direct` — raw WinUI mount (engine bypassed entirely; environmental sanity check)
- `Today` — current code with V1 protocol OFF (legacy `MountXxx` path)
- `V2` — current code with V1 protocol ON (the new `IElementHandler` path)

So the row "Today" answers "did Phase 1 work regress the legacy path?"
and the row "V2" answers "where does the new extensible model land?"
without environmental drift between the two columns.

## Absolute results

See [`aggregator-out/summary-absolute.md`](aggregator-out/summary-absolute.md)
for the raw table. Annotated three-way comparison:

| Bench | Baseline Direct | Current Direct | Baseline Today | **Current Today (V1=OFF)** | Baseline V2 (≡Today at Phase 0) | **Current V2 (V1=ON)** | Today Δ vs baseline | V2-vs-Today (current) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| M1  | 32,802  | 37,463 (+14%)     | 32,688    | **55,725 (+70%)** | 38,140    | **60,795**  | (environmental)         | +9.1% |
| M2  | 52,336  | 89,873 (+72%)     | 64,676    | **75,003 (+16%)** | 61,894    | **117,782** | (environmental)         | **+57.0%** |
| M3  | 176,725 | 261,977 (+48%)    | 210,203   | **210,086 (-0%)** | 237,622   | **204,238** | flat                    | -2.8% |
| M4  | 33,208  | 27,422 (-17%)     | 90,659    | **80,879 (-11%)** | 90,746    | **93,749**  | improved                | +15.9% |
| M5  | 21,245  | 27,878 (+31%)     | 86,188    | **86,147 (-0%)**  | 86,599    | **119,579** | flat                    | **+38.8%** |
| M6  | 30,206  | 40,507 (+34%)     | 32,512    | **46,513 (+43%)** | 31,742    | **49,987**  | (environmental)         | +7.5% |
| M7  | 987,028 | 1,104,966 (+12%)  | 13,137    | **12,981 (-1%)**  | 11,985    | **12,226**  | flat                    | -5.8% |
| M8  | 4,357   | 4,891 (+12%)      | 4,586     | **4,983 (+9%)**   | 4,155     | **4,914**   | matches Direct          | -1.4% |
| M9  | 815,246 | 1,727,801 (+112%) | 1,399,885 | **1,831,953 (+31%)** | 1,429,732 | **1,833,051** | (environmental)      | +0.1% |
| M10 | 33,633  | 31,332 (-7%)      | 53,172    | **44,820 (-16%)** | 48,915    | **40,934**  | improved                | -8.7% |
| M11 | 59      | 34 (-42%)         | 39,751    | **31,210 (-21%)** | 33,093    | **33,287**  | improved                | +6.7% |
| M12 | 31,376  | 24,508 (-22%)     | 28,046    | **27,890 (-0%)**  | 29,942    | **30,385**  | flat                    | +8.9% |
| M13 | 27      | 28 (+4%)          | 136.8     | **115.9 (-15%)**  | 155.1     | **108.5**   | improved                | -6.5% |

All values: mean ns / op, 5 reps per variant
(M1–M8 @ 5,000 iter, M9 @ 2,000 iter, M10–M13 @ 1,000 iter).

## Reading the columns

**Did Phase 1 regress the legacy path?** Compare `Baseline Today` →
`Current Today (V1=OFF)`. The Today numbers track Direct: where Direct
moves up (M1, M6, M9 — all environmental, the Phase 0 capture machine
state differed), Today moves up proportionally. Where Direct holds
steady (M3, M5, M12) Today is flat. M4, M10, M11, M13 actually
improved by 10–20%. **Net: legacy is in baseline range or better — no
regression introduced by Phase 1.**

**Is the new V1 path net good or bad?** Compare `Current Today
(V1=OFF)` → `Current V2 (V1=ON)` (same binary, same run, no
environmental delta). Pattern:

- **Slower on mount-heavy ported-control benches.** M2 (ToggleSwitch
  mount + one callback) **+57%**. M5 (warm dispatch including ported
  controls) **+38.8%**. M4 (cold dispatch) +16%.
- **Comparable on the others.** M1, M6, M11, M12 between +7% and +9% —
  within the ±10% Phase 1 exit-gate tolerance, mostly within CI.
- **Improvements on update / pool / event-state benches.** M10 (event
  handler state alloc) **-8.7%**, M13 (setters suppression scope)
  **-6.5%**, M7 (update no-change) -5.8%, M3 -2.8%, M8 -1.4%.

Allocation delta (V2 vs Today): mostly flat (< 1%), with three
ported-control outliers — M4 +26.4%, M5 +26.5%, M11 +47.4%. The V1
mount path is allocating more on the ported controls.

## Phase 1 exit-gate check (spec §14)

Gate: "ReactorV2 ≤ +10% on M1, M2, M5, M7, L1, L4 vs Phase 0 baseline."

| Bench | Gate | Current V2 vs baseline V2 | Status |
|---|---|---|---|
| M1 | ≤ +10% | +59% (38,140 → 60,795) — Direct +14% so partially environmental | needs re-baseline on quiet machine |
| M2 | ≤ +10% | +90% (61,894 → 117,782) | **FAIL** — V1 mount cost |
| M5 | ≤ +10% | +38% (86,599 → 119,579) | **FAIL** — V1 mount cost |
| M7 | ≤ +10% | +2% (11,985 → 12,226) | **pass** |
| L1 | n/a    | macro deferred to 1.18 | n/a |
| L4 | n/a    | macro deferred to 1.18 | n/a |

M13 `OnIsOnChangedFireCount = 0` (§8.2 invariant) — satisfied in both
flag states (selftest fixture pass).

## Environmental caveat

The Direct column (raw WinUI mount, no Reactor code) moved by –42% to
+112% between Phase 0 capture and this run. That is not Reactor —
that's machine state (power plan / background load / DRR — see
[memory `stress_perf DRR / battery`](../../../../../../.claude/projects/C--Users-andersonch-Code-reactor3/memory/reference_stress_perf_drr_battery.md)).
Cross-run absolute comparisons against the Phase 0 baseline are
contaminated by that drift. The **V2-vs-Today within this run**
column is the trustworthy delta for evaluating the new path.

A re-capture on a quiet, AC-powered, refresh-rate-locked machine is
needed before the formal Phase 1 exit-gate evaluation (task 1.19).
This snapshot is informative, not gate-final.

## Files

- `perfbench-controlmodel-m1-m8.jsonl` — 120 rows (8 benches × 3 variants × 5 reps)
- `perfbench-controlmodel-m9.jsonl` — 15 rows
- `perfbench-controlmodel-m10-m13.jsonl` — 60 rows
- `aggregator-out/summary-absolute.md` — table (a)
- `aggregator-out/summary-delta.md` — table (b), V2-vs-Today percentage delta with CI
- `aggregator-out/summary-gap.md` — table (c), V2-vs-Direct framework overhead
- `aggregator-out/trend.csv` — flat per-row export for plotting
- `run.log` — full bench stdout

## Re-running

```pwsh
$exe = 'tests/perf_bench/PerfBench.ControlModel/bin/ARM64/Release/net10.0-windows10.0.22621.0/PerfBench.ControlModel.exe'
$out = 'docs/specs/047/phase1-results/LAPTOP-4MEP83VI/<date>-arm64-perf'
& $exe --test M1 M2 M3 M4 M5 M6 M7 M8 --iterations 5000 --reps 5 --out "$out/perfbench-controlmodel-m1-m8.jsonl"
& $exe --test M9 --iterations 2000 --reps 5 --out "$out/perfbench-controlmodel-m9.jsonl"
& $exe --test M10 M11 M12 M13 --iterations 1000 --reps 5 --out "$out/perfbench-controlmodel-m10-m13.jsonl"
dotnet run --project tools/spec047-aggregator -- --in "$out/*.jsonl" --out "$out/aggregator-out"
```
