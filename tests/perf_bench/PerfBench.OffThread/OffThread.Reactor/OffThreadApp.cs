using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;
using PerfBench.Shared;
using static Microsoft.UI.Reactor.Factories;

public class OffThreadApp : Component
{
    public static BenchCliOptions Opts { get; set; } = new();

    private const int Columns = 40;
    private const int Rows = 25;
    private const int Count = Columns * Rows; // 1000

    private readonly BenchTracker _tracker = new();
    private int _tick;

    public override Element Render()
    {
        var (values, setValues) = UseState(new double[Count]);

        UseEffect(() =>
        {
            _tracker.ResetGcBaseline();

            CompositionTarget.Rendering += (_, _) => _tracker.FrameRendered();

            var timer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += (_, _) =>
            {
                _tick++;
                _tracker.BeginUpdate();
                _tracker.BeginUiBlock();

                var newValues = new double[Count];
                for (int i = 0; i < Count; i++)
                {
                    double t = _tick * 0.1 + i;
                    newValues[i] = Math.Sin(t) * Math.Cos(t * 0.7) + Math.Sqrt(Math.Abs(Math.Sin(t * 0.3)));
                }
                setValues(newValues);

                _tracker.EndUiBlock();
                _tracker.EndUpdate();
            };
            timer.Start();

            if (Opts.Headless)
            {
                var shutdown = new Microsoft.UI.Xaml.DispatcherTimer
                    { Interval = TimeSpan.FromSeconds(Opts.DurationSeconds) };
                shutdown.Tick += (_, _) =>
                {
                    shutdown.Stop();
                    timer.Stop();
                    _tracker.WriteReportFile("EXP4_OffThread_Reactor");
                    Microsoft.UI.Xaml.Application.Current.Exit();
                };
                shutdown.Start();
            }

            return () => timer.Stop();
        });

        var cells = new Element[Count];
        for (int i = 0; i < Count; i++)
        {
            cells[i] = TextBlock($"Item {i}: {values[i]:F2}")
                .FontSize(10)
                .Padding(1)
                .Grid(row: i / Columns, column: i % Columns);
        }

        var colDefs = Enumerable.Repeat(GridSize.Star(), Columns).ToArray();
        var rowDefs = Enumerable.Repeat(GridSize.Star(), Rows).ToArray();

        return VStack(
            HStack(
                TextBlock($"FPS: {_tracker.CurrentFps:F0}").Width(120),
                TextBlock($"Update: {_tracker.LastUpdateMs:F2}ms").Width(150),
                TextBlock($"Mem: {_tracker.CurrentMemoryMB}MB").Width(150)
            ).Padding(4),
            Grid(colDefs, rowDefs, cells)
        );
    }
}
