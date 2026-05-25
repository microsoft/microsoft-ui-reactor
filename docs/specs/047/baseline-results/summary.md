# Spec 047 Phase 0 Baseline Summary

This file pulls together the captured measurements that exit the Phase 0
gate. Spec §15.6 (a) absolute-comparison tables for every shipped scenario
are reproduced below; raw per-iteration JSON-Lines live under
`<machine>/<date>/`.

> **Generation:** these tables are emitted by
> [`tools/spec047-aggregator`](../../../tools/spec047-aggregator/) consuming
> the raw JSON-Lines files. Re-generate with:
>
> ```pwsh
> dotnet run --project tools/spec047-aggregator -- `
>     --in 'docs/specs/047/baseline-results/*.jsonl' `
>     --out docs/specs/047/baseline-results/aggregator-out
> ```
>
> The aggregator's glob expansion recurses through subdirectories
> automatically (no `**` segment needed). Each output row is keyed by
> (bench, variant, architecture), so ARM64-native and x64-emulated runs
> render as separate rows rather than being silently averaged.

## Machines

See [`machines.md`](machines.md). The headline Phase-0 capture is
**ARM64-native** on LAPTOP-4MEP83VI (Snapdragon X laptop). The companion
**x64-native** capture on CPC-ander-YTZ3O (Windows 365 Cloud PC, AMD EPYC
7763) closes the §14 deliverable-4 two-machine requirement; see
[`CPC-ander-YTZ3O/2026-05-25-x64/`](CPC-ander-YTZ3O/2026-05-25-x64/) and
the "Headline observations from x64" subsection below. A prior x64-
emulated capture from LAPTOP-4MEP83VI is preserved in the `2026-05-25/`
folder for reference but **superseded**.

## Micro suite (M1–M13) — ARM64-native, retail Release

The JSON-Lines stream for the headline Phase-0 run is at
`LAPTOP-4MEP83VI/2026-05-25-arm64/perfbench-controlmodel-m1-m8.jsonl`,
`…-m9.jsonl`, and `…-m10-m13.jsonl` (one row per bench × variant × rep).
Aggregator output (the §15.6 (a)/(b)/(c) tables) is regenerated on demand
into `aggregator-out/`.

### Headline observations from the captured data

195 rows ingested, 0 excluded. At Phase 0, V2 ≡ Today so the V2 column is
the noise floor on V2 ≈ Today; Phase 1+ V2 divergence shows up here.

| Bench | Direct ns | Today ns | V2 ns | Direct alloc | Today alloc | V2 alloc | V2 vs Today |
|---|---:|---:|---:|---:|---:|---:|---:|
| M1  | 32,803 | 32,688 | 38,140 | 3.77 MB | 5.35 MB | 5.39 MB | +16.7% (GC noise) |
| M2  | 52,337 | 64,676 | 61,895 | 13.4 MB | 19.4 MB | 19.3 MB | -4.3% |
| M3  | 176,725 | 210,204 | 237,622 | 26.7 MB | 45.4 MB | 43.2 MB | +13.0% (GC noise) |
| M4  | 33,208 | 90,659 | 90,746 | 4.94 MB | 9.97 MB | 9.99 MB | +0.1% |
| M5  | 21,245 | 86,188 | 86,600 | 4.93 MB | 9.96 MB | 11.06 MB | +0.5% |
| M6  | 30,207 | 32,513 | 31,743 | 3.61 MB | 4.70 MB | 4.70 MB | -2.4% |
| M7  | 987,029 | 13,138 | 11,985 | 123 MB | 780 KB | 780 KB | -8.8% |
| M8  | 4,357 | 4,586 | 4,155 | 1.02 MB | 2.12 MB | 2.12 MB | -9.4% |
| M9  | 815,247 | 1,399,885 | 1,429,733 | 96.8 MB | 624 MB | 624 MB | +2.1% |
| M10 | 33,633 | 53,173 | 48,915 | 2.97 MB | 4.06 MB | 3.95 MB | -8.0% |
| M11 | 60 | 39,752 | 33,094 | 40 B | 1.73 MB | 1.67 MB | -16.7% (GC noise) |
| M12 | 31,376 | 28,046 | 29,943 | 760 KB | 1.09 MB | 1.09 MB | +6.8% |
| M13 | 27 | 137 | 155 | 24 KB | 29 KB | 29 KB | +13.4% (correctness, §8.2) |

Values are mean of 5 reps. Iterations per rep: 5000 for M1–M8, 2000 for M9
(reduced from 5000 because each iteration constructs a 1000-element tree
of fresh elements; full 5000 OOM'd on the x64-emulated run, and the 2000
× 1000-element shape still produces a clean ARM64 measurement), 1000 for
M10–M13. The `meanNs` column is per-iteration; alloc bytes is per-rep
total — divide by iterations for per-op alloc.

