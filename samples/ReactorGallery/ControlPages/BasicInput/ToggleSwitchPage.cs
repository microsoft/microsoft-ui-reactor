using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.BasicInput;

class ToggleSwitchPage: Component
{
    public override Element Render()
    {
        var (isOn, setIsOn) = UseState(false);
        var (customOn, setCustomOn) = UseState(true);
        var (headerOn, setHeaderOn) = UseState(false);

        return ScrollView(VStack(16,
            PageHeader("ToggleSwitch", "A switch that toggles between two mutually exclusive states."),

            SampleCard("Basic ToggleSwitch",
                VStack(8,
                    ToggleSwitch(isOn, v => setIsOn(v)).AutomationName("Basic toggle"),
                    TextBlock($"State: {(isOn ? "On" : "Off")}").Foreground(Theme.SecondaryText)),
                sourceCode: @"
ToggleSwitch(isOn, v => setIsOn(v)).AutomationName(""Basic toggle"")
"),

            SampleCard("Custom On/Off Labels",
                ToggleSwitch(customOn, v => setCustomOn(v), "Working", "Not working"),
                sourceCode: @"
ToggleSwitch(customOn, v => setCustomOn(v), ""Working"", ""Not working"")
"),

            SampleCard("ToggleSwitch with Header",
                ToggleSwitch(headerOn, v => setHeaderOn(v)).Header("Wi-Fi"),
                sourceCode: @"
ToggleSwitch(headerOn, v => setHeaderOn(v)).Header(""Wi-Fi"")
")
        ).Margin(36, 24, 36, 36));
    }
}
