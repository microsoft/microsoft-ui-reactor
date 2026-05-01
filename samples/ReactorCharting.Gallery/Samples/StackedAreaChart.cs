using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public class StackedAreaChart : GallerySample
{
    public override string Title => "Stacked Area Chart";
    public override string Description =>
        "A stacked area chart showing 3 data series across 12 months. " +
        "Uses StackGenerator to compute cumulative baselines, then AreaGenerator " +
        "to render each layer with a distinct palette color.";
    public override string Category => "Areas";

    public override string SourceCode => """
        var stack = StackGenerator.Create<Dictionary<string, double>>()
            .SetKeys(keys)
            .SetValue((d, key) => d[key]);
        StackSeries[] series = stack.Generate(months);

        ..series.Select((s, si) => {
            var pts = s.Points.Select((p, j) => (x: (double)j, y0: p.Y0, y1: p.Y1)).ToArray();
            return D3AreaPath(pts, x: d => xs.Map(d.x), y0: d => ys.Map(d.y0), y1: d => ys.Map(d.y1),
                fill: Brush(Palette[si], opacity: 0.75));
        })
            .AutomationName("Stacked Area Chart")
            .FullDescription("Stacked area chart showing 3 data series (Product, Service, Support) across 12 months.")
        """;

    public override Element Render()
    {
        const double W = 700, H = 400;
        const double marginLeft = 50, marginTop = 20, marginRight = 20, marginBottom = 40;
        double plotW = W - marginLeft - marginRight;
        double plotH = H - marginTop - marginBottom;

        string[] keys = ["Product", "Service", "Support"];
        string[] monthLabels = ["Jan", "Feb", "Mar", "Apr", "May", "Jun",
                                "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

        // Sample data: 3 series over 12 months
        var months = new List<Dictionary<string, double>>();
        double[] bases = [40, 25, 15];
        double[] growth = [2.5, 1.8, 1.0];
        for (int m = 0; m < 12; m++)
        {
            var row = new Dictionary<string, double>();
            for (int k = 0; k < keys.Length; k++)
            {
                double val = bases[k] + growth[k] * m
                    + 8 * Math.Sin((m + k * 2) * 0.7)
                    + 3 * Math.Cos((m + k) * 1.1);
                row[keys[k]] = Math.Max(val, 2);
            }
            months.Add(row);
        }

        // Stack
        var stack = StackGenerator.Create<Dictionary<string, double>>()
            .SetKeys(keys)
            .SetValue((d, key) => d[key]);
        StackSeries[] series = stack.Generate(months);

        double maxY = series.SelectMany(s => s.Points).Max(p => p.Y1);

        var xScale = new LinearScale([0, 11], [marginLeft, marginLeft + plotW]);
        var yScale = new LinearScale([0, maxY * 1.1], [marginTop + plotH, marginTop]);
        yScale.Nice();

        return D3Canvas(W, H,
            [.. D3Grid(yScale, marginLeft, plotW),
             .. series.Select((s, si) =>
                {
                    var pts = s.Points.Select((p, j) => (x: (double)j, y0: p.Y0, y1: p.Y1)).ToArray();
                    return (Element)D3AreaPath(pts, x: d => xScale.Map(d.x), y0: d => yScale.Map(d.y0), y1: d => yScale.Map(d.y1),
                        fill: Brush(Palette[si], opacity: 0.75));
                }),
             .. D3Axes(xScale, yScale, marginLeft, marginTop, plotW, plotH),
             .. Enumerable.Range(0, 6).Select(i =>
                D3Charts.Text(xScale.Map(i * 2) - 10, marginTop + plotH + 6,
                    monthLabels[i * 2], 9, ChartMutedForeground)),
             .. D3Legend(marginLeft + plotW - 100, marginTop + 8, keys.Select((key, k) => (key, Brush(Palette[k], opacity: 0.75)))),
             D3Charts.Text(marginLeft, 2, "Stacked Area Chart", 14, ChartForeground),
            ]
        )
            .AutomationName("Stacked Area Chart")
            .FullDescription("Stacked area chart showing 3 data series (Product, Service, Support) across 12 months.");
    }
}
