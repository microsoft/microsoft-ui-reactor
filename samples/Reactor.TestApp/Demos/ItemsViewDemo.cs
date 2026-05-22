using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;
using WinUI = Microsoft.UI.Xaml.Controls;

class ItemsViewDemo : Component
{
    record Card(int Id, string Title, string Body, Color Tint);

    static readonly Color[] Palette =
    [
        Color.FromArgb(255, 0x42, 0x6E, 0xB4),
        Color.FromArgb(255, 0x5B, 0xA8, 0x4D),
        Color.FromArgb(255, 0xC9, 0x4F, 0x4F),
        Color.FromArgb(255, 0xE0, 0x9B, 0x2C),
        Color.FromArgb(255, 0x8A, 0x4E, 0xA5),
        Color.FromArgb(255, 0x2E, 0x9E, 0xA5),
    ];

    // Cache one SolidColorBrush per palette color (plus white for the
    // chip label) so re-renders don't churn fresh brush instances into
    // the viewBuilder output. Without this, ShallowEquals on the inner
    // Border falls back to per-render BrushesEqual color comparisons —
    // correct, but UpdateBorder still rewrites the Background property
    // every reconcile, which is what the reconciler-highlight overlay
    // flags as a per-item flash on every selection change.
    static readonly SolidColorBrush[] PaletteBrushes = Palette
        .Select(c => new SolidColorBrush(c)).ToArray();
    static readonly SolidColorBrush WhiteBrush = new(Microsoft.UI.Colors.White);

    public override Element Render()
    {
        var (layoutKind, setLayout) = UseState(ItemsViewLayoutKind.UniformGridLayout);
        var (selectionMode, setSelectionMode) = UseState(WinUI.ItemsViewSelectionMode.Single);
        var (itemCount, setItemCount) = UseState(60);
        var (lastInvoked, setLastInvoked) = UseState("(none)");
        var (lastSelection, setLastSelection) = UseState(global::System.Array.Empty<int>());

        var (items, updateItems) = UseReducer(Enumerable.Range(0, itemCount)
            .Select(i => new Card(
                i,
                $"Card {i}",
                $"This is item #{i}. Try switching layouts and selection modes.",
                Palette[i % Palette.Length]))
            .ToList());

        Element BuildItem(Card c, int index) =>
            ItemContainer(
                VStack(6,
                    Border(
                        TextBlock($"#{c.Id}").Bold().Foreground(WhiteBrush)
                    ).Background(PaletteBrushes[c.Id % PaletteBrushes.Length])
                     .CornerRadius(4).Padding(horizontal: 8, vertical: 4),
                    TextBlock(c.Title).SemiBold(),
                    Caption(c.Body).Foreground(SecondaryText)
                ).Padding(8)
            );

        return VStack(12,
            Heading("ItemsView"),
            TextBlock("Modern virtualized collection control: choose a layout, " +
                "selection mode, and item count. The same Reactor element factory " +
                "powers LazyVStack — here it feeds WinUI's ItemsView via " +
                "an ItemContainer-wrapped viewBuilder."),

            HStack(16,
                VStack(4,
                    TextBlock("Layout:"),
                    HStack(8,
                        Button("Stack", () => setLayout(ItemsViewLayoutKind.StackLayout))
                            .IsEnabled(layoutKind != ItemsViewLayoutKind.StackLayout),
                        Button("UniformGrid", () => setLayout(ItemsViewLayoutKind.UniformGridLayout))
                            .IsEnabled(layoutKind != ItemsViewLayoutKind.UniformGridLayout),
                        Button("LinedFlow", () => setLayout(ItemsViewLayoutKind.LinedFlowLayout))
                            .IsEnabled(layoutKind != ItemsViewLayoutKind.LinedFlowLayout)
                    )
                ),
                VStack(4,
                    TextBlock("Selection:"),
                    HStack(8,
                        Button("None", () => setSelectionMode(WinUI.ItemsViewSelectionMode.None))
                            .IsEnabled(selectionMode != WinUI.ItemsViewSelectionMode.None),
                        Button("Single", () => setSelectionMode(WinUI.ItemsViewSelectionMode.Single))
                            .IsEnabled(selectionMode != WinUI.ItemsViewSelectionMode.Single),
                        Button("Multiple", () => setSelectionMode(WinUI.ItemsViewSelectionMode.Multiple))
                            .IsEnabled(selectionMode != WinUI.ItemsViewSelectionMode.Multiple)
                    )
                ),
                VStack(4,
                    TextBlock("Items:"),
                    HStack(8,
                        Button("30", () => setItemCount(30)).IsEnabled(itemCount != 30),
                        Button("60", () => setItemCount(60)).IsEnabled(itemCount != 60),
                        Button("300", () => setItemCount(300)).IsEnabled(itemCount != 300),
                        Button("2000", () => setItemCount(2000)).IsEnabled(itemCount != 2000)
                    )
                )
            ),

            HStack(16,
                TextBlock($"Last invoked: {lastInvoked}").Foreground(SecondaryText),
                TextBlock($"Selected: {(lastSelection.Length == 0 ? "(none)" : string.Join(", ", lastSelection))}")
                    .Foreground(SecondaryText)
            ),

            Border(
                (ItemsView(items, c => c.Id.ToString(), BuildItem)
                    with
                    {
                        LayoutKind = layoutKind,
                        SelectionMode = selectionMode,
                        IsItemInvokedEnabled = true,
                    })
                    .ItemInvoked(c => setLastInvoked($"#{c.Id} ({c.Title})"))
                    .SelectionChanged(picked =>
                        setLastSelection(picked.Select(p => p.Id).ToArray()))
            )
                .CornerRadius(8)
                .Background(CardBackground)
                .Height(500)
        );
    }
}
