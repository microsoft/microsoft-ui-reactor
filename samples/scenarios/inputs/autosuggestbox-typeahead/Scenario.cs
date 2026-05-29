// id: autosuggestbox-typeahead
// intent: search box with typeahead suggestions
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using System;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("AutoSuggestBox Typeahead", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        var allItems = new[] { "Apple", "Apricot", "Banana", "Blueberry", "Cherry", "Grape" };
        var (query, setQuery) = UseState("");
        var matches = allItems.Where(x => x.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(5).ToArray();
        return VStack(12,
            Heading("AutoSuggestBox"),
            (AutoSuggestBox(query, setQuery) with { Header = "Fruit search", PlaceholderText = "Start typing", Suggestions = matches, IsSuggestionListOpen = query.Length > 0 && matches.Length > 0 })
                .AutomationName("Fruit search"),
            TextBlock($"Current value: {query}"))
            .Margin(16);
    }
}
