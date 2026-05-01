using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public sealed class PieChartSample : GallerySample
{
    public override string Title => "Pie Chart";
    public override string Description => "A classic pie chart showing market share across five companies, using PieGenerator and ArcGenerator.";
    public override string Category => "Radial";

    public override string SourceCode => """
        var arcs = PieGenerator.Generate(data, value: d => d.Value, sort: false);
        D3Canvas(width, height,
            ..arcs.SelectMany((a, i) => new[] {
                D3ArcPath(a.StartAngle, a.EndAngle, cx, cy, outerRadius: 150,
                    fill: Brush(Palette[i % Palette.Count])),
                D3Charts.Text(cx + lx, cy + ly, label, 11, brush),
            })
        )
            .AutomationName("Market Share")
            .FullDescription("Pie chart showing market share: Chrome 65%, Safari 18%, Firefox 7%, Edge 5%, Other 5%.")
        """;

    public override Element Render()
    {
        const double width = 700, height = 400;
        double cx = width / 2, cy = height / 2;

        var data = new (string Name, double Value)[]
        {
            ("Chrome", 65.0), ("Safari", 18.0), ("Firefox", 7.0),
            ("Edge", 5.0), ("Other", 5.0)
        };

        var arcs = PieGenerator.Generate(data, value: d => d.Value, sort: false);

        return D3Canvas(width, height,
            [.. arcs.SelectMany((a, i) =>
                {
                    var (ox, oy) = ArcGenerator.Centroid(a.StartAngle, a.EndAngle, innerRadius: 180, outerRadius: 180);
                    return new Element[]
                    {
                        D3ArcPath(a.StartAngle, a.EndAngle, cx, cy, outerRadius: 150,
                            fill: Brush(Palette[i % Palette.Count]),
                            stroke: ChartSurface, strokeWidth: 1),
                        D3Charts.Text(cx + ox - 20, cy + oy - 7,
                            $"{a.Data.Name} ({a.Data.Value}%)", 11, Brush(Palette[i % Palette.Count])),
                    };
                }),
             D3Charts.Text(cx - 60, 10, "Market Share", 16, ChartForeground),
            ]
        )
            .AutomationName("Market Share")
            .FullDescription("Pie chart showing market share: Chrome 65%, Safari 18%, Firefox 7%, Edge 5%, Other 5%.");
    }
}
