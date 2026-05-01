using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public sealed class CirclePackingSample : GallerySample
{
    public override string Title => "Circle Packing";
    public override string Description =>
        "A circle packing visualization of an organization hierarchy. Nested circles " +
        "show containment, with leaf circle sizes proportional to team headcount. Uses PackLayout.";
    public override string Category => "Hierarchies";

    public override string SourceCode => """
        var pack = PackLayout.Create<OrgNode>().Size(230).SetPadding(4);
        var root = pack.Layout(data, n => n.Children, n => n.Value);

        var elements = allNodes.SelectMany(node => {
            bool isLeaf = node.Children.Count == 0;
            if (isLeaf)
            {
                var fill = Brush(Palette[colorIdx % Palette.Count], opacity: 0.6);
                return new[] { D3Circle(nx, ny, r) with { Fill = fill, Stroke = stroke } };
            }
            else
                return new[] { D3Circle(nx, ny, r) with { Fill = Gray(200, alpha: alpha), Stroke = ChartMutedForeground } };
        });
        D3Canvas(W, H, [..elements, title])
            .AutomationName("Organization Headcount")
            .FullDescription("Circle packing of an organization hierarchy with nested circles showing team containment and leaf sizes proportional to headcount across 14 teams.")
        """;

    record OrgNode(string Name, double Value = 0, OrgNode[]? Children = null);

    public override Element Render()
    {
        const double W = 700, H = 500;
        double cx = W / 2, cy = H / 2;

        // Organization hierarchy with headcount values
        var data = new OrgNode("Company", 0, [
            new("Engineering", 0, [
                new("Frontend", 0, [
                    new("React Team", 12),
                    new("Design System", 6),
                    new("Mobile", 8),
                ]),
                new("Backend", 0, [
                    new("APIs", 10),
                    new("Data Platform", 14),
                    new("Infrastructure", 9),
                ]),
                new("QA", 0, [
                    new("Automation", 5),
                    new("Manual", 4),
                ]),
            ]),
            new("Product", 0, [
                new("PM Team", 8),
                new("UX Research", 5),
                new("Analytics", 6),
            ]),
            new("Operations", 0, [
                new("HR", 4),
                new("Finance", 5),
                new("Legal", 3),
                new("Facilities", 3),
            ]),
        ]);

        var pack = PackLayout.Create<OrgNode>().Size(230).SetPadding(4);
        var root = pack.Layout(data, n => n.Children, n => n.Value);

        var allNodes = root.Descendants().OrderBy(n => n.Depth).ToList();

        return D3Canvas(W, H,
        [
            .. allNodes.SelectMany(node =>
            {
                double nx = cx + node.X;
                double ny = cy + node.Y;
                double r = node.R;
                bool isLeaf = node.Children.Count == 0;

                if (isLeaf)
                {
                    int colorIdx = root.Children.IndexOf(node.TopAncestor);
                    var fill = Brush(Palette[colorIdx % Palette.Count], opacity: 0.6);
                    var stroke = Brush(Palette[colorIdx % Palette.Count], opacity: 0.9);
                    var circle = D3Circle(nx, ny, r) with { Fill = fill, Stroke = stroke, StrokeThickness = 1 };

                    if (r > 18)
                    {
                        string label = node.Data.Name;
                        double maxChars = r * 2 / 6;
                        if (label.Length > maxChars) label = label[..(int)maxChars] + "..";
                        return new Element[]
                        {
                            circle,
                            TextCenter(nx - r + 4, ny - 6, label, r * 2 - 8, 8, Gray(30)),
                        };
                    }

                    return [circle];
                }
                else
                {
                    byte alpha = node.Depth == 0 ? (byte)40 : (byte)25;
                    var fill = Gray(200, alpha: alpha);
                    var stroke = ChartMutedForeground;
                    return new Element[]
                    {
                        D3Circle(nx, ny, r) with { Fill = fill, Stroke = stroke, StrokeThickness = node.Depth == 0 ? 2 : 1 },
                    };
                }
            }),
            D3Charts.Text(10, 5, "Organization Headcount", 13, ChartForeground),
        ])
            .AutomationName("Organization Headcount")
            .FullDescription("Circle packing of an organization hierarchy with nested circles showing team containment and leaf sizes proportional to headcount across 14 teams.");
    }

}
