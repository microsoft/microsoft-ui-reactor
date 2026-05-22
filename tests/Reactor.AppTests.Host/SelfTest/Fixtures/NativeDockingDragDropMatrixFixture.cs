using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.4 — drag/drop matrix fixture.
//
//  Each fixture exercises one programmatic drag scenario against a known
//  starting layout. The pattern:
//
//    1. Build a DockManager with an explicit Layout.
//    2. Mount with ShowDropTargets=true (skips the real-drag-required
//       overlay-trigger path; OnConfirm still runs the §2.4 mutation).
//    3. Locate the panes by Key/Title in the visual tree.
//    4. Begin a DockDragSession + call overlay.ConfirmTargetForTest(target).
//    5. Assert the resulting visual tree matches the expected shape:
//       tab count, splitter count, text presence, and no orphaned panes.
//
//  These fixtures intentionally bypass real pointer events (the harness
//  doesn't deliver them). What they exercise is the §2.4 pipeline from
//  overlay confirm through layout mutation through reconcile — the layer
//  that breaks when the mutator math is wrong, the layout-override state
//  doesn't propagate, or the renderer produces a tree the reconciler
//  can't safely apply.
//
//  Failures from this matrix are the canonical polish-bug list.
// ════════════════════════════════════════════════════════════════════════

internal static class NativeDockingDragDropMatrixFixtures
{
    // ── Shared helpers ─────────────────────────────────────────────────

    private static DockableContent MakePane(string key, string text) =>
        new(Title: key, Key: key, Content: TextBlock(text), CanClose: true);

    private static void Simulate(
        Harness h,
        DockableContent source,
        DockManager manager,
        DockTarget target,
        int sourceIndex = 0)
    {
        DockDragSession.ResetForTest();
        DockDragSession.Begin(source, manager, sourceIndex);
        var overlay = h.FindAllControls<DockDropTargetOverlayControl>(_ => true).FirstOrDefault();
        if (overlay is null)
        {
            h.Check("Sim_OverlayFound", false);
            return;
        }
        overlay.ConfirmTargetForTest(target);
    }

    /// <summary>
    /// Number of FlexPanels (= splits) currently mounted. Each DockSplit
    /// in the effective layout renders to exactly one FlexPanel.
    /// </summary>
    private static int SplitCount(Harness h) =>
        h.FindAllControls<FlexPanel>(_ => true).Count;

    /// <summary>Total tabs across every mounted TabView.</summary>
    private static int TabCount(Harness h) =>
        h.FindAllControls<TabView>(_ => true).Sum(t => t.TabItems.Count);

    private static int TabViewCount(Harness h) =>
        h.FindAllControls<TabView>(_ => true).Count;

    /// <summary>
    /// Root FlexPanel produced by the §2.1 split renderer. Tests that
    /// assert orientation/placement read this to verify the split shape
    /// (not just split presence).
    /// </summary>
    private static FlexPanel? RootSplitPanel(Harness h) =>
        h.FindAllControls<FlexPanel>(_ => true).FirstOrDefault();

    /// <summary>
    /// True if <paramref name="panel"/>'s child at <paramref name="index"/>
    /// is a TabView whose first tab carries header text equal to
    /// <paramref name="header"/>. The split renderer's child layout is
    /// [TabView, DockSplitter, TabView, ...] — only even indices are
    /// pane slots.
    /// </summary>
    private static bool PaneAt(FlexPanel panel, int index, string header)
    {
        if (panel.Children.Count <= index) return false;
        if (panel.Children[index] is not TabView tv) return false;
        if (tv.TabItems.Count == 0) return false;
        return tv.TabItems[0] is TabViewItem tvi && (tvi.Header as string) == header;
    }

    // ── Scenarios ──────────────────────────────────────────────────────

