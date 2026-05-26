# Phase 1 perf snapshot — typed-payload rewrite (2026-05-26 run #2)

Second perf capture on LAPTOP-4MEP83VI ARM64-native, taken after the
KD-3 V1 mount-path fix: `ToggleSwitchHandler`, `SliderHandler`, and
`TextBoxHandler` were converted from the generic
`ReactorBinding<T>.OnCustomEvent` escape hatch to direct wiring via
the typed per-control event payloads from spec §9.2
(`ToggleSwitchEventPayload`, `SliderEventPayload` (new),
`TextBoxEventPayload`). Trampolines are now allocated and subscribed
exactly **once per control lifetime**; pool rent / return preserves
the typed payload, so subsequent mounts hit the null-check fast path
with zero allocation.

Compare against the [previous Phase 1 run](../2026-05-26-arm64-perf/README.md)
in the sibling folder, which captured the path-as-shipped (handlers
on `OnCustomEvent`, paying 5–6 allocs/mount).

## What changed since the previous run

- `src/Reactor/Core/V1Protocol/ControlEventPayloads.cs` — added
  `SliderEventPayload` (Slider was missed in the original §9.2
  seven-event audit).
- `src/Reactor/Core/Reconciler.cs` — added
  `GetOrCreateControlEventPayload<T>` helper. Pool return no longer
  clears `ControlEventState` (typed payloads must survive pool reuse
  to avoid the re-allocation / double-subscribe problem).
- `src/Reactor/Core/V1Protocol/Handlers/{ToggleSwitch,Slider,TextBox}Handler.cs`
  — rewritten `Mount` bodies to wire the intrinsic event through the
  typed payload slot with a null-check before subscribe. Trampolines
  are static (or captured-once) delegates; subsequent rents skip the
  subscription entirely. Matches the legacy
  `EnsureToggleSwitchWiring` / `EnsureTextBoxWiring` shape.
- `MarqueeHandler` (external proof) intentionally still uses
  `OnCustomEvent` — it's the documented external surface, has known
  pool-reuse caveats (Phase 2/3 follow-up tracked as KD-4 in the
  task file).

## V2-vs-Today delta — before and after

Same machine, same bench harness, same iteration / repetition counts.
The "before" column is the
[previous run](../2026-05-26-arm64-perf/aggregator-out/summary-delta.md)
(OnCustomEvent path), "after" is this run.

| Bench | Description                          | V2 vs Today (before) | V2 vs Today (after) | Δ pp |
|---|---|---:|---:|---:|
| M1  | Mount leaf, no callback              | +9.1%               | **−0.2%**           | −9.3 |
| M2  | Mount ToggleSwitch + OnIsOnChanged   | **+57.0%**          | **−21.5%**          | **−78.5** |
| M3  | Mount Button + 3 callbacks           | −2.8%               | +52.8% (noisy, ±26.4%) | — |
| M4  | Dispatch switch, cold                | +15.9%              | **−4.1%**           | −20.0 |
| M5  | Dispatch switch, warm                | **+38.8%**          | **−4.2%**           | **−43.0** |
| M6  | Dispatch external type               | +7.5%               | −6.1%               | −13.6 |
| M7  | Update no change                     | −5.8%               | −6.5%               | — |
| M8  | Update one leaf changed              | −1.4%               | −3.7%               | — |
| M9  | Update all changed                   | +0.1%               | +5.0%               | — |
| M10 | EventHandlerState alloc              | −8.7%               | **−12.3%**          | −3.6 |
| M11 | ModifierEHSFrequency                 | +6.7%               | +81.6% (very noisy, ±61.5%) | — |
| M12 | Pool rent hot path                   | +8.9%               | **−5.5%**           | −14.4 |
| M13 | Setters suppression scope            | −6.5%               | −2.0%               | — |

The bench machine state changed between the two runs (background
load, thermal); absolute Today numbers are 2–3× slower this run
across the board, which contaminates cross-run absolute comparisons.
Within-run V2-vs-Today is unaffected — that's what the table above
reports.

## Phase 1 exit-gate metrics (spec §14 item 1)

Gate: **V2 ≤ +10% on M1, M2, M5, M7, L1, L4 vs baseline.**

| Bench | Target | Result (V2-vs-Today current run) | Status |
|---|---|---|---|
| M1 | ≤ +10% | −0.2% | **pass** |
| M2 | ≤ +10% | **−21.5%** (V1 path now faster than legacy) | **pass** |
| M5 | ≤ +10% | −4.2% | **pass** |
| M7 | ≤ +10% | −6.5% | **pass** |
| L1 | n/a    | macro deferred to 1.18 | deferred |
| L4 | n/a    | macro deferred to 1.18 | deferred |

M13 invariant (`OnIsOnChangedFireCount = 0`) — satisfied (selftest
both flags pass 932/932; see
`../../../../../selftest-v1on-typed.txt`).

## Outliers worth a note

- **M3 +52.8% (±26.4% CI).** Mount_Leaf_ThreeCallbacks routes through
  `Button` + click/pointer/tapped — Button is **not** V1-ported, so
  the V1 dispatch path adds one dictionary miss (`_v1Handlers.TryGet`)
  per mount and otherwise falls through to legacy `MountButton`.
  The +52.8% mean has a ±26.4% CI half-width and is contaminated by
  the larger Today/Direct drift on this run (Today M3 went from
  210,086 to 666,413 ns between the two captures). Likely
  environmental — needs a quiet-machine re-run to confirm.
- **M11 +81.6% (±61.5% CI).** Same pattern — large CI, noisy bench.
  M11 measures ModifierEHSFrequency which doesn't touch V1-ported
  controls directly. Not a Phase 1 exit-gate metric.

These two are flagged but neither is in the gate set.

## Files

- `perfbench-controlmodel-m1-m8.jsonl` (120 rows)
- `perfbench-controlmodel-m9.jsonl` (15 rows)
- `perfbench-controlmodel-m10-m13.jsonl` (60 rows)
- `aggregator-out/summary-absolute.md` — table (a)
- `aggregator-out/summary-delta.md` — table (b), V2-vs-Today % with CI
- `aggregator-out/summary-gap.md` — table (c), V2-vs-Direct overhead
- `aggregator-out/trend.csv` — flat export
- `run.log` — bench stdout

Sibling: [`../2026-05-26-arm64-perf/`](../2026-05-26-arm64-perf/) —
prior run, pre-typed-payload fix.
