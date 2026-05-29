// id: vstack-basic
// intent: vertical shrink-wrap stack with spacing
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("VStack Basic", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        return Border(
            VStack(12,
                Subtitle("Daily checklist"),
                TextBlock("Keep related actions grouped in a clean vertical stack.")
                    .Foreground(Theme.SecondaryText),
                Button("Review changes"),
                Button("Run validation"),
                Button("Ship update"))
            .Padding(20)
        )
        .Background(Theme.SolidBackground);
    }
}
