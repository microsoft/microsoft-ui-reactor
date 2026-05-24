using System;
using System.Linq;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Spec 046 §6.4 — public group-targeted <see cref="DockHostModel.Dock(DockableContent, DockTabGroup, DockTarget)"/>
/// overload and its supporting mutator entry
/// <see cref="DockLayoutMutator.TryInsertPaneAtGroupTarget"/>. Covers
/// reference resolution, structural-equality resolution, unresolvable
/// group (no-op + diagnostic), and the trust contract (no role check).
/// </summary>
public class DockGroupTargetTests
{
    private static Document Doc(string key) => new() { Title = key, Key = key };
    private static ToolWindow Tool(string key) => new() { Title = key, Key = key };

    // ── DockLayoutMutator.TryInsertPaneAtGroupTarget ──────────────────────

    [Fact]
    public void TryInsertPaneAtGroupTarget_NullRoot_ReturnsNull()
    {
        var target = new DockTabGroup(new[] { (DockableContent)Doc("d1") });
        var result = DockLayoutMutator.TryInsertPaneAtGroupTarget(null, Doc("d2"), target, DockTarget.Center);
        Assert.Null(result);
    }

    [Fact]
    public void TryInsertPaneAtGroupTarget_ByReference_Center()
    {
        var target = new DockTabGroup(new[] { (DockableContent)Doc("d1") },
            SelectedIndex: 0, Role: DockGroupRole.DocumentArea);
        DockNode root = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new[] { (DockableContent)Tool("t1") }, Role: DockGroupRole.ToolWindowStrip),
            target,
        });
        var result = DockLayoutMutator.TryInsertPaneAtGroupTarget(root, Doc("d2"), target, DockTarget.Center);
        Assert.NotNull(result);
        var split = Assert.IsType<DockSplit>(result);
        var landedGroup = (DockTabGroup)split.Children[1];
        Assert.Equal(2, landedGroup.Documents.Count);
        Assert.Equal("d2", landedGroup.Documents[1].Key);
    }

    [Fact]
    public void TryInsertPaneAtGroupTarget_ByStructuralEquality_Center()
    {
        // Caller built a fresh record matching the layout group structurally.
        var d1 = Doc("d1");
        var originalGroup = new DockTabGroup(new[] { (DockableContent)d1 },
            SelectedIndex: 0, Role: DockGroupRole.DocumentArea);
        DockNode root = originalGroup;
        var equivalent = new DockTabGroup(new[] { (DockableContent)d1 },
            SelectedIndex: 0, Role: DockGroupRole.DocumentArea);
        Assert.NotSame(originalGroup, equivalent);
        var result = DockLayoutMutator.TryInsertPaneAtGroupTarget(root, Doc("d2"), equivalent, DockTarget.Center);
        Assert.NotNull(result);
        var g = Assert.IsType<DockTabGroup>(result);
        Assert.Equal(2, g.Documents.Count);
    }

    [Fact]
    public void TryInsertPaneAtGroupTarget_StaleGroup_ReturnsNull()
    {
        var stale = new DockTabGroup(new[] { (DockableContent)Doc("ghost") },
            SelectedIndex: 0, Role: DockGroupRole.General);
        DockNode root = new DockTabGroup(new[] { (DockableContent)Doc("d1") },
            SelectedIndex: 0, Role: DockGroupRole.General);
        var result = DockLayoutMutator.TryInsertPaneAtGroupTarget(root, Doc("d2"), stale, DockTarget.Center);
        Assert.Null(result);
    }

    [Fact]
    public void TryInsertPaneAtGroupTarget_TrustContract_PlacesDocumentIntoToolWindowStrip()
    {
        // Spec §9 Q3: strategies using the group-target overload are
        // trusted; no role compatibility re-check.
        var stripGroup = new DockTabGroup(new[] { (DockableContent)Tool("t1") },
            SelectedIndex: 0, Role: DockGroupRole.ToolWindowStrip);
        DockNode root = stripGroup;
        var result = DockLayoutMutator.TryInsertPaneAtGroupTarget(
            root, Doc("forced"), stripGroup, DockTarget.Center);
        var g = Assert.IsType<DockTabGroup>(result);
        Assert.Equal(2, g.Documents.Count);
        Assert.Equal("forced", g.Documents[1].Key);
        // Role unchanged — we didn't auto-promote.
        Assert.Equal(DockGroupRole.ToolWindowStrip, g.Role);
    }

    [Fact]
    public void TryInsertPaneAtGroupTarget_SplitRight_PropagatesRole()
    {
        var docArea = new DockTabGroup(new[] { (DockableContent)Doc("d1") },
            SelectedIndex: 0, Role: DockGroupRole.DocumentArea);
        DockNode root = docArea;
        var result = DockLayoutMutator.TryInsertPaneAtGroupTarget(
            root, Doc("d2"), docArea, DockTarget.SplitRight);
        var split = Assert.IsType<DockSplit>(result);
        Assert.All(split.Children, c => Assert.Equal(DockGroupRole.DocumentArea, ((DockTabGroup)c).Role));
    }

    // ── DockHostModel.Dock(content, group, target) overload ──────────────

    [Fact]
    public void DockHostModel_GroupTargetOverload_QueuesDockToGroupOp()
    {
        var model = new DockHostModel();
        var grp = new DockTabGroup(new[] { (DockableContent)Doc("d1") });
        model.Dock(Doc("d2"), grp, DockTarget.Center);
        Assert.Single(model.Pending);
        var op = Assert.IsType<PendingMutation.DockToGroupOp>(model.Pending[0]);
        Assert.Equal("d2", op.Content.Key);
        Assert.Same(grp, op.TargetGroup);
        Assert.Equal(DockTarget.Center, op.Target);
    }

    [Fact]
    public void DockHostModel_GroupTargetOverload_NullContent_Throws()
    {
        var model = new DockHostModel();
        var grp = new DockTabGroup(new[] { (DockableContent)Doc("d1") });
        Assert.Throws<ArgumentNullException>(() => model.Dock(null!, grp, DockTarget.Center));
    }

    [Fact]
    public void DockHostModel_GroupTargetOverload_NullGroup_Throws()
    {
        var model = new DockHostModel();
        Assert.Throws<ArgumentNullException>(() => model.Dock(Doc("d1"), (DockTabGroup)null!, DockTarget.Center));
    }

    [Fact]
    public void DockHostModel_GroupTargetOverload_FiresOnMutationQueued()
    {
        var model = new DockHostModel();
        int callbackCount = 0;
        model.OnMutationQueued = () => callbackCount++;
        var grp = new DockTabGroup(new[] { (DockableContent)Doc("d1") });
        model.Dock(Doc("d2"), grp, DockTarget.Center);
        Assert.Equal(1, callbackCount);
    }
}
