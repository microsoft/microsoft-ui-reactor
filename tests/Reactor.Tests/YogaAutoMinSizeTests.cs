using Microsoft.UI.Reactor.Layout;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Port of upstream facebook/yoga <c>tests/YGAutoMinSizeTest.cpp</c>: CSS Flexbox §4.5
/// automatic minimum size, opted into by clearing
/// <see cref="YogaErrata.MinSizeUndefinedInsteadOfAuto"/>. Upstream commits 4a59d094
/// (#1968), db7fc6d8 (#1979) and 3b372324 (#2036).
/// </summary>
public class YogaAutoMinSizeTests
{
    // Simulates a min-content-aware text measure: the longest "word" is WordWidth.
    private const float WordWidth = 30f;
    private const float NaturalWidth = 90f;
    private const float LineHeight = 16f;

    private static YogaSize MeasureWordWrappingText(
        YogaNode node, float width, YogaMeasureMode widthMode, float height, YogaMeasureMode heightMode)
    {
        if (widthMode == YogaMeasureMode.AtMost)
        {
            if (width < WordWidth)
                return new YogaSize(WordWidth, LineHeight * 3);
            if (width < NaturalWidth)
                return new YogaSize(width, LineHeight * 2);
            return new YogaSize(NaturalWidth, LineHeight);
        }
        if (widthMode == YogaMeasureMode.Exactly)
            return new YogaSize(width, LineHeight);
        return new YogaSize(NaturalWidth, LineHeight);
    }

    private static YogaConfig MakeWebConfig(bool useAutoMinSize)
    {
        var config = new YogaConfig { UseWebDefaults = true };
        if (useAutoMinSize)
            config.RemoveErrata(YogaErrata.MinSizeUndefinedInsteadOfAuto);
        return config;
    }

    // A 2-child row: shrinkable text (basis 90) + a 10-wide spacer that doesn't shrink.
    private sealed class ShrinkRow
    {
        public readonly YogaNode Root;
        public readonly YogaNode Text;
        public readonly YogaNode Spacer;

        public ShrinkRow(bool useAutoMinSize, float containerWidth)
        {
            var config = MakeWebConfig(useAutoMinSize);
            Root = new YogaNode(config);
            Root.FlexDirection = FlexDirection.Row;
            Root.Width = YogaValue.Point(containerWidth);
            Root.Height = YogaValue.Point(50f);

            Text = new YogaNode(config);
            Text.MeasureFunction = MeasureWordWrappingText;
            Text.FlexBasis = YogaValue.Point(NaturalWidth);
            Text.FlexGrow = 0f;
            Text.FlexShrink = 1f;
            Root.InsertChild(Text, 0);

            Spacer = new YogaNode(config);
            Spacer.Width = YogaValue.Point(10f);
            Spacer.FlexShrink = 0f;
            Root.InsertChild(Spacer, 1);
        }

        public void Layout() => Root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
    }

    private static YogaNode Spacer(YogaConfig config, float width)
    {
        var spacer = new YogaNode(config);
        spacer.Width = YogaValue.Point(width);
        spacer.FlexShrink = 0f;
        return spacer;
    }

    private static YogaNode ShrinkableText(YogaConfig config)
    {
        var text = new YogaNode(config);
        text.MeasureFunction = MeasureWordWrappingText;
        text.FlexBasis = YogaValue.Point(NaturalWidth);
        text.FlexGrow = 0f;
        text.FlexShrink = 1f;
        return text;
    }

    private static YogaNode Row(YogaConfig config, float width, float height)
    {
        var root = new YogaNode(config);
        root.FlexDirection = FlexDirection.Row;
        root.Width = YogaValue.Point(width);
        root.Height = YogaValue.Point(height);
        return root;
    }

    // upstream: default_config_preserves_existing_shrink (#1968)
    [Fact]
    public void DefaultConfig_PreservesExistingShrink()
    {
        var row = new ShrinkRow(useAutoMinSize: false, containerWidth: 20f);
        row.Layout();
        Assert.Equal(10f, row.Text.LayoutWidth);
    }

    // upstream: auto_min_floors_text_at_min_content_width (#1968)
    [Fact]
    public void AutoMin_FloorsTextAtMinContentWidth()
    {
        var row = new ShrinkRow(useAutoMinSize: true, containerWidth: 20f);
        row.Layout();
        Assert.Equal(WordWidth, row.Text.LayoutWidth);
        Assert.Equal(10f, row.Spacer.LayoutWidth);
    }

