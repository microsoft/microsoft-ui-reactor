// id: appbarbutton-in-commandbar
// intent: command bar with app bar buttons
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("AppBarButtonInCommandBar", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        var (lastAction, setLastAction) = UseState("(none)");
        return VStack(12,
            CommandBar(
                primaryCommands: new[]
                {
                    AppBarButton("Add", () => setLastAction("Add"), icon: "Add"),
                    AppBarButton("Share", () => setLastAction("Share"), icon: "Share")
                },
                secondaryCommands: new[]
                {
                    AppBarButton("Delete", () => setLastAction("Delete"), icon: "Delete")
                }),
            TextBlock($"Last action: {lastAction}").Padding(24));
    }
}