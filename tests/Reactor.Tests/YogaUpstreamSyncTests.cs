using Microsoft.UI.Reactor.Layout;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Hand-written upstream facebook/yoga regression tests added between the original
/// port base (dc6d185) and 41118bd3 that are not about §4.5 auto-min, flex errata or
/// the cache predicate (see <see cref="YogaAutoMinSizeTests"/>,
/// <see cref="YogaFlexErrataTests"/>, <see cref="YogaCacheTests"/>). Each region names
/// its upstream source file. Tests that need node cloning, loggers, StyleValuePool,
/// Grid or YGNodeFree are not ported; the display: none tests of
/// <c>YGPersistentNodeCloningTest.cpp</c> are ported as non-cloning analogs.
/// </summary>
public class YogaUpstreamSyncTests
{
    // ════════════════════════════════════════════════════════════════════
    // YGConfigTest.cpp (6c79cca0, 984334ac)
    // ════════════════════════════════════════════════════════════════════

    // upstream: default_config_is_constexpr (the comparable part: the shared default
    // config matches a freshly constructed one, and nodes default to it).
    [Fact]
    public void DefaultConfig_MatchesNewConfig()
    {
        var shared = YogaConfig.Default;
        var fresh = new YogaConfig();

        Assert.Same(shared, new YogaNode().Config);
        Assert.Equal(fresh.UseWebDefaults, shared.UseWebDefaults);
        Assert.Equal(fresh.PointScaleFactor, shared.PointScaleFactor);
        Assert.Equal(fresh.Errata, shared.Errata);
        Assert.Equal(
            fresh.IsExperimentalFeatureEnabled(YogaExperimentalFeature.WebFlexBasis),
            shared.IsExperimentalFeatureEnabled(YogaExperimentalFeature.WebFlexBasis));
        Assert.Equal(
            fresh.IsExperimentalFeatureEnabled(YogaExperimentalFeature.FixFlexBasisFitContent),
            shared.IsExperimentalFeatureEnabled(YogaExperimentalFeature.FixFlexBasisFitContent));
        Assert.Equal(fresh.Version, shared.Version);
        // Upstream defaults new configs to the two legacy-geometry errata (#1968, #2021).
        Assert.Equal(
            YogaErrata.MinSizeUndefinedInsteadOfAuto | YogaErrata.FlexFirstPassUsesRunningTotals,
            fresh.Errata);
    }

    // upstream: config_version_increments_only_on_actual_changes
    [Fact]
    public void ConfigVersion_IncrementsOnlyOnActualChanges()
    {
        var config = new YogaConfig();
        uint initialVersion = config.Version;

        config.PointScaleFactor = 2f;
        Assert.Equal(initialVersion + 1, config.Version);
        config.PointScaleFactor = 2f;
        Assert.Equal(initialVersion + 1, config.Version);

        config.SetErrata(YogaErrata.StretchFlexBasis);
        Assert.Equal(initialVersion + 2, config.Version);
        config.SetErrata(YogaErrata.StretchFlexBasis);
        Assert.Equal(initialVersion + 2, config.Version);

        config.SetExperimentalFeatureEnabled(YogaExperimentalFeature.WebFlexBasis, true);
        Assert.Equal(initialVersion + 3, config.Version);
        config.SetExperimentalFeatureEnabled(YogaExperimentalFeature.WebFlexBasis, true);
        Assert.Equal(initialVersion + 3, config.Version);
    }

