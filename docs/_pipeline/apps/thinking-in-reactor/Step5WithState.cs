using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using ThinkingInReactor.StaticVersion;

namespace ThinkingInReactor.WithState;

// <snippet:derived-list>
record ProductListProps(IReadOnlyList<Product> Products, string FilterText, bool InStockOnly);

class ProductList : Component<ProductListProps>
{
    public override Element Render()
    {
        // Derived data: computed from props on every render, never stored.
        var visible = Props.Products
            .Where(p => p.Name.Contains(Props.FilterText, StringComparison.OrdinalIgnoreCase))
            .Where(p => !Props.InStockOnly || p.InStock)
            .ToList();

        return VStack(4,
            visible.Count == 0
                ? TextBlock("No products match.").Foreground(Theme.SecondaryText)
                : ForEach(visible, p =>
                    Component<ProductRow, ProductRowProps>(new(p)).WithKey(p.Id)));
    }
}
// </snippet:derived-list>

// <snippet:state-owner>
class ProductCatalog : Component
{
    public override Element Render()
    {
        // The minimal state: two values the user can change.
        var (filterText, setFilterText) = UseState("");
        var (inStockOnly, setInStockOnly) = UseState(false);

        return VStack(16,
            Component<SearchBar, SearchBarProps>(new(filterText, inStockOnly)),
            Component<ProductList, ProductListProps>(
                new(Catalog.Products, filterText, inStockOnly))
        ).Padding(24);
    }
}
// </snippet:state-owner>
