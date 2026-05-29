// id: flexcolumn-with-justify
// intent: flexbox column with alignment and justification
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Layout;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("FlexColumn Justify", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        return Border(
            (FlexColumn(
                VStack(4,
                    Subtitle("Welcome"),
                    TextBlock("Header content stays at the top.").Foreground(Theme.SecondaryText)),
                Border(TextBlock("Centered content").Padding(12))
                    .Background(Theme.CardBackground)
                    .WithBorder(Theme.CardStroke, 1)
                    .CornerRadius(8),
                Caption("Footer status or actions can sit at the bottom.")
                    .Foreground(Theme.SecondaryText)) with
            {
                JustifyContent = FlexJustify.SpaceBetween,
                AlignItems = FlexAlign.Center,
            })
            .Height(220)
            .FlexPadding(20)
        )
        .Background(Theme.SolidBackground);
    }
}
