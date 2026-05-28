# Spec 047 §14 Phase 3 close-out, x64 advisory

**This is an advisory x64 capture, NOT authoritative.** Cloud PC
(`CPC-ander-YTZ3O`, AMD EPYC 7763 64-Core Processor, x64), not on
AC/dedicated hardware. Do not cite these numbers in §13 or §14 spec
text. A stable-AC ARM64 re-capture on `LAPTOP-4MEP83VI` should ratify
the matrix before §14 Phase 3 is closed.

## Why this capture exists

This run extends the `2026-05-27-phase3-final-3x5/` matrix with the
four close-out commits landed on `spec/047-phase3-close-out` (off PR
#436 HEAD):

- **Engine (1)** — `Panel<>.PerChildAttachedAfterAll` two-pass shape.
- **Engine (2)** — `TemplatedItems<>` strategy + `Reconciler.BindKeyedItemsSource`,
  plus the T-erased `TemplatedItemsErased<>` + `BindErasedKeyedItemsSource`
  variant.
- **Port (4)** — RelativePanel descriptor uses the new after-all callback.
- **Port (5) G2** — `TemplatedListView<T>` / `TemplatedGridView<T>` via
  base-derived registration. Adds a base-walk to
  `V1HandlerRegistry.TryGet` (exact-type entries always win; base-derived
  entries cached per concrete type for O(1) steady state).

`DescriptorVariantFactory` now registers **52 ported controls** (50
from prior + 2 new via `RegisterHandlerForDerivedTypes` for the typed
templated lists). The bench matrix detects (a) dispatch-table shape
change from the +2 entries and the new strategy pattern-match arms in
`V1HandlerAdapter`, (b) the registry's base-walk fallback cost on
non-derived lookups, and (c) the `DescriptorHandler.Children` switch
gaining the two new templated-items cases.

## Capture environment

`CPC-ander-YTZ3O`, x64 (AMD EPYC 7763 64-Core Processor), Release,
.NET 10.0.8, Windows 11 26200. **Cloud PC — not on AC/dedicated
hardware**. 3 process launches × 5 reps × 13 benches × 4 variants =
780 measurements across `launch-1.jsonl` + `launch-2.jsonl` +
`launch-3.jsonl`.

## Headline — V1 ON (descriptors, post close-out) vs V1 OFF (today)

Median of n=15 (3 launches × 5 reps) per cell. Compared against the
prior `2026-05-27-phase3-final-3x5/` headline (50 controls) to surface
the close-out delta.

| Bench | This capture (V1 ON vs V1 OFF) | Prior `phase3-final-3x5` | Delta vs prior | Notes |
|---|---:|---:|---:|---|
| M1 Mount_Leaf_NoCallback | **+21.2%** | +14.9% | +6.3pp regression | New strategy pattern-match arms in `V1HandlerAdapter.DispatchChildrenMount` add two upfront `is`-checks (`ITemplatedItemsStrategy`, `IErasedTemplatedItemsStrategy`). |
| M2 Mount_Leaf_OneCallback | -0.1% | -1.7% | within noise | |
| M3 Mount_Leaf_ThreeCallbacks | -1.0% | +3.3% | improvement | |
| M4 Dispatch_Switch_Cold | **-20.8%** | -21.2% | held | Dispatch wins persist — the +2 entries don't push past the inflection. |
| M5 Dispatch_Switch_Warm | **-23.9%** | -24.3% | held | Same. |
| M6 Dispatch_ExternalType | -0.5% | +0.2% | within noise | The new base-walk fallback in `V1HandlerRegistry.TryGet` is gated on `_baseEntries.Count == 0` so external-type lookups skip it. |
| M7 Update_NoChange | +6.3% | +7.4% | minor improvement | |
| M8 Update_OneLeafChanged | **+18.9%** | +25.5% | improvement (-6.6pp) | Largest movement. `DescriptorHandler.Children` switch refactor (added `ITemplatedItemsStrategy` / `IErasedTemplatedItemsStrategy` arms returning `null` so dispatch happens inline) shortens the non-ItemsHost Update path. |
| M9 Update_AllChanged | +4.5% | +3.6% | within noise | |
| M10 EventHandlerState_Alloc | -1.7% | +8.7% | improvement (-10.4pp) | Volatile run-to-run; not load-bearing. |
| M11 ModifierEHS_Frequency | +9.7% | +8.5% | within judgment band | |
| M12 Pool_Rent_HotPath | **+18.5%** | +20.9% | held | Descriptor-interpreter pool-rent overhead persists; known regression. |
| M13 Setters_Suppression_Scope | -2.1% | -0.9% | within noise | |

**Net signal**:

- **M1 regressed +6.3pp** from the prior advisory — directly attributable
  to the strategy pattern-match arms added in
  `V1HandlerAdapter.DispatchChildrenMount`. Two `is`-checks fire before
  the pattern switch on every Mount, even for leaves that don't use
  templated items. Worth folding into the prior `case` switch in a
  Phase 4 perf-tuning pass; not load-bearing for correctness.
- **M8 improved -6.6pp** — the `DescriptorHandler.Children` switch
  short-circuit for ItemsHost / templated-items strategies is a
  structural win.
- **M4 / M5 dispatch wins held**.
- **M12 Pool_Rent_HotPath +18.5%** carry-over — same descriptor-rent
  overhead the prior capture already documented; nothing in this branch
  intersects.

## Q1 decision matrix — for completeness

Per §13 Q1's pre-committed decision matrix applied to
ReactorDescriptors vs ReactorV2:

| Bench | vs ReactorV2 ns | Q1 band |
|---|---:|---|
| M1 | +20.2% | exceeds 15% — judgment call vs LOC/readability |
| M2 | -0.5% | ship descriptors |
| M5 | -19.5% | ship descriptors (improvement) |
| M7 | +7.3% | judgment-call band |
| M10 | +5.2% | judgment-call band |

**Verdict:** No reopen condition for Q1 — Q1's reopen is gated on
source-gen (§7) landing, not advisory perf noise. The close-out scope's
M1 +21.2% and M12 +18.5% should be confirmed on stable-AC ARM64 before
any spec-text change.

## Caveats

- **Cloud PC noise.** Per the prior README: "noise-prone, advisory.
  Do not cite in §13/§14 spec text."
- **ARM64 stable-AC re-capture on `LAPTOP-4MEP83VI` is deferred** for
  the §14 ratification gate.

## Reproduce

```powershell
cd C:\Users\andersonch\Code\reactor2
dotnet build tests/perf_bench/PerfBench.ControlModel -c Release -p:Platform=x64
$exe = "tests\perf_bench\PerfBench.ControlModel\bin\x64\Release\net10.0-windows10.0.22621.0\PerfBench.ControlModel.exe"
$out = "docs\specs\047\phase3-results\CPC-ander-YTZ3O-x64-advisory\2026-05-27-phase3-closeout-3x5"
$results = "tests\perf_bench\PerfBench.ControlModel\bin\x64\Release\net10.0-windows10.0.22621.0\results.jsonl"
for ($i = 1; $i -le 3; $i++) {
    Remove-Item $results -ErrorAction SilentlyContinue
    Start-Process -FilePath $exe -Wait -NoNewWindow   # Start-Process -Wait is required;
                                                      # `& $exe` does not block on this WinUI app.
    Copy-Item $results "$out\launch-$i.jsonl"
}
python "$out\aggregate.py" > "$out\summary.md"
```

See `summary.md` for the full per-bench table.
