using Microsoft.UI.Reactor.Layout;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.YogaGenerated;

/// <summary>
/// Ported from yoga/tests/generated/YGPercentageTest.cpp
/// </summary>
public class YogaPercentageTest
{
    [Fact]
    public void Percentage_Width_Height()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Percent(30f);
        root_child0.Height = YogaValue.Percent(30f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(60f, root_child0.LayoutWidth);
        Assert.Equal(60f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(140f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(60f, root_child0.LayoutWidth);
        Assert.Equal(60f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Percentage_Position_Left_Top()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(400f);
        root.Height = YogaValue.Point(400f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Percent(45f);
        root_child0.Height = YogaValue.Percent(55f);
        root_child0.SetPosition(YogaEdge.Left, YogaValue.Percent(10f));
        root_child0.SetPosition(YogaEdge.Top, YogaValue.Percent(20f));
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(400f, root.LayoutWidth);
        Assert.Equal(400f, root.LayoutHeight);
        Assert.Equal(40f, root_child0.LayoutX);
        Assert.Equal(80f, root_child0.LayoutY);
        Assert.Equal(180f, root_child0.LayoutWidth);
        Assert.Equal(220f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(400f, root.LayoutWidth);
        Assert.Equal(400f, root.LayoutHeight);
        Assert.Equal(260f, root_child0.LayoutX);
        Assert.Equal(80f, root_child0.LayoutY);
        Assert.Equal(180f, root_child0.LayoutWidth);
        Assert.Equal(220f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Percentage_Position_Bottom_Right()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(500f);
        root.Height = YogaValue.Point(500f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Percent(55f);
        root_child0.Height = YogaValue.Percent(15f);
        root_child0.SetPosition(YogaEdge.Bottom, YogaValue.Percent(10f));
        root_child0.SetPosition(YogaEdge.Right, YogaValue.Percent(20f));
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(500f, root.LayoutWidth);
        Assert.Equal(500f, root.LayoutHeight);
        Assert.Equal(-100f, root_child0.LayoutX);
        Assert.Equal(-50f, root_child0.LayoutY);
        Assert.Equal(275f, root_child0.LayoutWidth);
        Assert.Equal(75f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(500f, root.LayoutWidth);
        Assert.Equal(500f, root.LayoutHeight);
        Assert.Equal(125f, root_child0.LayoutX);
        Assert.Equal(-50f, root_child0.LayoutY);
        Assert.Equal(275f, root_child0.LayoutWidth);
        Assert.Equal(75f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Percentage_Flex_Basis()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root_child0.FlexBasis = YogaValue.Percent(50f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.FlexGrow = 1f;
        root_child1.FlexBasis = YogaValue.Percent(25f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(125f, root_child0.LayoutWidth);
        Assert.Equal(200f, root_child0.LayoutHeight);
        Assert.Equal(125f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(75f, root_child1.LayoutWidth);
        Assert.Equal(200f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(75f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(125f, root_child0.LayoutWidth);
        Assert.Equal(200f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(75f, root_child1.LayoutWidth);
        Assert.Equal(200f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Percentage_Flex_Basis_Cross()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root_child0.FlexBasis = YogaValue.Percent(50f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.FlexGrow = 1f;
        root_child1.FlexBasis = YogaValue.Percent(25f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(200f, root_child0.LayoutWidth);
        Assert.Equal(125f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(125f, root_child1.LayoutY);
        Assert.Equal(200f, root_child1.LayoutWidth);
        Assert.Equal(75f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(200f, root_child0.LayoutWidth);
        Assert.Equal(125f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(125f, root_child1.LayoutY);
        Assert.Equal(200f, root_child1.LayoutWidth);
        Assert.Equal(75f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Percentage_Flex_Basis_Main_Max_Height()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root_child0.FlexBasis = YogaValue.Percent(10f);
        root_child0.MaxHeight = YogaValue.Percent(60f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.FlexGrow = 4f;
        root_child1.FlexBasis = YogaValue.Percent(10f);
        root_child1.MaxHeight = YogaValue.Percent(20f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(52f, root_child0.LayoutWidth);
        Assert.Equal(120f, root_child0.LayoutHeight);
        Assert.Equal(52f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(148f, root_child1.LayoutWidth);
        Assert.Equal(40f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(148f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(52f, root_child0.LayoutWidth);
        Assert.Equal(120f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(148f, root_child1.LayoutWidth);
        Assert.Equal(40f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Percentage_Flex_Basis_Cross_Max_Height()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root_child0.FlexBasis = YogaValue.Percent(10f);
        root_child0.MaxHeight = YogaValue.Percent(60f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.FlexGrow = 4f;
        root_child1.FlexBasis = YogaValue.Percent(10f);
        root_child1.MaxHeight = YogaValue.Percent(20f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(200f, root_child0.LayoutWidth);
        Assert.Equal(120f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(120f, root_child1.LayoutY);
        Assert.Equal(200f, root_child1.LayoutWidth);
        Assert.Equal(40f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(200f, root_child0.LayoutWidth);
        Assert.Equal(120f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(120f, root_child1.LayoutY);
        Assert.Equal(200f, root_child1.LayoutWidth);
        Assert.Equal(40f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Percentage_Flex_Basis_Main_Max_Width()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root_child0.FlexBasis = YogaValue.Percent(15f);
        root_child0.MaxWidth = YogaValue.Percent(60f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.FlexGrow = 4f;
        root_child1.FlexBasis = YogaValue.Percent(10f);
        root_child1.MaxWidth = YogaValue.Percent(20f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(120f, root_child0.LayoutWidth);
        Assert.Equal(200f, root_child0.LayoutHeight);
        Assert.Equal(120f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(40f, root_child1.LayoutWidth);
        Assert.Equal(200f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(80f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(120f, root_child0.LayoutWidth);
        Assert.Equal(200f, root_child0.LayoutHeight);
        Assert.Equal(40f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(40f, root_child1.LayoutWidth);
        Assert.Equal(200f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Percentage_Flex_Basis_Cross_Max_Width()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root_child0.FlexBasis = YogaValue.Percent(10f);
        root_child0.MaxWidth = YogaValue.Percent(60f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.FlexGrow = 4f;
        root_child1.FlexBasis = YogaValue.Percent(15f);
        root_child1.MaxWidth = YogaValue.Percent(20f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(120f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(40f, root_child1.LayoutWidth);
        Assert.Equal(150f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(80f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(120f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(160f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(40f, root_child1.LayoutWidth);
        Assert.Equal(150f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Percentage_Flex_Basis_Main_Min_Width()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root_child0.FlexBasis = YogaValue.Percent(15f);
        root_child0.MinWidth = YogaValue.Percent(60f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.FlexGrow = 4f;
        root_child1.FlexBasis = YogaValue.Percent(10f);
        root_child1.MinWidth = YogaValue.Percent(20f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(120f, root_child0.LayoutWidth);
        Assert.Equal(200f, root_child0.LayoutHeight);
        Assert.Equal(120f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(80f, root_child1.LayoutWidth);
        Assert.Equal(200f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(80f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(120f, root_child0.LayoutWidth);
        Assert.Equal(200f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(80f, root_child1.LayoutWidth);
        Assert.Equal(200f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Percentage_Flex_Basis_Cross_Min_Width()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root_child0.FlexBasis = YogaValue.Percent(10f);
        root_child0.MinWidth = YogaValue.Percent(60f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.FlexGrow = 4f;
        root_child1.FlexBasis = YogaValue.Percent(15f);
        root_child1.MinWidth = YogaValue.Percent(20f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(200f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(200f, root_child1.LayoutWidth);
        Assert.Equal(150f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(200f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(200f, root_child1.LayoutWidth);
        Assert.Equal(150f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Percentage_Multiple_Nested_With_Padding_Margin_And_Percentage_Values()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root_child0.FlexBasis = YogaValue.Percent(10f);
        root_child0.MinWidth = YogaValue.Percent(60f);
        root_child0.SetMargin(YogaEdge.All, YogaValue.Point(5f));
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(3f));
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.Width = YogaValue.Percent(50f);
        root_child0_child0.SetMargin(YogaEdge.All, YogaValue.Point(5f));
        root_child0_child0.SetPadding(YogaEdge.All, YogaValue.Percent(3f));
        root_child0.InsertChild(root_child0_child0, 0);
        var root_child0_child0_child0 = new YogaNode(config);
        root_child0_child0_child0.Width = YogaValue.Percent(45f);
        root_child0_child0_child0.SetMargin(YogaEdge.All, YogaValue.Percent(5f));
        root_child0_child0_child0.SetPadding(YogaEdge.All, YogaValue.Point(3f));
        root_child0_child0.InsertChild(root_child0_child0_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.FlexGrow = 4f;
        root_child1.FlexBasis = YogaValue.Percent(15f);
        root_child1.MinWidth = YogaValue.Percent(20f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(5f, root_child0.LayoutX);
        Assert.Equal(5f, root_child0.LayoutY);
        Assert.Equal(190f, root_child0.LayoutWidth);
        Assert.Equal(48f, root_child0.LayoutHeight);
        Assert.Equal(8f, root_child0_child0.LayoutX);
        Assert.Equal(8f, root_child0_child0.LayoutY);
        Assert.Equal(92f, root_child0_child0.LayoutWidth);
        Assert.Equal(25f, root_child0_child0.LayoutHeight);
        Assert.Equal(10f, root_child0_child0_child0.LayoutX);
        Assert.Equal(10f, root_child0_child0_child0.LayoutY);
        Assert.Equal(36f, root_child0_child0_child0.LayoutWidth);
        Assert.Equal(6f, root_child0_child0_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(58f, root_child1.LayoutY);
        Assert.Equal(200f, root_child1.LayoutWidth);
        Assert.Equal(142f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(5f, root_child0.LayoutX);
        Assert.Equal(5f, root_child0.LayoutY);
        Assert.Equal(190f, root_child0.LayoutWidth);
        Assert.Equal(48f, root_child0.LayoutHeight);
        Assert.Equal(90f, root_child0_child0.LayoutX);
        Assert.Equal(8f, root_child0_child0.LayoutY);
        Assert.Equal(92f, root_child0_child0.LayoutWidth);
        Assert.Equal(25f, root_child0_child0.LayoutHeight);
        Assert.Equal(46f, root_child0_child0_child0.LayoutX);
        Assert.Equal(10f, root_child0_child0_child0.LayoutY);
        Assert.Equal(36f, root_child0_child0_child0.LayoutWidth);
        Assert.Equal(6f, root_child0_child0_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(58f, root_child1.LayoutY);
        Assert.Equal(200f, root_child1.LayoutWidth);
        Assert.Equal(142f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Percentage_Margin_Should_Calculate_Based_Only_On_Width()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root_child0.SetMargin(YogaEdge.All, YogaValue.Percent(10f));
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.Width = YogaValue.Point(10f);
        root_child0_child0.Height = YogaValue.Point(10f);
        root_child0.InsertChild(root_child0_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(20f, root_child0.LayoutX);
        Assert.Equal(20f, root_child0.LayoutY);
        Assert.Equal(160f, root_child0.LayoutWidth);
        Assert.Equal(60f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(10f, root_child0_child0.LayoutWidth);
        Assert.Equal(10f, root_child0_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(20f, root_child0.LayoutX);
        Assert.Equal(20f, root_child0.LayoutY);
        Assert.Equal(160f, root_child0.LayoutWidth);
        Assert.Equal(60f, root_child0.LayoutHeight);
        Assert.Equal(150f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(10f, root_child0_child0.LayoutWidth);
        Assert.Equal(10f, root_child0_child0.LayoutHeight);
    }

    [Fact]
    public void Percentage_Padding_Should_Calculate_Based_Only_On_Width()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root_child0.SetPadding(YogaEdge.All, YogaValue.Percent(10f));
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.Width = YogaValue.Point(10f);
        root_child0_child0.Height = YogaValue.Point(10f);
        root_child0.InsertChild(root_child0_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(200f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(20f, root_child0_child0.LayoutX);
        Assert.Equal(20f, root_child0_child0.LayoutY);
        Assert.Equal(10f, root_child0_child0.LayoutWidth);
        Assert.Equal(10f, root_child0_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(200f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(170f, root_child0_child0.LayoutX);
        Assert.Equal(20f, root_child0_child0.LayoutY);
        Assert.Equal(10f, root_child0_child0.LayoutWidth);
        Assert.Equal(10f, root_child0_child0.LayoutHeight);
    }

    [Fact]
    public void Percentage_Absolute_Position()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.PositionType = FlexPositionType.Absolute;
        root_child0.SetPosition(YogaEdge.Top, YogaValue.Percent(10f));
        root_child0.SetPosition(YogaEdge.Left, YogaValue.Percent(30f));
        root_child0.Width = YogaValue.Point(10f);
        root_child0.Height = YogaValue.Point(10f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(60f, root_child0.LayoutX);
        Assert.Equal(10f, root_child0.LayoutY);
        Assert.Equal(10f, root_child0.LayoutWidth);
        Assert.Equal(10f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(60f, root_child0.LayoutX);
        Assert.Equal(10f, root_child0.LayoutY);
        Assert.Equal(10f, root_child0.LayoutWidth);
        Assert.Equal(10f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Percentage_Width_Height_Undefined_Parent_Size()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Percent(50f);
        root_child0.Height = YogaValue.Percent(50f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(0f, root.LayoutWidth);
        Assert.Equal(0f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(0f, root.LayoutWidth);
        Assert.Equal(0f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Percent_Within_Flex_Grow()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.FlexDirection = FlexDirection.Row;
        root.Width = YogaValue.Point(350f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(100f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.FlexGrow = 1f;
        root.InsertChild(root_child1, 1);
        var root_child1_child0 = new YogaNode(config);
        root_child1_child0.Width = YogaValue.Percent(100f);
        root_child1.InsertChild(root_child1_child0, 0);
        var root_child2 = new YogaNode(config);
        root_child2.Width = YogaValue.Point(100f);
        root.InsertChild(root_child2, 2);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(350f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(100f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(150f, root_child1.LayoutWidth);
        Assert.Equal(100f, root_child1.LayoutHeight);
        Assert.Equal(0f, root_child1_child0.LayoutX);
        Assert.Equal(0f, root_child1_child0.LayoutY);
        Assert.Equal(150f, root_child1_child0.LayoutWidth);
        Assert.Equal(0f, root_child1_child0.LayoutHeight);
        Assert.Equal(250f, root_child2.LayoutX);
        Assert.Equal(0f, root_child2.LayoutY);
        Assert.Equal(100f, root_child2.LayoutWidth);
        Assert.Equal(100f, root_child2.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(350f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(250f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(100f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(150f, root_child1.LayoutWidth);
        Assert.Equal(100f, root_child1.LayoutHeight);
        Assert.Equal(0f, root_child1_child0.LayoutX);
        Assert.Equal(0f, root_child1_child0.LayoutY);
        Assert.Equal(150f, root_child1_child0.LayoutWidth);
        Assert.Equal(0f, root_child1_child0.LayoutHeight);
        Assert.Equal(0f, root_child2.LayoutX);
        Assert.Equal(0f, root_child2.LayoutY);
        Assert.Equal(100f, root_child2.LayoutWidth);
        Assert.Equal(100f, root_child2.LayoutHeight);
    }

    [Fact]
    public void Percentage_Container_In_Wrapping_Container()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.AlignItems = FlexAlign.Center;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.JustifyContent = FlexJustify.Center;
        var root_child0 = new YogaNode(config);
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.FlexDirection = FlexDirection.Row;
        root_child0_child0.JustifyContent = FlexJustify.Center;
        root_child0_child0.Width = YogaValue.Percent(100f);
        root_child0.InsertChild(root_child0_child0, 0);
        var root_child0_child0_child0 = new YogaNode(config);
        root_child0_child0_child0.Width = YogaValue.Point(50f);
        root_child0_child0_child0.Height = YogaValue.Point(50f);
        root_child0_child0.InsertChild(root_child0_child0_child0, 0);
        var root_child0_child0_child1 = new YogaNode(config);
        root_child0_child0_child1.Width = YogaValue.Point(50f);
        root_child0_child0_child1.Height = YogaValue.Point(50f);
        root_child0_child0.InsertChild(root_child0_child0_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(50f, root_child0.LayoutX);
        Assert.Equal(75f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(100f, root_child0_child0.LayoutWidth);
        Assert.Equal(50f, root_child0_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0_child0.LayoutY);
        Assert.Equal(50f, root_child0_child0_child0.LayoutWidth);
        Assert.Equal(50f, root_child0_child0_child0.LayoutHeight);
        Assert.Equal(50f, root_child0_child0_child1.LayoutX);
        Assert.Equal(0f, root_child0_child0_child1.LayoutY);
        Assert.Equal(50f, root_child0_child0_child1.LayoutWidth);
        Assert.Equal(50f, root_child0_child0_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(50f, root_child0.LayoutX);
        Assert.Equal(75f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(100f, root_child0_child0.LayoutWidth);
        Assert.Equal(50f, root_child0_child0.LayoutHeight);
        Assert.Equal(50f, root_child0_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0_child0.LayoutY);
        Assert.Equal(50f, root_child0_child0_child0.LayoutWidth);
        Assert.Equal(50f, root_child0_child0_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0_child1.LayoutX);
        Assert.Equal(0f, root_child0_child0_child1.LayoutY);
        Assert.Equal(50f, root_child0_child0_child1.LayoutWidth);
        Assert.Equal(50f, root_child0_child0_child1.LayoutHeight);
    }

    [Fact]
    public void Percent_Absolute_Position()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(60f);
        root.Height = YogaValue.Point(50f);
        var root_child0 = new YogaNode(config);
        root_child0.Height = YogaValue.Point(50f);
        root_child0.Width = YogaValue.Percent(100f);
        root_child0.SetPosition(YogaEdge.Left, YogaValue.Percent(50f));
        root_child0.PositionType = FlexPositionType.Absolute;
        root_child0.FlexDirection = FlexDirection.Row;
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.Width = YogaValue.Percent(100f);
        root_child0.InsertChild(root_child0_child0, 0);
        var root_child0_child1 = new YogaNode(config);
        root_child0_child1.Width = YogaValue.Percent(100f);
        root_child0.InsertChild(root_child0_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(60f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);
        Assert.Equal(30f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(60f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(60f, root_child0_child0.LayoutWidth);
        Assert.Equal(50f, root_child0_child0.LayoutHeight);
        Assert.Equal(60f, root_child0_child1.LayoutX);
        Assert.Equal(0f, root_child0_child1.LayoutY);
        Assert.Equal(60f, root_child0_child1.LayoutWidth);
        Assert.Equal(50f, root_child0_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(60f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);
        Assert.Equal(30f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(60f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(60f, root_child0_child0.LayoutWidth);
        Assert.Equal(50f, root_child0_child0.LayoutHeight);
        Assert.Equal(-60f, root_child0_child1.LayoutX);
        Assert.Equal(0f, root_child0_child1.LayoutY);
        Assert.Equal(60f, root_child0_child1.LayoutWidth);
        Assert.Equal(50f, root_child0_child1.LayoutHeight);
    }

    [Fact]
    public void Percent_Of_Minmax_Main()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.FlexDirection = FlexDirection.Row;
        root.MinWidth = YogaValue.Point(60f);
        root.MaxWidth = YogaValue.Point(60f);
        root.Height = YogaValue.Point(50f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Percent(50f);
        root_child0.Height = YogaValue.Point(20f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(60f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(30f, root_child0.LayoutWidth);
        Assert.Equal(20f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(60f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);
        Assert.Equal(30f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(30f, root_child0.LayoutWidth);
        Assert.Equal(20f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Percent_Of_Minmax_Cross_Stretched()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.MinWidth = YogaValue.Point(60f);
        root.MaxWidth = YogaValue.Point(60f);
        root.Height = YogaValue.Point(50f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Percent(50f);
        root_child0.Height = YogaValue.Point(20f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(60f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(30f, root_child0.LayoutWidth);
        Assert.Equal(20f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(60f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);
        Assert.Equal(30f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(30f, root_child0.LayoutWidth);
        Assert.Equal(20f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Percent_Absolute_Of_Minmax_Cross_Stretched()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.MinWidth = YogaValue.Point(60f);
        root.MaxWidth = YogaValue.Point(60f);
        root.Height = YogaValue.Point(50f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Percent(50f);
        root_child0.Height = YogaValue.Point(20f);
        root_child0.PositionType = FlexPositionType.Absolute;
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(60f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(30f, root_child0.LayoutWidth);
        Assert.Equal(20f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(60f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);
        Assert.Equal(30f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(30f, root_child0.LayoutWidth);
        Assert.Equal(20f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Percent_Of_Minmax_Cross_Unstretched()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.MinWidth = YogaValue.Point(60f);
        root.MaxWidth = YogaValue.Point(60f);
        root.Height = YogaValue.Point(50f);
        root.AlignItems = FlexAlign.FlexStart;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Percent(50f);
        root_child0.Height = YogaValue.Point(20f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(60f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(30f, root_child0.LayoutWidth);
        Assert.Equal(20f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(60f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);
        Assert.Equal(30f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(30f, root_child0.LayoutWidth);
        Assert.Equal(20f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Percent_Of_Max_Cross_Unstretched()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.MaxWidth = YogaValue.Point(60f);
        root.Height = YogaValue.Point(50f);
        root.AlignItems = FlexAlign.FlexStart;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Percent(50f);
        root_child0.Height = YogaValue.Point(20f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(0f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(20f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(0f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(20f, root_child0.LayoutHeight);
    }

}
