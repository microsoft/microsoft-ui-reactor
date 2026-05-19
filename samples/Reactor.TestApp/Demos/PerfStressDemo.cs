using System.Diagnostics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;
using WinMedia = Microsoft.UI.Xaml.Media;

class PerfStressDemo : Component
{
    record SortState(int[] Values, int[] Colors, int Pivot, int Left, int Right, bool Sorted);

    static readonly string[] BarColors =
    [
        "#4fc3f7", "#81c784", "#fff176", "#ff8a65", "#ba68c8",
        "#4dd0e1", "#aed581", "#ffd54f", "#e57373", "#9575cd",
    ];

    // Brush cache: at 500 bars × 3 colored sub-elements per bar, the string
    // overload of .Background() would allocate ~1500 SolidColorBrush instances
    // per render tick — and because each new brush is reference-different,
    // UpdateBorder writes Background unconditionally and WinUI re-paints every
    // border every tick. Caching one brush per color string per host thread
    // collapses that to ~1500 dictionary lookups per render and lets the
    // reconciler's reference-equality check on Background short-circuit.
    //
    // SolidColorBrush is a DependencyObject — it has thread affinity, so the
    // cache is keyed by managed thread id. PerfStress only renders on one UI
    // thread; the per-thread keying is defensive for hot reload / multi-window
    // futures.
    [ThreadStatic]
    private static Dictionary<string, WinMedia.SolidColorBrush>? t_brushCache;

    static WinMedia.SolidColorBrush Brush(string color)
    {
        var cache = t_brushCache ??= new Dictionary<string, WinMedia.SolidColorBrush>(StringComparer.OrdinalIgnoreCase);
        if (!cache.TryGetValue(color, out var brush))
        {
            brush = BrushHelper.Parse(color);
            cache[color] = brush;
        }
        return brush;
    }

