// StressPerf.ReactorOptimized — Reactor's "all three components combined"
// reference implementation for spec 034.
//
// This project demonstrates perf-critical inner-loop idioms; do not write
// ordinary UI code this way. The fluent chain remains the right tool for
// regular UI. See spec 034 §B (direct-record-initializer idiom) and
// docs/guide/advanced.md ("Hot loops") for the trade-off discussion.
//
// Phase 2 (this file at first commit): Components A + B — bucketed
// LayoutModifiers / VisualModifiers, direct record initializer cell
// construction. UseMemoCellsByIndex (Component C) is wired in Phase 4.

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using StressPerf.Shared;
using static Microsoft.UI.Reactor.Factories;

// Parse CLI args before WinUI starts
var cliOptions = CliOptions.Parse(args);
if (cliOptions.Headless)
    ConsoleHelper.EnsureConsole();

StockGridApp.CliOpts = cliOptions;
ReactorApp.Run<StockGridApp>("StressPerf.ReactorOptimized", fullScreen: true);

// ---------------------------------------------------------------------------

class StockGridApp : Component
{
    private const string AppName = "StressPerf.ReactorOptimized";

    public static CliOptions CliOpts { get; set; } = new();

    private static readonly GridSize[] Cols = Enumerable.Range(0, StockDataSource.Columns).Select(_ => GridSize.Px(64)).ToArray();
    private static readonly GridSize[] RowDefs = Enumerable.Range(0, StockDataSource.Rows).Select(_ => GridSize.Px(18)).ToArray();

    // Brushes are allocated once and held — see naive sibling for rationale.
    private static SolidColorBrush? _greenBrush;
    private static SolidColorBrush? _redBrush;
    private static SolidColorBrush GreenBrush => _greenBrush ??= new(global::Windows.UI.Color.FromArgb(255, 0, 128, 0));
    private static SolidColorBrush RedBrush => _redBrush ??= new(global::Windows.UI.Color.FromArgb(255, 255, 0, 0));

    public override Element Render()
    {
        var sourceRef = UseRef<StockDataSource?>(null);
        if (sourceRef.Current == null)
            sourceRef.Current = new StockDataSource();
        var source = sourceRef.Current;

        var (data, setData) = UseState(source.Snapshot());
        // Track which indices changed in the most recent Update() call so
        // UseMemoCellsByIndex (below) only re-runs the builder for those.
        // First render sees an empty list, which means "full reuse" — but
        // the hook also detects that this is the first render via its
        // internal state and rebuilds the whole grid then.
        var changedIndicesRef = UseRef<IReadOnlyList<int>>(Array.Empty<int>());

        var (percent, setPercent) = UseState(CliOpts.Percent);
        var (running, setRunning) = UseState(false);
        var (fps, setFps) = UseState("FPS: --");
        var (updateMs, setUpdateMs) = UseState("Update: -- ms");
        var (mem, setMem) = UseState("Mem: -- MB");

        var perfRef = UseRef<PerfTracker?>(null);
        var timerRef = UseRef<DispatcherTimer?>(null);
        var shutdownRef = UseRef<DispatcherTimer?>(null);
        var benchmarkUpdatePending = UseRef(false);

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

                    var changed = src.Update(percent);
                    changedIndicesRef.Current = changed;
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
                perfRef.Current!.WriteReportFile(AppName, CliOpts.Percent);
                Application.Current.Exit();
            };
            shutdownTimer.Start();
            shutdownRef.Current = shutdownTimer;
        }, Array.Empty<object>());

        // ── Build element tree ──────────────────────────────────────────
        // Spec 034 §B + §C combined: each cell is a single TextBlockElement
        // with one ElementModifiers carrying two bucket sub-records (no
        // fluent-chain clones), and UseMemoCellsByIndex skips the builder
        // entirely for indices the data source didn't touch.
        //
        // GreenBrush / RedBrush are deps because they're closed over by
        // the lambda; r / c / StockDataSource.Columns / FormatCell are
        // either lambda parameters or static, so REACTOR_HOOKS_007 does
        // not flag this call.
        var children = this.UseMemoCellsByIndex<StockItem>(
            data,
            changedIndicesRef.Current,
            (item, i) =>
            {
                int r = i / StockDataSource.Columns;
                int c = i % StockDataSource.Columns;
                return new TextBlockElement(StockDataSource.FormatCell(in item))
                {
                    FontSize = 8,
                    Modifiers = new ElementModifiers
                    {
                        Layout = new LayoutModifiers { Padding = new Thickness(2, 1, 2, 1) },
                        Visual = new VisualModifiers { Foreground = item.IsUp ? GreenBrush : RedBrush },
                    },
                    Attached = new Dictionary<Type, object>(1)
                    {
                        [typeof(GridAttached)] = new GridAttached(r, c, 1, 1),
                    },
                };
            },
            GreenBrush, RedBrush);

        return VStack(
            HStack(12,
                Button(running ? "Stop" : "Start", () => setRunning(!running)),
                TextBlock("Update %:").VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center),
                Slider(percent, 0, 100, v => setPercent(v)).Width(200),
                TextBlock(fps).VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center).Width(100),
                TextBlock(updateMs).VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center).Width(120),
                TextBlock(mem).VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center).Width(120)
            ).Padding(8),
            ScrollView(
                Grid(Cols, RowDefs, children)
            )
        );
    }
}
