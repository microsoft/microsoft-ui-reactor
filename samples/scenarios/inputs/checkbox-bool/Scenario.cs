// id: checkbox-bool
// intent: checkbox bound to boolean state
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Checkbox Boolean", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        var (accepted, setAccepted) = UseState(false);
        return VStack(12,
            Heading("CheckBox"),
            CheckBox(accepted, setAccepted, "Accept terms"),
            TextBlock($"Accepted: {accepted}"))
            .Margin(16);
    }
}
