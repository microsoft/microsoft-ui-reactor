// id: button-with-icon
// intent: button with a symbol icon
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("ButtonWithIcon", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        var (status, setStatus) = UseState("Ready");
        return VStack(12,
            Button(HStack(8, Icon(Symbol.Save), TextBlock("Save draft")),
                () => setStatus("Draft saved"))
                .AutomationName("Save draft")
                .AccentButton(),
            TextBlock(status).Foreground(Theme.SecondaryText))
            .Padding(24);
    }
}