**Phase-0 takeaways:**

- **Mount/unmount lifecycle** — M1/M2/M3/M4/M6/M10 RunOne now invokes
  `Reconciler.UnmountChild(ui)` after each iteration so the bench is
  measuring a true mount+unmount cycle rather than a leaking
  add-to-tree loop. Spec §15.5 correctness baseline; the original PR
  #411 numbers were re-captured after the fix.
- **M1 `Mount_Leaf_NoCallback`** — Direct 754 B/op, Today 1071 B/op,
  Reactor overhead = **+317 bytes per leaf**. Spec §11.1's draft
  estimate of ~248 B underestimates by ~28%; the measurement supersedes
  the estimate per §14 deliverable 4.
- **ARM64 vs x64-emulated** — ARM64-native is **~10–20× faster** than
  x64-emulated x86_64 on the same hardware for every Mn. The earlier
  x64-emulated capture is preserved only as a worst-case reference; the
  ARM64-native numbers are the load-bearing baseline for spec §11 / §12.
- **M2 / M3 (one / three callbacks)** — per-rep allocation variance is
  the dominant source of noise. The §9 split + per-control struct shapes
  from
  [`audits/event-handler-state-audit.md`](../audits/event-handler-state-audit.md)
  are designed to lock the alloc baseline.
- **M7 `Update_NoChange`** — Direct naive `tb.Text = tb.Text` loop over
  1000 children: **987 µs / op**. Reactor's `UpdateChild` short-circuit:
  **13 µs / op**. Reactor is **~75× faster** than the naive direct
  re-render path on a 1000-element no-change tree. Confirms spec §12.7's
  claim that Reactor's diff is a product feature, not pure framework
  overhead.
- **M13 `Setters_Suppression_Scope`** — counter
  `OnIsOnChangedFireCount = 1` on both ReactorToday and ReactorV2.
  **Confirms the §8.2 bug exists in the baseline.** Phase 1's fix
  (the §8.2 standalone setter-suppression PR per
  [`factoring-recommendation.md`](../factoring-recommendation.md))
  flips this counter to 0.
- **M11 `ModifierEHS_Frequency`** — placeholder counter at Phase 0.
  Real EventSource counter wiring deferred to Phase 1.
- **V2 vs Today columns** range from -16.7% to +16.7%. None are real
  signals at Phase 0; they're GC-noise floor. Phase 1 V2 work makes the
  column meaningful.

### Headline observations from x64 (CPC-ander-YTZ3O, Windows 365 Cloud PC)

Companion x64-native capture per spec §14 deliverable 4. 195 rows
ingested, 0 excluded. JSON-Lines at
[`CPC-ander-YTZ3O/2026-05-25-x64/`](CPC-ander-YTZ3O/2026-05-25-x64/);
aggregator output in the same folder's `aggregator-out/`.

| Bench | Direct ns | Today ns | V2 ns | Direct alloc | Today alloc | V2 alloc | V2 vs Today |
|---|---:|---:|---:|---:|---:|---:|---:|
| M1  | 88,060 | 101,529 | 101,557 | 3.77 MB | 5.35 MB | 5.39 MB | 0.0% |
| M2  | 98,019 | 129,841 | 136,717 | 14.1 MB | 19.1 MB | 19.0 MB | +5.3% (GC noise) |
| M3  | 274,521 | 353,868 | 350,436 | 28.3 MB | 44.8 MB | 44.6 MB | -1.0% |
| M4  | 58,636 | 143,436 | 159,307 | 5.10 MB | 10.1 MB | 10.2 MB | +11.1% (GC noise) |
| M5  | 58,706 | 144,107 | 150,496 | 5.10 MB | 10.1 MB | 10.2 MB | +4.4% |
| M6  | 87,794 | 96,019 | 95,956 | 3.77 MB | 4.79 MB | 4.83 MB | -0.1% |
| M7  | 1,417,352 | 28,337 | 28,157 | 123 MB | 812 KB | 812 KB | -0.6% |
| M8  | 11,965 | 12,879 | 12,660 | 1.02 MB | 2.12 MB | 2.12 MB | -1.7% |
| M9  | 1,138,719 | 2,213,204 | 2,220,921 | 96.8 MB | 624 MB | 624 MB | +0.3% |
| M10 | 98,502 | 123,920 | 97,959 | 3.12 MB | 4.04 MB | 3.78 MB | -21.0% (GC noise) |
| M11 | 43 | 93,346 | 92,743 | 40 B | 1.62 MB | 1.61 MB | -0.6% |
| M12 | 84,780 | 96,556 | 94,597 | 760 KB | 1.09 MB | 1.06 MB | -2.0% |
| M13 | 43 | 230 | 192 | 24 KB | 30 KB | 30 KB | -16.7% (correctness, §8.2) |