    // upstream: auto_min_includes_leaf_padding_and_border_width (#1979)
    [Fact]
    public void AutoMin_IncludesLeafPaddingAndBorderWidth()
    {
        var config = MakeWebConfig(useAutoMinSize: true);
        var root = Row(config, 20f, 50f);

        var text = ShrinkableText(config);
        text.SetPadding(YogaEdge.Left, YogaValue.Point(4f));
        text.SetPadding(YogaEdge.Right, YogaValue.Point(4f));
        text.SetBorder(YogaEdge.Left, 1f);
        text.SetBorder(YogaEdge.Right, 1f);
        root.InsertChild(text, 0);
        root.InsertChild(Spacer(config, 10f), 1);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        // 30 content + 4+4 padding + 1+1 border.
        Assert.Equal(40f, text.LayoutWidth);
    }

    // upstream: auto_min_includes_leaf_padding_height (#1979)
    [Fact]
    public void AutoMin_IncludesLeafPaddingHeight()
    {
        var config = MakeWebConfig(useAutoMinSize: true);
        var root = new YogaNode(config);
        root.FlexDirection = FlexDirection.Column;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(20f);

        var text = ShrinkableText(config); // tall basis forces shrink
        text.SetPadding(YogaEdge.Top, YogaValue.Point(4f));
        text.SetPadding(YogaEdge.Bottom, YogaValue.Point(4f));
        root.InsertChild(text, 0);

        var spacer = new YogaNode(config);
        spacer.Height = YogaValue.Point(10f);
        spacer.FlexShrink = 0f;
        root.InsertChild(spacer, 1);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        // Natural line height 16 + padding 4+4.
        Assert.Equal(24f, text.LayoutHeight);
    }

    // upstream: flex_basis_zero_floors_at_min_content (#1968)
    [Fact]
    public void FlexBasisZero_FloorsAtMinContent()
    {
        var config = MakeWebConfig(useAutoMinSize: true);
        var root = Row(config, 50f, 50f);

        var a = new YogaNode(config);
        a.MeasureFunction = MeasureWordWrappingText;
        a.Style.Flex = 1f; // YGNodeStyleSetFlex (no node-level setter in this port)
        root.InsertChild(a, 0);

        var b = new YogaNode(config);
        b.MeasureFunction = MeasureWordWrappingText;
        b.Style.Flex = 1f;
        root.InsertChild(b, 1);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(WordWidth, a.LayoutWidth);
        Assert.Equal(WordWidth, b.LayoutWidth);
    }

    // upstream: content_smaller_than_specified_shrinks_to_content (#1968)
    [Fact]
    public void ContentSmallerThanSpecified_ShrinksToContent()
    {
        var row = new ShrinkRow(useAutoMinSize: true, containerWidth: 20f);
        row.Layout();
        Assert.Equal(WordWidth, row.Text.LayoutWidth);
    }

    // upstream: auto_min_capped_by_max_size (#1968)
    [Fact]
    public void AutoMin_CappedByMaxSize()
    {
        var config = MakeWebConfig(useAutoMinSize: true);
        var root = Row(config, 10f, 50f);

        var text = ShrinkableText(config);
        text.MaxWidth = YogaValue.Point(20f);
        root.InsertChild(text, 0);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(20f, text.LayoutWidth);
    }

    // upstream: explicit_min_width_zero_opts_out (#1968)
    [Fact]
    public void ExplicitMinWidthZero_OptsOut()
    {
        var config = MakeWebConfig(useAutoMinSize: true);
        var root = Row(config, 20f, 50f);

        var text = ShrinkableText(config);
        text.MinWidth = YogaValue.Point(0f);
        root.InsertChild(text, 0);
        root.InsertChild(Spacer(config, 10f), 1);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(10f, text.LayoutWidth);
    }

    // upstream: aspect_ratio_transferred_size_floors_main (#1968). Yoga takes
    // min(content, transferred), and content is 0 here, so the item still shrinks.
    [Fact]
    public void AspectRatio_TransferredSizeFloorsMain()
    {
        var config = MakeWebConfig(useAutoMinSize: true);
        var root = Row(config, 30f, 50f);

        var img = new YogaNode(config);
        img.Height = YogaValue.Point(40f);
        img.AspectRatio = 2f;
        img.FlexBasis = YogaValue.Point(NaturalWidth);
        img.FlexShrink = 1f;
        root.InsertChild(img, 0);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(30f, img.LayoutWidth);
    }

    // upstream: nested_flexbox_recurses_into_min_content (#1968)
    [Fact]
    public void NestedFlexbox_RecursesIntoMinContent()
    {
        var config = MakeWebConfig(useAutoMinSize: true);
        var root = new YogaNode(config);
        root.FlexDirection = FlexDirection.Column;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(30f);

        var wrapper = new YogaNode(config);
        wrapper.FlexDirection = FlexDirection.Column;
        wrapper.FlexGrow = 0f;
        wrapper.FlexShrink = 1f;
        root.InsertChild(wrapper, 0);

        var leaf = new YogaNode(config);
        leaf.MeasureFunction = (_, _, _, _, _) => new YogaSize(200f, 50f);
        wrapper.InsertChild(leaf, 0);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(50f, wrapper.LayoutHeight);
    }

