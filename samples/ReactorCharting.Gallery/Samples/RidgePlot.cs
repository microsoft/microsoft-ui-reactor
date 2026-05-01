using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public class RidgePlot : GallerySample
{
    public override string Title => "Ridgeline Plot";
    public override string Description =>
        "A ridgeline (joy) plot with 5 overlapping area charts stacked vertically, " +
        "each representing a different distribution. Rows overlap slightly to create " +
        "the characteristic layered appearance.";
    public override string Category => "Areas";

    public override string SourceCode => """
        double rowStep = plotH / rows;
        double overlap = rowStep * 0.45;

        ..Enumerable.Range(0, rows).Reverse().SelectMany(r => {
            double baselineY = marginTop + r * (rowStep - overlap) + rowStep + overlap;
            var ys = new LinearScale([0, peak], [baselineY, baselineY - rowStep * 1.2]);
            return new Element[] {
                D3AreaPath(pts, x: d => xs.Map(d.x), y0: _ => baselineY, y1: d => ys.Map(d.y),
                    fill: ChartSurfaceAlpha(217)),
                D3AreaPath(pts, x: d => xs.Map(d.x), y0: _ => baselineY, y1: d => ys.Map(d.y),
                    fill: Brush(Palette[r], opacity: 0.55)),
                D3LinePath(pts, x: d => xs.Map(d.x), y: d => ys.Map(d.y),
                    stroke: Brush(Palette[r]), strokeWidth: 1.5, curve: D3Curve.Natural),
            };
        })
            .AutomationName("Ridgeline Plot")
            .FullDescription("Ridgeline plot with 5 overlapping distribution curves (Groups A through E) stacked vertically.")
        """;

    public override Element Render()
    {
        const double W = 700, H = 400;
        const double marginLeft = 80, marginTop = 25, marginRight = 20, marginBottom = 20;
        double plotW = W - marginLeft - marginRight;
        double plotH = H - marginTop - marginBottom;

        const int rows = 5;
        const int points = 50;
        string[] labels = ["Group A", "Group B", "Group C", "Group D", "Group E"];

        // Generate distribution-like data for each row
        // Each has a different center and spread, like kernel density estimates
        double[] centers = [0.25, 0.40, 0.55, 0.35, 0.65];
        double[] spreads = [0.12, 0.15, 0.10, 0.18, 0.13];
        double[] heights = [1.0, 0.8, 1.2, 0.7, 0.9];
        double[] secondaryCenters = [0.60, 0.70, 0.80, 0.65, 0.30];
        double[] secondaryHeights = [0.4, 0.3, 0.5, 0.6, 0.35];

        var distributions = Enumerable.Range(0, rows).Select(r =>
            Enumerable.Range(0, points).Select(i =>
            {
                double t = i / (double)(points - 1);
                double v = heights[r] * Math.Exp(-Math.Pow((t - centers[r]) / spreads[r], 2) / 2);
                v += secondaryHeights[r] * Math.Exp(-Math.Pow((t - secondaryCenters[r]) / (spreads[r] * 0.8), 2) / 2);
                v += 0.05 * Math.Sin(t * Math.PI * 12 + r * 1.7);
                return Math.Max(v, 0);
            }).ToArray()
        ).ToArray();

        double globalPeak = distributions.SelectMany(d => d).Max();

        var xScale = new LinearScale([0, points - 1], [marginLeft, marginLeft + plotW]);

        double rowStep = plotH / rows;
        double overlap = rowStep * 0.45;

        // Draw rows from back (bottom) to front (top) so front overlaps back
        return D3Canvas(W, H,
        [
            .. Enumerable.Range(0, rows).Reverse().SelectMany(r =>
            {
                double baselineY = marginTop + r * (rowStep - overlap) + rowStep + overlap;

                var yScale = new LinearScale([0, globalPeak * 1.1],
                    [baselineY, baselineY - rowStep * 1.2]);

                var pts = Enumerable.Range(0, points)
                    .Select(i => (x: (double)i, y: distributions[r][i]))
                    .ToArray();

                return (Element[])
                [
                    D3AreaPath(pts, x: d => xScale.Map(d.x), y0: _ => baselineY, y1: d => yScale.Map(d.y),
                        fill: ChartSurfaceAlpha(217)),
                    D3AreaPath(pts, x: d => xScale.Map(d.x), y0: _ => baselineY, y1: d => yScale.Map(d.y),
                        fill: Brush(Palette[r], opacity: 0.55)),
                    D3LinePath(pts, x: d => xScale.Map(d.x), y: d => yScale.Map(d.y),
                        stroke: Brush(Palette[r]), strokeWidth: 1.5, curve: D3Curve.Natural),
                    D3Line(marginLeft, baselineY, marginLeft + plotW, baselineY) with { Stroke = ChartSubtleStroke, StrokeThickness = 0.5 },
                    D3Charts.Text(4, baselineY - rowStep * 0.5 - 6, labels[r], 11, ChartMutedForeground),
                ];
            }),
            D3Charts.Text(marginLeft, 4, "Ridgeline Plot", 14, ChartForeground),
        ])
            .AutomationName("Ridgeline Plot")
            .FullDescription("Ridgeline plot with 5 overlapping distribution curves (Groups A through E) stacked vertically.");
    }
}
