using System.Diagnostics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace ThinkingInReactor.Concepts;

// <snippet:ui-from-state>
class FavoriteButton : Component
{
    public override Element Render()
    {
        var (isFavorite, setIsFavorite) = UseState(false);

        // Render describes the UI for the current state. No code here
        // reaches into the button to change its text.
        return Button(
            isFavorite ? "Remove from favorites" : "Add to favorites",
            () => setIsFavorite(!isFavorite));
    }
}
// </snippet:ui-from-state>

static class ElementValues
{
    public static Element Build()
    {
        // <snippet:elements-are-values>
        var title = TextBlock("Products");          // a record, not a control
        var big = title.FontSize(28).Bold();        // a new record

        // `title` is unchanged. Nothing is on screen yet: the reconciler
        // creates the WinUI TextBlock only when an element is rendered.
        return VStack(8, big, title);
        // </snippet:elements-are-values>
    }
}

// <snippet:effect-side-effect>
class SearchBox : Component
{
    public override Element Render()
    {
        var (query, setQuery) = UseState("");

        // A side effect: it talks to something outside the UI tree.
        // It runs after the render, and again only when `query` changes.
        UseEffect(() => Debug.WriteLine($"Searched for '{query}'"), query);

        return TextBox(query, setQuery, placeholderText: "Search...")
            .AutomationName("Search");
    }
}
// </snippet:effect-side-effect>
