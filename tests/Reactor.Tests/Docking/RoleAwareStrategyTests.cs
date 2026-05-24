using System;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Spec 046 §6.4 + Phase 8.6 — interaction between
/// <see cref="IDockLayoutStrategy"/> and the new group-targeted
/// <see cref="DockHostModel.Dock(DockableContent, DockTabGroup, DockTarget)"/>
/// overload. Strategies that take responsibility for placement bypass the
/// role compatibility check; the model trusts the strategy's choice (§9 Q3).
/// </summary>
public class RoleAwareStrategyTests
{
    private static Document Doc(string key) => new() { Title = key, Key = key };
    private static ToolWindow Tool(string key, DockSides allowed = DockSides.All) =>
        new() { Title = key, Key = key, AllowedSides = allowed };

    /// <summary>
    /// Strategy that captures a group reference at construction and uses
    /// the new group-targeted overload from BeforeInsertDocument. Returning
    /// true short-circuits the default Dock(content, target) routing.
    /// </summary>
    private sealed class GroupTargetingStrategy : IDockLayoutStrategy
    {
        private readonly DockTabGroup _target;
        public int BeforeInsertCalls { get; private set; }
        public GroupTargetingStrategy(DockTabGroup target) { _target = target; }
        public bool BeforeInsertDocument(DockHostModel model, Document doc)
        {
            BeforeInsertCalls++;
            model.Dock(doc, _target, DockTarget.Center);
            return true;
        }
    }

    private sealed class BypassMaskStrategy : IDockLayoutStrategy
    {
        public bool BeforeInsertToolWindow(DockHostModel model, ToolWindow tw)
        {
            // Strategy clears the mask via record-with before pinning.
            model.PinToSide(tw with { AllowedSides = DockSides.All }, DockSide.Left);
            return true;
        }
    }

    [Fact]
    public void Strategy_UsingGroupOverload_QueuesDockToGroupOpAndSkipsDefault()
    {
        // Layout has a single empty DocumentArea — but the strategy targets
        // a ToolWindowStrip explicitly (force-placement via the trusted
        // overload).
        var target = new DockTabGroup(new[] { (DockableContent)Tool("t1") },
            Role: DockGroupRole.ToolWindowStrip);
        var model = new DockHostModel { LayoutStrategy = new GroupTargetingStrategy(target) };
        var doc = Doc("d1");
        model.Dock(doc, DockTarget.Center);
        // Strategy queued ONE DockToGroupOp (no DockOp from the default path).
        var op = Assert.Single(model.Pending);
        var grp = Assert.IsType<PendingMutation.DockToGroupOp>(op);
        Assert.Same(target, grp.TargetGroup);
        Assert.Same(doc, grp.Content);
    }

    [Fact]
    public void Strategy_ReturnsFalse_DefaultRouteRunsRoleAware()
    {
        // No-op strategy returns false — model uses default Dock(Center)
        // routing, which is role-aware.
        IDockLayoutStrategy passThrough = new PassThroughStrategy();
        var model = new DockHostModel { LayoutStrategy = passThrough };
        model.Dock(Doc("d1"), DockTarget.Center);
        var op = Assert.Single(model.Pending);
        Assert.IsType<PendingMutation.DockOp>(op);
    }

    private sealed class PassThroughStrategy : IDockLayoutStrategy { }

    [Fact]
    public void Strategy_BypassesMaskByMutatingTool_PinToSideSucceeds()
    {
        // ToolWindow declares AllowedSides=Bottom; the strategy bypasses
        // by passing a mutated copy with AllowedSides=All. No throw.
        var model = new DockHostModel { LayoutStrategy = new BypassMaskStrategy() };
        var tw = Tool("errors", DockSides.Bottom);
        var ex = Record.Exception(() => model.Dock(tw, DockTarget.Center));
        // BeforeInsertToolWindow runs first, calls PinToSide(tw-modified, Left).
        // No exception expected.
        Assert.Null(ex);
        var op = Assert.Single(model.Pending);
        var pin = Assert.IsType<PendingMutation.PinToSideOp>(op);
        Assert.Equal(DockSide.Left, pin.Side);
        Assert.Equal(DockSides.All, pin.ToolWindow.AllowedSides);
    }

    [Fact]
    public void TryInsertPaneAtGroupTarget_PreservesTargetGroupRole_NoAutoPromote()
    {
        // Force-place a Document into a ToolWindowStrip group via the
        // mutator's strict overload. Spec §9 Q3: trust the placement,
        // do NOT promote/re-role the target group.
        var stripGroup = new DockTabGroup(new[] { (DockableContent)Tool("t1") },
            Role: DockGroupRole.ToolWindowStrip);
        DockNode root = stripGroup;
        var result = DockLayoutMutator.TryInsertPaneAtGroupTarget(
            root, Doc("forced"), stripGroup, DockTarget.Center);
        var g = Assert.IsType<DockTabGroup>(result);
        Assert.Equal(DockGroupRole.ToolWindowStrip, g.Role);
        Assert.Equal(2, g.Documents.Count);
    }
}
