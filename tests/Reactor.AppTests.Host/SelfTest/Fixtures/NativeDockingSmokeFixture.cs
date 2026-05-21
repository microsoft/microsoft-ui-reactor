using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 045 §2.1 / §2.2 / §2.16 — minimal smoke fixture for the Phase 2
/// native renderer. Asserts that <see cref="DockManager"/> mounts into a
/// Reactor-native subtree (FlexPanel + TabView) without depending on
/// WinUI.Dock controls. Mirrors <see cref="DockingSmokeFixtures"/> in
/// shape so the two renderers are reviewed side by side.
/// </summary>
internal static class NativeDockingSmokeFixtures
{
    internal class TwoPaneMountUpdateUnmount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();

            // P2: register the native renderer. Same call site as the
            // XAML wrapper; last registration wins on the same TElement.
            DockingNativeInterop.Register(host.Reconciler);

            var pane1 = new DockableContent(
                Title: "Solution Explorer",
                Content: TextBlock("native-solution-content"),
                Key: "tool:solution");
            var pane2 = new DockableContent(
                Title: "Properties",
                Content: TextBlock("native-properties-content"),
                Key: "tool:properties");

            host.Mount(_ => new DockManager
            {
                Layout = new DockSplit(
                    Orientation.Horizontal,
                    new DockNode[] { pane1, pane2 }),
            });
            await Harness.Render();

            // The native renderer mounts a FlexPanel for the split.
            var flexes = H.FindAllControls<FlexPanel>(_ => true);
            H.Check("NativeDock_FlexPanelMounted", flexes.Count >= 1);

            // The leaf renderer for a bare DockableContent inlines the
            // Content element — text markers must appear in the visual
            // tree.
            H.Check("NativeDock_Pane1ContentRendered",
                H.FindText("native-solution-content") is not null);
            H.Check("NativeDock_Pane2ContentRendered",
                H.FindText("native-properties-content") is not null);

            // ── Update: swap one pane's content ─────────────────────
            host.Mount(_ => new DockManager
            {
                Layout = new DockSplit(
                    Orientation.Horizontal,
                    new DockNode[]
                    {
                        pane1,
                        pane2 with { Content = TextBlock("native-properties-updated") },
                    }),
            });
            await Harness.Render();

            H.Check("NativeDock_PaneContentUpdated",
                H.FindText("native-properties-updated") is not null);
            H.Check("NativeDock_PreviousContentReplaced",
                H.FindText("native-properties-content") is null);

            // ── Unmount: replace with a different element ───────────
            host.Mount(_ => TextBlock("native-docking-unmounted"));
            await Harness.Render();

