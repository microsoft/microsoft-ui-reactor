# StressPerf — measurement methodology

Two ways to measure visual update rate. The cheap one is "good enough" most
of the time; the accurate one is what you cite in any final number.

## Easy mode — `Total Renders` (no admin, in-app)

Every variant's `PerfTracker` increments a counter at the moment a render
"completed" for that framework:

| Variant     | What counts as one "render"                                            |
|-------------|------------------------------------------------------------------------|
| Direct      | Tick handler finished patching `TextBlock.Text/.Foreground` directly.  |
| Bound       | Tick handler finished pushing INPC notifications.                      |
| Wpf         | Same — imperative property patch.                                      |
| DirectX     | Tick called `Canvas.Invalidate()`. Actual D2D draw happens on a callback. |
| Reactor     | Reconcile-complete callback fired (`OnRenderComplete`).                |
| RN-Fabric   | Top of the React component body executed (one full React render).     |

Reported in every `*.report.txt` under `Total Renders: N`. Free to compute,
no privileges, ships in every headless run.

**Use for**: regression detection, sweep harnesses, CI lanes, anything that
needs to land on developer machines without elevation.

## Accurate mode — `DxgKrnl::Present` count (admin, ETW)

Run `tests/stress_perf/PresentTracer/` against the target PID. It subscribes
to `Microsoft-Windows-DxgKrnl` (kernel-mode, requires admin) and counts the
events whose name is `Present` per process. Each event = "a fresh frame from
this process was committed to the display." Composed apps don't call
DXGI's `Present()` themselves — DWM does on their behalf — but the
attribution lands on the source PID.

Reported as a count + interval percentiles by `PresentTracer.exe`. The
master harness `run_stocks_grid_baseline.ps1` runs this concurrently with
each variant.

**Use for**: real numbers in any final write-up. The user-perceived FPS.

## Why both — the calibration

Earlier baseline (4,900 cells, 50% mutation, ARM64 Release):

| Framework  | In-app FPS counter         | In-app `Total Renders/s` | **ETW Present/s** |
|------------|----------------------------|--------------------------|-------------------|
| Reactor    | 8.4 (`CompositionTarget.Rendering`) | 3.8                  | **3.7**          |
| RN-Fabric  | 2.4 (`requestAnimationFrame`)        | 2.1                  | **3.7**          |

The two in-app FPS readings disagree wildly with each other and with truth.

- **Reactor's `CompositionTarget.Rendering` overcounts by ~2×.** It fires
  once per UI-thread-idle vsync, including frames where nothing visible
  changed. The compositor presented the *previous* image; our callback
  still fired. Ground truth is half.
- **RN's `requestAnimationFrame` is gated on JS-thread availability.**
  Under-reports at light loads, bursts wildly under saturation (we saw
  Min 0.6 / Max 48.6 in a single 10s run). Garbage statistics whenever
  the framework is busy, which is exactly when you'd want to measure it.

`Total Renders / sec` is much closer to the truth (within ~5% on Reactor,
within ~30% on RN-Fabric vs ETW). It's our "good enough" proxy. **Don't
report in-app FPS as a number anywhere; report `Total Renders` if you don't
have ETW, or the ETW count if you do.**

## How to run

### Easy mode

Each variant's csproj is a normal .NET WinUI / WPF project. Per-variant
headless invocation:

```powershell
# Pick any of: Direct, Bound, Wpf, DirectX, Reactor
dotnet run --project tests/stress_perf/StressPerf.Reactor -c Release -p:Platform=ARM64 `
  -- --headless --percent 50 --duration 10
```

For RN-Fabric: `cd tests/stress_perf_rn/StocksGrid && npm run windows --
--headless --percent 50 --duration 10` (see that directory's README).

Report file lands next to the executable as `<AppName>.report.txt`.

### Accurate mode

```powershell
# Build once (once per platform):
dotnet build tests/stress_perf/PresentTracer -c Release -p:Platform=ARM64

