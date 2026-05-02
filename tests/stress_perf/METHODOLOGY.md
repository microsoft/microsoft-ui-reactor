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

## Don'ts (so we don't redo this analysis)

1. **Don't trust `CompositionTarget.Rendering` for "FPS."** It's UI-thread-
   idle-vsync, not present-rate. Always 2× too high under load.
2. **Don't trust `requestAnimationFrame` for "FPS" in RN.** It's JS-thread
   tick rate. Under at light load, bursty at saturation.
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
