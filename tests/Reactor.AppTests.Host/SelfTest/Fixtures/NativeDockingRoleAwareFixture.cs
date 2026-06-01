using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

// ════════════════════════════════════════════════════════════════════════
//  Spec 046 — host-level fixtures for role-aware Dock(Center) routing,
//  reserved-empty DocumentArea semantics, and the
//  override-staleness-invalidation path in DockHostNativeComponent.
//
//  These fixtures drive a Scene-J-shaped VS layout through a sequence of
//  app-state updates (the "controlled-input" pattern from spec 045 §2.30:
//  app holds liveLayout in state, mutates via DockLayoutOps, host
//  re-renders against the new prop) and verify the visual tree converges
//  to the expected shape. They exist because the layout-algebra is fully
//  covered by Reactor.Tests/Docking/* but the host-side override path
//  needs a real Reactor host to exercise.
// ════════════════════════════════════════════════════════════════════════

internal static class NativeDockingRoleAwareFixtures
{
    private static Document Doc(string key) =>
        new() { Title = key, Key = key, Content = TextBlock($"body:{key}") };

    private static ToolWindow Tool(string key, DockSides allowed = DockSides.All) =>
        new() { Title = key, Key = key, AllowedSides = allowed, Content = TextBlock($"tool:{key}") };

