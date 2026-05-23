using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<SearchRecipeApp>("Search-with-Suggestions Recipe", width: 460, height: 380
#if DEBUG
    , preview: true
#endif
);

class SearchRecipeApp : Component
{
    public override Element Render() => Component<SearchBox>();
}

// <snippet:catalog>
class SearchBox : Component
{
    private static readonly string[] Catalog = new[] {
        "Account settings", "Accessibility", "Animation",
        "Buttons", "Backdrop", "Charts",
        "Components", "Commanding", "Context",
        "Effects", "Forms", "Hooks",
        "Localization", "Navigation", "Persistence",
        "Styling", "Testing", "Theming tokens",
    };
// </snippet:catalog>

    public override Element Render()
    {
        // <snippet:filter>
        // UseMemo on the dependency array means the filter runs only when
        // query changes — typing fast doesn't refilter mid-keystroke, and
        // a re-render that didn't touch the query is free.
        var (query, setQuery) = UseState("");
        var suggestions = UseMemo(
            () => string.IsNullOrWhiteSpace(query)
                ? new string[0]
                : Catalog.Where(c => c.Contains(query,
                    System.StringComparison.OrdinalIgnoreCase))
                    .Take(5).ToArray(),
            query);
        // </snippet:filter>

        // <snippet:render>
        return VStack(8,
            TextBox(query, setQuery, placeholder: "Search topics…").Width(300),
            suggestions.Length == 0
                ? Empty()
                : Border(
                    VStack(2,
                        suggestions.Select(s =>
                            TextBlock(s).Padding(8)).ToArray()
                    ).Background("#FFFFFF")
                ).WithBorder("#E0E0E0").Width(300)
        ).Padding(20);
        // </snippet:render>
    }
}
