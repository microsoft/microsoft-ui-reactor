using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public sealed class ChordDiagramSample : GallerySample
{
    public override string Title => "Chord Diagram";
    public override string Description => "A chord diagram showing trade flow between five world regions. Outer arcs represent each region's total flow; inner ribbons show pairwise connections.";
    public override string Category => "Networks";

    public override string SourceCode => """
        var chord = new ChordLayout().SetPadAngle(0.05);
        var data = chord.Generate(matrix);
        var ribbon = new RibbonGenerator().SetRadius(innerR);

        var arcElements = data.Groups.SelectMany(g => {
            var pb = new PathBuilder(3);
            pb.Arc(cx, cy, outerR, a0, a1);
            pb.Arc(cx, cy, innerR, a1, a0, ccw: true);
            pb.ClosePath();
            return new[] {
                D3Path(pb.ToString(), fill: fill),
                TextCenter(lx - 35, ly - 7, label, 70, 10, brush)
            };
        });
        var ribbons = data.Chords.Select(c =>
            D3PathTranslated(ribbon.Generate(c), cx, cy, fill: Brush(color, opacity: 0.55)));
        D3Canvas(W, H, [..arcElements, ..ribbons, title])
            .AutomationName("Chord Diagram — Regional Trade Flow")
            .FullDescription("Chord diagram showing trade flow between 5 world regions with ribbons indicating pairwise connections.");
        """;

    public override Element Render()
    {
        const double W = 700, H = 500;
        double cx = W / 2, cy = H / 2;
        double outerR = 200, innerR = 190;

        // -- regions and flow matrix (5x5) --
        string[] regions = ["N. America", "Europe", "Asia", "S. America", "Africa"];
        double[][] matrix =
        [
            [0,   120, 200, 80,  40],
            [90,  0,   160, 50,  30],
            [180, 140, 0,   60,  70],
            [60,  40,  50,  0,   20],
            [30,  25,  55,  15,  0 ],
        ];

        // -- compute layout --
        var chord = new ChordLayout().SetPadAngle(0.05);
        var data = chord.Generate(matrix);
        var ribbon = new RibbonGenerator().SetRadius(innerR);

        return D3Canvas(W, H,
        [
            .. data.Groups.SelectMany(g =>
            {
                var color = Palette[g.Index % Palette.Count];
                var fill = Brush(color);

                var pb = new PathBuilder(3);
                double a0 = g.StartAngle - Math.PI / 2;
                double a1 = g.EndAngle - Math.PI / 2;
                pb.Arc(cx, cy, outerR, a0, a1);
                pb.Arc(cx, cy, innerR, a1, a0, ccw: true);
                pb.ClosePath();

                double mid = (g.StartAngle + g.EndAngle) / 2 - Math.PI / 2;
                double lx = cx + (outerR + 16) * Math.Cos(mid);
                double ly = cy + (outerR + 16) * Math.Sin(mid);
                double labelW = 70;

                return new Element[]
                {
                    D3Path(pb.ToString(), fill: fill),
                    TextCenter(lx - labelW / 2, ly - 7, regions[g.Index], labelW, 10, Brush(color)),
                };
            }),
            .. data.Chords
                .Select(c => D3PathTranslated(ribbon.Generate(c), cx, cy,
                    fill: Brush(Palette[c.Source.Index % Palette.Count], opacity: 0.55))),
            D3Charts.Text(12, 6, "Chord Diagram — Regional Trade Flow", 14, ChartForeground),
        ])
            .AutomationName("Chord Diagram — Regional Trade Flow")
            .FullDescription("Chord diagram showing trade flow between 5 world regions with ribbons indicating pairwise connections.");
    }
}
