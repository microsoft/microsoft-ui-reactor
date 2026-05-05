using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Regression coverage for the templated/lazy collection element types in
/// <see cref="Element.OwnPropsEqual"/> and <see cref="Element.ShallowEquals"/>.
///
/// Without explicit cases for <c>TemplatedListElementBase</c> and
/// <c>LazyStackElementBase</c>, OwnPropsEqual fell through to <c>_ =&gt; false</c>,
/// which made <see cref="ReconcileHighlightOverlay"/> mark the entire list/grid/
/// flip-view (and lazy-stack ScrollViewer) as "modified" on every parent re-render
/// — even when nothing about the element actually changed. Visually that paints a
/// yellow stripe over the whole list region, making it look like every item was
/// reconciled when in fact none were.
///
/// These fixtures pin the contract:
///  - Unrelated parent re-renders MUST NOT add the list/grid/flip control to
///    LastModifiedElements.
///  - Property changes (Header, SelectionMode, Setters) MUST still flag the
///    control as modified.
///  - Item-level updates (Items reference change) MUST still propagate.
/// </summary>
internal static class TemplatedListHighlightTests
{
    // ── Typed ListView<T>: stable items + sibling state change → not modified ──
    internal class TemplatedListView_NoHighlightOnSiblingStateChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var prev = ReactorFeatureFlags.HighlightReconcileChanges;
            try
            {
                ReactorFeatureFlags.HighlightReconcileChanges = true;

                // Items list is allocated once, outside the render closure, so its
                // reference is stable across re-renders triggered by `count`.
                var items = new[] { "a", "b", "c" };

                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (count, setCount) = ctx.UseState(0);
                    return VStack(
                        TextBlock($"count:{count}").AutomationId("tlvCount"),
                        Button("inc", () => setCount(count + 1)),
                        ListView<string>(items, s => s, (item, _) => TextBlock(item))
                    );
                });

                await Harness.Render();

                // Bump unrelated state — the ListView's own props are unchanged.
                H.ClickButton("inc");
                await Harness.Render();

                var modified = host.Reconciler.LastModifiedElements;
                var listView = H.FindControl<ListView>(_ => true);

                H.Check("TemplatedListHL_ListViewExists", listView is not null);
                H.Check("TemplatedListHL_ListView_NotModifiedOnSiblingChange",
                    listView is not null && !modified.Contains(listView));

                // Sanity: the TextBlock that did change should still be flagged.
                var tb = H.FindControl<TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == "tlvCount");
                H.Check("TemplatedListHL_SiblingTextBlockModified",
                    tb is not null && modified.Contains(tb));
            }
            finally
            {
                ReactorFeatureFlags.HighlightReconcileChanges = prev;
            }
        }
    }

    // ── Typed ListView<T>: header change DOES flag the control ──
    internal class TemplatedListView_HeaderChangeFlagsListView(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var prev = ReactorFeatureFlags.HighlightReconcileChanges;
            try
            {
                ReactorFeatureFlags.HighlightReconcileChanges = true;

                var items = new[] { "x", "y" };

                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (header, setHeader) = ctx.UseState("h1");
                    return VStack(
                        Button("rename", () => setHeader("h2")),
                        ListView<string>(items, s => s, (item, _) => TextBlock(item))
                            with { Header = header }
                    );
                });

                await Harness.Render();

                H.ClickButton("rename");
                await Harness.Render();

                var modified = host.Reconciler.LastModifiedElements;
                var listView = H.FindControl<ListView>(_ => true);

                H.Check("TemplatedListHL_HeaderChange_ListViewExists", listView is not null);
                H.Check("TemplatedListHL_HeaderChange_ListViewModified",
                    listView is not null && modified.Contains(listView));
                H.Check("TemplatedListHL_HeaderChange_HeaderApplied",
                    listView?.Header is string s && s == "h2");
            }
            finally
            {
                ReactorFeatureFlags.HighlightReconcileChanges = prev;
            }
        }
    }

    // ── Typed ListView<T>: items reference change still triggers item updates ──
    internal class TemplatedListView_ItemsChangeStillUpdates(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { "Alpha", "Beta" }
                    : new[] { "Alpha", "Beta", "Gamma" };
                return VStack(
                    Button("grow", () => setPhase(1)),
                    ListView<string>(items, s => s, (item, _) => TextBlock(item).AutomationId($"tli_{item}"))
                );
            });

            await Harness.Render();
            H.Check("TemplatedListHL_ItemsChange_InitialAlpha",
                H.FindControl<TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == "tli_Alpha") is not null);

            H.ClickButton("grow");
            await Harness.Render();

            // Realized containers should reflect the new item count.
            var listView = H.FindControl<ListView>(_ => true);
            H.Check("TemplatedListHL_ItemsChange_CountReflected",
                listView is not null
                && listView.ItemsSource is global::System.Collections.IList src
                && src.Count == 3);
        }
    }

    // ── Typed ListView<T>: viewBuilder closure capturing outer state must
    //     refresh realized containers on parent re-render even when the items
    //     reference is unchanged. The previous SameItemsAs short-circuit was
    //     unsound: it assumed viewBuilder was a pure function of (item, index),
    //     but viewBuilder closures legitimately capture other state (count,
    //     theme, mode flags, etc.). With the short-circuit in place, the
    //     header text refreshed but list rows stayed frozen at their first-
    //     render values.
    internal class TemplatedListView_ViewBuilderClosureRefreshes(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Stable items reference — same list instance across renders.
            var items = new[] { "a", "b" };

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (count, setCount) = ctx.UseState(0);
                return VStack(
                    Button("inc", () => setCount(count + 1)),
                    ListView<string>(items, s => s, (item, _) =>
                        TextBlock($"{item}:{count}").AutomationId($"row_{item}"))
                );
            });

            await Harness.Render();

            // Sanity: rows reflect the initial count.
            var rowAInit = H.FindControl<TextBlock>(t =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == "row_a");
            H.Check("TemplatedListHL_ViewBuilderClosure_InitialCount",
                rowAInit is not null && rowAInit.Text == "a:0");

            // Bump count. The items reference is unchanged, but the viewBuilder
            // closure now captures count=1.
            H.ClickButton("inc");
            await Harness.Render();

            var rowA = H.FindControl<TextBlock>(t =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == "row_a");
            var rowB = H.FindControl<TextBlock>(t =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == "row_b");
            H.Check("TemplatedListHL_ViewBuilderClosure_RowA_Refreshed",
                rowA is not null && rowA.Text == "a:1");
            H.Check("TemplatedListHL_ViewBuilderClosure_RowB_Refreshed",
                rowB is not null && rowB.Text == "b:1");
        }
    }

    // Same contract for GridView<T>.
    internal class TemplatedGridView_ViewBuilderClosureRefreshes(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var items = new[] { "g1", "g2" };

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (count, setCount) = ctx.UseState(0);
                return VStack(
                    Button("inc", () => setCount(count + 1)),
                    GridView<string>(items, s => s, (item, _) =>
                        TextBlock($"{item}:{count}").AutomationId($"gv_{item}"))
                );
            });

            await Harness.Render();
            H.ClickButton("inc");
            await Harness.Render();

            var cell = H.FindControl<TextBlock>(t =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == "gv_g1");
            H.Check("TemplatedListHL_GridView_ViewBuilderClosure_Refreshed",
                cell is not null && cell.Text == "g1:1");
        }
    }

    // ── Typed ListView<T>: .Set(...) Setters force "modified" tagging ──
    // HasSetters has to gate the OwnPropsEqual short-circuit — without that
    // gate, ApplyControlSetters would silently stop running.
    internal class TemplatedListView_SettersStillFlagged(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var prev = ReactorFeatureFlags.HighlightReconcileChanges;
            try
            {
                ReactorFeatureFlags.HighlightReconcileChanges = true;

                var items = new[] { "p", "q" };
                int setterRuns = 0;

                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (count, setCount) = ctx.UseState(0);
                    return VStack(
                        TextBlock($"c:{count}").AutomationId("setterCount"),
                        Button("tick", () => setCount(count + 1)),
                        ListView<string>(items, s => s, (item, _) => TextBlock(item))
                            .Set(lv => { setterRuns++; })
                    );
                });

                await Harness.Render();
                int afterMount = setterRuns;
                H.Check("TemplatedListHL_Setters_RanOnMount", afterMount >= 1);

                H.ClickButton("tick");
                await Harness.Render();

                // Setter must run again on update because HasSetters disables the skip.
                H.Check("TemplatedListHL_Setters_RanOnUpdate", setterRuns > afterMount);

                // And the ListView should be tagged modified (the gate is HasSetters).
                var listView = H.FindControl<ListView>(_ => true);
                var modified = host.Reconciler.LastModifiedElements;
                H.Check("TemplatedListHL_Setters_ListViewModified",
                    listView is not null && modified.Contains(listView));
            }
            finally
            {
                ReactorFeatureFlags.HighlightReconcileChanges = prev;
            }
        }
    }

    // ── Typed GridView<T>: stable items + sibling change → not modified ──
    internal class TemplatedGridView_NoHighlightOnSiblingStateChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var prev = ReactorFeatureFlags.HighlightReconcileChanges;
            try
            {
                ReactorFeatureFlags.HighlightReconcileChanges = true;

                var items = new[] { "g1", "g2", "g3" };

                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (count, setCount) = ctx.UseState(0);
                    return VStack(
                        TextBlock($"gc:{count}").AutomationId("tgvCount"),
                        Button("ginc", () => setCount(count + 1)),
                        GridView<string>(items, s => s, (item, _) => TextBlock(item))
                    );
                });

                await Harness.Render();
                H.ClickButton("ginc");
                await Harness.Render();

                var modified = host.Reconciler.LastModifiedElements;
                var gridView = H.FindControl<GridView>(_ => true);
                H.Check("TemplatedListHL_GridViewExists", gridView is not null);
                H.Check("TemplatedListHL_GridView_NotModifiedOnSiblingChange",
                    gridView is not null && !modified.Contains(gridView));
            }
            finally
            {
                ReactorFeatureFlags.HighlightReconcileChanges = prev;
            }
        }
    }

    // ── Typed FlipView<T>: stable items + sibling change → not modified ──
    internal class TemplatedFlipView_NoHighlightOnSiblingStateChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var prev = ReactorFeatureFlags.HighlightReconcileChanges;
            try
            {
                ReactorFeatureFlags.HighlightReconcileChanges = true;

                var items = new[] { "f1", "f2" };

                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (count, setCount) = ctx.UseState(0);
                    return VStack(
                        TextBlock($"fc:{count}").AutomationId("tfvCount"),
                        Button("finc", () => setCount(count + 1)),
                        FlipView<string>(items, s => s, (item, _) => TextBlock(item))
                    );
                });

                await Harness.Render();
                H.ClickButton("finc");
                await Harness.Render();

                var modified = host.Reconciler.LastModifiedElements;
                var flipView = H.FindControl<FlipView>(_ => true);
                H.Check("TemplatedListHL_FlipViewExists", flipView is not null);
                H.Check("TemplatedListHL_FlipView_NotModifiedOnSiblingChange",
                    flipView is not null && !modified.Contains(flipView));
            }
            finally
            {
                ReactorFeatureFlags.HighlightReconcileChanges = prev;
            }
        }
    }

    // ── LazyVStack<T>: stable items + sibling change → ScrollViewer not modified ──
    // The lazy stack mounts as ScrollViewer + ItemsRepeater; the OwnPropsEqual
    // gate covers the ScrollViewer (the element control), which is what would
    // otherwise be added to LastModifiedElements.
    internal class LazyVStack_NoHighlightOnSiblingStateChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var prev = ReactorFeatureFlags.HighlightReconcileChanges;
            try
            {
                ReactorFeatureFlags.HighlightReconcileChanges = true;

                var items = Enumerable.Range(1, 10).Select(i => $"L{i}").ToArray();

                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (count, setCount) = ctx.UseState(0);
                    return VStack(
                        TextBlock($"lvc:{count}").AutomationId("lvCount"),
                        Button("lvinc", () => setCount(count + 1)),
                        LazyVStack<string>(items, s => s, (item, _) => TextBlock(item))
                    );
                });

                await Harness.Render();

                H.ClickButton("lvinc");
                await Harness.Render();

                var modified = host.Reconciler.LastModifiedElements;
                // After the click, the only element that should be marked modified
                // is the count TextBlock. The lazy ScrollViewer + ItemsRepeater
                // should NOT appear.
                var modifiedScrollViewers = modified.OfType<ScrollViewer>().ToList();
                var modifiedRepeaters = modified.OfType<ItemsRepeater>().ToList();

                H.Check("TemplatedListHL_LazyVStack_ScrollViewerNotModified",
                    modifiedScrollViewers.Count == 0);
                H.Check("TemplatedListHL_LazyVStack_RepeaterNotModified",
                    modifiedRepeaters.Count == 0);
            }
            finally
            {
                ReactorFeatureFlags.HighlightReconcileChanges = prev;
            }
        }
    }

    // ── LazyHStack<T>: same expectation, horizontal orientation ──
    internal class LazyHStack_NoHighlightOnSiblingStateChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var prev = ReactorFeatureFlags.HighlightReconcileChanges;
            try
            {
                ReactorFeatureFlags.HighlightReconcileChanges = true;

                var items = Enumerable.Range(1, 10).Select(i => $"H{i}").ToArray();

                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (count, setCount) = ctx.UseState(0);
                    return VStack(
                        TextBlock($"lhc:{count}").AutomationId("lhCount"),
                        Button("lhinc", () => setCount(count + 1)),
                        LazyHStack<string>(items, s => s, (item, _) => TextBlock(item))
                    );
                });

                await Harness.Render();
                H.ClickButton("lhinc");
                await Harness.Render();

                var modified = host.Reconciler.LastModifiedElements;
                var modifiedScrollViewers = modified.OfType<ScrollViewer>().ToList();
                var modifiedRepeaters = modified.OfType<ItemsRepeater>().ToList();

                H.Check("TemplatedListHL_LazyHStack_ScrollViewerNotModified",
                    modifiedScrollViewers.Count == 0);
                H.Check("TemplatedListHL_LazyHStack_RepeaterNotModified",
                    modifiedRepeaters.Count == 0);
            }
            finally
            {
                ReactorFeatureFlags.HighlightReconcileChanges = prev;
            }
        }
    }

    // ── LazyVStack<T>: spacing change DOES flag the ScrollViewer ──
    // Spacing is in OwnPropsEqual, so changing it must mark the control modified.
    internal class LazyVStack_SpacingChangeFlagsControl(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var prev = ReactorFeatureFlags.HighlightReconcileChanges;
            try
            {
                ReactorFeatureFlags.HighlightReconcileChanges = true;

                var items = Enumerable.Range(1, 5).Select(i => $"S{i}").ToArray();

                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (spacing, setSpacing) = ctx.UseState(8.0);
                    return VStack(
                        Button("respace", () => setSpacing(20.0)),
                        LazyVStack<string>(items, s => s, (item, _) => TextBlock(item))
                            with { Spacing = spacing }
                    );
                });

                await Harness.Render();
                H.ClickButton("respace");
                await Harness.Render();

                var modified = host.Reconciler.LastModifiedElements;
                // At least the ScrollViewer (or the inner ItemsRepeater whose
                // StackLayout.Spacing is rewritten) should appear in modified.
                bool anyLazyControlModified =
                    modified.OfType<ScrollViewer>().Any()
                    || modified.OfType<ItemsRepeater>().Any();
                H.Check("TemplatedListHL_LazyVStack_SpacingChangeFlagged",
                    anyLazyControlModified);
            }
            finally
            {
                ReactorFeatureFlags.HighlightReconcileChanges = prev;
            }
        }
    }
}
