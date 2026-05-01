using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Charting.Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public class MultiLineChart : GallerySample
{
    public override string Title => "Multi-Line Chart";
    public override string Description => "Three overlapping temperature series (New York, London, Tokyo) drawn with different colors from the D3 categorical palette.\n\n\U0001f4a1 Accessibility tip: Press T to toggle between chart and data table view. Screen readers announce the switch automatically.";
    public override string Category => "Lines";

    public override string SourceCode => @"
// Build chart as usual
var chart = D3Canvas(W, H, [..grid, ..axes, ..lines, ..legend])
    .AutomationName(""Monthly Temperatures (°C)"")
    .FullDescription(""Multi-line chart comparing temperatures for 3 cities."");

// Build data table as alternate view
var table = BuildDataTable(months, labels, allSeries);

// Wrap: press T or Alt+Shift+F11 to toggle between chart ↔ table
return Charts.WithAlternateView(chart, table);
";

    // Monthly average temperatures (12 months)
    private static readonly string[] Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private static readonly double[] NewYork = [0.5, 1.8, 6.5, 12.2, 17.8, 22.9, 25.6, 25.0, 20.8, 14.7, 8.9, 3.2];
    private static readonly double[] London  = [5.2, 5.5, 7.6, 9.9, 13.3, 16.4, 18.7, 18.4, 15.6, 12.0, 8.0, 5.5];
    private static readonly double[] Tokyo   = [5.8, 6.4, 9.4, 14.6, 19.0, 22.4, 25.9, 27.1, 23.5, 18.2, 13.0, 7.9];

    public override Element Render()
    {
        const double W = 700, H = 400;
        const double left = 50, top = 20, right = 120, bottom = 40;
        double width = W - left - right;
        double height = H - top - bottom;

        string[] labels = ["New York", "London", "Tokyo"];
        var colors = new[] { Palette[0], Palette[1], Palette[2] };
        var allSeries = new[] { NewYork, London, Tokyo };

        // Find global extent
        var allValues = NewYork.Concat(London).Concat(Tokyo);
        var (yMin, yMax) = D3Extent.Extent(allValues);

        var xs = new LinearScale([0, 11], [left, left + width]);
        var ys = new LinearScale([yMax + 3, yMin - 3], [top, top + height]).Nice();

        var monthLabels = Months.Select((m, i) =>
            D3Charts.Text(xs.Map(i) - 10, top + height + 4, m, 10, ChartMutedForeground));

        var lines = allSeries.Select((series, s) =>
        {
            var data = series.Select((v, i) => (x: (double)i, y: v)).ToArray();
            return (Element)D3LinePath(data, x: d => xs.Map(d.x), y: d => ys.Map(d.y),
                stroke: Brush(colors[s]), strokeWidth: 2);
        });

        double legendX = W - right + 10;

        var chart = D3Canvas(W, H,
            [.. D3Grid(ys, left, width),
             .. D3Axes(xs, ys, left, top, width, height),
             .. monthLabels,
             .. lines,
             .. D3Legend(legendX, top + 10, labels.Select((label, s) => (label, Brush(colors[s])))),
             D3Charts.Text(2, top - 14, "\u00b0C", 11, ChartMutedForeground)]
        )
            .AutomationName("Monthly Temperatures (\u00b0C)")
            .FullDescription("Multi-line chart comparing monthly average temperatures for New York, London, and Tokyo across 12 months.");

        // Build data table as alternate view
        var table = BuildDataTable(Months, labels, allSeries);

        // Wrap chart + table with toggle (T key or Alt+Shift+F11)
        return WithAlternateView(chart, table);
    }

    /// <summary>
    /// Builds an accessible data table showing the chart's raw values.
    /// Uses a simple Grid layout with header row + data rows.
    /// </summary>
    private static Element BuildDataTable(string[] months, string[] seriesNames, double[][] allSeries)
    {
        var headerBrush = Theme.SecondaryText;
        var textBrush = Theme.PrimaryText;

        // Column definitions: Month + one per series
        var colDefs = Enumerable.Range(0, seriesNames.Length + 1)
            .Select(_ => "*")
            .ToArray();

        // Build header row
        var headerCells = new List<Element>
        {
            TextBlock("Month")
                .FontWeight(Microsoft.UI.Text.FontWeights.SemiBold)
                .Foreground(headerBrush)
                .Padding(8, 6, 8, 6)
                .Grid(row: 0, column: 0),
        };
        for (int s = 0; s < seriesNames.Length; s++)
        {
            headerCells.Add(
                TextBlock(seriesNames[s])
                    .FontWeight(Microsoft.UI.Text.FontWeights.SemiBold)
                    .Foreground(headerBrush)
                    .Padding(8, 6, 8, 6)
                    .Grid(row: 0, column: s + 1));
        }

        // Build data rows
        var rows = new List<Element>();
        for (int r = 0; r < months.Length; r++)
        {
            var rowCells = new List<Element>
            {
                TextBlock(months[r])
                    .Foreground(textBrush)
                    .Padding(8, 4, 8, 4)
                    .Grid(row: r + 1, column: 0),
            };

            for (int s = 0; s < allSeries.Length; s++)
            {
                rowCells.Add(
                    TextBlock(allSeries[s][r].ToString("F1") + "\u00b0C")
                        .Foreground(textBrush)
                        .Padding(8, 4, 8, 4)
                        .Grid(row: r + 1, column: s + 1));
            }

            rows.AddRange(rowCells);
        }

        // Row definitions: header + 12 data rows
        var rowDefs = Enumerable.Range(0, months.Length + 1)
            .Select(_ => "Auto")
            .ToArray();

        var allCells = headerCells.Concat(rows).ToArray();

        return Grid(colDefs, rowDefs, allCells)
            .AutomationName("Temperature data table")
            .FullDescription("Data table showing monthly average temperatures in °C for New York, London, and Tokyo.")
            .IsTabStop(true)
            .MaxWidth(700);
    }
}
