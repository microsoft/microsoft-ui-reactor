using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Layout;

class DockingPage : Component
{
    static readonly string[] Items =
    {
        "Item Alpha", "Item Bravo", "Item Charlie", "Item Delta", "Item Echo", "Item Foxtrot",
    };

    public override Element Render()
    {
        var (selected, setSelected) = UseState(0);
        var (layoutKey, bumpLayoutKey) = UseReducer(0);

        var dock = new DockManager
        {
            Layout = new DockSplit(Orientation.Horizontal,
            [
                new DockTabGroup(
                [
                    new DockableContent(
                        Title: "Items",
                        Content: ListBox(Items, selected, setSelected).Padding(8),
                        Key: "items",
                        CanClose: true),
                ], Width: 200),
                new DockTabGroup(
                [
                    new DockableContent(
                        Title: "Detail",
                        Content: VStack(8,
                            TextBlock(Items[selected]).FontSize(22).SemiBold(),
                            TextBlock($"You selected entry #{selected + 1}.").Foreground(Theme.SecondaryText),
                            TextBlock("Drag tabs to re-dock, or tear one out to float it in its own window.")
                                .Foreground(Theme.SecondaryText)
                                .Set(tb => tb.TextWrapping = TextWrapping.Wrap)
                        ).Padding(16),
                        Key: "detail",
                        CanClose: true),
                ]),
            ]),
        }.WithKey($"dock-{layoutKey}").Height(360);

        return ScrollView(VStack(16,
            PageHeader("Docking", "Dockable, tearable, floatable panes with drag-to-rearrange layout (spec 045)."),

            SampleCard("Two-pane list / detail",
                VStack(8,
                    dock,
                    Button("Reset layout", () => { setSelected(0); bumpLayoutKey(k => k + 1); })),
                sourceCode: @"
new DockManager
{
    Layout = new DockSplit(Orientation.Horizontal,
    [
        new DockTabGroup([ new DockableContent(
            Title: ""Items"", Content: ListBox(Items, selected, setSelected), Key: ""items"", CanClose: true) ], Width: 200),
        new DockTabGroup([ new DockableContent(
            Title: ""Detail"", Content: detailView, Key: ""detail"", CanClose: true) ]),
    ]),
}.WithKey($""dock-{layoutKey}"").Height(360)
// Re-key the DockManager to reset the user's drag-modified layout.")
        ).Margin(36, 24, 36, 36));
    }
}
