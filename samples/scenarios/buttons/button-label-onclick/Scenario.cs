// id: button-label-onclick
// intent: basic button with click handler
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("ButtonLabelOnClick", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        return VStack(12,
            Heading("Click counter"),
            TextBlock($"Clicked {count} time{(count == 1 ? "" : "s")}."),
            Button("Increment", () => setCount(count + 1)).AccentButton())
            .Padding(24);
    }
}