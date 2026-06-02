using System;
using System.Linq;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Spec 046 §6.3 — role-aware routing for <see cref="DockTarget.Center"/>
/// inserts. Each test pins down one row of the acceptance / preference
/// matrix from the spec.
/// </summary>
public class RoleAwareRoutingTests
{
    private static Document Doc(string key) => new() { Title = key, Key = key };
    private static ToolWindow Tool(string key) => new() { Title = key, Key = key };
    private static DockableContent Untyped(string key) =>
        new(Title: key, Key: key);

    // ── CategoryOf ───────────────────────────────────────────────────────

    [Fact]
    public void CategoryOf_Document_IsDocument()
        => Assert.Equal(DockContentCategory.Document, DockLayoutMutator.CategoryOf(Doc("d")));

    [Fact]
    public void CategoryOf_ToolWindow_IsToolWindow()
        => Assert.Equal(DockContentCategory.ToolWindow, DockLayoutMutator.CategoryOf(Tool("t")));

    [Fact]
    public void CategoryOf_BaseDockableContent_IsUntyped()
        => Assert.Equal(DockContentCategory.Untyped, DockLayoutMutator.CategoryOf(Untyped("u")));

    [Fact]
    public void CategoryOf_TypedDocumentSubclass_IsDocument()
    {
        DockableContent typed = new Document<string> { Title = "x", Key = "x", State = "s" };
        Assert.Equal(DockContentCategory.Document, DockLayoutMutator.CategoryOf(typed));
    }

    // ── AcceptsCategory matrix (3×3, plus untyped) ───────────────────────

    // DockContentCategory is internal — visible to tests via InternalsVisibleTo,
    // but xunit's public test methods can't take an internal-typed parameter,
    // so we pass enum values as ints and cast inside.
    [Theory]
    [InlineData((int)DockGroupRole.General, (int)DockContentCategory.Document, true)]
    [InlineData((int)DockGroupRole.General, (int)DockContentCategory.ToolWindow, true)]
    [InlineData((int)DockGroupRole.General, (int)DockContentCategory.Untyped, true)]
    [InlineData((int)DockGroupRole.DocumentArea, (int)DockContentCategory.Document, true)]
    [InlineData((int)DockGroupRole.DocumentArea, (int)DockContentCategory.ToolWindow, false)]
    [InlineData((int)DockGroupRole.DocumentArea, (int)DockContentCategory.Untyped, true)]
    [InlineData((int)DockGroupRole.ToolWindowStrip, (int)DockContentCategory.Document, false)]
    [InlineData((int)DockGroupRole.ToolWindowStrip, (int)DockContentCategory.ToolWindow, true)]
    [InlineData((int)DockGroupRole.ToolWindowStrip, (int)DockContentCategory.Untyped, true)]
    public void AcceptsCategory_Matrix(int role, int cat, bool expected)
        => Assert.Equal(expected, DockLayoutMutator.AcceptsCategory((DockGroupRole)role, (DockContentCategory)cat));

    [Theory]
    [InlineData((int)DockGroupRole.DocumentArea, (int)DockContentCategory.Document, true)]
    [InlineData((int)DockGroupRole.ToolWindowStrip, (int)DockContentCategory.ToolWindow, true)]
    [InlineData((int)DockGroupRole.General, (int)DockContentCategory.Document, false)]
    [InlineData((int)DockGroupRole.General, (int)DockContentCategory.ToolWindow, false)]
    [InlineData((int)DockGroupRole.DocumentArea, (int)DockContentCategory.ToolWindow, false)]
    [InlineData((int)DockGroupRole.ToolWindowStrip, (int)DockContentCategory.Document, false)]
    [InlineData((int)DockGroupRole.General, (int)DockContentCategory.Untyped, false)]
    public void PreferredFor_Matrix(int role, int cat, bool expected)
        => Assert.Equal(expected, DockLayoutMutator.PreferredFor((DockGroupRole)role, (DockContentCategory)cat));

