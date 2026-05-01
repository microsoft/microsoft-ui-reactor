using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public class HorizontalBarChartSample : GallerySample
{
    public override string Title => "Horizontal Bar Chart";
    public override string Description => "A horizontal bar chart comparing populations of the world's most populous countries (in millions).";
    public override string Category => "Bars";

    public override string SourceCode => """
        var xs = new LinearScale([0, maxVal], [0, plotW]).Nice();
        var band = BandScale.Create(countries)
            .SetRange(0, plotH).SetPaddingInner(0.25).SetPaddingOuter(0.1);

        var gridBrush = ChartGrid;
        foreach (var t in xs.Ticks(6))
            D3Line(left + xs.Map(t), top, left + xs.Map(t), top + plotH)
                with { Stroke = gridBrush };

        for (int i = 0; i < countries.Length; i++)
        {
            var fill = Brush(Palette[i % Palette.Count], opacity: 0.85);
            double y = top + band.Map(countries[i]);
            double barW = xs.Map(populations[i]);
            D3Rect(left, y, barW, band.Bandwidth)
                with { Fill = fill, RadiusX = 2, RadiusY = 2 };
        }
            .AutomationName("Population by Country (millions)")
            .FullDescription("Horizontal bar chart comparing populations of the ten most populous countries, ranging from 128 million to 1,428 million.")
        """;

    public override Element Render()
    {
        const double W = 700, H = 400;
        const double left = 100, top = 30, right = 30, bottom = 40;
        double plotW = W - left - right;
        double plotH = H - top - bottom;

        // Sample data — country populations in millions
        string[] countries = ["India", "China", "USA", "Indonesia", "Pakistan",
                              "Nigeria", "Brazil", "Bangladesh", "Russia", "Mexico"];
        double[] populations = [1428, 1425, 340, 277, 230, 223, 216, 173, 144, 128];

        double maxVal = populations.Max();

        // Scales
        var xs = new LinearScale([0, maxVal], [0, plotW]).Nice();
        var band = BandScale.Create(countries).SetRange(0, plotH).SetPaddingInner(0.25).SetPaddingOuter(0.1);

        // Horizontal grid lines (vertical in this case — along X)
        var gridBrush = ChartGrid;
        var gridLines = xs.Ticks(6).Select(t =>
            D3Line(left + xs.Map(t), top, left + xs.Map(t), top + plotH) with { Stroke = gridBrush, StrokeThickness = 1 });

        // Axes
        var axisBrush = ChartAxis;

        return D3Canvas(W, H,
            [.. gridLines,

             // Bars + value labels
             .. (from t in countries.Select((country, i) => (country, i))
                 let fill = Brush(Palette[t.i % Palette.Count], opacity: 0.85)
                 let y = top + band.Map(t.country)
                 let barW = xs.Map(populations[t.i])
                 from el in new Element[]
                 {
                     D3Rect(left, y, barW, band.Bandwidth) with { Fill = fill, RadiusX = 2, RadiusY = 2 },
                     D3Charts.Text(left + barW + 4, y + band.Bandwidth / 2 - 7, $"{populations[t.i]:F0}M", 10, ChartMutedForeground),
                 }
                 select el),

             D3Line(left, top + plotH, left + plotW, top + plotH) with { Stroke = axisBrush, StrokeThickness = 1 },
             D3Line(left, top, left, top + plotH) with { Stroke = axisBrush, StrokeThickness = 1 },
             .. xs.Ticks(6).Select(t =>
                 D3Charts.Text(left + xs.Map(t) - 12, top + plotH + 6, Fmt(t), 10, axisBrush)),

             // Y axis labels
             .. countries.Select((country, i) =>
                 TextRight(2, top + band.Map(country) + band.Bandwidth / 2 - 7, country, left - 6, 10, ChartMutedForeground)),

             D3Charts.Text(left, 4, "Population by Country (millions)", 13, ChartForeground),
            ]
        )
            .AutomationName("Population by Country (millions)")
            .FullDescription("Horizontal bar chart comparing populations of the ten most populous countries, ranging from 128 million to 1,428 million.");
    }
}
