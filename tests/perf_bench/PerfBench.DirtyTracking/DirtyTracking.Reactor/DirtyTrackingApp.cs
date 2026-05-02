using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PerfBench.Shared;
using static Microsoft.UI.Reactor.Factories;

namespace PerfBench.DirtyTracking.Reactor;

public class DirtyTrackingApp : Component
{
    public static BenchCliOptions Opts { get; set; } = new();

    private const int Cols = 10;
    private const int Rows = 20;
    private const int Total = Cols * Rows; // 200

    private readonly BenchTracker _tracker = new();
    private readonly Random _rng = new(42);

    public override Element Render()
    {
        var (values, updateValues) = UseReducer(new int[Total]);
        var (hudText, setHudText) = UseState("");

        UseEffect(() =>
        {
            _tracker.ResetGcBaseline();
            CompositionTarget.Rendering += (_, _) => _tracker.FrameRendered();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += (_, _) =>
            {
                _tracker.BeginUpdate();

                int idx = _rng.Next(Total);
                updateValues(prev =>
                {
                    var next = (int[])prev.Clone();
                    next[idx]++;
                    return next;
                });

                _tracker.EndUpdate();

                if (!Opts.Headless)
                    setHudText($"FPS: {_tracker.CurrentFps:F0}  Update: {_tracker.LastUpdateMs:F2}ms  Mem: {_tracker.CurrentMemoryMB}MB");
            };
            timer.Start();

            if (Opts.Headless)
            {
                var shutdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Opts.DurationSeconds) };
                shutdown.Tick += (_, _) =>
                {
                    shutdown.Stop();
                    timer.Stop();
                    _tracker.WriteReportFile("EXP1_DirtyTracking_Reactor");
                    Microsoft.UI.Xaml.Application.Current.Exit();
                };
                shutdown.Start();
            }

            return () => timer.Stop();
        });

        var columns = Enumerable.Range(0, Cols).Select(_ => GridSize.Star()).ToArray();
        var rows = Enumerable.Range(0, Rows).Select(_ => GridSize.Star()).ToArray();

        var cells = new Element[Total + 1];
        for (int i = 0; i < Total; i++)
        {
            int col = i % Cols;
            int row = i / Cols;
            cells[i] = TextBlock($"Counter {i}: {values[i]}")
                .FontSize(10)
                .Grid(row: row, column: col);
        }

        // HUD overlay
        cells[Total] = Opts.Headless
            ? null!
            : TextBlock(hudText)
                .Foreground("Yellow")
                .FontSize(14);

        return Grid(columns, rows, cells);
    }
}
