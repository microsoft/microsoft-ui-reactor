using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public class LineChartMissingData : GallerySample
{
    public override string Title => "Line Chart with Missing Data";
    public override string Description => "Demonstrates using SetDefined to skip NaN values, creating visible gaps in the line where sensor readings were unavailable.";
    public override string Category => "Lines";

    public override string SourceCode => @"
var line = LineGenerator.Create<(double x, double y)>(
        d => xs.Map(d.x), d => ys.Map(d.y))
    .SetDefined((d, _) => !double.IsNaN(d.y));
var pathData = line.Generate(data);
D3Path(pathData, stroke: Brush(Palette[0]), strokeWidth: 2)
    .AutomationName(""Sensor Temperature with Missing Data"")
    .FullDescription(""Line chart showing 30 days of sensor temperature readings with gaps where data was unavailable, ranging from 22.1°C to 30.8°C."")
";

    public override Element Render()
    {
        const double W = 700, H = 400;
        const double left = 50, top = 20, right = 20, bottom = 40;
        double width = W - left - right;
        double height = H - top - bottom;

        // Sensor readings over 30 days, with some missing (NaN)
        double[] readings =
        [
            22.1, 23.4, 24.0, 23.8, 25.1, double.NaN, double.NaN, double.NaN, 26.3, 27.0,
            27.8, 28.1, 27.5, 26.9, 28.4, 29.1, double.NaN, double.NaN, 28.0, 27.2,
            26.5, 25.8, 26.1, 27.3, 28.0, 28.9, 29.5, double.NaN, 30.1, 30.8
        ];

        var data = readings.Select((r, i) => (x: (double)(i + 1), y: r)).ToArray();

        // Compute extent excluding NaN
        var validValues = readings.Where(v => !double.IsNaN(v));
        var (yMin, yMax) = D3Extent.Extent(validValues);

        var xs = new LinearScale([1, 30], [left, left + width]);
        var ys = new LinearScale([yMax + 1, yMin - 1], [top, top + height]).Nice();

        // Dashed line connecting across gaps
        var connectingData = data.Where(d => !double.IsNaN(d.y)).ToArray();
        var connectingPathEl = D3LinePath(connectingData, x: d => xs.Map(d.x), y: d => ys.Map(d.y),
                stroke: Brush(Palette[0], opacity: 0.2), strokeWidth: 1.5)
            .StrokeDashArray(4, 3);

        // Solid line with gaps
        var solidLine = D3LinePath(data, x: d => xs.Map(d.x), y: d => ys.Map(d.y),
            stroke: Brush(Palette[0]), strokeWidth: 2,
            defined: (d, _) => !double.IsNaN(d.y));

        // Data points (only for valid values)
        var dotBrush = Brush(Palette[0]);
        var dots = data
            .Where(d => !double.IsNaN(d.y))
            .Select(d => (Element)(D3Circle(xs.Map(d.x), ys.Map(d.y), 3) with { Fill = dotBrush }))
            .ToArray();

        // Missing region bands
        var bandBrush = Brush(Palette[3], opacity: 0.08);

        return D3Canvas(W, H,
            [.. D3Grid(ys, left, width),
             .. D3Axes(xs, ys, left, top, width, height),
             connectingPathEl,
             solidLine,
             .. dots,
             .. readings.Select((r, i) => (r, i))
                 .Where(t => double.IsNaN(t.r))
                 .Select(t =>
                 {
                     double x0 = xs.Map(t.i + 0.5);
                     double x1 = xs.Map(t.i + 1.5);
                     return D3Rect(x0, top, x1 - x0, height) with { Fill = bandBrush };
                 }),
             D3Charts.Text(W / 2 - 20, H - 12, "Day", 11, ChartMutedForeground),
             D3Charts.Text(2, top - 14, "\u00b0C", 11, ChartMutedForeground),
             D3Charts.Text(left + 5, top + 5, "Shaded regions = missing sensor data", 10, Brush(Palette[3]))]
        )
            .AutomationName("Sensor Temperature with Missing Data")
            .FullDescription("Line chart showing 30 days of sensor temperature readings with gaps where data was unavailable, ranging from 22.1\u00b0C to 30.8\u00b0C.");
    }
}
