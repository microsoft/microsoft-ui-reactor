// StressPerf.KeyedList — a /perf macro workload that exercises Reactor's KEYED
// child-reconciliation path (ChildReconciler.ReconcileKeyed →
// ReconcileKeyedMiddle, the LIS-based minimal-move machinery) that the existing
// StocksGrid workload (StressPerf.ReactorOptimized) can never reach.
//
// StocksGrid's cells are POSITIONAL: a fixed Grid of N cells mutated in place by
// index, so its child diff always takes ChildReconciler.ReconcilePositional. This
// harness instead renders a list of N stably-KEYED children (each carries a
// `.WithKey(...)`) and, every tick, REORDERS / INSERTS / REMOVES them — forcing
// the keyed arm and its LIS reorder pass. Several reconciler optimizations target
// exactly that path (keyed-list diff, keyed structural-skip); /perf needs this
// workload to measure them.
//
// The harness contract is mirrored byte-for-byte from StressPerf.ReactorOptimized:
// the same CLI flags (--headless / --percent / --duration / --json via the shared
// CliOptions), the same shutdown emission (report.txt + metrics.json + the
// REACTOR_PERF_JSON stdout line) via the shared PerfTracker, and the same
// OnRenderComplete phase-capture wiring. Only the SCENE and its per-tick mutation
// differ, so Run-PerfBenchmark.ps1 / PerfLib.ps1 can drive it identically.

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using StressPerf.KeyedList;
using StressPerf.Shared;
using static Microsoft.UI.Reactor.Factories;

// Parse CLI args before WinUI starts
var cliOptions = CliOptions.Parse(args);
if (cliOptions.Headless)
    ConsoleHelper.EnsureConsole();

KeyedListApp.CliOpts = cliOptions;

// Children are built via the direct record initializer (`new TextBlockElement(...)`)
// to avoid factory overhead in the hot path, which bypasses the factory's lazy
// handler registration. Opt into the full built-in catalog once at startup so every
// built-in element record has a registered handler before the first reconcile —
// the documented one-line prelude for the direct-record idiom (spec-048 §3.4),
// identical to StressPerf.ReactorOptimized.
ReactorApp.RegisterAllBuiltIns();

ReactorApp.Run<KeyedListApp>("StressPerf.KeyedList", fullScreen: true);

// ---------------------------------------------------------------------------

class KeyedListApp : Component
{
    private const string AppName = "StressPerf.KeyedList";

    public static CliOptions CliOpts { get; set; } = new();

