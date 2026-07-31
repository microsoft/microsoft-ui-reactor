using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.MenusAndToolbars;

class CommandBarPage : Component
{
    public override Element Render()
    {
        var (lastAction, setLastAction) = UseState("(none)");
        var (secondaryAction, setSecondaryAction) = UseState("(none)");
        var (isBold, setIsBold) = UseState(false);

        return ScrollView(
            VStack(16,
                PageHeader("CommandBar",
                    "A toolbar for exposing app commands and actions."),

                SampleCard("Primary Commands",
                    VStack(8,
                        CommandBar(
                            primaryCommands: new AppBarItemBase[]
                            {
                                AppBarButton("Add", () => setLastAction("Add"), icon: "Add"),
                                AppBarButton("Edit", () => setLastAction("Edit"), icon: "Edit"),
                                AppBarSeparator(),
                                AppBarButton("Delete", () => setLastAction("Delete"), icon: "Delete"),
                            }),
                        TextBlock($"Last action: {lastAction}").Foreground(Theme.SecondaryText)
                    ),
                    @"CommandBar(primaryCommands: new AppBarItemBase[] {
    AppBarButton(""Add"", () => setLastAction(""Add""), icon: ""Add""),
    AppBarButton(""Edit"", () => setLastAction(""Edit""), icon: ""Edit""),
    AppBarSeparator(),
    AppBarButton(""Delete"", () => setLastAction(""Delete""), icon: ""Delete""),
})"),

                SampleCard("Primary and Secondary Commands",
                    VStack(8,
                        CommandBar(
                            primaryCommands: new AppBarItemBase[]
                            {
                                AppBarButton("Share", () => setSecondaryAction("Share"), icon: "Share"),
                                AppBarToggleButton("Bold", isBold, b => setIsBold(b), icon: "Bold"),
                            },
                            secondaryCommands: new AppBarItemBase[]
                            {
                                AppBarButton("Copy", () => setSecondaryAction("Copy")),
                                AppBarButton("Paste", () => setSecondaryAction("Paste")),
                            }),
                        TextBlock($"Last action: {secondaryAction}").Foreground(Theme.SecondaryText)
                    ),
                    @"CommandBar(
    primaryCommands: new AppBarItemBase[] {
        AppBarButton(""Share"", () => {}, icon: ""Share""),
        AppBarToggleButton(""Bold"", isBold, b => setIsBold(b), icon: ""Bold""),
    },
    secondaryCommands: new AppBarItemBase[] {
        AppBarButton(""Copy"", () => {}),
        AppBarButton(""Paste"", () => {}),
    })")
            ).Margin(36, 24, 36, 36)
        );
    }
}
