using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Spec 046 §6.3 / §6.5 — stress-test scenarios exercising the role-aware
/// router and reserved-empty / prune rules through many open / close /
/// split combinations. Drives the Scene-J showcase bugs through pure
/// layout-algebra paths so regressions surface without UI infrastructure.
/// </summary>
public class DocumentAreaStressTests
{
    private static Document Doc(string key) => new() { Title = key, Key = key };
    private static ToolWindow Tool(string key, DockSides allowed = DockSides.All) =>
        new() { Title = key, Key = key, AllowedSides = allowed };

    private static DockNode VsLayout(
        DockTabGroup leftStrip,
        DockTabGroup docWell,
        DockTabGroup rightStrip)
        => new DockSplit(Orientation.Horizontal,
            new DockNode[] { leftStrip, docWell, rightStrip });

    private static IReadOnlyList<DockableContent> AllLeaves(DockNode? root)
    {
        var dict = new Dictionary<object, DockableContent>();
        DockLayoutMutator.IndexLeavesInto(root, dict);
        return dict.Values.ToArray();
    }

    private static IReadOnlyList<DockTabGroup> AllGroups(DockNode? root)
    {
        var acc = new List<DockTabGroup>();
        Walk(root, acc);
        return acc;
        static void Walk(DockNode? n, List<DockTabGroup> acc)
        {
            switch (n)
            {
                case DockTabGroup g: acc.Add(g); break;
                case DockSplit s: foreach (var c in s.Children) Walk(c, acc); break;
            }
        }
    }

    // ── Spec 046 §6.3: many opens land in the DocumentArea ───────────────

    [Fact]
    public void OpenManyDocuments_AllLandInDocumentArea()
    {
        DockNode root = VsLayout(
            new DockTabGroup(new[] { (DockableContent)Tool("gallery") }, Role: DockGroupRole.ToolWindowStrip),
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea),
            new DockTabGroup(new[] { (DockableContent)Tool("config") }, Role: DockGroupRole.ToolWindowStrip));

        for (int i = 0; i < 20; i++)
        {
            root = DockLayoutMutator.InsertPaneAtTarget(root, Doc($"d{i}"), DockTarget.Center, out var fb);
            Assert.Null(fb);
        }