    public override Element Render()
    {
        var sourceRef = UseRef<KeyedListSource?>(null);
        if (sourceRef.Current == null)
            sourceRef.Current = new KeyedListSource();
        var source = sourceRef.Current;

        // The full keyed snapshot drives a complete child-array rebuild every render
        // (deliberately NO positional memo fast-path), so the child reconciler runs a
        // real keyed diff each tick.
        var (data, setData) = UseState(source.Snapshot());

        var (percent, setPercent) = UseState(CliOpts.Percent);
        var (running, setRunning) = UseState(false);
        var (fps, setFps) = UseState("FPS: --");
        var (updateMs, setUpdateMs) = UseState("Update: -- ms");
        var (mem, setMem) = UseState("Mem: -- MB");

        var perfRef = UseRef<PerfTracker?>(null);
        var timerRef = UseRef<DispatcherTimer?>(null);
        var shutdownRef = UseRef<DispatcherTimer?>(null);
        var benchmarkUpdatePending = UseRef(false);
        var keysVerifiedRef = UseRef(false);

        if (perfRef.Current == null)
        {
            perfRef.Current = new PerfTracker();
            var perf = perfRef.Current;
            var pending = benchmarkUpdatePending;
            ReactorApp.PrimaryWindow!.Host.OnRenderComplete = (treeMs, reconcileMs, effectsMs) =>
            {
                if (pending.Current)
                {
                    pending.Current = false;
                    perf.RecordPhases(treeMs, reconcileMs, effectsMs);
                }
            };
        }

        var renderHooked = UseRef(false);
        if (!renderHooked.Current)
        {
            renderHooked.Current = true;
            var perf = perfRef.Current;
            CompositionTarget.Rendering += (_, _) => perf.FrameRendered();
        }

        UseEffect(() =>
        {
            if (running)
            {
                var src = sourceRef.Current!;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                timer.Tick += (_, _) =>
                {
                    var perf = perfRef.Current!;
                    perf.BeginUpdate();

                    src.Update(percent);
                    benchmarkUpdatePending.Current = true;
                    setData(src.Snapshot());

                    perf.EndUpdate();

                    setFps($"FPS: {perf.CurrentFps:F0}");
                    setUpdateMs($"Update: {perf.LastUpdateMs:F1} ms");
                    setMem($"Mem: {perf.CurrentMemoryMB} MB");
                };
                timer.Start();
                timerRef.Current = timer;
            }
            else
            {
                timerRef.Current?.Stop();
                timerRef.Current = null;
            }

            return () =>
            {
                timerRef.Current?.Stop();
                timerRef.Current = null;
            };
        }, running, percent);

        UseEffect(() =>
        {
            if (!CliOpts.Headless) return;
            setPercent(CliOpts.Percent);
            setRunning(true);

            var shutdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(CliOpts.DurationSeconds) };
            shutdownTimer.Tick += (_, _) =>
            {
                setRunning(false);
                shutdownTimer.Stop();
                var perf = perfRef.Current!;
                perf.WriteReportFile(AppName, CliOpts.Percent);
                if (CliOpts.Json)
                {
                    perf.WriteMetricsJsonFile(AppName, CliOpts.Percent);
                    // Echo a single marked line so log scrapers have a fallback to the
                    // {AppName}.metrics.json file written next to the exe.
                    Console.WriteLine("REACTOR_PERF_JSON " + perf.GetMetricsJson(AppName, CliOpts.Percent));
                }
                Application.Current.Exit();
            };
            shutdownTimer.Start();
            shutdownRef.Current = shutdownTimer;
        }, Array.Empty<object>());

        // ── Build element tree ──────────────────────────────────────────
        // Each row is a single keyed TextBlockElement. The `.Key` is what flips the
        // child reconciler onto its keyed arm (ChildReconciler.HasAnyKeys); reordered
        // keys in the post-prefix/suffix middle drive the LIS minimal-move pass. Built
        // via the direct record initializer (no fluent clones) to keep the per-render
        // tree-build cost dominated by the element allocation under measurement.
        var children = new Element[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            var row = data[i];
            children[i] = new TextBlockElement(row.Label)
            {
                Key = row.Key,
                FontSize = 12,
            };
        }

        // Structural self-check (runs once): EVERY emitted child must carry a non-null
        // Key. If any key were missing, ChildReconciler.HasAnyKeys would be false and the
        // entire scene would silently fall onto ReconcilePositional — measuring the WRONG
        // reconcile path and invalidating the whole workload. Fail loudly (no metrics.json
        // is written) rather than report misleading keyed numbers.
        if (!keysVerifiedRef.Current)
        {
            keysVerifiedRef.Current = true;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].Key is null)
                {
                    Console.Error.WriteLine(
                        $"FATAL: {AppName} child {i} has no Key — the keyed reconcile path " +
                        "(ReconcileKeyed/ReconcileKeyedMiddle) would not run; results are invalid.");
                    Environment.FailFast(
                        $"{AppName}: keyed-path invariant violated (child {i} missing Key).");
                }
            }
        }

        return VStack(
            HStack(12,
                Button(running ? "Stop" : "Start", () => setRunning(!running)),
                TextBlock("Move %:").VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center),
                Slider(percent, 0, 100, v => setPercent(v)).Width(200),
                TextBlock(fps).VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center).Width(100),
                TextBlock(updateMs).VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center).Width(120),
                TextBlock(mem).VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center).Width(120)
            ).Padding(8),
            ScrollView(
                VStack(children)
            )
        );
    }
}
