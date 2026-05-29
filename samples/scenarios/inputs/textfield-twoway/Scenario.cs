// id: textfield-twoway
// intent: two-way text input binding
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("TextField Two-Way", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("Reactor");
        return VStack(12,
            Heading("TextField"),
            (TextField(name, setName, placeholder: "Type a name") with { Header = "Display name", MaxLength = 24 }),
            TextBlock($"Current value: {name}"))
            .Margin(16);
    }
}
