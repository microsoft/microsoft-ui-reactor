// id: togglebutton-basic
// intent: toggle button with checked state
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("ToggleButtonBasic", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        var (isOn, setIsOn) = UseState(false);
        return VStack(12,
            ToggleButton("Notifications", isOn, setIsOn),
            TextBlock(isOn ? "Notifications are on" : "Notifications are off")
                .Foreground(Theme.SecondaryText))
            .Padding(24);
    }
}