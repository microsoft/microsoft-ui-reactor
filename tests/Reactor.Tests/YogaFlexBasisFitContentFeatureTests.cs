using Microsoft.UI.Reactor.Layout;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Layout regression tests for the <see cref="YogaExperimentalFeature.FixFlexBasisFitContent"/>
/// experimental feature: ports of upstream facebook/yoga tests/YGFlexBasisFitContentTest.cpp
/// (parameterized on the feature) plus coverage of the facebook/yoga#1997 height-independent
/// subtree predicate, the overflow resets, and the measurement-cache preservation it enables.
/// </summary>
public class YogaFlexBasisFitContentFeatureTests
{
    private static YogaConfig CreateConfig(bool enabled)
    {
        var config = new YogaConfig();
        config.SetExperimentalFeatureEnabled(YogaExperimentalFeature.FixFlexBasisFitContent, enabled);
        return config;
    }

    private static YogaSize MeasureTextLike(
        YogaNode node, float width, YogaMeasureMode widthMode, float height, YogaMeasureMode heightMode)
    {
        float measuredWidth = 200f;
        if (widthMode == YogaMeasureMode.AtMost)
            measuredWidth = global::System.Math.Min(measuredWidth, width);
        return new YogaSize(measuredWidth, 20f);
    }

    // ════════════════════════════════════════════════════════════════════
    // Upstream YGFlexBasisFitContentTest.cpp TEST_P cases (feature off/on)
    // ════════════════════════════════════════════════════════════════════

    // Upstream: YGFlexBasisFitContentTest.percentage_height_converges
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PercentageHeightConverges(bool enabled)
    {
        var config = CreateConfig(enabled);
        var root = new YogaNode(config) { Height = YogaValue.Point(300), Width = YogaValue.Point(100) };

        var container = new YogaNode(config);
        root.InsertChild(container, 0);

        var child = new YogaNode(config) { Height = YogaValue.Percent(50) };
        container.InsertChild(child, 0);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(75f, child.LayoutHeight);
        Assert.Equal(150f, container.LayoutHeight);
    }

    // Upstream: YGFlexBasisFitContentTest.percentage_with_flex_grow_converges
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PercentageWithFlexGrowConverges(bool enabled)
    {
        var config = CreateConfig(enabled);
        var root = new YogaNode(config) { Height = YogaValue.Point(400), Width = YogaValue.Point(100) };

        var containerA = new YogaNode(config) { FlexGrow = 1 };
        root.InsertChild(containerA, 0);

        var childA = new YogaNode(config) { Height = YogaValue.Percent(25) };
        containerA.InsertChild(childA, 0);

        var containerB = new YogaNode(config) { FlexGrow = 1 };
        root.InsertChild(containerB, 1);

        var childB = new YogaNode(config) { Height = YogaValue.Percent(50) };
        containerB.InsertChild(childB, 0);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(150f, containerA.LayoutHeight);
        Assert.Equal(250f, containerB.LayoutHeight);
    }

    // Upstream: YGFlexBasisFitContentTest.flex_shrink_overflow_converges
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FlexShrinkOverflowConverges(bool enabled)
    {
        var config = CreateConfig(enabled);
        var root = new YogaNode(config) { Height = YogaValue.Point(200), Width = YogaValue.Point(100) };

        var container = new YogaNode(config) { FlexShrink = 1 };
        root.InsertChild(container, 0);

        var child = new YogaNode(config) { Height = YogaValue.Percent(100) };
        container.InsertChild(child, 0);

        var fixedNode = new YogaNode(config) { Height = YogaValue.Point(150) };
        root.InsertChild(fixedNode, 1);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(50f, container.LayoutHeight);
        Assert.Equal(150f, fixedNode.LayoutHeight);
    }

