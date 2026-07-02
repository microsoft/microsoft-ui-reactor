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
                // Issue #327 (Option A): opt into cross-container row memoization. On a fast
                // scroll over these variable-height rows the ItemsRepeater recycles containers
                // constantly; without the memo every recycle rebuilds this whole Border/HStack
                // tree and the reconciler diffs it. Memo(key, factory) caches the inner element
                // per key in the ElementFactory's bounded LRU, so a recycle that re-asks for a
                // key still in the window returns the SAME instance → Element.ShallowEquals'
                // ReferenceEquals fast-path fires and the per-row reconcile is skipped.
                //
                // Purity contract: the key MUST capture every input the factory reads. Here the
                // row is a pure function of `item`, and `item` is fully determined by `item.Id`
                // (Title/Subtitle are derived from it), so the int Id is a complete key. If this
                // row closed over state NOT derived from the key (e.g. a selection flag or theme),
                // fold it into the key to avoid staleness, e.g. Memo((item.Id, isSelected), …).
                (item, index) => Memo(item.Id, () => Border(
                    HStack(12,
                        Border(
                            Caption($"{item.Id}")
                        ).Background(SubtleFill).CornerRadius(4).Width(48).MinHeight(32).HAlign(HorizontalAlignment.Center),
                        VStack(4,
                            TextBlock(item.Title).SemiBold(),
                            Caption(item.Subtitle).Foreground(SecondaryText)
                        )
                    )
                ).Padding(horizontal: 12, vertical: 8).Margin(0, 0, 0, 1))
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
                            .IsEnabled(!(mode == "LazyVStack")),
                        Button("ListView", () => setMode("ListView"))
                            .IsEnabled(!(mode == "ListView"))
                    )
                ),
                VStack(4,
                    TextBlock("Items:"),
                    HStack(8,
                        Button("100", () => setItemCount(100)).IsEnabled(!(itemCount == 100)),
                        Button("1000", () => setItemCount(1000)).IsEnabled(!(itemCount == 1000)),
                        Button("5000", () => setItemCount(5000)).IsEnabled(!(itemCount == 5000)),
                        Button("10000", () => setItemCount(10000)).IsEnabled(!(itemCount == 10000))
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
