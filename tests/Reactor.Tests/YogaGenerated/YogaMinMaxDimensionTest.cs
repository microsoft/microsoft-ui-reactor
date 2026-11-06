using Microsoft.UI.Reactor.Layout;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.YogaGenerated;

/// <summary>
/// Ported from yoga/tests/generated/YGMinMaxDimensionTest.cpp
/// </summary>
public class YogaMinMaxDimensionTest
{
    [Fact]
    public void Max_Width()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Height = YogaValue.Point(10f);
        root_child0.MaxWidth = YogaValue.Point(50f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(10f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(50f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(10f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Max_Height()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(10f);
        root_child0.MaxHeight = YogaValue.Point(50f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(10f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(90f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(10f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Justify_Content_Min_Max()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.MaxHeight = YogaValue.Point(200f);
        root.MinHeight = YogaValue.Point(100f);
        root.Width = YogaValue.Point(100f);
        root.JustifyContent = FlexJustify.Center;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(60f);
        root_child0.Height = YogaValue.Point(60f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(20f, root_child0.LayoutY);
        Assert.Equal(60f, root_child0.LayoutWidth);
        Assert.Equal(60f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(40f, root_child0.LayoutX);
        Assert.Equal(20f, root_child0.LayoutY);
        Assert.Equal(60f, root_child0.LayoutWidth);
        Assert.Equal(60f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Align_Items_Min_Max()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.MaxWidth = YogaValue.Point(200f);
        root.MinWidth = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.AlignItems = FlexAlign.Center;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(60f);
        root_child0.Height = YogaValue.Point(60f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(20f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(60f, root_child0.LayoutWidth);
        Assert.Equal(60f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(20f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(60f, root_child0.LayoutWidth);
        Assert.Equal(60f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Justify_Content_Overflow_Min_Max()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.MinHeight = YogaValue.Point(100f);
        root.MaxHeight = YogaValue.Point(110f);
        root.JustifyContent = FlexJustify.Center;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        var root_child2 = new YogaNode(config);
        root_child2.Width = YogaValue.Point(50f);
        root_child2.Height = YogaValue.Point(50f);
        root.InsertChild(root_child2, 2);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(50f, root.LayoutWidth);
        Assert.Equal(110f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(-20f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(30f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        Assert.Equal(0f, root_child2.LayoutX);
        Assert.Equal(80f, root_child2.LayoutY);
        Assert.Equal(50f, root_child2.LayoutWidth);
        Assert.Equal(50f, root_child2.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(50f, root.LayoutWidth);
        Assert.Equal(110f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(-20f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(30f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        Assert.Equal(0f, root_child2.LayoutX);
        Assert.Equal(80f, root_child2.LayoutY);
        Assert.Equal(50f, root_child2.LayoutWidth);
        Assert.Equal(50f, root_child2.LayoutHeight);
    }

    [Fact]
    public void Flex_Grow_To_Min()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.MinHeight = YogaValue.Point(100f);
        root.MaxHeight = YogaValue.Point(500f);
        root.Width = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root_child0.FlexShrink = 1f;
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(100f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(100f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Flex_Grow_In_At_Most_Container()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        root.AlignItems = FlexAlign.FlexStart;
        var root_child0 = new YogaNode(config);
        root_child0.FlexDirection = FlexDirection.Row;
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.FlexGrow = 1f;
        root_child0_child0.FlexBasis = YogaValue.Point(0f);
        root_child0.InsertChild(root_child0_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(0f, root_child0_child0.LayoutWidth);
        Assert.Equal(0f, root_child0_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(100f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(0f, root_child0_child0.LayoutWidth);
        Assert.Equal(0f, root_child0_child0.LayoutHeight);
    }

    [Fact]
    public void Flex_Grow_Child()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.Height = YogaValue.Point(100f);
        root_child0.FlexGrow = 1f;
        root_child0.FlexBasis = YogaValue.Point(0f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(0f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(0f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Flex_Grow_Within_Constrained_Min_Max_Column()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.MinHeight = YogaValue.Point(100f);
        root.MaxHeight = YogaValue.Point(200f);
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(0f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(0f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(0f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(0f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Flex_Grow_Within_Max_Width()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.FlexDirection = FlexDirection.Row;
        root_child0.MaxWidth = YogaValue.Point(100f);
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.Height = YogaValue.Point(20f);
        root_child0_child0.FlexGrow = 1f;
        root_child0.InsertChild(root_child0_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(20f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(100f, root_child0_child0.LayoutWidth);
        Assert.Equal(20f, root_child0_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(100f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(20f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(100f, root_child0_child0.LayoutWidth);
        Assert.Equal(20f, root_child0_child0.LayoutHeight);
    }

    [Fact]
    public void Flex_Grow_Within_Constrained_Max_Width()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.FlexDirection = FlexDirection.Row;
        root_child0.MaxWidth = YogaValue.Point(300f);
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.Height = YogaValue.Point(20f);
        root_child0_child0.FlexGrow = 1f;
        root_child0.InsertChild(root_child0_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(200f, root_child0.LayoutWidth);
        Assert.Equal(20f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(200f, root_child0_child0.LayoutWidth);
        Assert.Equal(20f, root_child0_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(200f, root_child0.LayoutWidth);
        Assert.Equal(20f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(200f, root_child0_child0.LayoutWidth);
        Assert.Equal(20f, root_child0_child0.LayoutHeight);
    }

    [Fact]
    public void Flex_Root_Ignored()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.MinHeight = YogaValue.Point(100f);
        root.MaxHeight = YogaValue.Point(500f);
        root.FlexGrow = 1f;
        var root_child0 = new YogaNode(config);
        root_child0.FlexBasis = YogaValue.Point(200f);
        root_child0.FlexGrow = 1f;
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Height = YogaValue.Point(100f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(300f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(200f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(200f, root_child1.LayoutY);
        Assert.Equal(100f, root_child1.LayoutWidth);
        Assert.Equal(100f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(300f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(200f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(200f, root_child1.LayoutY);
        Assert.Equal(100f, root_child1.LayoutWidth);
        Assert.Equal(100f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Flex_Grow_Root_Minimized()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.MinHeight = YogaValue.Point(100f);
        root.MaxHeight = YogaValue.Point(500f);
        var root_child0 = new YogaNode(config);
        root_child0.MinHeight = YogaValue.Point(100f);
        root_child0.MaxHeight = YogaValue.Point(500f);
        root_child0.FlexGrow = 1f;
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.FlexBasis = YogaValue.Point(200f);
        root_child0_child0.FlexGrow = 1f;
        root_child0.InsertChild(root_child0_child0, 0);
        var root_child0_child1 = new YogaNode(config);
        root_child0_child1.Height = YogaValue.Point(100f);
        root_child0.InsertChild(root_child0_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(300f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(300f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(100f, root_child0_child0.LayoutWidth);
        Assert.Equal(200f, root_child0_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child1.LayoutX);
        Assert.Equal(200f, root_child0_child1.LayoutY);
        Assert.Equal(100f, root_child0_child1.LayoutWidth);
        Assert.Equal(100f, root_child0_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(300f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(300f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(100f, root_child0_child0.LayoutWidth);
        Assert.Equal(200f, root_child0_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child1.LayoutX);
        Assert.Equal(200f, root_child0_child1.LayoutY);
        Assert.Equal(100f, root_child0_child1.LayoutWidth);
        Assert.Equal(100f, root_child0_child1.LayoutHeight);
    }

    [Fact]
    public void Flex_Grow_Height_Maximized()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(500f);
        var root_child0 = new YogaNode(config);
        root_child0.MinHeight = YogaValue.Point(100f);
        root_child0.MaxHeight = YogaValue.Point(500f);
        root_child0.FlexGrow = 1f;
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.FlexBasis = YogaValue.Point(200f);
        root_child0_child0.FlexGrow = 1f;
        root_child0.InsertChild(root_child0_child0, 0);
        var root_child0_child1 = new YogaNode(config);
        root_child0_child1.Height = YogaValue.Point(100f);
        root_child0.InsertChild(root_child0_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(500f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(500f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(100f, root_child0_child0.LayoutWidth);
        Assert.Equal(400f, root_child0_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child1.LayoutX);
        Assert.Equal(400f, root_child0_child1.LayoutY);
        Assert.Equal(100f, root_child0_child1.LayoutWidth);
        Assert.Equal(100f, root_child0_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(500f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(500f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(100f, root_child0_child0.LayoutWidth);
        Assert.Equal(400f, root_child0_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child1.LayoutX);
        Assert.Equal(400f, root_child0_child1.LayoutY);
        Assert.Equal(100f, root_child0_child1.LayoutWidth);
        Assert.Equal(100f, root_child0_child1.LayoutHeight);
    }

    [Fact]
    public void Flex_Grow_Within_Constrained_Min_Row()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.MinWidth = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(50f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(100f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(50f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(100f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Flex_Grow_Within_Constrained_Min_Column()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.MinHeight = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(0f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(0f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(0f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(0f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Flex_Grow_Within_Constrained_Max_Row()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        var root_child0 = new YogaNode(config);
        root_child0.Height = YogaValue.Point(100f);
        root_child0.MaxWidth = YogaValue.Point(100f);
        root_child0.FlexDirection = FlexDirection.Row;
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.FlexShrink = 1f;
        root_child0_child0.FlexBasis = YogaValue.Point(100f);
        root_child0.InsertChild(root_child0_child0, 0);
        var root_child0_child1 = new YogaNode(config);
        root_child0_child1.Width = YogaValue.Point(50f);
        root_child0.InsertChild(root_child0_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(50f, root_child0_child0.LayoutWidth);
        Assert.Equal(100f, root_child0_child0.LayoutHeight);
        Assert.Equal(50f, root_child0_child1.LayoutX);
        Assert.Equal(0f, root_child0_child1.LayoutY);
        Assert.Equal(50f, root_child0_child1.LayoutWidth);
        Assert.Equal(100f, root_child0_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(100f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(50f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(50f, root_child0_child0.LayoutWidth);
        Assert.Equal(100f, root_child0_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child1.LayoutX);
        Assert.Equal(0f, root_child0_child1.LayoutY);
        Assert.Equal(50f, root_child0_child1.LayoutWidth);
        Assert.Equal(100f, root_child0_child1.LayoutHeight);
    }

    [Fact]
    public void Flex_Grow_Within_Constrained_Max_Column()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.MaxHeight = YogaValue.Point(100f);
        root.Width = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.FlexShrink = 1f;
        root_child0.FlexBasis = YogaValue.Point(100f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(100f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(100f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Child_Min_Max_Width_Flexing()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(120f);
        root.Height = YogaValue.Point(50f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.MinWidth = YogaValue.Point(60f);
        root_child0.FlexGrow = 1f;
        root_child0.FlexBasis = YogaValue.Point(0f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.MaxWidth = YogaValue.Point(20f);
        root_child1.FlexGrow = 1f;
        root_child1.FlexBasis = YogaValue.Percent(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(120f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(100f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(20f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(120f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);
        Assert.Equal(20f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(20f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Min_Width_Overrides_Width()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.MinWidth = YogaValue.Point(100f);
        root.Width = YogaValue.Point(50f);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(0f, root.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(0f, root.LayoutHeight);
    }

    [Fact]
    public void Max_Width_Overrides_Width()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.MaxWidth = YogaValue.Point(100f);
        root.Width = YogaValue.Point(200f);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(0f, root.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(0f, root.LayoutHeight);
    }

    [Fact]
    public void Min_Height_Overrides_Height()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.MinHeight = YogaValue.Point(100f);
        root.Height = YogaValue.Point(50f);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(0f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(0f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
    }

    [Fact]
    public void Max_Height_Overrides_Height()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.MaxHeight = YogaValue.Point(100f);
        root.Height = YogaValue.Point(200f);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(0f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(0f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
    }

    [Fact]
    public void Min_Max_Percent_No_Width_Height()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.AlignItems = FlexAlign.FlexStart;
        var root_child0 = new YogaNode(config);
        root_child0.MinWidth = YogaValue.Percent(10f);
        root_child0.MaxWidth = YogaValue.Percent(10f);
        root_child0.MinHeight = YogaValue.Percent(10f);
        root_child0.MaxHeight = YogaValue.Percent(10f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(10f, root_child0.LayoutWidth);
        Assert.Equal(10f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(90f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(10f, root_child0.LayoutWidth);
        Assert.Equal(10f, root_child0.LayoutHeight);
    }

}
