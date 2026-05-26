using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking.Native;

/// <summary>
/// Spec 045 §2.3 — per-tab-group drop target mutator. Covers the
/// add-as-tab + split-relative-to-target-group operations exposed by
/// the per-group overlay.
/// </summary>
public class DockLayoutMutatorGroupTargetTests
{
    [Fact]
    public void GroupTarget_Center_FoldsPaneIntoTargetGroup()
    {
        // Use a 3-group split so removing docA from groupLeft doesn't
        // collapse the entire split — keeps the tree shape predictable.
        var docA = new Document { Title = "A", Key = "a" };
        var docZ = new Document { Title = "Z", Key = "z" };
        var docB = new Document { Title = "B", Key = "b" };
        var docC = new Document { Title = "C", Key = "c" };
        var groupLeft = new DockTabGroup(new DockableContent[] { docA, docZ });
        var groupRight = new DockTabGroup(new DockableContent[] { docB, docC });
        var root = new DockSplit(Orientation.Horizontal, new DockNode[] { groupLeft, groupRight });

        var result = DockLayoutMutator.MovePaneToGroupTarget(root, docA, groupRight, DockTarget.Center);

        Assert.NotNull(result);
        var split = Assert.IsType<DockSplit>(result);
        Assert.Equal(2, split.Children.Count);
        // Left group lost docA (now just {docZ}); right group gained docA.
        var leftAfter = Assert.IsType<DockTabGroup>(split.Children[0]);
        Assert.Single(leftAfter.Documents);
        Assert.Same(docZ, leftAfter.Documents[0]);
        var rightAfter = Assert.IsType<DockTabGroup>(split.Children[1]);
        Assert.Equal(3, rightAfter.Documents.Count);
        Assert.Same(docB, rightAfter.Documents[0]);
        Assert.Same(docC, rightAfter.Documents[1]);
        Assert.Same(docA, rightAfter.Documents[2]);
    }

    [Fact]
    public void GroupTarget_SplitRight_WrapsTargetGroupInHorizontalSplit()
    {
        var docA = new Document { Title = "A", Key = "a" };
        var docZ = new Document { Title = "Z", Key = "z" };
        var docB = new Document { Title = "B", Key = "b" };
        var docC = new Document { Title = "C", Key = "c" };
        var groupLeft = new DockTabGroup(new DockableContent[] { docA, docZ });
        var groupRight = new DockTabGroup(new DockableContent[] { docB, docC });
        var root = new DockSplit(Orientation.Horizontal, new DockNode[] { groupLeft, groupRight });

        // Drag docA out of groupLeft → split groupRight on its right.
        var result = DockLayoutMutator.MovePaneToGroupTarget(root, docA, groupRight, DockTarget.SplitRight);

        Assert.NotNull(result);
        // Outer split still has 2 children: leftAfter (groupLeft minus
        // docA) and the new horizontal split for groupRight + docA.
        var outerSplit = Assert.IsType<DockSplit>(result);
        Assert.Equal(2, outerSplit.Children.Count);
        var leftAfter = Assert.IsType<DockTabGroup>(outerSplit.Children[0]);
        Assert.Same(docZ, leftAfter.Documents[0]);
        var innerSplit = Assert.IsType<DockSplit>(outerSplit.Children[1]);
        Assert.Equal(Orientation.Horizontal, innerSplit.Orientation);
        Assert.Equal(2, innerSplit.Children.Count);
        var origRight = Assert.IsType<DockTabGroup>(innerSplit.Children[0]);
        Assert.Same(docB, origRight.Documents[0]);
        var newPaneGroup = Assert.IsType<DockTabGroup>(innerSplit.Children[1]);
        Assert.Same(docA, newPaneGroup.Documents[0]);
    }

