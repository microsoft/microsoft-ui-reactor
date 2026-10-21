using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Navigation;

class TitleBarPage : Component
{
    public override Element Render()
    {
        var (subtitle, setSubtitle) = UseState("Preview");

        return ScrollView(
            VStack(16,
                PageHeader("TitleBar",
                    "A customizable title bar for the application window."),

                SampleCard("Basic TitleBar",
                    Border(
                        TitleBar("My Application")
                    ).Background(Theme.LayerFill).CornerRadius(4).Height(48),
                    @"TitleBar(""My Application"")"),

                SampleCard("Icon: inherited, custom, or none",
                    VStack(8,
                        Border(
                            TitleBar("Inherits the window icon")
                        ).Background(Theme.LayerFill).CornerRadius(4).Height(48),
                        Border(
                            TitleBar("Custom icon").Icon(new FontIconData("\uE790"))
                        ).Background(Theme.LayerFill).CornerRadius(4).Height(48),
                        Border(
                            TitleBar("No icon").NoIcon()
                        ).Background(Theme.LayerFill).CornerRadius(4).Height(48),
                        Caption("With no .Icon(...), a TitleBar shows the window's own icon: the one passed to ReactorApp.Run(icon: ...) or WindowSpec.Icon, else Assets\\AppIcon.ico. This gallery declares its icon once in Program.cs.")
                                                    .Foreground(Theme.SecondaryText).TextWrapping()
                    ),
                    @"// Program.cs: the main window's icon drives its caption, Alt-Tab, and every TitleBar in that window.
// A secondary window inherits from its own WindowSpec.Icon instead.
ReactorApp.Run<Shell>(""My App"", icon: WindowIcon.FromPath(""Assets/AppIcon.ico""));

TitleBar(""Inherits the window icon"")
TitleBar(""Custom icon"").Icon(new FontIconData(""\uE790""))
TitleBar(""No icon"").NoIcon()"),

                SampleCard("TitleBar with Subtitle",
                    VStack(8,
                        Border(
                            TitleBar("My App").Subtitle(subtitle)
                        ).Background(Theme.LayerFill).CornerRadius(4).Height(48),
                        TextBox(subtitle, s => setSubtitle(s), placeholderText: "Enter subtitle")
                            .Width(250)
                    ),
                    @"TitleBar(""My App"").Subtitle(""Preview"")",
                    options: OptionPanel(
                        TextBox(subtitle, s => setSubtitle(s), header: "Subtitle")
                    )),

                SampleCard("TitleBar with Content",
                    Border(
                        (TitleBar("Gallery") with
                        {
                            Content = HStack(8,
                                AutoSuggestBox("", _ => { }).Width(200),
                                Button(Icon(FontIcon("\uE713", fontSize: 16)), () => { })
                                    .Width(36).Height(36).Padding(0)
                                    .ToolTip("Settings")
                                    .AutomationName("Settings")
                                    .IsDragRegion(false)
                            ),
                        }).AutoRefreshDragRegions()
                    ).Background(Theme.LayerFill).CornerRadius(4).Height(48),
                    @"(TitleBar(""Gallery"") with {
    Content = HStack(8,
        AutoSuggestBox("""", _ => {}).Width(200),
        Button(Icon(FontIcon(""\uE713"", fontSize: 16)), OnSettings)
            .AutomationName(""Settings"").IsDragRegion(false))
}).AutoRefreshDragRegions()")
            ).Margin(36, 24, 36, 36)
        );
    }
}
