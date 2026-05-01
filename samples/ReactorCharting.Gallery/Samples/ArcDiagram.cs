using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public sealed class ArcDiagramSample : GallerySample
{
    public override string Title => "Arc Diagram";
    public override string Description => "An arc diagram with 10 nodes arranged along a horizontal line. Connections are drawn as semicircular arcs above and below the baseline, with arc height proportional to the distance between nodes.";
    public override string Category => "Networks";

    public override string SourceCode => """
        D3Canvas(W, H,
            [D3Line(...) with { Stroke = ChartSubtleStroke },
             ..edges.Select((edge, ei) => {
                 var pb = new PathBuilder(3);
                 pb.MoveTo(x1, baseline);
                 pb.Arc(midX, baseline, r, Math.PI, 0, ccw: !above);
                 return D3Path(pb.ToString(), stroke: stroke, strokeWidth: 1.8);
             }),
             ..Enumerable.Range(0, nodeCount).SelectMany(i => new Element[] {
                 D3Circle(nodeX[i], baseline, 8) with { Fill = fill },
                 D3Charts.Text(nodeX[i] - 24, baseline + 14, labels[i], 9, ChartMutedForeground),
             }),
            ]
        )
            .AutomationName("Arc Diagram")
            .FullDescription("Arc diagram with 10 nodes on a horizontal baseline connected by 12 semicircular arcs.")
        """;

    public override Element Render()
    {
        const double W = 700, H = 500;
        const double padX = 50, baseline = 320;

        // -- data: 10 nodes --
        string[] labels =
        [
            "Alpha", "Bravo", "Charlie", "Delta", "Echo",
            "Foxtrot", "Golf", "Hotel", "India", "Juliet"
        ];
        int[] categories = [0, 0, 1, 1, 2, 2, 3, 3, 0, 1];
        int nodeCount = labels.Length;

        // ~12 edges (source, target)
        (int s, int t)[] edges =
        [
            (0, 2), (0, 5), (1, 3), (1, 4), (2, 6),
            (3, 7), (4, 8), (5, 9), (6, 8), (7, 9),
            (0, 9), (3, 6),
        ];

        // -- compute node x positions --
        double spacing = (W - padX * 2) / (nodeCount - 1);
        var nodeX = Enumerable.Range(0, nodeCount).Select(i => padX + i * spacing).ToArray();

        var white = ChartSurface;

        return D3Canvas(W, H,
            [D3Line(padX - 10, baseline, W - padX + 10, baseline) with { Stroke = ChartSubtleStroke, StrokeThickness = 1 },
             .. edges.Select((edge, ei) =>
             {
                 double x1 = nodeX[edge.s];
                 double x2 = nodeX[edge.t];
                 if (x1 > x2) (x1, x2) = (x2, x1);

                 double midX = (x1 + x2) / 2;
                 double r = (x2 - x1) / 2;
                 bool above = ei % 2 == 0;

                 var pb = new PathBuilder(3);
                 pb.MoveTo(x1, baseline);
                 pb.Arc(midX, baseline, r, Math.PI, 0, ccw: !above);

                 int colorIdx = categories[edge.s];
                 var stroke = Brush(Palette[colorIdx % Palette.Count], opacity: 0.6);
                 return D3Path(pb.ToString(), stroke: stroke, strokeWidth: 1.8);
             }),
             .. Enumerable.Range(0, nodeCount).SelectMany(i =>
             {
                 var fill = Brush(Palette[categories[i] % Palette.Count]);
                 return new Element[]
                 {
                     D3Circle(nodeX[i], baseline, 8) with { Fill = fill, Stroke = white, StrokeThickness = 1.5 },
                     TextCenter(nodeX[i] - 24, baseline + 14, labels[i], 48, 9, ChartMutedForeground),
                 };
             }),
             D3Charts.Text(12, 6, "Arc Diagram", 14, ChartForeground),
            ]
        )
            .AutomationName("Arc Diagram")
            .FullDescription("Arc diagram with 10 nodes on a horizontal baseline connected by 12 semicircular arcs.");
    }
}
