using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PerfBench.Shared;
using static Microsoft.UI.Reactor.Factories;

namespace PerfBench.PropertyDiff.Reactor;

public record struct CellData(double Value, bool IsUp);

public class PropertyDiffApp : Component
{
    public static BenchCliOptions Opts { get; set; } = new();

    private const int Cols = 80;
    private const int Rows = 60;
    private const int Total = Cols * Rows; // 4800

    private readonly BenchTracker _tracker = new();
    private readonly Random _rng = new(42);

    public override Element Render()
    {
        var (cells, updateCells) = UseReducer(new CellData[Total]);
        var (hudText, setHudText) = UseState("");

        UseEffect(() =>
        {
            _tracker.ResetGcBaseline();
            CompositionTarget.Rendering += (_, _) => _tracker.FrameRendered();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += (_, _) =>
            {
                _tracker.BeginUpdate();

                updateCells(prev =>
                {
                    var next = (CellData[])prev.Clone();
                    int updateCount = (int)(Total * Opts.Percent / 100.0);
                    for (int n = 0; n < updateCount; n++)
                    {
                        int idx = _rng.Next(Total);
                        double newVal = _rng.NextDouble() * 100.0;
                        next[idx] = new CellData(newVal, newVal >= 50.0);
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
                    var suffix = global::Microsoft.UI.Reactor.Core.Reconciler.EnableBitmaskDiff ? "BitmaskOn" : "BitmaskOff";
                    _tracker.WriteReportFile($"EXP2_PropertyDiff_Reactor_{suffix}");
                    Microsoft.UI.Xaml.Application.Current.Exit();
                };
                shutdown.Start();
            }

            return () => timer.Stop();
        });

        var columns = Enumerable.Range(0, Cols).Select(_ => GridSize.Star()).ToArray();
        var rows = Enumerable.Range(0, Rows).Select(_ => GridSize.Star()).ToArray();

        var elements = new Element[Total];
        for (int i = 0; i < Total; i++)
        {
            int col = i % Cols;
            int row = i / Cols;
            var cd = cells[i];
            elements[i] = TextBlock($"Cell {i}: {cd.Value:F2}")
                .FontSize(8)
                .Foreground(cd.IsUp ? "Green" : "Red")
                .Grid(row: row, column: col);
        }

        return VStack(
            Grid(columns, rows, elements),
            Opts.Headless
                ? null!
                : TextBlock(hudText).Foreground("Yellow").FontSize(14)
        );
    }
}
