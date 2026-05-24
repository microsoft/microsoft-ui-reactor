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
                        TitleBar("Gallery") with
                        {
                            Content = HStack(8,
                                AutoSuggestBox("", _ => { }).Width(200),
                                Button("\uE713", () => { }).Width(36).Height(36)
                            ),
                        }
                    ).Background(Theme.LayerFill).CornerRadius(4).Height(48),
                    @"TitleBar(""Gallery"") with {
    Content = HStack(8,
        AutoSuggestBox("""", _ => {}).Width(200),
        Button(""⚙"", () => {}))
}")
            ).Margin(36, 24, 36, 36)
        );
    }
}
