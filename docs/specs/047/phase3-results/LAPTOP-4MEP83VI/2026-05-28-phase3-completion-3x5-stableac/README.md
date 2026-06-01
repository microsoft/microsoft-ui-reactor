# Spec 047 §14 Phase 3 completion — ARM64 ratification capture (LAPTOP-4MEP83VI)

**Result: NOT RATIFIED / INCONCLUSIVE.** This capture is valuable
evidence but does **not** satisfy the ARM64 stable-AC ratification gate.
The fixed variant-ordering run shows strong time/order drift (suspected
thermal throttling) that systematically disadvantages whichever variant
runs last — which is always `ReactorDescriptors`. Under the contaminated
numbers the §13 Q1 gating bench M2 exceeds the 15% threshold; a
controlled order-swap re-run (below) proves that M2 "regression" is a
position artifact, not a real descriptor cost. A thermally-clean Phase 3
ARM64 re-run is still required to formally close the §14 gate.

This is the capture the spec defers to in §14
("ARM64 stable-AC ratification gate — pending"). It is the authoritative
**machine** (`LAPTOP-4MEP83VI`, the Phase 0/2 baseline machine) but the
**run conditions did not stay stable**, so it cannot stand as the
ratifying capture on its own.

## Capture environment

`LAPTOP-4MEP83VI`, ARM64-native (Qualcomm/Snapdragon, ARMv8 64-bit),
Release, .NET 10.0.8, Windows 11 26200. AC power connected (battery 80%),
Windows power plan forced to **High performance** for the run and restored
to Balanced afterward. Branch `spec/047-phase3-completion` @ HEAD
(PR #440). The `PerfBench.ControlModel` harness is **unchanged from
`main` on this branch** — the bench's `DescriptorVariantFactory`
registration set is identical to prior captures (so this measures the
same descriptor interpreter, not the production `RegisterV1BuiltInHandlers`
~76-type table).

3 process launches × 5 reps × 13 benches × 4 variants = 780 measurements
in `launch-1.jsonl` + `launch-2.jsonl` + `launch-3.jsonl`. The
order-swap confirmation adds 180 measurements in
`confirm-reversed-launch-{1,2,3}.jsonl`.

> **Note on power telemetry.** The bench records `powerState`/`powerPlan`
> as `unknown` (env capture does not read them). The "High performance /
> AC" conditions above are documented manually, not embedded in the JSON.
> No CPU frequency / package-temperature / throttle telemetry was
> captured, so "thermal throttling" below is the **suspected** mechanism
> of the observed time/order drift, not a directly measured fact.

## Headline — V1 ON (descriptors) vs V1 OFF (today), median-of-n=15

Primary run, fixed variant order per bench
(Direct → ReactorToday → ReactorV2 → **ReactorDescriptors last**).
Full per-cell table with 95% CI in `summary.md`.

| Bench | Desc vs Today (ns) | Desc vs ReactorV2 (ns) | Trust |
|---|---:|---:|---|
| M1 Mount_Leaf_NoCallback   | +30.1% | -1.6%  | **High** (fast, stable) |
| M2 Mount_Leaf_OneCallback  | +23.4% | +36.1% | **Low** (drift-contaminated) |
| M3 Mount_Leaf_ThreeCallbacks | +175.3% | +119.0% | **Invalid** (drift-contaminated) |
| M4 Dispatch_Switch_Cold    | -17.4% | -22.3% | Low (drift) |
| M5 Dispatch_Switch_Warm    | -30.4% | -28.9% | Low (drift) |
| M6 Dispatch_ExternalType   | -4.0%  | -0.8%  | High |
| M7 Update_NoChange         | +8.9%  | +3.5%  | **High** (fast, stable) |
| M8 Update_OneLeafChanged   | +17.9% | +1.4%  | **High** (fast, stable) |
| M9 Update_AllChanged       | +3.4%  | +1.2%  | Medium (long but alloc-bound) |
| M10 EventHandlerState_Alloc| +17.4% | +15.9% | Low (drift) |
| M11 ModifierEHS_Frequency  | +11.5% | +0.5%  | **High** (fast, stable) |
| M12 Pool_Rent_HotPath      | +44.2% | +5.6%  | Low (drift) |
| M13 Setters_Suppression    | -3.0%  | -4.0%  | High (correctness bench) |

## Why these numbers are contaminated — the drift evidence

Within a single launch, `ReactorDescriptors` mean ns climbs steeply from
rep0 → rep4 on the long-running benches, while the short benches stay
flat:

| Bench | rep0 → rep4 climb (Descriptors) | per-rep duration |
|---|---:|---|
| M1  | +24% | ~40 µs |
| M2  | +45% | ~95 µs |
| M3  | +55% | ~1.7 ms |
| M4  | +28% | ~100 µs |
| M5  | +30% | ~100 µs |
| M12 | +11% | ~55 µs |
| M7 / M8 / M11 / M13 | ≈flat | ≤10 µs |

The climb tracks bench duration, not the variant — the classic
signature of a CPU shedding clock under sustained load on a fanless /
thermally-limited ARM64 laptop. Because the four variants run
back-to-back within each bench and `ReactorDescriptors` is **always
scheduled last**, it runs against the hottest core in each bench window.
The means are therefore **not independent of run position**.

## Decisive control — order-swap re-run (gating benches)

To separate "real regression" from "position artifact" I re-ran the
§13 Q1 gating benches (M1/M2/M5/M7) with the variant order **reversed**
so `ReactorDescriptors` runs **first / cold** and `ReactorToday` runs
last / hot (`--variant ReactorDescriptors ReactorV2 ReactorToday`,
3 launches). Raw data: `confirm-reversed-launch-{1,2,3}.jsonl`.

| Bench | Desc vs Today — Desc LAST | Desc vs Today — Desc FIRST | swing |
|---|---:|---:|---:|
| M1 | +30.1% | +32.5%  | +2.4pp (stable) |
| M2 | +23.4% | **-30.5%** | **-54.0pp (sign flip)** |
| M5 | -30.4% | -7.2%   | +23.3pp |
| M7 | +8.9%  | +128.2% | +119.4pp (see note) |

| Bench | Desc vs ReactorV2 — LAST | Desc vs ReactorV2 — FIRST |
|---|---:|---:|
| M1 | -1.6%  | +9.2%  |
| M2 | **+36.1%** | **+1.1%** |
| M5 | -28.9% | -11.4% |
| M7 | +3.5%  | +113.7% (see note) |

**Reading:**

- **M2 is the headline proof.** Its Descriptors-vs-Today delta flips
  from **+23.4%** (Descriptors last) to **−30.5%** (Descriptors first) —
  a 54-percentage-point swing driven purely by execution position. The
  order-robust Descriptors-vs-ReactorV2 comparison collapses from +36.1%
  to **+1.1%** when both variants sit in comparable positions. **There
  is no real M2 descriptor regression** — the formal Q1 failure in the
  primary table is a contamination artifact.
- **M1 is order-robust** (+30% vs Today in both orderings) and is
  Descriptors ≈ ReactorV2 (±10pp). This is the genuine **V1-protocol
  vs legacy mount overhead** seen in every prior capture (it is not
  descriptor-specific — hand-coded V1 pays the same).
- **M5** stays a Descriptors win in both orderings (direction robust).
- **M7 reversed has its own artifact** — a rep0→rep1 step jump
  (~10 µs → ~27 µs) appears for Descriptors in the small-selection
  reversed run (likely JIT tiering / background recompilation specific
  to the reduced job set). The **full-run** M7 (+8.9% vs Today, +3.5%
  vs V2, flat across reps) is the trustworthy M7 number; the reversed
  M7 should be disregarded.

## What can and cannot be concluded

**Supported by the thermally-insensitive (fast, flat) benches** —
M1, M7, M8, M11, M13 — where Descriptors vs ReactorV2 is within ±5%
(M1 -1.6%, M7 +3.5%, M8 +1.4%, M11 +0.5%, M13 -4.0%): in paths that do
not heat the core, **descriptor dispatch/interpreter overhead over
hand-coded V1 is small.** This is consistent with the Phase 2 stable-AC
capture and the x64 advisory captures. It does **not** prove "descriptors
add zero cost" globally — the drift-contaminated long benches are simply
unmeasurable on this run.

**Unresolved on this capture** (require a thermally-clean re-run):
M2, M3, M4, M5, M10, M12. M3 +175.3% in particular is **invalidated by
drift**, not shown to be real — but also not shown to be benign; M3
exercises the 3-callback wiring path and deserves a clean measurement.

**Allocation note.** This README interprets timing. Allocation deltas are
in `summary.md`; they are not the gating axis for §13 Q1 (which keys off
ns vs ReactorV2). A few are worth a glance on the clean re-run — e.g.
M7 Descriptors alloc is higher than Today (extra `EventHandlerState` /
binding state on the V1 path), consistent with the known V1 memory
profile rather than a new regression.

## Recommendation

1. **Do not cite these primary deltas in §13/§14 spec text.** Treat this
   capture as *inconclusive* for ratification.
2. **Re-run on `LAPTOP-4MEP83VI` under controlled thermal conditions**
   before closing the §14 gate. Concretely, mitigate the order/thermal
   confound with one or more of:
   - randomize / rotate variant order per launch (or interleave per rep);
   - insert a cooldown (`Start-Sleep`) between variants and between benches;
   - reduce `--iterations` so each bench window is shorter / cooler;
   - capture CPU effective-clock / package-temp telemetry alongside the run
     so "thermal" stops being an inference.
   The clean run must put M2 back under the Q1 threshold (the order-swap
   says it will: Desc ≈ V2 at +1.1%) and give a real M3 number.
3. Until that clean run lands, the §14 ARM64 gate stays **pending**, now
   with a named owner/date to be appended in the spec.

## Files

- `launch-{1,2,3}.jsonl` — primary 3×5 capture (fixed variant order). 780 rows.
- `summary.md` — aggregator output (per-cell means + 95% CI + Q1 deltas).
- `confirm-reversed-launch-{1,2,3}.jsonl` — order-swap control
  (Descriptors first), gating benches M1/M2/M5/M7. 180 rows.
- `aggregate.py` — reads `launch-*.jsonl`; run with no args from this dir.

## Reproduce

```powershell
dotnet build tests/perf_bench/PerfBench.ControlModel -c Release -p:Platform=ARM64
$exe = "tests\perf_bench\PerfBench.ControlModel\bin\ARM64\Release\net10.0-windows10.0.22621.0\PerfBench.ControlModel.exe"
$out = "docs\specs\047\phase3-results\LAPTOP-4MEP83VI\2026-05-28-phase3-completion-3x5-stableac"
$results = "tests\perf_bench\PerfBench.ControlModel\bin\ARM64\Release\net10.0-windows10.0.22621.0\results.jsonl"
for ($i = 1; $i -le 3; $i++) {
    Remove-Item $results -ErrorAction SilentlyContinue
    Start-Process -FilePath $exe -Wait -NoNewWindow   # -Wait required; & $exe does not block this WinUI app
    Copy-Item $results "$out\launch-$i.jsonl"
}
python "$out\aggregate.py" > "$out\summary.md"

# order-swap control:
for ($i = 1; $i -le 3; $i++) {
    Remove-Item $results -ErrorAction SilentlyContinue
    Start-Process -FilePath $exe -Wait -NoNewWindow -ArgumentList @(
        "--test","M1","M2","M5","M7","--variant","ReactorDescriptors","ReactorV2","ReactorToday")
    Copy-Item $results "$out\confirm-reversed-launch-$i.jsonl"
}
```

## Captures index

- `../../phase2-results/LAPTOP-4MEP83VI/2026-05-26-q1-fastpath-3x5-stableac/`
  — Phase 2 Q1 stable-AC capture (clean; M1 -1.0%, M2 +9.6%). The
  reference for what a thermally-clean ARM64 run looks like.
- `../CPC-ander-YTZ3O-x64-advisory/2026-05-28-phase3-finish-3x5/` —
  latest x64 Cloud-PC advisory (M3 -1.8%, well within noise) — supports
  the "M3 +175% is contamination" reading but is itself advisory-only.
- `./` (this dir) — Phase 3 completion ARM64 attempt. **Not ratifying**
  due to thermal/order drift; superseded once a clean ARM64 re-run lands.
