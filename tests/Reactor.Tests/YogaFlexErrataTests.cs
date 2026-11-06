using Microsoft.UI.Reactor.Layout;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Errata-dependent flex distribution tests ported from upstream facebook/yoga:
/// <c>tests/YGFirstChildMinWidthTest.cpp</c> (73bebc0d, #2021 — the first flex pass
/// against the original totals, behind <see cref="YogaErrata.FlexFirstPassUsesRunningTotals"/>)
/// and the <c>withoutErrata</c> half of <c>tests/YGFlexShrinkAllFrozenTest.cpp</c>
/// (aa6ede70, #2038/#1651). The default-errata half of the latter lives in
/// <see cref="YogaUpstreamFixTests"/>.
/// </summary>
public class YogaFlexErrataTests
{
    // ── YGFirstChildMinWidthTest.cpp (#2021) ──

    private static YogaConfig MakeFixedConfig()
    {
        var config = new YogaConfig();
        config.SetErrata(config.Errata & (YogaErrata.All ^ YogaErrata.FlexFirstPassUsesRunningTotals));
        return config;
    }

    private static float[] LayoutRow(YogaConfig config, float minWidth0, float minWidth1, float minWidth2)
    {
        var root = new YogaNode(config);
        root.Width = YogaValue.Point(540f);
        root.FlexDirection = FlexDirection.Row;

        float[] minWidths = { minWidth0, minWidth1, minWidth2 };
        for (int i = 0; i < 3; i++)
        {
            var child = new YogaNode(config);
            child.FlexGrow = 1f;
            child.FlexShrink = 1f;
            child.MaxWidth = YogaValue.Point(180f);
            child.Height = YogaValue.Point(30f);
            child.MinWidth = YogaValue.Point(minWidths[i]);
            root.InsertChild(child, i);
        }

        root.CalculateLayout(540f, 30f, FlexLayoutDirection.LTR);

        var widths = new float[3];
        for (int i = 0; i < 3; i++)
            widths[i] = root.GetChild(i).LayoutWidth;
        return widths;
    }

    private static void AssertAllGrowToMax(float minWidth0, float minWidth1, float minWidth2)
    {
        var widths = LayoutRow(MakeFixedConfig(), minWidth0, minWidth1, minWidth2);
        for (int i = 0; i < 3; i++)
            Assert.True(MathF.Abs(180f - widths[i]) <= 1e-3f,
                $"child[{i}] should grow to maxWidth, not collapse to its minWidth; width {widths[i]}");
    }

    // upstream: YGFirstChildMinWidth.first_child_larger_minwidth_row
    [Fact]
    public void FirstChildLargerMinWidthRow()
    {
        AssertAllGrowToMax(60f, 30f, 30f);
    }

    // upstream: YGFirstChildMinWidth.other_positions_still_work
    [Fact]
    public void OtherPositionsStillWork()
    {
        AssertAllGrowToMax(30f, 60f, 30f);
        AssertAllGrowToMax(30f, 30f, 60f);
    }

    // upstream: YGFirstChildMinWidth.uniform_minwidth_row
    [Fact]
    public void UniformMinWidthRow()
    {
        AssertAllGrowToMax(30f, 30f, 30f);
    }

    // upstream: YGFirstChildMinWidth.errata_preserves_prefix_geometry_by_default
    [Fact]
    public void Errata_PreservesPrefixGeometryByDefault()
    {
        var config = new YogaConfig();
        Assert.True(config.HasErrata(YogaErrata.FlexFirstPassUsesRunningTotals));

        var widths = LayoutRow(config, 60f, 30f, 30f);
        Assert.Equal(60f, widths[0], 1e-3f);
        Assert.Equal(30f, widths[1], 1e-3f);
        Assert.Equal(30f, widths[2], 1e-3f);
    }

    // upstream: YGFirstChildMinWidth.errata_bit_round_trips
    [Fact]
    public void FlexFirstPassErrataBit_RoundTrips()
    {
        var config = new YogaConfig();
        Assert.NotEqual(YogaErrata.None, config.Errata & YogaErrata.FlexFirstPassUsesRunningTotals);

        config.SetErrata(config.Errata & (YogaErrata.All ^ YogaErrata.FlexFirstPassUsesRunningTotals));
        Assert.Equal(YogaErrata.None, config.Errata & YogaErrata.FlexFirstPassUsesRunningTotals);
    }

    // ── YGFlexShrinkAllFrozenTest.cpp, withoutErrata = true (YGConfigSetErrata(None)) ──

    private static YogaConfig NewConfigWithoutErrata()
    {
        var config = new YogaConfig();
        config.SetErrata(YogaErrata.None);
        return config;
    }

    // upstream: YGFlexShrinkAllFrozen.all_items_frozen_at_min_width (withoutErrata)
    [Fact]
    public void WithoutErrata_AllItemsFrozenAtMinWidth()
    {
        var config = NewConfigWithoutErrata();
        var root = new YogaNode(config);
        root.FlexDirection = FlexDirection.Row;
        root.Width = YogaValue.Point(350f);
        root.Height = YogaValue.Point(50f);

        float[] widths = { 100.1f, 200.3f, 300.7f };
        float[] minWidths = { 90f, 190f, 290f };
        for (int i = 0; i < 3; i++)
        {
            var child = new YogaNode(config);
            child.Width = YogaValue.Point(widths[i]);
            child.MinWidth = YogaValue.Point(minWidths[i]);
            child.FlexShrink = 1f;
            root.InsertChild(child, i);
        }

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        for (int i = 0; i < 3; i++)
            Assert.Equal(minWidths[i], root.GetChild(i).LayoutWidth);
    }

