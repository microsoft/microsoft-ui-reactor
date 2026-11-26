using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using ThinkingInReactor.WithState;

namespace ThinkingInReactor.Final;

// <snippet:inverse-search-bar>
record SearchBarProps(
    string FilterText,
    bool InStockOnly,
    Action<string> OnFilterTextChange,
    Action<bool> OnInStockOnlyChange);

class SearchBar : Component<SearchBarProps>
{
    public override Element Render() =>
        VStack(8,
            TextBox(Props.FilterText, Props.OnFilterTextChange, placeholderText: "Search...")
                .AutomationName("Search products"),
            CheckBox(Props.InStockOnly, Props.OnInStockOnlyChange,
                label: "Only show products in stock"));
}
// </snippet:inverse-search-bar>

// <snippet:inverse-root>
class ProductCatalog : Component
{
    public override Element Render()
    {
        var (filterText, setFilterText) = UseState("");
        var (inStockOnly, setInStockOnly) = UseState(false);

        return VStack(16,
            // The setters go down as callbacks. The child calls them to ask
            // the parent to change its state.
            Component<SearchBar, SearchBarProps>(
                new(filterText, inStockOnly, setFilterText, setInStockOnly)),
            Component<ProductList, ProductListProps>(
                new(Catalog.Products, filterText, inStockOnly))
        ).Padding(24);
    }
}
// </snippet:inverse-root>
