using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor;

class SettingsExpanderPage : Component
{
    public override Element Render()
    {
        var (open, setOpen) = UseState(true);
        var (scale, setScale) = UseState(1);
        var (brightness, setBrightness) = UseState(70.0);
        var (nightLight, setNightLight) = UseState(false);
        var (sounds, setSounds) = UseState(true);
        var (volume, setVolume) = UseState(60.0);
        var (opened, setOpened) = UseState(0);

        return ScrollView(
            VStack(16,
                PageHeader("SettingsExpander", "A settings card that expands to show more settings. A native port of the Windows Community Toolkit control."),

                SampleCard("Header control and expanding items",
                    SettingsExpander("Display",
                        description: "Brightness, night light and scale",
                        icon: FontIcon("\uE7F4"),
                        content: ComboBox(["100%", "125%", "150%"], scale, setScale),
                        isExpanded: open,
                        onIsExpandedChanged: setOpen,
                        items:
                        [
                            SettingsCard("Brightness", content: Slider(brightness, 0, 100, setBrightness)),
                            SettingsCard("Night light", content: ToggleSwitch(nightLight, setNightLight)),
                        ]),
                    """
                    // content: the control in the header row. items: the expanding body.
                    SettingsExpander("Display",
                        description: "Brightness, night light and scale",
                        icon: FontIcon("\uE7F4"),
                        content: ComboBox(["100%", "125%", "150%"], scale, setScale),
                        isExpanded: open,
                        onIsExpandedChanged: setOpen,
                        items:
                        [
                            SettingsCard("Brightness", content: Slider(brightness, 0, 100, setBrightness)),
                            SettingsCard("Night light", content: ToggleSwitch(nightLight, setNightLight)),
                        ])
                    """,
                    OptionPanel(
                        TextBlock(open ? "Expanded" : "Collapsed")
                    )),

                SampleCard("Items header, footer and clickable items",
                    SettingsExpander("Sound",
                        description: "Volume and system sounds",
                        icon: FontIcon("\uE767"),
                        items:
                        [
                            SettingsCard("Volume", content: Slider(volume, 0, 100, setVolume)),
                            SettingsCard("System sounds", content: ToggleSwitch(sounds, setSounds)),
                            SettingsCard("More sound settings", onClick: () => setOpened(opened + 1)),
                        ])
                        .ItemsHeader(InfoBar(message: "Some sounds are muted while Focus is on.").IsClosable(false))
                        .ItemsFooter(TextBlock("Changes apply to every output device.").Margin(58, 12, 16, 12).Opacity(0.6)),
                    """
                    SettingsExpander("Sound",
                        description: "Volume and system sounds",
                        icon: FontIcon("\uE767"),
                        items:
                        [
                            SettingsCard("Volume", content: Slider(volume, 0, 100, setVolume)),
                            SettingsCard("System sounds", content: ToggleSwitch(sounds, setSounds)),
                            SettingsCard("More sound settings", onClick: () => setOpened(opened + 1)),
                        ])
                        .ItemsHeader(InfoBar(message: "Some sounds are muted while Focus is on.").IsClosable(false))
                        .ItemsFooter(TextBlock("Changes apply to every output device.").Margin(58, 12, 16, 12).Opacity(0.6))
                    """,
                    OptionPanel(
                        TextBlock($"More sound settings opened {opened} time(s)")
                    ))
            ).Margin(36, 24, 36, 36)
        );
    }
}
