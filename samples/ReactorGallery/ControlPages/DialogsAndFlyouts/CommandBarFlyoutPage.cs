using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.DialogsAndFlyouts;

class CommandBarFlyoutPage : Component
{
    public override Element Render()
    {
        var (lastAction, setLastAction) = UseState("(none)");
        var (commandAction, setCommandAction) = UseState("(none)");

        return ScrollView(
            VStack(16,
                PageHeader("CommandBarFlyout",
                    "A flyout that provides quick access to common commands."),

                SampleCard("Basic CommandBarFlyout",
                    VStack(8,
                        CommandBarFlyout(
                            Button("Show Commands"),
                            primaryCommands: new AppBarItemBase[]
                            {
                                AppBarButton("Cut", () => setLastAction("Cut"), icon: "Cut"),
                                AppBarButton("Copy", () => setLastAction("Copy"), icon: "Copy"),
                                AppBarButton("Paste", () => setLastAction("Paste"), icon: "Paste"),
                            }),
                        TextBlock($"Last action: {lastAction}").Foreground(Theme.SecondaryText)
                    ),
                    @"CommandBarFlyout(
    Button(""Show Commands""),
    primaryCommands: new AppBarItemBase[] {
        AppBarButton(""Cut"", () => {}, icon: ""Cut""),
        AppBarButton(""Copy"", () => {}, icon: ""Copy""),
        AppBarButton(""Paste"", () => {}, icon: ""Paste""),
    })"),

                SampleCard("CommandBarFlyout with Secondary",
                    VStack(8,
                        CommandBarFlyout(
                            Button("More Options"),
                            primaryCommands: new AppBarItemBase[]
                            {
                                AppBarButton("Share", () => setCommandAction("Share"), icon: "Share"),
                            },
                            secondaryCommands: new AppBarItemBase[]
                            {
                                AppBarButton("Select All", () => setCommandAction("Select All")),
                                AppBarButton("Print", () => setCommandAction("Print")),
                            }),
                        TextBlock($"Last action: {commandAction}").Foreground(Theme.SecondaryText)
                    ),
                    @"var (commandAction, setCommandAction) = UseState(""(none)"");

VStack(8,
    CommandBarFlyout(
        Button(""More Options""),
        primaryCommands: new AppBarItemBase[] {
            AppBarButton(""Share"", () => setCommandAction(""Share""), icon: ""Share""),
        },
        secondaryCommands: new AppBarItemBase[] {
            AppBarButton(""Select All"", () => setCommandAction(""Select All"")),
            AppBarButton(""Print"", () => setCommandAction(""Print"")),
        }),
    TextBlock($""Last action: {commandAction}""));")
            ).Margin(36, 24, 36, 36)
        );
    }
}
