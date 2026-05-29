// id: border-with-corner
// intent: padded border with corner radius
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Border With Corner", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        return Border(
            Border(
                VStack(8,
                    Subtitle("Pinned note"),
                    TextBlock("Border, padding, and corner radius create a clear content container.")
                        .Foreground(Theme.SecondaryText),
                    Caption("Updated just now").Foreground(Theme.TertiaryText))
                .Padding(16))
            .Background(Theme.CardBackground)
            .WithBorder(Theme.CardStroke, 1)
            .CornerRadius(14)
            .Padding(4)
        )
        .Padding(24)
        .Background(Theme.SolidBackground);
    }
}
