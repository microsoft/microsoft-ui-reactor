using System;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Input;
using Xunit;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// Headless coverage for <see cref="ReactorDiagnostics.GetReferenceEdges"/>: the projection
/// of the reconciler's reference bookkeeping. Resolved targets need a mounted control and
/// are covered by the <c>Diagnostics_ReferenceEdges</c> selftest; everything about pending
/// edges, naming, list fan-out, ordering and the AutomationId form is pinned here.
/// </summary>
public class ReactorDiagnosticsReferenceEdgesTests
{
    private static ReferenceEdgeBag Bag() => new();

    private static void AddScalar(ReferenceEdgeBag bag, int slot, ElementRef? cell)
        => bag.Edges[slot] = new ReferenceEdge { Cell = cell };

    private static void AddList(ReferenceEdgeBag bag, int slot, params ElementRef[] cells)
        => AddList(bag, slot, cells, cells.Distinct().ToArray());

    // `cells` as authored; `subscribed` as WireReferenceListEdge leaves its deduplicated
    // bookkeeping (which keeps retained cells in their earlier order).
    private static void AddList(ReferenceEdgeBag bag, int slot, ElementRef[] cells, ElementRef[] subscribed)
    {
        var edge = new ReferenceListEdge { Authored = cells };
        edge.Cells.AddRange(subscribed);
        bag.ListEdges[slot] = edge;
    }

    [Fact]
    public void ListEdge_ReportsTheAuthoredSequence_NotTheSubscriptionSet()
    {
        var a = new ElementRef<WinUI.Button>(new ElementRef());
        var b = new ElementRef<WinUI.TextBlock>(new ElementRef());
        var bag = Bag();
        // Re-authored from [a, b] to [b, a, a]: the subscription set still reads [a, b].
        AddList(bag, ReferenceSlots.ModifierRef_FlowsTo, cells: [b, a, a], subscribed: [a, b]);

        var edges = ReferenceEdgeMap.Describe(bag, null, null, null);

        Assert.Equal(new[] { ("TextBlock", 0), ("Button", 1), ("Button", 2) },
            edges.Select(e => (e.ExpectedTargetTypeName!, e.Index)));
    }

    [Fact]
    public void EffectiveLabeledById_TheOutermostDecoratorWins()
    {
        var inner = Microsoft.UI.Reactor.Factories.Button("x").LabeledBy("inner-label");
        var outerOnly = Microsoft.UI.Reactor.Factories.Flyout(Microsoft.UI.Reactor.Factories.Button("x"), Microsoft.UI.Reactor.Factories.TextBlock("m")).LabeledBy("outer-label");
        var both = Microsoft.UI.Reactor.Factories.Flyout(inner, Microsoft.UI.Reactor.Factories.TextBlock("m")).LabeledBy("outer-label");
        var innerOnly = Microsoft.UI.Reactor.Factories.Flyout(inner, Microsoft.UI.Reactor.Factories.TextBlock("m"));

        Assert.Equal("outer-label", ReferenceEdgeMap.EffectiveLabeledById(Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.DecoratorChain(outerOnly)));
        Assert.Equal("outer-label", ReferenceEdgeMap.EffectiveLabeledById(Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.DecoratorChain(both)));
        Assert.Equal("inner-label", ReferenceEdgeMap.EffectiveLabeledById(Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.DecoratorChain(innerOnly)));
    }

    [Fact]
    public void PendingModifierRef_IsReportedUnresolved()
    {
        var bag = Bag();
        AddScalar(bag, ReferenceSlots.ModifierRef_XYFocusRight, new ElementRef());

        var edge = Assert.Single(ReferenceEdgeMap.Describe(bag, null, null, null));

        Assert.Equal(new ReferenceEdgeSnapshot("XYFocusRight", false, 0, null, null, false, null), edge);
    }

    [Fact]
    public void TypedRef_ReportsItsExpectedTargetTypeName()
    {
        var typed = new ElementRef<WinUI.Button>(new ElementRef());
        var bag = Bag();
        AddScalar(bag, ReferenceSlots.ModifierRef_LabeledBy, typed);

        Assert.Equal("Button", Assert.Single(ReferenceEdgeMap.Describe(bag, null, null, null)).ExpectedTargetTypeName);
    }

    [Fact]
    public void UnsetScalarSlot_IsNotAnEdge()
    {
        var bag = Bag();
        AddScalar(bag, ReferenceSlots.ModifierRef_XYFocusUp, null);

        Assert.Empty(ReferenceEdgeMap.Describe(bag, null, null, null));
    }

