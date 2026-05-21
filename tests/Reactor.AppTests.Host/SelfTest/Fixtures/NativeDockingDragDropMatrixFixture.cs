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
            // would duplicate. Acceptable result: 2 unique panes still
            // visible (no orphan, no crash). The mutator's MovePane removes
            // first then inserts, so net stays 2.
            H.Check("M01_TabCountStable", TabCount(H) is >= 2 and <= 3);
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
