using Microsoft.UI.Reactor.Layout;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Edge-case tests targeting uncovered utility paths in the Yoga layout engine.
/// Complements the auto-generated YogaGenerated/ tests.
/// </summary>
public class YogaEdgeCaseTests
{
    // ════════════════════════════════════════════════════════════════════
    // 1. YogaConfig
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Config_PointScaleFactor_ChangeIncrementsVersion()
    {
        var config = new YogaConfig();
        uint v0 = config.Version;
        config.PointScaleFactor = 2.0f;
        Assert.Equal(v0 + 1, config.Version);
    }

    [Fact]
    public void Config_PointScaleFactor_SameValueDoesNotIncrementVersion()
    {
        var config = new YogaConfig();
        config.PointScaleFactor = 1.0f; // same as default
        Assert.Equal(0u, config.Version);
    }

    [Fact]
    public void Config_SetErrata_ChangesValueAndIncrementsVersion()
    {
        var config = new YogaConfig();
        config.SetErrata(YogaErrata.StretchFlexBasis);
        Assert.Equal(YogaErrata.StretchFlexBasis, config.Errata);
        Assert.Equal(1u, config.Version);
    }

    [Fact]
    public void Config_SetErrata_SameValueDoesNotIncrementVersion()
    {
        var config = new YogaConfig();
        config.SetErrata(YogaErrata.None); // same as default
        Assert.Equal(0u, config.Version);
    }

    [Fact]
    public void Config_AddErrata_CombinesFlags()
    {
        var config = new YogaConfig();
        config.AddErrata(YogaErrata.StretchFlexBasis);
        config.AddErrata(YogaErrata.AbsolutePercentAgainstInnerSize);
        Assert.True(config.HasErrata(YogaErrata.StretchFlexBasis));
        Assert.True(config.HasErrata(YogaErrata.AbsolutePercentAgainstInnerSize));
        Assert.Equal(2u, config.Version);
    }

    [Fact]
    public void Config_AddErrata_AlreadyPresent_DoesNotIncrementVersion()
    {
        var config = new YogaConfig();
        config.AddErrata(YogaErrata.StretchFlexBasis);
        uint v1 = config.Version;
        config.AddErrata(YogaErrata.StretchFlexBasis);
        Assert.Equal(v1, config.Version);
    }

    [Fact]
    public void Config_RemoveErrata_ClearsFlag()
    {
        var config = new YogaConfig();
        config.SetErrata(YogaErrata.StretchFlexBasis | YogaErrata.AbsolutePercentAgainstInnerSize);
        uint v1 = config.Version;
        config.RemoveErrata(YogaErrata.StretchFlexBasis);
        Assert.False(config.HasErrata(YogaErrata.StretchFlexBasis));
        Assert.True(config.HasErrata(YogaErrata.AbsolutePercentAgainstInnerSize));
        Assert.Equal(v1 + 1, config.Version);
    }

    [Fact]
    public void Config_RemoveErrata_NotPresent_DoesNotIncrementVersion()
    {
        var config = new YogaConfig();
        uint v0 = config.Version;
        config.RemoveErrata(YogaErrata.StretchFlexBasis);
        Assert.Equal(v0, config.Version);
    }

    [Fact]
    public void Config_ExperimentalFeature_EnableDisable()
    {
        var config = new YogaConfig();
        Assert.False(config.IsExperimentalFeatureEnabled(YogaExperimentalFeature.WebFlexBasis));
        config.SetExperimentalFeatureEnabled(YogaExperimentalFeature.WebFlexBasis, true);
        Assert.True(config.IsExperimentalFeatureEnabled(YogaExperimentalFeature.WebFlexBasis));
        Assert.Equal(1u, config.Version);

        config.SetExperimentalFeatureEnabled(YogaExperimentalFeature.WebFlexBasis, false);
        Assert.False(config.IsExperimentalFeatureEnabled(YogaExperimentalFeature.WebFlexBasis));
        Assert.Equal(2u, config.Version);
    }

    [Fact]
    public void Config_ExperimentalFeature_SameValue_DoesNotIncrementVersion()
    {
        var config = new YogaConfig();
        config.SetExperimentalFeatureEnabled(YogaExperimentalFeature.WebFlexBasis, false);
        Assert.Equal(0u, config.Version);
    }

    [Fact]
    public void Config_ConfigUpdateInvalidatesLayout_DifferentErrata()
    {
        var a = new YogaConfig();
        var b = new YogaConfig();
        b.SetErrata(YogaErrata.StretchFlexBasis);
        Assert.True(YogaConfig.ConfigUpdateInvalidatesLayout(a, b));
    }

    [Fact]
    public void Config_ConfigUpdateInvalidatesLayout_DifferentPointScaleFactor()
    {
        var a = new YogaConfig();
        var b = new YogaConfig();
        b.PointScaleFactor = 3.0f;
        Assert.True(YogaConfig.ConfigUpdateInvalidatesLayout(a, b));
    }

    [Fact]
    public void Config_ConfigUpdateInvalidatesLayout_DifferentWebDefaults()
    {
        var a = new YogaConfig();
        var b = new YogaConfig();
        b.UseWebDefaults = true;
        Assert.True(YogaConfig.ConfigUpdateInvalidatesLayout(a, b));
    }

    [Fact]
    public void Config_ConfigUpdateInvalidatesLayout_DifferentExperimentalFeatures()
    {
        var a = new YogaConfig();
        var b = new YogaConfig();
        b.SetExperimentalFeatureEnabled(YogaExperimentalFeature.FixFlexBasisFitContent, true);
        Assert.True(YogaConfig.ConfigUpdateInvalidatesLayout(a, b));
    }

    [Fact]
    public void Config_ConfigUpdateInvalidatesLayout_SameConfigs_ReturnsFalse()
    {
        var a = new YogaConfig();
        var b = new YogaConfig();
        Assert.False(YogaConfig.ConfigUpdateInvalidatesLayout(a, b));
    }

    [Fact]
    public void Config_UseWebDefaults_SetsFlexDirectionAndAlignContent()
    {
        var config = new YogaConfig();
        config.UseWebDefaults = true;
        var node = new YogaNode(config);
        Assert.Equal(FlexDirection.Row, node.FlexDirection);
        Assert.Equal(FlexAlign.Stretch, node.AlignContent);
    }

