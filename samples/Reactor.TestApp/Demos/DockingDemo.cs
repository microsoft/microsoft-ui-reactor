using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

// ─────────────────────────────────────────────────────────────────────────
//  Spec 045 — minimal docking demo (list + detail).
//
//  Pattern:
//    • State lives in the parent (`UseState`).
//    • Layout SHAPE is in state too — captured from
//      `OnLiveLayoutChanged` so user-driven drags / drops survive a
//      re-render.
//    • Per render, the panes' Content is REBUILT fresh from the
//      current state. `RefreshContents(...)` walks the saved shape
//      and replaces each leaf's Content by Key, preserving the
//      user's tree shape while always rendering up-to-date bodies.
//    • Reset bumps a layout-key reducer; the DockManager's
//      `WithKey(...)` changes and the reconciler fully remounts,
//      clearing the host's internal `layoutOverride` and restoring
//      the initial shape.
// ─────────────────────────────────────────────────────────────────────────

class DockingDemo : Component
{
    static readonly string[] Items =
    {
        "Item Alpha", "Item Bravo", "Item Charlie", "Item Delta", "Item Echo",
        "Item Foxtrot", "Item Golf", "Item Hotel", "Item India", "Item Juliet",
    };

    // Stable Keys used both for the initial layout and for the
    // RefreshContents lookup.
    const string ItemsKey = "items";
    const string DetailKey = "detail";

    static DockNode InitialShape() =>
        new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new DockableContent[]
            {
                new DockableContent(Title: "Items", Key: ItemsKey, CanClose: true),
            }, Width: 240),
            new DockTabGroup(new DockableContent[]
            {
                new DockableContent(Title: "Detail", Key: DetailKey, CanClose: true),
            }),
        });

    /// <summary>
    /// Walks <paramref name="tree"/> and replaces the Content of each
    /// leaf whose Key appears in <paramref name="contentByKey"/>. Tree
    /// shape (splits, tab groups, ordering) is preserved verbatim — the
    /// shape comes from user-driven drag state; the Contents come from
    /// the current app state.
    /// </summary>
    static DockNode RefreshContents(DockNode tree, Dictionary<string, Element> contentByKey)
    {
        switch (tree)
        {
            case DockableContent leaf when leaf.Key is { } k && contentByKey.TryGetValue(k.ToString()!, out var fresh):
                return leaf with { Content = fresh };
            case DockTabGroup grp:
            {
                var docs = new DockableContent[grp.Documents.Count];
                for (int i = 0; i < grp.Documents.Count; i++)
                    docs[i] = (DockableContent)RefreshContents(grp.Documents[i], contentByKey);
                return grp with { Documents = docs };
            }
            case DockSplit split:
            {
                var children = new DockNode[split.Children.Count];
                for (int i = 0; i < split.Children.Count; i++)
                    children[i] = RefreshContents(split.Children[i], contentByKey);
                return split with { Children = children };
            }
            default:
                return tree;
        }
    }

    public override Element Render()
    {
        var (selected, setSelected) = UseState(0);
        var (shape, setShape) = UseState<DockNode>(InitialShape());
        // Bumping `layoutKey` flips the DockManager element's stable Key,
        // forcing a full remount — clears the host's layoutOverride so
        // Reset always lands back at the initial shape.
        var (layoutKey, bumpLayoutKey) = UseReducer(0);

        var freshContent = new Dictionary<string, Element>
        {
            [ItemsKey] = ListBox(Items, selected, setSelected).Padding(8),
            [DetailKey] = VStack(8,
                TextBlock(Items[selected]).FontSize(24).SemiBold(),
                TextBlock($"You selected entry #{selected}.").Opacity(0.7),
                TextBlock(
                    "Drag tabs to dock anywhere. Tear out to float; drag " +
                    "the floating tab back to dock it. Reset layout " +
                    "restores the two-pane shape."
                ).Opacity(0.6).TextWrapping(TextWrapping.Wrap)
            ).Padding(16),
        };

        return VStack(8,
            HStack(8,
                TextBlock("Docking — two-pane list/detail").FontSize(20).SemiBold(),
                Button("Reset layout", () =>
                {
                    setSelected(0);
                    setShape(InitialShape());
                    bumpLayoutKey(k => k + 1);
                })
            ).Margin(0, 0, 0, 8),

            new DockManager
            {
                Layout = RefreshContents(shape, freshContent),
                // Capture user-driven layout changes so drags / drops
                // persist across re-renders.
                OnLiveLayoutChanged = newLayout => { if (newLayout is not null) setShape(newLayout); },
            }.WithKey($"dock-{layoutKey}").Flex(grow: 1)
        );
    }
}
