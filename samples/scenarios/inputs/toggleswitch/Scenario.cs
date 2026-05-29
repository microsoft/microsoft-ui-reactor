// id: toggleswitch
// intent: toggle switch for on/off settings
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Toggle Switch", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        var (wifiEnabled, setWifiEnabled) = UseState(true);
        return VStack(12,
            Heading("ToggleSwitch"),
            (ToggleSwitch(wifiEnabled, setWifiEnabled, "On", "Off") with { Header = "Wi-Fi" }),
            TextBlock($"Wi-Fi is {(wifiEnabled ? "On" : "Off")}"))
            .Margin(16);
    }
}