    // upstream: config_update_invalidates_layout_detects_each_property
    [Fact]
    public void ConfigUpdateInvalidatesLayout_DetectsEachProperty()
    {
        var config1 = new YogaConfig();
        var config2 = new YogaConfig();

        Assert.False(YogaConfig.ConfigUpdateInvalidatesLayout(config1, config2));

        config2.SetErrata(YogaErrata.StretchFlexBasis);
        Assert.True(YogaConfig.ConfigUpdateInvalidatesLayout(config1, config2));
        config1.SetErrata(YogaErrata.StretchFlexBasis);
        Assert.False(YogaConfig.ConfigUpdateInvalidatesLayout(config1, config2));

        config2.PointScaleFactor = 3f;
        Assert.True(YogaConfig.ConfigUpdateInvalidatesLayout(config1, config2));
        config1.PointScaleFactor = 3f;
        Assert.False(YogaConfig.ConfigUpdateInvalidatesLayout(config1, config2));

        config2.UseWebDefaults = true;
        Assert.True(YogaConfig.ConfigUpdateInvalidatesLayout(config1, config2));
        config1.UseWebDefaults = true;
        Assert.False(YogaConfig.ConfigUpdateInvalidatesLayout(config1, config2));

        config2.SetExperimentalFeatureEnabled(YogaExperimentalFeature.WebFlexBasis, true);
        Assert.True(YogaConfig.ConfigUpdateInvalidatesLayout(config1, config2));
    }

    // upstream: config_errata_bitmask_add_remove_operations
    [Fact]
    public void ConfigErrata_BitmaskAddRemoveOperations()
    {
        var config = new YogaConfig();
        config.RemoveErrata(YogaErrata.MinSizeUndefinedInsteadOfAuto);
        config.RemoveErrata(YogaErrata.FlexFirstPassUsesRunningTotals);

        Assert.False(config.HasErrata(YogaErrata.StretchFlexBasis));
        Assert.False(config.HasErrata(YogaErrata.AbsolutePositionWithoutInsetsExcludesPadding));

        config.AddErrata(YogaErrata.StretchFlexBasis);
        Assert.True(config.HasErrata(YogaErrata.StretchFlexBasis));
        Assert.False(config.HasErrata(YogaErrata.AbsolutePositionWithoutInsetsExcludesPadding));

        config.AddErrata(YogaErrata.AbsolutePositionWithoutInsetsExcludesPadding);
        Assert.True(config.HasErrata(YogaErrata.StretchFlexBasis));
        Assert.True(config.HasErrata(YogaErrata.AbsolutePositionWithoutInsetsExcludesPadding));

        config.RemoveErrata(YogaErrata.StretchFlexBasis);
        Assert.False(config.HasErrata(YogaErrata.StretchFlexBasis));
        Assert.True(config.HasErrata(YogaErrata.AbsolutePositionWithoutInsetsExcludesPadding));

        config.RemoveErrata(YogaErrata.AbsolutePositionWithoutInsetsExcludesPadding);
        Assert.Equal(YogaErrata.None, config.Errata);
    }

    // ════════════════════════════════════════════════════════════════════
    // YGContentsNodeHasNewLayoutTest.cpp (96fc051d, #1970)
    // ════════════════════════════════════════════════════════════════════

    // upstream: contents_child_hasNewLayout_not_stamped_on_measure_only_visit
    [Fact]
    public void ContentsChild_HasNewLayoutNotStampedOnMeasureOnlyVisit()
    {
        var config = new YogaConfig();
        var leaf = new YogaNode(config);
        leaf.Width = YogaValue.Point(20f);
        leaf.Height = YogaValue.Point(20f);

        var contents = new YogaNode(config);
        contents.Display = YogaDisplay.Contents;
        contents.InsertChild(leaf, 0);

        var parent = new YogaNode(config);
        parent.FlexGrow = 1f;
        parent.InsertChild(contents, 0);

        var root = new YogaNode(config);
        root.FlexDirection = FlexDirection.Column;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.Overflow = YogaOverflow.Visible;
        root.InsertChild(parent, 0);

        root.CalculateLayout(200f, 200f, FlexLayoutDirection.LTR);

        root.HasNewLayout = false;
        parent.HasNewLayout = false;
        contents.HasNewLayout = false;
        leaf.HasNewLayout = false;

        root.Overflow = YogaOverflow.Scroll;
        root.CalculateLayout(200f, 200f, FlexLayoutDirection.LTR);

        Assert.False(contents.HasNewLayout,
            "contents.HasNewLayout was stamped during a measure-only visit");
    }

