using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor;

class SettingsCardPage : Component
{
    public override Element Render()
    {
        var (notify, setNotify) = UseState(true);
        var (enabled, setEnabled) = UseState(true);
        var (opened, setOpened) = UseState(0);
        var (theme, setTheme) = UseState(2);
        var (sync, setSync) = UseState(true);

        return ScrollView(
            VStack(16,
                PageHeader("SettingsCard", "A Windows 11 settings row: an icon, a header, a description and a control. A native port of the Windows Community Toolkit control."),

                SampleCard("Settings row with a toggle switch",
                    SettingsCard("Notifications",
                        description: "Alerts from apps and the system",
                        icon: FontIcon("\uE7E7"),
                        content: ToggleSwitch(notify, setNotify))
                        .IsEnabled(enabled),
                    """
                    SettingsCard("Notifications",
                        description: "Alerts from apps and the system",
                        icon: FontIcon("\uE7E7"),
                        content: ToggleSwitch(notify, setNotify))
                    """,
                    OptionPanel(
                        CheckBox(enabled, setEnabled, "Enabled")
                    )),

                SampleCard("Clickable card that opens another page",
                    VStack(4,
                        SettingsCard("Storage",
                            description: "Free up space and manage drives",
                            icon: FontIcon("\uEDA2"),
                            onClick: () => setOpened(opened + 1)),
                        SettingsCard("Get more themes",
                            description: "Opens the Microsoft Store",
                            icon: FontIcon("\uE719"),
                            onClick: () => setOpened(opened + 1))
                            .ActionIcon(FontIcon("\uE8A7"))
                            .ActionIconToolTip("Opens in a new window"),
                        TextBlock($"Clicked {opened} time(s)").Opacity(0.6)
                    ),
                    """
                    SettingsCard("Storage",
                        description: "Free up space and manage drives",
                        icon: FontIcon("\uEDA2"),
                        onClick: () => setOpened(opened + 1))

                    SettingsCard("Get more themes",
                        description: "Opens the Microsoft Store",
                        icon: FontIcon("\uE719"),
                        onClick: () => setOpened(opened + 1))
                        .ActionIcon(FontIcon("\uE8A7"))
                        .ActionIconToolTip("Opens in a new window")
                    """),

                SampleCard("Control below the header or on the left",
                    VStack(4,
                        SettingsCard("Theme",
                            description: "Select which app theme to display",
                            icon: FontIcon("\uE790"),
                            content: ComboBox(["Light", "Dark", "Use system setting"], theme, setTheme))
                            .ContentAlignment(SettingsCardContentAlignment.Vertical),
                        SettingsCard("Sync",
                            content: CheckBox(sync, setSync, "Sync settings across devices")
                                .AutomationName("Sync settings across devices"))
                            .ContentAlignment(SettingsCardContentAlignment.Left)
                    ),
                    """
                    SettingsCard("Theme",
                        description: "Select which app theme to display",
                        icon: FontIcon("\uE790"),
                        content: ComboBox(["Light", "Dark", "Use system setting"], theme, setTheme))
                        .ContentAlignment(SettingsCardContentAlignment.Vertical)

                    // Name the check box yourself: content otherwise takes the hidden header as its name.
                    SettingsCard("Sync",
                        content: CheckBox(sync, setSync, "Sync settings across devices")
                            .AutomationName("Sync settings across devices"))
                        .ContentAlignment(SettingsCardContentAlignment.Left)
                    """)
            ).Margin(36, 24, 36, 36)
        );
    }
}