    private static YogaNode AspectRatioRow(YogaConfig config, float shrink, out YogaNode box, out YogaNode sibling)
    {
        var root = new YogaNode(config);
        root.Width = YogaValue.Point(350f);
        root.Height = YogaValue.Point(350f);

        var row = new YogaNode(config);
        row.FlexDirection = FlexDirection.Row;
        root.InsertChild(row, 0);

        box = new YogaNode(config);
        box.AspectRatio = 22f / 24f;
        box.FlexShrink = shrink;
        row.InsertChild(box, 0);

        sibling = new YogaNode(config);
        sibling.Width = YogaValue.Point(600f);
        sibling.Height = YogaValue.Point(23f);
        row.InsertChild(sibling, 1);
        return root;
    }

    // upstream: YGFlexShrinkAllFrozen.aspect_ratio_sole_shrinkable_item (withoutErrata)
    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(3f)]
    [InlineData(6f)]
    [InlineData(9f)]
    [InlineData(11f)]
    [InlineData(12f)]
    [InlineData(19f)]
    [InlineData(22f)]
    [InlineData(999f)]
    public void WithoutErrata_AspectRatioSoleShrinkableItem(float shrink)
    {
        var root = AspectRatioRow(NewConfigWithoutErrata(), shrink, out var box, out var sibling);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(0f, box.LayoutWidth);
        Assert.Equal(0f, box.LayoutHeight);
        Assert.Equal(0f, sibling.LayoutX);
    }

    // upstream: YGFlexShrinkAllFrozen.unshrinkable_item_keeps_aspect_ratio_basis (withoutErrata)
    [Fact]
    public void WithoutErrata_UnshrinkableItemKeepsAspectRatioBasis()
    {
        var root = AspectRatioRow(NewConfigWithoutErrata(), 0f, out var box, out var sibling);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(21f, box.LayoutWidth);
        Assert.Equal(23f, box.LayoutHeight);
        Assert.Equal(21f, sibling.LayoutX);
    }

    // upstream: YGFlexShrinkAllFrozen.large_total_residual_is_not_divided_by (withoutErrata)
    [Fact]
    public void WithoutErrata_LargeTotalResidualIsNotDividedBy()
    {
        var config = NewConfigWithoutErrata();
        config.PointScaleFactor = 0f;
        var root = new YogaNode(config);
        root.FlexDirection = FlexDirection.Row;
        root.Width = YogaValue.Point(1000f);
        root.Height = YogaValue.Point(50f);

        float[] widths = { 10000.1f, 30000.7f, 34567.8f };
        for (int i = 0; i < 3; i++)
        {
            var child = new YogaNode(config);
            child.Width = YogaValue.Point(widths[i]);
            child.MinWidth = YogaValue.Point(widths[i] - 50f);
            child.FlexShrink = 1f;
            root.InsertChild(child, i);
        }

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        for (int i = 0; i < 3; i++)
            Assert.Equal(widths[i] - 50f, root.GetChild(i).LayoutWidth);
    }

    // upstream: YGFlexShrinkAllFrozen.tiny_item_remains_after_others_freeze (withoutErrata)
    [Fact]
    public void WithoutErrata_TinyItemRemainsAfterOthersFreeze()
    {
        var config = NewConfigWithoutErrata();
        var root = new YogaNode(config);
        root.FlexDirection = FlexDirection.Row;
        root.Width = YogaValue.Point(350f);
        root.Height = YogaValue.Point(50f);

        float[] widths = { 100.1f, 200.3f, 300.7f, 0.00001f };
        float[] minWidths = { 90f, 190f, 290f, 0f };
        for (int i = 0; i < 4; i++)
        {
            var child = new YogaNode(config);
            child.Width = YogaValue.Point(widths[i]);
            child.MinWidth = YogaValue.Point(minWidths[i]);
            child.FlexShrink = 1f;
            root.InsertChild(child, i);
        }

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        for (int i = 0; i < 4; i++)
            Assert.Equal(minWidths[i], root.GetChild(i).LayoutWidth);
    }

    // upstream: YGFlexShrinkAllFrozen.issue_1651_measured_items_freeze_at_lower_bound
    // (withoutErrata): with §4.5 auto-min the lower bound is the content height.
    [Fact]
    public void WithoutErrata_Issue1651_MeasuredItemsFreezeAtContentHeight()
    {
        var config = NewConfigWithoutErrata();
        config.UseWebDefaults = true;
        config.PointScaleFactor = 100f;
        var root = new YogaNode(config);
        root.FlexDirection = FlexDirection.Column;
        root.Height = YogaValue.Point(99f);

        float[] heights = { 10.01f, 10.01f, 12.01f };
        for (int i = 0; i < 3; i++)
        {
            float h = heights[i];
            var child = new YogaNode(config);
            child.MeasureFunction = (_, _, _, _, _) => new YogaSize(1f, h);
            root.InsertChild(child, i);
        }
        root.GetChild(0).SetMargin(YogaEdge.Bottom, YogaValue.Point(100f));

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        float top = 0f;
        for (int i = 0; i < 3; i++)
        {
            var child = root.GetChild(i);
            Assert.Equal(heights[i], child.LayoutHeight, 1e-4f);
            Assert.Equal(top, child.LayoutY, 1e-4f);
            top += heights[i] + (i == 0 ? 100f : 0f);
        }
    }
}
