using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Reactor.Charting.Accessibility;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Charting.Charts;
using Microsoft.UI.Xaml;

ReactorApp.Run<ChartingApp>("Charting", width: 700, height: 800
);

record SalesPoint(double Month, double Revenue);
record CategoryData(string Name, double Value);

// <snippet:line-chart>
class LineChartDemo : Component
{
    private static readonly SalesPoint[] Data =
    [
        new(1, 120), new(2, 180), new(3, 150),
        new(4, 220), new(5, 310), new(6, 280),
        new(7, 350), new(8, 400), new(9, 380),
        new(10, 420), new(11, 460), new(12, 510)
    ];

    public override Element Render()
    {
        return VStack(12,
            SubHeading("Line Chart"),
            LineChart(Data, d => d.Month, d => d.Revenue)
                .Title("Monthly Revenue — Line")
                .SeriesName("Revenue")
                .Units("months", "USD")
                .AxisLabel(ChartAxisType.X, "Month")
                .AxisLabel(ChartAxisType.Y, "Revenue (USD)")
                .Width(600).Height(250)
                .Stroke("#0078D4").StrokeWidth(2.5)
                .ShowGrid(true).ShowAxes(true)
        ).Padding(24);
    }
}
// </snippet:line-chart>

// <snippet:bar-chart>
class BarChartDemo : Component
{
    private static readonly SalesPoint[] Data =
    [
        new(1, 340), new(2, 420), new(3, 510), new(4, 380)
    ];

    public override Element Render()
    {
        return VStack(12,
            SubHeading("Bar Chart"),
            BarChart(Data, d => d.Month, d => d.Revenue)
                .Title("Quarterly Revenue — Bar")
                .SeriesName("Revenue")
                .Units("quarters", "USD")
                .AxisLabel(ChartAxisType.X, "Quarter")
                .AxisLabel(ChartAxisType.Y, "Revenue (USD)")
                .Width(600).Height(250)
                .Fill("#50C878")
                .ShowGrid(true).ShowAxes(true)
        ).Padding(24);
    }
}
// </snippet:bar-chart>

// <snippet:area-chart>
class AreaChartDemo : Component
{
    private static readonly SalesPoint[] Data =
    [
        new(1, 50), new(2, 120), new(3, 200),
        new(4, 350), new(5, 480), new(6, 600),
        new(7, 720), new(8, 850), new(9, 1020),
        new(10, 1150), new(11, 1300), new(12, 1500)
    ];

    public override Element Render()
    {
        return VStack(12,
            SubHeading("Area Chart"),
            AreaChart(Data, d => d.Month, d => d.Revenue)
                .Title("Monthly Revenue — Area")
                .SeriesName("Revenue")
                .Units("months", "USD")
                .AxisLabel(ChartAxisType.X, "Month")
                .AxisLabel(ChartAxisType.Y, "Revenue (USD)")
                .Width(600).Height(250)
                .Stroke("#9B59B6").Fill("#9B59B6")
                .FillOpacity(0.2)
                .ShowGrid(true).ShowAxes(true)
        ).Padding(24);
    }
}
// </snippet:area-chart>

// <snippet:pie-chart>
class PieChartDemo : Component
{
    private static readonly CategoryData[] Data =
    [
        new("Engineering", 42),
        new("Marketing", 18),
        new("Sales", 25),
        new("Support", 15)
    ];

    public override Element Render()
    {
        return VStack(12,
            SubHeading("Pie Chart"),
            PieChart(Data, d => d.Value, d => d.Name)
                .Title("Team Distribution")
                .Description("Pie chart showing team size across Engineering, Marketing, Sales, and Support.")
                .Width(300).Height(300)
                .InnerRadius(60)
                .PadAngle(0.03)
        ).Padding(24);
    }
}
// </snippet:pie-chart>

// <snippet:combined-chart>
class CombinedChartDemo : Component
{
    private static readonly SalesPoint[] Data2024 =
    [
        new(1, 100), new(2, 140), new(3, 180),
        new(4, 200), new(5, 260), new(6, 300)
    ];

    private static readonly SalesPoint[] Data2025 =
    [
        new(1, 160), new(2, 220), new(3, 280),
        new(4, 320), new(5, 390), new(6, 450)
    ];

    private static readonly string[] Years = ["2024", "2025"];

    public override Element Render()
    {
        var (year, setYear) = UseState(0);
        var data = year == 0 ? Data2024 : Data2025;

        return VStack(12,
            SubHeading("Interactive Chart"),
            ComboBox(Years, year, setYear),
            AreaChart(data, d => d.Month, d => d.Revenue)
                .Title("Revenue by Year")
                .SeriesName("Revenue")
                .Units("months", "USD")
                .Interactive()
                .Width(600).Height(250)
                .Stroke("#0078D4").Fill("#0078D4")
                .FillOpacity(0.15)
                .ShowGrid(true).ShowAxes(true)
        ).Padding(24);
    }
}
// </snippet:combined-chart>

