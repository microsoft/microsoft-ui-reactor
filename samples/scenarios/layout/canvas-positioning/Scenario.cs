// id: canvas-positioning
// intent: absolute positioning with Canvas and .Canvas(left, top)
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Canvas Positioning", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        var accent = ThemeResource.Brush("AccentFillColorDefaultBrush");
        var stroke = ThemeResource.Brush("CardStrokeColorDefaultBrush");

        return Border(
            Canvas(
                Line(72, 48, 180, 108).Stroke(stroke).StrokeThickness(2),
                Border(TextBlock("A").Padding(8))
                    .Background(Theme.CardBackground)
                    .WithBorder(Theme.CardStroke, 1)
                    .CornerRadius(8)
                    .Canvas(left: 48, top: 32),
                Ellipse().Width(24).Height(24).Fill(accent).CenterAt(x: 180, y: 108),
                Line(192, 120, 280, 160).Stroke(accent).StrokeThickness(2),
                Border(TextBlock("Focus").Padding(8))
                    .Background(Theme.LayerFill)
                    .WithBorder(Theme.CardStroke, 1)
                    .CornerRadius(8)
                    .Canvas(left: 280, top: 160))
            .Width(340)
            .Height(220)
        )
        .Padding(20)
        .Background(Theme.SolidBackground);
    }
}
