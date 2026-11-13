using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<SettingsCardsApp>("Settings Cards");

// <snippet:basic-card>
class BasicCardDemo : Component
{
    public override Element Render()
    {
        var (notify, setNotify) = UseState(true);
        var (bluetooth, setBluetooth) = UseState(false);

        return VStack(4,
            SettingsCard("Notifications",
                description: "Alerts from apps and the system",
                icon: FontIcon("\uE7E7"),
                content: ToggleSwitch(notify, setNotify)),
            SettingsCard("Bluetooth",
                description: "Discoverable as \"CONTOSO-PC\"",
                icon: FontIcon("\uE702"),
                content: ToggleSwitch(bluetooth, setBluetooth))
        ).Padding(24);
    }
}
// </snippet:basic-card>

// <snippet:clickable-card>
class ClickableCardDemo : Component
{
    public override Element Render()
    {
        var (opened, setOpened) = UseState(0);

        // onClick makes the whole card a button: chevron, hover and pressed
        // states, Tab focus, Enter / Space, and a UI Automation Invoke.
        return VStack(8,
            SettingsCard("Storage",
                description: "Free up space and manage drives",
                icon: FontIcon("\uEDA2"),
                onClick: () => setOpened(opened + 1)),
            TextBlock($"Opened {opened} time(s)").Opacity(0.6)
        ).Padding(24);
    }
}
// </snippet:clickable-card>

// <snippet:content-alignment>
class ContentAlignmentDemo : Component
{
    public override Element Render()
    {
        var (theme, setTheme) = UseState(2);
        var (sync, setSync) = UseState(true);

        return VStack(4,
            // The control moves below the header and description.
            SettingsCard("Theme",
                description: "Select which app theme to display",
                icon: FontIcon("\uE790"),
                content: ComboBox(["Light", "Dark", "Use system setting"], theme, setTheme))
                .ContentAlignment(SettingsCardContentAlignment.Vertical),
            // The control is left-aligned and the header is hidden: for content
            // that carries its own label, like a check box.
            SettingsCard("Sync",
                content: CheckBox(sync, setSync, "Sync settings across devices"))
                .ContentAlignment(SettingsCardContentAlignment.Left)
        ).Padding(24);
    }
}
// </snippet:content-alignment>

// <snippet:expander>
class ExpanderDemo : Component
{
    public override Element Render()
    {
        var (open, setOpen) = UseState(true);
        var (scale, setScale) = UseState(1);
        var (brightness, setBrightness) = UseState(70.0);
        var (nightLight, setNightLight) = UseState(false);

        // content: the control in the header row. items: the expanding body.
        return VStack(4,
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
        ).Padding(24);
    }
}
// </snippet:expander>

// <snippet:command-card>
class CommandCardDemo : Component
{
    public override Element Render()
    {
        var (lastSync, setLastSync) = UseState<string?>(null);
        var sync = new Command
        {
            Label = "Sync now",
            Description = $"Last synced: {lastSync ?? "never"}",
            Icon = FontIcon("\uE895"),
            Execute = () => setLastSync(DateTime.Now.ToShortTimeString()),
        };
        var clear = new Command
        {
            Label = "Clear sync history",
            Description = "Available after the first sync",
            Icon = FontIcon("\uE74D"),
            Execute = () => setLastSync(null),
            CanExecute = lastSync is not null,
        };

        // The command's label, description and icon fill the card; a click
        // runs it, and the card is disabled while the command cannot run.
        return VStack(4,
            SettingsCard(sync),
            SettingsCard(clear)
        ).Padding(24);
    }
}
// </snippet:command-card>

// <snippet:rich-content>
class RichContentDemo : Component
{
    public override Element Render()
    {
        var (diagnostics, setDiagnostics) = UseState(false);

        // The Element overload takes rich header and description content.
        return VStack(4,
            SettingsCard("Diagnostic data",
                description: HStack(4,
                    TextBlock("Help improve the app."),
                    HyperlinkButton("Privacy statement", new Uri("https://privacy.microsoft.com/"))),
                icon: FontIcon("\uE9D9"),
                content: ToggleSwitch(diagnostics, setDiagnostics))
        ).Padding(24);
    }
}
// </snippet:rich-content>

class SettingsCardsApp : Component
{
    public override Element Render() => ScrollView(
        VStack(24,
            Heading("Settings Cards"),
            Component<BasicCardDemo>(),
            Component<ClickableCardDemo>(),
            Component<ContentAlignmentDemo>(),
            Component<ExpanderDemo>(),
            Component<CommandCardDemo>(),
            Component<RichContentDemo>()
        ).Padding(24)
    );
}
