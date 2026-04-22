using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PerfBench.Shared;

namespace PerfBench.InteractivePool.Direct;

public sealed class MainWindow : Window
{
    private const int ItemCount = 500;

    private readonly BenchCliOptions _opts;
    private readonly BenchTracker _tracker = new();
    private readonly ScrollViewer _scrollViewer;
    private double _scrollOffset;

    private readonly TextBlock _hudFps = new();
    private readonly TextBlock _hudUpdate = new();
    private readonly TextBlock _hudMemory = new();

    public MainWindow(BenchCliOptions opts)
    {
        _opts = opts;
        Title = "EXP-6 InteractivePool Direct";
        _tracker.ResetGcBaseline();

        var root = new StackPanel();

        // HUD
        var hud = new StackPanel { Orientation = Orientation.Horizontal, Padding = new Thickness(4) };
        _hudFps.Width = 120;
        _hudUpdate.Width = 150;
        _hudMemory.Width = 150;
        hud.Children.Add(_hudFps);
        hud.Children.Add(_hudUpdate);
        hud.Children.Add(_hudMemory);
        root.Children.Add(hud);

        // ScrollViewer + ItemsRepeater for virtualized 500 interactive rows
        _scrollViewer = new ScrollViewer { Height = 800 };
        var repeater = new ItemsRepeater();
        repeater.Layout = new StackLayout { Spacing = 0 };
        repeater.ItemsSource = Enumerable.Range(0, ItemCount).ToList();
        repeater.ItemTemplate = new DirectElementFactory();

        _tracker.BeginMount();
        _scrollViewer.Content = repeater;
        root.Children.Add(_scrollViewer);
        Content = root;

        // Mark mount complete after first layout
        var mountTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        mountTimer.Tick += (_, _) =>
        {
            mountTimer.Stop();
            _tracker.EndMount();
        };
        mountTimer.Start();

        CompositionTarget.Rendering += (_, _) =>
        {
            _tracker.FrameRendered();
            _hudFps.Text = $"FPS: {_tracker.CurrentFps:F0}";
            _hudUpdate.Text = $"Update: {_tracker.LastUpdateMs:F2}ms";
            _hudMemory.Text = $"Mem: {_tracker.CurrentMemoryMB}MB";
        };

        // Programmatic scroll at 16ms
        var scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        scrollTimer.Tick += (_, _) =>
        {
            _scrollOffset += 20;
            _scrollViewer.ChangeView(null, _scrollOffset, null, true);
        };
        scrollTimer.Start();

        if (_opts.Headless)
        {
            var shutdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_opts.DurationSeconds) };
            shutdown.Tick += (_, _) =>
            {
                shutdown.Stop();
                scrollTimer.Stop();
                _tracker.WriteReportFile("EXP6_InteractivePool_Direct");
                Close();
            };
            shutdown.Start();
        }
    }
}

internal sealed partial class DirectElementFactory : IElementFactory
{
    public UIElement GetElement(ElementFactoryGetArgs args)
    {
        var i = args.Data is int idx ? idx : 0;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Padding = new Thickness(2), Spacing = 8 };
        row.Children.Add(new Button { Content = $"Action {i}", MinWidth = 80 });
        row.Children.Add(new TextBox { Text = $"Item {i}", Width = 150 });
        row.Children.Add(new ToggleSwitch { IsOn = i % 2 == 0 });
        return row;
    }

    public void RecycleElement(ElementFactoryRecycleArgs args) { }
}