    // Upstream: YGFlexBasisFitContentTest.scroll_avoids_remeasure
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScrollAvoidsRemeasure(bool enabled)
    {
        var config = CreateConfig(enabled);
        int measureCount = 0;

        var root = new YogaNode(config)
        {
            Overflow = YogaOverflow.Scroll,
            Width = YogaValue.Point(100),
            Height = YogaValue.Point(500),
        };

        var sibling = new YogaNode(config) { Height = YogaValue.Point(100) };
        root.InsertChild(sibling, 0);

        var wrapper = new YogaNode(config);
        root.InsertChild(wrapper, 1);

        var inner = new YogaNode(config);
        wrapper.InsertChild(inner, 0);

        var leaf = new YogaNode(config);
        leaf.MeasureFunction = (_, _, _, _, _) =>
        {
            measureCount++;
            return new YogaSize(50f, 50f);
        };
        inner.InsertChild(leaf, 0);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        int firstPassCount = measureCount;

        sibling.Height = YogaValue.Point(200);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        int secondPassCount = measureCount - firstPassCount;

        Assert.Equal(50f, leaf.LayoutHeight);
        Assert.Equal(0, secondPassCount);
    }

    // Upstream: YGFlexBasisFitContentTest.row_direction_unchanged
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RowDirectionUnchanged(bool enabled)
    {
        var config = CreateConfig(enabled);
        var root = new YogaNode(config) { Width = YogaValue.Point(100), Height = YogaValue.Point(100) };

        var container = new YogaNode(config);
        root.InsertChild(container, 0);

        var text = new YogaNode(config) { MeasureFunction = MeasureTextLike };
        container.InsertChild(text, 0);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(100f, text.LayoutWidth);
    }

    // Upstream: YGFlexBasisFitContentTest.row_scroll_skips_width
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RowScrollSkipsWidth(bool enabled)
    {
        var config = CreateConfig(enabled);
        var root = new YogaNode(config)
        {
            FlexDirection = FlexDirection.Row,
            Overflow = YogaOverflow.Scroll,
            Width = YogaValue.Point(100),
            Height = YogaValue.Point(100),
        };

        var text = new YogaNode(config) { MeasureFunction = MeasureTextLike };
        root.InsertChild(text, 0);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(200f, text.LayoutWidth);
    }

    // ════════════════════════════════════════════════════════════════════
    // facebook/yoga#1997: height-independent subtree in a vertical scroll
    // ════════════════════════════════════════════════════════════════════

    // scroll (column, 100x500)
    // ├── sibling (height 100; resized between layouts)
    // └── wrapper (flexGrow 1 → its height follows the sibling)
    //     └── inner (padding-left 10)        ← the column-stretch child under test
    //         ├── leaf  (width 30)
    //         └── leaf2 (auto)
    // inner's subtree has zero intrinsic height, so with the feature it is
    // measured with an unbounded (MaxContent) height instead of FitContent
    // against wrapper's height, and its cache survives wrapper resizing.
    private sealed class ScrollTree
    {
        public required YogaNode Root;
        public required YogaNode Sibling;
        public required YogaNode Wrapper;
        public required YogaNode Inner;
        public required YogaNode Leaf;
        public required YogaNode Leaf2;

        public YogaNode[] All => [Root, Sibling, Wrapper, Inner, Leaf, Leaf2];

        public static ScrollTree Build(YogaConfig config, bool scroll = true)
        {
            var root = new YogaNode(config) { Width = YogaValue.Point(100), Height = YogaValue.Point(500) };
            if (scroll)
                root.Overflow = YogaOverflow.Scroll;
            var sibling = new YogaNode(config) { Height = YogaValue.Point(100) };
            root.InsertChild(sibling, 0);
            var wrapper = new YogaNode(config) { FlexGrow = 1 };
            root.InsertChild(wrapper, 1);
            var inner = new YogaNode(config);
            inner.SetPadding(YogaEdge.Left, YogaValue.Point(10));
            wrapper.InsertChild(inner, 0);
            var leaf = new YogaNode(config) { Width = YogaValue.Point(30) };
            inner.InsertChild(leaf, 0);
            var leaf2 = new YogaNode(config);
            inner.InsertChild(leaf2, 1);
            return new ScrollTree { Root = root, Sibling = sibling, Wrapper = wrapper, Inner = inner, Leaf = leaf, Leaf2 = leaf2 };
        }

