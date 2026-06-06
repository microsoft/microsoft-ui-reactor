using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor;

class ItemsViewPage : Component
{
    record Product(string Name, string Category, double Price);

    public override Element Render()
    {
        var products = new Product[]
        {
            new("Laptop", "Electronics", 999.99),
            new("Headphones", "Electronics", 149.99),
            new("Notebook", "Office", 4.99),
            new("Pen Set", "Office", 12.99),
            new("Backpack", "Travel", 59.99),
            new("Water Bottle", "Travel", 19.99),
        }.ToList().AsReadOnly();

        return ScrollView(
            VStack(16,
                PageHeader("ItemsView", "A flexible, data-driven control for displaying collections."),

                SampleCard("Basic ItemsView",
                    ItemsView(
                        products,
                        p => p.Name,
                        (p, i) => Border(
                            VStack(4,
                                TextBlock(p.Name).Bold(),
                                TextBlock(p.Category).ApplyStyle("CaptionTextBlockStyle").Foreground(Theme.SecondaryText),
                                TextBlock($"${p.Price:F2}").Foreground(Theme.SystemSuccess)
                            ).Padding(12)
                        ).Background(Theme.SubtleFill).CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft).Margin(4)
                    ).Height(300),
                    """
                    ItemsView(
                        products,
                        p => p.Name,
                        (p, i) => Border(VStack(
                            TextBlock(p.Name).Bold(),
                            TextBlock($"${p.Price:F2}")
                        ))
                    )
                    """),

                SampleCard("Compact ItemsView",
                    ItemsView(
                        products,
                        p => p.Name,
                        (p, i) => HStack(8,
                            TextBlock($"{i + 1}.").Width(20).Foreground(Theme.SecondaryText),
                            TextBlock(p.Name).Flex(grow: 1),
                            TextBlock($"${p.Price:F2}").Foreground(Theme.AccentText)
                        ).Padding(8)
                    ).Height(250),
                    """
                    ItemsView(
                        products, p => p.Name,
                        (p, i) => HStack(8, TextBlock(p.Name).Flex(grow: 1), TextBlock(price))
                    )
                    """)
            ).Margin(36, 24, 36, 36)
        );
    }
}
