using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Controls;
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
ReactorApp.Run<StockGridApp>("StressPerf.ReactorGrid", fullScreen: true);

// ---------------------------------------------------------------------------

class StockGridApp : Component
{
    private const string AppName = "StressPerf.ReactorGrid";
    private const int ColumnCount = 30;
    private const int RowCount = 160;

    public static CliOptions CliOpts { get; set; } = new();

    // Cache brushes — lazy because SolidColorBrush requires the WinUI thread.
    private static SolidColorBrush? _greenBrush;
    private static SolidColorBrush? _redBrush;
    private static SolidColorBrush GreenBrush => _greenBrush ??= new(global::Windows.UI.Color.FromArgb(255, 0, 128, 0));
    private static SolidColorBrush RedBrush => _redBrush ??= new(global::Windows.UI.Color.FromArgb(255, 255, 0, 0));

    public override Element Render()
    {
        // The data source survives across renders via ref.
        var sourceRef = UseRef<StockGridSource?>(null);
        if (sourceRef.Current == null)
            sourceRef.Current = new StockGridSource(ColumnCount, RowCount);
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

        // Build column descriptors once
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
                    Sortable = false,
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
                perfRef.Current!.WriteReportFile(AppName, CliOpts.Percent);
                Application.Current.Exit();
            };
            shutdownTimer.Start();
            shutdownRef.Current = shutdownTimer;
        }, Array.Empty<object>());

        // --- Build element tree ---
        return VStack(
            HStack(12,
                Button(running ? "Stop" : "Start", () => setRunning(!running)),
                TextBlock("Update %:").VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center),
                Slider(percent, 0, 100, v => setPercent(v)).Width(200),
                TextBlock(fps).VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center).Width(100),
                TextBlock(updateMs).VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center).Width(120),
                TextBlock(mem).VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center).Width(120)
            ).Padding(8),
            DataGrid(
                source: source,
                columns: columns,
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
            ).Flex(grow: 1)
        );
    }
}

// ---------------------------------------------------------------------------

/// <summary>
/// A row of stock data for the DataGrid. Each row contains cells for every column.
/// </summary>
record StockRow(int Id, StockItem[] Cells);

/// <summary>
/// In-memory data source that feeds stock data to the DataGrid.
/// Fires DataChanged after mutations to trigger grid refresh via the
/// DataGrid's IObservableDataSource subscription.
/// </summary>
class StockGridSource : IDataSource<StockRow>, IObservableDataSource<StockRow>
{
    private readonly int _columnCount;
    private readonly int _rowCount;
    private readonly int _totalCells;
    private readonly StockRow[] _rows;
    private readonly Random _rng = new(42); // deterministic seed

    public StockGridSource(int columns, int rows)
    {
        _columnCount = columns;
        _rowCount = rows;
        _totalCells = columns * rows;
        _rows = new StockRow[rows];

        var rng = _rng;
        for (int r = 0; r < rows; r++)
        {
            var cells = new StockItem[columns];
            for (int c = 0; c < columns; c++)
            {
                char c1 = (char)('A' + (r % 26));
                char c2 = (char)('A' + (c / 3 % 26));
                char c3 = (char)('A' + (c % 26));
                string symbol = string.Create(3, (c1, c2, c3), static (span, s) =>
                {
                    span[0] = s.c1;
                    span[1] = s.c2;
                    span[2] = s.c3;
                });
                double price = Math.Round(10.0 + rng.NextDouble() * 990.0, 2);
                cells[c] = new StockItem(symbol, price, price, true);
            }
            _rows[r] = new StockRow(r, cells);
        }
    }

    public event EventHandler? DataChanged;

    public DataSourceCapabilities Capabilities => DataSourceCapabilities.ServerCount;

    public RowKey GetRowKey(StockRow item) => item.Id;

    public Task<DataPage<StockRow>> GetPageAsync(DataRequest request, CancellationToken cancellationToken = default)
    {
        var offset = 0;
        if (request.ContinuationToken is not null && int.TryParse(request.ContinuationToken, out var parsed))
            offset = parsed;

        var pageSize = Math.Min(request.PageSize, _rowCount - offset);
        if (pageSize <= 0)
            return Task.FromResult(new DataPage<StockRow>(Array.Empty<StockRow>(), null, _rowCount));

        var items = new StockRow[pageSize];
        Array.Copy(_rows, offset, items, 0, pageSize);

        var nextOffset = offset + pageSize;
        var continuation = nextOffset < _rowCount ? nextOffset.ToString() : null;

        return Task.FromResult(new DataPage<StockRow>(items, continuation, _rowCount));
    }

    /// <summary>
    /// Mutate a percentage of cells. Same logic as StockDataSource.Update.
    /// Fires DataChanged to trigger DataGrid refresh.
    /// </summary>
    public void Update(double percent)
    {
        int count = Math.Max(1, (int)(_totalCells * percent / 100.0));
        var rng = _rng;

        for (int i = 0; i < count; i++)
        {
            int idx = rng.Next(_totalCells);
            int row = idx / _columnCount;
            int col = idx % _columnCount;
            var cells = _rows[row].Cells;
            var item = cells[col];
            double delta = ((rng.NextDouble() - 0.48) * 2.0) * item.CurrentPrice * 0.02;
            double newPrice = Math.Max(0.01, Math.Round(item.CurrentPrice + delta, 2));
            cells[col] = new StockItem(item.Symbol, item.CurrentPrice, newPrice, newPrice >= item.CurrentPrice);
        }

        DataChanged?.Invoke(this, EventArgs.Empty);
    }
}