        public void Layout() => Root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
    }

    private static List<CachedMeasurement> CachedMeasurements(YogaNode node)
    {
        var result = new List<CachedMeasurement>();
        for (int i = 0; i < (int)node.Layout.NextCachedMeasurementsIndex; i++)
            result.Add(node.Layout.CachedMeasurements[i]);
        return result;
    }

    private static bool HasFitContentHeightMeasurement(YogaNode node, float availableHeight) =>
        CachedMeasurements(node).Exists(m =>
            m.HeightSizingMode == SizingMode.FitContent && m.AvailableHeight == availableHeight);

    private static void AssertRect(YogaNode node, float x, float y, float width, float height)
    {
        Assert.Equal(x, node.LayoutX);
        Assert.Equal(y, node.LayoutY);
        Assert.Equal(width, node.LayoutWidth);
        Assert.Equal(height, node.LayoutHeight);
    }

    private static void AssertSameLayout(YogaNode[] expected, YogaNode[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].LayoutX, actual[i].LayoutX);
            Assert.Equal(expected[i].LayoutY, actual[i].LayoutY);
            Assert.Equal(expected[i].LayoutWidth, actual[i].LayoutWidth);
            Assert.Equal(expected[i].LayoutHeight, actual[i].LayoutHeight);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QualifyingScrollSubtree_SkipsHeightFitContent_AndKeepsCacheAcrossRelayout(bool enabled)
    {
        var t = ScrollTree.Build(CreateConfig(enabled));

        t.Layout();

        AssertRect(t.Sibling, 0, 0, 100, 100);
        AssertRect(t.Wrapper, 0, 100, 100, 400);
        AssertRect(t.Inner, 0, 0, 100, 0);
        AssertRect(t.Leaf, 10, 0, 30, 0);
        AssertRect(t.Leaf2, 10, 0, 90, 0);
        // With the feature, inner is never measured against wrapper's 400px height.
        Assert.Equal(!enabled, HasFitContentHeightMeasurement(t.Inner, 400));
        if (enabled)
            Assert.DoesNotContain(CachedMeasurements(t.Inner), m => m.HeightSizingMode == SizingMode.FitContent);

        uint innerCacheCount = t.Inner.Layout.NextCachedMeasurementsIndex;
        uint leafGeneration = t.Leaf.Layout.GenerationCount;
        uint leaf2Generation = t.Leaf2.Layout.GenerationCount;

        // Resizing the sibling shrinks wrapper 400 → 300 but cannot affect inner's subtree.
        t.Sibling.Height = YogaValue.Point(200);
        t.Layout();

        AssertRect(t.Sibling, 0, 0, 100, 200);
        AssertRect(t.Wrapper, 0, 200, 100, 300);
        AssertRect(t.Inner, 0, 0, 100, 0);
        AssertRect(t.Leaf, 10, 0, 30, 0);
        AssertRect(t.Leaf2, 10, 0, 90, 0);

        if (enabled)
        {
            // Every lookup for inner hit its cache: no new entry, descendants never visited.
            Assert.Equal(innerCacheCount, t.Inner.Layout.NextCachedMeasurementsIndex);
            Assert.Equal(leafGeneration, t.Leaf.Layout.GenerationCount);
            Assert.Equal(leaf2Generation, t.Leaf2.Layout.GenerationCount);
            Assert.False(HasFitContentHeightMeasurement(t.Inner, 300));
        }
        else
        {
            // Positive control for the probes above: the legacy FitContent key
            // changes with wrapper's height, so inner re-measures its children.
            Assert.True(HasFitContentHeightMeasurement(t.Inner, 300));
            Assert.NotEqual(leafGeneration, t.Leaf.Layout.GenerationCount);
            Assert.NotEqual(leaf2Generation, t.Leaf2.Layout.GenerationCount);
        }
    }