    [Fact]
    public void ListEdge_FansOutOneEdgePerTarget_InAuthoredOrder()
    {
        var bag = Bag();
        AddList(bag, ReferenceSlots.ModifierRef_DescribedBy, new ElementRef(), new ElementRef<WinUI.TextBlock>(new ElementRef()));

        var edges = ReferenceEdgeMap.Describe(bag, null, null, null);

        Assert.Equal(new[]
        {
            new ReferenceEdgeSnapshot("DescribedBy", true, 0, null, null, false, null),
            new ReferenceEdgeSnapshot("DescribedBy", true, 1, null, "TextBlock", false, null),
        }, edges);
    }

    [Fact]
    public void DescriptorBindingAndModifierEdges_AreLabeled_AndOrdered()
    {
        var bag = Bag();
        AddScalar(bag, ReferenceSlots.ModifierRef_XYFocusDown, new ElementRef());
        AddScalar(bag, ReferenceSlots.BindingBase + 2, new ElementRef());
        AddScalar(bag, 0, new ElementRef());

        var edges = ReferenceEdgeMap.Describe(bag, null, null, null);

        Assert.Equal(new[] { "reference#0", "binding#2", "XYFocusDown" }, edges.Select(e => e.Property));
    }

    [Fact]
    public void PendingAutomationIdLabeledBy_IsReportedUnresolved()
    {
        var edge = Assert.Single(ReferenceEdgeMap.Describe(null, pendingLabeledBy: "nameLabel", authoredLabeledBy: null, resolvedLabeledBy: null));

        Assert.Equal(new ReferenceEdgeSnapshot("LabeledBy", false, 0, "nameLabel", null, false, null), edge);
    }

    [Fact]
    public void AuthoredAutomationIdLabeledBy_WithoutALiveMatch_IsUnresolved()
    {
        // The id was authored but the live LabeledBy is not the control it names (no
        // such AutomationId in the tree, or an ElementRef edge wrote LabeledBy instead).
        var edge = Assert.Single(ReferenceEdgeMap.Describe(null, null, authoredLabeledBy: "nameLabel", resolvedLabeledBy: null));

        Assert.Equal("nameLabel", edge.TargetAutomationId);
        Assert.False(edge.IsResolved);
    }

    [Fact]
    public void BothLabeledByForms_AreAdjacent_BeforeLaterProperties()
    {
        var bag = Bag();
        AddScalar(bag, ReferenceSlots.ModifierRef_LabeledBy, new ElementRef());
        AddList(bag, ReferenceSlots.ModifierRef_DescribedBy, new ElementRef());
        AddScalar(bag, ReferenceSlots.ModifierRef_XYFocusUp, new ElementRef());

        var edges = ReferenceEdgeMap.Describe(bag, pendingLabeledBy: "nameLabel", null, null);

        Assert.Equal(new[] { ("LabeledBy", (string?)null), ("LabeledBy", "nameLabel"), ("DescribedBy", null), ("XYFocusUp", null) },
            edges.Select(e => (e.Property, e.TargetAutomationId)));
    }

    [Fact]
    public void AutomationIdAndRefLabeledBy_AreReportedAsSeparateEdges()
    {
        var bag = Bag();
        AddScalar(bag, ReferenceSlots.ModifierRef_LabeledBy, new ElementRef());

        var edges = ReferenceEdgeMap.Describe(bag, pendingLabeledBy: "nameLabel", null, null);

        Assert.Equal(2, edges.Count);
        Assert.All(edges, e => Assert.Equal("LabeledBy", e.Property));
        Assert.Null(edges[0].TargetAutomationId);
        Assert.Equal("nameLabel", edges[1].TargetAutomationId);
    }

    [Fact]
    public void NoBookkeeping_ReportsNothing()
    {
        Assert.Empty(ReferenceEdgeMap.Describe(null, null, null, null));
        Assert.Empty(ReferenceEdgeMap.Describe(Bag(), null, null, null));
    }

    [Theory]
    [InlineData(ReferenceSlots.ModifierRef_LabeledById, "LabeledBy")]
    [InlineData(ReferenceSlots.ModifierRef_ToolTipPlacementTarget, "ToolTipPlacementTarget")]
    [InlineData(200_099, "modifier#200099")]
    [InlineData(3, "reference#3")]
    public void SlotLabels(int slot, string expected) => Assert.Equal(expected, ReferenceSlots.Label(slot));

    [Fact]
    public void LabeledByIdPseudoSlot_DoesNotCollideWithARealModifierSlot()
    {
        var real = typeof(ReferenceSlots)
            .GetFields(global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Static)
            .Where(f => f.Name.StartsWith("ModifierRef_", StringComparison.Ordinal) && f.Name != nameof(ReferenceSlots.ModifierRef_LabeledById))
            .Select(f => (int)f.GetValue(null)!);

        Assert.DoesNotContain(ReferenceSlots.ModifierRef_LabeledById, real);
    }
}
