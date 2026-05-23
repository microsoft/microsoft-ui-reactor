using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;
using PerfBench.Shared;
using static Microsoft.UI.Reactor.Factories;

public record InteractiveItem(string ButtonLabel, string TextValue, bool IsToggled);

public class InteractivePoolApp : Component
{
    public static BenchCliOptions Opts { get; set; } = new();

    private const int ItemCount = 500;

    private readonly BenchTracker _tracker = new();

    public override Element Render()
    {
        var items = Enumerable.Range(0, ItemCount)
            .Select(i => new InteractiveItem($"Action {i}", $"Item {i}", i % 2 == 0))
            .ToArray();

        var scrollRef = UseRef<Microsoft.UI.Xaml.Controls.ScrollViewer>(null!);

        UseEffect(() =>
        {
            _tracker.ResetGcBaseline();
            _tracker.BeginMount();

            CompositionTarget.Rendering += (_, _) => _tracker.FrameRendered();

            // Mark mount complete after first render
            var mountTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            mountTimer.Tick += (_, _) =>
            {
                mountTimer.Stop();
                _tracker.EndMount();
            };
            mountTimer.Start();

            // Programmatic scroll — use the LazyVStack's own ScrollViewer
            double scrollOffset = 0;
            var scrollTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            scrollTimer.Tick += (_, _) =>
            {
                scrollOffset += 20;
                var sv = scrollRef.Current;
                if (sv != null)
                {
                    sv.ChangeView(null, scrollOffset, null, true);
                }
            };
            scrollTimer.Start();

            if (Opts.Headless)
            {
                var shutdown = new Microsoft.UI.Xaml.DispatcherTimer
                    { Interval = TimeSpan.FromSeconds(Opts.DurationSeconds) };
                shutdown.Tick += (_, _) =>
                {
                    shutdown.Stop();
                    scrollTimer.Stop();
                    var suffix = Opts.Optimization == "on" ? "Pool" : "NoPool";
                    _tracker.WriteReportFile($"EXP6_InteractivePool_Reactor_{suffix}");
                    Microsoft.UI.Xaml.Application.Current.Exit();
                };
                shutdown.Start();
            }

            return () => scrollTimer.Stop();
        });

        return VStack(
            HStack(
                TextBlock($"FPS: {_tracker.CurrentFps:F0}").Width(120),
                TextBlock($"Update: {_tracker.LastUpdateMs:F2}ms").Width(150),
                TextBlock($"Mem: {_tracker.CurrentMemoryMB}MB").Width(150)
            ).Padding(4),
            LazyVStack<InteractiveItem>(
                items,
                item => item.ButtonLabel,
                (item, i) => HStack(8,
                    Button(item.ButtonLabel, () => { }),
                    TextBox(item.TextValue, _ => { }),
                    ToggleSwitch(item.IsToggled, _ => { })
                ).Padding(2)
            ).Set(sv => scrollRef.Current = sv).Height(800)
        );
    }
}
