# startup_perf — event spec

Each variant emits a sequence of ETW events on the
`BenchmarkSyntheticApps` provider (`FD80D616-E92B-4B2B-9BED-131ADA36A8FD`).
Events match `microsoft-ui-xaml-lift/Samples/FrameworkBenchmarkBlankApps`
exactly so the same WPR Regions XML resolves both.

## Events

All events carry these payload fields:

- `AppName` (string) — `blank_winui3` / `blank_reactor` / `blank_rnw`.
  Used by Regions XML's `<Naming><PayloadBased NameField="AppName"/>` so
  variants split into separate region rows in WPA.
- `Seq` (uint64) — monotonic per process.
- `Pid` (uint32) — `GetCurrentProcessId()`.

| Event           | Variant   | When                                                                                   |
| --------------- | --------- | -------------------------------------------------------------------------------------- |
| `wWinMainEntry` | all       | first managed/native instruction in `Main` / `wWinMain`                                |
| `XamlAppLoaded` | all       | `Application.Start` callback / `OnLaunched` / first `Render()` (Reactor)               |
| `WindowLoaded`  | all       | `MainWindow` constructor (WinUI3) / first post-commit `UseEffect` (Reactor) / `appWindow` configured + before `Start` (RNW) |
| `JSBundleLoaded` | RNW only | `InstanceSettings.InstanceLoaded` callback (Hermes loaded + bundle evaluated)          |
| `ReactMounted`  | RNW only  | from JS, in `useEffect(…, [])` of the root component                                   |
| `FirstRender`   | all       | first display frame after the root content commits (one-shot `CompositionTarget.Rendering` for WinUI3/Reactor; computed in JS for RNW) |
| `FirstIdle`     | all       | dispatcher Low-priority callback after `FirstRender`                                   |
| `ProcessStop`   | all       | app exit                                                                                |

## Region intervals (resolved by -lift's Regions XML)

| Region                                  | Start            | Stop             | Meaning                                                  |
| --------------------------------------- | ---------------- | ---------------- | -------------------------------------------------------- |
| `BlankBenchmark_ProcessLifetime`        | `wWinMainEntry`  | `ProcessStop`    | total process lifetime                                   |
| `BlankBenchmark_StartToFirstRender`     | `wWinMainEntry`  | `FirstRender`    | **TRUE TTFP** — process entry → first paint              |
| `BlankBenchmark_StartToFirstIdle`       | `wWinMainEntry`  | `FirstIdle`      | **TRUE TTI** — process entry → interactive               |
| `BlankBenchmark_RenderToIdle`           | `FirstRender`    | `FirstIdle`      | post-paint settle time                                   |
| `BlankBenchmark_StartToWindow`          | `wWinMainEntry`  | `WindowLoaded`   | host bootstrap                                           |
| `BlankBenchmark_WindowToFirstRender`    | `WindowLoaded`   | `FirstRender`    | XAML compose + first frame                               |
| `BlankBenchmark_WindowToJSBundle`       | `WindowLoaded`   | `JSBundleLoaded` | RNW: Hermes init + bundle eval                           |
| `BlankBenchmark_JSBundleToReactMount`   | `JSBundleLoaded` | `ReactMounted`   | RNW: React reconciler first commit                       |
| `BlankBenchmark_StartToReactMounted`    | `wWinMainEntry`  | `ReactMounted`   | RNW: end-to-end through React commit                     |

Our `Tracing.wprp` is intentionally slim: only the
`BenchmarkSyntheticApps` provider, no kernel or XAML providers. That
keeps ETLs tiny and parsing fast (`tracerpt` decodes them in <1 s) at
the cost of starting measurement at `wWinMainEntry` rather than at the
kernel-level `ProcessStart`. If you need sub-WinMain accuracy, capture
with -lift's full `Common/Tracing.wprp` instead — same provider GUID,
so our regions still resolve.

## What each variant approximates from -lift's MainWindow flow

```
-lift WinUI3 (C++):
  wWinMain
    → Tracing::Register
    → Tracing::TraceWinMainEntry
    → Application::Start
        → App::OnLaunched           [Tracing::TraceXamlAppLoaded]
        → MainWindow::Initialize    [Tracing::TraceWindowLoaded; metrics.RecordAppStart]
            → RootGrid.Loaded
                → CompositionTarget::Rendered (one-shot)
                    [metrics.RecordFirstFrame → TraceFirstRender]
                    → DispatcherQueue.Low
                        [metrics.RecordInteractive → TraceFirstIdle]
```

```
BlankWinUI3 (C# AOT):
  Main()
    [Metrics.RecordAppStart; BenchmarkTracing.TraceWinMainEntry]
    → Application.Start
        → App.OnLaunched            [BenchmarkTracing.TraceXamlAppLoaded]
        → MainWindow ctor           [BenchmarkTracing.TraceWindowLoaded]
            → rootGrid.Loaded
                → CompositionTarget.Rendering (one-shot)
                    [Metrics.RecordFirstFrame → TraceFirstRender]
                    → DispatcherQueue.Low
                        [Metrics.RecordInteractive → TraceFirstIdle]
```

```
BlankReactor (C# AOT):
  Main()
    [Metrics.RecordAppStart; BenchmarkTracing.TraceWinMainEntry]
    → ReactorApp.Run<BlankApp>
        → first BlankApp.Render()   [TraceXamlAppLoaded — fired once]
        → first UseEffect post-commit
            [TraceWindowLoaded]
            → CompositionTarget.Rendering (one-shot)
                [Metrics.RecordFirstFrame → TraceFirstRender]
                → DispatcherQueue.Low
                    [Metrics.RecordInteractive → TraceFirstIdle]
```

```
BlankRNW (C++ + JS):
  WinMain
    [CaptureWinMainEntry; RNWAppTracingRegister; TraceWinMainEntry]
    → ReactNativeAppBuilder().Build()    [CaptureAppBuilt; TraceXamlAppLoaded]
    → InstanceSettings.InstanceLoaded    [TraceJSBundleLoaded]
    → appWindow configured               [TraceWindowLoaded]
    → CaptureBeforeStart; reactNativeWin32App.Start()
        → JS index.js T0 = Date.now()
        → App.tsx useEffect
            → NativeModules.StartupTiming.reportReactMounted()
                                         [TraceReactMounted]
            → requestAnimationFrame      [JS TTFP]
                → requestIdleCallback    [JS TTI]
                    → reportMetrics(trueTtfp, trueTti)
                                         [TraceTTFP / TraceTTI = TraceFirstRender / TraceFirstIdle]
```

The shared milestones (`wWinMainEntry`, `XamlAppLoaded`, `WindowLoaded`,
`FirstRender`, `FirstIdle`, `ProcessStop`) are the cross-stack contract.
The RNW-only events (`JSBundleLoaded`, `ReactMounted`) explain *where*
inside RNW's startup the time goes.
