using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;
using PerfBench.Shared;
using static Microsoft.UI.Reactor.Factories;

public record JournalCell(string Text, string Color);

public class JournalApp : Component
{
    public static BenchCliOptions Opts { get; set; } = new();

    private const int Columns = 80;
    private const int Rows = 60;
    private const int Count = Columns * Rows; // 4800

    private readonly BenchTracker _tracker = new();
    private readonly Random _rng = new(42);
    private int _tick;

    public override Element Render()
    {
        var (cells, setCells) = UseState(Enumerable.Range(0, Count)
            .Select(_ => new JournalCell("000", "White")).ToArray());

        UseEffect(() =>
        {
            _tracker.ResetGcBaseline();

            CompositionTarget.Rendering += (_, _) => _tracker.FrameRendered();

            var timer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += (_, _) =>
            {
                _tick++;
                _tracker.BeginUpdate();

                var newCells = (JournalCell[])cells.Clone();
                int mutations = 0;
                double updatePercent = Opts.Percent / 100.0;

                for (int i = 0; i < Count; i++)
                {
                    if (_rng.NextDouble() < updatePercent)
                    {
                        string color = (_tick + i) % 2 == 0 ? "OrangeRed" : "LimeGreen";
                        newCells[i] = new JournalCell($"{_tick % 1000:D3}", color);
                        mutations += 2; // text + foreground
                    }
                }

                _tracker.RecordMutations(mutations);
                setCells(newCells);
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
                    _tracker.WriteReportFile("EXP5_Journal_Reactor");
                    Microsoft.UI.Xaml.Application.Current.Exit();
                };
                shutdown.Start();
            }

            return () => timer.Stop();
        });

        var elements = new Element[Count];
        for (int i = 0; i < Count; i++)
        {
            elements[i] = TextBlock(cells[i].Text)
                .FontSize(8)
                .Foreground(cells[i].Color)
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
            Grid(colDefs, rowDefs, elements)
        );
    }
}
