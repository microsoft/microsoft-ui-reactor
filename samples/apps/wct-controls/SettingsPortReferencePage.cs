using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

// The spec 065 reference content again (see SettingsReferencePage), rendered by Reactor's
// native SettingsCard and SettingsExpander instead of the Toolkit controls, for the
// side-by-side captures in docs/specs/065/reactor/. Same samples, AutomationIds, frames and
// column, so docs/specs/065/capture.ps1 captures both pages the same way.
internal sealed class SettingsPortReferencePage : Component<ThemeProps>
{
    public override Element Render() => Gallery.Page(
        "Settings reference (Reactor)",
        "The spec 065 reference content, rendered by Reactor's native SettingsCard and SettingsExpander.",
        VStack(16,
            HStack(8,
                Button("Use system theme", () => Props.SetTheme(ElementTheme.Default)),
                Button("Light theme", () => Props.SetTheme(ElementTheme.Light)),
                Button("Dark theme", () => Props.SetTheme(ElementTheme.Dark))),
            VStack(0,
                Frame(SettingsCard("Notifications",
                    description: "Alerts from apps and the system",
                    icon: FontIcon("\uE7E7"),
                    content: ToggleSwitch(isOn: true)).AutomationId("ref-card")),
                Frame(SettingsCard("Storage",
                    description: "Free up space",
                    icon: FontIcon("\uEDA2"),
                    onClick: () => { }).AutomationId("ref-card-clickable")),
                Frame(SettingsCard("Theme",
                    description: "Select which app theme to display",
                    icon: FontIcon("\uE790"),
                    content: ComboBox(["Light", "Dark", "Use system setting"], 2))
                    .ContentAlignment(SettingsCardContentAlignment.Vertical)
                    .AutomationId("ref-card-vertical")),
                Frame(SettingsCard("Bluetooth",
                    description: "Discoverable as \"CONTOSO-PC\"",
                    icon: FontIcon("\uE702"),
                    content: ToggleSwitch(isOn: false)).IsEnabled(false).AutomationId("ref-card-disabled")),
                Frame(Display(isExpanded: false).AutomationId("ref-expander")),
                Frame(Display(isExpanded: true).AutomationId("ref-expander-expanded")),
                Frame(SettingsCard("Sync",
                    content: CheckBox(true, label: "Sync settings across devices"))
                    .ContentAlignment(SettingsCardContentAlignment.Left)
                    .AutomationId("ref-card-left")),
                Frame(SettingsCard("Microsoft Store",
                    description: "Get more apps",
                    icon: FontIcon("\uE719"),
                    onClick: () => { })
                    .ActionIcon(FontIcon("\uE8A7"))
                    .AutomationId("ref-card-actionicon")),
                Frame(SettingsExpander("Sound",
                    description: "Volume and system sounds",
                    icon: FontIcon("\uE767"),
                    isExpanded: true,
                    items:
                    [
                        SettingsCard("Volume", content: Slider(60, 0, 100)),
                        SettingsCard("More sound settings", onClick: () => { }),
                    ])
                    .ItemsHeader(SettingsReferencePage.ItemsNote("Some sounds are muted while Focus is on."))
                    .ItemsFooter(SettingsReferencePage.ItemsNote("Changes apply to every output device."))
                    .AutomationId("ref-expander-items")))
                .Background(Theme.SolidBackground)
                .MaxWidth(656)
                // Keeps the overlay scroll indicator (right edge of the viewport) out of the crops.
                .Margin(0, 0, 24, 0)));

    private static SettingsExpanderElement Display(bool isExpanded) => SettingsExpander("Display",
        description: "Brightness, night light and scale",
        icon: FontIcon("\uE7F4"),
        content: ComboBox(["100%", "125%", "150%"], 1),
        isExpanded: isExpanded,
        items:
        [
            SettingsCard("Brightness", content: Slider(70, 0, 100)),
            SettingsCard("Night light", content: ToggleSwitch(isOn: false)),
        ]);

    private static Element Frame(Element sample) => Border(sample).Padding(8);
}
