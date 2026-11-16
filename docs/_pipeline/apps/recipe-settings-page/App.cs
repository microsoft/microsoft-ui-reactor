using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<SettingsRecipeApp>("Settings Recipe");

class SettingsRecipeApp : Component
{
    public override Element Render() => Component<SettingsPage>();
}

class SettingsPage : Component
{
    public override Element Render()
    {
        // <snippet:persisted-state>
        // Each preference is a separate UsePersisted call with its own key.
        // The window scope ties the values to the host's lifetime; flip to
        // PersistedScope.Application when the prefs should survive across
        // windows.
        var (notify, setNotify) = UsePersisted("prefs/notify", true,
            PersistedScope.Window);
        var (theme, setTheme) = UsePersisted("prefs/theme", 0,
            PersistedScope.Window);
        var (volume, setVolume) = UsePersisted("prefs/volume", 60.0,
            PersistedScope.Window);
        // </snippet:persisted-state>

        // <snippet:reset-card>
        // A clickable card runs an action instead of holding a control: it
        // shows a chevron and is a button for the keyboard and Narrator.
        var reset = SettingsCard("Reset to defaults",
            description: "Restore every preference on this page",
            icon: FontIcon("\uE7A7"),
            onClick: () =>
            {
                setNotify(true);
                setTheme(0);
                setVolume(60.0);
            });
        // </snippet:reset-card>

        // <snippet:render>
        // One SettingsCard per preference. Each card gives its control the
        // header as its automation name, so Narrator announces "Theme".
        return VStack(4,
            Heading("Settings").Margin(0, 0, 0, 12),
            SettingsCard("Notifications",
                description: "Show an alert when a task finishes",
                icon: FontIcon("\uE7E7"),
                content: ToggleSwitch(notify, setNotify)),
            SettingsCard("Theme",
                description: "Choose the app's colors",
                icon: FontIcon("\uE790"),
                content: ComboBox(["System", "Light", "Dark"], theme, setTheme)),
            SettingsCard("Volume",
                icon: FontIcon("\uE767"),
                content: Slider(volume, 0, 100, setVolume).Width(200)),
            reset
        ).Padding(20);
        // </snippet:render>
    }
}
