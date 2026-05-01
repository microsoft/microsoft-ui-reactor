using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public class StreamgraphChart : GallerySample
{
    public override string Title => "Streamgraph";
    public override string Description =>
        "A streamgraph (wiggle offset) with 4 layers of smooth data centered around " +
        "the horizontal midline. Uses StackGenerator then applies a manual centering " +
        "offset to produce the characteristic symmetric shape.";
    public override string Category => "Areas";

    public override string SourceCode => @"
StackSeries[] series = stack.Generate(rows);

// Centering offsets (wiggle-like) — no mutation
var offsets = Enumerable.Range(0, n)
    .Select(j => -series[^1].Points[j].Y1 / 2.0).ToArray();

series.Select((s, si) => {
    var pts = Enumerable.Range(0, n)
        .Select(j => (x: (double)j,
            y0: s.Points[j].Y0 + offsets[j],
            y1: s.Points[j].Y1 + offsets[j])).ToArray();
    return D3AreaPath(pts, x: d => xScale.Map(d.x),
        y0: d => yScale.Map(d.y0), y1: d => yScale.Map(d.y1),
        fill: Brush(Palette[si], opacity: 0.8));
})
    .AutomationName(""Streamgraph (Centered Stack)"")
    .FullDescription(""Streamgraph with 4 layers (Alpha, Beta, Gamma, Delta) of smooth data centered around the horizontal midline."")";

    public override Element Render()
    {
        const double W = 700, H = 400;
        const double marginLeft = 50, marginTop = 30, marginRight = 20, marginBottom = 30;
        double plotW = W - marginLeft - marginRight;
        double plotH = H - marginTop - marginBottom;

        const int n = 30; // data points per layer
        string[] keys = ["Alpha", "Beta", "Gamma", "Delta"];

        // Generate smooth bumpy data for each layer
        var rows = Enumerable.Range(0, n).Select(j =>
        {
            double t = j / (double)(n - 1);
            return new Dictionary<string, double>
            {
                ["Alpha"] = Math.Max(1, 12 + 18 * Math.Exp(-Math.Pow((t - 0.3) * 4, 2))
                                 + 6 * Math.Sin(t * Math.PI * 3)),
                ["Beta"]  = Math.Max(1, 10 + 14 * Math.Exp(-Math.Pow((t - 0.55) * 3.5, 2))
                                 + 4 * Math.Cos(t * Math.PI * 2.5)),
                ["Gamma"] = Math.Max(1, 8  + 20 * Math.Exp(-Math.Pow((t - 0.7) * 4, 2))
                                 + 5 * Math.Sin(t * Math.PI * 4 + 1)),
                ["Delta"] = Math.Max(1, 6  + 10 * Math.Exp(-Math.Pow((t - 0.45) * 3, 2))
                                 + 7 * Math.Cos(t * Math.PI * 1.8 + 2)),
            };
        }).ToList();

        // Stack
        var stack = StackGenerator.Create<Dictionary<string, double>>()
            .SetKeys(keys)
            .SetValue((d, key) => d[key]);
        StackSeries[] series = stack.Generate(rows);

        // Centering offsets (wiggle-like) — no mutation of series data
        var offsets = Enumerable.Range(0, n)
            .Select(j => -series[^1].Points[j].Y1 / 2.0)
            .ToArray();

        double yMin = series.SelectMany(s => s.Points.Select((p, j) => p.Y0 + offsets[j])).Min();
        double yMax = series.SelectMany(s => s.Points.Select((p, j) => p.Y1 + offsets[j])).Max();

        var xScale = new LinearScale([0, n - 1], [marginLeft, marginLeft + plotW]);
        var yScale = new LinearScale([yMin * 1.1, yMax * 1.1], [marginTop + plotH, marginTop]);

        return D3Canvas(W, H,
            [D3Line(marginLeft, yScale.Map(0), marginLeft + plotW, yScale.Map(0))
                with { Stroke = ChartSubtleStroke, StrokeThickness = 1 },
             .. series.Select((s, si) =>
                {
                    var pts = Enumerable.Range(0, n)
                        .Select(j => (x: (double)j, y0: s.Points[j].Y0 + offsets[j], y1: s.Points[j].Y1 + offsets[j]))
                        .ToArray();
                    return (Element)D3AreaPath(pts, x: d => xScale.Map(d.x), y0: d => yScale.Map(d.y0), y1: d => yScale.Map(d.y1),
                        fill: Brush(Palette[si], opacity: 0.8));
                }),
             .. D3Legend(marginLeft + 10, marginTop + 4, keys.Select((key, k) => (key, Brush(Palette[k], opacity: 0.8)))),
             D3Charts.Text(marginLeft + 100, 6, "Streamgraph (Centered Stack)", 14, ChartForeground),
            ]
        )
            .AutomationName("Streamgraph (Centered Stack)")
            .FullDescription("Streamgraph with 4 layers (Alpha, Beta, Gamma, Delta) of smooth data centered around the horizontal midline.");
    }
}
