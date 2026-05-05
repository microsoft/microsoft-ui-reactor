# startup_perf — minimal startup baseline

Three sibling apps that each start, paint once, and quit:

| Variant         | Stack                                | AppName payload  |
| --------------- | ------------------------------------ | ---------------- |
| `BlankWinUI3/`  | C# WinUI 3 (unpackaged)              | `blank_winui3`   |
| `BlankReactor/` | C# Reactor                           | `blank_reactor`  |
| `BlankRNW/`     | RN-Windows 0.82, C++ host + Hermes   | `blank_rnw`      |

All three emit ETW on **the same provider** as the
[`microsoft-ui-xaml-lift` blank-app benchmarks][lift]:

- Provider: `BenchmarkSyntheticApps`
  `{FD80D616-E92B-4B2B-9BED-131ADA36A8FD}`
- Keyword: `MICROSOFT_KEYWORD_MEASURES` (bit 46, `0x0000400000000000`)
- Events: `wWinMainEntry`, `XamlAppLoaded`, `WindowLoaded`, `JSBundleLoaded`
  (RNW only), `ReactMounted` (RNW only), `FirstRender`, `FirstIdle`,
  `ProcessStop`. Each carries an `AppName` string payload.

The provider GUID and event names are identical to -lift's so:

- The same WPR profile resolves both.
- The same Regions XML resolves both (`BenchmarkBlankApps.Regions.xml`
  in -lift). Region instances split by `AppName` payload, so `blank_rnw`
  captured here vs. `blank_rnw` captured in -lift land in the same WPA
  region row — directly comparable.

[lift]: https://github.com/microsoft/microsoft-ui-xaml-lift/tree/main/Samples/FrameworkBenchmarkBlankApps

## Why this exists

`tests/stress_perf` and `tests/stress_perf_rn` measure **steady-state**
behaviour (FPS, mount-ms, P50/P95/P99 frame deltas with a real workload).
On those, RNW shows large memory and frame-time regressions vs. the C#
Reactor sibling.

-lift's blank apps measure **startup only**. There, RNW (~300 ms) is
within ~2× of WinUI 3 (~100–150 ms) — close, not the orders-of-magnitude
gap we see in the workload tests.

This set lets us reproduce that calibration from inside this repo, and
adds peak working set per run so we can tell startup memory apart from
runtime growth.

## Build

```powershell
# All three apps. From repo root.
dotnet build tests/startup_perf/BlankWinUI3/BlankWinUI3.csproj   -c Release -p:Platform=ARM64
dotnet build tests/startup_perf/BlankReactor/BlankReactor.csproj -c Release -p:Platform=ARM64

# RNW: install once, then build via the RN CLI (or msbuild directly if VS
# version checks fail).
cd tests/startup_perf/BlankRNW
npm install
npx @react-native-community/cli run-windows --release --no-deploy --no-launch --arch ARM64
```

If the VS detection rejects your Visual Studio version, build the
solution directly with msbuild:

```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\arm64\MSBuild.exe"
& $msbuild tests\startup_perf\BlankRNW\windows\BlankRNW.sln `
    /p:Configuration=Release /p:Platform=ARM64 /restore /m /p:RestoreIgnoreFailedSources=true
```

If `nuget.org` is unreachable from your network (common in restricted
environments), add `--ignore-failed-sources` to `dotnet build`. Cached
packages under `%USERPROFILE%\.nuget\packages` are used as fallback.

## Run + capture (single variant)

```powershell
# Start the WPR session
wpr -start tests\startup_perf\Common\Tracing.wprp -filemode

# Launch one app, wait for the window to render, close it
.\tests\startup_perf\BlankWinUI3\bin\ARM64\Release\net9.0-windows10.0.22621.0\BlankWinUI3.exe

