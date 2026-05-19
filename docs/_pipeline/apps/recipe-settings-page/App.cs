using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<SettingsRecipeApp>("Settings Recipe", width: 460, height: 420
#if DEBUG
    , preview: true
#endif
);

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

        // <snippet:render>
        return VStack(16,
            Heading("Settings"),
            SettingsRow("Notifications",
                ToggleSwitch(notify, setNotify)),
            SettingsRow("Theme",
                ComboBox(["System", "Light", "Dark"], theme, setTheme)),
            SettingsRow("Volume",
                Slider(volume, 0, 100, setVolume).Width(200))
        ).Padding(20);
        // </snippet:render>
    }

    // <snippet:row-helper>
    // A `SettingsRow` is a label + control — two slots in an HStack with a
    // fixed-width label so the controls line up across rows.
    private static Element SettingsRow(string label, Element control) =>
        HStack(16,
            TextBlock(label).Width(120),
            control
        );
    // </snippet:row-helper>
}
