# Spec 042 perf gate — Reactor vs WinUI vanilla virtualizing list

Captured: 2026-05-17 11:58:02 -07:00
Duration: 8s per run, 5× repetitions, warm-up discarded (1 rep).

Reactor variant: `StressPerf.VirtualList.Reactor` — Reactor `LazyVStack<ListItem>` on top of `ItemsRepeater` + `KeyedListDiff` from spec 042 Phase 1.
WinUI variant:   `StressPerf.VirtualList.WinUI` — hand-written `ItemsRepeater` + `ObservableCollection<ListItem>` + recycling element factory. Edits mutate the OC in place.

Both variants share `StressPerf.Shared.ListItemSource` for data + row metrics, identical scroll tween, identical edit policy (50/50 insert/remove, deterministic seed 1234567).

## Median across 5 reps (ms — lower is better)

| Count | Edits/s | Reps | Reactor P50 | WinUI P50 | Δ P50 % | Reactor P95 | WinUI P95 | Δ P95 % | Reactor Avg | WinUI Avg | Δ Avg % |
|------:|--------:|-----:|------------:|----------:|--------:|------------:|----------:|--------:|------------:|----------:|--------:|
|   1000 |       0 |    5 |       16.66 |     16.67 |    -0.1 |       17.46 |     17.96 |    -2.8 |       16.66 |     16.66 |       0 |
|   1000 |       4 |    5 |       16.67 |     16.66 |     0.1 |       21.12 |     19.64 |     7.5 |       16.83 |     16.74 |     0.5 |
|   1000 |      16 |    5 |       16.67 |     16.70 |    -0.2 |       36.13 |     23.81 |    51.7 |       19.94 |     17.28 |    15.4 |
|  10000 |       0 |    5 |       16.66 |     16.77 |    -0.7 |       18.14 |     28.97 |   -37.4 |       16.66 |     18.56 |   -10.2 |
|  10000 |       4 |    5 |       16.65 |     16.80 |    -0.9 |       46.91 |     31.55 |    48.7 |       19.93 |     19.33 |     3.1 |
|  10000 |      16 |    5 |       16.64 |     17.20 |    -3.3 |       86.56 |     31.33 |   176.3 |       21.87 |     19.32 |    13.2 |

Negative Δ values indicate Reactor is faster than the WinUI baseline.

## Pass criteria

Spec 042 perf gate: Reactor stays within **+5%** of WinUI on the steady-state scroll case, **+10%** under the edit-stress mode. The reconciler does extra work (it computes a keyed diff between two array references before producing OC events) so a small positive delta is expected; a delta larger than the threshold is a regression.

## Raw artefacts

- `summary.csv` — one row per cell, median across reps
- `per-rep.csv` — every individual rep's percentiles
- `{reactor,winui}.<cell>.rep<N>.frames.csv` — every captured frame delta per rep, for forensic analysis
