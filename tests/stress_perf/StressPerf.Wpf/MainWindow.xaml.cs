using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using StressPerf.Shared;

namespace StressPerf.Wpf;

public partial class MainWindow : Window
{
    private const string AppName = "StressPerf.Wpf";

    private static readonly SolidColorBrush GreenBrush = new(Colors.LimeGreen);
    private static readonly SolidColorBrush RedBrush = new(Colors.Red);

    static MainWindow()
    {
        GreenBrush.Freeze();
        RedBrush.Freeze();
    }

    private readonly StockDataSource _source = new();
    private readonly PerfTracker _perf = new();
    private readonly CliOptions _options;
    private readonly TextBlock[] _cells = new TextBlock[StockDataSource.TotalItems];

    private DispatcherTimer? _updateTimer;
    private Slider? _percentSlider;
    private TextBlock? _fpsText;
    private TextBlock? _memText;
    private TextBlock? _updateText;
    private Button? _toggleButton;
    private bool _running;
    private Stopwatch? _headlessStopwatch;

    public MainWindow()
    {
        InitializeComponent();
        _options = App.Options;
        BuildUI();

        CompositionTarget.Rendering += (_, _) => _perf.FrameRendered();

        if (_options.Headless)
        {
            // Defer headless start until the window is fully loaded
            Loaded += (_, _) =>
            {
                _percentSlider!.Value = _options.Percent;
                StartUpdating();
                // Use a Stopwatch-based check inside the update tick instead of a separate timer,
                // since the dispatcher may starve lower-priority timers under heavy load.
                _headlessStopwatch = Stopwatch.StartNew();
            };
        }
    }

    private void BuildUI()
    {
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // --- Controls row ---
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };

        _toggleButton = new Button { Content = "Start", Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        _toggleButton.Click += (_, _) => { if (_running) StopUpdating(); else StartUpdating(); };
        controls.Children.Add(_toggleButton);

        controls.Children.Add(new TextBlock { Text = "Update %:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        _percentSlider = new Slider { Minimum = 0, Maximum = 100, Value = _options.Percent, Width = 200, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(_percentSlider);

        _fpsText = new TextBlock { Text = "FPS: --", VerticalAlignment = VerticalAlignment.Center, Width = 100, Margin = new Thickness(12, 0, 0, 0) };
        controls.Children.Add(_fpsText);

        _updateText = new TextBlock { Text = "Update: -- ms", VerticalAlignment = VerticalAlignment.Center, Width = 120 };
        controls.Children.Add(_updateText);

        _memText = new TextBlock { Text = "Mem: -- MB", VerticalAlignment = VerticalAlignment.Center, Width = 120 };
        controls.Children.Add(_memText);

        Grid.SetRow(controls, 0);
        Root.Children.Add(controls);

        // --- Stock grid ---
        var grid = new Grid();
        for (int c = 0; c < StockDataSource.Columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        for (int r = 0; r < StockDataSource.Rows; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });

        for (int i = 0; i < StockDataSource.TotalItems; i++)
        {
            int row = i / StockDataSource.Columns;
            int col = i % StockDataSource.Columns;
            var item = _source.Items[i];
            var tb = new TextBlock
            {
                Text = StockDataSource.FormatCell(in item),
                FontSize = 8,
                Foreground = GreenBrush,
                Padding = new Thickness(2, 1, 2, 1),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetRow(tb, row);
            Grid.SetColumn(tb, col);
            grid.Children.Add(tb);
            _cells[i] = tb;
        }

        var scroll = new ScrollViewer
        {
            Content = grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetRow(scroll, 1);
        Root.Children.Add(scroll);
    }

    private void StartUpdating()
    {
        _running = true;
        _toggleButton!.Content = "Stop";
        _updateTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
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

    private void OnTick(object? sender, EventArgs e)
    {
        _perf.BeginUpdate();

        double pct = _percentSlider!.Value;
        var changed = _source.Update(pct);

        // Direct update: set TextBlock properties for changed items only
        var items = _source.Items;
        foreach (int idx in changed)
        {
            ref readonly var item = ref items[idx];
            var tb = _cells[idx];
            tb.Text = StockDataSource.FormatCell(in item);
            tb.Foreground = item.IsUp ? GreenBrush : RedBrush;
        }

        _perf.EndUpdate();
        // For WPF imperative, a "render" = one tick of property patches.
        _perf.RecordRender();

        // Update stats display
        _fpsText!.Text = $"FPS: {_perf.CurrentFps:F0}";
        _updateText!.Text = $"Update: {_perf.LastUpdateMs:F1} ms";
        _memText!.Text = $"Mem: {_perf.CurrentMemoryMB} MB";

        // Headless auto-shutdown check
        if (_headlessStopwatch is not null && _headlessStopwatch.Elapsed.TotalSeconds >= _options.DurationSeconds)
        {
            _headlessStopwatch = null;
            StopUpdating();
            _perf.WriteReportFile(AppName, _options.Percent);
            Application.Current.Shutdown();
        }
    }
}
