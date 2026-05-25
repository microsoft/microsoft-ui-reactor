// Spec 047 Phase 0 — V2 skeleton. Intentionally a verbatim copy of
// StressPerf.Reactor's Program.cs at Phase-0 freeze. Only the AppName
// constant differs so the JSON-Lines collector can stamp BenchVariant=V2.
// Phase 1+ work will start to introduce real differences here.
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
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
ReactorApp.Run<StockGridApp>("StressPerf.ReactorV2", fullScreen: true);

// ---------------------------------------------------------------------------

class StockGridApp : Component
{
    private const string AppName = "StressPerf.ReactorV2";

    public static CliOptions CliOpts { get; set; } = new();

    private static readonly GridSize[] Cols = Enumerable.Range(0, StockDataSource.Columns).Select(_ => GridSize.Px(64)).ToArray();
    private static readonly GridSize[] RowDefs = Enumerable.Range(0, StockDataSource.Rows).Select(_ => GridSize.Px(18)).ToArray();

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
                perfRef.Current!.WriteReportFile(AppName, CliOpts.Percent);
                Application.Current.Exit();
            };
            shutdownTimer.Start();
            shutdownRef.Current = shutdownTimer;
        }, Array.Empty<object>());

        var children = new Element[StockDataSource.TotalItems];
        for (int i = 0; i < StockDataSource.TotalItems; i++)
        {
            int r = i / StockDataSource.Columns;
            int c = i % StockDataSource.Columns;
            ref readonly var item = ref data[i];
            children[i] = TextBlock(StockDataSource.FormatCell(in item))
                .FontSize(8)
                .Foreground(item.IsUp ? GreenBrush : RedBrush)
                .Padding(2, 1, 2, 1)
                .Grid(row: r, column: c);
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
            ScrollView(
                Grid(Cols, RowDefs, children)
            )
        );
    }
}
