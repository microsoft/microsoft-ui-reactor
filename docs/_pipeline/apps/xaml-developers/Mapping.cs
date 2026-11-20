using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// XAML-to-Reactor mapping examples for the "Coming from XAML" page. They compile
// as part of this doc app, so CI fails if the DataTemplate, Style, theme-resource
// or visual-state replacement shapes drift from the real API.

record Product(int Id, string Name, string Category);

class DataTemplateExample : Component
{
    static readonly Product[] Catalog =
    [
        new(1, "Contoso Keyboard", "Peripherals"),
        new(2, "Fabrikam Monitor", "Displays"),
        new(3, "Northwind Mouse", "Peripherals"),
    ];

    public override Element Render() => ProductList(Catalog);

    // <snippet:datatemplate-foreach>
    // The "template" is an ordinary method that returns an Element.
    // ForEach calls it once for each item, on every render.
    static Element ProductList(IReadOnlyList<Product> items) =>
        VStack(8, ForEach(items, item => Card(item).WithKey(item.Id.ToString())));

    static Element Card(Product item) =>
        CardSurface(VStack(4,
            TextBlock(item.Name).FontSize(16).SemiBold(),
            TextBlock(item.Category).Foreground(Theme.SecondaryText)));
    // </snippet:datatemplate-foreach>

    // <snippet:style-as-composition>
    // A shared "style" is a plain method that wraps a child and applies
    // modifiers. There is no registry and no TargetType check.
    static Element CardSurface(Element child) =>
        Border(child)
            .Background(Theme.CardBackground)
            .CornerRadius(8)
            .Padding(16);
    // </snippet:style-as-composition>
}

// <snippet:theme-resource>
class ThemeResourceExample : Component
{
    public override Element Render() =>
        VStack(12,
            // Named tokens for common {ThemeResource} brushes.
            Border(VStack(4,
                    TextBlock("Storage").SemiBold(),
                    TextBlock("12 GB of 50 GB used").Foreground(Theme.SecondaryText),
                    TextBlock("Last synced 5 minutes ago").Foreground(Theme.TertiaryText)))
                .Background(Theme.CardBackground)
                .BorderBrush(Theme.CardStroke)
                .BorderThickness(1)
                .CornerRadius(8)
                .Padding(16),
            // Any other theme resource, by its key.
            Border(TextBlock("Sync is paused"))
                .BorderBrush(Theme.Ref("ControlStrongStrokeColorDefaultBrush"))
                .BorderThickness(1)
                .CornerRadius(8)
                .Padding(12),
            // A resource override for one control and its children.
            Button("Delete", () => { })
                .Resources(r => r
                    .Set("ButtonBackground", Theme.SystemCritical)
                    .Set("ButtonForeground", "#FFFFFF")));
}
// </snippet:theme-resource>

// <snippet:visual-states>
enum LoadState { Loading, Loaded, Failed }

class VisualStatesExample : Component
{
    public override Element Render()
    {
        var (state, setState) = UseState(LoadState.Loading);

        // Each visual state is a branch in Render, not a named VisualState.
        Element status = state switch
        {
            LoadState.Loading => ProgressRing(),
            LoadState.Failed => TextBlock("Could not load the orders.")
                .Foreground(Theme.SystemCritical),
            _ => TextBlock("12 orders loaded."),
        };

        return VStack(12,
            status,
            HStack(8,
                Button("Loading", () => setState(LoadState.Loading)),
                Button("Loaded", () => setState(LoadState.Loaded)),
                Button("Failed", () => setState(LoadState.Failed))),
            // Pointer states run on the compositor, with no re-render.
            Border(TextBlock("Hover or press me"))
                .Padding(12).CornerRadius(8).Background(Theme.CardBackground)
                .InteractionStates(s => s.PointerOver(scale: 1.03f).Pressed(scale: 0.97f)));
    }
}
// </snippet:visual-states>