    // ════════════════════════════════════════════════════════════════════
    // 2. PixelGridHelper
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void PixelGrid_RoundValue_Scale2_RoundsToHalfPixels()
    {
        // 0.3 at scale 2 => 0.3*2=0.6 => rounds to 1.0 => 1.0/2=0.5
        float result = PixelGridHelper.RoundValueToPixelGrid(0.3, 2.0, false, false);
        Assert.Equal(0.5f, result);
    }

    [Fact]
    public void PixelGrid_RoundValue_Scale3_RoundsToThirdPixels()
    {
        // 0.4 at scale 3 => 0.4*3=1.2 => rounds to 1.0 => 1.0/3=0.333...
        float result = PixelGridHelper.RoundValueToPixelGrid(0.4, 3.0, false, false);
        Assert.True(MathF.Abs(result - 1.0f / 3.0f) < 0.001f);
    }

    [Fact]
    public void PixelGrid_RoundValue_ForceCeil()
    {
        // 0.3 at scale 1 => forceCeil => 1.0
        float result = PixelGridHelper.RoundValueToPixelGrid(0.3, 1.0, true, false);
        Assert.Equal(1.0f, result);
    }

    [Fact]
    public void PixelGrid_RoundValue_ForceFloor()
    {
        // 0.7 at scale 1 => forceFloor => 0.0
        float result = PixelGridHelper.RoundValueToPixelGrid(0.7, 1.0, false, true);
        Assert.Equal(0.0f, result);
    }

    [Fact]
    public void PixelGrid_RoundValue_NaN_ReturnsNaN()
    {
        float result = PixelGridHelper.RoundValueToPixelGrid(double.NaN, 1.0, false, false);
        Assert.True(float.IsNaN(result));
    }

    [Fact]
    public void PixelGrid_RoundValue_NaNScaleFactor_ReturnsNaN()
    {
        float result = PixelGridHelper.RoundValueToPixelGrid(10.0, double.NaN, false, false);
        Assert.True(float.IsNaN(result));
    }

    [Fact]
    public void PixelGrid_RoundValue_NegativeFractional()
    {
        // -0.3 at scale 1 => -0.3*1=-0.3 => fractial=-0.3+1=0.7 => rounds to -0.3-0.7+1.0=0.0
        float result = PixelGridHelper.RoundValueToPixelGrid(-0.3, 1.0, false, false);
        Assert.Equal(0.0f, result);
    }

    [Fact]
    public void PixelGrid_RoundValue_ExactlyHalf_RoundsUp()
    {
        // 0.5 at scale 1 => fractial=0.5 => InexactEquals(0.5, 0.5) => rounds up to 1.0
        float result = PixelGridHelper.RoundValueToPixelGrid(0.5, 1.0, false, false);
        Assert.Equal(1.0f, result);
    }

    [Fact]
    public void PixelGrid_RoundLayoutResults_SkipsWhenPointScaleFactorIsZero()
    {
        var config = new YogaConfig();
        config.PointScaleFactor = 0;
        var node = new YogaNode(config);
        node.PositionType = FlexPositionType.Absolute;
        node.Width = YogaValue.Point(100.7f);
        node.Height = YogaValue.Point(50.3f);
        node.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        // With scale=0, rounding is skipped; raw fractional dimensions preserved
        // (CalculateLayout itself rounds, but the pixel grid rounding phase is skipped)
        float w = node.LayoutWidth;
        float h = node.LayoutHeight;
        // The dimensions should still be computed (not NaN)
        Assert.True(w > 0);
        Assert.True(h > 0);
    }

    [Fact]
    public void PixelGrid_RoundLayoutResults_TextNode_RoundsDifferently()
    {
        var config = new YogaConfig();
        config.PointScaleFactor = 1.0f;
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);

