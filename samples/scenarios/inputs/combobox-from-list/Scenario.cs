// id: combobox-from-list
// intent: dropdown picker from a string array
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("ComboBox List", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        var cities = new[] { "Seattle", "London", "Tokyo", "Sydney" };
        var (selectedIndex, setSelectedIndex) = UseState(1);
        return VStack(12,
            Heading("ComboBox"),
            (ComboBox(cities, selectedIndex, setSelectedIndex) with { Header = "Office", PlaceholderText = "Choose a city" }),
            TextBlock($"Selected: {cities[selectedIndex]}"))
            .Margin(16);
    }
}
