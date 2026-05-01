using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public sealed class TidyTreeSample : GallerySample
{
    public override string Title => "Tidy Tree";
    public override string Description =>
        "A tidy tree layout (Reingold-Tilford) of a file system hierarchy three levels deep. " +
        "Uses TreeLayout to compute node positions, then draws curved bezier links, circular nodes, and text labels.";
    public override string Category => "Hierarchies";

    public override string SourceCode => """
        var layout = TreeLayout.Create<FsNode>().Size(660, 440);
        var root = layout.Hierarchy(data, n => n.Children);
        layout.Layout(root);

        D3Canvas(W, H,
            [..nodes.SelectMany(n => n.Children.Select(child => {
                double my = (n.Y + child.Y) / 2;
                var pb = new PathBuilder(3);
                pb.MoveTo(n.X, n.Y);
                pb.BezierCurveTo(n.X, my, child.X, my, child.X, child.Y);
                return D3Path(pb.ToString(), linkStroke, null, 1.5);
            })),
             ..nodes.Select(n => D3Circle(n.X, n.Y, isLeaf ? 4 : 5)
                with { Fill = fill, Stroke = stroke }),
             ..labels]
        )
            .AutomationName("Tidy Tree")
            .FullDescription("Tidy tree layout of a file system hierarchy showing a project with 3 subdirectories across 4 levels and 21 nodes.")
        """;

    record FsNode(string Name, FsNode[]? Children = null);

    public override Element Render()
    {
        const double W = 700, H = 500;

        // File system hierarchy data
        var data = new FsNode("project", [
            new("src", [
                new("components", [
                    new("App.tsx"),
                    new("Header.tsx"),
                    new("Sidebar.tsx"),
                    new("Footer.tsx"),
                ]),
                new("utils", [
                    new("format.ts"),
                    new("api.ts"),
                    new("hooks.ts"),
                ]),
                new("styles", [
                    new("main.css"),
                    new("theme.css"),
                ]),
            ]),
            new("public", [
                new("index.html"),
                new("favicon.ico"),
            ]),
            new("config", [
                new("tsconfig.json"),
                new("vite.config.ts"),
                new("package.json"),
            ]),
        ]);

        var layout = TreeLayout.Create<FsNode>().Size(660, 440);
        var root = layout.Hierarchy(data, n => n.Children);
        layout.Layout(root);

        var nodes = root.Descendants().ToList();

        var linkStroke = ChartSubtleStroke;

        return D3Canvas(W, H,
            [.. nodes.SelectMany(node =>
                node.Children.Select(child =>
                    D3Link(node.X, node.Y, child.X, child.Y, linkStroke, 1.5))),
             .. nodes.SelectMany(node =>
             {
                 bool isLeaf = node.Children.Count == 0;
                 double r = isLeaf ? 4 : 5;
                 var fill = isLeaf ? Brush(Palette[0]) : ChartSubtleFill;
                 Microsoft.UI.Xaml.Media.Brush? stroke = isLeaf ? null : ChartMutedForeground;

                 Element label = isLeaf
                     ? D3Charts.Text(node.X + 8, node.Y - 7, node.Data.Name, 9, ChartMutedForeground)
                     : TextCenter(node.X - 4, node.Y - 18, node.Data.Name, 40, 10, ChartForeground);

                 return new Element[]
                 {
                     D3Circle(node.X, node.Y, r) with { Fill = fill, Stroke = stroke, StrokeThickness = 1.5 },
                     label,
                 };
             }),
            ]
        )
            .AutomationName("Tidy Tree")
            .FullDescription("Tidy tree layout of a file system hierarchy showing a project with 3 subdirectories across 4 levels and 21 nodes.");
    }

}
