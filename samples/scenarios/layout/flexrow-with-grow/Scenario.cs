// id: flexrow-with-grow
// intent: CSS-flexbox row with one child growing to fill space
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Layout;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("FlexRow Grow", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        return Border(
            (FlexRow(
                Subtitle("Files"),
                TextBlock("").Flex(grow: 1),
                Button("Refresh"),
                Button("Share")) with
            {
                AlignItems = FlexAlign.Center,
                ColumnGap = 8,
            })
            .Padding(16)
        )
        .Background(Theme.SolidBackground);
    }
}
