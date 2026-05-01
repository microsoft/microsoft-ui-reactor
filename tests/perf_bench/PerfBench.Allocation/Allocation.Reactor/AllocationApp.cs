using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PerfBench.Shared;
using static Microsoft.UI.Reactor.Factories;

namespace PerfBench.Allocation.Reactor;

public record AllocCell(int Value, string Color);

public class AllocationApp : Component
{
    public static BenchCliOptions Opts { get; set; } = new();

    private const int Cols = 80;
    private const int Rows = 60;
    private const int Total = Cols * Rows; // 4800

    private static readonly string[] Colors = { "Red", "Green", "Blue", "White" };

    private readonly BenchTracker _tracker = new();

    private static AllocCell[] CreateInitialCells()
    {
        var initial = new AllocCell[Total];
        for (int i = 0; i < Total; i++)
            initial[i] = new AllocCell(0, "White");
        return initial;
    }

    public override Element Render()
    {
        var (cells, updateCells) = UseReducer(CreateInitialCells());
        var (hudText, setHudText) = UseState("");

        UseEffect(() =>
        {
            _tracker.ResetGcBaseline();
            CompositionTarget.Rendering += (_, _) => _tracker.FrameRendered();

            // 30Hz timer - allocates new array each time (worst case GC pressure)
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += (_, _) =>
            {
                _tracker.BeginUpdate();

                updateCells(prev =>
                {
                    var next = new AllocCell[Total];
                    for (int i = 0; i < Total; i++)
                    {
                        int newVal = prev[i].Value + 1;
                        next[i] = new AllocCell(newVal, Colors[newVal % Colors.Length]);
                    }
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
                    _tracker.WriteReportFile("EXP10_Allocation_Reactor");
                    Microsoft.UI.Xaml.Application.Current.Exit();
                };
                shutdown.Start();
            }

            return () => timer.Stop();
        });

        var columns = Enumerable.Range(0, Cols).Select(_ => GridSize.Star()).ToArray();
        var rows = Enumerable.Range(0, Rows).Select(_ => GridSize.Star()).ToArray();

        var elements = new Element[Total + 1];
        for (int i = 0; i < Total; i++)
        {
            int col = i % Cols;
            int row = i / Cols;
            elements[i] = TextBlock((cells[i].Value % 1000).ToString())
                .FontSize(8)
                .Foreground(cells[i].Color)
                .Grid(row: row, column: col);
        }

        // HUD overlay
        elements[Total] = Opts.Headless
            ? null!
            : TextBlock(hudText)
                .Foreground("Yellow")
                .FontSize(14);

        return Grid(columns, rows, elements);
    }
}
