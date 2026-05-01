using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public sealed class TreemapSample : GallerySample
{
    public override string Title => "Treemap";
    public override string Description =>
        "A treemap showing file sizes in a software project. Each leaf rectangle is " +
        "proportional to the file size. Uses TreemapLayout with squarify tiling.";
    public override string Category => "Hierarchies";

    public override string SourceCode => """
        var treemap = TreemapLayout.Create<FileNode>()
            .Size(W, H).SetPadding(2).SetPaddingInner(2);
        var root = treemap.Hierarchy(data, n => n.Children, n => n.Size);
        treemap.Layout(root);

        D3Canvas(W, H,
            [..root.Leaves().Select(leaf => {
                var fill = Brush(Palette[colorIdx], opacity: 0.75);
                return D3Rect(leaf.X0, leaf.Y0, w, h)
                    with { Fill = fill, RadiusX = 2, RadiusY = 2 };
            }),
             ..labels,
             ..root.Children.Select(f =>
                D3Rect(f.X0, f.Y0, f.Width, f.Height)
                    with { Stroke = ChartMutedForeground, StrokeThickness = 1.5,
                           RadiusX = 2, RadiusY = 2 })]
        )
            .AutomationName("Treemap")
            .FullDescription("Treemap of file sizes in a software project with rectangles proportional to file size in kilobytes across src, tests, and config folders.")
        """;

    record FileNode(string Name, double Size = 0, FileNode[]? Children = null);

    public override Element Render()
    {
        const double W = 700, H = 500;

        // Project file hierarchy with sizes in KB
        var data = new FileNode("project", 0, [
            new("src", 0, [
                new("App.tsx", 12),
                new("index.tsx", 3),
                new("Router.tsx", 8),
                new("components", 0, [
                    new("Dashboard.tsx", 28),
                    new("Header.tsx", 15),
                    new("Sidebar.tsx", 22),
                    new("DataGrid.tsx", 45),
                    new("Chart.tsx", 38),
                    new("Modal.tsx", 18),
                ]),
                new("services", 0, [
                    new("api.ts", 32),
                    new("auth.ts", 24),
                    new("storage.ts", 16),
                ]),
                new("utils", 0, [
                    new("format.ts", 10),
                    new("validate.ts", 14),
                    new("helpers.ts", 8),
                ]),
            ]),
            new("tests", 0, [
                new("App.test.tsx", 18),
                new("Dashboard.test.tsx", 22),
                new("DataGrid.test.tsx", 30),
                new("api.test.ts", 20),
                new("auth.test.ts", 15),
            ]),
            new("config", 0, [
                new("webpack.config.js", 12),
                new("tsconfig.json", 4),
                new("jest.config.js", 6),
                new("package.json", 3),
            ]),
        ]);

        var treemap = TreemapLayout.Create<FileNode>()
            .Size(W, H)
            .SetPadding(2)
            .SetPaddingInner(2);
        var root = treemap.Hierarchy(data, n => n.Children, n => n.Size);
        treemap.Layout(root);

        // Assign colors by top-level folder

        return D3Canvas(W, H,
            [.. root.Leaves()
                .Where(leaf => leaf.Width >= 1 && leaf.Height >= 1)
                .SelectMany(leaf =>
                {
                    double w = leaf.Width;
                    double h = leaf.Height;
                    int colorIdx = root.Children.IndexOf(leaf.TopAncestor);
                    var fill = Brush(Palette[colorIdx % Palette.Count], opacity: 0.75);

                    string label = leaf.Data.Name;
                    if (label.Length > (int)(w / 6)) label = label[..(int)(w / 6)] + "..";

                    return (Element[])
                    [
                        D3Rect(leaf.X0, leaf.Y0, w, h) with { Fill = fill, RadiusX = 2, RadiusY = 2 },
                        .. (w > 40 && h > 16 ? [D3Charts.Text(leaf.X0 + 4, leaf.Y0 + 3, label, 9, Gray(30))] : (Element[])[]),
                        .. (w > 40 && h > 30 ? [D3Charts.Text(leaf.X0 + 4, leaf.Y0 + 15, $"{leaf.Data.Size} KB", 8, ChartMutedForeground)] : (Element[])[]),
                    ];
                }),
             .. root.Children
                .Where(folder => folder.Width >= 1 && folder.Height >= 1)
                .Select(folder =>
                    D3Rect(folder.X0, folder.Y0, folder.Width, folder.Height)
                        with { Stroke = ChartMutedForeground, StrokeThickness = 1.5,
                               RadiusX = 2, RadiusY = 2 }),
            ]
        )
            .AutomationName("Treemap")
            .FullDescription("Treemap of file sizes in a software project with rectangles proportional to file size in kilobytes across src, tests, and config folders.");
    }

}
