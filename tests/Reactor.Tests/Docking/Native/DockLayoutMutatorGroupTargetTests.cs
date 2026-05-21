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
}