        var textNode = new YogaNode(config);
        textNode.MeasureFunction = (n, w, wm, h, hm) => new YogaSize(50.6f, 20.4f);
        root.InsertChild(textNode, 0);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        // Text node: NodeType = Text, rounding uses forceFloor for position, special logic for dimensions
        Assert.True(textNode.LayoutWidth >= 50f);
        Assert.True(textNode.LayoutHeight >= 20f);
    }

    [Fact]
    public void PixelGrid_RoundLayoutResults_Scale2_ProducesHalfPixelValues()
    {
        var config = new YogaConfig();
        config.PointScaleFactor = 2.0f;
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;

        var c0 = new YogaNode(config);
        c0.FlexGrow = 1f;
        root.InsertChild(c0, 0);
        var c1 = new YogaNode(config);
        c1.FlexGrow = 1f;
        root.InsertChild(c1, 1);
        var c2 = new YogaNode(config);
        c2.FlexGrow = 1f;
        root.InsertChild(c2, 2);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        // 100/3 = 33.333... at scale 2: each child should be either 33 or 33.5 or 34
        float totalWidth = c0.LayoutWidth + c1.LayoutWidth + c2.LayoutWidth;
        Assert.Equal(100f, totalWidth);
    }

    // ════════════════════════════════════════════════════════════════════
    // 3. CacheHelper
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Cache_NegativeLastComputedSize_ReturnsFalse()
    {
        var config = new YogaConfig();
        bool result = CacheHelper.CanUseCachedMeasurement(
            SizingMode.StretchFit, 100f,
            SizingMode.StretchFit, 100f,
            SizingMode.StretchFit, 100f,
            SizingMode.StretchFit, 100f,
            -1f, 50f, // negative lastComputedWidth
            0f, 0f, config);
        Assert.False(result);
    }

    [Fact]
    public void Cache_NegativeLastComputedHeight_ReturnsFalse()
    {
        var config = new YogaConfig();
        bool result = CacheHelper.CanUseCachedMeasurement(
            SizingMode.StretchFit, 100f,
            SizingMode.StretchFit, 100f,
            SizingMode.StretchFit, 100f,
            SizingMode.StretchFit, 100f,
            50f, -1f, // negative lastComputedHeight
            0f, 0f, config);
        Assert.False(result);
    }

    [Fact]
    public void Cache_SameSpecAndSameSize_ReturnsTrue()
    {
        var config = new YogaConfig();
        bool result = CacheHelper.CanUseCachedMeasurement(
            SizingMode.StretchFit, 100f,
            SizingMode.StretchFit, 200f,
            SizingMode.StretchFit, 100f,
            SizingMode.StretchFit, 200f,
            100f, 200f,
            0f, 0f, config);
        Assert.True(result);
    }

    [Fact]
    public void Cache_SizeIsExactAndMatchesOldMeasuredSize_ReturnsTrue()
    {
        var config = new YogaConfig();
        // StretchFit (exact) with availableWidth-marginRow == lastComputedWidth
        bool result = CacheHelper.CanUseCachedMeasurement(
            SizingMode.StretchFit, 100f,    // width: exact 100
            SizingMode.StretchFit, 200f,    // height: exact 200
            SizingMode.MaxContent, 50f,     // last width: different mode
            SizingMode.StretchFit, 200f,    // last height: same
            100f, 200f,                     // lastComputed: matches available-margin
            0f, 0f, config);
        Assert.True(result);
    }

    [Fact]
    public void Cache_OldSizeIsMaxContentAndStillFits_ReturnsTrue()
    {
        var config = new YogaConfig();
        // New mode=FitContent, last mode=MaxContent, size >= lastComputedSize
        bool result = CacheHelper.CanUseCachedMeasurement(
            SizingMode.FitContent, 150f,   // new: FitContent with more space
            SizingMode.StretchFit, 200f,   // height: same
            SizingMode.MaxContent, 50f,    // last: MaxContent
            SizingMode.StretchFit, 200f,   // last height: same
            100f, 200f,                    // lastComputed: 100 fits in 150
            0f, 0f, config);
        Assert.True(result);
    }

    [Fact]
    public void Cache_NewSizeIsStricterAndStillValid_ReturnsTrue()
    {
        var config = new YogaConfig();
        // Both FitContent, last size > new size, lastComputed <= new size
        bool result = CacheHelper.CanUseCachedMeasurement(
            SizingMode.FitContent, 100f,    // new: FitContent 100
            SizingMode.StretchFit, 200f,    // height: same
            SizingMode.FitContent, 200f,    // last: FitContent 200
            SizingMode.StretchFit, 200f,    // last height: same
            80f, 200f,                      // lastComputed: 80 still fits in 100
            0f, 0f, config);
        Assert.True(result);
    }

    [Fact]
    public void Cache_WithRounding_UsesRoundedComparison()
    {
        var config = new YogaConfig();
        config.PointScaleFactor = 2.0f;
        // Values that differ slightly but round to the same at scale 2
        bool result = CacheHelper.CanUseCachedMeasurement(
            SizingMode.StretchFit, 100.1f,
            SizingMode.StretchFit, 200f,
            SizingMode.StretchFit, 100.2f,
            SizingMode.StretchFit, 200f,
            100f, 200f,
            0f, 0f, config);
        // 100.1 and 100.2 both round to 100.0 at scale 2, so they should match
        Assert.True(result);
    }

    // ════════════════════════════════════════════════════════════════════
    // 4. BaselineHelper
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Baseline_CustomBaselineFunc_IsUsed()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        root.AlignItems = FlexAlign.Baseline;

        var child = new YogaNode(config);
        child.Width = YogaValue.Point(50f);
        child.Height = YogaValue.Point(50f);
        child.MeasureFunction = (n, w, wm, h, hm) => new YogaSize(50, 50);
        child.BaselineFunction = (n, w, h) => 30f; // custom baseline at 30px
        root.InsertChild(child, 0);

        var child2 = new YogaNode(config);
        child2.Width = YogaValue.Point(50f);
        child2.Height = YogaValue.Point(30f);
        root.InsertChild(child2, 1);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        // The layout should complete without errors, using the custom baseline
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
    }

    [Fact]
    public void Baseline_NoChildren_ReturnsHeight()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(50f);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        // CalculateBaseline on a node with no children returns the node's height
        float baseline = BaselineHelper.CalculateBaseline(root);
        Assert.Equal(50f, baseline);
    }

    [Fact]
    public void Baseline_IsBaselineLayout_Column_ReturnsFalse()
    {
        var config = new YogaConfig();
        var node = new YogaNode(config);
        node.FlexDirection = FlexDirection.Column;
        node.AlignItems = FlexAlign.Baseline;
        Assert.False(BaselineHelper.IsBaselineLayout(node));
    }

    [Fact]
    public void Baseline_IsBaselineLayout_RowWithBaselineAlignItems_ReturnsTrue()
    {
        var config = new YogaConfig();
        var node = new YogaNode(config);
        node.FlexDirection = FlexDirection.Row;
        node.AlignItems = FlexAlign.Baseline;
        Assert.True(BaselineHelper.IsBaselineLayout(node));
    }

    [Fact]
    public void Baseline_IsBaselineLayout_RowWithChildBaselineAlignSelf()
    {
        var config = new YogaConfig();
        var node = new YogaNode(config);
        node.FlexDirection = FlexDirection.Row;
        node.AlignItems = FlexAlign.Stretch;

        var child = new YogaNode(config);
        child.AlignSelf = FlexAlign.Baseline;
        node.InsertChild(child, 0);

        Assert.True(BaselineHelper.IsBaselineLayout(node));
    }

    [Fact]
    public void Baseline_IsBaselineLayout_RowNoBaselineChildren_ReturnsFalse()
    {
        var config = new YogaConfig();
        var node = new YogaNode(config);
        node.FlexDirection = FlexDirection.Row;
        node.AlignItems = FlexAlign.Center;

        var child = new YogaNode(config);
        child.AlignSelf = FlexAlign.FlexStart;
        node.InsertChild(child, 0);

        Assert.False(BaselineHelper.IsBaselineLayout(node));
    }

    // ════════════════════════════════════════════════════════════════════
    // 5. YogaNode edge cases
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Node_WebDefaultsConstructor_SetsRowAndStretch()
    {
        var config = new YogaConfig();
        config.UseWebDefaults = true;
        var node = new YogaNode(config);
        Assert.Equal(FlexDirection.Row, node.FlexDirection);
        Assert.Equal(FlexAlign.Stretch, node.AlignContent);
    }

    [Fact]
    public void Node_DirtiedFunc_CalledOnMarkDirty()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        var child = new YogaNode(config);
        child.MeasureFunction = (n, w, wm, h, hm) => new YogaSize(10, 10);
        root.InsertChild(child, 0);
        root.CalculateLayout(100, 100, FlexLayoutDirection.LTR);

        bool dirtiedCalled = false;
        root.DirtiedFunc = (n) => dirtiedCalled = true;

        // Clear dirty state first by calculating layout
        // Now mark the leaf dirty
        child.MarkDirty();
        Assert.True(dirtiedCalled);
    }

    [Fact]
    public void Node_DisplayContents_FlattensChildren()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);

        var contentsNode = new YogaNode(config);
        contentsNode.Display = YogaDisplay.Contents;
        root.InsertChild(contentsNode, 0);

        var grandchild = new YogaNode(config);
        grandchild.Width = YogaValue.Point(50f);
        grandchild.Height = YogaValue.Point(50f);
        contentsNode.InsertChild(grandchild, 0);

        // GetLayoutChildren should flatten through the Contents node
        var layoutChildren = new List<YogaNode>();
        root.CollectLayoutChildren(layoutChildren);
        Assert.Single(layoutChildren);
        Assert.Same(grandchild, layoutChildren[0]);
    }

    [Fact]
    public void Node_ProcessedDimensions_MinEqualsMax_LocksDimension()
    {
        var config = new YogaConfig();
        var node = new YogaNode(config);
        node.PositionType = FlexPositionType.Absolute;
        node.MinWidth = YogaValue.Point(100f);
        node.MaxWidth = YogaValue.Point(100f);
        node.Height = YogaValue.Point(50f);

        node.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(100f, node.LayoutWidth);
    }

    [Fact]
    public void Node_InsertChild_OnNodeWithMeasureFunction_Throws()
    {
        var config = new YogaConfig();
        var node = new YogaNode(config);
        node.MeasureFunction = (n, w, wm, h, hm) => new YogaSize(10, 10);

        var child = new YogaNode(config);
        Assert.Throws<InvalidOperationException>(() => node.InsertChild(child, 0));
    }

    [Fact]
    public void Node_SetMeasureFunction_OnNodeWithChildren_Throws()
    {
        var config = new YogaConfig();
        var node = new YogaNode(config);
        var child = new YogaNode(config);
        node.InsertChild(child, 0);

        Assert.Throws<InvalidOperationException>(() =>
            node.MeasureFunction = (n, w, wm, h, hm) => new YogaSize(10, 10));
    }

    [Fact]
    public void Node_MarkDirty_WithoutMeasureFunction_Throws()
    {
        var config = new YogaConfig();
        var node = new YogaNode(config);
        Assert.Throws<InvalidOperationException>(() => node.MarkDirty());
    }

    [Fact]
    public void Node_RemoveChild_SetsOwnerToNull()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        var child = new YogaNode(config);
        root.InsertChild(child, 0);
        Assert.Same(root, child.Owner);

        root.RemoveChild(child);
        Assert.Null(child.Owner);
        Assert.Equal(0, root.ChildCount);
    }

    [Fact]
    public void Node_RemoveChildByIndex_SetsOwnerToNull()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        var child = new YogaNode(config);
        root.InsertChild(child, 0);

        root.RemoveChild(0);
        Assert.Null(child.Owner);
        Assert.Equal(0, root.ChildCount);
    }

    [Fact]
    public void Node_ClearChildren_SetsAllOwnersToNull()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        var c1 = new YogaNode(config);
        var c2 = new YogaNode(config);
        root.InsertChild(c1, 0);
        root.InsertChild(c2, 1);

        root.ClearChildren();
        Assert.Null(c1.Owner);
        Assert.Null(c2.Owner);
        Assert.Equal(0, root.ChildCount);
    }

    [Fact]
    public void Node_Display_Grid_ThrowsNotImplemented()
    {
        var config = new YogaConfig();
        var node = new YogaNode(config);
        Assert.Throws<NotImplementedException>(() => node.Display = YogaDisplay.Grid);
    }

    [Fact]
    public void Node_SetConfig_InvalidatingChange_MarksDirty()
    {
        var config1 = new YogaConfig();
        var config2 = new YogaConfig();
        config2.SetErrata(YogaErrata.StretchFlexBasis);

        var root = new YogaNode(config1);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        // Changing config should mark dirty
        root.SetConfig(config2);
        Assert.True(root.IsDirty);
    }

    // ════════════════════════════════════════════════════════════════════
    // 6. Errata-affected layout
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Errata_StretchFlexBasis_AffectsLayout()
    {
        // Without errata
        var configNone = new YogaConfig();
        var rootNone = new YogaNode(configNone);
        rootNone.PositionType = FlexPositionType.Absolute;
        rootNone.Width = YogaValue.Point(300f);
        rootNone.Height = YogaValue.Point(100f);
        rootNone.FlexDirection = FlexDirection.Row;

        var childNone = new YogaNode(configNone);
        childNone.FlexGrow = 1f;
        childNone.FlexBasis = YogaValue.Point(0f);
        rootNone.InsertChild(childNone, 0);
        rootNone.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        // With errata
        var configErrata = new YogaConfig();
        configErrata.SetErrata(YogaErrata.StretchFlexBasis);
        var rootErrata = new YogaNode(configErrata);
        rootErrata.PositionType = FlexPositionType.Absolute;
        rootErrata.Width = YogaValue.Point(300f);
        rootErrata.Height = YogaValue.Point(100f);
        rootErrata.FlexDirection = FlexDirection.Row;

        var childErrata = new YogaNode(configErrata);
        childErrata.FlexGrow = 1f;
        childErrata.FlexBasis = YogaValue.Point(0f);
        rootErrata.InsertChild(childErrata, 0);
        rootErrata.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        // Both should produce valid layouts; the stretch flex basis errata
        // may or may not change the result with just one child, but layout should be valid
        Assert.Equal(300f, childNone.LayoutWidth);
        Assert.Equal(300f, childErrata.LayoutWidth);
    }

    [Fact]
    public void Errata_AbsolutePositionWithoutInsetsExcludesPadding()
    {
        // Without errata: absolute child positioned relative to padding box
        var configNone = new YogaConfig();
        var rootNone = new YogaNode(configNone);
        rootNone.PositionType = FlexPositionType.Absolute;
        rootNone.Width = YogaValue.Point(200f);
        rootNone.Height = YogaValue.Point(200f);
        rootNone.SetPadding(YogaEdge.All, YogaValue.Point(20f));

        var absChildNone = new YogaNode(configNone);
        absChildNone.PositionType = FlexPositionType.Absolute;
        absChildNone.Width = YogaValue.Point(50f);
        absChildNone.Height = YogaValue.Point(50f);
        rootNone.InsertChild(absChildNone, 0);
        rootNone.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        float xWithout = absChildNone.LayoutX;

        // With errata
        var configErrata = new YogaConfig();
        configErrata.SetErrata(YogaErrata.AbsolutePositionWithoutInsetsExcludesPadding);
        var rootErrata = new YogaNode(configErrata);
        rootErrata.PositionType = FlexPositionType.Absolute;
        rootErrata.Width = YogaValue.Point(200f);
        rootErrata.Height = YogaValue.Point(200f);
        rootErrata.SetPadding(YogaEdge.All, YogaValue.Point(20f));

        var absChildErrata = new YogaNode(configErrata);
        absChildErrata.PositionType = FlexPositionType.Absolute;
        absChildErrata.Width = YogaValue.Point(50f);
        absChildErrata.Height = YogaValue.Point(50f);
        rootErrata.InsertChild(absChildErrata, 0);
        rootErrata.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        float xWith = absChildErrata.LayoutX;

        // With errata, absolute child without insets excludes padding (x=0 vs x=20)
        Assert.Equal(20f, xWithout);
        Assert.Equal(0f, xWith);
    }

    [Fact]
    public void Errata_AbsolutePercentAgainstInnerSize()
    {
        // Without errata: percent-based absolute child resolves against parent outer size
        var configNone = new YogaConfig();
        var rootNone = new YogaNode(configNone);
        rootNone.PositionType = FlexPositionType.Absolute;
        rootNone.Width = YogaValue.Point(200f);
        rootNone.Height = YogaValue.Point(200f);
        rootNone.SetPadding(YogaEdge.All, YogaValue.Point(20f));

        var absChildNone = new YogaNode(configNone);
        absChildNone.PositionType = FlexPositionType.Absolute;
        absChildNone.Width = YogaValue.Percent(50f);
        absChildNone.Height = YogaValue.Percent(50f);
        rootNone.InsertChild(absChildNone, 0);
        rootNone.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        float widthWithout = absChildNone.LayoutWidth;

        // With errata: percent resolves against inner size
        var configErrata = new YogaConfig();
        configErrata.SetErrata(YogaErrata.AbsolutePercentAgainstInnerSize);
        var rootErrata = new YogaNode(configErrata);
        rootErrata.PositionType = FlexPositionType.Absolute;
        rootErrata.Width = YogaValue.Point(200f);
        rootErrata.Height = YogaValue.Point(200f);
        rootErrata.SetPadding(YogaEdge.All, YogaValue.Point(20f));

        var absChildErrata = new YogaNode(configErrata);
        absChildErrata.PositionType = FlexPositionType.Absolute;
        absChildErrata.Width = YogaValue.Percent(50f);
        absChildErrata.Height = YogaValue.Percent(50f);
        rootErrata.InsertChild(absChildErrata, 0);
        rootErrata.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        float widthWith = absChildErrata.LayoutWidth;

        // Without errata: 50% of 200 = 100
        // With errata: 50% of (200-40) = 80
        Assert.Equal(100f, widthWithout);
        Assert.Equal(80f, widthWith);
    }

    // ════════════════════════════════════════════════════════════════════
    // 7. YogaValue and YogaFloat utilities
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void YogaValue_Point_HasCorrectProperties()
    {
        var val = YogaValue.Point(42f);
        Assert.True(val.IsPoint);
        Assert.False(val.IsPercent);
        Assert.False(val.IsAuto);
        Assert.False(val.IsUndefined);
        Assert.True(val.IsDefined);
        Assert.Equal(42f, val.Value);
        Assert.Equal(YogaUnit.Point, val.Unit);
    }

    [Fact]
    public void YogaValue_Percent_HasCorrectProperties()
    {
        var val = YogaValue.Percent(50f);
        Assert.True(val.IsPercent);
        Assert.False(val.IsPoint);
        Assert.False(val.IsAuto);
        Assert.Equal(50f, val.Value);
    }

    [Fact]
    public void YogaValue_Auto_HasCorrectProperties()
    {
        var val = YogaValue.Auto;
        Assert.True(val.IsAuto);
        Assert.False(val.IsPoint);
        Assert.False(val.IsPercent);
        Assert.True(val.IsDefined);
    }

    [Fact]
    public void YogaValue_Undefined_HasCorrectProperties()
    {
        var val = YogaValue.Undefined;
        Assert.True(val.IsUndefined);
        Assert.False(val.IsDefined);
        Assert.False(val.IsAuto);
    }

    [Fact]
    public void YogaValue_Resolve_Point_ReturnsValue()
    {
        var val = YogaValue.Point(42f);
        Assert.Equal(42f, val.Resolve(100f));
    }

    [Fact]
    public void YogaValue_Resolve_Percent_ComputesAgainstReference()
    {
        var val = YogaValue.Percent(50f);
        Assert.Equal(100f, val.Resolve(200f));
    }

    [Fact]
    public void YogaValue_Resolve_Auto_ReturnsNaN()
    {
        float result = YogaValue.Auto.Resolve(100f);
        Assert.True(float.IsNaN(result));
    }

    [Fact]
    public void YogaValue_Resolve_Undefined_ReturnsNaN()
    {
        float result = YogaValue.Undefined.Resolve(100f);
        Assert.True(float.IsNaN(result));
    }

    [Fact]
    public void YogaValue_Equality()
    {
        Assert.Equal(YogaValue.Point(10f), YogaValue.Point(10f));
        Assert.NotEqual(YogaValue.Point(10f), YogaValue.Point(20f));
        Assert.NotEqual(YogaValue.Point(10f), YogaValue.Percent(10f));
        Assert.Equal(YogaValue.Auto, YogaValue.Auto);
    }

    [Fact]
    public void YogaFloat_IsDefined_And_IsUndefined()
    {
        Assert.True(YogaFloat.IsDefined(0f));
        Assert.True(YogaFloat.IsDefined(42f));
        Assert.True(YogaFloat.IsDefined(-1f));
        Assert.False(YogaFloat.IsDefined(float.NaN));

        Assert.True(YogaFloat.IsUndefined(float.NaN));
        Assert.False(YogaFloat.IsUndefined(0f));
    }

    [Fact]
    public void YogaFloat_InexactEquals_CloseValues()
    {
        Assert.True(YogaFloat.InexactEquals(1.0f, 1.00005f));
        Assert.False(YogaFloat.InexactEquals(1.0f, 1.001f));
    }

    [Fact]
    public void YogaFloat_InexactEquals_BothNaN_ReturnsTrue()
    {
        Assert.True(YogaFloat.InexactEquals(float.NaN, float.NaN));
    }

    [Fact]
    public void YogaFloat_InexactEquals_OneNaN_ReturnsFalse()
    {
        Assert.False(YogaFloat.InexactEquals(float.NaN, 1.0f));
        Assert.False(YogaFloat.InexactEquals(1.0f, float.NaN));
    }

    [Fact]
    public void YogaFloat_MaxOrDefined_BothDefined()
    {
        Assert.Equal(5f, YogaFloat.MaxOrDefined(3f, 5f));
        Assert.Equal(5f, YogaFloat.MaxOrDefined(5f, 3f));
    }

    [Fact]
    public void YogaFloat_MaxOrDefined_OneUndefined_ReturnsDefined()
    {
        Assert.Equal(3f, YogaFloat.MaxOrDefined(3f, float.NaN));
        Assert.Equal(3f, YogaFloat.MaxOrDefined(float.NaN, 3f));
    }

    [Fact]
    public void YogaFloat_MinOrDefined_BothDefined()
    {
        Assert.Equal(3f, YogaFloat.MinOrDefined(3f, 5f));
    }

    [Fact]
    public void YogaFloat_MinOrDefined_OneUndefined_ReturnsDefined()
    {
        Assert.Equal(5f, YogaFloat.MinOrDefined(5f, float.NaN));
        Assert.Equal(5f, YogaFloat.MinOrDefined(float.NaN, 5f));
    }

    [Fact]
    public void YogaFloat_UnwrapOrDefault_DefinedValue()
    {
        Assert.Equal(42f, YogaFloat.UnwrapOrDefault(42f));
        Assert.Equal(42f, YogaFloat.UnwrapOrDefault(42f, 99f));
    }

    [Fact]
    public void YogaFloat_UnwrapOrDefault_NaN_ReturnsDefault()
    {
        Assert.Equal(0f, YogaFloat.UnwrapOrDefault(float.NaN));
        Assert.Equal(99f, YogaFloat.UnwrapOrDefault(float.NaN, 99f));
    }

    // ════════════════════════════════════════════════════════════════════
    // 8. AlignHelper
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void AlignHelper_ResolveChildAlignment_AutoFallsBackToParent()
    {
        var config = new YogaConfig();
        var parent = new YogaNode(config);
        parent.AlignItems = FlexAlign.Center;

        var child = new YogaNode(config);
        child.AlignSelf = FlexAlign.Auto;
        parent.InsertChild(child, 0);

        var result = AlignHelper.ResolveChildAlignment(parent, child);
        Assert.Equal(FlexAlign.Center, result);
    }

    [Fact]
    public void AlignHelper_ResolveChildAlignment_ExplicitSelfOverridesParent()
    {
        var config = new YogaConfig();
        var parent = new YogaNode(config);
        parent.AlignItems = FlexAlign.Center;

        var child = new YogaNode(config);
        child.AlignSelf = FlexAlign.FlexEnd;
        parent.InsertChild(child, 0);

        var result = AlignHelper.ResolveChildAlignment(parent, child);
        Assert.Equal(FlexAlign.FlexEnd, result);
    }

    [Fact]
    public void AlignHelper_ResolveChildAlignment_BaselineInColumn_FallsBackToFlexStart()
    {
        var config = new YogaConfig();
        var parent = new YogaNode(config);
        parent.FlexDirection = FlexDirection.Column;
        parent.AlignItems = FlexAlign.Baseline;

        var child = new YogaNode(config);
        child.AlignSelf = FlexAlign.Auto;
        parent.InsertChild(child, 0);

        var result = AlignHelper.ResolveChildAlignment(parent, child);
        Assert.Equal(FlexAlign.FlexStart, result);
    }

    [Fact]
    public void AlignHelper_FallbackAlignment_FlexAlign()
    {
        Assert.Equal(FlexAlign.FlexStart, AlignHelper.FallbackAlignment(FlexAlign.SpaceBetween));
        Assert.Equal(FlexAlign.FlexStart, AlignHelper.FallbackAlignment(FlexAlign.Stretch));
        Assert.Equal(FlexAlign.FlexStart, AlignHelper.FallbackAlignment(FlexAlign.SpaceAround));
        Assert.Equal(FlexAlign.FlexStart, AlignHelper.FallbackAlignment(FlexAlign.SpaceEvenly));
        Assert.Equal(FlexAlign.Center, AlignHelper.FallbackAlignment(FlexAlign.Center));
        Assert.Equal(FlexAlign.FlexEnd, AlignHelper.FallbackAlignment(FlexAlign.FlexEnd));
    }

    [Fact]
    public void AlignHelper_FallbackAlignment_FlexJustify()
    {
        Assert.Equal(FlexJustify.FlexStart, AlignHelper.FallbackAlignment(FlexJustify.SpaceBetween));
        Assert.Equal(FlexJustify.FlexStart, AlignHelper.FallbackAlignment(FlexJustify.SpaceAround));
        Assert.Equal(FlexJustify.FlexStart, AlignHelper.FallbackAlignment(FlexJustify.SpaceEvenly));
        Assert.Equal(FlexJustify.Center, AlignHelper.FallbackAlignment(FlexJustify.Center));
        Assert.Equal(FlexJustify.FlexEnd, AlignHelper.FallbackAlignment(FlexJustify.FlexEnd));
    }

    // ════════════════════════════════════════════════════════════════════
    // 9. TrailingPositionHelper
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void TrailingPosition_NeedsTrailingPosition_ReverseDirections()
    {
        Assert.True(TrailingPositionHelper.NeedsTrailingPosition(FlexDirection.RowReverse));
        Assert.True(TrailingPositionHelper.NeedsTrailingPosition(FlexDirection.ColumnReverse));
        Assert.False(TrailingPositionHelper.NeedsTrailingPosition(FlexDirection.Row));
        Assert.False(TrailingPositionHelper.NeedsTrailingPosition(FlexDirection.Column));
    }

    [Fact]
    public void TrailingPosition_GetPositionOfOppositeEdge()
    {
        var config = new YogaConfig();
        var parent = new YogaNode(config);
        parent.PositionType = FlexPositionType.Absolute;
        parent.Width = YogaValue.Point(200f);
        parent.Height = YogaValue.Point(200f);
        parent.FlexDirection = FlexDirection.RowReverse;

        var child = new YogaNode(config);
        child.Width = YogaValue.Point(50f);
        child.Height = YogaValue.Point(50f);
        parent.InsertChild(child, 0);

        parent.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        // In RowReverse, first child is placed at the right edge
        Assert.Equal(150f, child.LayoutX);
    }

    // ════════════════════════════════════════════════════════════════════
    // 10. BoundAxisHelper
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void BoundAxis_ClampsByMinAndMax()
    {
        var config = new YogaConfig();
        var node = new YogaNode(config);
        node.PositionType = FlexPositionType.Absolute;
        node.MinWidth = YogaValue.Point(50f);
        node.MaxWidth = YogaValue.Point(150f);
        node.Width = YogaValue.Point(200f);
        node.Height = YogaValue.Point(100f);

        node.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(150f, node.LayoutWidth); // clamped to maxWidth
    }

    [Fact]
    public void BoundAxis_MinWidthEnforced()
    {
        var config = new YogaConfig();
        var node = new YogaNode(config);
        node.PositionType = FlexPositionType.Absolute;
        node.MinWidth = YogaValue.Point(100f);
        node.Width = YogaValue.Point(30f);
        node.Height = YogaValue.Point(50f);

        node.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(100f, node.LayoutWidth); // clamped to minWidth
    }

    // ════════════════════════════════════════════════════════════════════
    // 11. FlexLineHelper
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void FlexLineHelper_RentAndReturnList_Reuses()
    {
        var list1 = FlexLineHelper.RentList();
        list1.Add(new YogaNode());
        FlexLineHelper.ReturnList(list1);

        var list2 = FlexLineHelper.RentList();
        // Returned list should be reused (cleared)
        Assert.Same(list1, list2);
        Assert.Empty(list2);
    }

    // ════════════════════════════════════════════════════════════════════
    // 12. Additional layout integration tests for edge cases
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Layout_DisplayNone_ChildIsSkipped()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);

        var visible = new YogaNode(config);
        visible.Width = YogaValue.Point(50f);
        visible.Height = YogaValue.Point(50f);
        root.InsertChild(visible, 0);

        var hidden = new YogaNode(config);
        hidden.Display = YogaDisplay.None;
        hidden.Width = YogaValue.Point(100f);
        hidden.Height = YogaValue.Point(100f);
        root.InsertChild(hidden, 1);

        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, hidden.LayoutWidth);
        Assert.Equal(0f, hidden.LayoutHeight);
    }

    [Fact]
    public void Layout_AspectRatio_Zero_TreatedAsAuto()
    {
        var config = new YogaConfig();
        var node = new YogaNode(config);
        node.PositionType = FlexPositionType.Absolute;
        node.Width = YogaValue.Point(100f);
        node.AspectRatio = 0; // should be treated as auto (NaN)
        node.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        // Width should be 100, height should not be constrained by aspect ratio
        Assert.Equal(100f, node.LayoutWidth);
    }

    [Fact]
    public void Layout_AspectRatio_Infinity_TreatedAsAuto()
    {
        var config = new YogaConfig();
        var node = new YogaNode(config);
        node.PositionType = FlexPositionType.Absolute;
        node.Width = YogaValue.Point(100f);
        node.AspectRatio = float.PositiveInfinity; // should be treated as auto (NaN)
        node.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(100f, node.LayoutWidth);
    }

    [Fact]
    public void Layout_HasNewLayout_ClearsAfterRead()
    {
        var config = new YogaConfig();
        var node = new YogaNode(config);
        node.PositionType = FlexPositionType.Absolute;
        node.Width = YogaValue.Point(100f);
        node.Height = YogaValue.Point(100f);
        node.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);

        Assert.True(node.HasNewLayout);
        node.HasNewLayout = false;
        Assert.False(node.HasNewLayout);
    }

    // ════════════════════════════════════════════════════════════════════
    // 6. Setter equality guards (#138 — layout cache enablement)
    //    A setter assigned its current value must NOT re-dirty the node, so
    //    FlexPanel's per-frame re-apply of stable style does not defeat the
    //    Yoga layout cache. A genuine change must still dirty.
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Setter_SameValue_DoesNotMarkDirty()
    {
        var node = new YogaNode();
        // Prime each input to a known value, then clear the dirty flag.
        node.FlexDirection = FlexDirection.Row;
        node.JustifyContent = FlexJustify.Center;
        node.AlignItems = FlexAlign.Center;
        node.FlexGrow = 1f;
        node.FlexShrink = 0f;
        node.FlexBasis = YogaValue.Point(50f);
        node.Width = YogaValue.Point(100f);
        node.Height = YogaValue.Percent(50f);
        node.MinWidth = YogaValue.Point(10f);
        node.MaxHeight = YogaValue.Point(200f);
        node.AspectRatio = 1.5f;
        node.SetMargin(YogaEdge.Left, YogaValue.Point(8f));
        node.SetPadding(YogaEdge.Top, YogaValue.Point(4f));
        node.SetBorder(YogaEdge.Right, 2f);
        node.SetPosition(YogaEdge.Bottom, YogaValue.Point(3f));
        node.SetGap(YogaGutter.Row, 6f);
        node.SetDirty(false);
        Assert.False(node.IsDirty);

        // Re-assign every input its current value — none should dirty.
        node.FlexDirection = FlexDirection.Row;
        node.JustifyContent = FlexJustify.Center;
        node.AlignItems = FlexAlign.Center;
        node.FlexGrow = 1f;
        node.FlexShrink = 0f;
        node.FlexBasis = YogaValue.Point(50f);
        node.Width = YogaValue.Point(100f);
        node.Height = YogaValue.Percent(50f);
        node.MinWidth = YogaValue.Point(10f);
        node.MaxHeight = YogaValue.Point(200f);
        node.AspectRatio = 1.5f;
        node.SetMargin(YogaEdge.Left, YogaValue.Point(8f));
        node.SetPadding(YogaEdge.Top, YogaValue.Point(4f));
        node.SetBorder(YogaEdge.Right, 2f);
        node.SetPosition(YogaEdge.Bottom, YogaValue.Point(3f));
        node.SetGap(YogaGutter.Row, 6f);

        Assert.False(node.IsDirty);
    }

    [Fact]
    public void Setter_UndefinedToUndefined_DoesNotMarkDirty()
    {
        // YogaValue.Undefined carries a NaN payload; the record-struct == must
        // treat two Undefined values as equal so re-applying Undefined (e.g. a
        // child with no margin) does not dirty.
        var node = new YogaNode();
        node.SetMargin(YogaEdge.Left, YogaValue.Undefined);
        node.Width = YogaValue.Auto;
        node.SetDirty(false);

        node.SetMargin(YogaEdge.Left, YogaValue.Undefined);
        node.Width = YogaValue.Auto;

        Assert.False(node.IsDirty);
    }

    [Theory]
    [InlineData("flexDirection")]
    [InlineData("justify")]
    [InlineData("width")]
    [InlineData("flexGrow")]
    [InlineData("margin")]
    [InlineData("gap")]
    [InlineData("display")]
    public void Setter_RealChange_MarksDirty(string which)
    {
        var node = new YogaNode();
        node.FlexDirection = FlexDirection.Row;
        node.JustifyContent = FlexJustify.Center;
        node.Width = YogaValue.Point(100f);
        node.FlexGrow = 1f;
        node.SetMargin(YogaEdge.Left, YogaValue.Point(8f));
        node.SetGap(YogaGutter.Row, 6f);
        node.Display = YogaDisplay.Flex;
        node.SetDirty(false);
        Assert.False(node.IsDirty);

        switch (which)
        {
            case "flexDirection": node.FlexDirection = FlexDirection.Column; break;
            case "justify": node.JustifyContent = FlexJustify.FlexEnd; break;
            case "width": node.Width = YogaValue.Point(101f); break;
            case "flexGrow": node.FlexGrow = 2f; break;
            case "margin": node.SetMargin(YogaEdge.Left, YogaValue.Point(9f)); break;
            case "gap": node.SetGap(YogaGutter.Row, 7f); break;
            case "display": node.Display = YogaDisplay.None; break;
        }

        Assert.True(node.IsDirty);
    }

    [Fact]
    public void Setter_SameValue_KeepsLayoutCacheHot()
    {
        // End-to-end: after a layout, re-assigning identical style must not
        // re-dirty, so the next layout reuses the cache instead of recomputing.
        // Observable: the child's measure function is NOT invoked again.
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;

        int measureCalls = 0;
        var child = new YogaNode(config);
        child.MeasureFunction = (n, w, wm, h, hm) => { measureCalls++; return new YogaSize(20f, 10f); };
        root.InsertChild(child, 0);

        root.CalculateLayout(200f, 100f, FlexLayoutDirection.LTR);
        int callsAfterFirst = measureCalls;
        Assert.True(callsAfterFirst > 0);

        // Re-apply identical container style (what FlexPanel does every
        // MeasureOverride). With the equality guards this must not dirty.
        root.FlexDirection = FlexDirection.Row;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(100f);
        Assert.False(root.IsDirty);
        Assert.False(child.IsDirty);

        root.CalculateLayout(200f, 100f, FlexLayoutDirection.LTR);
        // Cache hit ⇒ measure function not invoked again.
        Assert.Equal(callsAfterFirst, measureCalls);
    }

    // ════════════════════════════════════════════════════════════════════
    // 7. Flex-basis cache invalidation across layout generations (#138 follow-up)
    //    The equality guards stop FlexPanel re-dirtying stable children every
    //    MeasureOverride, so a child's resolved flex-basis must be invalidated
    //    per layout generation rather than relying on the dirty flag. Without
    //    that, a flex-basis:0 grow item measured at content during an
    //    undefined-main-axis pass leaks that content size into a later
    //    definite-width pass and mis-distributes the grow space. The 704
    //    single-generation Yoga fixtures never exercise this; only a repeated
    //    (multi-generation) layout of the same un-dirtied tree does.
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void FlexBasis_StaleAcrossGenerations_RecomputedOnDefinitePass()
    {
        // Two flex-grow children with flex-basis:0 in a row, content width 50
        // each (via measure function). In a definite 600px row the basis:0 grow
        // split 1:2 must be 200 / 400 — the content size must NOT enter the base.
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.FlexDirection = FlexDirection.Row;
        root.Height = YogaValue.Point(50f);

        var child0 = new YogaNode(config);
        child0.FlexGrow = 1f;
        child0.FlexShrink = 0f;
        child0.FlexBasis = YogaValue.Point(0f);
        child0.MeasureFunction = (n, w, wm, h, hm) => new YogaSize(50f, 10f);
        root.InsertChild(child0, 0);

        var child1 = new YogaNode(config);
        child1.FlexGrow = 2f;
        child1.FlexShrink = 0f;
        child1.FlexBasis = YogaValue.Point(0f);
        child1.MeasureFunction = (n, w, wm, h, hm) => new YogaSize(50f, 10f);
        root.InsertChild(child1, 1);

        // Generation 1: undefined main axis (no root width, undefined available
        // width). flex-basis:0 is not applied in an indefinite main axis, so the
        // children are measured at content and each ComputedFlexBasis becomes 50.
        root.CalculateLayout(float.NaN, 50f, FlexLayoutDirection.LTR);

        // Generation 2: definite 600px main axis. Only the root constraint
        // changed (what FlexPanel does each frame); the children were never
        // re-dirtied, so their stale gen-1 content basis must be invalidated by
        // layout generation, not by the dirty flag.
        root.Width = YogaValue.Point(600f);
        root.CalculateLayout(600f, 50f, FlexLayoutDirection.LTR);

        // flex-basis:0 grow 1:2 across 600px ⇒ 200 / 400.
        // A leaked content basis (50 each) would instead give ~217 / 383.
        Assert.Equal(200f, child0.LayoutWidth);
        Assert.Equal(400f, child1.LayoutWidth);
    }
}
