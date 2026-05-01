using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public sealed class SunburstSample : GallerySample
{
    public override string Title => "Sunburst";
    public override string Description =>
        "A sunburst chart showing disk usage as nested angular slices. Uses PartitionLayout " +
        "with ToPolar and ArcGenerator to render concentric rings of a hierarchy.";
    public override string Category => "Hierarchies";

    public override string SourceCode => """
        var partition = PartitionLayout.Create<DiskNode>()
            .Size(totalAngleWidth, totalHeightNorm);
        var root = partition.Layout(data, n => n.Children, n => n.Size);

        D3Canvas(W, H,
            [..allNodes.Where(n => n.Parent != null)
                .Select(node => {
                    var (sa, ea, ir, or) = node.ToPolar(...);
                    return D3ArcPath(sa, ea, cx, cy,
                        innerRadius: ir, outerRadius: or,
                        fill: fill, stroke: ChartSurface, strokeWidth: 1);
                }),
             ..labels]
        )
            .AutomationName("Disk Usage")
            .FullDescription("Sunburst chart of disk usage showing nested angular slices across Users, Program Files, and Windows directories with sizes in megabytes.")
        """;

    record DiskNode(string Name, double Size = 0, DiskNode[]? Children = null);

    public override Element Render()
    {
        const double W = 500, H = 500;
        double cx = W / 2, cy = H / 2;
        double maxRadius = Math.Min(W, H) / 2 - 10;

        // Disk usage hierarchy (sizes in MB)
        var data = new DiskNode("C:\\", 0, [
            new("Users", 0, [
                new("Documents", 0, [
                    new("Reports", 450),
                    new("Photos", 1200),
                    new("Projects", 800),
                ]),
                new("Downloads", 0, [
                    new("Installers", 600),
                    new("Media", 900),
                ]),
                new("AppData", 0, [
                    new("Cache", 350),
                    new("Logs", 120),
                    new("Config", 80),
                ]),
            ]),
            new("Program Files", 0, [
                new("VS Code", 400),
                new("Office", 1500),
                new("Browser", 300),
                new("Games", 2200),
            ]),
            new("Windows", 0, [
                new("System32", 1800),
                new("WinSxS", 1200),
                new("Temp", 400),
            ]),
        ]);

        // Use PartitionLayout in polar coordinates
        double totalAngleWidth = 1;
        double totalHeightNorm = 1;

        var partition = PartitionLayout.Create<DiskNode>().Size(totalAngleWidth, totalHeightNorm);
        var root = partition.Layout(data, n => n.Children, n => n.Size);

        var allNodes = root.Descendants().ToList();

        return D3Canvas(W, H,
            [.. allNodes
                .Where(node => node.Parent != null)
                .SelectMany(node =>
                {
                    var (startAngle, endAngle, innerRadius, outerRadius) =
                        node.ToPolar(totalAngleWidth, totalHeightNorm, maxRadius);
                    if (endAngle - startAngle < 0.005) return [];

                    int colorIdx = root.Children.IndexOf(node.TopAncestor);
                    var fill = Brush(Palette[colorIdx % Palette.Count], opacity: Math.Max(0.3, 0.9 - node.Depth * 0.15));

                    bool showLabel = (endAngle - startAngle) > 0.15 && node.Children.Count == 0;
                    double midAngle = (startAngle + endAngle) / 2 - Math.PI / 2;
                    double midR = (innerRadius + outerRadius) / 2;
                    double lx = cx + Math.Cos(midAngle) * midR;
                    double ly = cy + Math.Sin(midAngle) * midR;

                    return (Element[])
                    [
                        D3ArcPath(startAngle, endAngle, cx, cy,
                            innerRadius: innerRadius, outerRadius: outerRadius,
                            fill: fill, stroke: ChartSurface, strokeWidth: 1),
                        .. (showLabel ? [TextCenter(lx - 20, ly - 6, node.Data.Name, 40, 8, Gray(30))] : (Element[])[]),
                    ];
                }),
             TextCenter(cx - 20, cy - 7, "Disk", 40, 12, ChartForeground),
            ]
        )
            .AutomationName("Disk Usage")
            .FullDescription("Sunburst chart of disk usage showing nested angular slices across Users, Program Files, and Windows directories with sizes in megabytes.");
    }

}
