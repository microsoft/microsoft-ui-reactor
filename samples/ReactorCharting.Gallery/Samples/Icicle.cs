using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public sealed class IcicleSample : GallerySample
{
    public override string Title => "Icicle Chart";
    public override string Description =>
        "An icicle (partition) chart showing budget allocation as horizontal rectangles " +
        "stacked by depth. Each row represents a level in the hierarchy. Uses PartitionLayout.";
    public override string Category => "Hierarchies";

    public override string SourceCode => """
        var partition = PartitionLayout.Create<BudgetNode>()
            .Size(W, H).SetPadding(1);
        var root = partition.Layout(data, n => n.Children, n => n.Amount);
        D3Canvas(W, H,
            [..allNodes.Where(n => n.Width >= 1 && n.Height >= 1)
                .SelectMany(node => new Element[] {
                    D3Rect(node.X0, node.Y0, w, h)
                        with { Fill = fill, RadiusX = 1, RadiusY = 1 },
                    D3Rect(node.X0, node.Y0, w, h)
                        with { Stroke = ChartSurfaceAlpha(180), StrokeThickness = 0.5 },
                }),
            ]
        )
            .AutomationName("Icicle Chart")
            .FullDescription("Icicle partition chart of company budget allocation with horizontal rectangles stacked by hierarchy depth across Engineering, Sales, Operations, and R&D.")
        """;

    record BudgetNode(string Name, double Amount = 0, BudgetNode[]? Children = null);

    public override Element Render()
    {
        const double W = 700, H = 500;

        // Company budget hierarchy (thousands $)
        var data = new BudgetNode("Total Budget", 0, [
            new("Engineering", 0, [
                new("Salaries", 0, [
                    new("Senior Eng", 450),
                    new("Mid Eng", 320),
                    new("Junior Eng", 180),
                ]),
                new("Cloud Infra", 0, [
                    new("AWS", 200),
                    new("Azure", 80),
                    new("GCP", 40),
                ]),
                new("Tools & Licenses", 0, [
                    new("IDE Licenses", 30),
                    new("CI/CD", 25),
                    new("Monitoring", 20),
                ]),
            ]),
            new("Sales & Marketing", 0, [
                new("Advertising", 0, [
                    new("Digital Ads", 180),
                    new("Print", 40),
                    new("Events", 90),
                ]),
                new("Sales Team", 0, [
                    new("Salaries", 280),
                    new("Commissions", 150),
                ]),
            ]),
            new("Operations", 0, [
                new("Rent", 120),
                new("Utilities", 35),
                new("Insurance", 50),
                new("Office Supplies", 15),
            ]),
            new("R&D", 0, [
                new("Research", 160),
                new("Prototyping", 80),
                new("Patents", 40),
            ]),
        ]);

        var partition = PartitionLayout.Create<BudgetNode>()
            .Size(W, H)
            .SetPadding(1);
        var root = partition.Layout(data, n => n.Children, n => n.Amount);

        var allNodes = root.Descendants().ToList();

        return D3Canvas(W, H,
        [
            .. allNodes
                .Where(node => node.Width >= 1 && node.Height >= 1)
                .SelectMany(node =>
                {
                    double w = node.Width, h = node.Height;
                    int colorIdx = Math.Max(0, root.Children.IndexOf(node.TopAncestor));
                    double opacity = Math.Min(node.Depth == 0 ? 0.35 : 0.5 + node.Depth * 0.1, 0.85);
                    var fill = Brush(Palette[colorIdx % Palette.Count], opacity: opacity);

                    string label = node.Data.Name;
                    double maxChars = w / 7;
                    if (label.Length > maxChars) label = label[..(int)maxChars] + "..";

                    string valLabel = node.Value >= 1000
                        ? $"${node.Value / 1000:0.#}M"
                        : $"${node.Value:0}k";

                    return (Element[])
                    [
                        D3Rect(node.X0, node.Y0, w, h) with { Fill = fill, RadiusX = 1, RadiusY = 1 },
                        D3Rect(node.X0, node.Y0, w, h) with { Stroke = ChartSurfaceAlpha(180), StrokeThickness = 0.5 },
                        .. (w > 50 && h > 16 ? [D3Charts.Text(node.X0 + 4, node.Y0 + 3, label, 9, Gray(20))] : (Element[])[]),
                        .. (w > 50 && h > 30 ? [D3Charts.Text(node.X0 + 4, node.Y0 + 16, valLabel, 8, ChartMutedForeground)] : (Element[])[]),
                    ];
                }),
        ])
            .AutomationName("Icicle Chart")
            .FullDescription("Icicle partition chart of company budget allocation with horizontal rectangles stacked by hierarchy depth across Engineering, Sales, Operations, and R&D.");
    }

}
