# StressPerf: WinUI 3 Update Strategy Comparison

## Goal

Compare three approaches to updating a large grid of UI elements in WinUI 3,
measuring FPS and memory under identical workloads.

## Grid Layout

- **80 columns x 60 rows = 4,800 cells**
- Each cell displays: `SYMBOL $PRICE` (e.g. `AAB $142.37`)
- Cell foreground color: **green** if price went up, **red** if it went down
- Fixed cell size: 76 x 22 px, FontSize 10
- Grid is wrapped in a ScrollViewer

## Data Model

Each cell maps to a `StockItem`:

| Field        | Type   | Description                          |
|------------- |--------|--------------------------------------|
| Symbol       | string | 3-letter ticker (deterministic)      |
| PrevPrice    | double | Price before the last update         |
| CurrentPrice | double | Current price                        |
| IsUp         | bool   | true if CurrentPrice >= PrevPrice    |

Symbols are generated deterministically from row/column indices so all three
apps display identical data.

## Update Loop

1. A `DispatcherTimer` fires every **33 ms** (~30 Hz target).
2. On each tick, **N%** of the 4,800 items are randomly mutated
   (price changes by up to +/-2% of current value, biased slightly upward).
3. The UI is updated according to the variant's strategy.
4. `CompositionTarget.Rendering` counts actual composed frames for FPS.

## Three Variants

### 1. Direct (`StressPerf.Direct`)

Baseline — minimal overhead:

- Build a WinUI `Grid` with 80 columns / 60 rows at startup.
- Store a `TextBlock[]` of 4,800 references.
- On tick: loop over changed indices, set `TextBlock.Text` and
  `TextBlock.Foreground` directly.

### 2. Data-Bound (`StressPerf.Bound`)

Standard MVVM pattern:

- Build identical grid at startup.
- Each `TextBlock` is bound (`Binding`, `OneWay`) to a
  `StockItemViewModel : INotifyPropertyChanged`.
- ViewModel exposes `DisplayText` (string) and `PriceBrush` (SolidColorBrush).
- On tick: set ViewModel properties; the binding engine propagates changes.

### 3. Reactor Functional (`StressPerf.Reactor`)

Reactor's declarative reconciliation:

- A `Component` holds `StockItem[]` in `UseState`.
- `Render()` produces 4,800 `Text(...)` elements inside a `Grid(...)`.
- On tick: call `setData(newArray)` — the Reactor reconciler diffs old vs new
  element trees and patches only changed `TextBlock` controls.

This is the **naive** Reactor variant: plain fluent API, no memo, no
direct-initializer tricks. It is what an unaware user writes and stays
that way permanently — it's the framework-level baseline the spec-034
table compares the optimized variant against.

### 4. Reactor Perf-Tuned (`StressPerf.ReactorOptimized`)

Same workload, same UI, same `Component`/`Render()` shape, but the
inner-loop cell construction follows the spec-034 §B/§C idioms:

- Cells are built via direct `new TextBlockElement { … }` initializer
  with `Modifiers = new ElementModifiers { Layout = …, Visual = … }`
  buckets — bypasses the `with`-clone steps the fluent chain produces.
- `UseMemoCellsByIndex` (Phase 4 of spec 034) skips re-running the
  builder for unchanged indices, so a 10% mutation tick allocates ~10%
  of the cells the naive variant does.

This is the **optimized** Reactor variant — the reference implementation
of the spec-034 perf-tips skill. Do not adopt this shape for ordinary
UI; restrict it to identifiable hot loops (lists/grids with hundreds-plus
elements per render).

## Interactive Mode (default)

Each app opens a window with:

- **Slider** (0–100%) controlling the percentage of items updated per tick.
- **Start / Stop** toggle button.
- **FPS** and **Memory** readout (updated every second).
- The stock grid below the controls.

## CLI / Headless Mode

```
StressPerf.Direct.exe --headless --percent 50 --duration 10
```

| Flag         | Default | Description                              |
|------------- |---------|------------------------------------------|
| `--headless` | off     | Auto-start, run for duration, print report, exit |
| `--percent`  | 10      | Percentage of cells updated per tick     |
| `--duration` | 10      | Seconds to run before reporting          |

### Report Output (stdout)

```
=== StressPerf.Direct ===
Duration:    10.0s
Percent:     50%
Avg FPS:     31.2
Min FPS:     28.4
Max FPS:     33.1
Avg Update:  2.3 ms
Max Update:  5.1 ms
Avg Memory:  142.3 MB
Peak Memory: 158.7 MB
```

## Perf Harness (`StressPerf.Shared`)

Shared library consumed by all three apps:

| Class             | Responsibility                                      |
|-------------------|-----------------------------------------------------|
| `StockDataSource` | Generates and mutates 4,800 StockItems              |
| `PerfTracker`     | FPS counter, update-time recorder, memory sampler    |
| `CliOptions`      | Parses `--headless`, `--percent`, `--duration`       |
| `ConsoleHelper`   | Attaches parent console for stdout in WinExe builds  |

## Build & Run

WinUI variants (`Direct`, `Bound`, `DirectX`, `Reactor`,
`ReactorOptimized`, `ReactorGrid`, `VirtualList.Reactor`) target
`net10.0-windows10.0.22621.0` and pick up the repo-wide
`WindowsAppSDKVersion` from `Directory.Build.props`. The WPF variant
targets `net10.0-windows`; `PresentTracer` is plain `net10.0`. The
shared scaffold (`StressPerf.Shared`) is `net10.0-windows`.

```bash
# Build all
dotnet build tests/stress_perf/StressPerf.Direct -c Release -p:Platform=x64
dotnet build tests/stress_perf/StressPerf.Bound  -c Release -p:Platform=x64
dotnet build tests/stress_perf/StressPerf.Reactor   -c Release -p:Platform=x64
dotnet build tests/stress_perf/StressPerf.ReactorOptimized -c Release -p:Platform=x64

# Run interactive
dotnet run --project tests/stress_perf/StressPerf.Direct -c Release -p:Platform=x64

# Run headless benchmark
dotnet run --project tests/stress_perf/StressPerf.Direct -c Release -p:Platform=x64 -- --headless --percent 50 --duration 10
```
