using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// Doc app for `reactor-vs-xaml.md` — the XAML-to-Reactor mapping examples that
// the page previously carried as uncompiled prose blocks. Keeping them here
// means CI fails if the DataTemplate-replacement or Style-replacement shapes
// ever drift from the real factory / modifier surface.
ReactorApp.Run<ReactorVsXamlApp>("Reactor vs XAML", width: 640, height: 520);

record Product(int Id, string Name, string Category);

class ReactorVsXamlApp : Component
{
    static readonly Product[] Catalog =
    [
        new(1, "Contoso Keyboard", "Peripherals"),
        new(2, "Fabrikam Monitor", "Displays"),
        new(3, "Northwind Mouse", "Peripherals"),
    ];

    public override Element Render() =>
        ScrollView(ProductList(Catalog)).Padding(16);

    // <snippet:datatemplate-foreach>
    // A XAML DataTemplate is a recipe the framework instantiates per item.
    // In Reactor the recipe is just a closure — `Card` is an ordinary method
    // returning an Element, and `ForEach` runs it once per item per render.
    static Element ProductList(IReadOnlyList<Product> items) =>
        VStack(8, ForEach(items, item => Card(item).WithKey(item.Id.ToString())));

    static Element Card(Product item) =>
        CardSurface(VStack(4,
            TextBlock(item.Name).FontSize(16).SemiBold(),
            TextBlock(item.Category).Foreground(Theme.SecondaryText)));
    // </snippet:datatemplate-foreach>

    // <snippet:style-as-composition>
    // A XAML `Style` is a keyed bag of setters matched by TargetType. The
    // Reactor analogue is a plain method that composes a wrapper element — no
    // static registration, no runtime TargetType check, just composition.
    static Element CardSurface(Element child) =>
        Border(child)
            .Background(Theme.CardBackground)
            .CornerRadius(8)
            .Padding(16);
    // </snippet:style-as-composition>
}
