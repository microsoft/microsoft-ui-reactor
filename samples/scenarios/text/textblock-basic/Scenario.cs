// id: textblock-basic
// intent: display a simple text label
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("TextBlock Basic", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        return VStack(
            TextBlock("Hello from Reactor."),
            TextBlock("TextBlock displays read-only text content."),
            TextBlock("Use it for labels, hints, and short status messages."));
    }
}
