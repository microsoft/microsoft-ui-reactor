// StressPerf.DataGrid — a /perf macro workload that exercises Reactor's DataGrid
// control (src/Reactor/Controls/DataGrid/) — the per-render array/LINQ allocation
// path (#663/#669) and the per-cell/row modifier-delegate churn (#671) — that NO
// existing WIRED /perf leg reaches.
//
// The StocksGrid leg (StressPerf.ReactorOptimized) mutates a fixed positional native
// Grid in place; the keyed-list leg (StressPerf.KeyedList) reorders keyed rows; the
// flex leg (StressPerf.Flex) drives the Yoga layout engine. None instantiate the
// DataGrid control, so #669's per-render rebuilds and #671's per-render delegate
// closures can't be measured. (A legacy StressPerf.ReactorGrid project DOES stand up
// a DataGrid, but it predates the compare-mode contract — it emits no metrics.json /
// REACTOR_PERF_JSON / alloc table and is wired only into the old single-tree
// full-matrix scripts (run_full_matrix.ps1 / run_sweep_arm64.ps1 / run_benchmark.sh),
// NOT Run-PerfBenchmark.ps1 compare mode. This leg is the modern equivalent; the two
// intentionally coexist.)
//
// This harness renders the real DataGrid control over a 30-column × 200-row
// IObservableDataSource and, every tick, mutates a `--percent` fraction of cells
// (firing DataChanged) AND updates the on-screen FPS/Update/Mem labels. Both force a
// full DataGridComponent.Render() each frame — which re-runs, UNCONDITIONALLY and
// regardless of whether sort/filter changed, the per-render rebuilds #663/#669 target
// (the sortKey join, the DataRequest + .ToList()s, the header+row colWidths/
// gridColDefs arrays, the Columns getter's Where+ToList, the per-column LINQ lookups,
// the row-def arrays, the setter spread) and re-allocates the per-realized-cell/row
// .OnTapped/.OnPointerPressed closures inside RenderRow (the #671 churn). A small
// rowHeight keeps many rows realized so that delegate churn is at measurable scale.
//
// MEASUREMENT CAVEAT (for maintainers + whoever measures #669/#671 against this leg):
// the DataGrid's re-render is ASYNC/DEBOUNCED — DataChanged → LoadDataAsync →
// StateChanged → a dispatcher-coalesced forceRender (plus a scroll-settle timer) —
// unlike the synchronous KeyedList/Flex element trees. So the per-render `avgReconcileMs`
// / `avgDiffMs` numbers (captured by the OnRenderComplete phase hook) are noisier here
// and a render may be coalesced. Judge DataGrid allocation/delegate wins primarily on
// the allocation table (`allocBytesPerRender` / `gen0`, which the shared PerfTracker
// reads from process-wide GC counters across the whole run) plus `rendersPerSec` — the
// same guidance the Flex leg documents for its deferred-layout caveat. No post-render
// timing hook is added here (that would touch the shared PerfTracker used by the other
// legs); add one only if a /perf run actually needs a finer signal (measure-then-escalate).
//
// The harness contract is mirrored byte-for-byte from StressPerf.Flex: the same CLI
// flags (--headless / --percent / --duration / --json via the shared CliOptions), the
// same shutdown emission (report.txt + metrics.json + the REACTOR_PERF_JSON stdout
// line) via the shared PerfTracker, and the same OnRenderComplete phase-capture wiring.
// Only the SCENE and its per-tick mutation differ, so Run-PerfBenchmark.ps1 /
// PerfLib.ps1 can drive it identically.

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using StressPerf.DataGrid;
using StressPerf.Shared;
using static Microsoft.UI.Reactor.Factories;

// Parse CLI args before WinUI starts
var cliOptions = CliOptions.Parse(args);
if (cliOptions.Headless)
    ConsoleHelper.EnsureConsole();

DataGridApp.CliOpts = cliOptions;

// Opt into the full built-in catalog once at startup so every built-in element record
// has a registered handler before the first reconcile — the documented one-line
// prelude for the direct-record idiom (spec-048 §3.4), identical to StressPerf.Flex.
ReactorApp.RegisterAllBuiltIns();

ReactorApp.Run<DataGridApp>("StressPerf.DataGrid", fullScreen: true);

// ---------------------------------------------------------------------------

class DataGridApp : Component
{
    private const string AppName = "StressPerf.DataGrid";
    private const int ColumnCount = DataGridSceneSource.DefaultColumns;
    private const int RowCount = DataGridSceneSource.DefaultRows;

    public static CliOptions CliOpts { get; set; } = new();

