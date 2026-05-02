using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using StressPerf.Shared;

namespace StressPerf.Bound;

public sealed class MainWindow : Window
{
    private const string AppName = "StressPerf.Bound";

    private readonly StockDataSource _source = new();
    private readonly PerfTracker _perf = new();
    private readonly CliOptions _options;
    private readonly StockItemViewModel[] _viewModels = new StockItemViewModel[StockDataSource.TotalItems];

    // Phase timing
    private readonly Stopwatch _phaseSw = new();
    private double _mutateSum, _vmSetSum;
    private int _tickCount;
    private readonly Stopwatch _reportClock = Stopwatch.StartNew();

    private DispatcherTimer? _updateTimer;
    private DispatcherTimer? _shutdownTimer;
    private Slider? _percentSlider;
    private TextBlock? _fpsText;
    private TextBlock? _memText;
    private TextBlock? _updateText;
    private Button? _toggleButton;
    private bool _running;

    public MainWindow(CliOptions options)
    {
        _options = options;
        Title = AppName;
        AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);

        // Pre-create all ViewModels
        for (int i = 0; i < StockDataSource.TotalItems; i++)
        {
            var vm = new StockItemViewModel();
            var item = _source.Items[i];
            vm.Update(item.Symbol, item.CurrentPrice, item.IsUp);
            _viewModels[i] = vm;
        }

        BuildUI();

        CompositionTarget.Rendering += (_, _) => _perf.FrameRendered();

        if (options.Headless)
        {
            _percentSlider!.Value = options.Percent;
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

    private void BuildUI()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // --- Controls row ---
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Padding = new Thickness(8) };

        _toggleButton = new Button { Content = "Start" };
        _toggleButton.Click += (_, _) => { if (_running) StopUpdating(); else StartUpdating(); };
        controls.Children.Add(_toggleButton);

        controls.Children.Add(new TextBlock { Text = "Update %:", VerticalAlignment = VerticalAlignment.Center });
        _percentSlider = new Slider { Minimum = 0, Maximum = 100, Value = _options.Percent, Width = 200 };
        controls.Children.Add(_percentSlider);

        _fpsText = new TextBlock { Text = "FPS: --", VerticalAlignment = VerticalAlignment.Center, Width = 100 };
        controls.Children.Add(_fpsText);

        _updateText = new TextBlock { Text = "Update: -- ms", VerticalAlignment = VerticalAlignment.Center, Width = 120 };
        controls.Children.Add(_updateText);

        _memText = new TextBlock { Text = "Mem: -- MB", VerticalAlignment = VerticalAlignment.Center, Width = 120 };
        controls.Children.Add(_memText);

        Grid.SetRow(controls, 0);
        root.Children.Add(controls);

        // --- Stock grid with data bindings ---
        var grid = new Grid();
        for (int c = 0; c < StockDataSource.Columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        for (int r = 0; r < StockDataSource.Rows; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });

        for (int i = 0; i < StockDataSource.TotalItems; i++)
        {
            int row = i / StockDataSource.Columns;
            int col = i % StockDataSource.Columns;
            var vm = _viewModels[i];

            var tb = new TextBlock
            {
                FontSize = 8,
                Padding = new Thickness(2, 1, 2, 1),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Text = vm.DisplayText,
                Foreground = vm.PriceBrush,
            };

            tb.SetBinding(TextBlock.TextProperty, new Binding
            {
                Source = vm,
                Path = new PropertyPath(nameof(StockItemViewModel.DisplayText)),
                Mode = BindingMode.OneWay,
            });
            tb.SetBinding(TextBlock.ForegroundProperty, new Binding
            {
                Source = vm,
                Path = new PropertyPath(nameof(StockItemViewModel.PriceBrush)),
                Mode = BindingMode.OneWay,
            });

            Grid.SetRow(tb, row);
            Grid.SetColumn(tb, col);
            grid.Children.Add(tb);
        }

        var scroll = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        Content = root;
    }

    private void StartUpdating()
    {
        _running = true;
        _toggleButton!.Content = "Stop";
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _updateTimer.Tick += OnTick;
        _updateTimer.Start();
    }

    private void StopUpdating()
    {
        _running = false;
        _toggleButton!.Content = "Start";
        _updateTimer?.Stop();
        _updateTimer = null;
    }

    private void OnTick(object? sender, object e)
    {
        _perf.BeginUpdate();

        _phaseSw.Restart();
        double pct = _percentSlider!.Value;
        var changed = _source.Update(pct);
        double mutateMs = _phaseSw.Elapsed.TotalMilliseconds;

        _phaseSw.Restart();
        // Data-bound update: set ViewModel properties; binding engine pushes to UI
        var items = _source.Items;
        foreach (int idx in changed)
        {
            ref readonly var item = ref items[idx];
            _viewModels[idx].Update(item.Symbol, item.CurrentPrice, item.IsUp);
        }
        double vmSetMs = _phaseSw.Elapsed.TotalMilliseconds;

        _perf.EndUpdate();
        // For MVVM, a "render" = one tick that finished pushing INPC
        // notifications through the binding pipeline.
        _perf.RecordRender();

        _mutateSum += mutateMs;
        _vmSetSum += vmSetMs;
        _tickCount++;
        if (_reportClock.Elapsed.TotalSeconds >= 1.0 && _tickCount > 0)
        {
            try { File.AppendAllText(@"C:\temp\bound_perf_phases.log",
                $"PERF [{_tickCount} ticks]: mutate={_mutateSum / _tickCount:F2}ms  vmSet={_vmSetSum / _tickCount:F2}ms  total={(_mutateSum + _vmSetSum) / _tickCount:F2}ms\n"); } catch { }
            _mutateSum = 0; _vmSetSum = 0; _tickCount = 0;
            _reportClock.Restart();
        }

        // Update stats display
        _fpsText!.Text = $"FPS: {_perf.CurrentFps:F0}";
        _updateText!.Text = $"Update: {_perf.LastUpdateMs:F1} ms";
        _memText!.Text = $"Mem: {_perf.CurrentMemoryMB} MB";
    }
}
