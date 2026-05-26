using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

// ════════════════════════════════════════════════════════════════════════
//  Splitter pointer-drag against complex DockManager layouts.
//
//  The SplitterMatrix fixtures lock the splitter+FlexPanel math down at
//  the unit level. These fixtures verify the SAME math survives once the
//  splitter is composed by DockHostNativeComponent — i.e., when the
//  splitter sits in a FlexPanel produced by DockSplitRenderer alongside
//  TabViews / tool-window strips / role-tagged groups / nested splits.
//
//  Two checking modes per layout:
//    • Pointer drag (BeginSimulatedDrag → End): exercises the live
//      pane.ActualWidth → grow conversion path on the real composed tree.
//    • Programmatic resize (SplitRatios dict + manual ratio mutation):
//      exercises the alternate path apps use without involving the
//      splitter at all. Parity assertion: both paths converge to the
//      same final pane widths.
//
//  Each layout is its own fixture class — easy to enable/disable, easy
//  to find when a future bug regresses one shape.
// ════════════════════════════════════════════════════════════════════════

internal static class DockingComplexLayoutSplitterFixtures
{
    // ── Shared rig helpers ───────────────────────────────────────────────

    private static DockableContent Doc(string key, string text) =>
        new(Title: key, Key: key, Content: TextBlock(text), CanClose: true);

    private static ToolWindow Tool(string key, string text) =>
        new() { Title = key, Key = key, Content = TextBlock(text) };

    private static FlexPanel? FindFlexPanel(Harness h, Func<FlexPanel, bool>? predicate = null) =>
        h.FindAllControls<FlexPanel>(p => predicate?.Invoke(p) ?? true).FirstOrDefault();

    private static List<DockSplitterControl> FindSplitters(Harness h) =>
        h.FindAllControls<DockSplitterControl>(_ => true);

    private static bool Near(double a, double b, double tol = 1.0) =>
        Math.Abs(a - b) <= tol;

    /// <summary>
    /// Drive a single pointer drag against the splitter at the given
    /// 0-based index in the visual tree. Returns the splitter's parent
    /// FlexPanel + the ordered pane children (skipping splitter siblings)
    /// so the caller can read ActualWidth / ActualHeight before & after.
    /// </summary>
    private static (FlexPanel Panel, FrameworkElement Leading, FrameworkElement Trailing)
        DragSplitter(Harness h, int splitterIndex, double cumDelta)
    {
        var splitters = FindSplitters(h);
        var s = splitters[splitterIndex];
        var panel = (FlexPanel)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(s);
        int idx = 0;
        for (int i = 0; i < panel.Children.Count; i++)
            if (ReferenceEquals(panel.Children[i], s)) { idx = i; break; }
        var leading = (FrameworkElement)panel.Children[idx - 1];
        var trailing = (FrameworkElement)panel.Children[idx + 1];

        var origin = new Point(0, 0);
        var dest = s.Direction == DockSplitterDirection.Columns
            ? new Point(cumDelta, 0)
            : new Point(0, cumDelta);

        s.BeginSimulatedDrag(origin);
        s.ContinueSimulatedDrag(dest);
        s.EndSimulatedDrag(dest);
        return (panel, leading, trailing);
    }

    private static double Extent(FrameworkElement fe, DockSplitterDirection dir) =>
        dir == DockSplitterDirection.Columns ? fe.ActualWidth : fe.ActualHeight;

    // ════════════════════════════════════════════════════════════════════
    //  1 — IDE shape (Scene A): vertical split → two horizontal halves
    //
    //         ┌──────────┬──────────┐
    //         │ editor   │ solution │   ← top horizontal split (in vert)
    //         ├──────────┴──────────┤
    //         │      output         │   ← bottom (single group)
    //         └─────────────────────┘
    // ════════════════════════════════════════════════════════════════════

    internal class L01_IDEShape_DragInnerHorizontalSplitter_OnlyTopPanesResize(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var layout = new DockSplit(Orientation.Vertical, new DockNode[]
            {
                new DockSplit(Orientation.Horizontal, new DockNode[]
                {
                    Doc("editor", "editor body"),
                    Doc("solution", "solution body"),
                }),
                Doc("output", "output body"),
            });
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);
            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();
            await Harness.Render();

            // Two splitters in this tree: outer vertical (between top half
            // and bottom output), inner horizontal (between editor + solution).
            // Find the horizontal one and drag it +60.
            var splitters = FindSplitters(H);
            H.Check("L01_TwoSplittersMounted", splitters.Count == 2);
            var inner = splitters.First(s => s.Direction == DockSplitterDirection.Columns);
            var outer = splitters.First(s => s.Direction == DockSplitterDirection.Rows);

