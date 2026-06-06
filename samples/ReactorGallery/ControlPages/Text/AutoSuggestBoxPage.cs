using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor;

class AutoSuggestBoxPage : Component
{
    public override Element Render()
    {
        var (query, setQuery) = UseState("");
        var (submitted, setSubmitted) = UseState("");
        var allItems = new[] { "Apple", "Apricot", "Banana", "Blueberry", "Cherry", "Coconut", "Date", "Fig", "Grape" };

        return ScrollView(
            VStack(16,
                PageHeader("AutoSuggestBox", "A text input that shows filtered suggestions as the user types."),

                SampleCard("Basic AutoSuggestBox",
                    VStack(8,
                        AutoSuggestBox(query, setQuery).Width(300),
                        TextBlock($"Current text: \"{query}\"").Foreground(Theme.SecondaryText)
                    ),
                    """
                    var (query, setQuery) = UseState("");
                    AutoSuggestBox(query, setQuery)
                    """),

                SampleCard("With Query Submitted",
                    VStack(8,
                        AutoSuggestBox(query, setQuery, s => setSubmitted(s)).Width(300),
                        When(submitted != "",
                            () => TextBlock($"Submitted: \"{submitted}\"").Foreground(Theme.SystemSuccess))
                    ),
                    @"AutoSuggestBox(query, setQuery, s => setSubmitted(s))"),

                SampleCard("Filtered Results Display",
                    VStack(8,
                        AutoSuggestBox(query, setQuery).Width(300),
                        TextBlock("Matching items:").Bold(),
                        VStack(2,
                            allItems
                                .Where(i => string.IsNullOrEmpty(query) || i.Contains(query, System.StringComparison.OrdinalIgnoreCase))
                                .Select(i => TextBlock($"  • {i}").Foreground(Theme.SecondaryText))
                                .ToArray()
                        )
                    ),
                    """
                    AutoSuggestBox(query, setQuery)
                    allItems.Where(i => i.Contains(query)).Select(...)
                    """)
            ).Margin(36, 24, 36, 36)
        );
    }
}
