using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

/// <summary>
/// A treemap where each rectangle contains a Reactor ListView showing the
/// child items for that category. D3's squarify layout sizes categories;
/// standard Reactor controls provide scrollable drill-down inside each cell.
/// </summary>
public sealed class NestedListExplorerSample : GallerySample
{
    public override string Title => "Nested List Explorer";
    public override string Description =>
        "A treemap where each rectangle hosts a ListView showing child items. " +
        "D3's squarify layout sizes categories; Reactor controls provide scrollable drill-down.";
    public override string Category => "Controls";

    public override string SourceCode => """
        var treemap = TreemapLayout.Create<CatalogItem>()
            .Size(W, H).SetPadding(4).SetPaddingInner(4);
        var root = treemap.Hierarchy(data, n => n.Subs, n => n.Size);
        treemap.Layout(root);

        return D3Canvas(W, H,
            [.. root.Children.Select((folder, ci) =>
                Border(VStack(4,
                    TextBlock(folder.Data.Name).SemiBold().Foreground(color),
                    ListView(folder.Leaves().Select(leaf =>
                        HStack(8, TextBlock(leaf.Data.Name), TextBlock($"${leaf.Data.Size}"))
                    ).ToArray())
                )) with { CornerRadius = 6, BorderBrush = color, ... }
                  .Size(folder.Width, folder.Height)
                  .Canvas(folder.X0, folder.Y0)
            )]
        )
            .AutomationName("Nested List Explorer")
            .FullDescription("Treemap of 4 product categories with scrollable list views showing items and prices.");
        """;

    record CatalogItem(string Name, double Size = 0, CatalogItem[]? Subs = null);

    public override Element Render()
    {
        const double W = 700, H = 460;

        var data = new CatalogItem("Store", 0, [
            new("Electronics", 0, [
                new("Laptop", 320),
                new("Phone", 280),
                new("Tablet", 190),
                new("Headphones", 80),
                new("Monitor", 250),
                new("Keyboard", 45),
                new("Mouse", 30),
            ]),
            new("Clothing", 0, [
                new("Jacket", 120),
                new("Jeans", 85),
                new("T-Shirt", 40),
                new("Sneakers", 110),
                new("Hat", 25),
            ]),
            new("Books", 0, [
                new("Fiction", 60),
                new("Science", 55),
                new("History", 45),
                new("Art", 35),
            ]),
            new("Home", 0, [
                new("Sofa", 400),
                new("Table", 200),
                new("Lamp", 60),
                new("Rug", 90),
                new("Shelf", 75),
                new("Mirror", 50),
            ]),
        ]);

        var treemap = TreemapLayout.Create<CatalogItem>()
            .Size(W, H)
            .SetPadding(4)
            .SetPaddingInner(4);
        var root = treemap.Hierarchy(data, n => n.Subs, n => n.Size);
        treemap.Layout(root);

        return D3Canvas(W, H,
        [
            .. root.Children
                .Select((folder, ci) => folder)
                .Where(folder => folder.Width >= 10 && folder.Height >= 10)
                .Select((folder, ci) =>
                {
                    var color = Brush(Palette[ci % Palette.Count]);
                    var bgColor = Brush(Palette[ci % Palette.Count], opacity: 0.06);
                    var dimBrush = ChartAxis;

                    var header = (TextBlock(folder.Data.Name) with { FontSize = 12 })
                        .SemiBold().Foreground(color).Margin(8, 6, 8, 2);

                    var items = folder.Leaves().Select(leaf =>
                        (Element)HStack(8,
                            (TextBlock(leaf.Data.Name) with { FontSize = 11 }).VAlign(VerticalAlignment.Center),
                            (TextBlock($"${leaf.Data.Size:N0}") with { FontSize = 10 })
                                .Foreground(dimBrush).VAlign(VerticalAlignment.Center)
                        )
                    ).ToArray();

                    return (Border(
                        VStack(0, header, ListView(items).Margin(4, 0, 4, 4))
                    ) with
                    {
                        CornerRadius = 6,
                        BorderBrush = color,
                        BorderThickness = 1.5,
                        Background = bgColor,
                    })
                    .Size(folder.Width, folder.Height)
                    .Canvas(folder.X0, folder.Y0);
                }),
        ])
            .AutomationName("Nested List Explorer")
            .FullDescription("Treemap of 4 product categories with scrollable list views showing items and prices.");
    }
}
