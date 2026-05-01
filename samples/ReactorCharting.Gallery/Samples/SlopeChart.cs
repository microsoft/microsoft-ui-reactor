using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public class SlopeChart : GallerySample
{
    public override string Title => "Slope Chart";
    public override string Description => "A slope chart comparing 'Before' and 'After' values for six departments, with connecting lines colored by whether values improved or declined.";
    public override string Category => "Lines";

    public override string SourceCode => """
        var ys = new LinearScale([yMax + 5, yMin - 5], [top, top + height]).Nice();
        items.SelectMany(item => {
            var brush = Brush(item.After >= item.Before ? Palette[2] : Palette[3]);
            return new Element[] {
                D3Line(xLeft, ys.Map(item.Before), xRight, ys.Map(item.After))
                    with { Stroke = brush, StrokeThickness = 2 },
                D3Circle(xLeft, ys.Map(item.Before), 5) with { Fill = brush },
                D3Circle(xRight, ys.Map(item.After), 5) with { Fill = brush },
            };
        })
            .AutomationName("Department Performance: Before vs After")
            .FullDescription("Slope chart comparing before and after performance scores for six departments, with lines colored green for improvement and red for decline.")
        """;

    public override Element Render()
    {
        const double canvasW = 700, canvasH = 400;
        const double left = 130, top = 40, right = 130, bottom = 30;
        double width = canvasW - left - right;
        double height = canvasH - top - bottom;

        var items = new (string Label, double Before, double After)[]
        {
            ("Engineering", 72, 85),
            ("Marketing", 65, 61),
            ("Sales", 58, 78),
            ("Support", 80, 82),
            ("Finance", 70, 68),
            ("HR", 55, 74),
        };

        var allValues = items.Select(i => i.Before).Concat(items.Select(i => i.After));
        var (yMin, yMax) = D3Extent.Extent(allValues);

        var ys = new LinearScale([yMax + 5, yMin - 5], [top, top + height]);
        ys.Nice();

        double xLeft = left;
        double xRight = left + width;
        var gridBrush = ChartGrid;
        var axisBrush = ChartAxis;

        return D3Canvas(canvasW, canvasH,
            [// Column headers
             TextCenter(xLeft - 30, top - 30, "Before", 60, 13, ChartMutedForeground),
             TextCenter(xRight - 30, top - 30, "After", 60, 13, ChartMutedForeground),

             // Grid lines + tick labels
             .. ys.Ticks(6).SelectMany(t => new Element[]
             {
                 D3Line(xLeft, ys.Map(t), xRight, ys.Map(t)) with { Stroke = gridBrush, StrokeThickness = 1 },
                 D3Charts.Text(xLeft - 28, ys.Map(t) - 7, Fmt(t), 9, ChartMutedForeground),
                 D3Charts.Text(xRight + 8, ys.Map(t) - 7, Fmt(t), 9, ChartMutedForeground),
             }),

             // Vertical axis lines
             D3Line(xLeft, top, xLeft, top + height) with { Stroke = axisBrush, StrokeThickness = 1 },
             D3Line(xRight, top, xRight, top + height) with { Stroke = axisBrush, StrokeThickness = 1 },

             // Slopes: line + dots + labels per item
             .. items.SelectMany(item =>
             {
                 double y1 = ys.Map(item.Before), y2 = ys.Map(item.After);
                 var brush = Brush(item.After >= item.Before ? Palette[2] : Palette[3]);
                 return new Element[]
                 {
                     D3Line(xLeft, y1, xRight, y2) with { Stroke = brush, StrokeThickness = 2 },
                     D3Circle(xLeft, y1, 5) with { Fill = brush },
                     D3Circle(xRight, y2, 5) with { Fill = brush },
                     D3Charts.Text(4, y1 - 7, $"{item.Label} ({item.Before:F0})", 10, brush),
                     D3Charts.Text(xRight + 30, y2 - 7, $"{item.Label} ({item.After:F0})", 10, brush),
                 };
             }),

             // Legend
             .. D3Legend(canvasW / 2 - 80, canvasH - 22, [("Improved", Brush(Palette[2])), ("Declined", Brush(Palette[3]))]),
            ]
        )
            .AutomationName("Department Performance: Before vs After")
            .FullDescription("Slope chart comparing before and after performance scores for six departments, with lines colored green for improvement and red for decline.");
    }
}
