# Spec 047 §14 Phase 3 finish, x64 advisory

**This is an advisory x64 capture, NOT authoritative.** Cloud PC
(`CPC-ander-YTZ3O`, AMD EPYC 7763 64-Core Processor, x64), not on
AC/dedicated hardware. Do not cite these numbers in §13 or §14 spec
text. A stable-AC ARM64 re-capture on `LAPTOP-4MEP83VI` remains the
§14 ratification gate.

## Why this capture exists

Extends the `2026-05-27-phase3-closeout-3x5/` matrix with the engines +
ports + dispatch consolidation landed on `spec/047-phase3-finish` (off
`spec/047-phase3-close-out` HEAD):

- **Engine (1)** — `Reconciler.BindErasedKeyedItemsSource` gains a
  `case WinUI.ItemsRepeater` arm. Adds the internal
  `IItemsRepeaterFactorySource` companion interface to
  `IKeyedItemSource`.
- **Engine (2)** — `.ImperativeBridged(mount, update)` PropEntry —
  bridged superset of Engine (4)'s `.Imperative` (full `MountContext` /
  `UpdateContext` so the lambda can call `Reconciler.ReconcileV1Child`).
- **Engine (3)** — TeachingTip.Target audit. No engine code.
- **Engine (4)** — `.Imperative(mount, update)` PropEntry — property-
  level escape hatch with old + new TElement.
- **Engine (5)** — NumberBox.Min/Max coercion audit. No new engine
  surface; `.CoercingOneWay` already matched the legacy suppression
  pattern.
- **Port (6) Lazy*Stack G2** — `LazyVStackElement<T>` /
  `LazyHStackElement<T>` via base-derived registration. Single
  descriptor on `LazyStackElementBase` catches every closed-T variant
  through `RegisterHandlerForDerivedTypes`.
- **Carve-forwards** (12) Expander.HeaderTemplate, (14) Path.PathDataString,
  (15) NumberBox Min/Max — all ported through the engines above.
- **Dispatch consolidation** — `ITemplatedItemsStrategy` +
  `IErasedTemplatedItemsStrategy` inherit from a new base
  `IItemsBinderStrategy`; `V1HandlerAdapter.DispatchChildrenMount` /
  `DispatchChildrenUpdate` and `DescriptorHandler.Mount` / `Update`
  collapse their per-strategy `is`-checks into one. **Expected M1
  improvement** vs `phase3-closeout-3x5/` where the +2 `is`-checks
  drove the M1 regression.

`DescriptorVariantFactory` now registers **53 ported controls** (52
prior + 1 new — the single `LazyStackDescriptor` on the non-generic
base catches every closed-T `LazyVStackElement<T>` /
`LazyHStackElement<T>` variant).

## Capture environment

`CPC-ander-YTZ3O`, x64 (AMD EPYC 7763 64-Core Processor), Release,
.NET 10.0.8, Windows 11 26200. **Cloud PC — not on AC/dedicated
hardware**. 3 process launches × 5 reps × 13 benches × 4 variants =
780 measurements across `launch-1.jsonl` + `launch-2.jsonl` +
`launch-3.jsonl`.

## Headline — V1 ON (descriptors, post-finish) vs V1 OFF (today)

Median of n=15 (3 launches × 5 reps) per cell. Compared against the
prior `2026-05-27-phase3-closeout-3x5/` headline.

