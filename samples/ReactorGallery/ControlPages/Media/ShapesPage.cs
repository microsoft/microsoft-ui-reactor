using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Media;

class ShapesPage : Component
{
    static Element Tile(string label, Element shape) =>
        VStack(6,
            Border(shape).Size(120, 90).Background(Theme.SubtleFill).CornerRadius(6).Center(),
            Caption(label).Foreground(Theme.SecondaryText).Center());

    public override Element Render()
    {
        var accent = new SolidColorBrush(Colors.SteelBlue);
        var fill = new SolidColorBrush(Colors.MediumPurple);

        // Simple triangle geometry for the Path sample.
        var triangle = new PathGeometry();
        var figure = new PathFigure { StartPoint = new Point(10, 70), IsClosed = true };
        figure.Segments.Add(new LineSegment { Point = new Point(50, 10) });
        figure.Segments.Add(new LineSegment { Point = new Point(90, 70) });
        triangle.Figures.Add(figure);

        return ScrollView(VStack(16,
            PageHeader("Shapes", "Rectangle, Ellipse, Line, and Path draw vector primitives you can fill and stroke."),

            SampleCard("Filled shapes",
                HStack(16,
                    Tile("Rectangle", Rectangle().Width(80).Height(56).Fill(accent)),
                    Tile("Ellipse", Ellipse().Width(72).Height(56).Fill(fill)),
                    Tile("Rounded", Rectangle().Width(80).Height(56).Fill(new SolidColorBrush(Colors.SeaGreen))
                        .Set(r => { r.RadiusX = 14; r.RadiusY = 14; }))),
                sourceCode: @"
Rectangle().Width(80).Height(56).Fill(accentBrush)
Ellipse().Width(72).Height(56).Fill(purpleBrush)
Rectangle().Width(80).Height(56).Fill(greenBrush)
    .Set(r => { r.RadiusX = 14; r.RadiusY = 14; })   // shapes round via RadiusX/Y, not .CornerRadius
"),

            SampleCard("Line and Path",
                HStack(16,
                    Tile("Line", Line(10, 10, 100, 70).Stroke(accent).StrokeThickness(4)),
                    Tile("Path", Path2D().Fill(new SolidColorBrush(Colors.OrangeRed)).Set(p => p.Data = triangle))),
                sourceCode: @"
Line(10, 10, 100, 70).Stroke(brush).StrokeThickness(4)

// Path2D draws an arbitrary Geometry:
var triangle = new PathGeometry();
var figure = new PathFigure { StartPoint = new Point(10, 70), IsClosed = true };
figure.Segments.Add(new LineSegment { Point = new Point(50, 10) });
figure.Segments.Add(new LineSegment { Point = new Point(90, 70) });
triangle.Figures.Add(figure);
Path2D().Fill(brush).Set(p => p.Data = triangle)
")
        ).Margin(36, 24, 36, 36));
    }
}
