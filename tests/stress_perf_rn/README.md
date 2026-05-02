# StressPerf — React Native ports

Two `react-native-windows` apps that match the C# StressPerf demos for
apples-to-apples framework comparison. Both render into the same
XAML / WinUI 3 host the C# variants use — react-native-web (DOM) would
not be a fair comparison.

```
tests/stress_perf_rn/
├── README.md          ← you are here
├── SPEC.md            ← scenario specifications (shared with the Reactor sibling)
├── StocksGrid/        ← matches StressPerf.Reactor (70×70 stocks grid w/ mutation)
│   ├── App.tsx
│   ├── StockDataSource.ts
│   ├── PerfTracker.ts
│   ├── index.js, app.json, package.json, tsconfig.json, …
│   └── windows/       ← C++/WinRT app shell (.sln, .vcxproj, manifest)
└── VirtualList/       ← matches StressPerf.VirtualList.Reactor (5k-row list scroll)
    └── (same shape)
```

Versions: **RN 0.82, react-native-windows 0.82.5, Fabric (New
Architecture) on by default**. `node_modules/` and build outputs are
gitignored; `package-lock.json` and the generated `windows/` folder
are committed (they're the project shell, not transient).

For the methodology these apps participate in, see
`tests/stress_perf/METHODOLOGY.md`. For the latest baseline results,
see `docs/reports/stress-perf-stocks-grid.md`.

## One-time prerequisites

The C# variants build with just `dotnet build`. The RN variants need
**MSBuild + the C++/WinRT workload** because react-native-windows
0.82's `cpp-app` template boots into a native `.exe` host that loads
the JS bundle. The `Desktop development with C++` and `Universal
Windows Platform development` workloads in VS plus the Windows 11 SDK
are sufficient.

If something's missing, `react-native-windows` ships a checker /
installer:

```powershell
# From an elevated PowerShell, one-time:
& "C:\Users\andersonch\Code\reactor3\tests\stress_perf_rn\StocksGrid\node_modules\react-native-windows\scripts\rnw-dependencies.ps1" -Install -NoPrompt
```

It detects what's missing and installs only the delta.

> **Note for VS 2026 (VS 18.x):** `react-native-windows` 0.82's CLI is
> pinned to VS `[17.11, 18.0)`. We ship a one-line patch to widen that
> range to `[17.11, 19.0)` in
> `node_modules/@react-native-windows/cli/lib-commonjs/utils/vsInstalls.js`
> (the `major + 1` → `major + 2` change). `npm install` re-applies the
> stock CLI; you'll need to re-apply the patch after dependency
> changes. Upstream tracks the fix.

## Running

### Interactive

```powershell
cd tests/stress_perf_rn/StocksGrid    # or VirtualList
npm run windows
```

Window comes up maximized with toolbar controls (Start / percent
buttons / etc.).

### Headless — driven by CLI args, parsed in the C++ host

The native host (`windows/StocksGrid/StocksGrid.cpp`) parses
`argv` and forwards the parsed values to JS as **initial props** on
the React root:

```powershell
# Inside e.g. tests/stress_perf_rn/StocksGrid/, after building:
.\windows\ARM64\Release\StocksGrid.exe --headless --percent 50 --duration 10

# Same shape for VirtualList:
.\windows\ARM64\Release\VirtualList.exe --headless --count 5000 --duration 5
```

App.tsx receives `headless` / `percent` / `duration` (or `count`) as
component props. When `headless=true` the bench auto-starts, runs for
the duration, and renders the final report into an on-screen
`<Text testID="HeadlessReport">` block. The harness scrapes that via
UI Automation. (We don't write a report file — RN doesn't ship a
synchronous file API, and adding `react-native-fs` would mean another
native module dependency.)

> Why CLI args instead of `process.env`? RN bundles `process.env`
> references **at compile time** via the babel-preset, not runtime —
> so env vars set before launch never reach the JS code. The C++ host
> route is the proper way to pass runtime config.

### Driven by the parent harness

The full matrix runner at
`tests/stress_perf/run_stocks_grid_baseline.ps1` knows about both RN
apps. It launches them with the right CLI args, runs PresentTracer
concurrently, and scrapes the on-screen report. No env-var dance
needed; just run the harness elevated and the RN runs are part of the
output CSV.

## Notes on fairness

- Both apps render into XAML / WinUI 3 via the WindowsAppSDK
  Composition layer.
- Both use deterministic data from matching item-generation algorithms
  in C# (`StressPerf.Shared.{StockDataSource, ListItemSource}`) and TS
  (`StocksGrid/StockDataSource.ts`,
  `VirtualList/ListItemSource.ts`).
- The RNG used to seed initial stock prices isn't byte-identical
  between C# `Random(42)` and the TS LCG, but the workload shape (cell
  count, mutation count, ±2% delta math) is — which is what the perf
  comparison actually measures.
- The 4,900-cell grid is intentionally a stress test, well past
  saturation for any of the frameworks. Real apps have 10–100× smaller
  trees and reach 60 fps everywhere. See
  `docs/reports/stress-perf-stocks-grid.md` for context on what this
  scenario stresses and what conclusions are valid from it.
