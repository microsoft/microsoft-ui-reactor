// BlankWinUI3 — startup-only baseline using a hand-rolled WinUI 3 host.
//
// NOTE: -lift's WinUI3 variant is C++/WinRT packaged (MSIX).  This one is
// C# AOT unpackaged so it fits the rest of this repo's build conventions.
// The CLR/AOT bootstrap will add a small fixed cost vs pure C++; relative
// comparisons (BlankWinUI3 vs BlankReactor vs BlankRNW) are still apples-to-
// apples because all three pay the same managed/native bootstrap cost
// (Reactor is also C# AOT, RNW is C++ but loads Hermes which dwarfs CLR).
//
// Methodology mirrors -lift/.../FrameworkBenchmarkBlankApps/WinUI3:
//   wWinMainEntry   → start of Main()  (analogous to wWinMain in -lift)
//   XamlAppLoaded   → App.OnLaunched
//   WindowLoaded    → MainWindow constructor
//   FirstRender     → first CompositionTarget.Rendering after RootGrid.Loaded
//   FirstIdle       → DispatcherQueue Low-priority callback after FirstRender
//   ProcessStop     → app exit

using BenchmarkCommon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

const string AppName = "blank_winui3";

App.Metrics.RecordAppStart();
BenchmarkTracing.Log.SetAppName(AppName);
BenchmarkTracing.Log.TraceWinMainEntry();

WinRT.ComWrappersSupport.InitializeComWrappers();
Microsoft.UI.Xaml.Application.Start(_ =>
{
    var ctx = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
    SynchronizationContext.SetSynchronizationContext(ctx);
    new App();
});

BenchmarkTracing.Log.TraceProcessStop();

// ---------------------------------------------------------------------------

internal sealed partial class App : Application
{
    public static readonly BlankPerfMetrics Metrics = new();
    private Window? _window;

    protected override void OnLaunched(LaunchActivatedEventArgs e)
    {
        BenchmarkTracing.Log.TraceXamlAppLoaded();

        _window = new MainWindow();
        // 1000x1000 to match BlankReactor + BlankRNW. Same surface area
        // across stacks so first-paint cost and working set are
        // comparable.
        _window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 1000));
        _window.Activate();
    }
}

internal sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        Title = "BlankWinUI3 Startup Benchmark";

        var rootGrid = new Grid { Padding = new Thickness(12) };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new TextBlock
        {
            Text = "Blank WinUI3 Startup Benchmark",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(heading, 0);
        rootGrid.Children.Add(heading);

        Content = rootGrid;

        BenchmarkTracing.Log.TraceWindowLoaded();

        // First-paint hook.  CompositionTarget.Rendering fires every frame —
        // detach after the first one to avoid noise.
        rootGrid.Loaded += (_, _) =>
        {
            EventHandler<object>? renderHandler = null;
            renderHandler = (_, _) =>
            {
                if (App.Metrics.IsFirstFrameRecorded) return;

                App.Metrics.RecordFirstFrame(); // emits FirstRender ETW
                CompositionTarget.Rendering -= renderHandler;

                DispatcherQueue.TryEnqueue(
                    DispatcherQueuePriority.Low,
                    () =>
                    {
                        if (App.Metrics.IsFinalized) return;
                        App.Metrics.RecordInteractive(); // emits FirstIdle ETW

                        // Inject metrics from code (not XAML) to keep the pre-paint
                        // tree as small as -lift's blank app.
                        var firstFrameTb = new TextBlock
                        {
                            FontSize = 11,
                            Margin = new Thickness(0, 0, 12, 0),
                            Text = $"First Frame: {App.Metrics.FirstFrameMs} ms",
                        };
                        var interactiveTb = new TextBlock
                        {
                            FontSize = 11,
                            Text = $"Interactive: {App.Metrics.InteractiveMs} ms",
                        };
                        var panel = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Padding = new Thickness(8, 2, 8, 2),
                        };
                        panel.Children.Add(firstFrameTb);
                        panel.Children.Add(interactiveTb);
                        rootGrid.Children.Add(panel);
                    });
            };
            CompositionTarget.Rendering += renderHandler;
        };
    }
}