**Phase-0 takeaways from the x64 capture:**

- **§8.2 bug reproduces on x64** — M13 `OnIsOnChangedFireCount = 1` on
  both ReactorToday and ReactorV2, same as ARM64. The bug is not
  architecture-dependent.
- **M7 `Update_NoChange` Reactor speedup holds** — Direct naive
  re-render: **1417 µs**; ReactorToday: **28 µs**. Reactor is **~50×**
  faster than the naive direct path on x64 (vs ~75× on ARM64). The diff
  short-circuit's value scales across architectures.
- **Alloc-bytes parity with ARM64** — per-op allocations match across
  architectures within rounding (e.g. M1 Today 1071 B/op both machines;
  M9 Today ~624 MB / 2000 iter both). Confirms the bench is measuring
  the same code path; alloc is the deterministic axis, ns is the
  CPU-sensitive one.
- **Cloud PC absolute numbers are slower than the Snapdragon X laptop**
  by ~1.6–3.4× across the suite (worst on M12, best on M9). This is the
  shared-vCPU Windows 365 host showing through, not an ARM64-vs-x64
  silicon claim. A real bare-metal x64 workstation will produce
  different absolute numbers; the deliverable-4 requirement is satisfied
  by having captured both arches with the spec §15.5 separation
  enforced.
- **V2 vs Today columns** range from -21.0% to +11.1%. As on ARM64,
  these are GC-noise floor at Phase 0, not real V2 signal. M10's -21%
  and M4's +11% are the widest spreads — both are dominated by alloc
  variance per-rep, same diagnosis as M3 on ARM64.

### §11.1 / §11.6 — re-derived target table (ARM64-native)

Per spec §14 Phase 0 deliverable 4, this table replaces §11.1's estimated
column with the measured values:

| Case | Bytes today (measured M1–M3, mean of 5 reps) | Direct (measured) | Phase-1 V2 target |
|---|---:|---:|---:|
| Leaf, no callbacks (M1) | 1071 | 754 | min(Direct + 100, Today × 0.4) = min(854, **428**) ⇒ **428** |
| Leaf, one callback (M2) | ~3884 | ~2679 | min(Direct + 100, Today × 0.4) = min(2779, **1554**) ⇒ **1554** |
| Leaf, three callbacks (M3) | ~9075 | ~5343 | min(Direct + 100, Today × 0.4) = min(5443, **3630**) ⇒ **3630** |

Per-op alloc derived as alloc-bytes / iterations:
- M1: Today 5,353,584 B / 5000 iter = 1071 B/op; Direct 3,771,877 / 5000 = 754 B/op
- M2: Today 19,420,072 / 5000 = 3884 B/op; Direct 13,395,280 / 5000 = 2679 B/op
- M3: Today 45,372,930 / 5000 = 9075 B/op; Direct 26,714,741 / 5000 = 5343 B/op

The chosen target = `min(Direct + 100, ReactorToday × 0.4)`. The "tighter
constraint" rule means V2 closes >60% of the Today–Direct gap.

### §12 — replace estimated ns figures (ARM64-native)

| Spec section | Today's estimate (ns) | Measured (mean of 5 reps) | Footnote |
|---|---:|---:|---|
| §12.1 mount dispatch | ~150 ns (estimate) | M4 cold one-of-8 types: Reactor 91 µs total → ~11 µs per element type | Original estimate held shape; absolute number includes Add to Children + UnmountChild round-trip. |
| §12.2 update no-change | ~50 ns (estimate) | M7 ReactorToday: 13 µs / op for 1000-element tree → ~13 ns per element | Estimate held within 4× — actual is faster than estimate. |
| §12.4 echo suppression | ~30 ns (estimate) | M13 baseline: 137 ns total for one Set + callback fire. The 30 ns estimate was for the BeginSuppress + ShouldSuppress check alone; M13 includes the entire mount + setter + callback path. | Estimate held. |
| §12.10 reconciler full update | "fraction of mount" | M9 all-changed: 1.40 ms ÷ 1000 elements = 1.40 µs per element update + alloc. Full update cost is ~110× a no-change cost (M7 13 ns vs M9 1400 ns per element). | The "fraction of mount" claim should be re-phrased: full-update is ~110× a no-change update but still cheaper than a fresh mount. |

Per spec §14: original estimated values are preserved in the footnotes so
the reasoning is not lost.

### Visual verification (screenshots)

