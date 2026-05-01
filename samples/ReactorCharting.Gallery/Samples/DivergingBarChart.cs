using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public class DivergingBarChartSample : GallerySample
{
    public override string Title => "Diverging Bar Chart";
    public override string Description => "A diverging bar chart showing customer sentiment scores ranging from negative to positive, with bars extending from a central baseline.";
    public override string Category => "Bars";

    public override string SourceCode => """
        var xs = new LinearScale([-absMax, absMax], [0, plotW]).Nice();
        var band = BandScale.Create(items)
            .SetRange(0, plotH).SetPaddingInner(0.2).SetPaddingOuter(0.1);

        double zeroX = left + xs.Map(0);
        D3Line(zeroX, top, zeroX, top + plotH)
            with { Stroke = ChartMutedForeground, StrokeThickness = 1.5 };

        var posBrush = Brush(Palette[0]);
        var negBrush = Brush(Palette[3]);

        for (int i = 0; i < items.Length; i++)
        {
            double y = top + band.Map(items[i]);
            double v = scores[i];
            double barStart = v >= 0 ? zeroX : left + xs.Map(v);
            double barWidth = v >= 0
                ? xs.Map(v) - xs.Map(0)
                : xs.Map(0) - xs.Map(v);
            var fill = v >= 0 ? posBrush : negBrush;
            D3Rect(barStart, y, barWidth, band.Bandwidth)
                with { Fill = fill, RadiusX = 2, RadiusY = 2 };
        }
            .AutomationName("Customer Sentiment Scores")
            .FullDescription("Diverging bar chart showing customer sentiment scores for ten categories, ranging from -35 to +72, with bars extending from a central baseline.")
        """;

    public override Element Render()
    {
        const double W = 700, H = 400;
        const double left = 110, top = 30, right = 30, bottom = 40;
        double plotW = W - left - right;
        double plotH = H - top - bottom;

        // Sample data — sentiment scores (-100 to +100)
        string[] items = ["Product Quality", "Customer Service", "Pricing",
                          "Delivery Speed", "Return Policy", "Website UX",
                          "Mobile App", "Loyalty Program", "Packaging",
                          "Overall Value"];
        double[] scores = [72, 45, -18, -35, 28, 55, -12, 60, 38, -8];

        double absMax = Math.Max(Math.Abs(scores.Min()), Math.Abs(scores.Max()));

        // Scales
        var xs = new LinearScale([-absMax, absMax], [0, plotW]).Nice();
        var band = BandScale.Create(items).SetRange(0, plotH).SetPaddingInner(0.2).SetPaddingOuter(0.1);

        // Vertical grid lines
        var gridBrush = ChartGrid;
        var gridLines = xs.Ticks(8).Select(t =>
            D3Line(left + xs.Map(t), top, left + xs.Map(t), top + plotH) with { Stroke = gridBrush, StrokeThickness = 1 });

        // Zero line
        double zeroX = left + xs.Map(0);

        // Bars + value labels
        var posBrush = Brush(Palette[0]);
        var negBrush = Brush(Palette[3]);

        // Axes
        var axisBrush = ChartAxis;

        return D3Canvas(W, H,
            [.. gridLines,
             D3Line(zeroX, top, zeroX, top + plotH) with { Stroke = ChartMutedForeground, StrokeThickness = 1.5 },

             // Bars + value labels
             .. (from t in items.Select((item, i) => (item, i))
                 let y = top + band.Map(t.item)
                 let v = scores[t.i]
                 let barStart = v >= 0 ? zeroX : left + xs.Map(v)
                 let barWidth = v >= 0 ? xs.Map(v) - xs.Map(0) : xs.Map(0) - xs.Map(v)
                 let fill = v >= 0 ? posBrush : negBrush
                 let labelX = v >= 0 ? barStart + barWidth + 4 : barStart - 30
                 from el in new Element[]
                 {
                     D3Rect(barStart, y, barWidth, band.Bandwidth) with { Fill = fill, RadiusX = 2, RadiusY = 2 },
                     D3Charts.Text(labelX, y + band.Bandwidth / 2 - 7, (v >= 0 ? "+" : "") + v.ToString("F0"), 10, ChartMutedForeground),
                 }
                 select el),

             D3Line(left, top + plotH, left + plotW, top + plotH) with { Stroke = axisBrush, StrokeThickness = 1 },
             .. xs.Ticks(8).Select(t =>
                 D3Charts.Text(left + xs.Map(t) - 12, top + plotH + 6, Fmt(t), 10, axisBrush)),

             // Y axis labels
             .. items.Select((item, i) =>
                 TextRight(2, top + band.Map(item) + band.Bandwidth / 2 - 7, item, left - 6, 10, ChartMutedForeground)),

             // Legend
             .. D3Legend(left + plotW - 120, top + 2, [("Positive", posBrush), ("Negative", negBrush)]),

             D3Charts.Text(left, 4, "Customer Sentiment Scores", 13, ChartForeground),
            ]
        )
            .AutomationName("Customer Sentiment Scores")
            .FullDescription("Diverging bar chart showing customer sentiment scores for ten categories, ranging from -35 to +72, with bars extending from a central baseline.");
    }
}
