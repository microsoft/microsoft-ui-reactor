using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace ThinkingInReactor;

// <snippet:data-model>
record Product(string Id, string Name, string Category, decimal Price, bool InStock);

static class Catalog
{
    public static readonly IReadOnlyList<Product> Products =
    [
        new("p1", "Notebook", "Paper", 4.50m, true),
        new("p2", "Sticky notes", "Paper", 2.00m, true),
        new("p3", "Printer paper", "Paper", 8.99m, false),
        new("p4", "Gel pen", "Writing", 1.50m, true),
        new("p5", "Highlighter", "Writing", 1.25m, false),
        new("p6", "Desk lamp", "Lighting", 24.00m, true),
    ];
}
// </snippet:data-model>

// <snippet:product-row>
record ProductRowProps(Product Product);

class ProductRow : Component<ProductRowProps>
{
    public override Element Render()
    {
        var p = Props.Product;

        return HStack(12,
            TextBlock(p.Name)
                .Foreground(p.InStock ? Theme.PrimaryText : Theme.SystemCritical)
                .Width(160),
            TextBlock(p.Category).Foreground(Theme.SecondaryText).Width(100),
            TextBlock($"${p.Price:0.00}"));
    }
}
// </snippet:product-row>