// <snippet:dynamic-data>
class DynamicDataDemo : Component
{
    private static readonly List<SalesPoint> InitialPoints =
        Enumerable.Range(1, 8)
            .Select(i => new SalesPoint(i, Random.Shared.Next(50, 500)))
            .ToList();

    public override Element Render()
    {
        var (points, updatePoints) = UseReducer(InitialPoints);

        return VStack(12,
            SubHeading("Dynamic Data"),
            Button("Randomize", () => updatePoints(_ =>
                Enumerable.Range(1, 8)
                    .Select(i => new SalesPoint(i, Random.Shared.Next(50, 500)))
                    .ToList())),
            BarChart<SalesPoint>(points, d => d.Month, d => d.Revenue)
                .Title("Dynamic Revenue Data")
                .SeriesName("Revenue")
                .Units("months", "USD")
                .Width(600).Height(250)
                .Fill("#E74C3C")
                .ShowGrid(true).ShowAxes(true)
        ).Padding(24);
    }
}
// </snippet:dynamic-data>

/// <summary>
/// Demonstrates <see cref="PieChartElement{T}.LabelView"/> — replace the built-in
/// text slice label with any Element. The string LabelAccessor is still passed so
/// screen readers describe the slice correctly.
/// </summary>
class PieLabelViewDemo : Component
{
    private static readonly CategoryData[] Data =
    [
        new("Engineering", 42), new("Sales", 25),
        new("Marketing", 18),   new("Support", 15)
    ];

    public override Element Render()
    {
        return VStack(12,
            SubHeading("Pie LabelView"),
            // <snippet:pie-label-view>
            // Percent rendered inside the slice. The string label accessor
            // is still passed so screen readers describe the slice.
            PieChart(Data, d => d.Value, d => d.Name)
                .Title("Team Distribution")
                .Width(300).Height(300)
                .InnerRadius(50).PadAngle(0.02)
                .LabelView((d, layout) =>
                    TextBlock($"{layout.Fraction:P0}")
                        .FontSize(12).Bold().Foreground(Theme.AccentText))
            // </snippet:pie-label-view>
        ).Padding(24);
    }
}

/// <summary>
/// Demonstrates <see cref="ChartElement{T}.XTickLabelView"/> — replace the numeric
/// X-axis tick label with any Element.
/// </summary>
class AxisTickViewDemo : Component
{
    private static readonly SalesPoint[] Data =
    [
        new(1, 120), new(2, 180), new(3, 150),
        new(4, 220), new(5, 310), new(6, 280)
    ];

    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun"];

    public override Element Render()
    {
        return VStack(12,
            SubHeading("Axis XTickLabelView"),
            // <snippet:axis-tick-view>
            // X axis ticks: render month name plus a caption per tick.
            LineChart(Data, d => d.Month, d => d.Revenue)
                .Title("Revenue by Month")
                .SeriesName("Revenue")
                .Width(600).Height(220)
                .Stroke("#0078D4").StrokeWidth(2.5)
                .ShowGrid(true).ShowAxes(true)
                .XTickLabelView(t => VStack(2,
                    TextBlock(Months[Math.Clamp((int)t - 1, 0, Months.Length - 1)])
                        .FontSize(11).SemiBold(),
                    TextBlock("month").FontSize(8).Opacity(0.6)))
            // </snippet:axis-tick-view>
        ).Padding(24);
    }
}

// <snippet:accessible-chart>
/// <summary>
/// Canonical accessible chart pattern — demonstrates all recommended accessibility
/// modifiers for both static and interactive charts. Follow this pattern to ensure
/// charts are fully accessible to screen readers, keyboard users, and users who
/// need forced-colors or reduced-motion.
/// </summary>
class AccessibleChartDemo : Component
{
    private static readonly SalesPoint[] Data =
    [
        new(1, 120), new(2, 180), new(3, 150),
        new(4, 220), new(5, 310), new(6, 280)
    ];

    public override Element Render()
    {
        return VStack(12,
            SubHeading("Accessible Chart"),

            // Static accessible chart: Title + SeriesName + Units
            LineChart(Data, d => d.Month, d => d.Revenue)
                .Title("Monthly Revenue 2024")
                .SeriesName("Revenue")
                .Units("months", "USD")
                .AxisLabel(ChartAxisType.X, "Month")
                .AxisLabel(ChartAxisType.Y, "Revenue (USD)")
                .Width(600).Height(250)
                .Stroke("#0078D4").StrokeWidth(2.5)
                .ShowGrid(true).ShowAxes(true),

            // Interactive accessible chart: adds keyboard nav and point invocation
            BarChart(Data, d => d.Month, d => d.Revenue)
                .Title("Monthly Revenue — Interactive")
                .SeriesName("Revenue")
                .Units("months", "USD")
                .Interactive()
                .Width(600).Height(250)
                .Fill("#50C878")
                .ShowGrid(true).ShowAxes(true)
        ).Padding(24);
    }
}
// </snippet:accessible-chart>