    // upstream: overflow_hidden_disables_auto_min (#1968)
    [Fact]
    public void OverflowHidden_DisablesAutoMin()
    {
        var config = MakeWebConfig(useAutoMinSize: true);
        var root = Row(config, 20f, 50f);

        var text = ShrinkableText(config);
        text.Overflow = YogaOverflow.Hidden;
        root.InsertChild(text, 0);
        root.InsertChild(Spacer(config, 10f), 1);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(10f, text.LayoutWidth);
    }

    // upstream: min_content_measure_func_preferred_during_probe (#1968)
    [Fact]
    public void MinContentMeasureFunc_PreferredDuringProbe()
    {
        var config = MakeWebConfig(useAutoMinSize: true);
        var root = Row(config, 20f, 50f);

        int minContentCalls = 0;
        var item = ShrinkableText(config);
        item.MinContentMeasureFunction = (_, _, _, _, _) =>
        {
            minContentCalls++;
            return new YogaSize(0f, 0f);
        };
        root.InsertChild(item, 0);
        root.InsertChild(Spacer(config, 10f), 1);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(10f, item.LayoutWidth);
        Assert.True(minContentCalls > 0, $"min-content callback calls: {minContentCalls}");
    }

    // upstream: has_min_content_measure_func_tracks_setter (#1968)
    [Fact]
    public void HasMinContentMeasureFunc_TracksSetter()
    {
        var node = new YogaNode(new YogaConfig());
        Assert.False(node.HasMinContentMeasureFunc);
        node.MinContentMeasureFunction = (_, _, _, _, _) => new YogaSize(0f, 0f);
        Assert.True(node.HasMinContentMeasureFunc);
        node.MinContentMeasureFunction = null;
        Assert.False(node.HasMinContentMeasureFunc);
    }

    // upstream: static_min_content_width_short_circuits_probe (#1968)
    [Fact]
    public void StaticMinContentWidth_ShortCircuitsProbe()
    {
        var config = MakeWebConfig(useAutoMinSize: true);
        var root = Row(config, 20f, 50f);

        int minContentCalls = 0;
        var item = ShrinkableText(config);
        item.MinContentWidth = 0f;
        item.MinContentMeasureFunction = (_, _, _, _, _) =>
        {
            minContentCalls++;
            return new YogaSize(0f, 0f);
        };
        root.InsertChild(item, 0);
        root.InsertChild(Spacer(config, 10f), 1);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(10f, item.LayoutWidth);
        Assert.Equal(0, minContentCalls);
    }

    // upstream: static_min_content_short_circuits_container_recursion (#1968)
    [Fact]
    public void StaticMinContent_ShortCircuitsContainerRecursion()
    {
        var config = MakeWebConfig(useAutoMinSize: true);
        var root = Row(config, 20f, 50f);

        var item = new YogaNode(config);
        item.FlexBasis = YogaValue.Point(NaturalWidth);
        item.FlexGrow = 0f;
        item.FlexShrink = 1f;
        item.MinContentWidth = 0f;
        root.InsertChild(item, 0);

        var inner = new YogaNode(config);
        inner.MeasureFunction = MeasureWordWrappingText;
        item.InsertChild(inner, 0);

        root.InsertChild(Spacer(config, 10f), 1);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(10f, item.LayoutWidth);
    }

    // upstream: nested_leaf_probed_at_most_once_per_axis_per_layout (#2036)
    [Fact]
    public void NestedLeaf_ProbedAtMostOncePerAxisPerLayout()
    {
        var config = MakeWebConfig(useAutoMinSize: true);
        var root = Row(config, 20f, 50f);

        var item = new YogaNode(config);
        root.InsertChild(item, 0);
        var parent = item;
        for (int depth = 0; depth < 10; depth++)
        {
            var container = new YogaNode(config);
            container.FlexDirection = depth % 2 == 0 ? FlexDirection.Column : FlexDirection.Row;
            parent.InsertChild(container, 0);
            parent = container;
        }

        int minContentCalls = 0;
        var leaf = new YogaNode(config);
        leaf.MeasureFunction = MeasureWordWrappingText;
        leaf.MinContentMeasureFunction = (_, _, _, _, _) =>
        {
            minContentCalls++;
            return new YogaSize(WordWidth, LineHeight);
        };
        parent.InsertChild(leaf, 0);

        root.InsertChild(Spacer(config, 10f), 1);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.True(minContentCalls <= 2, $"min-content callback calls: {minContentCalls}");
        Assert.Equal(WordWidth, item.LayoutWidth);
    }

