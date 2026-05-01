using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public sealed class BubbleChartSample : GallerySample
{
    public override string Title => "Bubble Chart";
    public override string Description => "A bubble chart where circle size encodes a third variable, showing 30 data points with x, y, and size dimensions.";
    public override string Category => "Dots";

    public override string SourceCode => """
        var xs = new LinearScale([0, 100], [left, left+pw]).Nice();
        var ys = new LinearScale([0, 100], [top+ph, top]).Nice();
        var rs = new LinearScale([5, 45], [3, 20]);
        var bubbles = points.Select((p, i) =>
            D3Circle(xs.Map(p.x), ys.Map(p.y), rs.Map(p.size)) with {
                Fill = Brush(Palette[i % Palette.Count], opacity: 0.45),
                Stroke = Brush(Palette[i % Palette.Count])
            });
        D3Canvas(width, height,
            ..D3Grid(ys, left, pw), ..D3Axes(xs, ys, left, top, pw, ph),
            ..bubbles)
            .AutomationName("Bubble Chart (size encodes third variable)")
            .FullDescription("Bubble chart showing 30 data points where circle size encodes a third variable, with x and y axes ranging from 0 to 100.")
        """;

    public override Element Render()
    {
        const double width = 700, height = 400;
        const double left = 50, top = 30, right = 20, bottom = 30;
        double pw = width - left - right;
        double ph = height - top - bottom;

        var rng = new Random(123);
        var points = Enumerable.Range(0, 30)
            .Select(_ => (x: rng.NextDouble() * 100, y: rng.NextDouble() * 100,
                          size: 5 + rng.NextDouble() * 40))
            .ToArray();

        var xs = new LinearScale([0, 100], [left, left + pw]).Nice();
        var ys = new LinearScale([0, 100], [top + ph, top]).Nice();
        var rs = new LinearScale([5, 45], [3, 20]);

        var bubbles = points.Select((p, i) =>
            D3Circle(xs.Map(p.x), ys.Map(p.y), rs.Map(p.size)) with
            {
                Fill = Brush(Palette[i % Palette.Count], opacity: 0.45),
                Stroke = Brush(Palette[i % Palette.Count]),
            });

        return D3Canvas(width, height,
            [.. D3Grid(ys, left, pw),
             .. D3Axes(xs, ys, left, top, pw, ph),
             .. bubbles,
             D3Charts.Text(left, 6, "Bubble Chart (size encodes third variable)", 14, ChartForeground)]
        )
            .AutomationName("Bubble Chart (size encodes third variable)")
            .FullDescription("Bubble chart showing 30 data points where circle size encodes a third variable, with x and y axes ranging from 0 to 100.");
    }
}
