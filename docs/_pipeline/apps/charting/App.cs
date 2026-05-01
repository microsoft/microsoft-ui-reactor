using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Reactor.Charting.Accessibility;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Charting.Charts;
using Microsoft.UI.Xaml;

ReactorApp.Run<ChartingApp>("Charting", width: 700, height: 800
#if DEBUG
    , preview: true
#endif
);

record SalesPoint(double Month, double Revenue);
record CategoryData(string Name, double Value);

// <snippet:line-chart>
class LineChartDemo : Component
{
    public override Element Render()
    {
        var data = new SalesPoint[]
        {
            new(1, 120), new(2, 180), new(3, 150),
            new(4, 220), new(5, 310), new(6, 280),
            new(7, 350), new(8, 400), new(9, 380),
            new(10, 420), new(11, 460), new(12, 510)
        };

        return VStack(12,
            SubHeading("Line Chart"),
            LineChart(data, d => d.Month, d => d.Revenue)
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
    public override Element Render()
    {
        var data = new SalesPoint[]
        {
            new(1, 340), new(2, 420), new(3, 510), new(4, 380)
        };

        return VStack(12,
            SubHeading("Bar Chart"),
            BarChart(data, d => d.Month, d => d.Revenue)
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
    public override Element Render()
    {
        var data = new SalesPoint[]
        {
            new(1, 50), new(2, 120), new(3, 200),
            new(4, 350), new(5, 480), new(6, 600),
            new(7, 720), new(8, 850), new(9, 1020),
            new(10, 1150), new(11, 1300), new(12, 1500)
        };

        return VStack(12,
            SubHeading("Area Chart"),
            AreaChart(data, d => d.Month, d => d.Revenue)
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
    public override Element Render()
    {
        var data = new CategoryData[]
        {
            new("Engineering", 42),
            new("Marketing", 18),
            new("Sales", 25),
            new("Support", 15)
        };

        return VStack(12,
            SubHeading("Pie Chart"),
            PieChart(data, d => d.Value, d => d.Name)
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
    public override Element Render()
    {
        var (year, setYear) = UseState(0);
        var years = new[] { "2024", "2025" };

        var data2024 = new SalesPoint[]
        {
            new(1, 100), new(2, 140), new(3, 180),
            new(4, 200), new(5, 260), new(6, 300)
        };
        var data2025 = new SalesPoint[]
        {
            new(1, 160), new(2, 220), new(3, 280),
            new(4, 320), new(5, 390), new(6, 450)
        };

        var data = year == 0 ? data2024 : data2025;

        return VStack(12,
            SubHeading("Interactive Chart"),
            ComboBox(years, year, setYear),
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
    public override Element Render()
    {
        var (points, updatePoints) = UseReducer(
            Enumerable.Range(1, 8)
                .Select(i => new SalesPoint(i, Random.Shared.Next(50, 500)))
                .ToList());

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

// <snippet:accessible-chart>
/// <summary>
/// Canonical accessible chart pattern — demonstrates all recommended accessibility
/// modifiers for both static and interactive charts. Follow this pattern to ensure
/// charts are fully accessible to screen readers, keyboard users, and users who
/// need forced-colors or reduced-motion.
/// </summary>
class AccessibleChartDemo : Component
{
    public override Element Render()
    {
        var data = new SalesPoint[]
        {
            new(1, 120), new(2, 180), new(3, 150),
            new(4, 220), new(5, 310), new(6, 280)
        };

        return VStack(12,
            SubHeading("Accessible Chart"),

            // Static accessible chart: Title + SeriesName + Units
            LineChart(data, d => d.Month, d => d.Revenue)
                .Title("Monthly Revenue 2024")
                .SeriesName("Revenue")
                .Units("months", "USD")
                .AxisLabel(ChartAxisType.X, "Month")
                .AxisLabel(ChartAxisType.Y, "Revenue (USD)")
                .Width(600).Height(250)
                .Stroke("#0078D4").StrokeWidth(2.5)
                .ShowGrid(true).ShowAxes(true),

            // Interactive accessible chart: adds keyboard nav and point invocation
            BarChart(data, d => d.Month, d => d.Revenue)
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
                Component<AccessibleChartDemo>()
            ).Padding(24)
        );
    }
}