    public override Element Render()
    {
        var (elementCount, setElementCount) = UseState(100);
        var (running, setRunning) = UseState(false);
        var (sortState, setSortState) = UseReducer<SortState?>(null);
        var (renderTimes, setRenderTimes) = UseReducer(new List<double>());
        var (totalSwaps, setTotalSwaps) = UseState(0);
        var (stepCount, setStepCount) = UseState(0);
        var (showLabels, setShowLabels) = UseState(false);
        var (showBorders, setShowBorders) = UseState(true);
        var (tickMs, setTickMs) = UseState(16);
        var (totalSortMs, setTotalSortMs) = UseState(0.0);

        void StartSort()
        {
            var rng = new Random(42); // deterministic seed for reproducible results
            var values = Enumerable.Range(1, elementCount).ToArray();
            // Fisher-Yates shuffle
            for (int i = values.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
            var colors = new int[elementCount];
            setSortState(_ => new SortState(values, colors, -1, -1, -1, false));
            setRenderTimes(_ => new List<double>());
            setTotalSwaps(0);
            setStepCount(0);
            setTotalSortMs(0);
            setRunning(true);
            RunQuicksort(values, colors);
        }

        async void RunQuicksort(int[] values, int[] colors)
        {
            var totalTimer = Stopwatch.StartNew();
            var sw = new Stopwatch();
            int swaps = 0;
            int steps = 0;

            async Task QSort(int lo, int hi)
            {
                if (lo >= hi || lo < 0) return;

                // Partition
                int pivot = values[hi];
                int pivotColor = 1; // green = pivot
                colors[hi] = pivotColor;

                int i = lo;
                for (int j = lo; j < hi; j++)
                {
                    colors[j] = 2; // yellow = comparing
                    if (values[j] <= pivot)
                    {
                        // Swap
                        (values[i], values[j]) = (values[j], values[i]);
                        (colors[i], colors[j]) = (colors[j], colors[i]);
                        colors[i] = 3; // orange = swapped
                        i++;
                        swaps++;
                    }

                    steps++;
                    if (steps % Math.Max(1, elementCount / 20) == 0)
                    {
                        // Emit a render tick
                        sw.Restart();
                        setSortState(_ => new SortState(
                            (int[])values.Clone(),
                            (int[])colors.Clone(),
                            hi, lo, i, false));
                        setTotalSwaps(swaps);
                        setStepCount(steps);
                        sw.Stop();
                        setRenderTimes(list =>
                        {
                            var copy = new List<double>(list) { sw.Elapsed.TotalMilliseconds };
                            if (copy.Count > 200) copy.RemoveAt(0);
                            return copy;
                        });
                        await Task.Delay(tickMs);
                    }
                }

                // Final pivot swap
                (values[i], values[hi]) = (values[hi], values[i]);
                (colors[i], colors[hi]) = (colors[hi], colors[i]);
                swaps++;

                // Mark sorted partition
                colors[i] = 4; // purple = in final position

                sw.Restart();
                setSortState(_ => new SortState(
                    (int[])values.Clone(),
                    (int[])colors.Clone(),
                    i, lo, hi, false));
                setTotalSwaps(swaps);
                setStepCount(steps);
                sw.Stop();
                setRenderTimes(list =>
                {
                    var copy = new List<double>(list) { sw.Elapsed.TotalMilliseconds };
                    if (copy.Count > 200) copy.RemoveAt(0);
                    return copy;
                });
                await Task.Delay(tickMs);

                // Reset colors for next partition
                for (int k = lo; k <= hi; k++)
                    if (colors[k] != 4) colors[k] = 0;

                await QSort(lo, i - 1);
                await QSort(i + 1, hi);
            }

            await QSort(0, values.Length - 1);

            // Mark all sorted
            totalTimer.Stop();
            for (int k = 0; k < colors.Length; k++) colors[k] = 4;
            setSortState(_ => new SortState(
                (int[])values.Clone(),
                (int[])colors.Clone(),
                -1, -1, -1, true));
            setTotalSortMs(totalTimer.Elapsed.TotalMilliseconds);
            setRunning(false);
        }

        // Compute stats
        double avgMs = renderTimes.Count > 0 ? renderTimes.Average() : 0;
        double maxMs = renderTimes.Count > 0 ? renderTimes.Max() : 0;
        double p95Ms = 0;
        if (renderTimes.Count > 0)
        {
            var sorted = renderTimes.OrderBy(x => x).ToList();
            p95Ms = sorted[(int)(sorted.Count * 0.95)];
        }

        return ScrollView(VStack(12,
            Heading("Performance Stress Test"),
            TextBlock("Quicksort visualization — stresses tree diffing with many simultaneous property changes, " +
                 "element creation/removal, and structural mutations."),

            // Controls
            HStack(12,
                VStack(4,
                    TextBlock("Elements:"),
                    HStack(8,
                        Button("10", () => { if (!running) setElementCount(10); }).IsEnabled(!(running || elementCount == 10)),
                        Button("50", () => { if (!running) setElementCount(50); }).IsEnabled(!(running || elementCount == 50)),
                        Button("100", () => { if (!running) setElementCount(100); }).IsEnabled(!(running || elementCount == 100)),
                        Button("250", () => { if (!running) setElementCount(250); }).IsEnabled(!(running || elementCount == 250)),
                        Button("500", () => { if (!running) setElementCount(500); }).IsEnabled(!(running || elementCount == 500)),
                        Button("1000", () => { if (!running) setElementCount(1000); }).IsEnabled(!(running || elementCount == 1000))
                    )
                ),
                VStack(4,
                    TextBlock("Tick interval:"),
                    HStack(8,
                        Slider(tickMs, 0, 100, v => { if (!running) setTickMs((int)v); }).Width(150),
                        TextBlock($"{tickMs}ms")
                    )
                )
            ),

            HStack(12,
                CheckBox(showLabels, v => setShowLabels(v), label: "Show value labels"),
                CheckBox(showBorders, v => setShowBorders(v), label: "Show bar gaps")
            ),

            HStack(8,
                Button("Start Sort", StartSort).IsEnabled(!running),
                Button("Reset", () =>
                {
                    setSortState(_ => null);
                    setRenderTimes(_ => new List<double>());
                    setTotalSwaps(0);
                    setStepCount(0);
                }).IsEnabled(!running)
            ),

            // Status
            sortState?.Sorted == true
                ? TextBlock($"Sorted in {totalSortMs:F0} ms  ({totalSwaps} swaps, {stepCount} steps)")
                    .SemiBold()
                : running
                    ? TextBlock($"Sorting... step {stepCount}, {totalSwaps} swaps").Foreground(SecondaryText)
                    : Empty(),

            // Visualization area — Expr() (spec 033 §5) keeps the per-bar
            // build locals scoped to the branch that uses them.
            Border(Expr(() =>
            {
                if (sortState is null)
                    return TextBlock("Click 'Start Sort' to begin").Foreground(TertiaryText).MinHeight(220);

                var barElements = new Element[sortState.Values.Length];
                double maxVal = elementCount;
                for (int i = 0; i < sortState.Values.Length; i++)
                {
                    double heightPct = sortState.Values[i] / maxVal * 200;
                    int colorIdx = sortState.Colors[i] % BarColors.Length;
                    bool isPivot = i == sortState.Pivot;
                    bool isActive = i >= sortState.Left && i <= sortState.Right;

                    double barWidth = Math.Max(2, 800.0 / elementCount - (showBorders ? 1 : 0));
                    double barHeight = Math.Max(4, heightPct);
                    double opacity = isPivot ? 1.0 : isActive ? 0.9 : 0.7;
                    int val = sortState.Values[i];

                    // Each bar contains child controls to stress the reconciler:
                    // a tiny progress indicator + a value label + a colored pip.
                    // All Background() calls go through the cached brush so the
                    // reconciler sees stable brush references between renders
                    // and skips redundant WinUI Background writes.
                    Element barContent = VStack(0,
                        // Top: small colored indicator pip (changes with sort state)
                        Border(Empty())
                            .Background(Brush(isPivot ? "#ffffff" : isActive ? "#ffeb3b" : BarColors[(colorIdx + 1) % BarColors.Length]))
                            .CornerRadius(1)
                            .Width(Math.Min(barWidth - 1, 6))
                            .Height(2),
                        // Middle: value label (only when bars are wide enough)
                        barWidth >= 10
                            ? TextBlock($"{val}").FontSize(Math.Min(7, barWidth * 0.8))
                            : Empty(),
                        // Bottom: progress-like fill showing relative position
                        Border(Empty())
                            .Background(Brush(BarColors[(colorIdx + 2) % BarColors.Length]))
                            .CornerRadius(0)
                            .Width(Math.Max(1, barWidth * 0.6))
                            .Height(Math.Max(1, barHeight * 0.15))
                            .Opacity(0.5)
                    );

                    Element bar = Border(barContent)
                        .Background(Brush(BarColors[colorIdx]))
                        .CornerRadius(0)
                        .Width(barWidth)
                        .Height(barHeight)
                        .Opacity(opacity)
                        .VAlign(VerticalAlignment.Bottom);

                    if (showBorders)
                        bar = bar.Margin(0, 0, 1, 0);

                    barElements[i] = bar;
                }
                return HStack(0, barElements).Height(220).VAlign(VerticalAlignment.Bottom);
            }))
                .CornerRadius(8)
                .Background(Brush("#1a1a2e"))
                .Padding(8),

            // Performance stats
            When(renderTimes.Count > 0, () => VStack(4,
                SubHeading("Render Performance"),
                HStack(16,
                    VStack(2,
                        TextBlock("Elements").SemiBold(),
                        TextBlock($"{elementCount}")
                    ),
                    VStack(2,
                        TextBlock("Samples").SemiBold(),
                        TextBlock($"{renderTimes.Count}")
                    ),
                    VStack(2,
                        TextBlock("Avg").SemiBold(),
                        TextBlock($"{avgMs:F2} ms")
                    ),
                    VStack(2,
                        TextBlock("P95").SemiBold(),
                        TextBlock($"{p95Ms:F2} ms")
                    ),
                    VStack(2,
                        TextBlock("Max").SemiBold(),
                        TextBlock($"{maxMs:F2} ms")
                    ),
                    VStack(2,
                        TextBlock("Swaps").SemiBold(),
                        TextBlock($"{totalSwaps}")
                    ),
                    VStack(2,
                        TextBlock("Total").SemiBold(),
                        TextBlock($"{totalSortMs:F0} ms")
                    )
                ),

                // Mini histogram of render times
                Caption("Render time distribution (last 200 ticks):").Foreground(SecondaryText).Margin(0, 8, 0, 0),
                HStack(0,
                    renderTimes.TakeLast(100).Select((t, i) =>
                    {
                        double h = Math.Min(50, t * 10); // 1ms = 10px
                        string color = t < 2 ? "#81c784" : t < 8 ? "#fff176" : t < 16 ? "#ff8a65" : "#e57373";
                        return (Element)Border(Empty())
                            .Background(Brush(color))
                            .CornerRadius(0)
                            .Width(Math.Max(1, 600.0 / 100))
                            .Height(Math.Max(1, h))
                            .VAlign(VerticalAlignment.Bottom);
                    }).ToArray()
                ).Height(60)
            )),

            // Color legend
            HStack(16,
                LegendItem("#4fc3f7", "Default"),
                LegendItem("#81c784", "Pivot"),
                LegendItem("#fff176", "Comparing"),
                LegendItem("#ff8a65", "Swapped"),
                LegendItem("#ba68c8", "Final position")
            ).Margin(0, 8, 0, 0)
        ));
    }

    static Element LegendItem(string color, string label) =>
        HStack(4,
            Border(Empty()).Background(Brush(color)).CornerRadius(2).Width(12).Height(12),
            Caption(label).Foreground(SecondaryText)
        );
}
