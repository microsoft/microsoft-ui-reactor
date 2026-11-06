using Microsoft.UI.Reactor.Layout;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Hand-ported regression tests for upstream facebook/yoga fixes taken after the
/// original port base (facebook/yoga@dc6d185, pinned in cgmanifest.json). Each test
/// is the upstream regression test for that fix, restricted to the default-errata
/// configuration (the upstream <c>withoutErrata</c> variants depend on the CSS §4.5
/// automatic-minimum-size feature, which this port does not carry).
/// </summary>
/// <remarks>
/// Upstream provenance beyond the base commit (keep in sync when syncing again):
/// <list type="bullet">
/// <item>Applied: aa6ede70 (#2038, positive shrink-total residual), 4a4e7810 (#2022,
/// wrap-line rounding tolerance), 98806ccf (#2037, double-precision edge
/// difference), 6ac92f4c (#2040, whole-pixel text size).</item>
/// <item>Applied in part: 433d4634 (#1665) — only its positive-residual case, via #2038.
/// Its |total| &lt; 1e-6 arm is not taken: a negative residual is already clamped by
/// min/padding, so the arm only changed layouts with genuinely tiny shrink factors,
/// against CSS (see <see cref="Shrink_GenuinelyTinyTotal_StillAbsorbsOverflow"/>).</item>
/// <item>Declined: 8c5ea16 (#1905; unreachable from FlexPanel's depth-2 trees and turned a
/// hidden node's layout into NaN in a nested tree), 52f6d0bd (#2041; bit-identical
/// perf change), b7ff95d2 (#1976; unreachable: the root always runs a layout pass and
/// nothing reads HasNewLayout).</item>
/// </list>
/// </remarks>
public class YogaUpstreamFixTests
{
    private static YogaNode MeasuredLeaf(YogaConfig config, float width, float height)
    {
        var node = new YogaNode(config);
        node.MeasureFunction = (_, _, _, _, _) => new YogaSize(width, height);
        return node;
    }

    // ── facebook/yoga#1665: shrink total cancels to a residual, not 0 ──

    [Fact]
    public void FlexBasisZero_BorderAndMinWidth_DoesNotExplode()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.FlexDirection = FlexDirection.Row;
        root.Width = YogaValue.Point(393f);
        root.Height = YogaValue.Point(100f);

        float[] borders = { 0.594443f, 0.594442f, 0.594442f, 0.594442f };
        for (int i = 0; i < 4; i++)
        {
            var child = new YogaNode(config);
            child.FlexBasis = YogaValue.Point(0f);
            child.FlexGrow = 1f;
            child.FlexShrink = 1f;
            child.MinWidth = YogaValue.Point(160f);
            child.SetBorder(YogaEdge.All, borders[i]);
            root.InsertChild(child, i);
        }

        root.CalculateLayout(393f, 100f, FlexLayoutDirection.LTR);

