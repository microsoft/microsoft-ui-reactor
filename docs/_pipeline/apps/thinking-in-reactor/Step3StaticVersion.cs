using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace ThinkingInReactor.StaticVersion;

// <snippet:static-children>
record SearchBarProps(string FilterText, bool InStockOnly);

class SearchBar : Component<SearchBarProps>
{
    public override Element Render() =>
        VStack(8,
            TextBox(Props.FilterText, placeholderText: "Search...")
                .AutomationName("Search products"),
            CheckBox(Props.InStockOnly, label: "Only show products in stock"));
}

record ProductListProps(IReadOnlyList<Product> Products);

class ProductList : Component<ProductListProps>
{
    public override Element Render() =>
        VStack(4,
            ForEach(Props.Products, p =>
                Component<ProductRow, ProductRowProps>(new(p)).WithKey(p.Id)));
}
// </snippet:static-children>

// <snippet:static-root>
class ProductCatalog : Component
{
    public override Element Render() =>
        VStack(16,
            Component<SearchBar, SearchBarProps>(new("", false)),
            Component<ProductList, ProductListProps>(new(Catalog.Products))
        ).Padding(24);
}
// </snippet:static-root>
