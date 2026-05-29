// id: use-context-basic
// intent: provide a value at the root and consume it from a descendant
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// Context removes prop-drilling when several descendants need the same value.
ReactorApp.Run<App>("UseContextBasic", width: 400, height: 200);

class App : Component
{
    internal static readonly Context<string> ThemeContext = new("Light");

    public override Element Render()
    {
        var (theme, setTheme) = UseState("Light");

        return VStack(12,
            Button(theme == "Light" ? "Switch to dark" : "Switch to light",
                () => setTheme(theme == "Light" ? "Dark" : "Light")).AutomationName("Toggle theme"),
            Component<ThemeBadge>())
            .Provide(ThemeContext, theme);
    }

    class ThemeBadge : Component
    {
        public override Element Render()
        {
            var theme = UseContext(ThemeContext);
            return TextBlock($"Current theme from context: {theme}");
        }
    }
}
