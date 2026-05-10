using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using StressPerf.Shared;

namespace StressPerf.XBind;

public sealed partial class MainWindow : Window
{
    private const string AppName = "StressPerf.XBind";

    private readonly StockDataSource _source = new();
    private readonly PerfTracker _perf = new();
    private readonly CliOptions _options;

    // Internal so the compile-time x:Bind in MainWindow.xaml can resolve
    // expressions of the form `ViewModels[N].DisplayText` (the generated
    // partial class lives in the same assembly), without exposing the
    // StockItemViewModel[] type across the WinRT ABI — which would trip
    // CsWinRT1030 and require AllowUnsafeBlocks. Must be assigned before
    // InitializeComponent runs, since x:Bind reads each ViewModels[N]
    // reference once at template-instantiation time and then subscribes
    // to that VM's INPC.
    internal StockItemViewModel[] ViewModels { get; }

    // Phase timing
    private readonly Stopwatch _phaseSw = new();
    private double _mutateSum, _vmSetSum;
    private int _tickCount;
    private readonly Stopwatch _reportClock = Stopwatch.StartNew();

    private DispatcherTimer? _updateTimer;
    private DispatcherTimer? _shutdownTimer;
    private bool _running;

    public MainWindow(CliOptions options)
    {
        _options = options;

        ViewModels = new StockItemViewModel[StockDataSource.TotalItems];
        for (int i = 0; i < StockDataSource.TotalItems; i++)
        {
            var vm = new StockItemViewModel();
            var item = _source.Items[i];
            vm.Update(item.Symbol, item.CurrentPrice, item.IsUp);
            ViewModels[i] = vm;
        }

        InitializeComponent();

        Title = AppName;
        AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);

        _percentSlider.Value = _options.Percent;

        CompositionTarget.Rendering += (_, _) => _perf.FrameRendered();

        if (options.Headless)
        {
            StartUpdating();
            _shutdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(options.DurationSeconds) };
            _shutdownTimer.Tick += (_, _) =>
            {
                StopUpdating();
                _shutdownTimer.Stop();
                _perf.WriteReportFile(AppName, _options.Percent);
                Close();
            };
            _shutdownTimer.Start();
        }
    }

    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if (_running) StopUpdating(); else StartUpdating();
    }

    private void StartUpdating()
    {
        _running = true;
        _toggleButton.Content = "Stop";
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _updateTimer.Tick += OnTick;
        _updateTimer.Start();
    }

    private void StopUpdating()
    {
        _running = false;
        _toggleButton.Content = "Start";
        _updateTimer?.Stop();
        _updateTimer = null;
    }

    private void OnTick(object? sender, object e)
    {
        _perf.BeginUpdate();

        _phaseSw.Restart();
        double pct = _percentSlider.Value;
        var changed = _source.Update(pct);
        double mutateMs = _phaseSw.Elapsed.TotalMilliseconds;

        _phaseSw.Restart();
        // Compiled-binding update: setting a VM property fires INPC; the
        // x:Bind-generated handler for each TextBlock pushes the new value
        // directly into the visual without going through PropertyPath
        // parsing or BindingExpression. With the flat XAML tree (no
        // ContentPresenter wrappers) every cell is a raw TextBlock — same
        // visual-tree shape as StressPerf.Bound, so the only thing that
        // changes between this variant and Bound is the binding mechanism.
        var items = _source.Items;
        foreach (int idx in changed)
        {
            ref readonly var item = ref items[idx];
            ViewModels[idx].Update(item.Symbol, item.CurrentPrice, item.IsUp);
        }
        double vmSetMs = _phaseSw.Elapsed.TotalMilliseconds;

        _perf.EndUpdate();
        // Definition matches StressPerf.Bound: a "render" is one tick that
        // finished pushing INPC notifications through the binding pipeline.
        _perf.RecordRender();

        _mutateSum += mutateMs;
        _vmSetSum += vmSetMs;
        _tickCount++;
        if (_reportClock.Elapsed.TotalSeconds >= 1.0 && _tickCount > 0)
        {
            try { File.AppendAllText(@"C:\temp\xbind_perf_phases.log",
                $"PERF [{_tickCount} ticks]: mutate={_mutateSum / _tickCount:F2}ms  vmSet={_vmSetSum / _tickCount:F2}ms  total={(_mutateSum + _vmSetSum) / _tickCount:F2}ms\n"); } catch { }
            _mutateSum = 0; _vmSetSum = 0; _tickCount = 0;
            _reportClock.Restart();
        }

        _fpsText.Text = $"FPS: {_perf.CurrentFps:F0}";
        _updateText.Text = $"Update: {_perf.LastUpdateMs:F1} ms";
        _memText.Text = $"Mem: {_perf.CurrentMemoryMB} MB";
    }
}