    [Fact]
    public void GroupTarget_SplitTop_WrapsInVerticalSplitLeadingNewPane()
    {
        var docA = new Document { Title = "A", Key = "a" };
        var docZ = new Document { Title = "Z", Key = "z" };
        var docB = new Document { Title = "B", Key = "b" };
        var groupTarget = new DockTabGroup(new DockableContent[] { docA });
        var groupOther = new DockTabGroup(new DockableContent[] { docB, docZ });
        var root = new DockSplit(Orientation.Horizontal, new DockNode[] { groupTarget, groupOther });

        // Drag docB out of groupOther → SplitTop on groupTarget. Result:
        // outer split now has [vSplit(docB / groupTarget), groupOther-after].
        var result = DockLayoutMutator.MovePaneToGroupTarget(root, docB, groupTarget, DockTarget.SplitTop);

        Assert.NotNull(result);
        var hSplit = Assert.IsType<DockSplit>(result);
        Assert.Equal(2, hSplit.Children.Count);
        var vSplit = Assert.IsType<DockSplit>(hSplit.Children[0]);
        Assert.Equal(Orientation.Vertical, vSplit.Orientation);
        var topGroup = Assert.IsType<DockTabGroup>(vSplit.Children[0]);
        Assert.Same(docB, topGroup.Documents[0]);
        var bottomGroup = Assert.IsType<DockTabGroup>(vSplit.Children[1]);
        Assert.Same(docA, bottomGroup.Documents[0]);
        var rightAfter = Assert.IsType<DockTabGroup>(hSplit.Children[1]);
        Assert.Same(docZ, rightAfter.Documents[0]);
    }

    [Fact]
    public void GroupTarget_PaneNotFound_ReturnsOriginalRoot()
    {
        var docA = new Document { Title = "A", Key = "a" };
        var docB = new Document { Title = "B", Key = "b" };
        var orphan = new Document { Title = "X", Key = "x" };
        var group = new DockTabGroup(new DockableContent[] { docA, docB });
        var root = group;

        var result = DockLayoutMutator.MovePaneToGroupTarget(root, orphan, group, DockTarget.Center);

        Assert.Same(root, result);
    }

    [Fact]
    public void GroupTarget_SameGroupReDrop_StillAddsAsTab()
    {
        // Drop docA from groupX back onto groupX as a tab — should be a
        // no-op (or land at the end). After remove, groupX has just docB;
        // after re-insert at Center, docA is appended.
        var docA = new Document { Title = "A", Key = "a" };
        var docB = new Document { Title = "B", Key = "b" };
        var group = new DockTabGroup(new DockableContent[] { docA, docB });

        var result = DockLayoutMutator.MovePaneToGroupTarget(group, docA, group, DockTarget.Center);

        Assert.NotNull(result);
        var resultGroup = Assert.IsType<DockTabGroup>(result);
        Assert.Equal(2, resultGroup.Documents.Count);
        Assert.Same(docB, resultGroup.Documents[0]);
        Assert.Same(docA, resultGroup.Documents[1]);
    }

    [Fact]
    public void GroupTarget_NullRoot_ReturnsNull()
    {
        var docA = new Document { Title = "A", Key = "a" };
        var group = new DockTabGroup(new DockableContent[] { docA });
        var result = DockLayoutMutator.MovePaneToGroupTarget(null, docA, group, DockTarget.Center);
        Assert.Null(result);
    }

    // ─── Scene J south-drop regression ──────────────────────────────────
    //
    //  Reproduces the user-reported crash: in the dock-showcase Scene J,
    //  open 2 documents in the middle DocumentArea, drag one to the south
    //  of the other (SplitBottom on the same group). The mutator must
    //  produce a well-formed tree — same set of leaf keys, vertical split
    //  containing both DocumentArea groups, source group with the
    //  remaining doc, sibling group with the moved doc.