            var outerPanel = (FlexPanel)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(outer);
            var outputTabHeight = ((FrameworkElement)outerPanel.Children[2]).ActualHeight;

            var innerPanel = (FlexPanel)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(inner);
            var editor = (FrameworkElement)innerPanel.Children[0];
            var solution = (FrameworkElement)innerPanel.Children[2];
            var editorBefore = editor.ActualWidth;
            var solutionBefore = solution.ActualWidth;

            inner.BeginSimulatedDrag(new Point(0, 0));
            inner.EndSimulatedDrag(new Point(60, 0));
            await Harness.Render();

            H.Check("L01_EditorGrewBy60", Near(editor.ActualWidth, editorBefore + 60));
            H.Check("L01_SolutionShrunkBy60", Near(solution.ActualWidth, solutionBefore - 60));
            // Bottom row's height must NOT change from a horizontal drag.
            H.Check("L01_OutputHeightUnchanged",
                Near(((FrameworkElement)outerPanel.Children[2]).ActualHeight, outputTabHeight));

        }
    }

    internal class L02_IDEShape_DragOuterVerticalSplitter_OnlyVerticalPanesResize(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var layout = new DockSplit(Orientation.Vertical, new DockNode[]
            {
                new DockSplit(Orientation.Horizontal, new DockNode[]
                {
                    Doc("editor", "editor body"),
                    Doc("solution", "solution body"),
                }),
                Doc("output", "output body"),
            });
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);
            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();
            await Harness.Render();

            var splitters = FindSplitters(H);
            var outer = splitters.First(s => s.Direction == DockSplitterDirection.Rows);
            var inner = splitters.First(s => s.Direction == DockSplitterDirection.Columns);

            var outerPanel = (FlexPanel)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(outer);
            var topHalf = (FrameworkElement)outerPanel.Children[0];
            var bottom = (FrameworkElement)outerPanel.Children[2];

            // Inner editor + solution widths must NOT change from the
            // outer vertical drag — only their height halves do.
            var innerPanel = (FlexPanel)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(inner);
            var editorWBefore = ((FrameworkElement)innerPanel.Children[0]).ActualWidth;

            var topBefore = topHalf.ActualHeight;
            var bottomBefore = bottom.ActualHeight;
            outer.BeginSimulatedDrag(new Point(0, 0));
            outer.EndSimulatedDrag(new Point(0, -40));
            await Harness.Render();

            H.Check("L02_TopShrunkBy40", Near(topHalf.ActualHeight, topBefore - 40));
            H.Check("L02_BottomGrewBy40", Near(bottom.ActualHeight, bottomBefore + 40));
            H.Check("L02_InnerWidthsUntouched",
                Near(((FrameworkElement)innerPanel.Children[0]).ActualWidth, editorWBefore));

        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  2 — 3x3 grid via nested splits
    //
    //  Outer vertical split with 3 rows. Each row is a horizontal split
    //  with 3 columns. 6 splitters total (3 inner horizontal + 2 outer
    //  vertical). Drag a middle inner splitter — only its two siblings
    //  resize; the other 7 panes stay put.
    // ════════════════════════════════════════════════════════════════════

    internal class L03_ThreeByThreeGrid_DragMiddleRowSplitter_OnlyMidRowResizes(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DockSplit Row(int r) => new(Orientation.Horizontal, new DockNode[]
            {
                Doc($"r{r}c0", $"r{r}c0 body"),
                Doc($"r{r}c1", $"r{r}c1 body"),
                Doc($"r{r}c2", $"r{r}c2 body"),
            });
            var layout = new DockSplit(Orientation.Vertical, new DockNode[] { Row(0), Row(1), Row(2) });
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);
            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();
            await Harness.Render();

            // Locate the middle row's first inner splitter (between r1c0 and r1c1).
            // Strategy: the row that contains the cell with header "r1c0" is
            // the middle row's FlexPanel; its splitters are the inner ones.
            // 3 rows × 2 horizontal splitters/row + 2 vertical splitters
            // between rows = 8 splitters.
            var allSplitters = FindSplitters(H);
            H.Check("L03_EightSplittersMounted", allSplitters.Count == 8);

            // Column splitters live in each row's inner FlexPanel; row
            // splitters (vertical) live in the outer FlexPanel. Group the
            // column splitters by their parent FlexPanel so the middle
            // row is the entry whose parent is centered (in document
            // order, 2nd of 3) in the outer split.
            var columnSplitters = allSplitters.Where(s => s.Direction == DockSplitterDirection.Columns).ToList();
            H.Check("L03_SixColumnSplitters", columnSplitters.Count == 6);
            if (columnSplitters.Count < 6) return;

            // Middle row's leftmost column splitter = the 3rd column
            // splitter in tree order (rows 0/1/2 each contribute 2 in
            // index pairs (0,1), (2,3), (4,5)).
            var target = columnSplitters[2];
            var targetPanel = (FlexPanel)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(target);
            int idx = 0;
            for (int i = 0; i < targetPanel.Children.Count; i++)
                if (ReferenceEquals(targetPanel.Children[i], target)) { idx = i; break; }
            var leading = (FrameworkElement)targetPanel.Children[idx - 1];
            var trailing = (FrameworkElement)targetPanel.Children[idx + 1];

            // Capture widths of every TabView in the visual tree, drag,
            // and assert only the splitter's two siblings changed.
            var allTabs = H.FindAllControls<TabView>(_ => true).Cast<FrameworkElement>().ToList();
            var before = allTabs.ToDictionary(fe => fe, fe => fe.ActualWidth);
            var leadingBefore = leading.ActualWidth;
            var trailingBefore = trailing.ActualWidth;

            target.BeginSimulatedDrag(new Point(0, 0));
            target.EndSimulatedDrag(new Point(40, 0));
            await Harness.Render();

            H.Check("L03_LeadingGrewBy40", Near(leading.ActualWidth, leadingBefore + 40));
            H.Check("L03_TrailingShrunkBy40", Near(trailing.ActualWidth, trailingBefore - 40));
            // Every OTHER TabView in the tree must be unchanged.
            int changed = 0;
            foreach (var fe in allTabs)
            {
                if (ReferenceEquals(fe, leading) || ReferenceEquals(fe, trailing)) continue;
                if (!Near(fe.ActualWidth, before[fe])) changed++;
            }
            H.Check("L03_NonSiblingsUntouched", changed == 0);

        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  3 — Role-tagged Scene-J shape: tool strip / doc area / tool strip
    //
    //  Same layout shape that triggered the Pix bug. With Width hints on
    //  the side strips, BootstrapRatios falls back to EqualShare because
    //  the middle has no hint — exercises the very case the production
    //  fix targets.
    // ════════════════════════════════════════════════════════════════════

    internal class L04_SceneJShape_DragLeftSplitter_DocAreaAndLeftResize(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var layout = new DockSplit(Orientation.Horizontal, new DockNode[]
            {
                new DockTabGroup(new DockableContent[] { Tool("gallery", "gallery body") },
                    Width: 240, Role: DockGroupRole.ToolWindowStrip),
                new DockTabGroup(Array.Empty<DockableContent>(),
                    Role: DockGroupRole.DocumentArea),
                new DockTabGroup(new DockableContent[] { Tool("config", "config body") },
                    Width: 280, Role: DockGroupRole.ToolWindowStrip),
            });
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);
            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();
            await Harness.Render();

            var splitters = FindSplitters(H);
            H.Check("L04_TwoSplittersMounted", splitters.Count == 2);
            var s0 = splitters[0];
            var panel = (FlexPanel)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(s0);
            var leftStrip = (FrameworkElement)panel.Children[0];
            var docArea = (FrameworkElement)panel.Children[2];
            var rightStrip = (FrameworkElement)panel.Children[4];

            var leftBefore = leftStrip.ActualWidth;
            var docBefore = docArea.ActualWidth;
            var rightBefore = rightStrip.ActualWidth;

            // Drag the left splitter rightward 75 DIPs — left strip grows,
            // doc area shrinks, right strip stays put.
            s0.BeginSimulatedDrag(new Point(0, 0));
            s0.EndSimulatedDrag(new Point(75, 0));
            await Harness.Render();

            H.Check("L04_LeftStripGrewBy75", Near(leftStrip.ActualWidth, leftBefore + 75));
            H.Check("L04_DocAreaShrunkBy75", Near(docArea.ActualWidth, docBefore - 75));
            H.Check("L04_RightStripUntouched", Near(rightStrip.ActualWidth, rightBefore));

        }
    }

    internal class L05_SceneJShape_DragRightSplitter_DocAreaAndRightResize(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var layout = new DockSplit(Orientation.Horizontal, new DockNode[]
            {
                new DockTabGroup(new DockableContent[] { Tool("gallery", "gallery body") },
                    Width: 240, Role: DockGroupRole.ToolWindowStrip),
                new DockTabGroup(Array.Empty<DockableContent>(),
                    Role: DockGroupRole.DocumentArea),
                new DockTabGroup(new DockableContent[] { Tool("config", "config body") },
                    Width: 280, Role: DockGroupRole.ToolWindowStrip),
            });
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);
            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();
            await Harness.Render();

            var splitters = FindSplitters(H);
            var s1 = splitters[1];
            var panel = (FlexPanel)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(s1);
            var leftStrip = (FrameworkElement)panel.Children[0];
            var docArea = (FrameworkElement)panel.Children[2];
            var rightStrip = (FrameworkElement)panel.Children[4];

            var leftBefore = leftStrip.ActualWidth;
            var docBefore = docArea.ActualWidth;
            var rightBefore = rightStrip.ActualWidth;

            s1.BeginSimulatedDrag(new Point(0, 0));
            s1.EndSimulatedDrag(new Point(-50, 0));
            await Harness.Render();

            H.Check("L05_DocShrunkBy50", Near(docArea.ActualWidth, docBefore - 50));
            H.Check("L05_RightGrewBy50", Near(rightStrip.ActualWidth, rightBefore + 50));
            H.Check("L05_LeftUntouched", Near(leftStrip.ActualWidth, leftBefore));

        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  4 — Deep 5-level nested split
    //
    //  H ├ H ├ V ├ H ├ leaf (5 splits, 5 splitters). Drag the innermost
    //  splitter — only its two leaf neighbors resize.
    // ════════════════════════════════════════════════════════════════════

    internal class L06_DeepNested_DragInnermost_OnlyInnermostResizes(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DockNode innermost = new DockSplit(Orientation.Horizontal, new DockNode[]
            {
                Doc("inner-a", "a body"),
                Doc("inner-b", "b body"),
            });
            var l4 = new DockSplit(Orientation.Horizontal, new DockNode[] { Doc("l4", "l4 body"), innermost });
            var l3 = new DockSplit(Orientation.Vertical,   new DockNode[] { Doc("l3", "l3 body"), l4 });
            var l2 = new DockSplit(Orientation.Horizontal, new DockNode[] { Doc("l2", "l2 body"), l3 });
            var l1 = new DockSplit(Orientation.Vertical,   new DockNode[] { Doc("l1", "l1 body"), l2 });
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);
            host.Mount(_ => new DockManager { Layout = l1 });
            await Harness.Render();
            await Harness.Render();

            var splitters = FindSplitters(H);
            H.Check("L06_FiveSplitters", splitters.Count == 5);

            // Find the splitter whose immediate sibling TabView headers are
            // "inner-a" / "inner-b" — that's the innermost.
            // The innermost split is the deepest in the visual tree: walk
            // every splitter's ancestor chain and pick the one with the
            // most ancestors. (Equivalently: the last splitter in tree
            // order when traversal is pre-order from root.)
            DockSplitterControl? innerS = null;
            int innerDepth = -1;
            FlexPanel? innerP = null;
            foreach (var s in splitters)
            {
                int d = 0;
                Microsoft.UI.Xaml.DependencyObject? n = s;
                while ((n = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(n)) is not null) d++;
                if (d > innerDepth)
                {
                    innerDepth = d;
                    innerS = s;
                    innerP = (FlexPanel)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(s);
                }
            }
            H.Check("L06_InnermostFound", innerS is not null);
            if (innerS is null || innerP is null) return;

            int innerIdx = 0;
            for (int i = 0; i < innerP.Children.Count; i++)
                if (ReferenceEquals(innerP.Children[i], innerS)) { innerIdx = i; break; }
            var a = (FrameworkElement)innerP.Children[innerIdx - 1];
            var b = (FrameworkElement)innerP.Children[innerIdx + 1];
            bool isCols = innerS.Direction == DockSplitterDirection.Columns;
            double aBefore = isCols ? a.ActualWidth : a.ActualHeight;
            double bBefore = isCols ? b.ActualWidth : b.ActualHeight;

            // The innermost pane is narrow after 5 levels of nesting
            // (~84 DIPs each); use a delta small enough not to trip the
            // 60-DIP min clamp.
            const double dlta = 10;
            innerS.BeginSimulatedDrag(new Point(0, 0));
            innerS.EndSimulatedDrag(isCols ? new Point(dlta, 0) : new Point(0, dlta));
            await Harness.Render();

            double aAfter = isCols ? a.ActualWidth : a.ActualHeight;
            double bAfter = isCols ? b.ActualWidth : b.ActualHeight;
            H.Check("L06_InnerLeadingGrewByDelta", Near(aAfter, aBefore + dlta));
            H.Check("L06_InnerTrailingShrunkByDelta", Near(bAfter, bBefore - dlta));

        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  5 — Mixed Width hints (some panes hinted, some not)
    //
    //  Hits the EqualShare-fallback branch of BootstrapRatios. The drag
    //  must still produce 1:1 cursor follow.
    // ════════════════════════════════════════════════════════════════════

    internal class L07_MixedWidthHints_DragSplitter_LeadingTracksCursor(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var layout = new DockSplit(Orientation.Horizontal, new DockNode[]
            {
                new DockTabGroup(new DockableContent[] { Doc("a", "a body") }, Width: 200),
                new DockTabGroup(new DockableContent[] { Doc("b", "b body") }), // no hint
                new DockTabGroup(new DockableContent[] { Doc("c", "c body") }, Width: 250),
            });
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);
            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();
            await Harness.Render();

            var splitters = FindSplitters(H);
            var s = splitters[0];
            var p = (FlexPanel)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(s);
            var aBefore = ((FrameworkElement)p.Children[0]).ActualWidth;
            var bBefore = ((FrameworkElement)p.Children[2]).ActualWidth;

            s.BeginSimulatedDrag(new Point(0, 0));
            s.EndSimulatedDrag(new Point(35, 0));
            await Harness.Render();

            H.Check("L07_AGrewBy35", Near(((FrameworkElement)p.Children[0]).ActualWidth, aBefore + 35));
            H.Check("L07_BShrunkBy35", Near(((FrameworkElement)p.Children[2]).ActualWidth, bBefore - 35));
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  6 — Pointer vs. programmatic SplitRatios parity
    //
    //  Mount the same layout twice. In rig A, drive a pointer drag that
    //  pulls leading from 1/3 to ~1/2 of the pair. In rig B, write the
    //  equivalent ratios into a SplitRatios dictionary directly. Both
    //  paths must produce the same pane widths (within float tolerance).
    // ════════════════════════════════════════════════════════════════════

    internal class L08_PointerVsProgrammaticParity_ThreePaneEqualShare(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DockNode MakeLayout() => new DockSplit(Orientation.Horizontal, new DockNode[]
            {
                Doc("a", "a body"),
                Doc("b", "b body"),
                Doc("c", "c body"),
            });

            // --- Rig A: pointer drag ---
            var hostA = H.CreateHost();
            DockingNativeInterop.Register(hostA.Reconciler);
            hostA.Mount(_ => new DockManager { Layout = MakeLayout() });
            await Harness.Render();
            await Harness.Render();
            var splittersA = FindSplitters(H);
            var sA = splittersA[0];
            var pA = (FlexPanel)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(sA);
            sA.BeginSimulatedDrag(new Point(0, 0));
            sA.EndSimulatedDrag(new Point(60, 0));
            await Harness.Render();
            var aPointer = ((FrameworkElement)pA.Children[0]).ActualWidth;
            var bPointer = ((FrameworkElement)pA.Children[2]).ActualWidth;
            var cPointer = ((FrameworkElement)pA.Children[4]).ActualWidth;

            // --- Rig B: programmatic SplitRatios ---
            // Derive the ratios that should match Rig A's outcome. The
            // pointer drag added 60 DIPs to the leading pane in the pair
            // (a, b). Starting from 1/3 each, the new ratios are derived
            // by the same solver. We can compute what Rig B should pass
            // by reading totalPaneSpace = panelW - 2*handle.
            var panelW = pA.ActualWidth;
            var paneSpace = panelW - 2 * DockSplitterControl.HitThicknessDip;
            var initialShare = paneSpace / 3.0;
            // After drag: leading += 60, middle -= 60, trailing unchanged.
            var ratios = new Dictionary<string, double[]>
            {
                ["0"] = new[] {
                    (initialShare + 60) / paneSpace,
                    (initialShare - 60) / paneSpace,
                    initialShare / paneSpace,
                },
            };
            var hostB = H.CreateHost();
            DockingNativeInterop.Register(hostB.Reconciler);
            hostB.Mount(_ => new DockManager
            {
                Layout = MakeLayout(),
                SplitRatios = ratios,
            });
            await Harness.Render();
            await Harness.Render();

            var splittersB = FindSplitters(H);
            var pB = (FlexPanel)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(splittersB[0]);
            var aProg = ((FrameworkElement)pB.Children[0]).ActualWidth;
            var bProg = ((FrameworkElement)pB.Children[2]).ActualWidth;
            var cProg = ((FrameworkElement)pB.Children[4]).ActualWidth;

            // Both rigs should land at the same widths within ~1 DIP.
            H.Check("L08_Parity_A", Near(aPointer, aProg, tol: 1.5));
            H.Check("L08_Parity_B", Near(bPointer, bProg, tol: 1.5));
            H.Check("L08_Parity_C", Near(cPointer, cProg, tol: 1.5));

        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  7 — Independent drags across multiple splitters
    //
    //  4-pane horizontal. Drag splitter 0, then splitter 2, then splitter
    //  1. Each operation must affect only its own pair; previous drag
    //  results must persist.
    // ════════════════════════════════════════════════════════════════════

    internal class L09_FourPaneH_ThreeSequentialDrags_Compose(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var layout = new DockSplit(Orientation.Horizontal, new DockNode[]
            {
                Doc("a", "a"), Doc("b", "b"), Doc("c", "c"), Doc("d", "d"),
            });
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);
            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();
            await Harness.Render();

            var splitters = FindSplitters(H);
            H.Check("L09_ThreeSplitters", splitters.Count == 3);
            var panel = (FlexPanel)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(splitters[0]);
            FrameworkElement Pane(int i) => (FrameworkElement)panel.Children[i * 2];

            // 1) Drag s0 by +40 (a grows, b shrinks).
            var bBefore = Pane(1).ActualWidth;
            splitters[0].BeginSimulatedDrag(new Point(0, 0));
            splitters[0].EndSimulatedDrag(new Point(40, 0));
            await Harness.Render();
            var aAfter1 = Pane(0).ActualWidth;
            var bAfter1 = Pane(1).ActualWidth;
            H.Check("L09_S0Drag_AGrew", aAfter1 > bAfter1);

            // 2) Drag s2 by -25 (d grows, c shrinks).
            var cBefore = Pane(2).ActualWidth;
            var dBefore = Pane(3).ActualWidth;
            splitters[2].BeginSimulatedDrag(new Point(0, 0));
            splitters[2].EndSimulatedDrag(new Point(-25, 0));
            await Harness.Render();
            var cAfter2 = Pane(2).ActualWidth;
            var dAfter2 = Pane(3).ActualWidth;
            H.Check("L09_S2Drag_CShrunkAndDGrew",
                cAfter2 < cBefore - 1 && dAfter2 > dBefore + 1);

            // 3) Drag s1 by +30 (b grows, c shrinks). a and d must remain
            // at their post-step-2 widths.
            var aPreS1 = Pane(0).ActualWidth;
            var dPreS1 = Pane(3).ActualWidth;
            splitters[1].BeginSimulatedDrag(new Point(0, 0));
            splitters[1].EndSimulatedDrag(new Point(30, 0));
            await Harness.Render();

            H.Check("L09_S1Drag_AUnchanged", Near(Pane(0).ActualWidth, aPreS1));
            H.Check("L09_S1Drag_DUnchanged", Near(Pane(3).ActualWidth, dPreS1));

        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  8 — Mixed orientation (V outside, H inside): drag horizontal does
    //  not affect the vertical splitter's pane heights
    // ════════════════════════════════════════════════════════════════════

    internal class L10_MixedOrient_HorizontalDrag_VerticalUnaffected(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var layout = new DockSplit(Orientation.Vertical, new DockNode[]
            {
                new DockSplit(Orientation.Horizontal, new DockNode[]
                {
                    Doc("tl", "tl"), Doc("tr", "tr"),
                }),
                new DockSplit(Orientation.Horizontal, new DockNode[]
                {
                    Doc("bl", "bl"), Doc("br", "br"),
                }),
            });
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);
            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();
            await Harness.Render();

            var splitters = FindSplitters(H);
            var v = splitters.First(s => s.Direction == DockSplitterDirection.Rows);
            var hSplitters = splitters.Where(s => s.Direction == DockSplitterDirection.Columns).ToList();
            H.Check("L10_OneVertical", splitters.Count(s => s.Direction == DockSplitterDirection.Rows) == 1);
            H.Check("L10_TwoHorizontal", hSplitters.Count == 2);

            var vPanel = (FlexPanel)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(v);
            var topHalf = (FrameworkElement)vPanel.Children[0];
            var bottomHalf = (FrameworkElement)vPanel.Children[2];
            var topHBefore = topHalf.ActualHeight;
            var bottomHBefore = bottomHalf.ActualHeight;

            // Drag the top inner horizontal splitter by +50. The vertical
            // splitter's height ratio must not budge.
            hSplitters[0].BeginSimulatedDrag(new Point(0, 0));
            hSplitters[0].EndSimulatedDrag(new Point(50, 0));
            await Harness.Render();

            H.Check("L10_TopHeightUnchanged", Near(topHalf.ActualHeight, topHBefore));
            H.Check("L10_BottomHeightUnchanged", Near(bottomHalf.ActualHeight, bottomHBefore));

        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  11 — Scene-J south-drop end-to-end crash regression
    //
    //  Reproduces the user-reported crash: in the dock-showcase Scene J,
    //  open 2 documents in the middle DocumentArea, drag one to the south
    //  of the other. The drop's per-group overlay routes through
    //  MovePaneToGroupTarget(SplitBottom), and the resulting layout is
    //  fed back to the host via OnLiveLayoutChanged + a parent re-render.
    //  This fixture drives the full pipeline programmatically: build the
    //  3-column role-tagged layout, mount it through a real host, apply
    //  the south-drop transform, re-mount with the new layout, and assert
    //  the host renders without throwing + produces the nested vertical
    //  split with both documents addressable.
    // ════════════════════════════════════════════════════════════════════

    internal class L11_SceneJSouthDrop_LayoutTransformsAndRenders(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var doc1 = new Document { Title = "Document 1.md", Key = "j:doc:1", Content = TextBlock("doc 1 body") };
            var doc2 = new Document { Title = "Document 2.md", Key = "j:doc:2", Content = TextBlock("doc 2 body") };
            var galleryTool = new ToolWindow { Title = "Gallery", Key = "j:tool:gallery", Content = TextBlock("gallery") };
            var configTool  = new ToolWindow { Title = "Config",  Key = "j:tool:config",  Content = TextBlock("config") };

            var documentArea = new DockTabGroup(
                new DockableContent[] { doc1, doc2 },
                Role: DockGroupRole.DocumentArea);
            var initialLayout = new DockSplit(Orientation.Horizontal, new DockNode[]
            {
                new DockTabGroup(new DockableContent[] { galleryTool },
                    Width: 240, Role: DockGroupRole.ToolWindowStrip),
                documentArea,
                new DockTabGroup(new DockableContent[] { configTool },
                    Width: 280, Role: DockGroupRole.ToolWindowStrip),
            });

            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            // Use a mutable layout slot the test mounts with; the second
            // mount uses the transformed layout. Mirrors how Scene J's
            // OnLiveLayoutChanged + useState combination feeds the
            // mutator's output back to the host on the next render.
            DockNode currentLayout = initialLayout;
            host.Mount(_ => new DockManager { Layout = currentLayout });
            await Harness.Render();
            await Harness.Render();

            // Sanity check: 2 documents mounted as 2 tabs in a single TabView.
            var initialTabs = H.FindAllControls<TabView>(_ => true)
                .Where(tv => tv.TabItems.Count > 1)
                .ToList();
            H.Check("L11_InitialHasMultiTabGroup", initialTabs.Count == 1);

            // Apply the same transform the host's per-group OnConfirm
            // would: MovePaneToGroupTarget(doc1, documentArea, SplitBottom).
            var transformed = DockLayoutMutator.MovePaneToGroupTarget(
                initialLayout, doc1, documentArea, DockTarget.SplitBottom);
            H.Check("L11_MutatorReturnedNonNull", transformed is not null);
            if (transformed is null) return;

            // Re-mount with the new layout (simulates the parent re-render
            // that Scene J triggers via setLiveLayout).
            currentLayout = transformed;
            try
            {
                host.Mount(_ => new DockManager { Layout = currentLayout });
                await Harness.Render();
                await Harness.Render();
                H.Check("L11_RemountedWithoutThrowing", true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"# L11 mount-after-transform threw: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"# L11 stack: {ex.StackTrace}");
                H.Check("L11_RemountedWithoutThrowing", false);
                return;
            }

            // Post-transform structure: two TabViews each with a single
            // tab (doc1, doc2), plus tabs for the two tool windows.
            // 4 TabViews total. The middle column has a FlexPanel with 2
            // TabViews + 1 splitter inside.
            var tabViews = H.FindAllControls<TabView>(_ => true);
            H.Check("L11_FourTabViews", tabViews.Count == 4);

            // Doc1 and Doc2 must each be mounted as a tab on its own TabView.
            int doc1Found = 0, doc2Found = 0;
            foreach (var tv in tabViews)
            {
                foreach (var item in tv.TabItems)
                {
                    if (item is TabViewItem ti && ti.Header is string h)
                    {
                        if (h == "Document 1.md") doc1Found++;
                        if (h == "Document 2.md") doc2Found++;
                    }
                }
            }
            H.Check("L11_Doc1MountedExactlyOnce", doc1Found == 1);
            H.Check("L11_Doc2MountedExactlyOnce", doc2Found == 1);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  12 — DockDragSession leak across host unmount (scene-switch repro)
    //
    //  User-reported pattern: in the dock-showcase, alternate Scene J →
    //  Scene A → Scene J → Scene A → … After 2-3 cycles, Scene J's tab
    //  drag stops working — Begin returns null because the static
    //  DockDragSession.Current.IsActive is still true from a previous
    //  drag whose host was unmounted before the drag ended.
    //
    //  DockDragSession.Current is process-static. DockHostNativeComponent
    //  registers a SessionChanged subscription via UseEffect, but its
    //  cleanup only unhooks the handler — it doesn't cancel the session
    //  itself. So a host that unmounts mid-drag (or with a session in a
    //  weird intermediate state) leaks the static slot.
    //
    //  Fix: cleanup also cancels Current when its SourceManager refers to
    //  the manager that's being unmounted. This fixture exercises that.
    // ════════════════════════════════════════════════════════════════════

    internal class L12_HostUnmount_CancelsOwnedDragSession(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Reset to a known clean state.
            DockDragSession.ResetForTest();

            var docA = new Document { Title = "A", Key = "L12:a", Content = TextBlock("a") };
            var docB = new Document { Title = "B", Key = "L12:b", Content = TextBlock("b") };
            var layoutA = new DockTabGroup(new DockableContent[] { docA, docB });

            // Capture the manager so we can simulate Begin against it
            // exactly the way HandleTabDragStarting would.
            DockManager? managerA = null;
            var hostA = H.CreateHost();
            DockingNativeInterop.Register(hostA.Reconciler);
            hostA.Mount(_ => managerA = new DockManager { Layout = layoutA });
            await Harness.Render();
            await Harness.Render();
            H.Check("L12_ManagerCaptured", managerA is not null);

            // Simulate the production drag-start path: HandleTabDragStarting
            // calls DockDragSession.Begin(pane, manager, tabIndex, owner: model).
            // We fetch the host's stable model via the bridge so the OwnerToken
            // matches what the host's UseEffect cleanup looks for.
            var hostModel = DockHostModelBridge.Get(managerA!);
            H.Check("L12_HostModelAvailable", hostModel is not null);
            var session = DockDragSession.Begin(docA, managerA!, 0, owner: hostModel);
            H.Check("L12_DragBegan", session is { IsActive: true });
            H.Check("L12_StaticIsActive_BeforeUnmount",
                DockDragSession.Current is { IsActive: true });

            // User switches scenes — the host is disposed. Simulates Scene
            // J's component unmounting when the user clicks Scene A.
            hostA.Dispose();
            await Harness.Render();
            await Harness.Render();

            // CONTRACT: the host's UseEffect cleanup must cancel any
            // session whose SourceManager belongs to it. Without the fix,
            // Current.IsActive stays true forever, and the next
            // DockDragSession.Begin call (in any future host) returns null
            // because the static slot is occupied.
            H.Check("L12_SessionCancelledOnUnmount",
                DockDragSession.Current is null || !DockDragSession.Current.IsActive);

            // Concretely demonstrate the user-visible symptom: try to
            // start a new drag now. Without the fix this returns null
            // (silently refused); with the fix it returns a fresh
            // session.
            var docC = new Document { Title = "C", Key = "L12:c", Content = TextBlock("c") };
            var managerB = new DockManager { Layout = new DockTabGroup(new DockableContent[] { docC }) };
            var nextSession = DockDragSession.Begin(docC, managerB, 0);
            H.Check("L12_NextDragBeginSucceeds", nextSession is { IsActive: true });

            // Cleanup for subsequent fixtures.
            DockDragSession.ResetForTest();
        }
    }
}