    public static TheoryData<string> DisqualifyingVariants => new()
    {
        "MeasuredDescendant",
        "PercentPadding",
        "PercentMargin",
        "NoScrollAncestor",
        "CenterAlignedChild",
    };

    // Each variant breaks exactly one precondition of the #1997 proof, so inner
    // must keep the legacy FitContent height measurement and lay out exactly as
    // with the feature disabled.
    [Theory]
    [MemberData(nameof(DisqualifyingVariants))]
    public void DisqualifiedSubtree_KeepsHeightFitContent_AndMatchesFeatureOff(string variant)
    {
        ScrollTree Build(bool enabled)
        {
            var config = CreateConfig(enabled);
            var t = ScrollTree.Build(config, scroll: variant != "NoScrollAncestor");
            switch (variant)
            {
                case "MeasuredDescendant":
                    t.Leaf2.MeasureFunction = (_, _, _, _, _) => new YogaSize(40f, 25f);
                    break;
                case "PercentPadding":
                    t.Leaf2.SetPadding(YogaEdge.Left, YogaValue.Percent(10));
                    break;
                case "PercentMargin":
                    t.Leaf2.SetMargin(YogaEdge.Right, YogaValue.Percent(10));
                    break;
                case "CenterAlignedChild":
                    t.Wrapper.AlignItems = FlexAlign.Center;
                    break;
            }
            return t;
        }

        var on = Build(enabled: true);
        var off = Build(enabled: false);

        on.Layout();
        off.Layout();
        AssertSameLayout(off.All, on.All);
        Assert.True(HasFitContentHeightMeasurement(on.Inner, 400),
            $"{variant}: inner should be measured with FitContent against wrapper's 400px height");

        on.Sibling.Height = YogaValue.Point(200);
        off.Sibling.Height = YogaValue.Point(200);
        on.Layout();
        off.Layout();
        AssertSameLayout(off.All, on.All);
        Assert.Equal(300f, on.Wrapper.LayoutHeight);
        Assert.True(HasFitContentHeightMeasurement(on.Inner, 300),
            $"{variant}: inner should be re-measured with FitContent against wrapper's 300px height");
    }

    // Sanity check that the disqualifying variants above change real layout, so
    // AssertSameLayout compares meaningful geometry rather than an all-zero tree.
    [Fact]
    public void DisqualifyingVariants_ProduceExpectedGeometry()
    {
        var measured = ScrollTree.Build(CreateConfig(true));
        measured.Leaf2.MeasureFunction = (_, _, _, _, _) => new YogaSize(40f, 25f);
        measured.Layout();
        AssertRect(measured.Inner, 0, 0, 100, 25);
        AssertRect(measured.Leaf2, 10, 0, 90, 25);

        var percentPadding = ScrollTree.Build(CreateConfig(true));
        percentPadding.Leaf2.SetPadding(YogaEdge.Left, YogaValue.Percent(10));
        percentPadding.Layout();
        Assert.Equal(9f, percentPadding.Leaf2.LayoutPaddingLeft);
    }

    // ════════════════════════════════════════════════════════════════════
    // facebook/yoga#1997: overflow resets
    // ════════════════════════════════════════════════════════════════════

