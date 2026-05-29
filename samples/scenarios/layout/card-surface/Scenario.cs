// id: card-surface
// intent: themed card surface following Win11 design
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Core.Theme;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Card Surface", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        return Border(
            Card(
                VStack(12,
                    Subtitle("Sprint planning"),
                    TextBlock("Card() applies the canonical WinUI surface, stroke, radius, and padding.")
                        .Foreground(SecondaryText),
                    Button("Open board")))
            .Width(280)
        )
        .Padding(24)
        .Background(SolidBackground);
    }
}
