# Spec 047 §14 Phase 3 batches 1+2 — value-bearing descriptors, x64 advisory

**This is an advisory x64 capture, NOT authoritative.** Cloud PC
(`CPC-ander-YTZ3O`, AMD EPYC 7763, x64), not on AC/dedicated hardware.
Do not cite these numbers in §13 or §14 spec text. A stable-AC ARM64
re-capture on `LAPTOP-4MEP83VI` should ratify the matrix before §14
Phase 3 is closed.

## Why this capture exists

This run extends the Phase 3 prereq (`2026-05-27-textbox-proof-3x5/`)
matrix with the next two descriptor batches:

- **Batch 1** — `CheckBoxDescriptor`, `RadioButtonDescriptor`,
  `RatingControlDescriptor`, `ToggleSplitButtonDescriptor`. All
  `.Controlled` single-event; CheckBox/RadioButton wire both `Checked`
  and `Unchecked` in one subscribe lambda.
- **Batch 2** — `ColorPickerDescriptor`,
  `CalendarDatePickerDescriptor`, `DatePickerDescriptor`,
  `TimePickerDescriptor`. All `.Controlled` single-event against
  `TypedEventHandler` / `EventHandler<TArgs>` shapes.

After both batches the `DescriptorVariantFactory` registers 12 ported
controls (4 from Q1/prereq: ToggleSwitch/Slider/Border/TextBox; 8 new
from batches 1+2). The bench matrix should detect any registration-table
or dispatch-table shape change that incidentally regresses M1-M10 even
though those benches don't mount any of the new controls directly.

## Capture environment

`CPC-ander-YTZ3O`, x64 (AMD EPYC 7763 64-Core Processor), Release, .NET
10.0.x, Windows 11 26200. **Cloud PC — not on AC/dedicated hardware**.
3 process launches × 5 reps × 5 benches × 3 variants = 225 measurements
total across `launch-1.jsonl` + `launch-2.jsonl` + `launch-3.jsonl`.

## Headline — V1 ON vs V1 OFF

The user-facing question for this PR is "does turning V1 ON regress
anything." Reading the aggregator output through that lens:

| Bench | V1 ON (descriptors) vs V1 OFF (Today) | V1 ON (handcoded) vs V1 OFF (Today) | Verdict |
|---|---:|---:|---|
| M1 Mount_Leaf_NoCallback | **-2.1%** | -1.7% | within noise |
| M2 Mount_Leaf_OneCallback | **+1.5%** | +1.5% | within noise |
| M5 Dispatch_Switch_Warm | **+0.3%** | +1.3% | within noise |
| M7 Update_NoChange | **+0.1%** | +0.9% | within noise |
| M10 EventHandlerState_Alloc | **-0.2%** | +4.5% | within noise |

**No gating bench regresses more than +1.5% when V1 ON (descriptors) is
compared to V1 OFF on this advisory capture.** Adding batches 1+2 to the
descriptor registration set did not destabilise the matrix.

## Q1 decision matrix — for completeness

Per §13 Q1's pre-committed decision matrix applied to
ReactorDescriptors vs ReactorV2:

| Bench | vs ReactorV2 ns | vs ReactorV2 alloc | Q1 band |
|---|---:|---:|---|
| M1 | -0.5% | -0.0% | ≤5%: ship descriptors |
| M2 | -0.0% | +2.6% | ≤5%: ship descriptors |
| M5 | -0.9% | -2.0% | ≤5%: ship descriptors |
| M7 | -0.8% | +0.0% | ≤5%: ship descriptors |
| M10 | -4.5% | +4.4% | ≤5%: ship descriptors |

**All five gating benches land in the "ship descriptors as primary"
band on this capture.** Consistent with `2026-05-27-textbox-proof-3x5/`
(the immediately prior capture on the same machine).

The Phase 2 ARM64 stable-AC verdict (judgment-call band, recommendation
= descriptors as primary at Phase 3 scope) remains the authoritative
verdict. This capture confirms the new code compiles, runs, and doesn't
blow up the matrix at coarse resolution after batches 1+2.

## §15.6 regression budgets — vs ReactorV2 baseline

| Budget | Worst gating bench on this capture | Pass? |
|---|---|---|
| ≤5% vs ReactorV2 on M1/M2/M5/M7/M10 → primary | M10 at -4.5% (better) | ✅ |
| 5-15% vs ReactorV2 → judgment call | none | n/a |
| >15% vs ReactorV2 → stop and investigate | none | n/a |

**No bench exceeds the 5% budget.** Batches 1+2 ship without §15.6
escalation.

## Comparison to the prior 3×5 advisory capture

Movement vs `2026-05-27-textbox-proof-3x5/` (same machine, same day,
same 5 gating benches, prior capture had only the Q1 + prereq
descriptors registered — these new batches added 8 more entries):

| Bench | Prior delta vs V2 ns | This capture delta vs V2 ns | Movement |
|---|---:|---:|---|
| M1 | -0.9% | -0.5% | within noise |
| M2 | -2.2% | -0.0% | within noise |
| M5 | +1.0% | -0.9% | within noise |
| M7 | -1.4% | -0.8% | within noise |
| M10 | +1.1% | -4.5% | -5.6 ppt shift (likely Cloud PC noise) |

The M10 shift (-5.6 ppt) is the largest movement and lands inside the
±8% half-width noise envelope documented in the prior capture's README.
No directional regression attributable to the new descriptor entries.

## Files

- `launch-1.jsonl` / `launch-2.jsonl` / `launch-3.jsonl` — raw bench
  output. 225 rows total.
- `aggregate.py` — copy of the Phase 2 / prereq aggregator.
- `summary.md` — aggregator output (mirrors the tables above).

## Next step

A stable-AC ARM64 re-capture on `LAPTOP-4MEP83VI` mirroring the Phase 2
methodology should land before §14 Phase 3 is closed — same 3×5 pattern,
same benches, same variant set, AC power + foreground + other apps
closed. Output to `docs/specs/047/phase3-results/LAPTOP-4MEP83VI/<date>-batch-1-2-3x5/`.
