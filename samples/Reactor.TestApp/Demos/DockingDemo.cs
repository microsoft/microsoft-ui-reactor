using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

// ─────────────────────────────────────────────────────────────────────────
//  Spec 045 — minimal docking demo (list + detail).
//
//  Shows how little code it takes to set up a Reactor docking window
//  system. The contract is just three things:
//    1. A `DockNode` tree describing the initial layout.
//    2. A `DockManager` element that hosts the tree.
//    3. (Optional) round-trip the host's effective layout back into
//       app state via `OnLiveLayoutChanged` so the user's drags + tab
//       closes survive a re-render, and a "Reset" can restore the
//       starting shape.
//
//  Cross-pane state is wired through a static `Observable<int>` —
//  Reactor's idiomatic "shared cell" for inter-component data. Each
//  pane subscribes via `UseObservable` and re-renders when the
//  selected index changes; the panes themselves don't have to know
//  about each other.
// ─────────────────────────────────────────────────────────────────────────

class DockingDemo : Component
{
    static readonly string[] Items =
    {
        "Item Alpha", "Item Bravo", "Item Charlie", "Item Delta", "Item Echo",
        "Item Foxtrot", "Item Golf", "Item Hotel", "Item India", "Item Juliet",
    };

    // Shared selection. Both panes read/write this; re-renders are
    // automatic because each pane is wrapped in a Memo(ctx => ...)
    // closure that calls UseObservable.
    static readonly Observable<int> Selected = new(0);

    static DockNode InitialLayout() =>
        new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockableContent(
                Title: "Items",
                Content: Memo(ctx =>
                {
                    var idx = ctx.UseObservable(Selected).Value;
                    return ListBox(Items, idx, i => Selected.Value = i)
                        .Padding(8);
                }),
                Key: "items",
                CanClose: true,
                Width: 200),
            new DockableContent(
                Title: "Detail",
                Content: Memo(ctx =>
                {
                    var idx = ctx.UseObservable(Selected).Value;
                    return VStack(8,
                        TextBlock(Items[idx]).FontSize(24).SemiBold(),
                        TextBlock($"You selected entry #{idx}.").Opacity(0.7),
                        TextBlock("Drag tabs around to dock them anywhere. " +
                                  "Tear a tab out to float it; drag the floating tab back to dock it. " +
                                  "Hit Reset layout to restore the two-pane shape."
                                 ).Opacity(0.6).TextWrapping(TextWrapping.Wrap)
                    ).Padding(16);
                }),
                Key: "detail",
                CanClose: true),
        });

    public override Element Render()
    {
        // The host's effective layout flows back to state via
        // OnLiveLayoutChanged so user-driven changes (drag/drop, tab
        // close, splitter resize) survive a re-render. Reset replaces
        // the state with a fresh InitialLayout().
        var (layout, setLayout) = UseState<DockNode?>(InitialLayout());

        return VStack(8,
            HStack(8,
                TextBlock("Docking — two-pane list/detail").FontSize(20).SemiBold(),
                Button("Reset layout", () => setLayout(InitialLayout()))
            ).Margin(0, 0, 0, 8),

            new DockManager
            {
                Layout = layout,
                OnLiveLayoutChanged = newLayout => setLayout(newLayout),
            }.Flex(grow: 1)
        );
    }
}
