using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public sealed class DonutChartSample : GallerySample
{
    public override string Title => "Donut Chart";
    public override string Description => "A donut chart (pie with inner radius) showing six expense categories, using PieGenerator and ArcGenerator with innerRadius=80.";
    public override string Category => "Radial";

    public override string SourceCode => """
        var arcs = PieGenerator.Generate(data, value: d => d.Value, sort: false, padAngle: 0.02);
        var slices = arcs.SelectMany((a, i) => new[] {
            D3ArcPath(a.StartAngle, a.EndAngle, cx, cy,
                outerRadius: 150, innerRadius: 80, padAngle: 0.02,
                fill: Brush(Palette[i % Palette.Count])),
            D3Charts.Text(cx + lx, cy + ly, a.Data.Name, 10, brush),
        });
        D3Canvas(width, height, [..slices, ..centerText, title])
            .AutomationName("Monthly Expenses")
            .FullDescription("Donut chart showing monthly expenses: Housing $1,800, Food $650, Transport $400, Health $300, Utilities $250, Savings $600.");
        """;

    public override Element Render()
    {
        const double width = 700, height = 400;
        double cx = width / 2, cy = height / 2;

        var data = new (string Name, double Value)[]
        {
            ("Housing", 1800.0), ("Food", 650.0), ("Transport", 400.0),
            ("Utilities", 250.0), ("Health", 300.0), ("Savings", 600.0)
        };

        var arcs = PieGenerator.Generate(data, value: d => d.Value, sort: false, padAngle: 0.02);
        double total = data.Sum(d => d.Value);

        return D3Canvas(width, height,
        [
            .. arcs.SelectMany((a, i) =>
                {
                    var (lx, ly) = ArcGenerator.Centroid(a.StartAngle, a.EndAngle, innerRadius: 185, outerRadius: 185);
                    return new Element[]
                    {
                        D3ArcPath(a.StartAngle, a.EndAngle, cx, cy,
                            outerRadius: 150, innerRadius: 80, padAngle: 0.02,
                            fill: Brush(Palette[i % Palette.Count])),
                        D3Charts.Text(cx + lx - 24, cy + ly - 7,
                            $"{a.Data.Name}", 10, Brush(Palette[i % Palette.Count])),
                    };
                }),
            D3Charts.Text(cx - 24, cy - 12, $"${total:N0}", 14, ChartForeground),
            D3Charts.Text(cx - 16, cy + 6, "/ month", 10, ChartMutedForeground),
            D3Charts.Text(cx - 80, 10, "Monthly Expenses", 16, ChartForeground),
        ])
            .AutomationName("Monthly Expenses")
            .FullDescription("Donut chart showing monthly expenses: Housing $1,800, Food $650, Transport $400, Health $300, Utilities $250, Savings $600.");
    }
}
