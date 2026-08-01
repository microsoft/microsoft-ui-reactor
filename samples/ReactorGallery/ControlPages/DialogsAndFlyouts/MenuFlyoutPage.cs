using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.DialogsAndFlyouts;

class MenuFlyoutPage : Component
{
    public override Element Render()
    {
        var (lastAction, setLastAction) = UseState("(none)");
        var (formatAction, setFormatAction) = UseState("(none)");

        return ScrollView(
            VStack(16,
                PageHeader("MenuFlyout",
                    "A flyout that displays a list of menu commands."),

                SampleCard("Basic MenuFlyout",
                    VStack(8,
                        MenuFlyout(
                            Button("Open Menu"),
                            MenuItem("Cut", () => setLastAction("Cut"), icon: "Cut"),
                            MenuItem("Copy", () => setLastAction("Copy"), icon: "Copy"),
                            MenuItem("Paste", () => setLastAction("Paste"), icon: "Paste")),
                        TextBlock($"Last action: {lastAction}").Foreground(Theme.SecondaryText)
                    ),
                    @"MenuFlyout(
    Button(""Open Menu""),
    MenuItem(""Cut"", () => {}, icon: ""Cut""),
    MenuItem(""Copy"", () => {}, icon: ""Copy""),
    MenuItem(""Paste"", () => {}, icon: ""Paste""))"),

                SampleCard("MenuFlyout with Separators and SubItems",
                    VStack(8,
                        MenuFlyout(
                            Button("Format"),
                            MenuItem("Bold", () => setFormatAction("Bold")),
                            MenuItem("Italic", () => setFormatAction("Italic")),
                            MenuSeparator(),
                            MenuSubItem("Font Size",
                                MenuItem("Small", () => setFormatAction("Small")),
                                MenuItem("Medium", () => setFormatAction("Medium")),
                                MenuItem("Large", () => setFormatAction("Large")))),
                        TextBlock($"Last action: {formatAction}").Foreground(Theme.SecondaryText)
                    ),
                    @"var (formatAction, setFormatAction) = UseState(""(none)"");

VStack(8,
    MenuFlyout(
        Button(""Format""),
        MenuItem(""Bold"", () => setFormatAction(""Bold"")),
        MenuItem(""Italic"", () => setFormatAction(""Italic"")),
        MenuSeparator(),
        MenuSubItem(""Font Size"",
            MenuItem(""Small"", () => setFormatAction(""Small"")),
            MenuItem(""Medium"", () => setFormatAction(""Medium"")))),
    TextBlock($""Last action: {formatAction}""));")
            ).Margin(36, 24, 36, 36)
        );
    }
}
