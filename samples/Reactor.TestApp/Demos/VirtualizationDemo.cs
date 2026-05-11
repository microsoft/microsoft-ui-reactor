using System.Diagnostics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

class VirtualizationDemo : Component
{
    record ItemData(int Id, string Title, string Subtitle);

    public override Element Render()
    {
        var (mode, setMode) = UseState("LazyVStack");
        var (itemCount, setItemCount) = UseState(1000);
        var (selectedIndex, setSelectedIndex) = UseState(-1);

        // Generate item data
        var items = Enumerable.Range(0, itemCount)
            .Select(i => new ItemData(i, $"Item {i}", $"Description for item {i} — this row tests virtualization"))
            .ToList();

        Element list = mode switch
        {
            "LazyVStack" => LazyVStack<ItemData>(
                items,
                item => item.Id.ToString(),
                (item, index) => Border(
                    HStack(12,
                        Border(
                            Caption($"{item.Id}")
                        ).Background(SubtleFill).CornerRadius(4).Width(48).MinHeight(32).HAlign(HorizontalAlignment.Center),
                        VStack(4,
                            TextBlock(item.Title).SemiBold(),
                            Caption(item.Subtitle).Foreground(SecondaryText)
                        )
                    )
                ).Padding(horizontal: 12, vertical: 8).Margin(0, 0, 0, 1)
            ),

            "ListView" => ListView(
                items.Select(item => (Element)Border(
                    HStack(12,
                        Border(
                            Caption($"{item.Id}")
                        ).Background(SubtleFill).CornerRadius(4).Width(48).MinHeight(32).HAlign(HorizontalAlignment.Center),
                        VStack(4,
                            TextBlock(item.Title).SemiBold(),
                            Caption(item.Subtitle).Foreground(SecondaryText)
                        )
                    )
                ).Padding(horizontal: 12, vertical: 8)).ToArray()
            )
            .Set(lv => { lv.Height = 500; lv.SelectionMode = Microsoft.UI.Xaml.Controls.ListViewSelectionMode.Single; }),

            _ => Empty()
        };

        return VStack(12,
            Heading("Virtualization Test"),
            TextBlock($"Renders {itemCount} items. If virtualization is working, scrolling should be smooth " +
                 "and only visible items should be realized in the visual tree."),

            HStack(12,
                VStack(4,
                    TextBlock("Mode:"),
                    HStack(8,
                        Button("LazyVStack", () => setMode("LazyVStack"))
                            .Disabled(mode == "LazyVStack"),
                        Button("ListView", () => setMode("ListView"))
                            .Disabled(mode == "ListView")
                    )
                ),
                VStack(4,
                    TextBlock("Items:"),
                    HStack(8,
                        Button("100", () => setItemCount(100)).Disabled(itemCount == 100),
                        Button("1000", () => setItemCount(1000)).Disabled(itemCount == 1000),
                        Button("5000", () => setItemCount(5000)).Disabled(itemCount == 5000),
                        Button("10000", () => setItemCount(10000)).Disabled(itemCount == 10000)
                    )
                )
            ),

            TextBlock($"Mode: {mode} | Items: {itemCount}").Foreground(SecondaryText),

            // The list itself
            Border(list)
                .CornerRadius(8)
                .Background(CardBackground)
                .Height(500)
        );
    }
}
