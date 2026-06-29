using Microsoft.UI.Reactor.Layout;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #681 (correctness) — Yoga measurement-cache contract underpinning the
/// FlexPanel cross-axis staleness guard.
///
/// Yoga caches per-leaf measure results keyed by the constraint (available size
/// + sizing mode), NOT by content. So if a measure-backed leaf's content changes
/// but the constraint passed to it is identical, the cache can serve a stale
/// size. Marking the leaf dirty is what clears that cache (see the
/// <c>needToVisitNode</c> reset in <c>YogaAlgorithm</c>). FlexPanel relies on
/// this by re-dirtying every child node every MeasureOverride.
///
/// These tests pin the contract from both sides so that any future
/// "skip re-dirty on unchanged value" optimization (the #742 danger zone) that
/// would suppress the dirty is caught here.
/// </summary>
public class YogaMeasureCacheStalenessTests
{
    private static (YogaNode root, YogaNode leaf, Func<float> getMeasured) BuildRootWithMeasureLeaf(
        Func<float> contentHeight)
    {
        var config = new YogaConfig();
        var root = new YogaNode(config)
        {
            FlexDirection = FlexDirection.Column,
            Width = YogaValue.Point(100f),
            // Height is auto so it tracks the leaf's measured (main-axis) size.
        };
        var leaf = new YogaNode(config);
        // Constant cross-axis width, content-driven height. The constraint Yoga
        // passes is identical across passes; only the returned size changes.
        leaf.MeasureFunction = (n, w, wm, h, hm) => new YogaSize(50f, contentHeight());
        root.InsertChild(leaf, 0);
        return (root, leaf, contentHeight);
    }

    [Fact]
    public void MeasureLeaf_DirtyAfterContentChange_RemeasuresForIdenticalConstraint()
    {
        float measured = 20f;
        var (root, leaf, _) = BuildRootWithMeasureLeaf(() => measured);

        root.CalculateLayout(100f, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(20f, leaf.LayoutHeight);
        Assert.Equal(20f, root.LayoutHeight);

        // Content changes (taller) — same constraint. FlexPanel would mark the
        // leaf dirty here; do the same. The new size must flow through.
        measured = 40f;
        leaf.MarkDirty();

        root.CalculateLayout(100f, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(40f, leaf.LayoutHeight);
        Assert.Equal(40f, root.LayoutHeight);
    }

    [Fact]
    public void MeasureLeaf_NotDirtied_ServesStaleCachedSize_NegativeControl()
    {
        // Negative control: this is exactly the staleness the #138 setter guards
        // could trigger — without dirtying, the constraint-keyed cache serves the
        // old size even though the measure function would now return a new one.
        float measured = 20f;
        var (root, leaf, _) = BuildRootWithMeasureLeaf(() => measured);

        root.CalculateLayout(100f, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(20f, leaf.LayoutHeight);

        // Content changes but the leaf is NOT dirtied and the constraint is
        // identical → the cache serves the stale 20.
        measured = 40f;
        root.CalculateLayout(100f, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(20f, leaf.LayoutHeight);
    }
}