    // ── Routing scenarios — Phase 2 spec table ────────────────────────────

    private static DockTabGroup FirstGroupContaining(DockNode root, string key)
    {
        return FindGroup(root, key) ?? throw new InvalidOperationException($"no group contains key='{key}'");
        static DockTabGroup? FindGroup(DockNode n, string key) => n switch
        {
            DockTabGroup g when g.Documents.Any(d => Equals(d.Key, key)) => g,
            DockSplit s => s.Children.Select(c => FindGroup(c, key)).FirstOrDefault(r => r is not null),
            _ => null,
        };
    }

    [Fact]
    public void Repro_FeedbackBug_DocumentLandsInDocumentArea_NotLeftmost()
    {
        // Spec 046 §2.1 / §2.2: VS layout with two tool strips around an
        // empty DocumentArea well. Pre-046, this routed the document into
        // the leftmost descendant (Gallery). Post-046, it must land in
        // the middle DocumentArea.
        var layout = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new[] { Tool("Gallery") }, Role: DockGroupRole.ToolWindowStrip),
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea),
            new DockTabGroup(new[] { Tool("Config") }, Role: DockGroupRole.ToolWindowStrip),
        });
        var inserted = DockLayoutMutator.InsertPaneAtTarget(layout, Doc("d1"), DockTarget.Center, out var fallback);
        Assert.Null(fallback);
        var landing = FirstGroupContaining(inserted, "d1");
        Assert.Equal(DockGroupRole.DocumentArea, landing.Role);
        Assert.DoesNotContain(((DockSplit)inserted).Children.OfType<DockTabGroup>(),
            g => g.Role == DockGroupRole.ToolWindowStrip && g.Documents.Any(d => Equals(d.Key, "d1")));
    }

    [Fact]
    public void Document_PrefersDocumentArea_OverGeneral()
    {
        var layout = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.General),
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea),
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.General),
        });
        var result = DockLayoutMutator.InsertPaneAtTarget(layout, Doc("d1"), DockTarget.Center, out var fb);
        Assert.Null(fb);
        Assert.Equal(DockGroupRole.DocumentArea, FirstGroupContaining(result, "d1").Role);
    }

    [Fact]
    public void Document_FirstDocumentAreaWinsTreeOrder()
    {
        var layout = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea),
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea),
        });
        var result = DockLayoutMutator.InsertPaneAtTarget(layout, Doc("d1"), DockTarget.Center, out var fb);
        Assert.Null(fb);
        var split = (DockSplit)result;
        var firstChild = (DockTabGroup)split.Children[0];
        Assert.Contains(firstChild.Documents, d => Equals(d.Key, "d1"));
    }

    [Fact]
    public void ToolWindowStrip_RejectsDocuments_FallsBackToFirstAccepting()
    {
        // Layout: ToolWindowStrip + DocumentArea — DocumentArea wins (preferred).
        var layout = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.ToolWindowStrip),
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea),
        });
        var result = DockLayoutMutator.InsertPaneAtTarget(layout, Doc("d1"), DockTarget.Center, out var fb);
        Assert.Null(fb);
        Assert.Equal(DockGroupRole.DocumentArea, FirstGroupContaining(result, "d1").Role);
    }

    [Fact]
    public void Document_NoAcceptor_FallsBackToFirstChild_WithDiagnostic()
    {
        // ToolWindowStrip only — Document has no acceptor. Fallback to leftmost
        // group + diagnostic.
        var layout = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new[] { Tool("t1") }, Role: DockGroupRole.ToolWindowStrip),
            new DockTabGroup(new[] { Tool("t2") }, Role: DockGroupRole.ToolWindowStrip),
        });
        var result = DockLayoutMutator.InsertPaneAtTarget(layout, Doc("d1"), DockTarget.Center, out var fb);
        Assert.NotNull(fb);
        Assert.Contains("Document", fb!.Description);
        // Lands in leftmost despite role mismatch.
        var landing = FirstGroupContaining(result, "d1");
        Assert.Contains(landing.Documents, d => Equals(d.Key, "t1"));
    }

    [Fact]
    public void ToolWindow_NoAcceptor_FallsBackWithDiagnostic()
    {
        // Only a DocumentArea exists — ToolWindow rejected; fallback + diagnostic.
        var layout = new DockTabGroup(new[] { Doc("d1") }, Role: DockGroupRole.DocumentArea);
        var result = DockLayoutMutator.InsertPaneAtTarget(layout, Tool("t1"), DockTarget.Center, out var fb);
        Assert.NotNull(fb);
        Assert.Contains("ToolWindow", fb!.Description);
        // Both panes now in the same DocumentArea (fallback path).
        var group = (DockTabGroup)result;
        Assert.Equal(2, group.Documents.Count);
    }

    [Fact]
    public void ToolWindow_PrefersToolWindowStrip_OverGeneral()
    {
        var layout = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.General),
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.ToolWindowStrip),
        });
        var result = DockLayoutMutator.InsertPaneAtTarget(layout, Tool("t1"), DockTarget.Center, out var fb);
        Assert.Null(fb);
        Assert.Equal(DockGroupRole.ToolWindowStrip, FirstGroupContaining(result, "t1").Role);
    }

    [Fact]
    public void NestedSplits_DocumentFindsNestedDocumentArea()
    {
        // Spec 046 §2.2 — nested split routing.
        var layout = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new[] { Tool("t1") }, Role: DockGroupRole.ToolWindowStrip),
            new DockSplit(Orientation.Vertical, new DockNode[]
            {
                new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea),
                new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.General),
            }),
        });
        var result = DockLayoutMutator.InsertPaneAtTarget(layout, Doc("d1"), DockTarget.Center, out var fb);
        Assert.Null(fb);
        Assert.Equal(DockGroupRole.DocumentArea, FirstGroupContaining(result, "d1").Role);
    }

    [Fact]
    public void UntypedDockableContent_AcceptsAnywhere_LandsInLeftmostAccepting()
    {
        // Untyped accepts every role on the second pass; no first-pass preference.
        var layout = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.ToolWindowStrip),
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea),
        });
        var result = DockLayoutMutator.InsertPaneAtTarget(layout, Untyped("u1"), DockTarget.Center, out var fb);
        Assert.Null(fb);
        // First accepting child in tree order — leftmost.
        var split = (DockSplit)result;
        var leftmost = (DockTabGroup)split.Children[0];
        Assert.Contains(leftmost.Documents, d => Equals(d.Key, "u1"));
    }

    [Fact]
    public void EmptyRoot_DocumentWrapsAsDocumentArea()
    {
        var result = DockLayoutMutator.InsertPaneAtTarget(null, Doc("d1"), DockTarget.Center, out var fb);
        Assert.Null(fb);
        var g = Assert.IsType<DockTabGroup>(result);
        Assert.Equal(DockGroupRole.DocumentArea, g.Role);
    }

    [Fact]
    public void EmptyRoot_ToolWindowWrapsAsGeneral_NotStrip()
    {
        // Spec 046 §2.2 — don't auto-promote lone ToolWindow to ToolWindowStrip;
        // strips imply edge attachment which a free-standing wrap lacks.
        var result = DockLayoutMutator.InsertPaneAtTarget(null, Tool("t1"), DockTarget.Center, out var fb);
        Assert.Null(fb);
        var g = Assert.IsType<DockTabGroup>(result);
        Assert.Equal(DockGroupRole.General, g.Role);
    }

    [Fact]
    public void SingleLeafRoot_RoleDerivedFromLeaf()
    {
        // Root is a bare DockableContent leaf (a Document) — wrap+pane uses
        // the leaf's category to infer DocumentArea.
        DockNode leafRoot = Doc("d1");
        var result = DockLayoutMutator.InsertPaneAtTarget(leafRoot, Doc("d2"), DockTarget.Center, out var fb);
        Assert.Null(fb);
        var g = Assert.IsType<DockTabGroup>(result);
        Assert.Equal(DockGroupRole.DocumentArea, g.Role);
        Assert.Equal(2, g.Documents.Count);
    }

    // ── Split / edge target role propagation (§2.3) ──────────────────────

    [Fact]
    public void SplitRight_OnDocumentAreaRoot_ProducesTwoDocumentAreas()
    {
        DockNode root = new DockTabGroup(new[] { Doc("d1") }, Role: DockGroupRole.DocumentArea);
        var result = DockLayoutMutator.InsertPaneAtTarget(root, Doc("d2"), DockTarget.SplitRight);
        var split = Assert.IsType<DockSplit>(result);
        Assert.Equal(2, split.Children.Count);
        Assert.All(split.Children, c => Assert.Equal(DockGroupRole.DocumentArea, ((DockTabGroup)c).Role));
    }

    [Fact]
    public void DockBottom_WithToolWindow_AgainstDocumentArea_CreatesToolWindowStrip()
    {
        DockNode root = new DockTabGroup(new[] { Doc("d1") }, Role: DockGroupRole.DocumentArea);
        var result = DockLayoutMutator.InsertPaneAtTarget(root, Tool("t1"), DockTarget.DockBottom);
        var split = Assert.IsType<DockSplit>(result);
        // root first, new sibling at bottom.
        Assert.Equal(DockGroupRole.DocumentArea, ((DockTabGroup)split.Children[0]).Role);
        Assert.Equal(DockGroupRole.ToolWindowStrip, ((DockTabGroup)split.Children[1]).Role);
    }

    [Fact]
    public void SplitLeft_WithDocument_AgainstDocumentArea_StaysDocumentArea()
    {
        DockNode root = new DockTabGroup(new[] { Doc("d1") }, Role: DockGroupRole.DocumentArea);
        var result = DockLayoutMutator.InsertPaneAtTarget(root, Doc("d2"), DockTarget.SplitLeft);
        var split = Assert.IsType<DockSplit>(result);
        Assert.Equal(DockGroupRole.DocumentArea, ((DockTabGroup)split.Children[0]).Role);
        Assert.Equal(DockGroupRole.DocumentArea, ((DockTabGroup)split.Children[1]).Role);
    }

    // ── MovePaneToGroupTarget — sibling role from target group (§2.3) ────

    [Fact]
    public void MovePaneToGroupTarget_SplitRight_PropagatesDocumentAreaRole()
    {
        // Two docs in a DocumentArea group; drag one to SplitRight of the
        // group. New sibling must also be DocumentArea.
        var target = new DockTabGroup(new[] { Doc("d1"), Doc("d2") },
            SelectedIndex: 0, Role: DockGroupRole.DocumentArea);
        DockNode root = target;
        var result = DockLayoutMutator.MovePaneToGroupTarget(root, target.Documents[1], target, DockTarget.SplitRight);
        var split = Assert.IsType<DockSplit>(result);
        Assert.Equal(DockGroupRole.DocumentArea, ((DockTabGroup)split.Children[0]).Role);
        Assert.Equal(DockGroupRole.DocumentArea, ((DockTabGroup)split.Children[1]).Role);
    }

    [Fact]
    public void MovePaneToGroupTarget_SplitRight_AgainstGeneral_FallsBackToPaneInference()
    {
        // Target is General — sibling role inferred from pane (Document → DocumentArea).
        var target = new DockTabGroup(new[] { Doc("d1"), Doc("d2") },
            SelectedIndex: 0, Role: DockGroupRole.General);
        DockNode root = target;
        var result = DockLayoutMutator.MovePaneToGroupTarget(root, target.Documents[1], target, DockTarget.SplitRight);
        var split = Assert.IsType<DockSplit>(result);
        Assert.Equal(DockGroupRole.General, ((DockTabGroup)split.Children[0]).Role);
        Assert.Equal(DockGroupRole.DocumentArea, ((DockTabGroup)split.Children[1]).Role);
    }
}