    // Port-specific companion (upstream facebook/yoga#1976, b7ff95d2, which has no
    // upstream test): the same measure-only visit must not zero out and flag a
    // display: none child either.
    [Fact]
    public void DisplayNoneChild_HasNewLayoutNotStampedOnMeasureOnlyVisit()
    {
        var config = new YogaConfig();
        var hidden = new YogaNode(config);
        hidden.Display = YogaDisplay.None;
        hidden.Width = YogaValue.Point(20f);
        hidden.Height = YogaValue.Point(20f);

        var leaf = new YogaNode(config);
        leaf.Width = YogaValue.Point(20f);
        leaf.Height = YogaValue.Point(20f);

        var parent = new YogaNode(config);
        parent.FlexGrow = 1f;
        parent.InsertChild(hidden, 0);
        parent.InsertChild(leaf, 1);

        var root = new YogaNode(config);
        root.FlexDirection = FlexDirection.Column;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.Overflow = YogaOverflow.Visible;
        root.InsertChild(parent, 0);

        root.CalculateLayout(200f, 200f, FlexLayoutDirection.LTR);
        Assert.Equal(0f, hidden.LayoutWidth);
        Assert.True(hidden.HasNewLayout);

        root.HasNewLayout = false;
        parent.HasNewLayout = false;
        hidden.HasNewLayout = false;
        leaf.HasNewLayout = false;

        root.Overflow = YogaOverflow.Scroll;
        root.CalculateLayout(200f, 200f, FlexLayoutDirection.LTR);

        // parent's layout pass is served from cache (which still flags parent);
        // only its measure-only visit runs the algorithm.
        Assert.False(hidden.HasNewLayout,
            "display: none child was zeroed out and flagged during a measure-only visit");
    }

    // upstream: absolute_descendant_through_contents_is_reachable_via_hasNewLayout
    [Fact]
    public void AbsoluteDescendantThroughContents_IsReachableViaHasNewLayout()
    {
        var config = new YogaConfig();
        var absoluteChild = new YogaNode(config);
        absoluteChild.PositionType = FlexPositionType.Absolute;
        absoluteChild.SetPosition(YogaEdge.Right, YogaValue.Point(0f));
        absoluteChild.SetPosition(YogaEdge.Bottom, YogaValue.Point(0f));
        absoluteChild.Width = YogaValue.Point(10f);
        absoluteChild.Height = YogaValue.Point(10f);

        var contents = new YogaNode(config);
        contents.Display = YogaDisplay.Contents;
        contents.InsertChild(absoluteChild, 0);

        var staticChild = new YogaNode(config);
        staticChild.PositionType = FlexPositionType.Static;
        staticChild.Width = YogaValue.Point(50f);
        staticChild.Height = YogaValue.Point(50f);
        staticChild.InsertChild(contents, 0);

        var root = new YogaNode(config);
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.InsertChild(staticChild, 0);

        root.CalculateLayout(100f, 100f, FlexLayoutDirection.LTR);

        root.HasNewLayout = false;
        staticChild.HasNewLayout = false;
        contents.HasNewLayout = false;
        absoluteChild.HasNewLayout = false;

        root.Width = YogaValue.Point(150f);
        root.CalculateLayout(150f, 100f, FlexLayoutDirection.LTR);

        Assert.True(absoluteChild.HasNewLayout);
        Assert.True(staticChild.HasNewLayout);
        Assert.True(contents.HasNewLayout,
            "contents node on the path to a freshly-positioned absolute descendant must have HasNewLayout");
        Assert.Equal(140f, absoluteChild.LayoutX);
    }