    // upstream: container_min_content_memo_respects_owner_width (#2036)
    [Fact]
    public void ContainerMinContentMemo_RespectsOwnerWidth()
    {
        var config = MakeWebConfig(useAutoMinSize: true);
        var root = Row(config, 200f, 50f);

        var outer = new YogaNode(config);
        outer.FlexDirection = FlexDirection.Row;
        outer.FlexBasis = YogaValue.Point(300f);
        root.InsertChild(outer, 0);

        root.InsertChild(Spacer(config, 150f), 1);

        var inner = new YogaNode(config);
        inner.FlexDirection = FlexDirection.Column;
        inner.SetPadding(YogaEdge.Left, YogaValue.Percent(10f));
        inner.SetPadding(YogaEdge.Right, YogaValue.Percent(10f));
        outer.InsertChild(inner, 0);

        var leaf = new YogaNode(config);
        leaf.MeasureFunction = MeasureWordWrappingText;
        leaf.MinContentMeasureFunction = (_, _, _, _, _) => new YogaSize(WordWidth, LineHeight);
        inner.InsertChild(leaf, 0);

        outer.InsertChild(Spacer(config, 20f), 1);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        // Root probes inner against 200: 30 + 2 * 20 = 70, flooring outer at 70.
        Assert.Equal(70f, outer.LayoutWidth);
        // Outer probes inner against 70: 30 + 2 * 7 = 44, so inner takes the 50
        // left beside the spacer. Reusing the root's 70 would give 70.
        Assert.Equal(50f, inner.LayoutWidth);
    }

    // upstream: min_content_reprobed_on_next_layout (#2036)
    [Fact]
    public void MinContent_ReprobedOnNextLayout()
    {
        var row = new ShrinkRow(useAutoMinSize: true, containerWidth: 20f);
        float wordWidth = WordWidth;
        int minContentCalls = 0;
        row.Text.MinContentMeasureFunction = (_, _, _, _, _) =>
        {
            minContentCalls++;
            return new YogaSize(wordWidth, LineHeight);
        };
        row.Layout();
        Assert.Equal(WordWidth, row.Text.LayoutWidth);

        wordWidth = 40f;
        row.Text.MarkDirty();
        minContentCalls = 0;
        row.Layout();

        Assert.True(minContentCalls > 0, $"min-content callback calls: {minContentCalls}");
        Assert.Equal(40f, row.Text.LayoutWidth);
    }

    // upstream: static_min_content_getter_setter_round_trip (#1968)
    [Fact]
    public void StaticMinContent_GetterSetterRoundTrip()
    {
        var node = new YogaNode(new YogaConfig());
        Assert.True(float.IsNaN(node.MinContentWidth));
        Assert.True(float.IsNaN(node.MinContentHeight));

        node.MinContentWidth = 0f;
        node.MinContentHeight = 42f;
        Assert.Equal(0f, node.MinContentWidth);
        Assert.Equal(42f, node.MinContentHeight);

        node.MinContentWidth = float.NaN;
        Assert.True(float.IsNaN(node.MinContentWidth));
        Assert.Equal(42f, node.MinContentHeight);
    }

    // Port-specific: unlike upstream, the static min-content setters dirty the node
    // so a changed floor is picked up by the next layout.
    [Fact]
    public void StaticMinContent_SetterDirtiesNodeAndRelayouts()
    {
        var row = new ShrinkRow(useAutoMinSize: true, containerWidth: 20f);
        row.Layout();
        Assert.Equal(WordWidth, row.Text.LayoutWidth);
        Assert.False(row.Root.IsDirty);

        row.Text.MinContentWidth = 0f;
        Assert.True(row.Text.IsDirty);
        Assert.True(row.Root.IsDirty);

        row.Layout();
        Assert.Equal(10f, row.Text.LayoutWidth);
    }

    // upstream: errata_bit_round_trips (#1968)
    [Fact]
    public void ErrataBit_RoundTrips()
    {
        var config = new YogaConfig();
        Assert.NotEqual(YogaErrata.None, config.Errata & YogaErrata.MinSizeUndefinedInsteadOfAuto);

        config.SetErrata(config.Errata & (YogaErrata.All ^ YogaErrata.MinSizeUndefinedInsteadOfAuto));
        Assert.Equal(YogaErrata.None, config.Errata & YogaErrata.MinSizeUndefinedInsteadOfAuto);

        config.SetErrata(config.Errata | YogaErrata.MinSizeUndefinedInsteadOfAuto);
        Assert.NotEqual(YogaErrata.None, config.Errata & YogaErrata.MinSizeUndefinedInsteadOfAuto);
    }
}
