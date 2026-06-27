// StressPerf.Flex — a /perf macro workload that exercises Reactor's FlexPanel / Yoga
// LAYOUT engine (the Flex/ + Yoga/ subsystems #670 optimizes) that NO existing /perf
// workload reaches.
//
// The StocksGrid workload (StressPerf.ReactorOptimized) mutates a fixed positional
// Grid in place; the keyed-list workload (StressPerf.KeyedList) reorders keyed rows.
// Neither drives a real Yoga measure/layout pass each frame, so #670's layout-cache
// guards (#138), inline per-node arrays (#142/#143), attached-DP push caching (#147)
// and per-frame list/line pooling (#141/#144) cannot be measured. The existing
// FlexPanel-heavy StressPerf.VirtualList is vsync-capped AND virtualized, which is
// exactly why it can't surface them either.
//
// This harness renders a DEEP NESTED, fully-realized (non-virtualized) flex tree
// (sections → rows → leaf cells, ~2000 leaves) and, every tick, re-rolls the flex
// inputs (grow / basis / width) on a `--percent` fraction of the leaves — forcing a
// real Yoga relayout each frame. The remaining leaves re-push their UNCHANGED inputs,
// which is precisely the YogaNode setter-equality-guard (cache-hit) path #670 targets.
// The win shows up as lower per-frame ALLOCATION + Gen0 on the deep tree and lower
// inline-per-node MEMORY — captured by the shared PerfTracker.
//
// The harness contract is mirrored byte-for-byte from StressPerf.KeyedList: the same
// CLI flags (--headless / --percent / --duration / --json via the shared CliOptions),
// the same shutdown emission (report.txt + metrics.json + the REACTOR_PERF_JSON stdout
// line) via the shared PerfTracker, and the same OnRenderComplete phase-capture wiring.
// Only the SCENE and its per-tick mutation differ, so Run-PerfBenchmark.ps1 /
// PerfLib.ps1 can drive it identically.

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using StressPerf.Flex;
using StressPerf.Shared;
using static Microsoft.UI.Reactor.Factories;

// Parse CLI args before WinUI starts
var cliOptions = CliOptions.Parse(args);
if (cliOptions.Headless)
    ConsoleHelper.EnsureConsole();

FlexApp.CliOpts = cliOptions;

// Children are built via the direct record initializer (`new TextBlockElement(...)`)
// to avoid factory overhead in the hot path, which bypasses the factory's lazy
// handler registration. Opt into the full built-in catalog once at startup so every
// built-in element record has a registered handler before the first reconcile —
// the documented one-line prelude for the direct-record idiom (spec-048 §3.4),
// identical to StressPerf.KeyedList.
ReactorApp.RegisterAllBuiltIns();

ReactorApp.Run<FlexApp>("StressPerf.Flex", fullScreen: true);

// ---------------------------------------------------------------------------

class FlexApp : Component
{
    private const string AppName = "StressPerf.Flex";

    public static CliOptions CliOpts { get; set; } = new();

    public override Element Render()
    {
        var sourceRef = UseRef<FlexSceneSource?>(null);
        if (sourceRef.Current == null)
            sourceRef.Current = new FlexSceneSource();
        var source = sourceRef.Current;

        // The full leaf snapshot drives a complete child-tree rebuild every render
        // (deliberately NO positional memo fast-path), so a real Yoga layout pass runs
        // each tick.
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
        var shapeVerifiedRef = UseRef(false);

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

        // ── Build the deep nested flex tree ─────────────────────────────────
        // root FlexColumn → S section FlexColumns → R FlexRows → C leaf cells. Each leaf
        // pushes its per-child flex inputs (grow / basis via .Flex(...), width via
        // .Width(...)) onto its Yoga node; the (1 - percent) of leaves that did NOT
        // change this tick re-push identical values — the YogaNode setter-equality-guard
        // (cache-hit) path #670 optimizes. The tree SHAPE is fixed, so the child
        // reconciler stays on its cheap positional arm and the measured cost is the
        // layout-engine work, not child diffing.
        int sections = source.Sections, rows = source.Rows, cols = source.Cols;
        var sectionEls = new Element[sections];
        Element? sampleLeaf = null;
        int li = 0;
        for (int s = 0; s < sections; s++)
        {
            var rowEls = new Element[rows];
            for (int r = 0; r < rows; r++)
            {
                var cellEls = new Element[cols];
                for (int c = 0; c < cols; c++)
                {
                    var leaf = data[li++];
                    cellEls[c] = new TextBlockElement(leaf.Label) { FontSize = 10 }
                        .Flex(grow: leaf.Grow, basis: leaf.Basis)
                        .Width(leaf.Width);
                }
                rowEls[r] = FlexRow(cellEls);
                sampleLeaf ??= cellEls[0];
            }
            sectionEls[s] = FlexColumn(rowEls).Flex(grow: 1);
        }
        // Typed as Element so the structural self-check below is a real runtime assertion
        // (not a compile-time always-true comparison the analyzer would flag).
        Element flexRoot = FlexColumn(sectionEls);

        // Structural self-check (runs once): the representative containers MUST be
        // FlexElement-backed AND a representative leaf MUST survive the fluent
        // .Flex(...).Width(...) chain as a concrete TextBlockElement. The FlexElement check
        // guards against a refactor silently swapping the scene onto Grid/StackPanel (which
        // never run the Yoga layout engine). The leaf-type check guards against a modifier
        // regression that degrades the leaf to a bare Element and drops its grow/basis/width
        // inputs — without those the reconciler pushes nothing onto the Yoga nodes, so a
        // mutation would be a no-op (no relayout). Either failure means the workload is NOT
        // exercising the Flex/Yoga layout engine, so fail loudly (no metrics.json is written)
        // rather than report misleading layout numbers.
        if (!shapeVerifiedRef.Current)
        {
            shapeVerifiedRef.Current = true;
            bool rootIsFlex = flexRoot is FlexElement;
            bool sectionIsFlex = sectionEls.Length > 0 && sectionEls[0] is FlexElement;
            bool leafIsTextBlock = sampleLeaf is TextBlockElement;
            if (!rootIsFlex || !sectionIsFlex || !leafIsTextBlock)
            {
                Console.Error.WriteLine(
                    $"FATAL: {AppName} scene is not Flex/Yoga-backed (root={rootIsFlex} " +
                    $"section={sectionIsFlex} leafTextBlock={leafIsTextBlock}) — the layout " +
                    "engine under measurement would not run (mutations would be no-ops); " +
                    "results are invalid.");
                Environment.FailFast(
                    $"{AppName}: Flex/Yoga-layout invariant violated (scene not FlexElement-backed " +
                    "or leaf degraded through the flex modifier chain).");
            }
        }

        return VStack(
            HStack(12,
                Button(running ? "Stop" : "Start", () => setRunning(!running)),
                TextBlock("Churn %:").VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center),
                Slider(percent, 0, 100, v => setPercent(v)).Width(200),
                TextBlock(fps).VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center).Width(100),
                TextBlock(updateMs).VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center).Width(120),
                TextBlock(mem).VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center).Width(120)
            ).Padding(8),
            ScrollView(
                flexRoot
            )
        );
    }
}