        // All 20 docs must end up in the single DocumentArea — the tool
        // strips never accept a Document (spec §6.3 acceptance matrix).
        var docArea = AllGroups(root).Single(g => g.Role == DockGroupRole.DocumentArea);
        Assert.Equal(20, docArea.Documents.Count);
        Assert.All(docArea.Documents, d => Assert.IsType<Document>(d));
    }

    [Fact]
    public void OpenManyToolWindows_AllLandInFirstStrip_NotInDocumentArea()
    {
        DockNode root = VsLayout(
            new DockTabGroup(new[] { (DockableContent)Tool("gallery") }, Role: DockGroupRole.ToolWindowStrip),
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea),
            new DockTabGroup(new[] { (DockableContent)Tool("config") }, Role: DockGroupRole.ToolWindowStrip));

        for (int i = 0; i < 10; i++)
        {
            root = DockLayoutMutator.InsertPaneAtTarget(root, Tool($"t{i}"), DockTarget.Center, out var fb);
            Assert.Null(fb);
        }

        // Strips share tool windows by tree order; first strip wins on
        // tie because PreferredFor is symmetric. Neither lands in the
        // DocumentArea (it rejects ToolWindow).
        var strips = AllGroups(root).Where(g => g.Role == DockGroupRole.ToolWindowStrip).ToArray();
        Assert.Equal(2, strips.Length);
        Assert.Equal(11, strips[0].Documents.Count); // gallery + 10
        Assert.Single(strips[1].Documents);          // config alone
        var docArea = AllGroups(root).Single(g => g.Role == DockGroupRole.DocumentArea);
        Assert.Empty(docArea.Documents);
    }

    // ── Spec 046 §2.3: splits inside DocumentArea propagate the role ─────

    [Fact]
    public void SplitDocumentInsideDocumentArea_NewSiblingIsDocumentArea()
    {
        // After dragging a doc to SplitRight, both arms must be DocumentArea
        // so subsequent Dock(Center) routes correctly into either side.
        var d1 = Doc("d1");
        var d2 = Doc("d2");
        var docArea = new DockTabGroup(new[] { (DockableContent)d1, d2 }, Role: DockGroupRole.DocumentArea);

        // Simulate the drag-driven split: MovePaneToGroupTarget with SplitRight.
        var afterSplit = DockLayoutMutator.MovePaneToGroupTarget(docArea, d2, docArea, DockTarget.SplitRight);
        var split = Assert.IsType<DockSplit>(afterSplit);
        Assert.Equal(2, split.Children.Count);
        Assert.All(split.Children, c =>
        {
            var g = Assert.IsType<DockTabGroup>(c);
            Assert.Equal(DockGroupRole.DocumentArea, g.Role);
        });
    }

    [Fact]
    public void SplitAndOpenNewDoc_LandsInFirstDocumentAreaArm()
    {
        // Scene J repro: after splitting the doc area, Open New Document
        // must still find a DocumentArea group (the new arm or the
        // original — depending on tree order). Spec §6.3 says "first in
        // tree order" wins.
        var d1 = Doc("d1");
        var d2 = Doc("d2");
        var docArea = new DockTabGroup(new[] { (DockableContent)d1, d2 }, Role: DockGroupRole.DocumentArea);
        var afterSplit = DockLayoutMutator.MovePaneToGroupTarget(docArea, d2, docArea, DockTarget.SplitRight)!;

        var afterOpen = DockLayoutMutator.InsertPaneAtTarget(afterSplit, Doc("d3"), DockTarget.Center, out var fb);
        Assert.Null(fb);

        var split = Assert.IsType<DockSplit>(afterOpen);
        var firstArm = Assert.IsType<DockTabGroup>(split.Children[0]);
        Assert.Contains(firstArm.Documents, d => (d.Key as string) == "d3");
    }

    [Fact]
    public void DeepNestedSplits_RouteToFirstDocumentArea()
    {
        // Build a deep tree: Split[ Tool, Split[ Tool, Split[ Tool, DocArea(empty) ] ] ]
        // and verify Dock(Center) walks past the tool strips to reach
        // the DocumentArea no matter how deep it sits.
        var nested = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new[] { (DockableContent)Tool("t1") }, Role: DockGroupRole.ToolWindowStrip),
            new DockSplit(Orientation.Vertical, new DockNode[]
            {
                new DockTabGroup(new[] { (DockableContent)Tool("t2") }, Role: DockGroupRole.ToolWindowStrip),
                new DockSplit(Orientation.Horizontal, new DockNode[]
                {
                    new DockTabGroup(new[] { (DockableContent)Tool("t3") }, Role: DockGroupRole.ToolWindowStrip),
                    new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea),
                }),
            }),
        });

        var result = DockLayoutMutator.InsertPaneAtTarget(nested, Doc("d1"), DockTarget.Center, out var fb);
        Assert.Null(fb);
        var docArea = AllGroups(result).Single(g => g.Role == DockGroupRole.DocumentArea);
        Assert.Single(docArea.Documents);
        Assert.Equal("d1", docArea.Documents[0].Key);
    }

    // ── Spec 046 §6.5: close-in-split collapse ──────────────────────────

    [Fact]
    public void CloseNonLastDoc_InSplitArm_ArmCollapses()
    {
        // Two DocumentArea arms with one doc each; close one — the
        // surviving non-empty DocumentArea makes the empty arm cull, and
        // the outer split collapses to the lone surviving well.
        var d1 = Doc("d1");
        var d2 = Doc("d2");
        DockNode root = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new[] { (DockableContent)Tool("gallery") }, Role: DockGroupRole.ToolWindowStrip),
            new DockSplit(Orientation.Vertical, new DockNode[]
            {
                new DockTabGroup(new[] { (DockableContent)d1 }, Role: DockGroupRole.DocumentArea),
                new DockTabGroup(new[] { (DockableContent)d2 }, Role: DockGroupRole.DocumentArea),
            }),
            new DockTabGroup(new[] { (DockableContent)Tool("config") }, Role: DockGroupRole.ToolWindowStrip),
        });

        var (after, _) = DockLayoutMutator.RemovePane(root, d2);
        var outer = Assert.IsType<DockSplit>(after);
        Assert.Equal(3, outer.Children.Count);
        // Middle child must now be a single DocumentArea(d1), NOT a split
        // with an empty arm.
        var mid = Assert.IsType<DockTabGroup>(outer.Children[1]);
        Assert.Equal(DockGroupRole.DocumentArea, mid.Role);
        Assert.Single(mid.Documents);
        Assert.Equal("d1", mid.Documents[0].Key);
    }

    [Fact]
    public void CloseAllDocsInSplitArm_BothEmpty_OneSurvives()
    {
        // Both arms have one doc; close both — once both arms are empty,
        // the all-empty branch kicks in and one DocumentArea survives.
        var d1 = Doc("d1");
        var d2 = Doc("d2");
        DockNode root = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new[] { (DockableContent)d1 }, Role: DockGroupRole.DocumentArea),
            new DockTabGroup(new[] { (DockableContent)d2 }, Role: DockGroupRole.DocumentArea),
        });
        var (afterD2, _) = DockLayoutMutator.RemovePane(root, d2);
        // After d2 close: empty arm culls. Result is DocumentArea(d1) alone.
        var midAfter = Assert.IsType<DockTabGroup>(afterD2);
        Assert.Single(midAfter.Documents);

        var (afterD1, _) = DockLayoutMutator.RemovePane(afterD2, d1);
        // After d1 close: now zero docs in the only DocumentArea — the
        // all-empty branch keeps it as the reserved well.
        var lone = Assert.IsType<DockTabGroup>(afterD1);
        Assert.Equal(DockGroupRole.DocumentArea, lone.Role);
        Assert.Empty(lone.Documents);
    }

    [Fact]
    public void CloseDoc_LeavesToolStripsUntouched()
    {
        // Closing docs in the DocumentArea must never affect surrounding
        // tool strips — tool windows persist independent of the well's
        // reserved-empty rule.
        var d1 = Doc("d1");
        var d2 = Doc("d2");
        DockNode root = VsLayout(
            new DockTabGroup(new[] { (DockableContent)Tool("gallery") }, Role: DockGroupRole.ToolWindowStrip),
            new DockTabGroup(new[] { (DockableContent)d1, d2 }, Role: DockGroupRole.DocumentArea),
            new DockTabGroup(new[] { (DockableContent)Tool("config") }, Role: DockGroupRole.ToolWindowStrip));

        var (after1, _) = DockLayoutMutator.RemovePane(root, d1);
        var (after2, _) = DockLayoutMutator.RemovePane(after1, d2);

        var outer = Assert.IsType<DockSplit>(after2);
        Assert.Equal(3, outer.Children.Count);
        Assert.Equal(DockGroupRole.ToolWindowStrip, ((DockTabGroup)outer.Children[0]).Role);
        Assert.Equal(DockGroupRole.DocumentArea,    ((DockTabGroup)outer.Children[1]).Role);
        Assert.Equal(DockGroupRole.ToolWindowStrip, ((DockTabGroup)outer.Children[2]).Role);
        Assert.Empty(((DockTabGroup)outer.Children[1]).Documents);
    }

    // ── Complex open / close / split sequences ──────────────────────────

    [Fact]
    public void OpenSplit_RepeatedSequence_BuildsManyArms()
    {
        // Open a doc, split it, open another, split, …, building a chain
        // of DocumentArea arms. Every arm must remain DocumentArea so the
        // splitting invariant holds at depth.
        DockNode root = new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea);
        for (int i = 0; i < 5; i++)
        {
            var d = Doc($"d{i}");
            root = DockLayoutMutator.InsertPaneAtTarget(root, d, DockTarget.Center, out _);
            if (i > 0)
            {
                // Drag the newest doc into its own SplitRight arm so the
                // layout keeps fanning out horizontally.
                var firstDocArea = AllGroups(root).First(g =>
                    g.Role == DockGroupRole.DocumentArea && g.Documents.Any(x => (x.Key as string) == $"d{i}"));
                root = DockLayoutMutator.MovePaneToGroupTarget(root, d, firstDocArea, DockTarget.SplitRight)!;
            }
        }
        // Every group in the tree should be DocumentArea-roled.
        Assert.All(AllGroups(root), g => Assert.Equal(DockGroupRole.DocumentArea, g.Role));
        // And the total doc count should be 5 (no leaks).
        Assert.Equal(5, AllLeaves(root).Count);
    }

    [Fact]
    public void RemoveAddRoundTrip_PreservesLayoutShape()
    {
        // Open, split, close, reopen — verify the reopened doc lands in
        // a DocumentArea and the leaf set is consistent through the cycle.
        DockNode root = VsLayout(
            new DockTabGroup(new[] { (DockableContent)Tool("gallery") }, Role: DockGroupRole.ToolWindowStrip),
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea),
            new DockTabGroup(new[] { (DockableContent)Tool("config") }, Role: DockGroupRole.ToolWindowStrip));

        // Open 3 docs.
        for (int i = 0; i < 3; i++)
            root = DockLayoutMutator.InsertPaneAtTarget(root, Doc($"d{i}"), DockTarget.Center, out _);
        Assert.Equal(5, AllLeaves(root).Count);

        // Close them all.
        for (int i = 0; i < 3; i++)
        {
            var key = $"d{i}";
            var pane = AllLeaves(root).First(l => (l.Key as string) == key);
            var (after, _) = DockLayoutMutator.RemovePane(root, pane);
            root = after!;
        }
        // Two tool strips + one empty DocumentArea (reserved well).
        Assert.Equal(2, AllLeaves(root).Count);
        Assert.Single(AllGroups(root), g => g.Role == DockGroupRole.DocumentArea && g.Documents.Count == 0);

        // Reopen a doc — must land in the surviving well.
        root = DockLayoutMutator.InsertPaneAtTarget(root, Doc("d-revived"), DockTarget.Center, out var fb);
        Assert.Null(fb);
        var revivedArea = AllGroups(root).Single(g => g.Role == DockGroupRole.DocumentArea);
        Assert.Single(revivedArea.Documents);
        Assert.Equal("d-revived", revivedArea.Documents[0].Key);
    }

    // ── Tool windows: routing, AllowedSides, edge synthesis ─────────────

    [Fact]
    public void ToolWindow_DropOnBottomEdge_CreatesBottomStrip()
    {
        // Layout with only a DocumentArea. Dropping a ToolWindow on the
        // bottom DOCK edge must create a new ToolWindowStrip (spec §2.3
        // edge-wrap rule for ToolWindow → ToolWindowStrip).
        DockNode root = new DockTabGroup(new[] { (DockableContent)Doc("d1") }, Role: DockGroupRole.DocumentArea);
        var result = DockLayoutMutator.InsertPaneAtTarget(root, Tool("errors"), DockTarget.DockBottom, out _);
        var split = Assert.IsType<DockSplit>(result);
        Assert.Equal(Orientation.Vertical, split.Orientation);
        Assert.Equal(2, split.Children.Count);
        var bottom = Assert.IsType<DockTabGroup>(split.Children[1]);
        Assert.Equal(DockGroupRole.ToolWindowStrip, bottom.Role);
    }

    [Fact]
    public void ToolWindow_WithAllowedSidesBottom_DropFilteredOnLeft()
    {
        // Drop filter: AllowedSides=Bottom rejects SplitLeft on any strip;
        // SplitBottom against a tool-window strip is allowed (role accepts
        // ToolWindow AND mask includes Bottom).
        var bottomOnly = Tool("errors", DockSides.Bottom);
        var strip = new DockTabGroup(new[] { (DockableContent)Tool("gallery") },
            Role: DockGroupRole.ToolWindowStrip);
        Assert.False(DockDropFilter.CanDropInto(strip, bottomOnly, DockSide.Left));
        Assert.True(DockDropFilter.CanDropInto(strip, bottomOnly, DockSide.Bottom));
        // Edge-level filter only consults AllowedSides (no group to vet).
        Assert.False(DockDropFilter.CanDockAtEdge(bottomOnly, DockSide.Left));
        Assert.True(DockDropFilter.CanDockAtEdge(bottomOnly, DockSide.Bottom));
        // A DocumentArea rejects ToolWindows on every side — role gate
        // wins regardless of the AllowedSides mask.
        var docArea = new DockTabGroup(new[] { (DockableContent)Doc("d1") },
            Role: DockGroupRole.DocumentArea);
        Assert.False(DockDropFilter.CanDropInto(docArea, bottomOnly, DockSide.Bottom));
    }

    [Fact]
    public void ToolWindow_IntoToolWindowStrip_RoleAccepted()
    {
        // Two strips — adding a tool with Center routing lands in the
        // first strip (tree order) without a fallback diagnostic.
        DockNode root = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new[] { (DockableContent)Tool("gallery") }, Role: DockGroupRole.ToolWindowStrip),
            new DockTabGroup(new[] { (DockableContent)Tool("config") }, Role: DockGroupRole.ToolWindowStrip),
        });
        var result = DockLayoutMutator.InsertPaneAtTarget(root, Tool("output"), DockTarget.Center, out var fb);
        Assert.Null(fb);
        var split = Assert.IsType<DockSplit>(result);
        var first = (DockTabGroup)split.Children[0];
        Assert.Equal(2, first.Documents.Count); // gallery + output
        Assert.Single(((DockTabGroup)split.Children[1]).Documents);
    }

    [Fact]
    public void Document_IntoToolStripOnlyLayout_FallsBackWithDiagnostic()
    {
        // Layout has no DocumentArea or General group — only tool strips.
        // Dock(Center) of a Document hits the leftmost-descendant
        // fallback and surfaces the diagnostic.
        DockNode root = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new[] { (DockableContent)Tool("gallery") }, Role: DockGroupRole.ToolWindowStrip),
            new DockTabGroup(new[] { (DockableContent)Tool("config") }, Role: DockGroupRole.ToolWindowStrip),
        });
        var result = DockLayoutMutator.InsertPaneAtTarget(root, Doc("d1"), DockTarget.Center, out var fb);
        Assert.NotNull(fb);
        // Document still landed somewhere — fallback path.
        Assert.Contains(AllLeaves(result), l => (l.Key as string) == "d1");
    }

    // ── Many-doc edge cases ─────────────────────────────────────────────

    [Fact]
    public void ManyDocs_OpenAndClose_LeavesReservedWell()
    {
        // Open 50 docs, close them all in reverse order. The DocumentArea
        // must end up empty-but-present.
        DockNode root = new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea);
        var opened = new List<Document>();
        for (int i = 0; i < 50; i++)
        {
            var d = Doc($"d{i}");
            opened.Add(d);
            root = DockLayoutMutator.InsertPaneAtTarget(root, d, DockTarget.Center, out _);
        }
        var docArea = Assert.IsType<DockTabGroup>(root);
        Assert.Equal(50, docArea.Documents.Count);

        for (int i = opened.Count - 1; i >= 0; i--)
        {
            var (after, found) = DockLayoutMutator.RemovePane(root, opened[i]);
            Assert.True(found);
            root = after!;
        }
        var emptied = Assert.IsType<DockTabGroup>(root);
        Assert.Equal(DockGroupRole.DocumentArea, emptied.Role);
        Assert.Empty(emptied.Documents);
    }

    [Fact]
    public void RepeatedSplitThenCloseAll_CollapsesToReservedWell()
    {
        // Build a fanned-out chain of DocumentArea splits, then close
        // every doc. The all-empty branch keeps exactly one DocumentArea
        // alive at the root.
        DockNode root = new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea);
        var docs = new List<Document>();
        for (int i = 0; i < 4; i++)
        {
            var d = Doc($"d{i}");
            docs.Add(d);
            root = DockLayoutMutator.InsertPaneAtTarget(root, d, DockTarget.Center, out _);
            if (i > 0)
            {
                var hostGroup = AllGroups(root).First(g => g.Documents.Any(x => (x.Key as string) == $"d{i}"));
                root = DockLayoutMutator.MovePaneToGroupTarget(root, d, hostGroup, DockTarget.SplitRight)!;
            }
        }
        Assert.Equal(4, AllGroups(root).Count); // 4 DocumentArea arms

        foreach (var d in docs)
        {
            var (after, _) = DockLayoutMutator.RemovePane(root, d);
            root = after!;
        }
        // Single reserved well remains.
        var lone = Assert.IsType<DockTabGroup>(root);
        Assert.Equal(DockGroupRole.DocumentArea, lone.Role);
        Assert.Empty(lone.Documents);
    }

    // ── Mixed Document + ToolWindow stress ──────────────────────────────

    [Fact]
    public void Mixed_OpenDocsAndTools_RouteToCorrectRoles()
    {
        DockNode root = VsLayout(
            new DockTabGroup(new[] { (DockableContent)Tool("gallery") }, Role: DockGroupRole.ToolWindowStrip),
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea),
            new DockTabGroup(new[] { (DockableContent)Tool("config") }, Role: DockGroupRole.ToolWindowStrip));

        // Alternate doc / tool inserts.
        for (int i = 0; i < 10; i++)
        {
            DockableContent payload = i % 2 == 0 ? Doc($"d{i}") : Tool($"t{i}");
            root = DockLayoutMutator.InsertPaneAtTarget(root, payload, DockTarget.Center, out var fb);
            Assert.Null(fb);
        }

        var docArea = AllGroups(root).Single(g => g.Role == DockGroupRole.DocumentArea);
        // 5 documents (indices 0,2,4,6,8) — none of them are ToolWindows.
        Assert.Equal(5, docArea.Documents.Count);
        Assert.All(docArea.Documents, d => Assert.IsType<Document>(d));

        var strips = AllGroups(root).Where(g => g.Role == DockGroupRole.ToolWindowStrip).ToArray();
        // 5 new tools + gallery + config = 7 total across strips.
        var totalTools = strips.Sum(g => g.Documents.Count);
        Assert.Equal(7, totalTools);
    }

    [Fact]
    public void CloseAllInDocumentArea_WithToolWindowsPresent_ToolsUntouched()
    {
        var d1 = Doc("d1");
        DockNode root = VsLayout(
            new DockTabGroup(new[] { (DockableContent)Tool("gallery") }, Role: DockGroupRole.ToolWindowStrip),
            new DockTabGroup(new[] { (DockableContent)d1 }, Role: DockGroupRole.DocumentArea),
            new DockTabGroup(new[] { (DockableContent)Tool("config") }, Role: DockGroupRole.ToolWindowStrip));

        var (after, _) = DockLayoutMutator.RemovePane(root, d1);
        var outer = Assert.IsType<DockSplit>(after);
        Assert.Equal(3, outer.Children.Count);
        // Tools survive.
        Assert.Single(((DockTabGroup)outer.Children[0]).Documents);
        Assert.Single(((DockTabGroup)outer.Children[2]).Documents);
        // Well is empty but present.
        Assert.Empty(((DockTabGroup)outer.Children[1]).Documents);
    }
}