# Run the full matrix elevated:
# (right-click → Run as administrator on a PowerShell prompt, then:)
& 'C:\Users\andersonch\Code\reactor3\tests\stress_perf\run_stocks_grid_baseline.ps1'
```

Output: `tests/stress_perf/baseline-stocks-grid.log` (full per-scenario
PresentTracer dump) and `baseline-stocks-grid.csv` (one row per
variant × percent with all metrics aggregated).

## Always label runs by power state — battery and AC are not comparable

On Windows 11, **Dynamic Refresh Rate (DRR)** lowers the display refresh
rate when GPU activity is low to save battery. On battery, idle desktop
runs at ~20 Hz, modest activity (WPF / WinUI dirty-rect updates) bumps
to ~30 Hz, heavy GPU activity (DirectX / Win2D full-canvas redraws) keeps
the display at full refresh. **Same content-commit rate, different
display refresh rate, different perceived smoothness.**

Confirmed via `tests/stress_perf/run_dwm_attribution_test.ps1` on
2026-05-02:

| Phase             | Total Present/s | Global VSync/s |
|-------------------|----------------:|---------------:|
| Idle baseline     | 1.8             | 21.7           |
| WPF @ 50%         | 6.3             | 29.3           |
| DirectX @ 50%     | 12.0            | **44.7**       |
| WPF @ 100%        | 4.6             | 19.7           |
| DirectX @ 100%    | 13.2            | **45.9**       |

The harness now records `GlobalVsyncPerSec` per scenario so this is
visible in every CSV. Always include it in framework comparisons:
**a higher Effective Refresh on a 60 Hz display vs. a lower one on a 30 Hz
display can swap which framework "feels" faster** even though both metrics
say one wins.

### How to make battery vs AC comparable

- For published numbers, run on AC (DRR pins to display max).
- If you must run on battery, pin `powercfg /setactive <high-perf GUID>`
  to minimize CPU/GPU throttling — DRR may still kick in.
- Always label CSVs and write-ups with the `PowerState` column.
- Never directly diff battery numbers against AC numbers; treat them as
  separate baselines.

## Per-tick latency: `Avg Update` (C#) vs `Avg Mount` (RN)

The synchronous variants (Direct / Bound / Wpf / DirectX / Reactor) bracket
the tick handler with a stopwatch — `BeginUpdate` before the property patch
+ reconcile, `EndUpdate` after — and report `Avg Update` ms. Because the
work runs on the UI thread synchronously, the bracket captures all of it:
data mutation, framework reconciliation, and any commit-to-tree work.

**RN-Fabric can't use that pattern.** `setState` returns immediately while
React reconcile → Fabric commit → Yoga → Composition continues across
other threads. A JS-side stopwatch around `setSnapshot` measures only JS
dispatch and undercounts the per-tick cost by a large factor.

For RN we report `Avg Mount` instead. The tracker stamps T0 just before
`setSnapshot` and records `(rAF-now − T0)` from a single
`requestAnimationFrame` scheduled inside a `useLayoutEffect` on the
dispatched state. By the time the rAF callback runs:

- React has finished its commit phase (useLayoutEffect ran)
- Fabric has had a chance to apply the commit to the host tree
- One display frame has been scheduled

It's a **pure-JS proxy**, not pixel-accurate. It excludes any Fabric work
that lands after the rAF tick (e.g. layout follow-ups in subsequent
frames). For true JS-to-pixel mount time, hook the native side per the
[RNW Fabric perf wiki, Part 2](https://github.com/microsoft/react-native-windows/wiki/Performance-tests-Fabric#part-2--native-perf-tests).

**Don't diff `Avg Update` against `Avg Mount`.** They bracket different
work. The harness reports them in separate columns (`InAppAvgUpdateMs`
for C#, `InAppAvgMountMs` for RN) for that reason.

## Memory: in-app `usedJSHeapSize` vs harness `WorkingSet64`

Each variant's PerfTracker can read process memory locally, but the only
in-process API exposed to RN/Hermes is `performance.memory.usedJSHeapSize`
— **JS heap only**. It excludes:

- Hermes engine
- JSI bridge
- Fabric reconciler + shadow tree
- Yoga
- TypeLayout / text-shaping caches

These are tens-to-hundreds of MB of fixed cost RN pays before any cells
exist. C# variants don't have an equivalent fixed cost. Reading
`usedJSHeapSize` and comparing it to a C# variant's `WorkingSet64` would
massively under-report RN.

Because of this, **the harness samples `WorkingSet64` externally for every
variant** (see `run_stocks_grid_baseline.ps1`'s polling loop) and that's
the figure published as `PeakRssMB`. RN's PerfTracker still emits a
per-second JS-heap series into its samples CSV under a `JsHeap_MB` column
header, but the human-readable report omits it — the only authoritative
memory column is the harness's `PeakRssMB`.

When citing RN memory numbers, separate **engine-baseline** from
**per-cell**: a 0-cell (or empty-tree) run gives the fixed cost; the
delta from the loaded run is per-content cost. The published baseline
report's RN row mostly reflects engine-baseline — note that explicitly
when comparing.

## Allocation: `Alloc/render` + Gen0/1/2 (C# variants)

Each C# `PerfTracker` also brackets the measured render loop with
`GC.GetTotalAllocatedBytes()` and `GC.CollectionCount(0/1/2)`, snapshotted on the
first render (so process-startup allocations are excluded) and read again at
report time. Every `*.report.txt` now carries three extra lines:

```
Alloc/render: <managed bytes allocated per render>
GC Gen0/1/2: <gen0> / <gen1> / <gen2>      (collections during the run)
Gen0/Krender: <gen0 collections per 1,000 renders>
```

These are the **sensitive signal for allocation-reduction work**: the mean-ms and
working-set figures barely move when a PR removes per-render allocations, but
`Alloc/render` and `Gen0/Krender` track it directly. The `/perf` CI harness
parses these and reports a Reactor `main`-vs-PR allocation table with the same
paired 95%-CI gating as the timing metrics — see
[`ci/README.md`](ci/README.md#the-comment). They are C#-only (no Rust/RN
equivalent is collected) and absent in a harness built before the metric landed.

> **Statistical gating note.** The `/perf` comparison reports the **median of 12
> paired runs** per side and flags a metric as a win/regression only when the
> **95% confidence interval of the paired delta excludes 0** — not a fixed
> percentage floor. A 2-run median cannot resolve the ~1–3% deltas these PRs
> target; its run-to-run swing is larger than the effect. Full rationale in
> [`ci/README.md`](ci/README.md#variance-trust-the-delta-not-the-absolutes).

## Low-mutation skip-floor: isolating the O(n) child skip-walk

The headline macro leg runs at `--percent 50` (half the cells mutate each tick).
At that mutation rate the per-tick cost is dominated by the *changed* cells, and a
large fixed cost is **diluted**: `ChildReconciler` re-walks **all** children every
tick to find what moved, an O(n) pass that runs whether 1 cell changed or 500 did.
At 50% that skip-walk is a fraction of the work; a structural-skip optimization
(skip untouched child ranges) barely moves the headline number.

So `/perf` runs a **second interleaved A/B leg at `--percent 0`** and reports it as
its own *low-mutation skip-floor* table. At 0% the workload still mutates exactly
one cell per tick — `StockDataSource.Update` clamps the change count to
`Math.Max(1, …)` — so virtually every child is unchanged and reconcile/diff time
*is* the skip-walk floor. That makes the floor the whole signal instead of a diluted
fraction, which is what lets a structural-skip PR's win clear a paired-Δ CI. It uses
the same interleaving, reps, warm-up, and 95%-CI gating as the headline table (so
each leg's delta independently cancels time-correlated drift); it is opt-out via
`-IncludeSkipFloor $false`. See
[`ci/README.md`](ci/README.md#the-comment).

## Keyed-list workload: the keyed child-diff path StocksGrid never hits

The StocksGrid macro workload (`StressPerf.ReactorOptimized`) renders a fixed grid
of cells mutated **in place by index**. Its child diff therefore always takes
`ChildReconciler.ReconcilePositional` — the positional re-walk. It never exercises
the reconciler's **keyed** arm, so keyed-diff optimizations (the keyed-list LIS
diff, keyed structural-skip) are invisible to it *by construction* — the same blind
spot that made the original headline-only comparison unable to resolve them.

So `/perf` runs a **third interleaved A/B leg** on `StressPerf.KeyedList`: a ~500-row
list of **stably keyed** children that are reordered / inserted / removed each tick.
Because every child carries a key, the child reconciler takes its keyed arm
(`ReconcileKeyed` → `ReconcileKeyedMiddle`, the LIS-based minimal-move pass) and runs
a real keyed diff every tick. The workload is deterministic (fixed RNG seed, constant
row count — insertions paired with removals) so `main` and PR compare identical edit
sequences, and its rows' labels are content-stable so a moved row's text never changes
— isolating the **structural** (keyed-diff) signal from per-cell property updates. It
reports the four headline metrics in its own table, plus an **allocation** sub-table
(`Alloc bytes/render`, `Gen0 GC / 1k renders`) — the sensitive macro signal for
keyed-diff *allocation* reductions the positional StocksGrid alloc table can't isolate,
rendered only when the keyed leg reports the metric — all under the same interleaving,
reps, warm-up, and 95%-CI gating as the headline leg, and is opt-out via
`-IncludeKeyedList $false`. See
[`ci/README.md`](ci/README.md#the-comment).

## Reconciler micro-benchmarks: ns-resolution Core path

Every metric above is measured **across a live WinUI render pipeline**, which is
the right scope for "what does the app feel like" but the wrong scope for the
**Core/Reconciler** layer most perf work targets. The StocksGrid macro workload is
render-bound (renders/sec is ~76% gated by the render thread) and its reconcile/ms
and alloc figures are diluted by render + working-set noise — small per-reconcile
deltas are unresolvable there. (The StocksGrid cells also render through a native
`Grid` with fixed tracks, structurally separate from FlexPanel/Yoga, so layout-engine
optimizations don't surface in this workload at all.)

For that layer the repo ships a dedicated micro-suite,
[`tests/perf_bench/PerfBench.ControlModel`](../perf_bench/PerfBench.ControlModel)
(spec-047 M1–M13). It runs the production reconciler as a **headless loop whose
measured region brackets only the reconcile body** — no render pipeline — with
`Stopwatch` for **ns/op** and the **per-thread** `GC.GetAllocatedBytesForCurrentThread()`
for **bytes/op** (per-thread, so WinUI and background-thread allocations are
excluded). That makes it ns-resolution and free of the dilution above.

`/perf` builds and runs this suite once per side — `main` and the PR each link
their own `src/Reactor` — and reports a per-bench `main`-vs-PR table; see
[`ci/README.md`](ci/README.md#reconciler-micro-benchmarks-ns-resolution-winui-undiluted).
The two metrics are read differently. **Allocated bytes/op is deterministic** for
identical code — an unchanged diff reproduces the byte count exactly — so its paired
95% CI is trustworthy and **drives each row's flag**. **ns/op is rep-interleaved but
not auto-flagged by default.** The two per-side runs are interleaved at the **rep
level** — each rep alternates a fresh `main` then PR process — so the
process-to-process timing offset (thermal/scheduling drift) that a single back-to-back
pair of invocations leaves as a *constant* bias is instead randomized round-to-round
into the paired variance, making the ns paired CI unbiased. This matters because
before interleaving that offset shifted every paired ns difference the same way and
made the paired CI exclude 0 even for an identical binary: running the **same**
ControlModel binary as both sides, alloc was deterministic (14/16 benches exactly
0.0% Δ) while ns spuriously flagged up to −14.8% on a no-op. Even interleaved, ns
carries residual cold-JIT / scheduling jitter, so promoting it to a flag is gated
behind a minimum-effect band **and** a master switch (`$MicroNsAutoFlag`) that stays
**dormant** pending a real-CI identical-binary calibration of that band — which can
only run once the interleave is on `main`, since `/perf` builds the harness from the
default branch. Arming the switch is a measurement-only follow-up — it changes verdict
labels, never what merges. While dormant the row flag tracks allocated bytes/op (v1
behaviour).

It is the **authoritative instrument** for per-reconcile allocation deltas (and,
once the ns flag is armed, reconcile-time deltas); the macro tables remain the
user-facing throughput sanity check.

## Startup / first frame: the from-scratch mount the steady-state windows exclude

Every table above measures the **steady state** — its alloc/timing windows baseline
on the *first* benchmark tick, so the cost of building the whole element tree, creating
every WinUI control, and the first layout (the from-scratch **mount**) is excluded by
construction. That mount is exactly where a `#696`-class regression lives, and nothing
else in the harness sees it. `StressPerf.ReactorOptimized` therefore captures four
**startup** anchors once per process (`StressPerf.Shared/StartupTiming.cs` marks managed
entry at the top of `Main`; `PerfTracker.RecordFirstRenderIfUnset` records the first
`OnRenderComplete`; `FrameRendered` records the first composed frame after it):

