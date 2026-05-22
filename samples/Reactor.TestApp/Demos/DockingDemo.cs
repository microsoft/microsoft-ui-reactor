using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

// ─────────────────────────────────────────────────────────────────────────
//  Spec 045 — minimal docking demo (list + detail).
//
//  Idiomatic Reactor: declare the full tree in Render(), let state flow
//  naturally. The host owns the user's drag-modified SHAPE internally
//  (shape-only `layoutOverride`, spec §2.30); the app owns the
//  CONTENT (Title, Content, CanClose, etc.) via `manager.Layout`. The
//  host resolves the effective layout per render by matching shape
//  leaves to the app's content by Key.
//
//  Reset uses `.WithKey(...)` on the DockManager to force a remount —
//  this clears the host's internal override and the tree starts fresh.
// ─────────────────────────────────────────────────────────────────────────

class DockingDemo : Component
{
    static readonly string[] Items =
    {
        "Item Alpha", "Item Bravo", "Item Charlie", "Item Delta", "Item Echo",
        "Item Foxtrot", "Item Golf", "Item Hotel", "Item India", "Item Juliet",
    };

    public override Element Render()
    {
        var (selected, setSelected) = UseState(0);
        var (layoutKey, bumpLayoutKey) = UseReducer(0);

        return VStack(8,
            HStack(8,
                TextBlock("Docking — two-pane list/detail").FontSize(20).SemiBold(),
                Button("Reset layout", () =>
                {
                    setSelected(0);
                    bumpLayoutKey(k => k + 1);
                })
            ).Margin(0, 0, 0, 8),

            new DockManager
            {
                Layout = new DockSplit(Orientation.Horizontal, [
                    new DockTabGroup([
                        new DockableContent(
                            Title: "Items",
                            Content: ListBox(Items, selected, setSelected).Padding(8),
                            Key: "items",
                            CanClose: true),
                    ], Width: 240),
                    new DockTabGroup([
                        new DockableContent(
                            Title: "Detail",
                            Content: VStack(8,
                                TextBlock(Items[selected]).FontSize(24).SemiBold(),
                                TextBlock($"You selected entry #{selected}.").Opacity(0.7),
                                TextBlock(
                                    "Drag tabs to dock anywhere. Tear out to float; " +
                                    "drag the floating tab back to dock it. Reset layout " +
                                    "restores the two-pane shape."
                                ).Opacity(0.6).TextWrapping(TextWrapping.Wrap)
                            ).Padding(16),
                            Key: "detail",
                            CanClose: true),
                    ]),
                ]),
            }.WithKey($"dock-{layoutKey}").Flex(grow: 1)
        );
    }
}