// <snippet:live-chart>
record Sample(DateTime Time, double Value);

class LiveChartDemo : Component
{
    public override Element Render()
    {
        var (samples, updateSamples) = UseReducer<IReadOnlyList<Sample>>(Array.Empty<Sample>());

        // UseEffect takes a synchronous Action/Func<Action> — never an async lambda.
        // Start the pump from the effect and cancel it from the returned cleanup.
        UseEffect(() =>
        {
            var cts = new CancellationTokenSource();
            _ = PumpAsync(cts.Token);
            return () => { cts.Cancel(); };
        }, Array.Empty<object>());

        return VStack(12,
            SubHeading("Live feed"),
            LineChart(samples, s => s.Time.Ticks, s => s.Value)
                .Title("Live feed")
                .SeriesName("Value")
                .Width(600).Height(220)
                .Stroke("#0078D4")
        ).Padding(24);

        async Task PumpAsync(CancellationToken token)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    updateSamples(prev =>
                    {
                        var next = prev.Append(new Sample(DateTime.Now, Random.Shared.Next(0, 100))).ToList();
                        return next.Count > 60 ? next.Skip(next.Count - 60).ToList() : next;
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on unmount.
            }
        }
    }
}
// </snippet:live-chart>

// <snippet:chart-switching>
class ChartSwitchDemo : Component
{
    private static readonly SalesPoint[] Data =
    [
        new(1, 120), new(2, 180), new(3, 150), new(4, 220), new(5, 310), new(6, 280)
    ];

    public override Element Render()
    {
        // ComboBox reports the selected *index*, so hold an int and branch on it.
        var (kind, setKind) = UseState(0);
        var kinds = new[] { "line", "bar", "area" };

        // Each factory returns ChartElement<T>, so the switch has one common type
        // and the modifier chain below is shared.
        ChartElement<SalesPoint> chart = kind switch
        {
            0 => LineChart(Data, d => d.Month, d => d.Revenue),
            1 => BarChart(Data, d => d.Month, d => d.Revenue),
            _ => AreaChart(Data, d => d.Month, d => d.Revenue),
        };

        return VStack(8,
            SubHeading("Switch chart type"),
            ComboBox(kinds, kind, setKind),
            chart
                .Title("Revenue")
                .SeriesName("Revenue")
                .Width(600).Height(250)
                .Stroke("#0078D4")
        ).Padding(24);
    }
}
// </snippet:chart-switching>

// <snippet:d3-custom>
class D3CustomDemo : Component
{
    public override Element Render()
    {
        const double w = 600, h = 240;
        const double left = 50, top = 20, right = 20, bottom = 40;
        double plotW = w - left - right, plotH = h - top - bottom;

        var data = Enumerable.Range(0, 40)
            .Select(i => (x: (double)i, y: 40 + 30 * Math.Sin(i / 4.0) + i))
            .ToArray();

        var (yMin, yMax) = D3Extent.Extent(data.Select(d => d.y));

        // Scales are plain objects: Set* mutates fluently, Map projects a value.
        var xs = new LinearScale([0, 39], [left, left + plotW]);
        var ys = new LinearScale([yMax, yMin], [top, top + plotH]).Nice();

        var line = D3Charts.Brush("#0078D4");

        return VStack(12,
            SubHeading("Custom D3 canvas"),
            D3Charts.D3Canvas(w, h,
                [.. D3Charts.D3Grid(ys, left, plotW),
                 .. D3Charts.D3Axes(xs, ys, left, top, plotW, plotH),
                 D3Charts.D3LinePath(data, x: d => xs.Map(d.x), y: d => ys.Map(d.y),
                     stroke: line, strokeWidth: 2),
                 .. data.Select(d => (Element)(D3Charts.D3Circle(xs.Map(d.x), ys.Map(d.y), 3)
                     with { Fill = line }))])
        ).Padding(24);
    }
}
// </snippet:d3-custom>

class ChartingApp : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(24,
                Heading("Charting"),
                Component<LineChartDemo>(),
                Component<BarChartDemo>(),
                Component<AreaChartDemo>(),
                Component<PieChartDemo>(),
                Component<CombinedChartDemo>(),
                Component<DynamicDataDemo>(),
                Component<PieLabelViewDemo>(),
                Component<AxisTickViewDemo>(),
                Component<AccessibleChartDemo>(),
                Component<ChartSwitchDemo>(),
                Component<D3CustomDemo>(),
                Component<LiveChartDemo>()
            ).Padding(24)
        );
    }
}
