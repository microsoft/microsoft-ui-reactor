// id: use-state-record
// intent: record-shaped state with structural equality and with-expression updates
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// Immutable record state stays easy to compare and update with `with` expressions.
ReactorApp.Run<App>("UseStateRecord", width: 400, height: 200);

class App : Component
{
    record Settings(bool DarkMode, int FontSize);

    public override Element Render()
    {
        var (settings, setSettings) = UseState(new Settings(false, 14));

        return VStack(12,
            Heading(settings.DarkMode ? "Dark mode" : "Light mode"),
            ToggleSwitch(
                settings.DarkMode,
                isOn => setSettings(settings with { DarkMode = isOn }),
                onContent: "On",
                offContent: "Off",
                header: "Theme"),
            TextBlock($"Font size: {settings.FontSize}"),
            Slider(
                settings.FontSize,
                min: 12,
                max: 24,
                onValueChanged: value => setSettings(settings with { FontSize = (int)Math.Round(value) })),
            TextBlock("Preview text").FontSize(settings.FontSize));
    }
}

