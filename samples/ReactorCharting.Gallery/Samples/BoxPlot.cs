using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public sealed class BoxPlotSample : GallerySample
{
    public override string Title => "Box Plot";
    public override string Description => "A box plot for four groups showing min, Q1, median, Q3, and max, computed with D3Statistics.QuantileSorted.";
    public override string Category => "Analysis";

    public override string SourceCode => """
        var ys = new LinearScale([yMin, yMax], [top + ph, top]).Nice();
        D3Canvas(width, height,
            [..D3Grid(ys, left, pw),
             ..groupData.SelectMany((sorted, g) => new Element[] {
                 D3Line(cx, ys.Map(min), cx, ys.Map(q1)) with { Stroke = stroke },
                 D3Rect(bx, boxY, boxWidth, boxH) with { Fill = fill, RadiusX = 2 },
                 D3Line(bx, ys.Map(median), bx+boxWidth, ys.Map(median)) with { Stroke = ... },
             }),
            ]
        )
            .AutomationName("Box Plot (four groups)")
            .FullDescription("Box plot for 4 groups (A, B, C, D) showing min, Q1, median, Q3, and max for each distribution.")
        """;

    public override Element Render()
    {
        const double width = 700, height = 400;
        const double left = 50, top = 30, right = 20, bottom = 40;
        double pw = width - left - right;
        double ph = height - top - bottom;

        var groups = new[] { "A", "B", "C", "D" };
        var rng = new Random(55);

        // Generate data per group with different distributions
        double[][] groupData = new double[groups.Length][];
        for (int g = 0; g < groups.Length; g++)
        {
            double center = 30 + g * 15;
            double spread = 8 + g * 3;
            groupData[g] = Enumerable.Range(0, 40).Select(_ =>
            {
                double u1 = 1.0 - rng.NextDouble();
                double u2 = rng.NextDouble();
                return center + spread * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
            }).OrderBy(v => v).ToArray();
        }

        // Find overall extent for y scale
        double yMin = groupData.Min(d => d[0]);
        double yMax = groupData.Max(d => d[^1]);
        var ys = new LinearScale([yMin, yMax], [top + ph, top]).Nice();

        double groupWidth = pw / groups.Length;
        double boxWidth = groupWidth * 0.5;

        return D3Canvas(width, height,
            [.. D3Grid(ys, left, pw),
             // Y axis
             D3Line(left, top, left, top + ph) with { Stroke = ChartAxis, StrokeThickness = 1 },
             .. ys.Ticks(6).Select(t =>
                 TextRight(0, ys.Map(t) - 7, Fmt(t), left - 6, 10, ChartMutedForeground)),
             // Box plots per group
             .. (from entry in groupData.Select((sorted, g) => (sorted, g))
                 let min = entry.sorted[0]
                 let max = entry.sorted[^1]
                 let q1 = D3Statistics.QuantileSorted(entry.sorted, 0.25)
                 let median = D3Statistics.QuantileSorted(entry.sorted, 0.5)
                 let q3 = D3Statistics.QuantileSorted(entry.sorted, 0.75)
                 let cx = left + entry.g * groupWidth + groupWidth / 2
                 let bx = cx - boxWidth / 2
                 let color = Palette[entry.g % Palette.Count]
                 let fill = Brush(color, opacity: 0.35)
                 let stroke = Brush(color)
                 let boxY = ys.Map(q3)
                 let boxH = ys.Map(q1) - ys.Map(q3)
                 from el in new Element[]
                 {
                     // Whiskers
                     D3Line(cx, ys.Map(min), cx, ys.Map(q1)) with { Stroke = stroke, StrokeThickness = 1.5 },
                     D3Line(cx, ys.Map(q3), cx, ys.Map(max)) with { Stroke = stroke, StrokeThickness = 1.5 },
                     // Caps
                     D3Line(cx - boxWidth * 0.3, ys.Map(min), cx + boxWidth * 0.3, ys.Map(min)) with { Stroke = stroke, StrokeThickness = 1.5 },
                     D3Line(cx - boxWidth * 0.3, ys.Map(max), cx + boxWidth * 0.3, ys.Map(max)) with { Stroke = stroke, StrokeThickness = 1.5 },
                     // Box: Q1 to Q3
                     D3Rect(bx, boxY, boxWidth, boxH) with { Fill = fill, RadiusX = 2, RadiusY = 2 },
                     // Box outline
                     D3Line(bx, boxY, bx + boxWidth, boxY) with { Stroke = stroke, StrokeThickness = 1.5 },
                     D3Line(bx, boxY + boxH, bx + boxWidth, boxY + boxH) with { Stroke = stroke, StrokeThickness = 1.5 },
                     D3Line(bx, boxY, bx, boxY + boxH) with { Stroke = stroke, StrokeThickness = 1.5 },
                     D3Line(bx + boxWidth, boxY, bx + boxWidth, boxY + boxH) with { Stroke = stroke, StrokeThickness = 1.5 },
                     // Median line
                     D3Line(bx, ys.Map(median), bx + boxWidth, ys.Map(median)) with { Stroke = stroke, StrokeThickness = 2.5 },
                     // Group label
                     D3Charts.Text(cx - 8, top + ph + 8, $"Group {groups[entry.g]}", 11, ChartMutedForeground),
                 }
                 select el),
             D3Charts.Text(left, 6, "Box Plot (four groups)", 14, ChartForeground)]
        )
            .AutomationName("Box Plot (four groups)")
            .FullDescription("Box plot for 4 groups (A, B, C, D) showing min, Q1, median, Q3, and max for each distribution.");
    }
}
