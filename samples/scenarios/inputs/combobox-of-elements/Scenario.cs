// id: combobox-of-elements
// intent: dropdown with custom element items
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("ComboBox Elements", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        var labels = new[] { "Low", "Medium", "High" };
        var items = new Element[] { TextBlock("🟢 Low"), TextBlock("🟡 Medium"), TextBlock("🔴 High") };
        var (selectedIndex, setSelectedIndex) = UseState(0);
        return VStack(12,
            Heading("ComboBox with Elements"),
            (ComboBox(items, selectedIndex, setSelectedIndex) with { Header = "Priority" }),
            TextBlock($"Selected: {labels[selectedIndex]}"))
            .Margin(16);
    }
}