```
firstReconcileDurationMs       <- first OnRenderComplete's reconcile/diff-patch arg
entryToFirstReconcileMs        <- managed entry -> first reconcile complete
windowOpenToFirstReconcileMs   <- window Activated -> first reconcile (n/a-guarded)
entryToFirstFrameMs            <- managed entry -> first composed frame ("first frame rendered")
```

`firstReconcileDurationMs` is the **Reactor-isolated** signal: it is the first render's
reconcile-phase *duration* (the same phase the steady-state **Avg Diff** column averages),
undiluted by AOT runtime init, window creation, or XAML resource load — so a 2× mount
regression shows up as a 2× number here, where it would be a single-digit-% blip diluted
into the bootstrap-dominated `entryToFirstFrameMs`. The mount value is much larger than
steady-state Avg Diff because it creates every control rather than patching a few.
`entryToFirstFrameMs` is the human-recognisable "first frame rendered" number.
`windowOpenToFirstReconcileMs` is **n/a-guarded**: it is emitted only when the window's
`Activated` event demonstrably preceded the first mount (else JSON `null` -> n/a, never a
negative number that would poison a paired CI). In the current `ReactorWindow` lifecycle the
host calls `Mount(...)` (which completes the first reconcile synchronously) *before*
`Activate()`, so `Activated` always fires after the mount and the guard structurally rejects
this anchor — it is effectively always n/a here, and the `/perf` renderer omits its row
whenever it is n/a on both sides. It is retained in the JSON contract for any future host or
launch ordering where `Activated` can win the race; the entry-based anchors are the robust
signals that always have a value.