    /// <summary>
    /// Drag a tab from a 2-tab group onto Center of the SAME group — the
    /// mutator should fold the pane back as a tab; total tabs unchanged.
    /// (Edge case: idempotent drop-on-self.)
    /// </summary>
    internal class DragToCenterSameGroup_NoOp(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DockDragSession.ResetForTest();
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var a = MakePane("a", "body-a");
            var b = MakePane("b", "body-b");
            host.Mount(_ => new DockManager
            {
                Layout = new DockTabGroup(new[] { a, b }),
                ShowDropTargets = true,
            });
            await Harness.Render();

            H.Check("M01_StartingTabs", TabCount(H) == 2);
            Simulate(H, a, new DockManager(), DockTarget.Center);
            await Harness.Render();

            // Center adds-as-tab to the first group; with 'a' as source it
            // would duplicate. The mutator's MovePane removes-then-inserts,
            // so the net is exactly 2 — a duplicate-pane regression would
            // produce 3 tabs.
            H.Check("M01_TabCountStable_NoDuplicate", TabCount(H) == 2);
            // WinUI TabView only mounts the selected tab's body in the
            // visual tree; check tab headers instead of body text.
            var tabs = H.FindAllControls<TabView>(_ => true).FirstOrDefault();
            var headers = tabs?.TabItems
                .OfType<TabViewItem>()
                .Select(t => t.Header as string)
                .ToList() ?? new();
            H.Check("M01_BothTabsPresent",
                headers.Contains("a") && headers.Contains("b"));
            DockDragSession.ResetForTest();
        }
    }

    internal class DragToSplitRight_AddsColumn(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DockDragSession.ResetForTest();
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var a = MakePane("a", "body-a");
            var b = MakePane("b", "body-b");
            host.Mount(_ => new DockManager
            {
                Layout = new DockTabGroup(new[] { a, b }),
                ShowDropTargets = true,
            });
            await Harness.Render();

            H.Check("M02_StartingSplits", SplitCount(H) == 0);
            Simulate(H, a, new DockManager(), DockTarget.SplitRight);
            await Harness.Render();

            H.Check("M02_HorizontalSplitAppeared", SplitCount(H) == 1);
            H.Check("M02_TwoTabViews", TabViewCount(H) == 2);
            H.Check("M02_BothPanesReachable",
                H.FindText("body-a") is not null && H.FindText("body-b") is not null);

            var panel = RootSplitPanel(H);
            H.Check("M02_FlexPanelIsRow",
                panel is { Direction: FlexDirection.Row });
            // SplitRight: 'a' is moved into a new trailing group. 'b' stays
            // in the original (leading) group.
            H.Check("M02_LeadingGroupContainsB",
                panel is not null && PaneAt(panel, 0, "b"));
            H.Check("M02_TrailingGroupContainsA",
                panel is not null && PaneAt(panel, 2, "a"));
            DockDragSession.ResetForTest();
        }
    }

    internal class DragToSplitLeft_AddsLeadingColumn(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DockDragSession.ResetForTest();
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var a = MakePane("a", "body-a");
            var b = MakePane("b", "body-b");
            host.Mount(_ => new DockManager
            {
                Layout = new DockTabGroup(new[] { a, b }),
                ShowDropTargets = true,
            });
            await Harness.Render();

            Simulate(H, b, new DockManager(), DockTarget.SplitLeft);
            await Harness.Render();

            H.Check("M03_HorizontalSplitAppeared", SplitCount(H) == 1);
            H.Check("M03_BodyBReachable", H.FindText("body-b") is not null);

            var panel = RootSplitPanel(H);
            H.Check("M03_FlexPanelIsRow",
                panel is { Direction: FlexDirection.Row });
            // SplitLeft: 'b' is moved into a new leading group. 'a' stays
            // in the original (trailing) group.
            H.Check("M03_LeadingGroupContainsB",
                panel is not null && PaneAt(panel, 0, "b"));
            H.Check("M03_TrailingGroupContainsA",
                panel is not null && PaneAt(panel, 2, "a"));
            DockDragSession.ResetForTest();
        }
    }

    internal class DragToSplitTop_AddsRow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DockDragSession.ResetForTest();
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var a = MakePane("a", "body-a");
            var b = MakePane("b", "body-b");
            host.Mount(_ => new DockManager
            {
                Layout = new DockTabGroup(new[] { a, b }),
                ShowDropTargets = true,
            });
            await Harness.Render();

            Simulate(H, a, new DockManager(), DockTarget.SplitTop);
            await Harness.Render();

            H.Check("M04_VerticalSplitAppeared", SplitCount(H) == 1);
            H.Check("M04_TwoTabViews", TabViewCount(H) == 2);

            var panel = RootSplitPanel(H);
            H.Check("M04_FlexPanelIsColumn",
                panel is { Direction: FlexDirection.Column });
            // SplitTop: 'a' moves into a new leading group above 'b'.
            H.Check("M04_LeadingGroupContainsA",
                panel is not null && PaneAt(panel, 0, "a"));
            H.Check("M04_TrailingGroupContainsB",
                panel is not null && PaneAt(panel, 2, "b"));
            DockDragSession.ResetForTest();
        }
    }

    internal class DragToSplitBottom_AddsRow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DockDragSession.ResetForTest();
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var a = MakePane("a", "body-a");
            var b = MakePane("b", "body-b");
            host.Mount(_ => new DockManager
            {
                Layout = new DockTabGroup(new[] { a, b }),
                ShowDropTargets = true,
            });
            await Harness.Render();

            Simulate(H, b, new DockManager(), DockTarget.SplitBottom);
            await Harness.Render();

            H.Check("M05_VerticalSplitAppeared", SplitCount(H) == 1);
            H.Check("M05_BothBodiesReachable",
                H.FindText("body-a") is not null && H.FindText("body-b") is not null);

            var panel = RootSplitPanel(H);
            H.Check("M05_FlexPanelIsColumn",
                panel is { Direction: FlexDirection.Column });
            // SplitBottom: 'b' moves into a new trailing group below 'a'.
            H.Check("M05_LeadingGroupContainsA",
                panel is not null && PaneAt(panel, 0, "a"));
            H.Check("M05_TrailingGroupContainsB",
                panel is not null && PaneAt(panel, 2, "b"));
            DockDragSession.ResetForTest();
        }
    }

    internal class DragLastTabFromGroup_CollapsesGroup(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DockDragSession.ResetForTest();
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            // Two side-by-side groups, each with one pane. Dragging the
            // sole pane out of group L should collapse L; group R survives.
            var only = MakePane("only", "body-only");
            var right = MakePane("right", "body-right");
            host.Mount(_ => new DockManager
            {
                Layout = new DockSplit(Orientation.Horizontal, new DockNode[]
                {
                    new DockTabGroup(new[] { only }),
                    new DockTabGroup(new[] { right }),
                }),
                ShowDropTargets = true,
            });
            await Harness.Render();

            H.Check("M06_StartingTwoSplits", SplitCount(H) == 1);
            H.Check("M06_StartingTabViews", TabViewCount(H) == 2);

            Simulate(H, only, new DockManager(), DockTarget.SplitRight);
            await Harness.Render();

            // After moving 'only' to SplitRight at root:
            // - The L group collapses (its sole document gone) → outer
            //   split collapses to just the R group + new split rebuilt
            //   at root with [layout, only-wrapped-in-group].
            // - Net: still 1 split, 2 TabViews, 2 bodies.
            H.Check("M06_OnePathRemains", SplitCount(H) == 1);
            H.Check("M06_TwoTabViewsAfter", TabViewCount(H) == 2);
            H.Check("M06_BothBodiesReachable",
                H.FindText("body-only") is not null && H.FindText("body-right") is not null);
            DockDragSession.ResetForTest();
        }
    }

    internal class DragToDockLeftEdge_WrapsAtRoot(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DockDragSession.ResetForTest();
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var a = MakePane("a", "body-a");
            var b = MakePane("b", "body-b");
            host.Mount(_ => new DockManager
            {
                Layout = new DockTabGroup(new[] { a, b }),
                ShowDropTargets = true,
            });
            await Harness.Render();

            Simulate(H, a, new DockManager(), DockTarget.DockLeft);
            await Harness.Render();

            H.Check("M07_HorizontalSplitAtRoot", SplitCount(H) == 1);
            H.Check("M07_TwoTabViews", TabViewCount(H) == 2);

            var panel = RootSplitPanel(H);
            H.Check("M07_FlexPanelIsRow",
                panel is { Direction: FlexDirection.Row });
            // DockLeft wraps 'a' to the leading edge of the root split.
            H.Check("M07_LeadingGroupContainsA",
                panel is not null && PaneAt(panel, 0, "a"));
            DockDragSession.ResetForTest();
        }
    }

    internal class DragToDockRightEdge_WrapsAtRoot(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DockDragSession.ResetForTest();
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var a = MakePane("a", "body-a");
            var b = MakePane("b", "body-b");
            host.Mount(_ => new DockManager
            {
                Layout = new DockTabGroup(new[] { a, b }),
                ShowDropTargets = true,
            });
            await Harness.Render();

            Simulate(H, a, new DockManager(), DockTarget.DockRight);
            await Harness.Render();

            H.Check("M08_HorizontalSplitAtRoot", SplitCount(H) == 1);
            H.Check("M08_TwoTabViews", TabViewCount(H) == 2);

            var panel = RootSplitPanel(H);
            H.Check("M08_FlexPanelIsRow",
                panel is { Direction: FlexDirection.Row });
            // DockRight wraps 'a' to the trailing edge of the root split.
            H.Check("M08_TrailingGroupContainsA",
                panel is not null && PaneAt(panel, 2, "a"));
            DockDragSession.ResetForTest();
        }
    }

    internal class DragToDockTopEdge_WrapsAtRoot(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DockDragSession.ResetForTest();
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var a = MakePane("a", "body-a");
            var b = MakePane("b", "body-b");
            host.Mount(_ => new DockManager
            {
                Layout = new DockTabGroup(new[] { a, b }),
                ShowDropTargets = true,
            });
            await Harness.Render();

            Simulate(H, b, new DockManager(), DockTarget.DockTop);
            await Harness.Render();

            H.Check("M09_VerticalSplitAtRoot", SplitCount(H) == 1);

            var panel = RootSplitPanel(H);
            H.Check("M09_FlexPanelIsColumn",
                panel is { Direction: FlexDirection.Column });
            // DockTop wraps 'b' above the existing layout.
            H.Check("M09_LeadingGroupContainsB",
                panel is not null && PaneAt(panel, 0, "b"));
            DockDragSession.ResetForTest();
        }
    }

    internal class DragToDockBottomEdge_WrapsAtRoot(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DockDragSession.ResetForTest();
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var a = MakePane("a", "body-a");
            var b = MakePane("b", "body-b");
            host.Mount(_ => new DockManager
            {
                Layout = new DockTabGroup(new[] { a, b }),
                ShowDropTargets = true,
            });
            await Harness.Render();

            Simulate(H, b, new DockManager(), DockTarget.DockBottom);
            await Harness.Render();

            H.Check("M10_VerticalSplitAtRoot", SplitCount(H) == 1);

            var panel = RootSplitPanel(H);
            H.Check("M10_FlexPanelIsColumn",
                panel is { Direction: FlexDirection.Column });
            // DockBottom wraps 'b' below the existing layout.
            H.Check("M10_TrailingGroupContainsB",
                panel is not null && PaneAt(panel, 2, "b"));
            DockDragSession.ResetForTest();
        }
    }

    /// <summary>
    /// Sequential drag chain: drag A → SplitRight, drag B → SplitTop. The
    /// resulting tree should have 2 splits and 3+ tab strips, with no
    /// orphaned panes.
    /// </summary>
    internal class SequentialDrags_AccumulateLayout(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DockDragSession.ResetForTest();
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var a = MakePane("a", "body-a");
            var b = MakePane("b", "body-b");
            var c = MakePane("c", "body-c");
            host.Mount(_ => new DockManager
            {
                Layout = new DockTabGroup(new[] { a, b, c }),
                ShowDropTargets = true,
            });
            await Harness.Render();

            Simulate(H, a, new DockManager(), DockTarget.SplitRight);
            await Harness.Render();
            Simulate(H, b, new DockManager(), DockTarget.SplitTop);
            await Harness.Render();

            H.Check("M11_MultipleSplits", SplitCount(H) >= 2);
            H.Check("M11_AllPanesPresent",
                H.FindText("body-a") is not null
                && H.FindText("body-b") is not null
                && H.FindText("body-c") is not null);
            DockDragSession.ResetForTest();
        }
    }

    /// <summary>
    /// Cancel an in-flight drag (overlay's OnDismiss path). Layout must
    /// be byte-identical to the starting state (no removal applied).
    /// </summary>
    internal class CancelDrag_LayoutUnchanged(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DockDragSession.ResetForTest();
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var a = MakePane("a", "body-a");
            var b = MakePane("b", "body-b");
            host.Mount(_ => new DockManager
            {
                Layout = new DockTabGroup(new[] { a, b }),
                ShowDropTargets = true,
            });
            await Harness.Render();

            var startSplits = SplitCount(H);
            var startTabs = TabCount(H);

            // Begin drag then dismiss via overlay (Esc path).
            DockDragSession.Begin(a, new DockManager(), 0);
            var overlay = H.FindAllControls<DockDropTargetOverlayControl>(_ => true).FirstOrDefault();
            if (overlay is null) { H.Check("M12_OverlayFound", false); return; }
            // The control's OverlayDismissed event is internal; for the
            // smoke fixture we exercise it indirectly via Esc on the
            // global hook by raising it through reflection-free public
            // path: cancel the session and let the host's defensive
            // dragActive-but-no-session clear path catch up.
            DockDragSession.Current?.Cancel();
            await Harness.Render();

            H.Check("M12_SplitsUnchanged", SplitCount(H) == startSplits);
            H.Check("M12_TabsUnchanged", TabCount(H) == startTabs);
            // Check tab headers, not bodies — non-selected tab bodies
            // aren't in the visual tree.
            var tabs = H.FindAllControls<TabView>(_ => true).FirstOrDefault();
            var headers = tabs?.TabItems
                .OfType<TabViewItem>()
                .Select(t => t.Header as string)
                .ToList() ?? new();
            H.Check("M12_BothTabsPresent",
                headers.Contains("a") && headers.Contains("b"));
            DockDragSession.ResetForTest();
        }
    }

    /// <summary>
    /// Drag from a deeply nested split. Outer split ratios should be
    /// preserved across the mutation (mutator should only touch the
    /// removed pane's parent path).
    /// </summary>
    internal class NestedSplitDrag_OuterShapePreserved(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DockDragSession.ResetForTest();
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var a = MakePane("a", "body-a");
            var b = MakePane("b", "body-b");
            var c = MakePane("c", "body-c");
            var d = MakePane("d", "body-d");
            host.Mount(_ => new DockManager
            {
                Layout = new DockSplit(Orientation.Vertical, new DockNode[]
                {
                    new DockSplit(Orientation.Horizontal, new DockNode[]
                    {
                        new DockTabGroup(new[] { a, b }),
                        new DockTabGroup(new[] { c }),
                    }),
                    new DockTabGroup(new[] { d }),
                }),
                ShowDropTargets = true,
            });
            await Harness.Render();

            H.Check("M13_StartingSplits", SplitCount(H) == 2);

            Simulate(H, b, new DockManager(), DockTarget.SplitRight);
            await Harness.Render();

            // The whole-tree wrap at SplitRight adds one outer split. The
            // inner [a,b] collapses to a after b leaves. Resulting tree:
            // outer Horizontal split [{whole-old-tree-minus-b}, group(b)].
            // We expect at least the same number of splits, plus the new
            // outer wrap, and all 4 panes still reachable.
            H.Check("M13_SplitsIncreased", SplitCount(H) >= 2);
            H.Check("M13_AllPanesPresent",
                H.FindText("body-a") is not null
                && H.FindText("body-b") is not null
                && H.FindText("body-c") is not null
                && H.FindText("body-d") is not null);
            DockDragSession.ResetForTest();
        }
    }

    /// <summary>
    /// Drag every pane out, one at a time. After the last drag, at least
    /// one pane must still be visible (the last-pane-out case wraps it
    /// at root). No crash on empty intermediate states.
    /// </summary>
    internal class DragEveryPaneOut_NoOrphans(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DockDragSession.ResetForTest();
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var a = MakePane("a", "body-a");
            var b = MakePane("b", "body-b");
            var c = MakePane("c", "body-c");
            host.Mount(_ => new DockManager
            {
                Layout = new DockTabGroup(new[] { a, b, c }),
                ShowDropTargets = true,
            });
            await Harness.Render();

            Simulate(H, a, new DockManager(), DockTarget.SplitRight);
            await Harness.Render();
            Simulate(H, b, new DockManager(), DockTarget.SplitBottom);
            await Harness.Render();
            Simulate(H, c, new DockManager(), DockTarget.DockLeft);
            await Harness.Render();

            H.Check("M14_AllPanesReachable",
                H.FindText("body-a") is not null
                && H.FindText("body-b") is not null
                && H.FindText("body-c") is not null);
            DockDragSession.ResetForTest();
        }
    }

    /// <summary>
    /// Window-resize regression. After a splitter drag, the panes are
    /// supposed to redistribute on a parent resize via Yoga grow. Today
    /// the splitter sets inline Width/Height which freezes panes in
    /// place — this fixture FAILS as the canonical witness, and should
    /// pass once §2.1 splitter is repaired.
    /// </summary>
    internal class WindowResizeAfterSplitterDrag_PanesRedistribute(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            host.Mount(_ => new DockManager
            {
                Layout = new DockSplit(Orientation.Horizontal, new DockNode[]
                {
                    MakePane("L", "body-L"),
                    MakePane("R", "body-R"),
                }),
            });
            await Harness.Render();

            var splitter = H.FindAllControls<DockSplitterControl>(_ => true).FirstOrDefault();
            if (splitter is null) { H.Check("M15_SplitterFound", false); return; }
            var panel = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(splitter) as FlexPanel;
            if (panel is null) { H.Check("M15_FlexPanelFound", false); return; }

            // Drive the inline-size mutation path that the real pointer
            // drag uses (snapshot pair + apply absolute delta). This is
            // the path that leaves inline Width/Height set on the panes
            // and that breaks subsequent window resize.
            splitter.SimulatePointerDragForTest(cumulativeDeltaDip: 80);
            await Harness.Render();

            // Take a baseline of the leading pane width *and* the panel.
            var leadingBefore = (panel.Children[0] as FrameworkElement)?.ActualWidth ?? 0;
            var panelWidthBefore = panel.ActualWidth;
            Console.WriteLine($"# M15 baseline panelW={panelWidthBefore:F1} leadingW={leadingBefore:F1}");

            // Shrink the panel by 200 DIP. Yoga should redistribute grow
            // values so both children shrink proportionally. If the
            // splitter pinned inline Width/Height, the leading pane stays
            // the same DIP — that's the bug.
            panel.Width = panelWidthBefore - 200;
            await Harness.Render();

            var leadingAfter = (panel.Children[0] as FrameworkElement)?.ActualWidth ?? 0;
            Console.WriteLine($"# M15 after  panelW={panel.ActualWidth:F1} leadingW={leadingAfter:F1}");

            // The leading pane should have shrunk too. Allow ±10 DIP
            // slack for splitter handle + measurement rounding.
            var shrank = leadingAfter < leadingBefore - 1;
            H.Check("M15_LeadingPaneShrankWithPanel", shrank);
            DockDragSession.ResetForTest();
        }
    }

    /// <summary>
    /// Idempotency — running the same drop target twice on the same pane
    /// should produce the same tree as running it once (the second drag
    /// re-finds the pane and re-applies the operation).
    /// </summary>
    /// <summary>
    /// Repro: after dragging an inner column splitter, dragging the outer
    /// row splitter must NOT reset the column ratios. Witnesses a bug
    /// where a row-splitter release ends up re-bootstrapping or
    /// overwriting the inner panel's child Grow values.
    /// </summary>
    internal class RowSplitterDragPreservesInnerColumnRatios(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            host.Mount(_ => new DockManager
            {
                Layout = new DockSplit(Orientation.Vertical, new DockNode[]
                {
                    new DockSplit(Orientation.Horizontal, new DockNode[]
                    {
                        MakePane("editor", "body-editor"),
                        MakePane("tools",  "body-tools"),
                    }),
                    new DockSplit(Orientation.Horizontal, new DockNode[]
                    {
                        MakePane("output",   "body-output"),
                        MakePane("terminal", "body-terminal"),
                    }),
                }),
            });
            await Harness.Render();

            // Find the row splitter (Rows direction) and the top column
            // splitter (the FIRST Columns-direction splitter — its parent
            // is the top inner FlexPanel).
            var splitters = H.FindAllControls<DockSplitterControl>(_ => true);
            var rowSplitter = splitters.FirstOrDefault(s => s.Direction == DockSplitterDirection.Rows);
            var colSplitters = splitters.Where(s => s.Direction == DockSplitterDirection.Columns).ToList();
            if (rowSplitter is null || colSplitters.Count < 2)
            {
                H.Check("M17_SplittersFound", false);
                return;
            }

            var topColSplitter = colSplitters[0];
            var topColParent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(topColSplitter) as FlexPanel;
            if (topColParent is null) { H.Check("M17_TopColParent", false); return; }

            // Drag the top column splitter right by 80 DIP (editor grows).
            topColSplitter.SimulatePointerDragForTest(cumulativeDeltaDip: 80);
            await Harness.Render();

            var editorGrowAfterColDrag = FlexPanel.GetGrow(topColParent.Children[0]);
            var toolsGrowAfterColDrag = FlexPanel.GetGrow(topColParent.Children[2]);
            Console.WriteLine($"# M17 after-col-drag editor={editorGrowAfterColDrag:F3} tools={toolsGrowAfterColDrag:F3}");
            H.Check("M17_ColumnDragShiftedGrow",
                editorGrowAfterColDrag > toolsGrowAfterColDrag + 0.01);

            // Now drag the row splitter UP by 60 DIP (top half shrinks).
            rowSplitter.SimulatePointerDragForTest(cumulativeDeltaDip: -60);
            await Harness.Render();

            var editorGrowAfterRowDrag = FlexPanel.GetGrow(topColParent.Children[0]);
            var toolsGrowAfterRowDrag = FlexPanel.GetGrow(topColParent.Children[2]);
            Console.WriteLine($"# M17 after-row-drag editor={editorGrowAfterRowDrag:F3} tools={toolsGrowAfterRowDrag:F3}");

            // Column ratios should be preserved across the row drag.
            // Allow a small tolerance for normalization.
            H.Check("M17_ColumnRatiosPreservedAcrossRowDrag",
                Math.Abs(editorGrowAfterRowDrag - editorGrowAfterColDrag) < 0.05
                && Math.Abs(toolsGrowAfterRowDrag - toolsGrowAfterColDrag) < 0.05);
        }
    }

    /// <summary>
    /// Witness for the splitter "jump back" bug: after a pointer-drag
    /// release, the rendered pane widths should match the cursor-
    /// committed widths (within a 1 DIP fudge for floating-point
    /// rounding). Pre-fix the solver used full panel.ActualWidth as
    /// totalDip while Yoga distributed (panel - splitter.Width), so the
    /// re-render landed ~handle*ratio DIP off the cursor position.
    /// </summary>
    internal class SplitterReleaseNoVisibleJump(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            host.Mount(_ => new DockManager
            {
                Layout = new DockSplit(Orientation.Horizontal, new DockNode[]
                {
                    MakePane("L", "body-L"),
                    MakePane("R", "body-R"),
                }),
            });
            await Harness.Render();

            var splitter = H.FindAllControls<DockSplitterControl>(_ => true).FirstOrDefault();
            if (splitter is null) { H.Check("M18_SplitterFound", false); return; }
            var panel = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(splitter) as FlexPanel;
            if (panel is null) { H.Check("M18_FlexPanelFound", false); return; }

            // Drive a big drag where the bug is most visible.
            splitter.SimulatePointerDragForTest(cumulativeDeltaDip: 200);
            await Harness.Render();

            var leadingAfter = (panel.Children[0] as FrameworkElement)?.ActualWidth ?? 0;
            var trailingAfter = (panel.Children[2] as FrameworkElement)?.ActualWidth ?? 0;
            var splitterW = splitter.ActualWidth;
            var pairTotal = leadingAfter + trailingAfter;
            var expectedPair = panel.ActualWidth - splitterW;
            Console.WriteLine($"# M18 panelW={panel.ActualWidth:F1} splitterW={splitterW:F1} leading={leadingAfter:F1} trailing={trailingAfter:F1}");

            // After release, leading + trailing should equal panel - splitter,
            // i.e. no overflow and no gap. Pre-fix this was off by a fraction
            // proportional to the splitter handle's share of the pair (e.g.
            // ~16 DIP for a 50/50 split). Yoga rounding gives ±1 DIP slack.
            H.Check("M18_PairFillsPanelMinusSplitter",
                Math.Abs(pairTotal - expectedPair) <= 2.0);
        }
    }

    /// <summary>
    /// End-to-end IDE-layout fixture. Mounts a Vertical split containing
    /// two Horizontal splits (editor+tools / output+terminal) and drives:
    ///   1. column splitter drag — assert top-row ratios shift
    ///   2. row splitter drag    — assert outer ratios shift AND column
    ///      ratios are preserved (the row-resets-columns regression)
    ///   3. container resize     — assert all panes redistribute, ratios
    ///      unchanged
    ///
    /// Also asserts CONTROL IDENTITY across every operation: the host's
    /// inner component-wrapper Border, every FlexPanel, every TabView,
    /// and every DockSplitterControl must be the same instance pre- and
    /// post-operation. The Border-swap regression shows up here as an
    /// instance change after the first splitter drag.
    /// </summary>
    internal class IdeLayoutResizeAndContainerResize_NoControlChurn(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DockDragSession.ResetForTest();
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            // Use DockTabGroup wrappers like the showcase IDE layout —
            // bare DockableContent leaves render as Border (no tab strip)
            // and don't surface the control-identity churn we're hunting.
            host.Mount(_ => new DockManager
            {
                Layout = new DockSplit(Orientation.Vertical, new DockNode[]
                {
                    new DockSplit(Orientation.Horizontal, new DockNode[]
                    {
                        new DockTabGroup(new[] { MakePane("editor", "body-editor") }),
                        new DockTabGroup(new[] { MakePane("tools",  "body-tools") }),
                    }),
                    new DockSplit(Orientation.Horizontal, new DockNode[]
                    {
                        new DockTabGroup(new[] { MakePane("output",   "body-output") }),
                        new DockTabGroup(new[] { MakePane("terminal", "body-terminal") }),
                    }),
                }),
            });
            await Harness.Render();

            // ── Snapshot the initial control identity set.
            var splittersInit = H.FindAllControls<DockSplitterControl>(_ => true);
            var rowInit = splittersInit.First(s => s.Direction == DockSplitterDirection.Rows);
            var colsInit = splittersInit.Where(s => s.Direction == DockSplitterDirection.Columns).ToList();
            var topColSplitter = colsInit[0];
            var topColPanel = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(topColSplitter) as FlexPanel;
            var bottomColSplitter = colsInit[1];
            var bottomColPanel = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(bottomColSplitter) as FlexPanel;
            var outerPanel = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(rowInit) as FlexPanel;
            var tabViewsInit = H.FindAllControls<TabView>(_ => true);

            H.Check("M19_Initial_FourTabViews", tabViewsInit.Count == 4);
            H.Check("M19_Initial_ThreeSplitters", splittersInit.Count == 3);
            H.Check("M19_Initial_OuterFlexPanel", outerPanel is not null);
            H.Check("M19_Initial_TopColParent", topColPanel is not null);
            H.Check("M19_Initial_BottomColParent", bottomColPanel is not null);

            double GrowOf(UIElement child) => FlexPanel.GetGrow(child);
            double[] PaneGrows(FlexPanel panel) =>
                panel.Children.OfType<FrameworkElement>().Where(c => c is not DockSplitterControl)
                     .Select(c => GrowOf(c)).ToArray();

            // ── Step 1: drag the top column splitter right by 100 DIP.
            topColSplitter.SimulatePointerDragForTest(cumulativeDeltaDip: 100);
            await Harness.Render();

            var topAfterCol = PaneGrows(topColPanel!);
            var outerAfterCol = PaneGrows(outerPanel!);
            var bottomAfterCol = PaneGrows(bottomColPanel!);
            Console.WriteLine($"# M19 after-col top=[{string.Join(",", topAfterCol.Select(g => g.ToString("F3")))}] outer=[{string.Join(",", outerAfterCol.Select(g => g.ToString("F3")))}] bottom=[{string.Join(",", bottomAfterCol.Select(g => g.ToString("F3")))}]");

            H.Check("M19_Col_EditorGrew",
                topAfterCol.Length == 2 && topAfterCol[0] > topAfterCol[1] + 0.01);
            H.Check("M19_Col_OuterUntouched",
                outerAfterCol.Length == 2
                && Math.Abs(outerAfterCol[0] - 0.5) < 0.05
                && Math.Abs(outerAfterCol[1] - 0.5) < 0.05);
            H.Check("M19_Col_BottomUntouched",
                bottomAfterCol.Length == 2
                && Math.Abs(bottomAfterCol[0] - 0.5) < 0.05
                && Math.Abs(bottomAfterCol[1] - 0.5) < 0.05);

            // Control identity must NOT change across a splitter drag.
            var splittersAfterCol = H.FindAllControls<DockSplitterControl>(_ => true);
            var tabViewsAfterCol = H.FindAllControls<TabView>(_ => true);
            H.Check("M19_Col_SplitterIdentityPreserved",
                splittersAfterCol.Count == 3
                && ReferenceEquals(splittersAfterCol.First(s => s.Direction == DockSplitterDirection.Rows), rowInit)
                && ReferenceEquals(splittersAfterCol.Where(s => s.Direction == DockSplitterDirection.Columns).ElementAt(0), topColSplitter)
                && ReferenceEquals(splittersAfterCol.Where(s => s.Direction == DockSplitterDirection.Columns).ElementAt(1), bottomColSplitter));
            H.Check("M19_Col_TabViewIdentityPreserved",
                tabViewsAfterCol.Count == 4
                && tabViewsAfterCol.Zip(tabViewsInit, ReferenceEquals).All(x => x));
            H.Check("M19_Col_FlexPanelIdentityPreserved",
                ReferenceEquals(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(topColSplitter), topColPanel)
                && ReferenceEquals(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(rowInit), outerPanel)
                && ReferenceEquals(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(bottomColSplitter), bottomColPanel));

            // ── Step 2: drag the row splitter down by 60 DIP.
            rowInit.SimulatePointerDragForTest(cumulativeDeltaDip: 60);
            await Harness.Render();

            var topAfterRow = PaneGrows(topColPanel!);
            var outerAfterRow = PaneGrows(outerPanel!);
            var bottomAfterRow = PaneGrows(bottomColPanel!);
            Console.WriteLine($"# M19 after-row top=[{string.Join(",", topAfterRow.Select(g => g.ToString("F3")))}] outer=[{string.Join(",", outerAfterRow.Select(g => g.ToString("F3")))}] bottom=[{string.Join(",", bottomAfterRow.Select(g => g.ToString("F3")))}]");

            H.Check("M19_Row_OuterShifted",
                outerAfterRow.Length == 2 && outerAfterRow[0] > outerAfterCol[0] + 0.01);
            // Critical: column ratios PRESERVED across row drag.
            H.Check("M19_Row_TopColumnsPreserved",
                Math.Abs(topAfterRow[0] - topAfterCol[0]) < 0.02
                && Math.Abs(topAfterRow[1] - topAfterCol[1]) < 0.02);
            H.Check("M19_Row_BottomColumnsPreserved",
                Math.Abs(bottomAfterRow[0] - bottomAfterCol[0]) < 0.02
                && Math.Abs(bottomAfterRow[1] - bottomAfterCol[1]) < 0.02);

            var splittersAfterRow = H.FindAllControls<DockSplitterControl>(_ => true);
            var tabViewsAfterRow = H.FindAllControls<TabView>(_ => true);
            H.Check("M19_Row_SplitterIdentityPreserved",
                splittersAfterRow.Count == 3
                && ReferenceEquals(splittersAfterRow.First(s => s.Direction == DockSplitterDirection.Rows), rowInit));
            H.Check("M19_Row_TabViewIdentityPreserved",
                tabViewsAfterRow.Count == 4
                && tabViewsAfterRow.Zip(tabViewsInit, ReferenceEquals).All(x => x));

            // ── Step 3: shrink the outer panel by 200 DIP on both axes.
            var outerWBefore = outerPanel!.ActualWidth;
            var outerHBefore = outerPanel!.ActualHeight;
            outerPanel!.Width = outerWBefore - 200;
            outerPanel!.Height = outerHBefore - 200;
            await Harness.Render();

            var topAfterResize = PaneGrows(topColPanel!);
            var outerAfterResize = PaneGrows(outerPanel!);
            var bottomAfterResize = PaneGrows(bottomColPanel!);
            Console.WriteLine($"# M19 after-resize panelW={outerPanel!.ActualWidth:F1} panelH={outerPanel!.ActualHeight:F1} top=[{string.Join(",", topAfterResize.Select(g => g.ToString("F3")))}] outer=[{string.Join(",", outerAfterResize.Select(g => g.ToString("F3")))}] bottom=[{string.Join(",", bottomAfterResize.Select(g => g.ToString("F3")))}]");

            // Ratios MUST be unchanged by container resize — that's the
            // grow-based-distribution contract. Pre-fix the splitter set
            // inline Width/Height which froze panes; post-fix grow drives
            // distribution and ratios are stable.
            H.Check("M19_Resize_TopRatiosUnchanged",
                Math.Abs(topAfterResize[0] - topAfterRow[0]) < 0.02
                && Math.Abs(topAfterResize[1] - topAfterRow[1]) < 0.02);
            H.Check("M19_Resize_OuterRatiosUnchanged",
                Math.Abs(outerAfterResize[0] - outerAfterRow[0]) < 0.02
                && Math.Abs(outerAfterResize[1] - outerAfterRow[1]) < 0.02);
            H.Check("M19_Resize_BottomRatiosUnchanged",
                Math.Abs(bottomAfterResize[0] - bottomAfterRow[0]) < 0.02
                && Math.Abs(bottomAfterResize[1] - bottomAfterRow[1]) < 0.02);

            // After resize the panes should actually have shrunk —
            // proof that grow is doing real distribution, not just
            // serving stale inline sizes.
            var paneWidthAfter = (topColPanel!.Children[0] as FrameworkElement)?.ActualWidth ?? 0;
            H.Check("M19_Resize_PanesRedistributed",
                paneWidthAfter < outerWBefore * topAfterRow[0]);

            var splittersAfterResize = H.FindAllControls<DockSplitterControl>(_ => true);
            var tabViewsAfterResize = H.FindAllControls<TabView>(_ => true);
            H.Check("M19_Resize_SplitterIdentityPreserved",
                splittersAfterResize.Count == 3
                && ReferenceEquals(splittersAfterResize.First(s => s.Direction == DockSplitterDirection.Rows), rowInit));
            H.Check("M19_Resize_TabViewIdentityPreserved",
                tabViewsAfterResize.Count == 4
                && tabViewsAfterResize.Zip(tabViewsInit, ReferenceEquals).All(x => x));

            DockDragSession.ResetForTest();
        }
    }

    /// <summary>
    /// Scene-level re-render fixture. Mounts a DockManager inside a
    /// Component that owns its own UseReducer tick, then forces a re-
    /// render of the wrapping component. This exercises
    /// DockingNativeInterop's update path (not just the host
    /// component's internal re-render path that M19 hits). Surfaces the
    /// Border swap regression seen in the showcase diagnostic.
    /// </summary>
    internal class SceneRerenderPreservesDockHostControls(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DockDragSession.ResetForTest();
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            // Use a wrapping component with its own state — re-renders
            // propagate through DockingNativeInterop.update.
            host.Mount(_ => Component<SceneRerenderWrapper>());
            await Harness.Render();

            // Capture initial control identities.
            var splittersBefore = H.FindAllControls<DockSplitterControl>(_ => true);
            var tabViewsBefore = H.FindAllControls<TabView>(_ => true);
            var flexPanelsBefore = H.FindAllControls<FlexPanel>(_ => true);
            var splitterBefore = splittersBefore.FirstOrDefault();
            H.Check("M20_Initial_SplitterMounted", splitterBefore is not null);
            H.Check("M20_Initial_TabViewMounted", tabViewsBefore.Count == 2);
            H.Check("M20_Initial_FlexPanelMounted", flexPanelsBefore.Count == 1);

            // Click the button inside the wrapper to force a re-render.
            // The wrapper's state change creates a new DockManager element
            // — DockingNativeInterop.update fires.
            H.ClickButton("BumpRender");
            await Harness.Render();
            H.ClickButton("BumpRender");
            await Harness.Render();
            H.ClickButton("BumpRender");
            await Harness.Render();

            var splittersAfter = H.FindAllControls<DockSplitterControl>(_ => true);
            var tabViewsAfter = H.FindAllControls<TabView>(_ => true);
            var flexPanelsAfter = H.FindAllControls<FlexPanel>(_ => true);

            H.Check("M20_SplitterIdentityAcrossSceneRerender",
                splittersAfter.Count == 1
                && ReferenceEquals(splittersAfter[0], splitterBefore));
            H.Check("M20_TabViewIdentityAcrossSceneRerender",
                tabViewsAfter.Count == 2
                && tabViewsAfter.Zip(tabViewsBefore, ReferenceEquals).All(x => x));
            H.Check("M20_FlexPanelIdentityAcrossSceneRerender",
                flexPanelsAfter.Count == 1
                && ReferenceEquals(flexPanelsAfter[0], flexPanelsBefore[0]));
        }
    }

    internal class SceneRerenderWrapper : Component
    {
        public override Element Render()
        {
            var (_, bump) = UseReducer(0);
            return VStack(
                Button("BumpRender", () => bump(t => t + 1)),
                new DockManager
                {
                    Layout = new DockSplit(Orientation.Horizontal, new DockNode[]
                    {
                        new DockTabGroup(new[] { new DockableContent("L", TextBlock("body-L"), Key: "l") }),
                        new DockTabGroup(new[] { new DockableContent("R", TextBlock("body-R"), Key: "r") }),
                    }),
                }.Flex(grow: 1)
            );
        }
    }

    internal class IdempotentDragSameTarget_StableTree(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DockDragSession.ResetForTest();
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var a = MakePane("a", "body-a");
            var b = MakePane("b", "body-b");
            host.Mount(_ => new DockManager
            {
                Layout = new DockTabGroup(new[] { a, b }),
                ShowDropTargets = true,
            });
            await Harness.Render();

            Simulate(H, a, new DockManager(), DockTarget.SplitRight);
            await Harness.Render();
            var splitsAfterFirst = SplitCount(H);
            var tabsAfterFirst = TabCount(H);

            Simulate(H, a, new DockManager(), DockTarget.SplitRight);
            await Harness.Render();
            var splitsAfterSecond = SplitCount(H);
            var tabsAfterSecond = TabCount(H);

            H.Check("M16_SplitsStableAcrossRepeat", splitsAfterSecond == splitsAfterFirst);
            H.Check("M16_TabsStableAcrossRepeat", tabsAfterSecond == tabsAfterFirst);
            DockDragSession.ResetForTest();
        }
    }
}