| Bench | This capture (V1 ON vs V1 OFF) | Prior `phase3-closeout-3x5` | Delta vs prior | Notes |
|---|---:|---:|---:|---|
| M1 Mount_Leaf_NoCallback | **+20.7%** | +21.2% | -0.5pp (held) | Dispatch consolidation collapsed the two strategy `is`-checks into one; the structural change is in place but didn't materially retract the close-out regression on this Cloud-PC run. M1 fix likely needs the actual fast-path work (Phase 4 perf-tuning), not just the marker fold. |
| M2 Mount_Leaf_OneCallback | +1.3% | -0.1% | within noise | |
| M3 Mount_Leaf_ThreeCallbacks | -1.8% | -1.0% | within noise | |
| M4 Dispatch_Switch_Cold | **-20.2%** | -20.8% | held | Dispatch wins persist with the +1 base-derived descriptor. |
| M5 Dispatch_Switch_Warm | **-17.8%** | -23.9% | -6.1pp (retraction) | Mild retraction but still a clear win; consistent with the wider registration table adding one more base-walk entry. |
| M6 Dispatch_ExternalType | -0.4% | -0.5% | held | Base-walk fallback in `V1HandlerRegistry.TryGet` still gated on `_baseEntries.Count == 0` for external lookups. |
| M7 Update_NoChange | +6.4% | +6.3% | held | |
| M8 Update_OneLeafChanged | **+21.8%** | +18.9% | +2.9pp | Lazy*Stack base-derived registration adds the only known new Update-path cost on this branch. Within Cloud-PC noise band but worth re-confirming on ARM64. |
| M9 Update_AllChanged | +3.5% | +4.5% | minor improvement | |
| M10 EventHandlerState_Alloc | +1.2% | -1.7% | volatile | Run-to-run drift; not load-bearing. |
| M11 ModifierEHS_Frequency | +10.7% | +9.7% | held | In judgment band. |
| M12 Pool_Rent_HotPath | **+30.7%** | +18.5% | **+12.2pp regression** | Notable Cloud-PC regression. M12 has been volatile across captures (Phase 3-final = +20.9%, close-out = +18.5%, now +30.7%). Worth confirming on stable AC. Engine work in this branch doesn't intersect with the rent hot path; the most likely cause is the +1 descriptor entry's effect on the rent-table dispatch shape. |
| M13 Setters_Suppression_Scope | +6.4% | -2.1% | flipped | Mild regression but within Cloud-PC noise for this picosecond bench. |

**Net signal**:

- **M1 regression persists** at +20.7% — the dispatch consolidation's structural fold (two markers → one base marker) didn't recover the +6.3pp the close-out added on this Cloud-PC run. The instruction count is reduced, but the cache-miss / branch-predict patterns appear to dominate at M1 scales. A genuine M1 fix likely needs the Phase 4 perf-tuning pass that consolidates strategy markers into a top-level `case` arm in the existing pattern switch rather than a leading `if`-block.
- **M4 / M5 dispatch wins held** — fatter registration table doesn't push past the inflection point.
- **M8 +2.9pp** and **M12 +12.2pp** are the new regressions vs prior. M8 is at the edge of Cloud-PC noise; M12 is large enough to flag for stable-AC re-confirmation.
- **No bench exceeds the §13 Q1 reopen threshold** (reopen is gated on source-gen, not advisory perf).

## Q1 decision matrix — for completeness

Per §13 Q1's pre-committed decision matrix applied to
ReactorDescriptors vs ReactorV2:

| Bench | vs ReactorV2 ns | Q1 band |
|---|---:|---|
| M1 | +18.9% | exceeds 15% — judgment call vs LOC/readability |
| M2 | +2.9% | ship descriptors |
| M4 | -19.2% | ship descriptors (improvement) |
| M5 | -17.1% | ship descriptors (improvement) |
| M8 | +26.8% | exceeds 15% — judgment call |
| M12 | +20.8% | exceeds 15% — judgment call |

**Verdict:** No reopen condition for Q1 — Q1's reopen is gated on
source-gen (§7) landing, not advisory perf noise. The Phase 3 finish
scope's M1 +20.7% / M8 +21.8% / M12 +30.7% should be confirmed on
stable-AC ARM64 before any spec-text change.

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
$out = "docs\specs\047\phase3-results\CPC-ander-YTZ3O-x64-advisory\2026-05-28-phase3-finish-3x5"
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