        // 4 × 160 overflows 393, so every child freezes at its minimum.
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(160f, root.GetChild(i).LayoutWidth);
            Assert.Equal(160f * i, root.GetChild(i).LayoutX);
        }
    }

    // CSS: the only unfrozen shrinkable item absorbs the whole overflow however small
    // its shrink factor is. Upstream #1665's |total| < 1e-6 arm would instead keep
    // B at its basis and let the line overflow.
    [Fact]
    public void Shrink_GenuinelyTinyTotal_StillAbsorbsOverflow()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.Height = YogaValue.Point(100f);

        var a = new YogaNode(config);
        a.Height = YogaValue.Point(80f);
        a.FlexShrink = 0f;
        root.InsertChild(a, 0);

        var b = new YogaNode(config);
        b.Height = YogaValue.Point(50f);
        b.FlexShrink = 1e-8f;
        root.InsertChild(b, 1);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(80f, a.LayoutHeight);
        Assert.Equal(20f, b.LayoutHeight);
        Assert.Equal(80f, b.LayoutY);
    }

    // ── facebook/yoga#2038 / #1651 (aa6ede70): positive shrink-total residual ──

    [Fact]
    public void Shrink_AllItemsFrozenAtMinWidth()
    {
        var config = new YogaConfig();
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
    public void Shrink_AspectRatioSoleShrinkableItem_FreezesAtZero(float shrink)
    {
        var root = AspectRatioRow(new YogaConfig(), shrink, out var box, out var sibling);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(0f, box.LayoutWidth);
        Assert.Equal(0f, box.LayoutHeight);
        Assert.Equal(0f, sibling.LayoutX);
    }

    [Fact]
    public void Shrink_UnshrinkableItem_KeepsAspectRatioBasis()
    {
        var root = AspectRatioRow(new YogaConfig(), 0f, out var box, out var sibling);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(21f, box.LayoutWidth);
        Assert.Equal(23f, box.LayoutHeight);
        Assert.Equal(21f, sibling.LayoutX);
    }

    [Fact]
    public void Shrink_LargeTotalResidual_IsNotDividedBy()
    {
        var config = new YogaConfig { PointScaleFactor = 0f };
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

    [Fact]
    public void Shrink_TinyItemRemainsAfterOthersFreeze()
    {
        var config = new YogaConfig();
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

    [Fact]
    public void Shrink_Issue1651_MeasuredItemsFreezeAtLowerBound()
    {
        var config = new YogaConfig { UseWebDefaults = true, PointScaleFactor = 100f };
        var root = new YogaNode(config);
        root.FlexDirection = FlexDirection.Column;
        root.Height = YogaValue.Point(99f);

        float[] heights = { 10.01f, 10.01f, 12.01f };
        for (int i = 0; i < 3; i++)
            root.InsertChild(MeasuredLeaf(config, 1f, heights[i]), i);
        root.GetChild(0).SetMargin(YogaEdge.Bottom, YogaValue.Point(100f));

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        // Default errata: no automatic minimum size, so every item freezes at 0.
        float top = 0f;
        for (int i = 0; i < 3; i++)
        {
            var child = root.GetChild(i);
            Assert.Equal(0f, child.LayoutHeight, 1e-4f);
            Assert.Equal(top, child.LayoutY, 1e-4f);
            top += i == 0 ? 100f : 0f;
        }
    }

    // ── facebook/yoga#2022 (4a4e7810): wrap line broke on float rounding noise ──

    [Fact]
    public void ContentSizedWrapRow_DoesNotBreakLineOnRounding()
    {
        for (int step = 0; step < 100; step++)
        {
            float measuredWidth = 84.0f + step / 100.0f;

            var config = new YogaConfig();
            var root = new YogaNode(config);
            root.Width = YogaValue.Point(400f);
            root.Height = YogaValue.Point(400f);

            var wrapper = new YogaNode(config);
            wrapper.AlignSelf = FlexAlign.FlexStart;
            wrapper.SetBorder(YogaEdge.All, 1f);
            root.InsertChild(wrapper, 0);

            var row = new YogaNode(config);
            row.FlexDirection = FlexDirection.Row;
            row.FlexWrap = FlexWrap.Wrap;
            row.SetPadding(YogaEdge.All, YogaValue.Point(16f));
            wrapper.InsertChild(row, 0);

            var measured = MeasuredLeaf(config, measuredWidth, 20f);
            row.InsertChild(measured, 0);

            var rigid = new YogaNode(config);
            rigid.Width = YogaValue.Point(20f);
            rigid.Height = YogaValue.Point(20f);
            row.InsertChild(rigid, 1);

            root.CalculateLayout(400f, 400f, FlexLayoutDirection.LTR);

            Assert.True(rigid.LayoutY == measured.LayoutY,
                $"step {step} (measured width {measuredWidth:R}): line broke, rigid.y={rigid.LayoutY} measured.y={measured.LayoutY}");
            Assert.True(row.LayoutHeight == 52f,
                $"step {step} (measured width {measuredWidth:R}): row height {row.LayoutHeight}");
        }
    }

    // The tolerance must not swallow a genuine overflow: items 0.01 wider than the
    // line still wrap.
    [Fact]
    public void FixedWidthWrapRow_StillBreaksOnRealOverflow()
    {
        var config = new YogaConfig();
        var row = new YogaNode(config);
        row.FlexDirection = FlexDirection.Row;
        row.FlexWrap = FlexWrap.Wrap;
        row.Width = YogaValue.Point(100f);

        var measured = MeasuredLeaf(config, 50.01f, 20f);
        row.InsertChild(measured, 0);

        var rigid = new YogaNode(config);
        rigid.Width = YogaValue.Point(50f);
        rigid.Height = YogaValue.Point(20f);
        row.InsertChild(rigid, 1);

        row.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(0f, measured.LayoutY);
        Assert.Equal(20f, rigid.LayoutY);
        Assert.Equal(0f, rigid.LayoutX);
    }

    // ── facebook/yoga#2037 (98806ccf): dimensions rounded in float lost a step ──

    [Theory]
    [InlineData(1012.0f + 1.0f / 3.0f)]
    [InlineData(1012.0f + 2.0f / 3.0f)]
    [InlineData(2004.0f + 1.0f / 3.0f)]
    [InlineData(2028.0f + 2.0f / 3.0f)]
    [InlineData(4052.0f + 2.0f / 3.0f)]
    public void Rounding_MeasuredHeightIsExactAcrossFloatSteps(float spacer)
    {
        var config = new YogaConfig { PointScaleFactor = 3f };
        var root = new YogaNode(config);
        root.Width = YogaValue.Point(300f);

        var spacerNode = new YogaNode(config);
        spacerNode.Height = YogaValue.Point(spacer);
        root.InsertChild(spacerNode, 0);

        var text = MeasuredLeaf(config, 292f, 44f);
        root.InsertChild(text, 1);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(44f, text.LayoutHeight);
    }

    // Same float-step loss at fractional DPI scales FlexPanel actually runs at, on both
    // axes (the width and height dimensions are rounded on separate code paths).
    [Theory]
    [InlineData(1.25f, false)]
    [InlineData(1.25f, true)]
    [InlineData(1.5f, false)]
    [InlineData(1.5f, true)]
    public void Rounding_MeasuredSizeIsExactAtFractionalScales(float scale, bool row)
    {
        var config = new YogaConfig { PointScaleFactor = scale };
        var root = new YogaNode(config);
        if (row)
        {
            root.FlexDirection = FlexDirection.Row;
            root.Height = YogaValue.Point(300f);
        }
        else
        {
            root.Width = YogaValue.Point(300f);
        }

        var spacer = new YogaNode(config);
        if (row) spacer.Width = YogaValue.Point(1001f);
        else spacer.Height = YogaValue.Point(1001f);
        root.InsertChild(spacer, 0);

        var text = row ? MeasuredLeaf(config, 44f, 292f) : MeasuredLeaf(config, 292f, 44f);
        root.InsertChild(text, 1);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(44f, row ? text.LayoutWidth : text.LayoutHeight);
    }

    // ── TASK-083: the ±∞ clamp stays on RoundValueToPixelGrid, and the double
    //    edge difference keeps sizes finite (and correct) past float range ──

    [Fact]
    public void RoundValueToPixelGrid_ClampsOverflowToFiniteRange()
    {
        Assert.Equal(float.MaxValue, PixelGridHelper.RoundValueToPixelGrid(1e39, 1.0, false, false));
        Assert.Equal(float.MinValue, PixelGridHelper.RoundValueToPixelGrid(-1e39, 1.0, false, false));
    }

    [Fact]
    public void Rounding_EdgesPastFloatRange_KeepFiniteSizes()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.FlexDirection = FlexDirection.Row;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(10f);

        var first = new YogaNode(config);
        first.Width = YogaValue.Point(3e38f);
        first.FlexShrink = 0f;
        root.InsertChild(first, 0);

        // Its right edge (6e38) is past float range: clamping both rounded edges
        // used to collapse its width to MaxValue - 3e38.
        var second = new YogaNode(config);
        second.Width = YogaValue.Point(3e38f);
        second.FlexShrink = 0f;
        root.InsertChild(second, 1);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(3e38f, second.LayoutX);
        Assert.Equal(3e38f, second.LayoutWidth);
        Assert.True(float.IsFinite(second.LayoutHeight), $"height {second.LayoutHeight}");
    }

    // ── facebook/yoga#2040 (6ac92f4c): a text node whose scaled size is a whole
    //    number of pixels keeps that size instead of losing a pixel ──

    [Fact]
    public void Rounding_MeasuredTextKeepsWholePixelWidth()
    {
        var config = new YogaConfig { PointScaleFactor = 3f };
        var root = new YogaNode(config);
        root.AlignItems = FlexAlign.FlexStart;
        root.SetPadding(YogaEdge.Left, YogaValue.Point(163.666641f));

        var text = MeasuredLeaf(config, 54.666656f, 20f);
        root.InsertChild(text, 0);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(164f / 3f, text.LayoutWidth, 1e-5f);
    }

    [Fact]
    public void Rounding_MeasuredTextKeepsWholePixelHeight()
    {
        var config = new YogaConfig { PointScaleFactor = 3f };
        var root = new YogaNode(config);
        root.AlignItems = FlexAlign.FlexStart;
        root.SetPadding(YogaEdge.Top, YogaValue.Point(163.666641f));

        var text = MeasuredLeaf(config, 20f, 54.666656f);
        root.InsertChild(text, 0);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(164f / 3f, text.LayoutHeight, 1e-5f);
    }
}
