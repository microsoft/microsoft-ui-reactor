using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.MenusAndToolbars;

class MenuBarPage : Component
{
    public override Element Render()
    {
        var (lastAction, setLastAction) = UseState("(none)");
        var (formatAction, setFormatAction) = UseState("(none)");

        return ScrollView(
            VStack(16,
                PageHeader("MenuBar",
                    "A horizontal bar that hosts a set of drop-down menus."),

                SampleCard("File/Edit/View Menus",
                    VStack(8,
                        MenuBar(
                            Menu("File",
                                MenuItem("New", () => setLastAction("New"), icon: "Page2"),
                                MenuItem("Open", () => setLastAction("Open"), icon: "OpenFile"),
                                MenuSeparator(),
                                MenuItem("Save", () => setLastAction("Save"), icon: "Save"),
                                MenuItem("Exit", () => setLastAction("Exit"))),
                            Menu("Edit",
                                MenuItem("Undo", () => setLastAction("Undo"), icon: "Undo"),
                                MenuItem("Redo", () => setLastAction("Redo"), icon: "Redo"),
                                MenuSeparator(),
                                MenuItem("Cut", () => setLastAction("Cut")),
                                MenuItem("Copy", () => setLastAction("Copy")),
                                MenuItem("Paste", () => setLastAction("Paste"))),
                            Menu("View",
                                MenuItem("Zoom In", () => setLastAction("Zoom In"), icon: "ZoomIn"),
                                MenuItem("Zoom Out", () => setLastAction("Zoom Out"), icon: "ZoomOut"))
                        ),
                        TextBlock($"Last action: {lastAction}").Foreground(Theme.SecondaryText)
                    ),
                    @"MenuBar(
    Menu(""File"",
        MenuItem(""New"", () => {}, icon: ""Page2""),
        MenuItem(""Open"", () => {}, icon: ""OpenFile""),
        MenuSeparator(),
        MenuItem(""Save"", () => {}, icon: ""Save"")),
    Menu(""Edit"",
        MenuItem(""Undo"", () => {}, icon: ""Undo""),
        MenuItem(""Cut"", () => {}),
        MenuItem(""Copy"", () => {})))"),

                SampleCard("Nested SubMenus",
                    VStack(8,
                        MenuBar(
                            Menu("Format",
                                MenuSubItem("Text Size",
                                    MenuItem("Small", () => setFormatAction("Small")),
                                    MenuItem("Medium", () => setFormatAction("Medium")),
                                    MenuItem("Large", () => setFormatAction("Large"))),
                                MenuSubItem("Alignment",
                                    MenuItem("Left", () => setFormatAction("Left")),
                                    MenuItem("Center", () => setFormatAction("Center")),
                                    MenuItem("Right", () => setFormatAction("Right"))))
                        ),
                        TextBlock($"Last action: {formatAction}").Foreground(Theme.SecondaryText)
                    ),
                    @"var (formatAction, setFormatAction) = UseState(""(none)"");

VStack(8,
    MenuBar(
        Menu(""Format"",
            MenuSubItem(""Text Size"",
                MenuItem(""Small"", () => setFormatAction(""Small"")),
                MenuItem(""Medium"", () => setFormatAction(""Medium""))),
            MenuSubItem(""Alignment"",
                MenuItem(""Left"", () => setFormatAction(""Left"")),
                MenuItem(""Center"", () => setFormatAction(""Center""))))),
    TextBlock($""Last action: {formatAction}""));")
            ).Margin(36, 24, 36, 36)
        );
    }
}
