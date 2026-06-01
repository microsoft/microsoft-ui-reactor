# Spec 047 §14 Phase 3 — final bulk-port, x64 advisory

**This is an advisory x64 capture, NOT authoritative.** Cloud PC
(`CPC-ander-YTZ3O`, AMD EPYC 7763 64-Core Processor, x64), not on
AC/dedicated hardware. Do not cite these numbers in §13 or §14 spec
text. A stable-AC ARM64 re-capture on `LAPTOP-4MEP83VI` should ratify
the matrix before §14 Phase 3 is closed.

## Why this capture exists

This run extends the `2026-05-27-batch-1-2-3x5/` matrix with the
remaining Phase 3 batches landed in the bulk-port PR
(`spec/047-phase3-bulk-port`):

- **Batch 3** — Display family (TextBlock, Image, PersonPicture,
  ProgressBar, ProgressRing, InfoBadge).
- **Batch 4** — Button family (Button, HyperlinkButton, RepeatButton,
  ToggleButton, DropDownButton, SplitButton).
- **Batch 5** — Value-bearing inputs (RichEditBox, PasswordBox,
  RadioButtons).
- **Batch 6** — Multi-event inputs (AutoSuggestBox, ComboBox).
- **Batch 7** — Single-content containers (Viewbox, Expander,
  ScrollViewer, ScrollView).
- **Batch 8** — Panels (StackPanel, Grid, Canvas, FlexPanel,
  RelativePanel).
- **Batch 9** — Named-slot containers (SplitView, InfoBar, TeachingTip).
- **Batch 10** — Shapes + display leaves (Rectangle, Ellipse, Line,
  Path, AnimatedIcon).
- **Batch 11** — Long tail (PipsPager, ListBox, SelectorBar,
  BreadcrumbBar).

After all batches the `DescriptorVariantFactory` registers **50 ported
controls** (12 from prior PRs: 4 prereq + 8 batches 1+2; 38 new from
batches 3-11). The bench matrix detects dispatch-table shape change
and descriptor-interpreter amortization across M1-M13 even though
those benches don't mount the new controls directly.

## Capture environment

`CPC-ander-YTZ3O`, x64 (AMD EPYC 7763 64-Core Processor), Release,
.NET 10.0.8, Windows 11 26200. **Cloud PC — not on AC/dedicated
hardware**. 3 process launches × 5 reps × 13 benches × 4 variants =
780 measurements across `launch-1.jsonl` + `launch-2.jsonl` +
`launch-3.jsonl`.

## Headline — V1 ON (descriptors) vs V1 OFF (today)

The user-facing question is "does turning V1 ON with the full Phase 3
descriptor set regress anything." Median of n=15 (3 launches × 5 reps)
per cell:

| Bench | V1 ON (Descriptors) vs V1 OFF (Today) | Verdict |
|---|---:|---|
| M1 Mount_Leaf_NoCallback | **+14.9%** | exceeds 5-15% judgment-call band ceiling |
| M2 Mount_Leaf_OneCallback | -1.7% | within noise |
| M3 Mount_Leaf_ThreeCallbacks | +3.3% | within noise |
| M4 Dispatch_Switch_Cold | **-21.2%** | improvement (fewer types fall through to switch arm) |
| M5 Dispatch_Switch_Warm | **-24.3%** | improvement (same; warmer dispatch hits handler table) |
| M6 Dispatch_ExternalType | +0.2% | within noise |
| M7 Update_NoChange | +7.4% | judgment-call band |
| M8 Update_OneLeafChanged | **+25.5%** | **largest regression** |
| M9 Update_AllChanged | +3.6% | within noise |
| M10 EventHandlerState_Alloc | +8.7% | judgment-call band |
| M11 ModifierEHS_Frequency | +8.5% | judgment-call band |
| M12 Pool_Rent_HotPath | **+20.9%** | second-largest regression |
| M13 Setters_Suppression_Scope | -0.9% | within noise |

**The +25.5% on M8 (Update_OneLeafChanged) and +20.9% on M12
(Pool_Rent_HotPath) exceed the §13 Q1 5-15% judgment-call band.**
This is a real surfacing of descriptor-interpreter Update overhead
compared to the hand-coded switch arms, now amortized over 50
registered controls.

The M4/M5 improvements are the mirror image: dispatch is faster when
more types are in the handler table (the fallback to the switch arm
is rarer). Net Mount cost is dominated by interpreter overhead (M1
+14.9%); net Update cost regresses substantially on one-leaf-change
workloads (M8 +25.5%).

These deltas reproduce the trade-off the §13 Q1 stable-AC capture
already documented (M2 +9.6% at batch-2 scope). The 5-percentage-point
shift on the M1/M5 readings between this capture and the prior
`2026-05-27-batch-1-2-3x5/` capture is consistent with the prior
README's "noise-prone, advisory" caveat — Cloud PC, no AC pinning.

## Q1 decision matrix — for completeness

Per §13 Q1's pre-committed decision matrix applied to
ReactorDescriptors vs ReactorV2:

| Bench | vs ReactorV2 ns | Q1 band |
|---|---:|---|
| M1 | +19.6% | exceeds 15% — judgment call vs LOC/readability |
| M2 | -5.5% | ship descriptors |
| M5 | -18.6% | ship descriptors (improvement) |
| M7 | +7.7% | judgment-call band |
| M10 | +2.4% | ship descriptors |

**Verdict:** No reopen condition for Q1 — Q1's reopen is gated on
source-gen (§7) landing, not advisory perf noise. The scope-amortized
deltas on M1/M8/M12 should be confirmed on stable-AC ARM64 before any
spec-text change.

## Caveats

- **Cloud PC noise.** Per the prior README: "noise-prone, advisory.
  Do not cite in §13/§14 spec text."
- **ARM64 stable-AC re-capture on `LAPTOP-4MEP83VI` is deferred** from
  this session (not on critical path; tracked for spec close-out
  alongside the §14 Phase 3 ratification gate).
- M7 (Update_NoChange) Direct measurement is dominated by the
  3-second `Stopwatch` calibration in BenchRunner — Direct timing on
  this no-op bench is degenerate; only the relative
  Today/V2/Descriptors comparison is meaningful.

## Reproduce

```powershell
cd C:\Users\andersonch\Code\reactor2
dotnet build tests/perf_bench/PerfBench.ControlModel -c Release -p:Platform=x64
# 3 launches:
1..3 | ForEach-Object {
    & tests/perf_bench/PerfBench.ControlModel/bin/x64/Release/net10.0-windows10.0.22621.0/PerfBench.ControlModel.exe
    Copy-Item tests/perf_bench/PerfBench.ControlModel/bin/x64/Release/net10.0-windows10.0.22621.0/results.jsonl `
              docs/specs/047/phase3-results/CPC-ander-YTZ3O-x64-advisory/2026-05-27-phase3-final-3x5/launch-$_.jsonl
}
```

Aggregation: median across (benchId, variant) over the three launches.
See `summary.md` for the full per-bench table.
