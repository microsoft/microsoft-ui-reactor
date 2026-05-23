using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// End-to-end coverage for the <see cref="ItemsViewElement{T}"/> reconciler
/// path. Each fixture mounts a real <see cref="WinUI.ItemsView"/> via
/// <see cref="ReactorHost"/>, drives a re-render, and walks the visual
/// tree to assert that the lazy realization went through the shared
/// <see cref="ElementFactory{T}"/> bridge (the same one used by
/// LazyVStack/LazyHStack) rather than the dead-code-path fallback.
/// </summary>
internal static class ItemsViewFixtures
{
    private record Product(string Sku, string Name, double Price);

    private static readonly Product[] Catalog =
    [
        new("A1", "Apple",  0.99),
        new("B2", "Banana", 0.49),
        new("C3", "Cherry", 2.99),
        new("D4", "Date",   1.49),
        new("E5", "Endive", 1.99),
    ];

    // ────────────────────────────────────────────────────────────────────
    //  Mount — verifies the dispatch arm wires the ItemsView at all, that
    //  the framework has materialized the template (PART_ItemsRepeater),
    //  and that the user's viewBuilder ran for visible rows.
    // ────────────────────────────────────────────────────────────────────

    internal class ItemsView_BasicMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ =>
                ItemsView(Catalog,
                    keySelector: p => p.Sku,
                    viewBuilder: (p, _) =>
                        ItemContainer(
                            HStack(
                                TextBlock(p.Name),
                                TextBlock($"${p.Price:F2}")
                            )
                        )
                ).Height(300)
            );

            await Harness.Render();

            var iv = H.FindControl<WinUI.ItemsView>(_ => true);
            H.Check("ItemsView_Mount_ControlCreated", iv is not null);

            // ItemsView with StackLayout (the default) — confirm the live
            // Layout matches what MountItemsView built.
            H.Check("ItemsView_Mount_HasStackLayout",
                iv?.Layout is WinUI.StackLayout);

            // viewBuilder produces ItemContainer roots — the realized tree
            // must include them, otherwise the framework would have hung
            // in measure (see ItemsView.cpp:317).
            H.Check("ItemsView_Mount_RealizesItemContainer",
                H.FindControl<WinUI.ItemContainer>(_ => true) is not null);

            // The framework realizes rows via ElementFactory<T>.GetElement →
            // viewBuilder → Mount. If the dispatch arm were missing, the
            // ItemsView would render empty and no row text would appear.
            H.Check("ItemsView_Mount_FirstRowRendered",
                H.FindTextContaining("Apple") is not null);

            H.Check("ItemsView_Mount_PriceRendered",
                H.FindTextContaining("$0.99") is not null);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Layout switching — flipping LayoutKind between renders must rotate
    //  the live ItemsView.Layout instance to the matching WinUI type.
    // ────────────────────────────────────────────────────────────────────

    internal class ItemsView_LayoutKind_AppliesUniformGrid(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ =>
                ItemsView(Catalog,
                    keySelector: p => p.Sku,
                    viewBuilder: (p, _) => ItemContainer(TextBlock(p.Name))
                ) with { LayoutKind = ItemsViewLayoutKind.UniformGridLayout }
            );

            await Harness.Render();

            var iv = H.FindControl<WinUI.ItemsView>(_ => true);
            H.Check("ItemsView_Layout_UniformGridApplied",
                iv?.Layout is WinUI.UniformGridLayout);
        }
    }

    internal class ItemsView_LayoutKind_AppliesLinedFlow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ =>
                ItemsView(Catalog,
                    keySelector: p => p.Sku,
                    viewBuilder: (p, _) => ItemContainer(TextBlock(p.Name))
                ) with { LayoutKind = ItemsViewLayoutKind.LinedFlowLayout }
            );

            await Harness.Render();

            var iv = H.FindControl<WinUI.ItemsView>(_ => true);
            H.Check("ItemsView_Layout_LinedFlowApplied",
                iv?.Layout is WinUI.LinedFlowLayout);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Update path — re-rendering with a mutated items list. The factory
    //  is updated in place via TryUpdateFactory so existing realized rows
    //  reflect the new viewBuilder output without a wholesale re-realize.
    // ────────────────────────────────────────────────────────────────────

    internal class ItemsView_Update_ReflectsNewItems(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var items = new List<Product>(Catalog);

            host.Mount(_ =>
                ItemsView(items.ToArray(),
                    keySelector: p => p.Sku,
                    viewBuilder: (p, _) => ItemContainer(TextBlock(p.Name))
                ).Height(300)
            );

            await Harness.Render();

            H.Check("ItemsView_Update_InitialRowVisible",
                H.FindTextContaining("Apple") is not null);

            // Append a new item and re-render. The keyed diff routes this
            // as a single Insert into the OC<ReactorRow> source; the
            // ItemsRepeater realizes a new container and the factory
            // mounts the new row's view.
            items.Add(new Product("F6", "Fig", 3.49));
            host.Mount(_ =>
                ItemsView(items.ToArray(),
                    keySelector: p => p.Sku,
                    viewBuilder: (p, _) => ItemContainer(TextBlock(p.Name))
                ).Height(300)
            );

            await Harness.Render(50);

            H.Check("ItemsView_Update_NewItemVisible",
                H.FindTextContaining("Fig") is not null);
            H.Check("ItemsView_Update_OldItemsStillVisible",
                H.FindTextContaining("Apple") is not null);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Selection mode — confirms the SelectionMode property reaches the
    //  live ItemsView (event payload translation is exercised in unit
    //  tests; this fixture covers the binding side).
    // ────────────────────────────────────────────────────────────────────

    // ────────────────────────────────────────────────────────────────────
    //  Regression: framework-managed selection must survive a re-render.
    //  An earlier UpdateItemContainer mirrored ItemContainerElement.IsSelected
    //  back onto the live control on every reconcile, which clobbered any
    //  selection the user had clicked into and triggered a feedback loop
    //  (visible as a double "yellow flash" with selection cleared each time).
    // ────────────────────────────────────────────────────────────────────

    internal class ItemsView_Selection_SurvivesRerender(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                // The state cell isn't read by the visual tree — it's only
                // here so the Bump button can force a real component
                // re-render of the ItemsView subtree.
                var (tick, setTick) = ctx.UseState(0);
                return VStack(8,
                    Button("Bump", () => setTick(tick + 1)),
                    ItemsView(Catalog,
                        keySelector: p => p.Sku,
                        viewBuilder: (p, _) => ItemContainer(TextBlock(p.Name))
                    ) with { SelectionMode = WinUI.ItemsViewSelectionMode.Single }
                );
            });

            await Harness.Render();

            var iv = H.FindControl<WinUI.ItemsView>(_ => true);
            H.Check("ItemsView_SelectionSurvive_ControlMounted", iv is not null);
            if (iv is null) return;

            // Programmatically select index 1. ItemsView exposes Select(int)
            // for this exactly so we don't need to simulate input.
            iv.Select(1);
            await Harness.Render();
            H.Check("ItemsView_SelectionSurvive_InitialSelectionApplied",
                iv.IsSelected(1));

            // Force a top-level re-render. Before the fix, this reconcile
            // pass walked every realized ItemContainer and wrote
            // n.IsSelected (false) back onto the live control, clearing
            // the selection.
            H.ClickButton("Bump");
            await Harness.Render();

            H.Check("ItemsView_SelectionSurvive_StillSelectedAfterRerender",
                iv.IsSelected(1));
            // And no rogue extra rows became selected as a side effect.
            H.Check("ItemsView_SelectionSurvive_OnlyOneSelected",
                iv.SelectedItems.Count == 1);
        }
    }

    internal class ItemsView_SelectionMode_Applied(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ =>
                ItemsView(Catalog,
                    keySelector: p => p.Sku,
                    viewBuilder: (p, _) => ItemContainer(TextBlock(p.Name))
                ) with { SelectionMode = WinUI.ItemsViewSelectionMode.Multiple }
            );

            await Harness.Render();

            var iv = H.FindControl<WinUI.ItemsView>(_ => true);
            H.Check("ItemsView_SelectionMode_LiveValueMatches",
                iv?.SelectionMode == WinUI.ItemsViewSelectionMode.Multiple);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Regression: an unrelated re-render must NOT mark every realized
    //  ItemContainer as modified. Before ItemContainerElement got arms
    //  in ShallowEquals / OwnPropsEqual, the reconciler-highlight overlay
    //  flagged every container on every render (visible as a yellow flash
    //  per row on every selection click).
    // ────────────────────────────────────────────────────────────────────

    internal class ItemsView_Rerender_DoesNotMarkContainersModified(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // The highlight overlay only populates LastModifiedElements
            // when this flag is on. Save/restore so the fixture doesn't
            // leak global state to subsequent fixtures.
            var prev = ReactorFeatureFlags.HighlightReconcileChanges;
            ReactorFeatureFlags.HighlightReconcileChanges = true;
            try
            {
                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (tick, setTick) = ctx.UseState(0);
                    return VStack(8,
                        Button("Bump", () => setTick(tick + 1)),
                        ItemsView(Catalog,
                            keySelector: p => p.Sku,
                            // Same Catalog reference + key-stable +
                            // pure viewBuilder → every realized row's
                            // (oldElement, newElement) pair should
                            // ShallowEquals to a skip on the second pass.
                            viewBuilder: (p, _) => ItemContainer(TextBlock(p.Name))
                        ) with { SelectionMode = WinUI.ItemsViewSelectionMode.Multiple }
                    );
                });
                await Harness.Render();

                // Snapshot mounted containers before the no-op rerender so
                // we can ask the targeted question: "are any of THESE
                // appearing in LastModifiedElements after Bump?"
                var containersBefore = H.FindAllControls<WinUI.ItemContainer>(_ => true);
                H.Check("ItemsViewRerender_HasRealizedContainers",
                    containersBefore.Count > 0);

                H.ClickButton("Bump");
                await Harness.Render();

                var modified = host.Reconciler.LastModifiedElements;
                int flashedContainers = containersBefore.Count(c => modified.Contains(c));

                // Pre-fix: every realized container was in LastModifiedElements.
                // Post-fix: ItemContainerElement's OwnPropsEqual returns true
                // when IsSelected and Setters match (both unchanged here), so
                // the highlight gate skips them. Allow a small slack for
                // bookkeeping noise; the regression we care about is "all of
                // them flash" which would be a high double-digit number with
                // the demo's 5-item Catalog.
                H.Check($"ItemsViewRerender_NoContainerFlash_modified={flashedContainers}_of_{containersBefore.Count}",
                    flashedContainers == 0);
            }
            finally
            {
                ReactorFeatureFlags.HighlightReconcileChanges = prev;
            }
        }
    }
}