    // Cache brushes — lazy because SolidColorBrush requires the WinUI thread.
    private static SolidColorBrush? _greenBrush;
    private static SolidColorBrush? _redBrush;
    private static SolidColorBrush GreenBrush => _greenBrush ??= new(global::Windows.UI.Color.FromArgb(255, 0, 128, 0));
    private static SolidColorBrush RedBrush => _redBrush ??= new(global::Windows.UI.Color.FromArgb(255, 255, 0, 0));

    public override Element Render()
    {
        // The data source survives across renders via ref.
        var sourceRef = UseRef<DataGridSceneSource?>(null);
        if (sourceRef.Current == null)
            sourceRef.Current = new DataGridSceneSource(ColumnCount, RowCount);
        var source = sourceRef.Current;

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

        // Lazily create PerfTracker and wire up render-complete callback
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

        // CompositionTarget.Rendering for FPS counting
        var renderHooked = UseRef(false);
        if (!renderHooked.Current)
        {
            renderHooked.Current = true;
            var perf = perfRef.Current;
            CompositionTarget.Rendering += (_, _) => perf.FrameRendered();
        }

        // Build column descriptors once. 30 sortable columns over StockRow.Cells[col],
        // so the per-column header/row LINQ lookups (#128) run at full width each render.
        var columns = UseMemo(() =>
        {
            var cols = new FieldDescriptor[ColumnCount];
            for (int c = 0; c < ColumnCount; c++)
            {
                int col = c;
                cols[c] = new FieldDescriptor
                {
                    Name = $"Col{c}",
                    DisplayName = $"Col {c}",
                    FieldType = typeof(StockItem),
                    GetValue = obj => ((StockRow)obj).Cells[col],
                    IsReadOnly = true,
                    Width = 64,
                    Sortable = true,
                    Filterable = false,
                };
            }
            return (IReadOnlyList<FieldDescriptor>)cols;
        });

        // Start/stop the update timer when `running` changes
        UseEffect(() =>
        {
            if (running)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                timer.Tick += (_, _) =>
                {
                    var perf = perfRef.Current!;
                    perf.BeginUpdate();

                    source.Update(percent);
                    benchmarkUpdatePending.Current = true;

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

        // Headless auto-start
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

        // ── Build the DataGrid scene ────────────────────────────────────────
        // The cellTemplate is a fresh closure each render, so the DataGridElement props
        // differ render-to-render and the DataGridComponent re-renders its subtree every
        // tick (the per-render allocation path under measurement). Small rowHeight (18)
        // keeps many rows realized so the per-cell/row .OnTapped/.OnPointerPressed
        // closures (#671) churn at measurable scale.
        Element dataGridEl = DataGrid(
            source: source,
            columns: columns,
            selectionMode: SelectionMode.Multiple,
            rowHeight: 18,
            showSearch: false,
            cellTemplate: ctx =>
            {
                if (ctx.Value is not StockItem item)
                    return TextBlock("").FontSize(8);
                return TextBlock(StockDataSource.FormatCell(in item))
                    .FontSize(8)
                    .Foreground(item.IsUp ? GreenBrush : RedBrush)
                    .Padding(2, 1, 2, 1);
            }
        ).Flex(grow: 1);

        // Structural self-check (runs once): the scene root MUST be a DataGrid component
        // element. This guards against a refactor silently swapping the scene off the
        // DataGrid control (onto a native Grid / VirtualList), which would never enter the
        // DataGridComponent render path under measurement — making every mutation a no-op
        // for #669/#671 and the reported numbers meaningless. Fail loudly (no metrics.json
        // is written) rather than report misleading DataGrid numbers.
        if (!shapeVerifiedRef.Current)
        {
            shapeVerifiedRef.Current = true;
            bool isDataGrid = dataGridEl is ComponentElement<DataGridElement<StockRow>>;
            if (!isDataGrid)
            {
                Console.Error.WriteLine(
                    $"FATAL: {AppName} scene root is not a DataGrid component element " +
                    $"(got {dataGridEl.GetType().Name}) — the DataGrid control under " +
                    "measurement would not run (mutations would be no-ops); results are invalid.");
                Environment.FailFast(
                    $"{AppName}: DataGrid-control invariant violated (scene root not " +
                    "ComponentElement<DataGridElement<StockRow>>).");
            }
        }

        return VStack(
            HStack(12,
                Button(running ? "Stop" : "Start", () => setRunning(!running)),
                TextBlock("Update %:").VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center),
                Slider(percent, 0, 100, v => setPercent(v)).Width(200),
                TextBlock(fps).VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center).Width(100),
                TextBlock(updateMs).VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center).Width(120),
                TextBlock(mem).VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center).Width(120)
            ).Padding(8),
            dataGridEl
        );
    }
}
