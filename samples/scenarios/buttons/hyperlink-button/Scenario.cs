// id: hyperlink-button
// intent: inline hyperlink-style button
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("HyperlinkButton", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        var (status, setStatus) = UseState("Open docs");
        return VStack(12,
            TextBlock("Need more details?"),
            HyperlinkButton("Open docs", onClick: () => setStatus("Navigation requested"))
                .TextLink(),
            TextBlock(status).Foreground(Theme.SecondaryText))
            .Padding(24);
    }
}