    [Fact]
    public void SceneJSouthDrop_TwoDocsInDocumentArea_DropSouth_ProducesNestedVerticalSplit()
    {
        // Initial layout — exactly the Scene-J shape after 2 docs opened.
        var doc1 = new Document { Title = "Document 1.md", Key = "j:doc:1" };
        var doc2 = new Document { Title = "Document 2.md", Key = "j:doc:2" };
        var galleryTool = new ToolWindow { Title = "Gallery", Key = "j:tool:gallery" };
        var configTool  = new ToolWindow { Title = "Config",  Key = "j:tool:config" };

        var documentArea = new DockTabGroup(
            new DockableContent[] { doc1, doc2 },
            Role: DockGroupRole.DocumentArea);
        var leftStrip = new DockTabGroup(
            new DockableContent[] { galleryTool },
            Width: 240, Role: DockGroupRole.ToolWindowStrip);
        var rightStrip = new DockTabGroup(
            new DockableContent[] { configTool },
            Width: 280, Role: DockGroupRole.ToolWindowStrip);
        var root = new DockSplit(Orientation.Horizontal,
            new DockNode[] { leftStrip, documentArea, rightStrip });

        // Drag doc1 to the south of the DocumentArea (where doc2 lives).
        // The per-group overlay routes through MovePaneToGroupTarget.
        var result = DockLayoutMutator.MovePaneToGroupTarget(
            root, doc1, documentArea, DockTarget.SplitBottom);

        Assert.NotNull(result);

        // Top-level shape unchanged: still a horizontal split with 3
        // children (left strip / center / right strip).
        var outer = Assert.IsType<DockSplit>(result);
        Assert.Equal(Orientation.Horizontal, outer.Orientation);
        Assert.Equal(3, outer.Children.Count);

        // Left/right strips untouched.
        var leftAfter = Assert.IsType<DockTabGroup>(outer.Children[0]);
        Assert.Same(galleryTool, leftAfter.Documents[0]);
        Assert.Equal(DockGroupRole.ToolWindowStrip, leftAfter.Role);
        var rightAfter = Assert.IsType<DockTabGroup>(outer.Children[2]);
        Assert.Same(configTool, rightAfter.Documents[0]);
        Assert.Equal(DockGroupRole.ToolWindowStrip, rightAfter.Role);

        // Center child is now a vertical split: original DocumentArea (with
        // doc2 only) above, new DocumentArea sibling (with doc1) below.
        var center = Assert.IsType<DockSplit>(outer.Children[1]);
        Assert.Equal(Orientation.Vertical, center.Orientation);
        Assert.Equal(2, center.Children.Count);

        var topGroup = Assert.IsType<DockTabGroup>(center.Children[0]);
        Assert.Single(topGroup.Documents);
        Assert.Same(doc2, topGroup.Documents[0]);
        Assert.Equal(DockGroupRole.DocumentArea, topGroup.Role);

        var bottomGroup = Assert.IsType<DockTabGroup>(center.Children[1]);
        Assert.Single(bottomGroup.Documents);
        Assert.Same(doc1, bottomGroup.Documents[0]);
        // Spec 046 §2.3: splitting a Document inside a DocumentArea —
        // the new sibling inherits the target group's DocumentArea role.
        Assert.Equal(DockGroupRole.DocumentArea, bottomGroup.Role);
    }

    [Fact]
    public void SceneJSouthDrop_NoLeafLost_KeyCountStable()
    {
        // Same scenario; assert the dropped pane's key is still reachable
        // by walking the tree (catches a regression where RemovePane
        // collapses or InsertPane misroutes through a fallback that
        // drops the pane).
        var doc1 = new Document { Title = "Document 1.md", Key = "j:doc:1" };
        var doc2 = new Document { Title = "Document 2.md", Key = "j:doc:2" };
        var documentArea = new DockTabGroup(
            new DockableContent[] { doc1, doc2 },
            Role: DockGroupRole.DocumentArea);
        var root = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new DockableContent[] { new ToolWindow { Key = "tool" } },
                Role: DockGroupRole.ToolWindowStrip),
            documentArea,
            new DockTabGroup(new DockableContent[] { new ToolWindow { Key = "tool2" } },
                Role: DockGroupRole.ToolWindowStrip),
        });

        var result = DockLayoutMutator.MovePaneToGroupTarget(
            root, doc1, documentArea, DockTarget.SplitBottom);

        var keys = CollectLeafKeys(result);
        Assert.Contains("j:doc:1", keys);
        Assert.Contains("j:doc:2", keys);
        Assert.Contains("tool",    keys);
        Assert.Contains("tool2",   keys);
        Assert.Equal(4, keys.Count);
    }

    private static List<string> CollectLeafKeys(DockNode? node)
    {
        var keys = new List<string>();
        void Walk(DockNode? n)
        {
            switch (n)
            {
                case DockableContent c: if (c.Key is string k) keys.Add(k); break;
                case DockTabGroup g: foreach (var d in g.Documents) Walk(d); break;
                case DockSplit s: foreach (var c in s.Children) Walk(c); break;
            }
        }
        Walk(node);
        return keys;
    }
}
