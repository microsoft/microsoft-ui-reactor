using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using WctControls;
using static Microsoft.UI.Reactor.Factories;
using ContentAlignment = CommunityToolkit.WinUI.Controls.ContentAlignment;
using WctCard = WctControls.SettingsCardElement;          // the Toolkit wrappers; the bare names are Reactor's port
using WctExpander = WctControls.SettingsExpanderElement;

// Fixed SettingsCard / SettingsExpander content for the spec 065 reference
// screenshots (docs/specs/065/toolkit/). The native Reactor port renders this
// same content again so the two sets can be compared side by side.
//
// Every sample carries a stable AutomationId and sits in an 8 px frame, so a
// capture can crop the card's UI Automation bounds plus 8 px: room for the focus
// rectangle the Toolkit draws 3 px outside the card (FocusVisualMargin = -3).
// The column paints a solid theme background, because cards are semi-transparent
// and Mica would make the captures depend on the desktop wallpaper.
internal sealed record ThemeProps(ElementTheme Theme, Action<ElementTheme> SetTheme);

internal sealed class SettingsReferencePage : Component<ThemeProps>
{
    public override Element Render() => Gallery.Page(
        "Settings reference",
        "Fixed SettingsCard and SettingsExpander content for the spec 065 reference screenshots.",
        VStack(16,
            HStack(8,
                Button("Use system theme", () => Props.SetTheme(ElementTheme.Default)),
                Button("Light theme", () => Props.SetTheme(ElementTheme.Light)),
                Button("Dark theme", () => Props.SetTheme(ElementTheme.Dark))),
            VStack(0,
                Frame(WctCard.SettingsCard(
                    header: "Notifications",
                    description: "Alerts from apps and the system",
                    headerIcon: Icon(FontIcon("\uE7E7")),
                    content: ToggleSwitch(isOn: true)).AutomationId("ref-card")),
                Frame(WctCard.SettingsCard(
                    header: "Storage",
                    description: "Free up space",
                    headerIcon: Icon(FontIcon("\uEDA2")),
                    isClickEnabled: true,
                    onClick: () => { }).AutomationId("ref-card-clickable")),
                Frame(WctCard.SettingsCard(
                    header: "Theme",
                    description: "Select which app theme to display",
                    headerIcon: Icon(FontIcon("\uE790")),
                    contentAlignment: ContentAlignment.Vertical,
                    content: ComboBox(["Light", "Dark", "Use system setting"], 2)).AutomationId("ref-card-vertical")),
                Frame(WctCard.SettingsCard(
                    header: "Bluetooth",
                    description: "Discoverable as \"CONTOSO-PC\"",
                    headerIcon: Icon(FontIcon("\uE702")),
                    content: ToggleSwitch(isOn: false)).IsEnabled(false).AutomationId("ref-card-disabled")),
                Frame(Display(isExpanded: false).AutomationId("ref-expander")),
                Frame(Display(isExpanded: true).AutomationId("ref-expander-expanded")))
                .Background(Theme.SolidBackground)
                .MaxWidth(656)
                // Keeps the overlay scroll indicator (right edge of the viewport) out of the crops.
                .Margin(0, 0, 24, 0)));

    private static WctExpander Display(bool isExpanded) => WctExpander.SettingsExpander(
        header: "Display",
        description: "Brightness, night light and scale",
        headerIcon: Icon(FontIcon("\uE7F4")),
        content: ComboBox(["100%", "125%", "150%"], 1),
        isExpanded: isExpanded,
        items: new object[]
        {
            WctCard.SettingsCard(header: "Brightness", content: Slider(70, 0, 100)),
            WctCard.SettingsCard(header: "Night light", content: ToggleSwitch(isOn: false)),
        });

    private static Element Frame(Element sample) => Border(sample).Padding(8);
}
