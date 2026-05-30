# Spec-047 Post-gate Re-capture — M12 only

**Machine:** `LAPTOP-4MEP83VI` (Qualcomm ARMv8, the spec-047 §4.9 baseline box)
**Arch/Runtime:** ARM64-native, Release, .NET 10.0.8 — identical to baseline
**Date:** 2026-05-30 (UTC) · **Branch:** `main`
**Head:** `2f4f0c50` — `perf(reconciler): gate V1 ReactorState tagging on HasCallbacks (spec 047 §4.4 follow-up) (#468)`
**Suite:** `PerfBench.ControlModel --test M12 --variant All --iterations 1000 --reps 5`

Targeted re-run to validate the M12 (`Pool_Rent_HotPath`) regression flagged by the
2026-05-29 Phase-4 capture in `../2026-05-29-arm64/RESULTS.md`. PR #468 lands the
`SetElementTagIfNeeded` gate — V1 `Mount`/`Update` skip the `ReactorState`
allocation + attached-DP write for callback-free leaves (the exact M12 shape).

> Scope caveat carries over from the parent capture: the §15.5 environment
> isolation isn't enforced from an automated run, so the timing (ns) axis is
> environment-contaminated and should be disregarded. Allocation bytes are
> deterministic and ARE the comparable axis.

## M12 — Per-render allocated bytes (mean across 5 reps)

| Variant | Baseline `2026-05-25` | Phase 4 `2026-05-29` (pre-gate) | **Post-gate (this capture)** | vs baseline | vs Phase 4 |
|---|---:|---:|---:|---:|---:|
| Direct | 773 | 772 | **752** | −2.7% | −2.6% |
| ReactorToday | 1,102 | 1,324 | **968** | **−12.2%** | **−26.9%** |
| Reactor (V1) | 1,102 | 1,289 | **968** | **−12.2%** | **−24.9%** |

### Framework overhead (Reactor − Direct)

| | Baseline | Phase 4 | Post-gate |
|---|---:|---:|---:|
| Reactor overhead / op | 329 B | 517 B (+57%) | **216 B (−34%)** |

The gate drops Reactor's per-op overhead by ~113 B vs baseline, almost exactly
matching the predicted savings (skipped `ReactorState` allocation + attached-DP
write for callback-free TextBlock).

## Verdict

✅ **M12 regression is fully closed — and then some.** Reactor (V1) is now
~12% *better* than the pre-V1 baseline on M12. The win comes from
`SetElementTagIfNeeded` skipping `ReactorState` allocation entirely for
callback-free leaves like `TextBlock("x")`, which is exactly what M12
exercises in a tight loop.

The Phase-4 doc's M12 cells (`+17.0%` and `+19.6%` in `../2026-05-29-arm64/RESULTS.md`)
are superseded by this capture.

## Out-of-scope

- The M1 cell in the Phase-4 doc (`+20.3%`) is **not** re-validated here. PR #468's
  commit message reports M1 dropping to `980 B/render` (`−8.5%` vs the 1,071 baseline),
  but that hasn't been independently re-captured on this box. M1, M2, M3, and the
  §11.6 byte-gate section in the Phase-4 doc should be re-measured before relying
  on them.
- Timing-axis (ns) re-capture under §15.5 isolation is still pending.

_Raw data: `perfbench-controlmodel-m12.jsonl`. Baseline:
`../../../baseline-results/LAPTOP-4MEP83VI/2026-05-25-arm64/`. Phase-4 pre-gate:
`../2026-05-29-arm64/`._
