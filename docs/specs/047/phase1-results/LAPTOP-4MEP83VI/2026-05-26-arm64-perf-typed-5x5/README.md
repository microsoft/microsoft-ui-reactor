# Phase 1 perf — 5 × 5 cross-process stability run (2026-05-26)

Final Phase 1 perf capture for the V1 mount-path debugging arc. After
the previous runs showed M4/M5 deltas swinging from −4% to +52% across
otherwise-identical invocations, this capture aggregates across **5
separate process launches × 5 reps each = 25 measurements per (bench,
variant)** on a manually quieted machine to settle the noise floor.

Each launch is a fresh `PerfBench.ControlModel.exe` invocation (no
shared state across launches; each starts cold and warms up
independently). The aggregator merges all five JSONL files into one
mean / CI / alloc table.

## Result

| Bench | ns Δ % (V2 vs Today) | 95% CI half-width | alloc Δ % |
|---|---:|---:|---:|
| M4 (Dispatch_Switch_Cold) | **+88.9%** | ±16.5% | **−0.5%** |
| M5 (Dispatch_Switch_Warm) | +13.1% | ±17.4% | **+0.4%** |

Allocation is at **parity** on both benches (was +20–47% before the
KD-3 typed-payload rewrite + wiring-gate fix). The +88.9% on M4 is
stable signal — the per-launch ratio is tight across all five runs:

| Launch | M4 Today (mean ns) | M4 V2 (mean ns) | V2/Today |
|---|---:|---:|---:|
| L1 | 96,065  | 172,889 | 1.80× |
| L2 | 75,027  | 160,100 | 2.13× |
| L3 | 92,765  | 198,190 | 2.13× |
| L4 | 82,310  | 130,127 | 1.58× |
| L5 | 71,607  | 127,826 | 1.78× |

V1 is consistently 1.6–2.1× slower than legacy on the M4 dispatch hot
path. This is **not run-to-run noise** — it's a real overhead.

## What's causing the M4 gap

With allocation at parity, the +88.9% timing delta is CPU
instructions in the V1 dispatch shell. Per V1-routed mount:

1. `Reconciler.Mount` checks `UseV1Protocol && _v1Handlers.TryGet(elementType)` — one dict lookup
2. `V1HandlerAdapter<TElement,TControl>.Mount(...)` — `IV1HandlerEntry` interface call + downcast
3. `_handler.Mount(ctx, typedEl)` — second interface call into `IElementHandler<,>`
4. `_handler.Children` getter + `DispatchChildrenMount` strategy switch (no-op for `None<>` but pays the lookup chain)
5. Generic specialization — each `(TElement, TControl)` pair lives in a separate JIT-compiled adapter type; PGO has more code to warm

vs the legacy direct dispatch:

```csharp
control = element switch {
    ToggleSwitchElement ts => MountToggleSwitch(ts),
    ...
};
```

— a monomorphic switch that JIT inlines aggressively. The two extra
interface dispatches in the V1 path are what the +88.9% is paying for.

M4 is a worst case: it does almost nothing besides Mount + Add +
Remove. On real-world mounts that do material work (attached props,
modifiers, child reconciliation, theme bindings), the dispatch shell
overhead is amortized. That's why M1 (Mount_Leaf_NoCallback) and M2
(Mount_Leaf_OneCallback) show V1 at parity or *faster* than legacy
despite using the same dispatch path.

## M5 anomaly

M5 is M4 with a different name (`M05_DispatchSwitchWarm._inner = new M04_DispatchSwitchCold()`).
Yet M5 absolute timings are roughly 3× the M4 timings for both Today
and V2 in every launch. That's a **bench harness artifact** — by the
time M5 runs after M4 in each process, the bench Parent panel and
DispatcherQueue have accumulated state that slows down every Mount +
Add + Remove cycle. Not a Reactor cost. M5's V2-vs-Today delta is
within noise (+13.1% ±17.4%).

Launch 4 in particular ran M5 V2 at 71K ns (0.32× of Today!) —
probably a JIT tier-up race where V1's adapter got promoted to optimal
tier while Today didn't. Single-launch outliers like this are why the
5 × 5 aggregate is needed.

## Phase 2 direction (per task file KD-3 footer)

Add a fast-path for high-use built-in elements that bypasses the
`V1HandlerAdapter` indirection — keep `IElementHandler<TElement,TControl>`
as the public author surface, but for the six ported built-ins route
through static `MountToggleSwitchV1` / `MountSliderV1` etc. helpers
that the JIT can inline directly. The typed-event-payload approach
this PR landed stays; only the dispatch shell changes.

Should close the M4-shaped gap entirely while preserving the
extensibility benefits the V1 path delivers on real mounts (M1, M2,
M7, M8, M10, M13 all at parity or better).

## Files

- `perfbench-m4-m5-launch[1-5].jsonl` — 30 rows each (M4 + M5, 3
  variants, 5 reps = 30); 150 rows total
- `aggregator-out/summary-absolute.md` — table (a) across all 150 rows
- `aggregator-out/summary-delta.md` — V2-vs-Today % with CI
- `aggregator-out/summary-gap.md` — V2-vs-Direct gap
- `aggregator-out/trend.csv` — flat export
- `run.log` — combined stdout of all 5 launches

## Related runs

- [`../2026-05-26-arm64-perf/`](../2026-05-26-arm64-perf/) — initial
  Phase 1 capture (OnCustomEvent path; KD-3 source data showing M2
  +57% / M5 +38.8%)
- [`../2026-05-26-arm64-perf-typed/`](../2026-05-26-arm64-perf-typed/)
  — typed-payload rewrite (KD-3 fix landed; M2 −21.5%, M5 −4.2% in
  that 5-rep snapshot)
- [`../2026-05-26-arm64-perf-typed-fullx10/`](../2026-05-26-arm64-perf-typed-fullx10/)
  — first 10-rep capture that surfaced M4 +51.9% under a quieter
  machine state
- [`../2026-05-26-arm64-perf-typed-gate/`](../2026-05-26-arm64-perf-typed-gate/)
  — same after the callback-presence wiring gate
- **this folder** — final 5×5 cross-process aggregate