    // Pass 1: inner has a fixed 50px height and a 1000px child, so its layout
    // pass reports overflow. Pass 2: the child is removed and inner's height
    // returns to auto; inner is now a dirty, childless, height-independent
    // subtree whose HadOverflow is stale. Its measure passes return before any
    // reset, so only the ComputeFlexBasisForChild reset keeps the stale flag from
    // propagating into wrapper's overflow.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QualifyingSubtree_ClearsStaleOverflow_BeforeOwnerReadsIt(bool enabled)
    {
        var t = ScrollTree.Build(CreateConfig(enabled));
        t.Wrapper.FlexGrow = 0;
        t.Wrapper.Height = YogaValue.Point(400);
        t.Inner.Height = YogaValue.Point(50);
        t.Inner.RemoveChild(t.Leaf2);
        t.Inner.RemoveChild(t.Leaf);
        var big = new YogaNode(t.Root.Config) { Height = YogaValue.Point(1000) };
        t.Inner.InsertChild(big, 0);

        t.Layout();

        AssertRect(t.Wrapper, 0, 100, 100, 400);
        AssertRect(t.Inner, 0, 0, 100, 50);
        AssertRect(big, 10, 0, 90, 1000);
        Assert.True(t.Inner.Layout.HadOverflow);

        t.Inner.RemoveChild(big);
        t.Inner.Height = YogaValue.Auto;
        Assert.True(t.Inner.IsDirty);

        t.Layout();

        AssertRect(t.Wrapper, 0, 100, 100, 400);
        AssertRect(t.Inner, 0, 0, 100, 0);
        Assert.False(t.Root.Layout.HadOverflow);
        if (enabled)
        {
            Assert.False(t.Inner.Layout.HadOverflow);
            Assert.False(t.Wrapper.Layout.HadOverflow);
        }
        else
        {
            // Legacy behaviour (positive control): nothing clears the childless
            // node's flag, and its owner picks the stale value up.
            Assert.True(t.Inner.Layout.HadOverflow);
            Assert.True(t.Wrapper.Layout.HadOverflow);
        }
    }

    // Outside any scroll subtree the #1997 child reset never applies, so this
    // isolates the CalculateLayoutImpl entry reset: with the feature a layout pass
    // clears HadOverflow even on a node that returns early as childless.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LayoutPass_ResetsOverflowOnChildlessNode(bool enabled)
    {
        var config = CreateConfig(enabled);
        var root = new YogaNode(config) { Width = YogaValue.Point(100), Height = YogaValue.Point(500) };
        var box = new YogaNode(config) { Height = YogaValue.Point(50) };
        root.InsertChild(box, 0);
        var big = new YogaNode(config) { Height = YogaValue.Point(1000) };
        box.InsertChild(big, 0);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.True(box.Layout.HadOverflow);

        box.RemoveChild(big);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        AssertRect(box, 0, 0, 100, 50);
        Assert.Equal(!enabled, box.Layout.HadOverflow);
    }

    // With the feature, a layout pass resets HadOverflow on entry instead of after
    // the early-return checks; overflow genuinely reported by a child during the
    // pass must still reach every ancestor, and clear once it is gone. Explicit
    // widths make each child's flex-resolution call a real layout pass (no
    // deferred stretch layout), which is when an owner ORs in its child's flag.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LayoutPass_KeepsOverflowReportedByChildren(bool enabled)
    {
        var config = CreateConfig(enabled);
        var root = new YogaNode(config) { Width = YogaValue.Point(100), Height = YogaValue.Point(500) };
        var middle = new YogaNode(config) { Width = YogaValue.Point(100), Height = YogaValue.Point(200) };
        root.InsertChild(middle, 0);
        var box = new YogaNode(config) { Width = YogaValue.Point(100), Height = YogaValue.Point(50) };
        middle.InsertChild(box, 0);
        var big = new YogaNode(config) { Height = YogaValue.Point(1000) };
        box.InsertChild(big, 0);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.True(box.Layout.HadOverflow);
        Assert.True(middle.Layout.HadOverflow);
        Assert.True(root.Layout.HadOverflow);

        // Relayout from a dirty child keeps reporting the overflow it still has.
        big.Height = YogaValue.Point(900);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.True(box.Layout.HadOverflow);
        Assert.True(middle.Layout.HadOverflow);
        Assert.True(root.Layout.HadOverflow);

        big.Height = YogaValue.Point(10);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.False(box.Layout.HadOverflow);
        Assert.False(middle.Layout.HadOverflow);
        Assert.False(root.Layout.HadOverflow);
    }
}