    // upstream: absolute_phase_cleanup_does_not_stamp_when_parent_layout_skipped
    [Fact]
    public void AbsolutePhaseCleanup_DoesNotStampWhenParentLayoutSkipped()
    {
        var config = new YogaConfig();
        var leaf = new YogaNode(config);
        leaf.Width = YogaValue.Point(10f);
        leaf.Height = YogaValue.Point(10f);

        var contents = new YogaNode(config);
        contents.Display = YogaDisplay.Contents;
        contents.InsertChild(leaf, 0);

        var b = new YogaNode(config);
        b.PositionType = FlexPositionType.Static;
        b.Width = YogaValue.Point(30f);
        b.Height = YogaValue.Point(30f);
        b.InsertChild(contents, 0);

        var a = new YogaNode(config);
        a.PositionType = FlexPositionType.Static;
        a.Width = YogaValue.Point(50f);
        a.Height = YogaValue.Point(50f);
        a.InsertChild(b, 0);

        var root = new YogaNode(config);
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.Overflow = YogaOverflow.Visible;
        root.InsertChild(a, 0);

        root.CalculateLayout(200f, 200f, FlexLayoutDirection.LTR);

        root.HasNewLayout = false;
        a.HasNewLayout = false;
        b.HasNewLayout = false;
        contents.HasNewLayout = false;
        leaf.HasNewLayout = false;

        root.Overflow = YogaOverflow.Scroll;
        root.CalculateLayout(200f, 200f, FlexLayoutDirection.LTR);

        Assert.False(b.HasNewLayout);
        Assert.False(contents.HasNewLayout,
            "contents.HasNewLayout was stamped during a walk where its parent's HasNewLayout remained false");
    }

    // ════════════════════════════════════════════════════════════════════
    // YGDirtyMarkingTest.cpp (d81d9c02). The YGNodeFree tests (f6206ec3, #1969)
    // are not ported: there is no YGNodeFree; RemoveChild already dirties.
    // ════════════════════════════════════════════════════════════════════