These piggyback the headline per-rep ReactorOptimized launches — **one sample per process,
so the cold first launch is dropped with the warmup rep** (startup rides the same per-rep
metrics object the interleave loop already warmup-drops) — and reuse the same paired 95% CI
machinery with **zero extra CI time**. The `/perf` comment renders a *Startup / first frame*
table directly under the regression table. Like the micro ns flag the startup flag
(`$StartupAutoFlag`) ships **dormant** / informational-only: one sample per launch plus
bootstrap process-to-process variance (AOT init, OS file cache, thermal) make this the
noisiest axis, so the Δ + CI are reported but no row is auto-flagged better-or-worse until
the band is calibrated from a real-CI identical-binary A/B (the same discipline as the ns
flip; measurement-only, never changes what merges). Lineage matches the alloc fields: a
harness built from a revision that predates the metric reads *n/a* (so on the metric's own
introducing run the `main` baseline shows n/a and the paired Δ populates on the next run),
surfaced with a visible note rather than a silent gap.

## Don'ts (so we don't redo this analysis)

1. **Don't trust `CompositionTarget.Rendering` for "FPS."** It's UI-thread-
   idle-vsync, not present-rate. Always 2× too high under load.
2. **Don't trust `requestAnimationFrame` for "FPS" in RN.** It's JS-thread
   tick rate. Under-reports at light load, bursty at saturation.
   2a. **Don't bracket `setState` with a JS stopwatch and call it "update
   time" in RN.** The dispatch returns immediately; the commit pipeline
   continues across other threads. Use the rAF-after-commit `Avg Mount`
   proxy or hook native per the RNW Fabric perf wiki. See above.
   2b. **Don't read `performance.memory.usedJSHeapSize` and compare it to
   a C# variant's working set.** JS heap excludes Hermes, JSI, Fabric,
   Yoga, and text caches — tens-to-hundreds of MB of RN-fixed cost. Use
   `WorkingSet64` from the harness for any cross-framework number.
3. **Don't trust DwmCore VSync events filtered by PID.** Vsyncs are global;
   the per-PID attribution is heuristic and only fires when our app's
   swap chain is the signal target. For "OS still presents at 60Hz when
   busy" hypothesis testing, capture VSync events *unfiltered* and look
   at totals across all PIDs.
4. **Don't trust `DxgKrnl::Render`.** It's GPU-render-packet count —
   correlates with GPU work, not with frame-presented-to-display rate.
   RN-Fabric pushes ~150 of these per second across all workloads
   regardless of whether content changed.
5. **Don't compare battery and AC numbers without flagging it.** DRR
   makes the display refresh rate itself a function of which framework
   is running. See above.
