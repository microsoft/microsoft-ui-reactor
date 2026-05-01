using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public sealed class HistogramSample : GallerySample
{
    public override string Title => "Histogram";
    public override string Description => "A histogram of 200 normally-distributed random values binned with BinGenerator and drawn as bars.";
    public override string Category => "Analysis";

    public override string SourceCode => """
        var binner = BinGenerator.Create().SetThresholdCount(20);
        var bins = binner.Generate(values);
        var xs = new LinearScale([bins[0].X0, bins[^1].X1], [left, left+pw]);
        var ys = new LinearScale([0, maxCount], [top+ph, top]).Nice();

        var bars = bins.Select(bin =>
            D3Rect(xs.Map(bin.X0)+1, ys.Map(bin.Count),
                xs.Map(bin.X1)-xs.Map(bin.X0)-2,
                ys.Map(0)-ys.Map(bin.Count)) with { Fill = fill });

        return D3Canvas(width, height,
            [..D3Grid(ys, left, pw), ..bars,
             ..D3Axes(xs, ys, left, top, pw, ph)])
            .AutomationName("Histogram (normal distribution, 200 values)")
            .FullDescription("Histogram of 200 normally-distributed values binned into 20 bins, centered around 50.");
        """;

    public override Element Render()
    {
        const double width = 700, height = 400;
        const double left = 50, top = 30, right = 20, bottom = 30;
        double pw = width - left - right;
        double ph = height - top - bottom;

        // Generate 200 normally-distributed values using Box-Muller
        var rng = new Random(99);
        var values = Enumerable.Range(0, 200).Select(_ =>
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = rng.NextDouble();
            return 50 + 15 * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }).ToArray();

        var binner = BinGenerator.Create().SetThresholdCount(20);
        var bins = binner.Generate(values);

        if (bins.Length == 0)
            return D3Canvas(width, height, []);

        int maxCount = bins.Max(b => b.Count);

        var xs = new LinearScale([bins[0].X0, bins[^1].X1], [left, left + pw]);
        var ys = new LinearScale([0, maxCount], [top + ph, top]).Nice();

        var fill = Brush(Palette[0], opacity: 0.7);

        return D3Canvas(width, height,
            [.. D3Grid(ys, left, pw),
             .. bins.Select(bin =>
             {
                 double bx = xs.Map(bin.X0) + 1;
                 double bw = xs.Map(bin.X1) - xs.Map(bin.X0) - 2;
                 double by = ys.Map(bin.Count);
                 double bh = ys.Map(0) - by;
                 return D3Rect(bx, by, bw, bh) with { Fill = fill };
             }),
             .. D3Axes(xs, ys, left, top, pw, ph),
             D3Charts.Text(left, 6, "Histogram (normal distribution, 200 values)", 14, ChartForeground)]
        )
            .AutomationName("Histogram (normal distribution, 200 values)")
            .FullDescription("Histogram of 200 normally-distributed values binned into 20 bins, centered around 50.");
    }
}
