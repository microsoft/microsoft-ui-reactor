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

    // Disjoint keys + different count from Catalog, so toggling between them
    // forces the inner ItemsRepeater to clear and re-prepare every container
    // (a real recycle round-trip) rather than merely reordering.
    private static readonly Product[] AltCatalog =
    [
        new("X1", "Xigua",    3.49),
        new("Y2", "Yam",      0.79),
        new("Z3", "Zucchini", 1.29),
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

    // ────────────────────────────────────────────────────────────────────
    //  Regression (issue #383): in a SelectionMode=Multiple ItemsView the
    //  per-item selection checkmark flickered (faded out/in) on every realized
    //  row during a window drag-resize. WinUI's ItemsView flips each realized
    //  ItemContainer's internal MultiSelectMode on every recycle round-trip,
    //  re-running MultiSelectStates.Multiple's opacity storyboard with
    //  useTransitions:true — and Reactor's host re-lays-out on every resize
    //  tick, so the inner ItemsRepeater recycles its working set dozens of
    //  times per gesture and the storyboard re-fires over and over.
    //
    //  The mitigation (ItemContainerSelectionFlickerGuard) collapses the
    //  Multiple state's opacity storyboard to zero duration on each realized
    //  container, so WinUI's animated GoToState snaps the checkmark to full
    //  opacity instantly instead of fading it.
    //
    //  This fixture is load-bearing in two complementary halves, both on a real
    //  realized container from a live SelectionMode=Multiple ItemsView (the only
    //  place ItemContainer actually realizes PART_SelectionCheckbox):
    //
    //   Part A — guard mechanism (direct Ensure, before/after). On an
    //   ItemContainer realized by a *raw WinUI* ItemsView (so it never went
    //   through Reactor's ElementFactory and is therefore un-armed), prove the
    //   animated Single -> Multiple transition genuinely FADES (opacity has not
    //   reached 1.0 the instant after GoToState), then call the guard's Ensure
    //   directly and prove the identical transition now SNAPS to 1.0. The fade
    //   makes the snap load-bearing — a no-op guard would leave it fading.
    //
    //   Part B — production wiring (auto-arm, no direct Ensure) + recycle
    //   survival. On an ItemContainer realized by a *Reactor* ItemsView, first
    //   poll until the GetElement-armed guard has actually collapsed the Multiple
    //   storyboard (so the assert doesn't race the deferred Loaded arm on a slow
    //   runner), then drive the transition WITHOUT calling the guard and assert it
    //   snaps. This guards the GetElement -> Ensure wiring. Then force a real
    //   recycle round-trip — toggle the ItemsView to a disjoint keyed item set and
    //   back, so the inner ItemsRepeater clears and re-prepares its containers (the
    //   exact OnItemsRepeaterElementClearing/Prepared path that re-fires the
    //   flicker in production) — and assert a realized container STILL snaps.
    // ────────────────────────────────────────────────────────────────────

    internal class ItemsView_MultiSelect_CheckmarkDoesNotFlicker(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // ── Part A: guard mechanism on an un-armed raw-WinUI container. ──
            // A raw WinUI ItemsView realizes its own ItemContainers (never through
            // Reactor's ElementFactory), so the guard has NOT auto-armed them.
            // Done before any Reactor host owns the content area.
            var rawView = new WinUI.ItemsView
            {
                Width = 300,
                Height = 400,
                SelectionMode = WinUI.ItemsViewSelectionMode.Multiple,
                ItemsSource = new[] { "a", "b", "c", "d", "e" },
                ItemTemplate = (Microsoft.UI.Xaml.DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                    "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                    "<ItemContainer><TextBlock Text=\"{Binding}\"/></ItemContainer></DataTemplate>"),
            };
            H.SetContent(rawView);
            await Harness.Render();
            await Harness.Render();

            var rawContainer = H.FindControl<WinUI.ItemContainer>(_ => true);
            H.Check("ItemsViewFlicker_DirectEnsure_ContainerRealized", rawContainer is not null);
            if (rawContainer is not null)
            {
                var rawCheckbox = FindNamedDescendant(rawContainer, "PART_SelectionCheckbox");
                H.Check("ItemsViewFlicker_DirectEnsure_CheckmarkPartFound", rawCheckbox is not null);
                if (rawCheckbox is not null)
                {
                    // BEFORE: un-armed → the animated transition genuinely fades.
                    Microsoft.UI.Xaml.VisualStateManager.GoToState(rawContainer, "Single", false);
                    await Harness.Render();
                    Microsoft.UI.Xaml.VisualStateManager.GoToState(rawContainer, "Multiple", true);
                    var before = rawCheckbox.Opacity;
                    H.Check($"ItemsViewFlicker_DirectEnsure_Before_Fades_opacity={before:F3}",
                        before < 0.999);

                    // AFTER: arm the guard directly → identical transition snaps.
                    Microsoft.UI.Xaml.VisualStateManager.GoToState(rawContainer, "Single", false);
                    await Harness.Render();
                    ItemContainerSelectionFlickerGuard.Ensure(rawContainer);
                    Microsoft.UI.Xaml.VisualStateManager.GoToState(rawContainer, "Multiple", true);
                    var after = rawCheckbox.Opacity;
                    H.Check($"ItemsViewFlicker_DirectEnsure_After_Snaps_opacity={after:F3}",
                        after >= 0.999);
                }
            }

            // ── Part B: production GetElement -> Ensure auto-arm wiring. ──
            // Mounting a Reactor ItemsView replaces the raw view in the content
            // area. A "recycle" button toggles between two disjoint keyed item
            // sets to force a recycle round-trip in Part C. No direct Ensure call
            // anywhere here — this asserts the wiring.
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (alt, setAlt) = ctx.UseState(false);
                var items = alt ? AltCatalog : Catalog;
                return VStack(
                    Button("recycle", () => setAlt(!alt)),
                    (ItemsView(items,
                        keySelector: p => p.Sku,
                        viewBuilder: (p, _) => ItemContainer(TextBlock(p.Name))
                    ) with { SelectionMode = WinUI.ItemsViewSelectionMode.Multiple }).Height(400)
                );
            });

            // Poll until the deferred-Loaded arm has actually collapsed the
            // storyboard, instead of relying on a fixed render-pass budget
            // (cheap insurance against CI flake on slower runners).
            var armed = await Harness.WaitFor(() =>
            {
                var c = H.FindControl<WinUI.ItemContainer>(_ => true);
                return c is not null && IsMultipleStoryboardCollapsed(c);
            });
            H.Check("ItemsViewFlicker_AutoArm_GuardCollapsedStoryboard", armed);

            var realized = H.FindControl<WinUI.ItemContainer>(_ => true);
            H.Check("ItemsViewFlicker_AutoArm_ContainerRealized", realized is not null);
            if (realized is null) return;

            var checkbox = FindNamedDescendant(realized, "PART_SelectionCheckbox");
            H.Check("ItemsViewFlicker_AutoArm_CheckmarkPartFound", checkbox is not null);
            if (checkbox is null) return;

            Microsoft.UI.Xaml.VisualStateManager.GoToState(realized, "Single", false);
            await Harness.Render();
            Microsoft.UI.Xaml.VisualStateManager.GoToState(realized, "Multiple", true);
            H.Check($"ItemsViewFlicker_AutoArmed_Snaps_opacity={checkbox.Opacity:F3}",
                checkbox.Opacity >= 0.999);

            // ── Part C: survive a real recycle round-trip. ──
            // Toggle to the disjoint keyed set and back, forcing the inner
            // ItemsRepeater to clear + re-prepare its containers — the exact
            // production trigger that re-fires the flicker. A reused container
            // keeps its collapsed storyboard; a freshly realized one is re-armed
            // on prepare. Either way the checkmark must still snap.
            //
            // Assert the realized dataset genuinely swapped each way before
            // checking the snap — otherwise the round-trip could pass vacuously
            // if the toggle ever stopped realizing a new set. AltCatalog has
            // fully disjoint keys AND a different cardinality from Catalog, so a
            // successful toggle (a) changes the bound source count and (b) forces
            // every container to be cleared + re-prepared (no key reuse). Reading
            // the source count is pool-proof: WinUI keeps cleared containers
            // parented in its recycle pool with stale text, so a tree-wide
            // FindText would still match the old set.
            int SourceCount() =>
                (H.FindControl<WinUI.ItemsView>(_ => true)?.ItemsSource
                    as global::System.Collections.ICollection)?.Count ?? -1;

            H.ClickButton("recycle");
            var swappedToAlt = await Harness.WaitFor(() =>
                // "Xigua" exists only in AltCatalog, so a realized container
                // showing it proves a fresh prepare ran (not a pooled-stale
                // leftover); the source-count change confirms the swap.
                H.FindText("Xigua") is not null && SourceCount() == AltCatalog.Length);
            H.Check("ItemsViewFlicker_Recycle_SwappedToAltSet", swappedToAlt);

            H.ClickButton("recycle");
            var swappedBack = await Harness.WaitFor(() =>
                H.FindText("Apple") is not null && SourceCount() == Catalog.Length);
            H.Check("ItemsViewFlicker_Recycle_SwappedBackToOriginal", swappedBack);

            var recycledArmed = await Harness.WaitFor(() =>
            {
                var c = H.FindControl<WinUI.ItemContainer>(_ => true);
                return c is not null && IsMultipleStoryboardCollapsed(c);
            });
            H.Check("ItemsViewFlicker_Recycle_GuardCollapsedStoryboard", recycledArmed);

            var recycled = H.FindControl<WinUI.ItemContainer>(_ => true);
            H.Check("ItemsViewFlicker_Recycle_ContainerRealized", recycled is not null);
            if (recycled is null) return;

            var recycledCheckbox = FindNamedDescendant(recycled, "PART_SelectionCheckbox");
            H.Check("ItemsViewFlicker_Recycle_CheckmarkPartFound", recycledCheckbox is not null);
            if (recycledCheckbox is null) return;

            Microsoft.UI.Xaml.VisualStateManager.GoToState(recycled, "Single", false);
            await Harness.Render();
            Microsoft.UI.Xaml.VisualStateManager.GoToState(recycled, "Multiple", true);
            H.Check($"ItemsViewFlicker_Recycle_Snaps_opacity={recycledCheckbox.Opacity:F3}",
                recycledCheckbox.Opacity >= 0.999);
        }
    }

    // True once the container's MultiSelectStates.Multiple opacity storyboard has
    // been collapsed by the guard — i.e. every keyframe KeyTime is zero. Used to
    // poll for the deferred-Loaded arm completing before asserting.
    private static bool IsMultipleStoryboardCollapsed(WinUI.ItemContainer container)
    {
        if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(container) == 0)
            return false;
        if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(container, 0)
                is not Microsoft.UI.Xaml.FrameworkElement root)
            return false;

        var groups = Microsoft.UI.Xaml.VisualStateManager.GetVisualStateGroups(root);
        foreach (var group in groups.Where(group => group.Name == "MultiSelectStates"))
        {
            foreach (var state in group.States.Where(
                state => state.Name == "Multiple" && state.Storyboard is not null))
            {
                bool sawKeyframe = false;
                foreach (var kf in state.Storyboard!.Children
                    .OfType<Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames>())
                {
                    foreach (var f in kf.KeyFrames)
                    {
                        sawKeyframe = true;
                        if (f.KeyTime.TimeSpan != global::System.TimeSpan.Zero)
                            return false;
                    }
                }
                return sawKeyframe;
            }
        }
        return false;
    }

    private static Microsoft.UI.Xaml.FrameworkElement? FindNamedDescendant(
        Microsoft.UI.Xaml.DependencyObject root, string name)
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is Microsoft.UI.Xaml.FrameworkElement fe && fe.Name == name)
                return fe;
            var found = FindNamedDescendant(child, name);
            if (found is not null) return found;
        }
        return null;
    }
}