    // upstream: dirty_propagation_through_child_set_to_display_contents
    [Fact]
    public void DirtyPropagation_ThroughChildSetToDisplayContents()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);

        var child = new YogaNode(config);
        root.InsertChild(child, 0);

        var grandchild = new YogaNode(config);
        grandchild.Width = YogaValue.Point(10f);
        grandchild.Height = YogaValue.Point(10f);
        child.InsertChild(grandchild, 0);

        child.Display = YogaDisplay.Contents;
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.False(child.IsDirty);

        grandchild.Width = YogaValue.Point(20f);
        Assert.True(root.IsDirty);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(20f, grandchild.LayoutWidth);
    }

    // ════════════════════════════════════════════════════════════════════
    // YGMeasureCacheTest.cpp (033064fe, #2042)
    // ════════════════════════════════════════════════════════════════════

    private static YogaMeasureFunc MeasureMax(global::System.Action onMeasure) =>
        (_, width, widthMode, height, heightMode) =>
        {
            onMeasure();
            return new YogaSize(
                widthMode == YogaMeasureMode.Undefined ? 10f : width,
                heightMode == YogaMeasureMode.Undefined ? 10f : height);
        };

    // upstream: keep_cached_measurement_when_owner_resizes_without_percentages
    [Fact]
    public void KeepCachedMeasurement_WhenOwnerResizesWithoutPercentages()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.Width = YogaValue.Point(100f);

        int measureCount = 0;
        var child = new YogaNode(config);
        child.MeasureFunction = MeasureMax(() => measureCount++);
        child.Width = YogaValue.Point(50f);
        root.InsertChild(child, 0);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        int measuresBeforeResize = measureCount;

        root.Width = YogaValue.Point(200f);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(measuresBeforeResize, measureCount);
    }

    // upstream: remeasure_when_owner_resizes_with_percent_padding
    [Fact]
    public void Remeasure_WhenOwnerResizesWithPercentPadding()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.Width = YogaValue.Point(100f);

        int measureCount = 0;
        var child = new YogaNode(config);
        child.MeasureFunction = MeasureMax(() => measureCount++);
        child.Width = YogaValue.Point(50f);
        child.SetPadding(YogaEdge.Top, YogaValue.Percent(10f));
        root.InsertChild(child, 0);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(10f, child.LayoutPaddingTop);
        int measuresBeforeResize = measureCount;

        root.Width = YogaValue.Point(200f);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(20f, child.LayoutPaddingTop);
        Assert.True(measureCount > measuresBeforeResize,
            $"expected a remeasure; count {measureCount} (before resize {measuresBeforeResize})");
    }

    // ════════════════════════════════════════════════════════════════════
    // YGMeasureTest.cpp (8c5ea16d, #1905)
    // ════════════════════════════════════════════════════════════════════

    // upstream: min_width_larger_than_width_propagates_to_auto_parent
    [Fact]
    public void MinWidthLargerThanWidth_PropagatesToAutoParent()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);

        var child = new YogaNode(config);
        child.FlexDirection = FlexDirection.Row;
        child.Height = YogaValue.Point(50f);
        root.InsertChild(child, 0);

        var grandchild = new YogaNode(config);
        grandchild.Width = YogaValue.Point(50f);
        grandchild.MinWidth = YogaValue.Point(100f);
        grandchild.Height = YogaValue.Point(50f);
        child.InsertChild(grandchild, 0);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);

        Assert.Equal(0f, child.LayoutX);
        Assert.Equal(0f, child.LayoutY);
        Assert.Equal(100f, child.LayoutWidth);
        Assert.Equal(50f, child.LayoutHeight);

        Assert.Equal(0f, grandchild.LayoutX);
        Assert.Equal(0f, grandchild.LayoutY);
        Assert.Equal(100f, grandchild.LayoutWidth);
        Assert.Equal(50f, grandchild.LayoutHeight);
    }

    // ════════════════════════════════════════════════════════════════════
    // YGRelayoutTest.cpp (033064fe, #2042)
    // ════════════════════════════════════════════════════════════════════

    // upstream: percent_padding_follows_owner_size_on_relayout
    [Fact]
    public void PercentPadding_FollowsOwnerSizeOnRelayout()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.Width = YogaValue.Point(100f);

        var child = new YogaNode(config);
        child.Width = YogaValue.Point(50f);
        child.SetPadding(YogaEdge.Left, YogaValue.Percent(50f));
        root.InsertChild(child, 0);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(50f, child.LayoutPaddingLeft);

        root.Width = YogaValue.Point(60f);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(30f, child.LayoutPaddingLeft);
    }

    // upstream: percent_min_width_follows_owner_size_on_relayout
    [Fact]
    public void PercentMinWidth_FollowsOwnerSizeOnRelayout()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.Width = YogaValue.Point(100f);

        var child = new YogaNode(config);
        child.Width = YogaValue.Point(50f);
        child.MinWidth = YogaValue.Percent(100f);
        root.InsertChild(child, 0);

        var grandchild = new YogaNode(config);
        grandchild.Height = YogaValue.Point(10f);
        child.InsertChild(grandchild, 0);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(100f, child.LayoutWidth);
        Assert.Equal(100f, grandchild.LayoutWidth);

        root.Width = YogaValue.Point(200f);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(200f, child.LayoutWidth);
        Assert.Equal(200f, grandchild.LayoutWidth);
    }

    // upstream: percent_min_height_follows_owner_size_on_relayout
    [Fact]
    public void PercentMinHeight_FollowsOwnerSizeOnRelayout()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.FlexDirection = FlexDirection.Row;
        root.Height = YogaValue.Point(100f);

        var child = new YogaNode(config);
        child.Height = YogaValue.Point(50f);
        child.MinHeight = YogaValue.Percent(100f);
        root.InsertChild(child, 0);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(100f, child.LayoutHeight);

        root.Height = YogaValue.Point(200f);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(200f, child.LayoutHeight);
    }

    // ════════════════════════════════════════════════════════════════════
    // YGPersistentNodeCloningTest.cpp display: none tests (07475907 #2055,
    // 41118bd3 #2058), as non-cloning analogs: this port never clones, so the
    // clone-count assertions have no counterpart; the zeroing / HasNewLayout
    // and layout-equivalence assertions do.
    // ════════════════════════════════════════════════════════════════════

    // Clears HasNewLayout like an embedder applying the layout would; like React
    // Native it doesn't descend into display: none nodes.
    private static void ConsumeNewLayout(YogaNode node)
    {
        node.HasNewLayout = false;
        if (node.Display == YogaDisplay.None)
            return;
        for (int i = 0; i < node.ChildCount; i++)
            ConsumeNewLayout(node.GetChild(i));
    }

    private static void AssertZeroedOut(YogaNode node)
    {
        Assert.Equal(0f, node.LayoutWidth);
        Assert.Equal(0f, node.LayoutHeight);
        Assert.True(node.HasNewLayout);
    }

    private static void AssertSameLayout(YogaNode actual, YogaNode expected)
    {
        Assert.Equal(expected.LayoutX, actual.LayoutX);
        Assert.Equal(expected.LayoutY, actual.LayoutY);
        Assert.Equal(expected.LayoutWidth, actual.LayoutWidth);
        Assert.Equal(expected.LayoutHeight, actual.LayoutHeight);
        Assert.Equal(expected.ChildCount, actual.ChildCount);
        for (int i = 0; i < actual.ChildCount; i++)
            AssertSameLayout(actual.GetChild(i), expected.GetChild(i));
    }

    private static YogaNode WithChildren(YogaConfig config, params YogaNode[] children)
    {
        var node = new YogaNode(config);
        for (int i = 0; i < children.Length; i++)
            node.InsertChild(children[i], i);
        return node;
    }

    // upstream analog: relayout_does_not_clone_zeroed_out_display_none_subtree
    [Fact]
    public void Relayout_KeepsDisplayNoneSubtreeZeroedOut()
    {
        var config = new YogaConfig();
        var sibling = new YogaNode(config);
        sibling.Height = YogaValue.Point(1f);

        var d = new YogaNode(config);
        var c = WithChildren(config, d);
        var b = new YogaNode(config);
        var a = WithChildren(config, b, c);
        var hidden = WithChildren(config, a);
        hidden.Display = YogaDisplay.None;

        var root = WithChildren(config, sibling, hidden);
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        ConsumeNewLayout(root);

        for (int pass = 0; pass < 2; pass++)
        {
            sibling.Height = YogaValue.Point(2f + pass);
            root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
            ConsumeNewLayout(root);
            Assert.Equal(2f + pass, sibling.LayoutHeight);
        }

        AssertZeroedOut(a);
        AssertZeroedOut(b);
        AssertZeroedOut(c);
        AssertZeroedOut(d);
    }

    // upstream analog: adding_node_to_display_none_subtree_does_not_clone_zeroed_out_subtrees.
    // Skipping already-zeroed siblings must not skip the newly added node.
    [Fact]
    public void AddingNodeToDisplayNoneSubtree_ZeroesOutNewNode()
    {
        var config = new YogaConfig();
        var e = new YogaNode(config);
        var d = WithChildren(config, e);
        var c = new YogaNode(config);
        var b = WithChildren(config, c);
        var a = WithChildren(config, b);
        var hidden = WithChildren(config, a, d);
        hidden.Display = YogaDisplay.None;

        var root = WithChildren(config, hidden);
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        ConsumeNewLayout(root);

        var newNode = new YogaNode(config);
        newNode.Width = YogaValue.Point(50f);
        newNode.Height = YogaValue.Point(50f);
        b.InsertChild(newNode, 1);
        Assert.True(float.IsNaN(newNode.LayoutWidth), $"precondition: fresh node width {newNode.LayoutWidth}");

        // Nodes under display: none are never laid out, so they stay dirty from
        // construction and InsertChild's dirtying stops at b (upstream behaves the
        // same; its test dirties the path by cloning). Relayout root explicitly.
        root.Height = YogaValue.Point(101f);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        AssertZeroedOut(newNode);
        AssertZeroedOut(c);
        AssertZeroedOut(e);
    }

    // upstream analog: showing_display_none_subtree_matches_never_hidden_layout
    [Fact]
    public void ShowingDisplayNoneSubtree_MatchesNeverHiddenLayout()
    {
        YogaNode MakeRoot(YogaConfig config, YogaDisplay toggledDisplay, out YogaNode sibling, out YogaNode toggled)
        {
            sibling = new YogaNode(config);
            sibling.Height = YogaValue.Point(1f);

            var d = new YogaNode(config);
            d.Width = YogaValue.Point(7f);
            d.Height = YogaValue.Point(7f);
            var c = WithChildren(config, d);
            c.FlexGrow = 1f;
            c.Height = YogaValue.Point(15f);
            var b = new YogaNode(config);
            b.Width = YogaValue.Point(10f);
            b.Height = YogaValue.Point(20f);
            b.SetMargin(YogaEdge.All, YogaValue.Point(3f));
            var a = WithChildren(config, b, c);
            a.FlexDirection = FlexDirection.Row;
            a.SetPadding(YogaEdge.All, YogaValue.Point(5f));
            toggled = WithChildren(config, a);
            toggled.Display = toggledDisplay;

            var root = WithChildren(config, sibling, toggled);
            root.Width = YogaValue.Point(100f);
            root.Height = YogaValue.Point(100f);
            return root;
        }

        var root = MakeRoot(new YogaConfig(), YogaDisplay.None, out var sibling, out var toggled);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        ConsumeNewLayout(root);

        // Lay out again while still hidden.
        sibling.Height = YogaValue.Point(2f);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        ConsumeNewLayout(root);

        toggled.Display = YogaDisplay.Flex;
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        var expectedRoot = MakeRoot(new YogaConfig(), YogaDisplay.Flex, out var expectedSibling, out var expectedToggled);
        expectedSibling.Height = YogaValue.Point(2f);
        expectedRoot.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.Equal(100f, expectedToggled.LayoutWidth);
        AssertSameLayout(root, expectedRoot);
    }

    // upstream analog: relayout_does_not_clone_display_contents_in_display_none_subtree
    [Fact]
    public void Relayout_ZeroesDisplayContentsInDisplayNoneSubtree()
    {
        var config = new YogaConfig();
        var d = new YogaNode(config);
        var c = new YogaNode(config);
        var b = WithChildren(config, c);
        var a = WithChildren(config, b, d);
        a.Display = YogaDisplay.Contents;
        var hidden = WithChildren(config, a);
        hidden.Display = YogaDisplay.None;

        var root = WithChildren(config, hidden);
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        ConsumeNewLayout(root);

        root.Height = YogaValue.Point(101f);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        AssertZeroedOut(a);
        AssertZeroedOut(b);
        AssertZeroedOut(c);
        AssertZeroedOut(d);
    }

    // upstream analog: relayout_clones_display_none_subtree_without_new_layout.
    // An embedder that clears HasNewLayout under display: none too gets the subtree
    // zeroed out (and flagged) again: the #2058 skip only applies to subtrees that
    // still carry HasNewLayout.
    [Fact]
    public void Relayout_RezeroesDisplayNoneSubtreeWithoutNewLayout()
    {
        var config = new YogaConfig();
        var b = new YogaNode(config);
        var a = WithChildren(config, b);
        var hidden = WithChildren(config, a);
        hidden.Display = YogaDisplay.None;

        var root = WithChildren(config, hidden);
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        root.HasNewLayout = false;
        hidden.HasNewLayout = false;
        a.HasNewLayout = false;
        b.HasNewLayout = false;

        root.Height = YogaValue.Point(101f);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.True(hidden.HasNewLayout);
        AssertZeroedOut(a);
        AssertZeroedOut(b);
    }
}
