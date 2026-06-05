using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

var commands = new[]
{
    "Open Settings",
    "New File",
    "Search Documentation",
    "Toggle Dark Mode",
    "Show Keyboard Shortcuts",
};

ReactorApp.Run(_ =>
{
    ReactorApp.OpenWindow(new WindowSpec
    {
        Style = WindowStyle.None,
        IsMovableByBackground = true,
        Level = WindowLevel.AlwaysOnTop,
        ShowInTaskbar = false,
        ShowInSwitcher = false,
        StartPosition = WindowStartPosition.CenterOnCurrent,
        CornerStyle = WindowCornerStyle.Rounded,
    }, () => new PaletteApp(commands));
});

internal sealed class PaletteApp(IReadOnlyList<string> commands) : Component
{
    public override Element Render()
    {
        var (query, setQuery) = UseState("");
        var filtered = commands
            .Where(c => query.Length == 0 || c.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .Select(c => TextBlock(c).Padding(12).Background("#1AFFFFFF").CornerRadius(6))
            .Cast<Element>()
            .ToArray();

        return Border(
            VStack(10,
                TextBlock("Command Palette").FontSize(20).Bold(),
                TextBox(query, setQuery, placeholderText: "Type a command…"),
                VStack(6, filtered)))
            .Padding(20)
            .Background("#F01F1F1F")
            .CornerRadius(12);
    }
}
