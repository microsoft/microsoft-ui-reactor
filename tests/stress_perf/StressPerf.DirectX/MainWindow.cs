using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StressPerf.Shared;
using Windows.UI;

namespace StressPerf.DirectX;

public sealed class MainWindow : Window
{
    private const string AppName = "StressPerf.DirectX";
    private const float CellWidth = 64f;
    private const float CellHeight = 18f;

    private readonly StockDataSource _source = new();
    private readonly PerfTracker _perf = new();
    private readonly CliOptions _options;

    private CanvasControl? _canvas;
    private CanvasTextFormat? _textFormat;
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

        BuildUI();

        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += (_, _) => _perf.FrameRendered();

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

        // --- Win2D canvas for the stock grid ---
        _canvas = new CanvasControl();
        _canvas.Draw += OnCanvasDraw;

        // Wrap in a ScrollViewer so you can scroll the full grid
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        // The canvas needs an explicit size matching the full grid
        _canvas.Width = StockDataSource.Columns * CellWidth;
        _canvas.Height = StockDataSource.Rows * CellHeight;
        scroll.Content = _canvas;

        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        Content = root;
    }

    private void OnCanvasDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;

        _textFormat ??= new CanvasTextFormat
        {
            FontSize = 8,
            FontFamily = "Segoe UI",
            WordWrapping = CanvasWordWrapping.NoWrap,
        };

        var items = _source.Items;
        var greenColor = Color.FromArgb(255, 50, 205, 50);  // LimeGreen
        var redColor = Color.FromArgb(255, 255, 0, 0);      // Red

        for (int i = 0; i < StockDataSource.TotalItems; i++)
        {
            int row = i / StockDataSource.Columns;
            int col = i % StockDataSource.Columns;
            float x = col * CellWidth + 2;  // 2px left padding
            float y = row * CellHeight + 1; // 1px top padding

            ref readonly var item = ref items[i];
            var text = StockDataSource.FormatCell(in item);
            var color = item.IsUp ? greenColor : redColor;

            ds.DrawText(text, x, y, color, _textFormat);
        }
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

        double pct = _percentSlider!.Value;
        _source.Update(pct);

        // Invalidate the canvas to trigger a redraw via Direct2D
        _canvas?.Invalidate();

        _perf.EndUpdate();
        // For DirectX, a "render" = one tick that called Invalidate(); the
        // actual D2D draw happens on the canvas callback. Tick count is the
        // closest proxy to "frames the app asked the GPU to redraw."
        _perf.RecordRender();

        // Update stats display
        _fpsText!.Text = $"FPS: {_perf.CurrentFps:F0}";
        _updateText!.Text = $"Update: {_perf.LastUpdateMs:F1} ms";
        _memText!.Text = $"Mem: {_perf.CurrentMemoryMB} MB";
    }
}
