# Spec 047 §14 Phase 3 (3.0.3) — TextBox descriptor proof, x64 advisory

**This is an advisory x64 capture, NOT authoritative.** The Phase 2 Q1
verdict was ratified on `LAPTOP-4MEP83VI` (ARM64, stable AC, dedicated
hardware). This capture was run on a Cloud PC (`CPC-ander-YTZ3O`, AMD
EPYC 7763, x64) and inherits Cloud PC noise characteristics — co-tenant
load, virtualized scheduling, no AC/foreground control. **Do not cite
these numbers in §13 or §14 spec text.** Use them as a directional read
on whether the Phase 3 prereq 3.0.2 (TextBox descriptor with the new
`HandCodedControlled` + `HandCodedEvent` builders) regresses the bench
matrix. A real ARM64 stable-AC re-capture on `LAPTOP-4MEP83VI` should
land before §14 Phase 3 is closed.

## Why this advisory capture exists

Phase 3 prereq 3.0.2 ships the first descriptor port that uses
`HandCodedControlled` / `HandCodedEvent` — the escape-hatch builders
needed for multi-event controls. TextBox is the proof point (2 events:
`TextChanged` round-tripping `Text`, plus fire-only `SelectionChanged`).
The `DescriptorVariantFactory` now registers TextBoxDescriptor alongside
the Q1 head-to-head trio (ToggleSwitch / Slider / Border).

Phase 2 §13 Q1 pre-committed to the bench matrix as the validation gate
for "does the descriptor model add bounded tax." With a new descriptor
shape entering the matrix, the gate must re-run before bulk Phase 3 port
work begins. The §9.2.1 thesis is that the hand-coded-shape descriptor
(i.e. `HandCodedControlled` with a user-supplied native trampoline)
matches the hand-coded handler within ±3% on M2 / M10.

## Capture environment

`CPC-ander-YTZ3O`, x64 (AMD EPYC 7763 64-Core Processor), Release, .NET
10.0.x, Windows 11 26200. **Cloud PC — not on AC/dedicated hardware**.
3 process launches × 5 reps = 15 measurements per (bench, variant) cell.
225 rows total across `launch-1.jsonl` + `launch-2.jsonl` +
`launch-3.jsonl`.

## Headline result

| Bench | vs ReactorV2 ns | vs ReactorToday ns | Phase 2 ARM64 (vs V2) | Band on this capture |
|---|---:|---:|---:|---|
| M1 Mount_Leaf_NoCallback | -0.9% | -2.6% | -1.0% | ≤5% |
| M2 Mount_Leaf_OneCallback | -2.2% | +2.9% | **+9.6%** | ≤5% |
| M5 Dispatch_Switch_Warm | +1.0% | +4.2% | -2.3% | ≤5% |
| M7 Update_NoChange | -1.4% | -0.5% | +8.1% | ≤5% |
| M10 EventHandlerState_Alloc | +1.1% | +9.5% | +19.3% | ≤5% |

**No bench exceeds ±5% vs ReactorV2 on this capture.** Adding the
`HandCodedControlled` + `HandCodedEvent` TextBox descriptor to the
registered set does not regress the matrix at this measurement
resolution.

The +9.6% M2 and +19.3% M10 numbers from the Phase 2 ARM64 stable-AC
capture do not reproduce on this x64 Cloud PC capture. Possible
explanations (none ratified):

1. **Architecture-dependent codegen.** Virtual `PropEntry.Mount`
   dispatch + delegate invocation cost may differ between RyuJIT-x64
   and RyuJIT-arm64 codegen paths. The Phase 2 README attributes the
   residual cost to "virtual `PropEntry.Mount` dispatch + getter/setter
   delegate invocations vs the hand-coded handler's inlined property
   writes" — that cost is JIT-implementation-sensitive.
2. **Cache hierarchy / memory bandwidth.** AMD EPYC 7763 has different
   L1/L2/L3 sizes and inclusivity vs Snapdragon X. The descriptor model
   touches more memory per mount (entry list + per-entry delegates) and
   may sit better in this cache hierarchy.
3. **Cloud PC virtualization noise.** Cloud PC runs in a virtualized
   environment with co-tenants competing for resources. The 95% CI
   half-widths in `summary.md` are wide (M2 vs V2 CI is ±15-16k ns on
   a ~190k ns mean ⇒ ±8%), so the -2.2% delta may be within noise.
4. **TextBox descriptor offsetting something.** Adding TextBoxDescriptor
   could change the registration table or method-dispatch table shape
   in a way that incidentally benefits the descriptor variant on these
   benches. (Unlikely — benches don't mount TextBox in M1/M2/M5/M7/M10
   — but worth flagging for the ARM64 re-capture.)

## §9.2.1 thesis check (TextBox HandCoded shape)

The §9.2.1 thesis: a hand-coded-shape descriptor (using
`HandCodedControlled` + native trampoline) matches the hand-coded
handler within ±3% on M2 / M10. On this advisory x64 capture:

| Bench | ReactorDescriptors vs ReactorV2 | Within ±3%? |
|---|---:|---|
| M2 | **-2.2%** | ✅ yes |
| M10 | **+1.1%** | ✅ yes |

**On this capture the thesis holds.** ARM64 re-capture should confirm.

## Q1 matrix application — for completeness

Per §13 Q1's pre-committed decision matrix, on the Q1 head-to-head
gating benches (M1 / M2 / M5 / M7):

| Band | Verdict | Triggered on this capture? |
|---|---|---|
| ≤5% on all M1/M2/M5/M7 | Ship descriptors as primary | **Yes** |
| 5-15% on any gating bench | Judgment call | No |
| >15% on any gating bench | Ship hand-coded as primary | No |

On this advisory capture the matrix lands in the "ship descriptors as
primary" band on all four gating benches. This is **more favorable**
than the Phase 2 ARM64 capture (which landed in the judgment-call band
on M2 / M7) — consistent with the noise / arch explanations above.

The Phase 2 ARM64 verdict (judgment-call band, recommendation =
descriptors as primary at Phase 3 scope) stands as the authoritative
verdict. This capture does not move it.

## Files

- `launch-1.jsonl` / `launch-2.jsonl` / `launch-3.jsonl` — raw bench
  output. 225 rows total.
- `aggregate.py` — copy of the Phase 2 aggregator. Run with no args
  from this directory.
- `summary.md` — aggregator output.

## Next step

A stable-AC ARM64 re-capture on `LAPTOP-4MEP83VI` mirroring the Phase 2
methodology should land before §14 Phase 3 is closed:

- Same 3×5 capture pattern (`--reps 5`, 3 process launches).
- Same benches (M1 / M2 / M5 / M7 / M10).
- Same variant set (ReactorToday / ReactorV2 / ReactorDescriptors).
- AC power, foreground window, other apps closed.
- Output to `docs/specs/047/phase3-results/LAPTOP-4MEP83VI/<date>-textbox-proof-3x5/`.

The ARM64 capture is what ratifies the §9.2.1 thesis for the multi-event
descriptor shape. This advisory capture only confirms the new code
compiles, runs, and doesn't blow up the matrix at coarse resolution.
