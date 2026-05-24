using System;
using System.Linq;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Spec 046 §6.5 — reserved-empty semantics for
/// <see cref="DockGroupRole.DocumentArea"/> groups. Closing the last
/// document in a DocumentArea group leaves the group as a visible
/// empty well; other roles cull as before.
/// </summary>
public class DocumentAreaCullTests
{
    private static Document Doc(string key) => new() { Title = key, Key = key };
    private static ToolWindow Tool(string key) => new() { Title = key, Key = key };

    [Fact]
    public void CloseLastDocument_InDocumentArea_GroupSurvives()
    {
        var d1 = Doc("d1");
        var docArea = new DockTabGroup(new[] { (DockableContent)d1 }, Role: DockGroupRole.DocumentArea);
        DockNode root = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new[] { (DockableContent)Tool("t1") }, Role: DockGroupRole.ToolWindowStrip),
            docArea,
        });
        var (after, found) = DockLayoutMutator.RemovePane(root, d1);
        Assert.True(found);
        var split = Assert.IsType<DockSplit>(after);
        Assert.Equal(2, split.Children.Count);
        var survivingDocArea = (DockTabGroup)split.Children[1];
        Assert.Equal(DockGroupRole.DocumentArea, survivingDocArea.Role);
        Assert.Empty(survivingDocArea.Documents);
    }

    [Fact]
    public void CloseLastDocument_InGeneralGroup_CulledAsBefore()
    {
        var d1 = Doc("d1");
        var general = new DockTabGroup(new[] { (DockableContent)d1 }, Role: DockGroupRole.General);
        DockNode root = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new[] { (DockableContent)Tool("t1") }, Role: DockGroupRole.ToolWindowStrip),
            general,
        });
        var (after, found) = DockLayoutMutator.RemovePane(root, d1);
        Assert.True(found);
        // Split collapses to the surviving tool strip (keep == 1 collapse).
        var lonely = Assert.IsType<DockTabGroup>(after);
        Assert.Equal(DockGroupRole.ToolWindowStrip, lonely.Role);
    }

    [Fact]
    public void CloseLastDocument_InDocumentArea_ThenReinsert_LandsBack()
    {
        var d1 = Doc("d1");
        var docArea = new DockTabGroup(new[] { (DockableContent)d1 }, Role: DockGroupRole.DocumentArea);
        DockNode root = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new[] { (DockableContent)Tool("t1") }, Role: DockGroupRole.ToolWindowStrip),
            docArea,
        });
        var (after, _) = DockLayoutMutator.RemovePane(root, d1);
        // Reinsert a new doc via Dock(Center) — must land in surviving DocumentArea.
        var result = DockLayoutMutator.InsertPaneAtTarget(after, Doc("d2"), DockTarget.Center, out var fb);
        Assert.Null(fb);
        var split = (DockSplit)result;
        var docAreaSurvivor = split.Children
            .OfType<DockTabGroup>()
            .Single(g => g.Role == DockGroupRole.DocumentArea);
        Assert.Single(docAreaSurvivor.Documents);
        Assert.Equal("d2", docAreaSurvivor.Documents[0].Key);
    }

    [Fact]
    public void NestedSplit_DocumentAreaSurvivesInnerCollapse()
    {
        // Inner Split[ DocumentArea(empty already), General(doc) ] — close
        // the General's doc; inner split collapses to lone DocumentArea
        // (which then collapses out of the inner split via keep==1).
        // Outer must still see DocumentArea.
        var d1 = Doc("d1");
        var docArea = new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea);
        var general = new DockTabGroup(new[] { (DockableContent)d1 }, Role: DockGroupRole.General);
        var innerSplit = new DockSplit(Orientation.Horizontal, new DockNode[] { docArea, general });
        DockNode root = new DockSplit(Orientation.Vertical, new DockNode[]
        {
            innerSplit,
            new DockTabGroup(new[] { (DockableContent)Tool("t1") }, Role: DockGroupRole.ToolWindowStrip),
        });
        var (after, _) = DockLayoutMutator.RemovePane(root, d1);
        var outer = Assert.IsType<DockSplit>(after);
        Assert.Equal(2, outer.Children.Count);
        // Inner split collapsed to DocumentArea (keep==1 promotes lone child).
        var surviving = Assert.IsType<DockTabGroup>(outer.Children[0]);
        Assert.Equal(DockGroupRole.DocumentArea, surviving.Role);
        Assert.Empty(surviving.Documents);
    }

    [Fact]
    public void CloseLastDocument_InOnlyDocumentArea_GroupSurvivesAsRoot()
    {
        // The whole layout is a single DocumentArea. Closing its last doc
        // leaves an empty DocumentArea, NOT a null root.
        var d1 = Doc("d1");
        DockNode root = new DockTabGroup(new[] { (DockableContent)d1 }, Role: DockGroupRole.DocumentArea);
        var (after, _) = DockLayoutMutator.RemovePane(root, d1);
        var grp = Assert.IsType<DockTabGroup>(after);
        Assert.Equal(DockGroupRole.DocumentArea, grp.Role);
        Assert.Empty(grp.Documents);
    }

    // ── Spec 046 §6.5 (refined per app feedback) ──
    // Empty DocumentArea survives ONLY when it's the only DocumentArea in
    // the tree. When any non-empty DocumentArea exists, empty ones cull so
    // the split arm collapses to the surviving sibling.

    [Fact]
    public void EmptyDocumentAreas_AllEmpty_FirstSurvives_RestCull()
    {
        // Two empty DocumentArea groups in a split with a General group
        // that's about to lose its last doc. After the cull, no non-empty
        // DocumentArea exists, so the all-empty fallback kicks in and the
        // first empty DocumentArea survives.
        var d1 = Doc("d1");
        DockNode root = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea),
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea),
            new DockTabGroup(new[] { (DockableContent)d1 }, Role: DockGroupRole.General),
        });
        var (after, _) = DockLayoutMutator.RemovePane(root, d1);
        var lone = Assert.IsType<DockTabGroup>(after);
        Assert.Equal(DockGroupRole.DocumentArea, lone.Role);
        Assert.Empty(lone.Documents);
    }

    [Fact]
    public void EmptyDocumentArea_SiblingNonEmptyDocumentArea_EmptyCulls()
    {
        // Scene J close-doc-in-split repro. User opened two docs in a
        // DocumentArea, split via drag (Split[ DocArea(d1), DocArea(d2) ]),
        // then closed d2. The empty arm should collapse because d1's
        // surviving DocumentArea already fulfills the reserved-well role.
        var d1 = Doc("d1");
        var d2 = Doc("d2");
        DockNode root = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new[] { (DockableContent)d1 }, Role: DockGroupRole.DocumentArea),
            new DockTabGroup(new[] { (DockableContent)d2 }, Role: DockGroupRole.DocumentArea),
        });
        var (after, _) = DockLayoutMutator.RemovePane(root, d2);
        // d2's empty DocumentArea culls; split collapses (keep==1) to d1's
        // surviving DocumentArea at the root.
        var survivor = Assert.IsType<DockTabGroup>(after);
        Assert.Equal(DockGroupRole.DocumentArea, survivor.Role);
        Assert.Single(survivor.Documents);
        Assert.Equal("d1", survivor.Documents[0].Key);
    }

    [Fact]
    public void EmptyDocumentAreas_PostCloseAll_FirstSurvives()
    {
        // Closing every doc across two DocumentArea arms leaves both empty.
        // The all-empty branch applies: first survives, second culls,
        // outer split collapses to the lone surviving well.
        var d1 = Doc("d1");
        var d2 = Doc("d2");
        DockNode root = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new[] { (DockableContent)d1 }, Role: DockGroupRole.DocumentArea),
            new DockTabGroup(new[] { (DockableContent)d2 }, Role: DockGroupRole.DocumentArea),
        });
        var (afterD2, _) = DockLayoutMutator.RemovePane(root, d2);
        // After d2 closes: empty arm culls per the new rule. Result is
        // DocumentArea(d1) at root.
        var afterD2Survivor = Assert.IsType<DockTabGroup>(afterD2);
        Assert.Single(afterD2Survivor.Documents);
        Assert.Equal("d1", afterD2Survivor.Documents[0].Key);

        var (afterD1, _) = DockLayoutMutator.RemovePane(afterD2, d1);
        // d1's DocumentArea is the only DocumentArea — empty survives as
        // the reserved well.
        var loneArea = Assert.IsType<DockTabGroup>(afterD1);
        Assert.Equal(DockGroupRole.DocumentArea, loneArea.Role);
        Assert.Empty(loneArea.Documents);
    }

    [Fact]
    public void EmptyDocumentArea_WithExplicitShowWhenEmpty_OptsOutOfPrune()
    {
        // Author explicitly set ShowWhenEmpty=true on both DocumentAreas
        // — opt out of the spec-046 prune rule. Both groups must survive
        // empty.
        DockNode root = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(Array.Empty<DockableContent>(),
                ShowWhenEmpty: true, Role: DockGroupRole.DocumentArea),
            new DockTabGroup(Array.Empty<DockableContent>(),
                ShowWhenEmpty: true, Role: DockGroupRole.DocumentArea),
            new DockTabGroup(new[] { (DockableContent)Doc("d1") }, Role: DockGroupRole.General),
        });
        var (after, _) = DockLayoutMutator.RemovePane(root, root switch
        {
            DockSplit s => ((DockTabGroup)s.Children[2]).Documents[0],
            _ => throw new InvalidOperationException(),
        });
        var split = Assert.IsType<DockSplit>(after);
        Assert.Equal(2, split.Children.Count);
        Assert.All(split.Children, c =>
        {
            var g = Assert.IsType<DockTabGroup>(c);
            Assert.Equal(DockGroupRole.DocumentArea, g.Role);
            Assert.True(g.ShowWhenEmpty);
        });
    }

    [Fact]
    public void NonEmptyDocumentAreas_NotPruned_EvenWithMultiple()
    {
        // Two DocumentAreas, both non-empty — prune leaves them both
        // alone. Only EMPTY ones cull.
        DockNode root = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new[] { (DockableContent)Doc("a") }, Role: DockGroupRole.DocumentArea),
            new DockTabGroup(new[] { (DockableContent)Doc("b") }, Role: DockGroupRole.DocumentArea),
            new DockTabGroup(new[] { (DockableContent)Doc("trigger") }, Role: DockGroupRole.General),
        });
        var (after, _) = DockLayoutMutator.RemovePane(root, ((DockTabGroup)((DockSplit)root).Children[2]).Documents[0]);
        var split = Assert.IsType<DockSplit>(after);
        Assert.Equal(2, split.Children.Count);
        Assert.All(split.Children, c =>
        {
            var g = Assert.IsType<DockTabGroup>(c);
            Assert.Equal(DockGroupRole.DocumentArea, g.Role);
            Assert.NotEmpty(g.Documents);
        });
    }
}
