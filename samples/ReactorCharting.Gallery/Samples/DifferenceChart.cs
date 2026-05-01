using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public class DifferenceChart : GallerySample
{
    public override string Title => "Difference Chart";
    public override string Description =>
        "A surplus/deficit difference chart showing two series (Revenue vs Expenses). " +
        "Green fill appears where Revenue exceeds Expenses, red where Expenses exceed Revenue. " +
        "Approximated with two clipped area regions.";
    public override string Category => "Areas";

    public override string SourceCode => @"
var greenPts = data.Select(d => (x: d.X, y0: Math.Min(d.A, d.B), y1: d.A)).ToArray();
var redPts   = data.Select(d => (x: d.X, y0: Math.Min(d.A, d.B), y1: d.B)).ToArray();

D3Canvas(W, H,
    [..grid, ..axes,
     D3AreaPath(greenPts, x: d => xs.Map(d.x), y0: d => ys.Map(d.y0), y1: d => ys.Map(d.y1), fill: greenFill),
     D3AreaPath(redPts,   x: d => xs.Map(d.x), y0: d => ys.Map(d.y0), y1: d => ys.Map(d.y1), fill: redFill),
     D3LinePath(data, x: d => xs.Map(d.X), y: d => ys.Map(d.A), stroke: greenBrush, curve: MonotoneX),
     D3LinePath(data, x: d => xs.Map(d.X), y: d => ys.Map(d.B), stroke: redBrush, curve: MonotoneX),
     ..legend, title])
    .AutomationName(""Difference Chart (Revenue vs Expenses)"")
    .FullDescription(""Surplus/deficit difference chart comparing Revenue and Expenses over 24 months, with green fill where Revenue exceeds Expenses and red where Expenses exceed Revenue."")";

    private record struct Point(double X, double A, double B);

    public override Element Render()
    {
        const double W = 700, H = 400;
        const double marginLeft = 55, marginTop = 25, marginRight = 20, marginBottom = 40;
        double plotW = W - marginLeft - marginRight;
        double plotH = H - marginTop - marginBottom;

        // Generate two crossing series over 24 data points (months)
        const int n = 24;
        var data = Enumerable.Range(0, n).Select(i =>
        {
            double t = i / (double)(n - 1);
            double revenue = 60 + 30 * Math.Sin(t * Math.PI * 2.2 + 0.3)
                               + 10 * Math.Cos(t * Math.PI * 4);
            double expenses = 55 + 20 * Math.Sin(t * Math.PI * 1.8 + 1.5)
                                + 15 * Math.Cos(t * Math.PI * 3 + 0.8);
            return new Point(i, revenue, expenses);
        }).ToArray();

        var (yMinA, yMaxA) = D3Extent.Extent(data, d => d.A);
        var (yMinB, yMaxB) = D3Extent.Extent(data, d => d.B);
        double yLo = Math.Min(yMinA, yMinB);
        double yHi = Math.Max(yMaxA, yMaxB);

        var xScale = new LinearScale([0, n - 1], [marginLeft, marginLeft + plotW]);
        var yScale = new LinearScale([yLo * 0.85, yHi * 1.1], [marginTop + plotH, marginTop]);
        yScale.Nice();

        // Grid + axes
        var grid = D3Grid(yScale, marginLeft, plotW);
        var axes = D3Axes(xScale, yScale, marginLeft, marginTop, plotW, plotH);

        // Green fill: where A > B, show area from B up to A
        var greenPts = data.Select((d, i) => (x: (double)i, y0: Math.Min(d.A, d.B), y1: d.A)).ToArray();
        var redPts = data.Select((d, i) => (x: (double)i, y0: Math.Min(d.A, d.B), y1: d.B)).ToArray();

        var greenBrush = Brush("#2ca02c");
        var redBrush = Brush("#d62728");

        // Legend
        double lx = marginLeft + plotW - 130;

        return D3Canvas(W, H,
        [
            .. grid,
            .. axes,
            D3AreaPath(greenPts, x: d => xScale.Map(d.x), y0: d => yScale.Map(d.y0), y1: d => yScale.Map(d.y1),
                fill: Brush("#2ca02c", opacity: 0.35)),
            D3AreaPath(redPts, x: d => xScale.Map(d.x), y0: d => yScale.Map(d.y0), y1: d => yScale.Map(d.y1),
                fill: Brush("#d62728", opacity: 0.35)),
            D3LinePath(data, x: d => xScale.Map(d.X), y: d => yScale.Map(d.A),
                stroke: greenBrush, strokeWidth: 2, curve: D3Curve.MonotoneX),
            D3LinePath(data, x: d => xScale.Map(d.X), y: d => yScale.Map(d.B),
                stroke: redBrush, strokeWidth: 2, curve: D3Curve.MonotoneX),
            .. D3Legend(lx, marginTop + 6, [("Revenue", greenBrush), ("Expenses", redBrush)]),
            Microsoft.UI.Reactor.Charting.D3Charts.Text(marginLeft, 4, "Difference Chart (Revenue vs Expenses)", 14, ChartForeground),
        ])
            .AutomationName("Difference Chart (Revenue vs Expenses)")
            .FullDescription("Surplus/deficit difference chart comparing Revenue and Expenses over 24 months, with green fill where Revenue exceeds Expenses and red where Expenses exceed Revenue.");
    }
}