    /// <summary>
    /// Build the initial Scene-J-style layout: a tool strip on each side
    /// with a reserved-empty DocumentArea between them.
    /// </summary>
    private static DockNode BuildVsLayout(ToolWindow leftTool, ToolWindow rightTool)
        => new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new DockableContent[] { leftTool },  Width: 200, Role: DockGroupRole.ToolWindowStrip),
            new DockTabGroup(Array.Empty<DockableContent>(),                 Role: DockGroupRole.DocumentArea),
            new DockTabGroup(new DockableContent[] { rightTool }, Width: 220, Role: DockGroupRole.ToolWindowStrip),
        });

    // ════════════════════════════════════════════════════════════════════
    //  Open / close happy path — app drives layout via setState, host
    //  echoes through manager.Layout. The override-staleness fix is what
    //  lets this path work after any drag-induced override is in place.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Inspect TabView headers in the visual tree to confirm a doc is
    /// attached as a tab. Necessary because TabView lazily realizes the
    /// body of unselected tabs — checking <c>FindText("body:dN")</c>
    /// would miss docs that are present-but-not-active.
    /// </summary>
    private static bool HasTabHeaderImpl(Harness h, string key)
    {
        var tabs = h.FindAllControls<TabView>(_ => true);
        foreach (var tv in tabs)
        {
            if (tv.TabItems.OfType<TabViewItem>().Any(tvi => tvi.Header is string s && s == key))
                return true;
        }
        return false;
    }

    internal class OpenDocs_LandInDocumentArea(Harness h) : SelfTestFixtureBase(h)
    {
        private bool HasTabHeader(string key) => HasTabHeaderImpl(H, key);


        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var leftTool = Tool("gallery");
            var rightTool = Tool("config");
            DockNode layout = BuildVsLayout(leftTool, rightTool);

            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();

            // Baseline — the empty DocumentArea is present (rendered as a
            // placeholder Border per DockTabGroupRenderer.cs §empty path).
            H.Check("RoleAware_Initial_GalleryTool",  H.FindText("tool:gallery")  is not null);
            H.Check("RoleAware_Initial_ConfigTool",   H.FindText("tool:config")   is not null);
            H.Check("RoleAware_Initial_NoDocsYet",    H.FindText("body:d0")       is null);

            // Add docs one at a time via DockLayoutOps.InsertPaneAtTarget,
            // mirroring Scene J's OpenDoc handler. Each Center insert must
            // land in the DocumentArea group.
            for (int i = 0; i < 5; i++)
            {
                layout = DockLayoutOps.InsertPaneAtTarget(layout, Doc($"d{i}"), DockTarget.Center, out _);
                host.Mount(_ => new DockManager { Layout = layout });
                await Harness.Render();
                H.Check($"RoleAware_OpenDoc{i}_BodyVisible", H.FindText($"body:d{i}") is not null);
            }

            // All 5 docs must be in a single DocumentArea tab strip — the
            // tool strips on either side never accepted a Document.
            var tabViews = H.FindAllControls<TabView>(_ => true);
            var docTabView = tabViews.FirstOrDefault(tv => tv.TabItems.Count >= 5);
            H.Check("RoleAware_AllDocsInOneGroup", docTabView is not null);

            host.Mount(_ => TextBlock("role-aware-done"));
            await Harness.Render();
        }
    }

    internal class CloseAllDocs_DocumentAreaSurvivesEmpty(Harness h) : SelfTestFixtureBase(h)
    {
        private bool HasTabHeader(string key) => HasTabHeaderImpl(H, key);


        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var leftTool = Tool("gallery");
            var rightTool = Tool("config");
            DockNode layout = BuildVsLayout(leftTool, rightTool);

            // Seed with two documents.
            layout = DockLayoutOps.InsertPaneAtTarget(layout, Doc("d1"), DockTarget.Center, out _);
            layout = DockLayoutOps.InsertPaneAtTarget(layout, Doc("d2"), DockTarget.Center, out _);

            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();
            // TabView shows only the SELECTED tab's body; verify both
            // docs are attached as headers (the unselected body is
            // unrealized by TabView until the user clicks).
            H.Check("RoleAware_CloseAll_BothDocsAttached",
                HasTabHeader("d1") && HasTabHeader("d2"));

            // Close every doc by calling RemovePane in app-state.
            var (afterD1, _) = DockLayoutOps.RemovePane(layout, layout switch
            {
                DockSplit s => ((DockTabGroup)s.Children[1]).Documents[0],
                _ => throw new InvalidOperationException(),
            });
            layout = afterD1!;
            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();

            var (afterD2, _) = DockLayoutOps.RemovePane(layout, layout switch
            {
                DockSplit s => ((DockTabGroup)s.Children[1]).Documents[0],
                _ => throw new InvalidOperationException(),
            });
            layout = afterD2!;
            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();

            H.Check("RoleAware_CloseAll_NoDocBodies",
                H.FindText("body:d1") is null && H.FindText("body:d2") is null);
            // Tools survive — DocumentArea reserved-well rule keeps the
            // outer split shape intact.
            H.Check("RoleAware_CloseAll_LeftToolSurvives",  H.FindText("tool:gallery") is not null);
            H.Check("RoleAware_CloseAll_RightToolSurvives", H.FindText("tool:config")  is not null);

            // Reopen a doc — must land back in the surviving reserved well.
            layout = DockLayoutOps.InsertPaneAtTarget(layout, Doc("d-revived"), DockTarget.Center, out var fb);
            H.Check("RoleAware_CloseAll_RevivalNoFallback", fb is null);
            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();
            H.Check("RoleAware_CloseAll_RevivedDocVisible",
                await Harness.WaitFor(() => H.FindText("body:d-revived") is not null));

            host.Mount(_ => TextBlock("close-all-done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Split + close + reopen — the bugs the user filed against Scene J.
    // ════════════════════════════════════════════════════════════════════

    internal class SplitDocArea_OpenNewDoc_LandsInDocumentArea(Harness h) : SelfTestFixtureBase(h)
    {
        private bool HasTabHeader(string key) => HasTabHeaderImpl(H, key);


        public override async Task RunAsync()
        {
            // Repro of bug 2: after splitting the DocumentArea via a
            // drag-style operation (simulated by MovePaneToGroupTarget),
            // Open New Document must still land in a DocumentArea.
            //
            // This exercises the host's override-staleness invalidation
            // path: the host stores a layoutOverride after the simulated
            // drag (via the regular DragConfirm wiring), and when the
            // app updates manager.Layout with the new doc, the override's
            // key set differs and the host must discard it.
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var leftTool = Tool("gallery");
            var rightTool = Tool("config");
            DockNode layout = BuildVsLayout(leftTool, rightTool);

            // Seed with two docs in the DocumentArea.
            layout = DockLayoutOps.InsertPaneAtTarget(layout, Doc("d1"), DockTarget.Center, out _);
            layout = DockLayoutOps.InsertPaneAtTarget(layout, Doc("d2"), DockTarget.Center, out _);

            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();

            // Simulate the drag-split: d2 moves into a SplitRight against
            // the DocumentArea group. The mutator's MovePaneToGroupTarget
            // mirrors what the drag pipeline produces on confirm.
            var docArea = layout switch
            {
                DockSplit s => (DockTabGroup)s.Children[1],
                _ => throw new InvalidOperationException(),
            };
            var d2 = docArea.Documents.First(d => (d.Key as string) == "d2");
            layout = DockLayoutMutator.MovePaneToGroupTarget(layout, d2, docArea, DockTarget.SplitRight)!;
            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();

            // Both docs attached as tab headers — one in each split arm.
            H.Check("RoleAware_Split_BothDocsAttached",
                HasTabHeader("d1") && HasTabHeader("d2"));
            var splitGroups = AllDocAreaGroups(layout);
            H.Check("RoleAware_Split_TwoDocumentAreaArms", splitGroups.Count == 2);

            // Open a third doc via Center routing. With my fix, the host
            // sees manager.Layout's new key set (d3 added) and discards
            // any stale override, so d3 appears in the visual tree.
            layout = DockLayoutOps.InsertPaneAtTarget(layout, Doc("d3"), DockTarget.Center, out var fb);
            H.Check("RoleAware_Split_NoRoutingFallback", fb is null);
            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();
            H.Check("RoleAware_Split_NewDocAttached", HasTabHeader("d3"));
            // Original docs still attached as tab headers.
            H.Check("RoleAware_Split_OriginalDocsStillAttached",
                HasTabHeader("d1") && HasTabHeader("d2"));

            host.Mount(_ => TextBlock("split-open-done"));
            await Harness.Render();
        }
    }

    internal class SplitDocArea_CloseNonLast_SplitCollapses(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Repro of bug 3: split DocumentArea, close one arm's doc,
            // the empty arm should cull and the split should collapse to
            // the surviving non-empty DocumentArea.
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var leftTool = Tool("gallery");
            var rightTool = Tool("config");
            DockNode layout = BuildVsLayout(leftTool, rightTool);
            layout = DockLayoutOps.InsertPaneAtTarget(layout, Doc("d1"), DockTarget.Center, out _);
            layout = DockLayoutOps.InsertPaneAtTarget(layout, Doc("d2"), DockTarget.Center, out _);
            var docArea = ((DockSplit)layout).Children[1] as DockTabGroup;
            var d2 = docArea!.Documents.First(d => (d.Key as string) == "d2");
            layout = DockLayoutMutator.MovePaneToGroupTarget(layout, d2, docArea, DockTarget.SplitBottom)!;
            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();

            H.Check("RoleAware_CollapseSplit_TwoArmsBefore",
                AllDocAreaGroups(layout).Count == 2);

            // Close d2 — the empty arm should cull, leaving DocumentArea(d1)
            // alone in the middle slot.
            var (afterClose, _) = DockLayoutOps.RemovePane(layout, FindDocByKey(layout, "d2")!);
            layout = afterClose!;
            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();

            // d1 visible, d2 gone, split collapsed.
            H.Check("RoleAware_CollapseSplit_D1Visible", H.FindText("body:d1") is not null);
            H.Check("RoleAware_CollapseSplit_D2Gone",    H.FindText("body:d2") is null);
            var arms = AllDocAreaGroups(layout);
            H.Check("RoleAware_CollapseSplit_OneArmAfter", arms.Count == 1);
            H.Check("RoleAware_CollapseSplit_SurvivorHasD1",
                arms.Count == 1 && arms[0].Documents.Any(d => (d.Key as string) == "d1"));

            host.Mount(_ => TextBlock("collapse-split-done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Stress — many docs + repeated splits + close cycles
    // ════════════════════════════════════════════════════════════════════

    internal class ManyDocs_OpenSplitCloseCycle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var leftTool = Tool("gallery");
            var rightTool = Tool("config");
            DockNode layout = BuildVsLayout(leftTool, rightTool);
            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();

            // Open 8 docs, splitting every other one into a new arm so
            // the layout fans out. Each step re-mounts to drive the host
            // through its override-invalidation path.
            for (int i = 0; i < 8; i++)
            {
                var doc = Doc($"d{i}");
                layout = DockLayoutOps.InsertPaneAtTarget(layout, doc, DockTarget.Center, out _);
                if (i > 0 && i % 2 == 0)
                {
                    var hostGroup = AllDocAreaGroups(layout)
                        .First(g => g.Documents.Any(x => (x.Key as string) == $"d{i}"));
                    layout = DockLayoutMutator.MovePaneToGroupTarget(layout, doc, hostGroup, DockTarget.SplitRight)!;
                }
                host.Mount(_ => new DockManager { Layout = layout });
                await Harness.Render();
            }

            // All 8 docs visible in the tree.
            for (int i = 0; i < 8; i++)
            {
                var key = $"d{i}";
                H.Check($"RoleAware_Stress_Doc{i}_VisibleInTabStrip",
                    AnyTabHeader(key));
            }

            // Now close every doc. After the last close, exactly one
            // empty DocumentArea remains (reserved well).
            for (int i = 0; i < 8; i++)
            {
                var pane = FindDocByKey(layout, $"d{i}");
                if (pane is null) continue;
                var (after, _) = DockLayoutOps.RemovePane(layout, pane);
                layout = after!;
                host.Mount(_ => new DockManager { Layout = layout });
                await Harness.Render();
            }
            var emptyAreas = AllDocAreaGroups(layout);
            H.Check("RoleAware_Stress_OneReservedWellRemains",
                emptyAreas.Count == 1 && emptyAreas[0].Documents.Count == 0);
            // Tools still flank the well.
            H.Check("RoleAware_Stress_LeftToolSurvives",  H.FindText("tool:gallery") is not null);
            H.Check("RoleAware_Stress_RightToolSurvives", H.FindText("tool:config")  is not null);

            host.Mount(_ => TextBlock("stress-done"));
            await Harness.Render();
        }

        private bool AnyTabHeader(string key) => HasTabHeaderImpl(H, key);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Tool windows: drop synthesis + AllowedSides constraints
    // ════════════════════════════════════════════════════════════════════

    internal class ToolWindow_DockBottomEdge_SynthesizesStrip(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            // Start with a layout that has NO tool strip. A ToolWindow
            // dropped on the DockBottom edge must synthesize a new
            // ToolWindowStrip (spec 046 §2.3 edge-wrap for ToolWindow).
            DockNode layout = new DockTabGroup(new DockableContent[] { Doc("d1") },
                Role: DockGroupRole.DocumentArea);
            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();

            H.Check("RoleAware_EdgeSynth_DocVisible", H.FindText("body:d1") is not null);

            // Drop a tool on the bottom edge.
            layout = DockLayoutOps.InsertPaneAtTarget(layout, Tool("errors"), DockTarget.DockBottom, out _);
            host.Mount(_ => new DockManager { Layout = layout });
            await Harness.Render();

            H.Check("RoleAware_EdgeSynth_ToolVisible", H.FindText("tool:errors") is not null);
            // Verify the resulting structure has a ToolWindowStrip-roled group.
            var stripExists = layout is DockSplit s
                && s.Children.OfType<DockTabGroup>().Any(g => g.Role == DockGroupRole.ToolWindowStrip);
            H.Check("RoleAware_EdgeSynth_StripCreated", stripExists);

            host.Mount(_ => TextBlock("edge-synth-done"));
            await Harness.Render();
        }
    }

    internal class ToolWindow_AllowedSidesMask_FilterAtEdges(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // AllowedSides isn't directly testable through visual paths —
            // it's a drag-filter contract. Verify via the public filter
            // surface that the host honors the mask. (Visual coverage
            // would require simulating the drag overlay, which the
            // separate DragDropMatrix fixtures already exercise.)
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var bottomOnly = Tool("errors", DockSides.Bottom);

            H.Check("RoleAware_Mask_RejectsLeft",
                !DockDropFilter.CanDockAtEdge(bottomOnly, DockSide.Left));
            H.Check("RoleAware_Mask_RejectsTop",
                !DockDropFilter.CanDockAtEdge(bottomOnly, DockSide.Top));
            H.Check("RoleAware_Mask_RejectsRight",
                !DockDropFilter.CanDockAtEdge(bottomOnly, DockSide.Right));
            H.Check("RoleAware_Mask_AllowsBottom",
                DockDropFilter.CanDockAtEdge(bottomOnly, DockSide.Bottom));

            // PinToSide must throw on a masked side.
            var model = new DockHostModel();
            bool threw = false;
            try { model.PinToSide(bottomOnly, DockSide.Left); }
            catch (InvalidOperationException) { threw = true; }
            H.Check("RoleAware_Mask_PinToSideRejects", threw);

            // PinToSide must succeed on an allowed side.
            bool succeeded = false;
            string? allowedSideError = null;
            try
            {
                model.PinToSide(bottomOnly, DockSide.Bottom);
                succeeded = true;
            }
            catch (InvalidOperationException ex) { allowedSideError = ex.Message; }
            H.Check("RoleAware_Mask_PinToSideAllowsBottom", succeeded);
            H.Check("RoleAware_Mask_PinToSideAllowsBottom_NoError", allowedSideError is null);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static IReadOnlyList<DockTabGroup> AllDocAreaGroups(DockNode? root)
    {
        var acc = new List<DockTabGroup>();
        Walk(root, acc);
        return acc;
        static void Walk(DockNode? n, List<DockTabGroup> acc)
        {
            switch (n)
            {
                case DockTabGroup g when g.Role == DockGroupRole.DocumentArea:
                    acc.Add(g); break;
                case DockSplit s:
                    foreach (var c in s.Children) Walk(c, acc);
                    break;
                case DockTabGroup:
                    break;
            }
        }
    }

    private static DockableContent? FindDocByKey(DockNode? root, string key)
    {
        var dict = new Dictionary<object, DockableContent>();
        DockLayoutMutator.IndexLeavesInto(root, dict);
        return dict.TryGetValue(key, out var v) ? v : null;
    }
}