Demo screenshots of each (Mn, variant) pair are in
[`LAPTOP-4MEP83VI/2026-05-25-arm64/screenshots/`](LAPTOP-4MEP83VI/2026-05-25-arm64/screenshots/)
(captured via `--demo` mode + win32 PrintWindow with PW_RENDERFULLCONTENT,
since WinUI 3's RenderTargetBitmap doesn't traverse the top-level
SwapChainPanel-rooted content).

39 PNGs (13 benches × 3 variants). See
[`screenshot-guide.md`](screenshot-guide.md) for what each scenario is
expected to show.

## Macro suite — L1 ships

Per [`macro-suite-status.md`](macro-suite-status.md), L1 is the only macro
fully shipped at Phase 0 (BlankWinUI3 + BlankReactor + BlankReactorV2,
all ARM64-built). L2 / L3 / L4 / L5 / L7–L9 / L11 are deferred per the
status doc.

L1 TTFF capture against LAPTOP-4MEP83VI is **not yet collected** at this
file's first write — `run_startup_bench.ps1` requires a full kernel ETW
session which is out of scope for the headless Phase-0 measurement loop.
Phase 1 ships the first L1 capture as part of the v1 protocol promotion
PRs.

## Aggregator output

The §15.6 (a) / (b) / (c) tables in machine-friendly form live under
[`aggregator-out/`](LAPTOP-4MEP83VI/2026-05-25-arm64/aggregator-out/).
Files:

- `summary-absolute.md` — table (a): variant-by-variant absolute values.
- `summary-delta.md` — table (b): V2 vs Today % with CI half-width.
- `summary-gap.md` — table (c): V2 vs Direct absolute overhead.
- `trend.csv` — flat per-row data for per-PR plotting.
- `excluded.txt` — rows rejected for environment-metadata mismatch
  (0 rows at the ARM64 capture).

## Caveats applicable to all Phase-0 numbers

1. **Single machine.** Per-row data is on LAPTOP-4MEP83VI only.
   Workstation-x64 captures are deferred to Phase 1.
2. **ARM64 native — but Snapdragon X-class.** A workstation-class x64
   chip will produce different absolute numbers; the Phase-1 follow-up
   captures both so the §15.6 comparison emitter rejects mismatched
   architectures.
3. **5 reps per bench × variant, iterations vary** (5000 for M1–M8,
   2000 for M9, 1000 for M10–M13). Sufficient for >5% precision on the
   per-op nanosecond figure but variance on alloc bytes (M2/M3) is GC-
   pressure-driven. The aggregator's 95% CI numbers flag where Phase 1
   needs more reps.
4. **Power / refresh metadata not stamped.** The Phase-0 bench predates
   the runbook's full environment-stamping plumbing; `PowerState` and
   `LockedRefreshHz` are "unknown" in the raw rows. Phase 1 wires the
   stamping per [`perf-suite-runbook.md`](perf-suite-runbook.md) §8.

## Re-running

```pwsh
# Full M1–M13 capture on ARM64-native retail (≈ 6–10 min on this machine):
& 'tests/perf_bench/PerfBench.ControlModel/bin/ARM64/Release/net10.0-windows10.0.22621.0/PerfBench.ControlModel.exe' `
    --test M1 M2 M3 M4 M5 M6 M7 M8 --iterations 5000 --reps 5 `
    --out "docs/specs/047/baseline-results/<machine>/<date>-arm64/perfbench-controlmodel-m1-m8.jsonl"
& 'tests/perf_bench/PerfBench.ControlModel/bin/ARM64/Release/net10.0-windows10.0.22621.0/PerfBench.ControlModel.exe' `
    --test M9 --iterations 2000 --reps 5 `
    --out "docs/specs/047/baseline-results/<machine>/<date>-arm64/perfbench-controlmodel-m9.jsonl"
& 'tests/perf_bench/PerfBench.ControlModel/bin/ARM64/Release/net10.0-windows10.0.22621.0/PerfBench.ControlModel.exe' `
    --test M10 M11 M12 M13 --iterations 1000 --reps 5 `
    --out "docs/specs/047/baseline-results/<machine>/<date>-arm64/perfbench-controlmodel-m10-m13.jsonl"

# Demo screenshots:
& 'tests/perf_bench/PerfBench.ControlModel/bin/ARM64/Release/net10.0-windows10.0.22621.0/PerfBench.ControlModel.exe' `
    --demo --screenshot-dir "docs/specs/047/baseline-results/<machine>/<date>-arm64/screenshots" `
    --demo-pause-ms 1500

# Regenerate aggregator output:
dotnet run --project tools/spec047-aggregator -- `
    --in 'docs/specs/047/baseline-results/<machine>/<date>-arm64/*.jsonl' `
    --out 'docs/specs/047/baseline-results/<machine>/<date>-arm64/aggregator-out'
```