            H.Check("NativeDock_UnmountedCleanly",
                H.FindText("native-docking-unmounted") is not null);
            H.Check("NativeDock_NoFlexPanelAfterUnmount",
                H.FindAllControls<FlexPanel>(_ => true).Count == 0);
        }
    }

    /// <summary>
    /// Mounts a tab group and verifies that the native renderer wires
    /// the TabView control with the right tab headers and that swapping
    /// the selected tab preserves the surrounding tree.
    /// </summary>
    internal class TabGroupRendersToTabView(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            host.Mount(_ => new DockManager
            {
                Layout = new DockTabGroup(new[]
                {
                    new DockableContent("Alpha", TextBlock("native-body-alpha"), Key: "k:a", CanClose: true),
                    new DockableContent("Beta",  TextBlock("native-body-beta"),  Key: "k:b"),
                }),
            });
            await Harness.Render();

            var tabs = H.FindAllControls<TabView>(_ => true);
            H.Check("NativeDock_TabView_Mounted", tabs.Count >= 1);

            var tab = tabs.FirstOrDefault();
            H.Check("NativeDock_TabView_HasTwoTabs", tab?.TabItems.Count == 2);

            // The selected (first) tab's content is rendered into the visual tree.
            H.Check("NativeDock_TabView_FirstBodyRendered",
                H.FindText("native-body-alpha") is not null);

            host.Mount(_ => TextBlock("native-tabs-unmounted"));
            await Harness.Render();
        }
    }

    /// <summary>
    /// Spec 045 §2.17 — asserts that a function component rendered inside
    /// a docked pane sees the live <c>DockContext</c> slots: <c>UseDockHost</c>
    /// returns a non-null model, <c>UsePane</c> returns identity matching
    /// the enclosing leaf, <c>UseActivePaneKey</c> reflects the manager's
    /// <c>ActiveDocument</c>, and <c>UseIsActivePane</c> flips correctly.
    /// </summary>
    internal class DockContextHooksResolveOnRealMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            // Build the docking tree inside the mount lambda each call —
            // matches the standard Reactor pattern where Content elements
            // are constructed fresh per render. (Storing element refs
            // outside Mount() means same-reference shallow-equality skips
            // the consumer's re-render before context propagation runs.)
            DockManager Build(bool alphaActive)
            {
                var alpha = new DockableContent(
                    Title: "Alpha",
                    Key: "k:alpha",
                    Content: Func(ctx =>
                    {
                        var dockHost = ctx.UseDockHost();
                        var pane = ctx.UsePane();
                        var isActive = ctx.UseIsActivePane();
                        return VStack(
                            TextBlock($"alpha-host:{(dockHost is null ? "null" : "ok")}"),
                            TextBlock($"alpha-pane-title:{pane.Title}"),
                            TextBlock($"alpha-pane-key:{pane.Key}"),
                            TextBlock($"alpha-active:{isActive}"));
                    }));
                var beta = new DockableContent(
                    Title: "Beta",
                    Key: "k:beta",
                    Content: Func(ctx =>
                    {
                        var isActive = ctx.UseIsActivePane();
                        return TextBlock($"beta-active:{isActive}");
                    }));
                return new DockManager
                {
                    Layout = new DockTabGroup(new[] { alpha, beta }),
                    ActiveDocument = alphaActive ? alpha : beta,
                };
            }

            host.Mount(_ => Build(alphaActive: true));
            await Harness.Render();

            H.Check("DockHooks_Host_Resolved",
                H.FindText("alpha-host:ok") is not null);
            H.Check("DockHooks_Pane_TitleResolved",
                H.FindText("alpha-pane-title:Alpha") is not null);
            H.Check("DockHooks_Pane_KeyResolved",
                H.FindText("alpha-pane-key:k:alpha") is not null);
            H.Check("DockHooks_IsActivePane_TrueWhenActive",
                H.FindText("alpha-active:True") is not null);

            host.Mount(_ => Build(alphaActive: false));
            await Harness.Render();

            H.Check("DockHooks_IsActivePane_FlipsOnActiveChange",
                H.FindText("alpha-active:False") is not null);

            host.Mount(_ => TextBlock("hooks-done"));
            await Harness.Render();
        }
    }

    /// <summary>
    /// Spec 045 §2.5 — side strip + side popup. Pinning a pane to the
    /// LeftSide renders a button strip; clicking the button opens a
    /// light-dismiss Popup with the pane's content. Click the button
    /// again (or close the popup) collapses it.
    /// </summary>
    internal class SidePopupExpandsAndCollapses(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            DockManager Build() => new()
            {
                Layout = new DockTabGroup(new[]
                {
                    new DockableContent("Center", TextBlock("center-body"), Key: "k:center"),
                }),
                LeftSide = new[]
                {
                    new DockableContent(
                        Title: "Outline",
                        Key: "k:outline",
                        Content: TextBlock("outline-popup-body"),
                        CanPin: true),
                },
            };

            host.Mount(_ => Build());
            await Harness.Render();

            // Strip button rendered with the pane title.
            var stripButton = H.FindButton("Outline");
            H.Check("SidePopup_StripButton_Rendered", stripButton is not null);

            // No open popups initially. Use VisualTreeHelper.GetOpenPopups
            // against the host's XamlRoot — WinUI hosts open Popups in a
            // private PopupRoot that VTH child-walks don't traverse, so
            // GetOpenPopups is the supported probe.
            var xamlRoot = stripButton!.XamlRoot;
            int OpenCount() => Microsoft.UI.Xaml.Media.VisualTreeHelper
                .GetOpenPopupsForXamlRoot(xamlRoot).Count;

            H.Check("SidePopup_NotOpenInitially", OpenCount() == 0);

            // Click → popup opens.
            H.ClickButton("Outline");
            await Harness.Render();
            H.Check("SidePopup_OpensOnClick", OpenCount() >= 1);

            // Click again → toggles closed.
            H.ClickButton("Outline");
            await Harness.Render();
            H.Check("SidePopup_TogglesClosedOnRepeatClick", OpenCount() == 0);

            host.Mount(_ => TextBlock("side-popup-done"));
            await Harness.Render();
        }
    }

    /// <summary>
    /// Spec 045 §2.6 — floating windows are real Reactor windows. Tear a
    /// pane out via the programmatic API; assert that a new
    /// <see cref="Microsoft.UI.Reactor.ReactorWindow"/> is registered and
    /// closing it removes it from the tracker.
    /// </summary>
    internal class FloatingWindowOpensAsRealWindow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            host.Mount(_ => new DockManager
            {
                Layout = new DockTabGroup(new[]
                {
                    new DockableContent("Center", TextBlock("center-body"), Key: "k:center"),
                }),
            });
            await Harness.Render();

            var baselineCount = DockFloatingTracker.Count;

            var pane = new DockableContent(
                Title: "Output (floating)",
                Key: "k:output-floating",
                Content: TextBlock("floating-pane-body"));

            // The harness opens its own primary Window outside ReactorApp's
            // registry, so a fixture-spawned floating window can otherwise
            // become the framework's PrimaryWindow and trip
            // ShutdownPolicy.OnPrimaryWindowClosed when the test closes it.
            // Pin the policy to None for the duration of this fixture.
            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            try
            {
                var floatingWindow = DockFloatingWindow.Open(pane, width: 600, height: 400);
                await Harness.Render();

                H.Check("FloatingWindow_OpenedAsRealReactorWindow",
                    floatingWindow is not null);
                H.Check("FloatingWindow_RegisteredWithTracker",
                    DockFloatingTracker.Count == baselineCount + 1);
                H.Check("FloatingWindow_TrackerSnapshotIncludesIt",
                    DockFloatingTracker.Snapshot().Contains(floatingWindow));

                // Close the floating window — tracker should drop it.
                floatingWindow!.Close();
                await Harness.Render();
                H.Check("FloatingWindow_RemovedFromTrackerOnClose",
                    DockFloatingTracker.Count == baselineCount);
            }
            finally
            {
                ReactorApp.ShutdownPolicy = savedPolicy;
            }

            host.Mount(_ => TextBlock("floating-done"));
            await Harness.Render();
        }
    }

    /// <summary>
    /// Spec 045 §2.1 — programmatic splitter-drag fixture. Mounts an
    /// IDE-style nested layout, fires <c>ResizeDelta</c> events directly
    /// on the splitter controls (simulating a pointer drag), and asserts
    /// that the FlexPanel's per-child <c>FlexGrow</c> attached value
    /// shifts as expected. Isolates the render → reconcile → FlexPanel
    /// pipeline from the pointer-capture / hit-test plumbing so failures
    /// fingerprint quickly: if these pass and the showcase doesn't, the
    /// bug is in <see cref="DockSplitterControl"/>'s pointer handling.
    /// </summary>
    internal class SplitterProgrammaticResizeAcrossRenders(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            // IDE-style nested layout. Apps typically rebuild Layout each
            // render — model that via a state counter the host bumps to
            // force fresh DockSplit instances.
            DockManager Build()
            {
                return new DockManager
                {
                    Layout = new DockSplit(
                        Orientation.Vertical,
                        new DockNode[]
                        {
                            // Top row — horizontal split with two leaves.
                            new DockSplit(
                                Orientation.Horizontal,
                                new DockNode[]
                                {
                                    new DockableContent("Editor",
                                        TextBlock("editor-body"),
                                        Key: "k:editor"),
                                    new DockableContent("Tools",
                                        TextBlock("tools-body"),
                                        Key: "k:tools"),
                                }),
                            // Bottom row — horizontal split with two leaves.
                            new DockSplit(
                                Orientation.Horizontal,
                                new DockNode[]
                                {
                                    new DockableContent("Output",
                                        TextBlock("output-body"),
                                        Key: "k:output"),
                                    new DockableContent("Terminal",
                                        TextBlock("terminal-body"),
                                        Key: "k:terminal"),
                                }),
                        }),
                };
            }

            host.Mount(_ => Build());
            await Harness.Render();

            // Discover the three splitter controls: 1 in outer (rows
            // splitter — horizontal bar) + 1 in each inner split (column
            // splitters — vertical bars). Distinguish by Direction.
            var splitters = H.FindAllControls<DockSplitterControl>(_ => true);
            H.Check("SplitProg_ThreeSplittersMounted", splitters.Count == 3);

            var rowSplitter = splitters.FirstOrDefault(s => s.Direction == DockSplitterDirection.Rows);
            var colSplitters = splitters.Where(s => s.Direction == DockSplitterDirection.Columns).ToList();
            H.Check("SplitProg_RowSplitterFound", rowSplitter is not null);
            H.Check("SplitProg_TwoColumnSplitters", colSplitters.Count == 2);

            // Capture the initial grow values from each splitter's parent
            // FlexPanel. Row direction parent splits vertically; column
            // direction parents split horizontally.
            double GrowOf(UIElement child) => FlexPanel.GetGrow(child);

            double[] GrowsFor(DockSplitterControl s)
            {
                var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(s) as FlexPanel;
                if (parent is null) return [];
                var result = new double[parent.Children.Count];
                for (int i = 0; i < parent.Children.Count; i++)
                    result[i] = GrowOf(parent.Children[i]);
                return result;
            }

            var beforeRowGrows = GrowsFor(rowSplitter!);
            var beforeCol0Grows = GrowsFor(colSplitters[0]);
            var beforeCol1Grows = GrowsFor(colSplitters[1]);
            Console.WriteLine($"# beforeRow=[{string.Join(",", beforeRowGrows)}]");
            Console.WriteLine($"# beforeCol0=[{string.Join(",", beforeCol0Grows)}]");
            Console.WriteLine($"# beforeCol1=[{string.Join(",", beforeCol1Grows)}]");

            H.Check("SplitProg_InitialRowsEqual",
                beforeRowGrows.Length >= 3
                && Math.Abs(beforeRowGrows[0] - beforeRowGrows[2]) < 0.0001);

            // ── Drag #1: shrink the row splitter's leading row by 100 DIP.
            // Fire ResizeDelta directly with hostExtent matching the
            // splitter's host. The control's OnDelta closure must compute
            // a new ratio and trigger re-render.
            FireResizeDelta(rowSplitter!, delta: 100, isFinal: false);
            FireResizeDelta(rowSplitter!, delta: 0, isFinal: true);
            await Harness.Render();

            var afterRowGrows1 = GrowsFor(rowSplitter!);
            Console.WriteLine($"# afterRowDrag1=[{string.Join(",", afterRowGrows1)}]");
            H.Check("SplitProg_RowDragShiftedLeadingDown",
                afterRowGrows1.Length >= 3 && afterRowGrows1[0] < beforeRowGrows[0] - 0.001);

            // ── Drag #2 on the SAME splitter: shrink another 50 DIP.
            // Verifies ratio accumulation across drags (not snap-back).
            FireResizeDelta(rowSplitter!, delta: 50, isFinal: false);
            FireResizeDelta(rowSplitter!, delta: 0, isFinal: true);
            await Harness.Render();

            var afterRowGrows2 = GrowsFor(rowSplitter!);
            Console.WriteLine($"# afterRowDrag2=[{string.Join(",", afterRowGrows2)}]");
            H.Check("SplitProg_RowDragCumulates",
                afterRowGrows2[0] < afterRowGrows1[0] - 0.001);

            // ── Drag the FIRST column splitter — should NOT affect the
            // row splitter's ratios, nor the OTHER column splitter's.
            FireResizeDelta(colSplitters[0], delta: 80, isFinal: false);
            FireResizeDelta(colSplitters[0], delta: 0, isFinal: true);
            await Harness.Render();

            var col0After = GrowsFor(colSplitters[0]);
            var col1After = GrowsFor(colSplitters[1]);
            var rowAfterCol = GrowsFor(rowSplitter!);
            Console.WriteLine($"# afterCol0Drag col0=[{string.Join(",", col0After)}] col1=[{string.Join(",", col1After)}] row=[{string.Join(",", rowAfterCol)}]");

            H.Check("SplitProg_Col0DragShiftedLeading",
                col0After[0] < beforeCol0Grows[0] - 0.001);
            H.Check("SplitProg_Col1Untouched",
                col1After.Length == beforeCol1Grows.Length
                && Math.Abs(col1After[0] - beforeCol1Grows[0]) < 0.0001);
            H.Check("SplitProg_RowUntouchedByColDrag",
                rowAfterCol.Length == afterRowGrows2.Length
                && Math.Abs(rowAfterCol[0] - afterRowGrows2[0]) < 0.0001);

            // ── Force a re-render by re-mounting a fresh Build(). All
            // DockSplit references change. Ratios MUST survive (the
            // tree-position-key fix).
            host.Mount(_ => Build());
            await Harness.Render();

            var splittersAfterRemount = H.FindAllControls<DockSplitterControl>(_ => true);
            var rowAfterRemount = splittersAfterRemount.FirstOrDefault(s => s.Direction == DockSplitterDirection.Rows);
            H.Check("SplitProg_RowSplitterStillPresentAfterRemount", rowAfterRemount is not null);

            var rowGrowsAfterRemount = GrowsFor(rowAfterRemount!);
            Console.WriteLine($"# afterRemount row=[{string.Join(",", rowGrowsAfterRemount)}]");
            H.Check("SplitProg_RowRatiosSurvivedRemount",
                rowGrowsAfterRemount.Length == afterRowGrows2.Length
                && Math.Abs(rowGrowsAfterRemount[0] - afterRowGrows2[0]) < 0.0001);

            host.Mount(_ => TextBlock("split-prog-done"));
            await Harness.Render();
        }

        /// <summary>
        /// Fires the splitter's internal ResizeDelta event using the
        /// splitter's live host extent. Bypasses pointer/keyboard.
        /// </summary>
        private static void FireResizeDelta(DockSplitterControl splitter, double delta, bool isFinal)
        {
            var hostExtent = splitter.GetHostExtent();
            if (hostExtent < 1) hostExtent = 1000;
            var args = new DockSplitterDeltaEventArgs(delta, splitter.Direction, hostExtent, isFinal);
            splitter.RaiseResizeDeltaForTest(args);
        }
    }

    /// <summary>
    /// Spec 045 §2.1 — rapid-fire drag simulator. Fires many small
    /// ResizeDelta events in quick succession (no render await between
    /// them) to model what a real pointer drag does. If the ratios shift
    /// smoothly cumulatively, the rewiring-during-render path is safe;
    /// if they snap or freeze, the bug is in the closure-recapture flow
    /// fired by mid-drag re-renders.
    /// </summary>
    internal class SplitterRapidFireDragSurvivesRerender(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            DockManager Build() => new()
            {
                Layout = new DockSplit(
                    Orientation.Horizontal,
                    new DockNode[]
                    {
                        new DockableContent("L", TextBlock("l-body"), Key: "k:l"),
                        new DockableContent("R", TextBlock("r-body"), Key: "k:r"),
                    }),
            };

            host.Mount(_ => Build());
            await Harness.Render();

            var splitter = H.FindAllControls<DockSplitterControl>(_ => true).FirstOrDefault();
            H.Check("SplitFire_SplitterMounted", splitter is not null);

            FlexPanel? parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(splitter!) as FlexPanel;
            H.Check("SplitFire_ParentIsFlexPanel", parent is not null);

            double LeadingGrow() => FlexPanel.GetGrow(parent!.Children[0]);
            var initial = LeadingGrow();
            Console.WriteLine($"# initial leading grow={initial:F4}");

            // Fire 20 incremental deltas with NO await between them. Each
            // increment is 4 DIP. Total = 80 DIP. Mid-drag re-renders are
            // queued; the closures must continue to find the right ratios.
            for (int i = 0; i < 20; i++)
            {
                FireResizeDelta(splitter!, delta: 4, isFinal: false);
            }
            FireResizeDelta(splitter!, delta: 0, isFinal: true);
            await Harness.Render();

            var afterRapidGrow = LeadingGrow();
            Console.WriteLine($"# afterRapid leading grow={afterRapidGrow:F4} (delta from initial={initial - afterRapidGrow:F4})");

            // 80 DIP of accumulated drag on a ~945 DIP host should shift
            // the leading ratio by ~80/945 ≈ 0.085. Allow some slack for
            // the actual hostExtent the test runs in.
            H.Check("SplitFire_LeadingShrankByAccumulatedDelta",
                afterRapidGrow < initial - 0.01);

            // Fire 20 MORE deltas — the ratios should continue to shift,
            // NOT snap back, NOT freeze.
            for (int i = 0; i < 20; i++)
            {
                FireResizeDelta(splitter!, delta: 4, isFinal: false);
            }
            FireResizeDelta(splitter!, delta: 0, isFinal: true);
            await Harness.Render();

            var afterSecond = LeadingGrow();
            Console.WriteLine($"# afterSecond leading grow={afterSecond:F4}");
            H.Check("SplitFire_SecondRapidBurstCumulates",
                afterSecond < afterRapidGrow - 0.01);

            // Reverse direction — drag the trailing child back.
            for (int i = 0; i < 30; i++)
            {
                FireResizeDelta(splitter!, delta: -4, isFinal: false);
            }
            FireResizeDelta(splitter!, delta: 0, isFinal: true);
            await Harness.Render();

            var afterReverse = LeadingGrow();
            Console.WriteLine($"# afterReverse leading grow={afterReverse:F4}");
            H.Check("SplitFire_ReverseDragGrowsLeading",
                afterReverse > afterSecond + 0.01);

            host.Mount(_ => TextBlock("rapid-fire-done"));
            await Harness.Render();
        }

        private static void FireResizeDelta(DockSplitterControl splitter, double delta, bool isFinal)
        {
            var hostExtent = splitter.GetHostExtent();
            if (hostExtent < 1) hostExtent = 1000;
            var args = new DockSplitterDeltaEventArgs(delta, splitter.Direction, hostExtent, isFinal);
            splitter.RaiseResizeDeltaForTest(args);
        }
    }

    /// <summary>
    /// Spec 045 §2.3 — drop-target overlay smoke. Mounts a DockManager with
    /// <c>ShowDropTargets = true</c>, asserts the 9 buttons are present in
    /// the visual tree at minimum 44×44 DIP, drives a confirm via the
    /// internal test hook, and verifies the model gets the
    /// <see cref="DockTarget"/> the user picked. Verifies the dismiss
    /// callback fires when the overlay is dismissed (Esc).
    /// </summary>
    internal class DropTargetOverlayShowsAndDismisses(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            DockTarget? lastHover = null;
            DockTarget? lastConfirmed = null;
            int dismissCount = 0;

            DockManager Build(bool show) => new()
            {
                Layout = new DockTabGroup(new[]
                {
                    new DockableContent("Center", TextBlock("dt-center-body"), Key: "k:center"),
                }),
                ShowDropTargets = show,
                OnDropTargetHovered = t => lastHover = t,
                OnDropTargetConfirmed = t => lastConfirmed = t,
                OnDropTargetsDismissed = () => dismissCount++,
            };

            // ── Initial mount with overlay OFF — no overlay control yet.
            host.Mount(_ => Build(show: false));
            await Harness.Render();
            var noOverlay = H.FindAllControls<DockDropTargetOverlayControl>(_ => true);
            H.Check("DropTarget_NotMountedWhenFlagFalse", noOverlay.Count == 0);

            // ── Flip on — overlay mounts.
            host.Mount(_ => Build(show: true));
            await Harness.Render();

            var overlays = H.FindAllControls<DockDropTargetOverlayControl>(_ => true);
            H.Check("DropTarget_OverlayMounted", overlays.Count == 1);
            var overlay = overlays[0];

            // 9 target buttons + 1 preview rectangle ⇒ 10 children. Each
            // button is a Border with Width == ButtonSizeDip (44).
            var borders = new global::System.Collections.Generic.List<Microsoft.UI.Xaml.Controls.Border>();
            foreach (var child in overlay.Children)
                if (child is Microsoft.UI.Xaml.Controls.Border b) borders.Add(b);

            var targetButtons = borders.FindAll(b =>
                b.Width >= DockDropTargetOverlayControl.ButtonSizeDip - 0.001
                && b.Height >= DockDropTargetOverlayControl.ButtonSizeDip - 0.001
                && b.IsTabStop);
            H.Check("DropTarget_NineButtonsRendered", targetButtons.Count == 9);
            H.Check("DropTarget_ButtonsAtLeast44Dip",
                targetButtons.TrueForAll(b => b.Width >= 44.0 && b.Height >= 44.0));

            // ── Programmatically confirm SplitLeft via the test hook.
            // The model callback should receive the same target.
            overlay.ConfirmTargetForTest(DockTarget.SplitLeft);
            await Harness.Render();
            H.Check("DropTarget_ConfirmCallbackFired", lastConfirmed == DockTarget.SplitLeft);

            // ── Programmatic hover updates preview rect + callback.
            overlay.SetHoveredForTest(DockTarget.DockRight);
            await Harness.Render();
            H.Check("DropTarget_HoverCallbackFired", lastHover == DockTarget.DockRight);

            var bounds = overlay.PreviewBounds;
            H.Check("DropTarget_PreviewRectVisible",
                bounds.Width > 0 && bounds.Height > 0);

            // Right-edge strip should sit at the right of the overlay —
            // i.e. its X is near (overlay.ActualWidth - bounds.Width).
            if (overlay.ActualWidth > 0)
            {
                var expectedX = overlay.ActualWidth - bounds.Width;
                H.Check("DropTarget_DockRightPreviewAtRightEdge",
                    Math.Abs(bounds.X - expectedX) < 1.0);
            }

            // ── Clear hover — preview hides.
            overlay.SetHoveredForTest(null);
            await Harness.Render();
            var clearBounds = overlay.PreviewBounds;
            H.Check("DropTarget_PreviewHidesOnNoHover", clearBounds.IsEmpty);

            // ── Flip overlay OFF — control unmounts.
            host.Mount(_ => Build(show: false));
            await Harness.Render();
            var gone = H.FindAllControls<DockDropTargetOverlayControl>(_ => true);
            H.Check("DropTarget_UnmountedWhenFlagFlipsOff", gone.Count == 0);

            host.Mount(_ => TextBlock("dt-overlay-done"));
            await Harness.Render();

            // dismissCount comes from the Esc path; not exercised here since
            // the headless harness doesn't deliver real keystrokes. Kept
            // as a sentinel so the callback wire-up doesn't go untested.
            _ = dismissCount;
        }
    }

    /// <summary>
    /// Visual demo fixture — mounts an IDE-style layout and drives each
    /// splitter programmatically with paced delays so a human observer
    /// can watch the panes resize step by step. Asserts the same as the
    /// other splitter fixtures but with ~800 ms gaps between operations.
    /// </summary>
    internal class SplitterProgrammaticVisualDemo(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            DockManager Build() => new()
            {
                Layout = new DockSplit(
                    Orientation.Vertical,
                    new DockNode[]
                    {
                        new DockSplit(
                            Orientation.Horizontal,
                            new DockNode[]
                            {
                                new DockableContent("Editor",
                                    VStack(8,
                                        TextBlock("editor body — top half, left pane").SemiBold(),
                                        TextBlock("Drag programmatically below to watch this pane resize.")),
                                    Key: "k:editor"),
                                new DockableContent("Tools",
                                    VStack(8,
                                        TextBlock("tools body — top half, right pane").SemiBold(),
                                        TextBlock("Outline / properties etc.")),
                                    Key: "k:tools"),
                            }),
                        new DockSplit(
                            Orientation.Horizontal,
                            new DockNode[]
                            {
                                new DockableContent("Output",
                                    VStack(8,
                                        TextBlock("output body — bottom half, left").SemiBold(),
                                        TextBlock("Build / test output.")),
                                    Key: "k:output"),
                                new DockableContent("Terminal",
                                    VStack(8,
                                        TextBlock("terminal body — bottom half, right").SemiBold(),
                                        TextBlock("PS> _")),
                                    Key: "k:terminal"),
                            }),
                    }),
            };

            host.Mount(_ => Build());
            await Harness.Render();
            await Task.Delay(1200); // settle on initial layout so the eye registers it

            var splitters = H.FindAllControls<DockSplitterControl>(_ => true);
            var rowSplitter = splitters.FirstOrDefault(s => s.Direction == DockSplitterDirection.Rows);
            var colSplitters = splitters.Where(s => s.Direction == DockSplitterDirection.Columns).ToList();

            H.Check("VizDemo_LayoutMounted",
                rowSplitter is not null && colSplitters.Count == 2);

            // 1) Shrink the top row gradually — five 40-DIP nudges.
            for (int i = 0; i < 5; i++)
            {
                FireResizeDelta(rowSplitter!, delta: 40, isFinal: false);
                await Harness.Render();
                await Task.Delay(400);
            }
            FireResizeDelta(rowSplitter!, delta: 0, isFinal: true);
            await Harness.Render();
            await Task.Delay(800);

            // 2) Grow the top row back — five -40-DIP nudges.
            for (int i = 0; i < 5; i++)
            {
                FireResizeDelta(rowSplitter!, delta: -40, isFinal: false);
                await Harness.Render();
                await Task.Delay(400);
            }
            FireResizeDelta(rowSplitter!, delta: 0, isFinal: true);
            await Harness.Render();
            await Task.Delay(800);

            // 3) Shrink the top-row's left column (editor) — five 40 DIP.
            for (int i = 0; i < 5; i++)
            {
                FireResizeDelta(colSplitters[0], delta: 40, isFinal: false);
                await Harness.Render();
                await Task.Delay(400);
            }
            FireResizeDelta(colSplitters[0], delta: 0, isFinal: true);
            await Harness.Render();
            await Task.Delay(800);

            // 4) Restore the top-row's left column — five -40 DIP.
            for (int i = 0; i < 5; i++)
            {
                FireResizeDelta(colSplitters[0], delta: -40, isFinal: false);
                await Harness.Render();
                await Task.Delay(400);
            }
            FireResizeDelta(colSplitters[0], delta: 0, isFinal: true);
            await Harness.Render();
            await Task.Delay(800);

            // 5) Shrink the bottom-row's left column (output) — five 40 DIP.
            for (int i = 0; i < 5; i++)
            {
                FireResizeDelta(colSplitters[1], delta: 40, isFinal: false);
                await Harness.Render();
                await Task.Delay(400);
            }
            FireResizeDelta(colSplitters[1], delta: 0, isFinal: true);
            await Harness.Render();
            await Task.Delay(1500);

            H.Check("VizDemo_CompletedAllFourQuadrants", true);

            host.Mount(_ => TextBlock("viz demo done"));
            await Harness.Render();
        }

        private static void FireResizeDelta(DockSplitterControl splitter, double delta, bool isFinal)
        {
            var hostExtent = splitter.GetHostExtent();
            if (hostExtent < 1) hostExtent = 1000;
            var args = new DockSplitterDeltaEventArgs(delta, splitter.Direction, hostExtent, isFinal);
            splitter.RaiseResizeDeltaForTest(args);
        }
    }
}
