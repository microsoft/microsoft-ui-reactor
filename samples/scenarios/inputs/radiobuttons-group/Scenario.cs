// id: radiobuttons-group
// intent: radio button group for single selection
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("RadioButtons Group", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        var items = new[] { "System", "Light", "Dark" };
        var (selectedIndex, setSelectedIndex) = UseState(0);
        return VStack(12,
            Heading("RadioButtons"),
            RadioButtons(items, selectedIndex, setSelectedIndex) with { Header = "Theme" },
            TextBlock($"Selected: {items[selectedIndex]}"))
            .Margin(16);
    }
}