# Stop and view
wpr -stop run.etl
wpa run.etl
```

Or for a quick CLI view, parse with `tracerpt`:

```powershell
tracerpt run.etl -o run.xml -of XML -lr -y
```

The `wWinMainEntry` → `FirstRender` and `wWinMainEntry` → `FirstIdle`
deltas are TRUE TTFP / TRUE TTI.

## Run all three + median (admin shell required)

`run_startup_bench.ps1` cancels any orphaned WPR session, then for each
variant: `wpr -start`, launch the exe, wait for window, sample working
set every 50 ms, close, `wpr -stop`. Parses each ETL with `tracerpt`,
prints medians + writes per-run + summary CSVs.

```powershell
.\tests\startup_perf\run_startup_bench.ps1 -Runs 5
```

## Diagnostic / first-time validation

`diag.ps1` bypasses WPR (uses `logman` directly) and dumps every event's
task name + payload from each variant. Useful when first wiring up a new
variant or after editing the tracing code.

```powershell
.\tests\startup_perf\diag.ps1
```

## Sample results (ARM64 dev box, 5 runs, 1000×1000 windows)

| Variant   | XamlAppLoaded | WindowLoaded | TTFP    | TTI     | Peak WS     |
| --------- | ------------: | -----------: | ------: | ------: | ----------: |
| WinUI3    | 72.7 ms       | 172.4 ms     | 234.3   | 238.9   | 105.7 MB    |
| RNW       | 72.7 ms       | 73.4 ms      | 234.1   | 234.8   | 82.9 MB     |
| Reactor   | 240.6 ms      | 257.8 ms     | 294.8   | 297.2   | 114.1 MB    |

What this gives us:

- RNW within 0.2 ms of WinUI3 on TTFP at the same surface area —
  reproduces -lift's "very close at startup" claim and tightens it.
- RNW has the *smallest* working set at startup (83 MB vs 106–114 MB).
  Any RNW memory bloat seen in `tests/stress_perf_rn` is therefore
  runtime-side (React reconciler, JS heap growth, Yoga layout), not
  startup overhead. Useful narrowing for workload analysis.
- Reactor's framework cost is ~168 ms one-time (XamlAppLoaded delta
  over WinUI3) and ~8 MB of working set.

## Methodology notes

- **PublishAot is off** (`<PublishAot>false</PublishAot>` in both C#
  csprojs). NativeAOT trims the `EventSource` subclass even when events
  are declared via manifest-style `[Event]` attributes, producing zero
  ETW emissions. Non-AOT C# adds ~70–100 ms of CLR bootstrap cost vs.
  -lift's pure C++ WinUI 3 baseline. Relative comparisons inside this
  repo (BlankWinUI3 vs. BlankReactor vs. BlankRNW) are still
  apples-to-apples; the C++ RNW host doesn't pay this cost (Hermes
  bootstrap dwarfs it anyway). Calibrate the absolute number by running
  -lift's BlankRNW alongside ours on the same hardware.

- **Cold-start protocol.** Run-1 of each variant is typically a
  cache-cold outlier; the script reports median over N runs so this
  doesn't skew results.

- **Self-describing TraceLogging.** C# uses `EventSource.Write()` with
  an `[EventData]` payload struct, the same pattern -lift's
  `Common/BenchmarkTracing.cs` uses, so tracerpt resolves event names
  and payload fields without an installed manifest. RNW uses C++
  `TraceLoggingProvider` macros directly.

- **What to expect on the same hardware as -lift's README.** -lift
  quotes ~100–150 ms WinUI 3 (C++) / ~300 ms RNW. Our BlankWinUI3 will
  read ~70–100 ms higher because of CLR bootstrap. BlankRNW should be
  within ~10 % of -lift's RNW number on the same machine — that's the
  calibration check.

## Files

```
tests/startup_perf/
├── README.md                 (this file)
├── SPEC.md                   event-by-event description of the pipeline
├── Common/
│   ├── BenchmarkTracing.cs   C# EventSource (used by BlankWinUI3 + BlankReactor)
│   ├── BlankPerfMetrics.cs   C# AppStart/FirstFrame/Interactive holder
│   └── Tracing.wprp          WPR capture profile (single provider, slim)
├── BlankWinUI3/              C# WinUI 3 minimal blank app
├── BlankReactor/             C# Reactor minimal blank app
├── BlankRNW/                 RN-Windows 0.82 minimal blank app
│   ├── App.tsx               JS-side T0 → rAF → idleCallback pipeline
│   ├── windows/BlankRNW/
│   │   ├── BlankRNW.cpp      WinMain QPC checkpoints + ETW
│   │   ├── RNWAppTracing.h   provider definition + capture vars
│   │   └── StartupTimingModule.h  TurboModule that bridges JS↔ETW
│   └── ...
├── run_startup_bench.ps1     captures N runs of each variant, median
└── diag.ps1                  per-event payload dump for first-wire validation
```
