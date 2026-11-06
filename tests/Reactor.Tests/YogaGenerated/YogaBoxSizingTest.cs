using Microsoft.UI.Reactor.Layout;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.YogaGenerated;

/// <summary>
/// Ported from yoga/tests/generated/YGBoxSizingTest.cpp
/// </summary>
public class YogaBoxSizingTest
{
    [Fact]
    public void Box_Sizing_Content_Box_Simple()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.SetPadding(YogaEdge.All, YogaValue.Point(5f));
        root.SetBorder(YogaEdge.All, 10f);
        root.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(130f, root.LayoutWidth);
        Assert.Equal(130f, root.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(130f, root.LayoutWidth);
        Assert.Equal(130f, root.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Simple()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.SetPadding(YogaEdge.All, YogaValue.Point(5f));
        root.SetBorder(YogaEdge.All, 10f);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_Percent()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Percent(50f);
        root_child0.Height = YogaValue.Percent(25f);
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(4f));
        root_child0.SetBorder(YogaEdge.All, 16f);
        root_child0.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(90f, root_child0.LayoutWidth);
        Assert.Equal(65f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(10f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(90f, root_child0.LayoutWidth);
        Assert.Equal(65f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Percent()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Percent(50f);
        root_child0.Height = YogaValue.Percent(25f);
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(4f));
        root_child0.SetBorder(YogaEdge.All, 16f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(40f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(50f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(40f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_Absolute()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Height = YogaValue.Percent(25f);
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(12f));
        root_child0.SetBorder(YogaEdge.All, 8f);
        root_child0.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root_child0.PositionType = FlexPositionType.Absolute;
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(40f, root_child0.LayoutWidth);
        Assert.Equal(65f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(60f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(40f, root_child0.LayoutWidth);
        Assert.Equal(65f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Absolute()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Height = YogaValue.Percent(25f);
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(12f));
        root_child0.SetBorder(YogaEdge.All, 8f);
        root_child0.PositionType = FlexPositionType.Absolute;
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(40f, root_child0.LayoutWidth);
        Assert.Equal(40f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(60f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(40f, root_child0.LayoutWidth);
        Assert.Equal(40f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_Containing_Block()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.SetPadding(YogaEdge.All, YogaValue.Point(12f));
        root.SetBorder(YogaEdge.All, 8f);
        root.Style.BoxSizing = YogaBoxSizing.ContentBox;
        var root_child0 = new YogaNode(config);
        root_child0.PositionType = FlexPositionType.Static;
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.Width = YogaValue.Point(50f);
        root_child0_child0.Height = YogaValue.Percent(25f);
        root_child0_child0.PositionType = FlexPositionType.Absolute;
        root_child0.InsertChild(root_child0_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(140f, root.LayoutWidth);
        Assert.Equal(140f, root.LayoutHeight);
        Assert.Equal(20f, root_child0.LayoutX);
        Assert.Equal(20f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(50f, root_child0_child0.LayoutWidth);
        Assert.Equal(31f, root_child0_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(140f, root.LayoutWidth);
        Assert.Equal(140f, root.LayoutHeight);
        Assert.Equal(20f, root_child0.LayoutX);
        Assert.Equal(20f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
        Assert.Equal(50f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(50f, root_child0_child0.LayoutWidth);
        Assert.Equal(31f, root_child0_child0.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Containing_Block()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.SetPadding(YogaEdge.All, YogaValue.Point(12f));
        root.SetBorder(YogaEdge.All, 8f);
        var root_child0 = new YogaNode(config);
        root_child0.PositionType = FlexPositionType.Static;
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.Width = YogaValue.Point(50f);
        root_child0_child0.Height = YogaValue.Percent(25f);
        root_child0_child0.PositionType = FlexPositionType.Absolute;
        root_child0.InsertChild(root_child0_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(20f, root_child0.LayoutX);
        Assert.Equal(20f, root_child0.LayoutY);
        Assert.Equal(60f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(50f, root_child0_child0.LayoutWidth);
        Assert.Equal(21f, root_child0_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(20f, root_child0.LayoutX);
        Assert.Equal(20f, root_child0.LayoutY);
        Assert.Equal(60f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
        Assert.Equal(10f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(50f, root_child0_child0.LayoutWidth);
        Assert.Equal(21f, root_child0_child0.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_Padding_Only()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.SetPadding(YogaEdge.All, YogaValue.Point(5f));
        root.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(110f, root.LayoutWidth);
        Assert.Equal(110f, root.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(110f, root.LayoutWidth);
        Assert.Equal(110f, root.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_Padding_Only_Percent()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(150f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(75f);
        root_child0.SetPadding(YogaEdge.All, YogaValue.Percent(10f));
        root_child0.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(150f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(70f, root_child0.LayoutWidth);
        Assert.Equal(95f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(150f, root.LayoutHeight);
        Assert.Equal(30f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(70f, root_child0.LayoutWidth);
        Assert.Equal(95f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Padding_Only()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.SetPadding(YogaEdge.All, YogaValue.Point(5f));
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Padding_Only_Percent()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(150f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(75f);
        root_child0.SetPadding(YogaEdge.All, YogaValue.Percent(10f));
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(150f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(75f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(150f, root.LayoutHeight);
        Assert.Equal(50f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(75f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_Border_Only()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.SetBorder(YogaEdge.All, 10f);
        root.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(120f, root.LayoutWidth);
        Assert.Equal(120f, root.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(120f, root.LayoutWidth);
        Assert.Equal(120f, root.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_Border_Only_Percent()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Percent(50f);
        root_child0.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(50f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Border_Only()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.SetBorder(YogaEdge.All, 10f);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Border_Only_Percent()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Percent(50f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(50f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_No_Padding_No_Border()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_No_Padding_No_Border()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_Children()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.SetPadding(YogaEdge.All, YogaValue.Point(5f));
        root.SetBorder(YogaEdge.All, 10f);
        root.Style.BoxSizing = YogaBoxSizing.ContentBox;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(25f);
        root_child0.Height = YogaValue.Point(25f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(25f);
        root_child1.Height = YogaValue.Point(25f);
        root.InsertChild(root_child1, 1);
        var root_child2 = new YogaNode(config);
        root_child2.Width = YogaValue.Point(25f);
        root_child2.Height = YogaValue.Point(25f);
        root.InsertChild(root_child2, 2);
        var root_child3 = new YogaNode(config);
        root_child3.Width = YogaValue.Point(25f);
        root_child3.Height = YogaValue.Point(25f);
        root.InsertChild(root_child3, 3);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(130f, root.LayoutWidth);
        Assert.Equal(130f, root.LayoutHeight);
        Assert.Equal(15f, root_child0.LayoutX);
        Assert.Equal(15f, root_child0.LayoutY);
        Assert.Equal(25f, root_child0.LayoutWidth);
        Assert.Equal(25f, root_child0.LayoutHeight);
        Assert.Equal(15f, root_child1.LayoutX);
        Assert.Equal(40f, root_child1.LayoutY);
        Assert.Equal(25f, root_child1.LayoutWidth);
        Assert.Equal(25f, root_child1.LayoutHeight);
        Assert.Equal(15f, root_child2.LayoutX);
        Assert.Equal(65f, root_child2.LayoutY);
        Assert.Equal(25f, root_child2.LayoutWidth);
        Assert.Equal(25f, root_child2.LayoutHeight);
        Assert.Equal(15f, root_child3.LayoutX);
        Assert.Equal(90f, root_child3.LayoutY);
        Assert.Equal(25f, root_child3.LayoutWidth);
        Assert.Equal(25f, root_child3.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(130f, root.LayoutWidth);
        Assert.Equal(130f, root.LayoutHeight);
        Assert.Equal(90f, root_child0.LayoutX);
        Assert.Equal(15f, root_child0.LayoutY);
        Assert.Equal(25f, root_child0.LayoutWidth);
        Assert.Equal(25f, root_child0.LayoutHeight);
        Assert.Equal(90f, root_child1.LayoutX);
        Assert.Equal(40f, root_child1.LayoutY);
        Assert.Equal(25f, root_child1.LayoutWidth);
        Assert.Equal(25f, root_child1.LayoutHeight);
        Assert.Equal(90f, root_child2.LayoutX);
        Assert.Equal(65f, root_child2.LayoutY);
        Assert.Equal(25f, root_child2.LayoutWidth);
        Assert.Equal(25f, root_child2.LayoutHeight);
        Assert.Equal(90f, root_child3.LayoutX);
        Assert.Equal(90f, root_child3.LayoutY);
        Assert.Equal(25f, root_child3.LayoutWidth);
        Assert.Equal(25f, root_child3.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Children()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.SetPadding(YogaEdge.All, YogaValue.Point(5f));
        root.SetBorder(YogaEdge.All, 10f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(25f);
        root_child0.Height = YogaValue.Point(25f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(25f);
        root_child1.Height = YogaValue.Point(25f);
        root.InsertChild(root_child1, 1);
        var root_child2 = new YogaNode(config);
        root_child2.Width = YogaValue.Point(25f);
        root_child2.Height = YogaValue.Point(25f);
        root.InsertChild(root_child2, 2);
        var root_child3 = new YogaNode(config);
        root_child3.Width = YogaValue.Point(25f);
        root_child3.Height = YogaValue.Point(25f);
        root.InsertChild(root_child3, 3);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(15f, root_child0.LayoutX);
        Assert.Equal(15f, root_child0.LayoutY);
        Assert.Equal(25f, root_child0.LayoutWidth);
        Assert.Equal(25f, root_child0.LayoutHeight);
        Assert.Equal(15f, root_child1.LayoutX);
        Assert.Equal(40f, root_child1.LayoutY);
        Assert.Equal(25f, root_child1.LayoutWidth);
        Assert.Equal(25f, root_child1.LayoutHeight);
        Assert.Equal(15f, root_child2.LayoutX);
        Assert.Equal(65f, root_child2.LayoutY);
        Assert.Equal(25f, root_child2.LayoutWidth);
        Assert.Equal(25f, root_child2.LayoutHeight);
        Assert.Equal(15f, root_child3.LayoutX);
        Assert.Equal(90f, root_child3.LayoutY);
        Assert.Equal(25f, root_child3.LayoutWidth);
        Assert.Equal(25f, root_child3.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(60f, root_child0.LayoutX);
        Assert.Equal(15f, root_child0.LayoutY);
        Assert.Equal(25f, root_child0.LayoutWidth);
        Assert.Equal(25f, root_child0.LayoutHeight);
        Assert.Equal(60f, root_child1.LayoutX);
        Assert.Equal(40f, root_child1.LayoutY);
        Assert.Equal(25f, root_child1.LayoutWidth);
        Assert.Equal(25f, root_child1.LayoutHeight);
        Assert.Equal(60f, root_child2.LayoutX);
        Assert.Equal(65f, root_child2.LayoutY);
        Assert.Equal(25f, root_child2.LayoutWidth);
        Assert.Equal(25f, root_child2.LayoutHeight);
        Assert.Equal(60f, root_child3.LayoutX);
        Assert.Equal(90f, root_child3.LayoutY);
        Assert.Equal(25f, root_child3.LayoutWidth);
        Assert.Equal(25f, root_child3.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_Siblings()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(25f);
        root_child0.Height = YogaValue.Point(25f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(25f);
        root_child1.Height = YogaValue.Point(25f);
        root_child1.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root_child1.SetPadding(YogaEdge.All, YogaValue.Point(10f));
        root_child1.SetBorder(YogaEdge.All, 10f);
        root.InsertChild(root_child1, 1);
        var root_child2 = new YogaNode(config);
        root_child2.Width = YogaValue.Point(25f);
        root_child2.Height = YogaValue.Point(25f);
        root.InsertChild(root_child2, 2);
        var root_child3 = new YogaNode(config);
        root_child3.Width = YogaValue.Point(25f);
        root_child3.Height = YogaValue.Point(25f);
        root.InsertChild(root_child3, 3);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(25f, root_child0.LayoutWidth);
        Assert.Equal(25f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(25f, root_child1.LayoutY);
        Assert.Equal(65f, root_child1.LayoutWidth);
        Assert.Equal(65f, root_child1.LayoutHeight);
        Assert.Equal(0f, root_child2.LayoutX);
        Assert.Equal(90f, root_child2.LayoutY);
        Assert.Equal(25f, root_child2.LayoutWidth);
        Assert.Equal(25f, root_child2.LayoutHeight);
        Assert.Equal(0f, root_child3.LayoutX);
        Assert.Equal(115f, root_child3.LayoutY);
        Assert.Equal(25f, root_child3.LayoutWidth);
        Assert.Equal(25f, root_child3.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(75f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(25f, root_child0.LayoutWidth);
        Assert.Equal(25f, root_child0.LayoutHeight);
        Assert.Equal(35f, root_child1.LayoutX);
        Assert.Equal(25f, root_child1.LayoutY);
        Assert.Equal(65f, root_child1.LayoutWidth);
        Assert.Equal(65f, root_child1.LayoutHeight);
        Assert.Equal(75f, root_child2.LayoutX);
        Assert.Equal(90f, root_child2.LayoutY);
        Assert.Equal(25f, root_child2.LayoutWidth);
        Assert.Equal(25f, root_child2.LayoutHeight);
        Assert.Equal(75f, root_child3.LayoutX);
        Assert.Equal(115f, root_child3.LayoutY);
        Assert.Equal(25f, root_child3.LayoutWidth);
        Assert.Equal(25f, root_child3.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Siblings()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(25f);
        root_child0.Height = YogaValue.Point(25f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(25f);
        root_child1.Height = YogaValue.Point(25f);
        root_child1.SetPadding(YogaEdge.All, YogaValue.Point(10f));
        root_child1.SetBorder(YogaEdge.All, 10f);
        root.InsertChild(root_child1, 1);
        var root_child2 = new YogaNode(config);
        root_child2.Width = YogaValue.Point(25f);
        root_child2.Height = YogaValue.Point(25f);
        root.InsertChild(root_child2, 2);
        var root_child3 = new YogaNode(config);
        root_child3.Width = YogaValue.Point(25f);
        root_child3.Height = YogaValue.Point(25f);
        root.InsertChild(root_child3, 3);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(25f, root_child0.LayoutWidth);
        Assert.Equal(25f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(25f, root_child1.LayoutY);
        Assert.Equal(40f, root_child1.LayoutWidth);
        Assert.Equal(40f, root_child1.LayoutHeight);
        Assert.Equal(0f, root_child2.LayoutX);
        Assert.Equal(65f, root_child2.LayoutY);
        Assert.Equal(25f, root_child2.LayoutWidth);
        Assert.Equal(25f, root_child2.LayoutHeight);
        Assert.Equal(0f, root_child3.LayoutX);
        Assert.Equal(90f, root_child3.LayoutY);
        Assert.Equal(25f, root_child3.LayoutWidth);
        Assert.Equal(25f, root_child3.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(75f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(25f, root_child0.LayoutWidth);
        Assert.Equal(25f, root_child0.LayoutHeight);
        Assert.Equal(60f, root_child1.LayoutX);
        Assert.Equal(25f, root_child1.LayoutY);
        Assert.Equal(40f, root_child1.LayoutWidth);
        Assert.Equal(40f, root_child1.LayoutHeight);
        Assert.Equal(75f, root_child2.LayoutX);
        Assert.Equal(65f, root_child2.LayoutY);
        Assert.Equal(25f, root_child2.LayoutWidth);
        Assert.Equal(25f, root_child2.LayoutHeight);
        Assert.Equal(75f, root_child3.LayoutX);
        Assert.Equal(90f, root_child3.LayoutY);
        Assert.Equal(25f, root_child3.LayoutWidth);
        Assert.Equal(25f, root_child3.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_Max_Width()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.MaxWidth = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(25f);
        root_child0.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(5f));
        root_child0.SetBorder(YogaEdge.All, 15f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(25f);
        root_child1.Height = YogaValue.Point(25f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(90f, root_child0.LayoutWidth);
        Assert.Equal(65f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(65f, root_child1.LayoutY);
        Assert.Equal(25f, root_child1.LayoutWidth);
        Assert.Equal(25f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(10f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(90f, root_child0.LayoutWidth);
        Assert.Equal(65f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(65f, root_child1.LayoutY);
        Assert.Equal(25f, root_child1.LayoutWidth);
        Assert.Equal(25f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Max_Width()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.MaxWidth = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(25f);
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(5f));
        root_child0.SetBorder(YogaEdge.All, 15f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(25f);
        root_child1.Height = YogaValue.Point(25f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(40f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(40f, root_child1.LayoutY);
        Assert.Equal(25f, root_child1.LayoutWidth);
        Assert.Equal(25f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(50f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(40f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(40f, root_child1.LayoutY);
        Assert.Equal(25f, root_child1.LayoutWidth);
        Assert.Equal(25f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_Max_Height()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.MaxHeight = YogaValue.Point(50f);
        root_child0.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(5f));
        root_child0.SetBorder(YogaEdge.All, 15f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(25f);
        root_child1.Height = YogaValue.Point(25f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(90f, root_child0.LayoutWidth);
        Assert.Equal(40f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(40f, root_child1.LayoutY);
        Assert.Equal(25f, root_child1.LayoutWidth);
        Assert.Equal(25f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(10f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(90f, root_child0.LayoutWidth);
        Assert.Equal(40f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(40f, root_child1.LayoutY);
        Assert.Equal(25f, root_child1.LayoutWidth);
        Assert.Equal(25f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Max_Height()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.MaxHeight = YogaValue.Point(50f);
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(5f));
        root_child0.SetBorder(YogaEdge.All, 15f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(25f);
        root_child1.Height = YogaValue.Point(25f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(40f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(40f, root_child1.LayoutY);
        Assert.Equal(25f, root_child1.LayoutWidth);
        Assert.Equal(25f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(50f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(40f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(40f, root_child1.LayoutY);
        Assert.Equal(25f, root_child1.LayoutWidth);
        Assert.Equal(25f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_Min_Width()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.MinWidth = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(25f);
        root_child0.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(5f));
        root_child0.SetBorder(YogaEdge.All, 15f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(25f);
        root_child1.Height = YogaValue.Point(25f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(65f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(65f, root_child1.LayoutY);
        Assert.Equal(25f, root_child1.LayoutWidth);
        Assert.Equal(25f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(65f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(65f, root_child1.LayoutY);
        Assert.Equal(25f, root_child1.LayoutWidth);
        Assert.Equal(25f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Min_Width()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.MinWidth = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(25f);
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(5f));
        root_child0.SetBorder(YogaEdge.All, 15f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(25f);
        root_child1.Height = YogaValue.Point(25f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(40f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(40f, root_child1.LayoutY);
        Assert.Equal(25f, root_child1.LayoutWidth);
        Assert.Equal(25f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(40f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(40f, root_child1.LayoutY);
        Assert.Equal(25f, root_child1.LayoutWidth);
        Assert.Equal(25f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_Min_Height()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.MinHeight = YogaValue.Point(50f);
        root_child0.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(5f));
        root_child0.SetBorder(YogaEdge.All, 15f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(25f);
        root_child1.Height = YogaValue.Point(25f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(90f, root_child0.LayoutWidth);
        Assert.Equal(90f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(90f, root_child1.LayoutY);
        Assert.Equal(25f, root_child1.LayoutWidth);
        Assert.Equal(25f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(10f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(90f, root_child0.LayoutWidth);
        Assert.Equal(90f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(90f, root_child1.LayoutY);
        Assert.Equal(25f, root_child1.LayoutWidth);
        Assert.Equal(25f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Min_Height()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.MinHeight = YogaValue.Point(50f);
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(5f));
        root_child0.SetBorder(YogaEdge.All, 15f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(25f);
        root_child1.Height = YogaValue.Point(25f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(25f, root_child1.LayoutWidth);
        Assert.Equal(25f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(50f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(25f, root_child1.LayoutWidth);
        Assert.Equal(25f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_No_Height_No_Width()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(2f));
        root_child0.SetBorder(YogaEdge.All, 7f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(18f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(18f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_No_Height_No_Width()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(2f));
        root_child0.SetBorder(YogaEdge.All, 7f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(18f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(18f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_Nested()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root.SetPadding(YogaEdge.All, YogaValue.Point(15f));
        root.SetBorder(YogaEdge.All, 3f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(20f);
        root_child0.Height = YogaValue.Point(20f);
        root_child0.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(2f));
        root_child0.SetBorder(YogaEdge.All, 7f);
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.Width = YogaValue.Point(10f);
        root_child0_child0.Height = YogaValue.Point(5f);
        root_child0_child0.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root_child0_child0.SetPadding(YogaEdge.All, YogaValue.Point(1f));
        root_child0_child0.SetBorder(YogaEdge.All, 2f);
        root_child0.InsertChild(root_child0_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(136f, root.LayoutWidth);
        Assert.Equal(136f, root.LayoutHeight);
        Assert.Equal(18f, root_child0.LayoutX);
        Assert.Equal(18f, root_child0.LayoutY);
        Assert.Equal(38f, root_child0.LayoutWidth);
        Assert.Equal(38f, root_child0.LayoutHeight);
        Assert.Equal(9f, root_child0_child0.LayoutX);
        Assert.Equal(9f, root_child0_child0.LayoutY);
        Assert.Equal(16f, root_child0_child0.LayoutWidth);
        Assert.Equal(11f, root_child0_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(136f, root.LayoutWidth);
        Assert.Equal(136f, root.LayoutHeight);
        Assert.Equal(80f, root_child0.LayoutX);
        Assert.Equal(18f, root_child0.LayoutY);
        Assert.Equal(38f, root_child0.LayoutWidth);
        Assert.Equal(38f, root_child0.LayoutHeight);
        Assert.Equal(13f, root_child0_child0.LayoutX);
        Assert.Equal(9f, root_child0_child0.LayoutY);
        Assert.Equal(16f, root_child0_child0.LayoutWidth);
        Assert.Equal(11f, root_child0_child0.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Nested()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.SetPadding(YogaEdge.All, YogaValue.Point(15f));
        root.SetBorder(YogaEdge.All, 3f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(20f);
        root_child0.Height = YogaValue.Point(20f);
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(2f));
        root_child0.SetBorder(YogaEdge.All, 7f);
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.Width = YogaValue.Point(10f);
        root_child0_child0.Height = YogaValue.Point(5f);
        root_child0_child0.SetPadding(YogaEdge.All, YogaValue.Point(1f));
        root_child0_child0.SetBorder(YogaEdge.All, 2f);
        root_child0.InsertChild(root_child0_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(18f, root_child0.LayoutX);
        Assert.Equal(18f, root_child0.LayoutY);
        Assert.Equal(20f, root_child0.LayoutWidth);
        Assert.Equal(20f, root_child0.LayoutHeight);
        Assert.Equal(9f, root_child0_child0.LayoutX);
        Assert.Equal(9f, root_child0_child0.LayoutY);
        Assert.Equal(10f, root_child0_child0.LayoutWidth);
        Assert.Equal(6f, root_child0_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(62f, root_child0.LayoutX);
        Assert.Equal(18f, root_child0.LayoutY);
        Assert.Equal(20f, root_child0.LayoutWidth);
        Assert.Equal(20f, root_child0.LayoutHeight);
        Assert.Equal(1f, root_child0_child0.LayoutX);
        Assert.Equal(9f, root_child0_child0.LayoutY);
        Assert.Equal(10f, root_child0_child0.LayoutWidth);
        Assert.Equal(6f, root_child0_child0.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_Nested_Alternating()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root.SetPadding(YogaEdge.All, YogaValue.Point(3f));
        root.SetBorder(YogaEdge.All, 2f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(40f);
        root_child0.Height = YogaValue.Point(40f);
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(8f));
        root_child0.SetBorder(YogaEdge.All, 2f);
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.Width = YogaValue.Point(20f);
        root_child0_child0.Height = YogaValue.Point(25f);
        root_child0_child0.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root_child0_child0.SetPadding(YogaEdge.All, YogaValue.Point(3f));
        root_child0_child0.SetBorder(YogaEdge.All, 6f);
        root_child0.InsertChild(root_child0_child0, 0);
        var root_child0_child0_child0 = new YogaNode(config);
        root_child0_child0_child0.Width = YogaValue.Point(10f);
        root_child0_child0_child0.Height = YogaValue.Point(5f);
        root_child0_child0_child0.SetPadding(YogaEdge.All, YogaValue.Point(1f));
        root_child0_child0_child0.SetBorder(YogaEdge.All, 1f);
        root_child0_child0.InsertChild(root_child0_child0_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(110f, root.LayoutWidth);
        Assert.Equal(110f, root.LayoutHeight);
        Assert.Equal(5f, root_child0.LayoutX);
        Assert.Equal(5f, root_child0.LayoutY);
        Assert.Equal(40f, root_child0.LayoutWidth);
        Assert.Equal(40f, root_child0.LayoutHeight);
        Assert.Equal(10f, root_child0_child0.LayoutX);
        Assert.Equal(10f, root_child0_child0.LayoutY);
        Assert.Equal(38f, root_child0_child0.LayoutWidth);
        Assert.Equal(43f, root_child0_child0.LayoutHeight);
        Assert.Equal(9f, root_child0_child0_child0.LayoutX);
        Assert.Equal(9f, root_child0_child0_child0.LayoutY);
        Assert.Equal(10f, root_child0_child0_child0.LayoutWidth);
        Assert.Equal(5f, root_child0_child0_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(110f, root.LayoutWidth);
        Assert.Equal(110f, root.LayoutHeight);
        Assert.Equal(65f, root_child0.LayoutX);
        Assert.Equal(5f, root_child0.LayoutY);
        Assert.Equal(40f, root_child0.LayoutWidth);
        Assert.Equal(40f, root_child0.LayoutHeight);
        Assert.Equal(-8f, root_child0_child0.LayoutX);
        Assert.Equal(10f, root_child0_child0.LayoutY);
        Assert.Equal(38f, root_child0_child0.LayoutWidth);
        Assert.Equal(43f, root_child0_child0.LayoutHeight);
        Assert.Equal(19f, root_child0_child0_child0.LayoutX);
        Assert.Equal(9f, root_child0_child0_child0.LayoutY);
        Assert.Equal(10f, root_child0_child0_child0.LayoutWidth);
        Assert.Equal(5f, root_child0_child0_child0.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Nested_Alternating()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.SetPadding(YogaEdge.All, YogaValue.Point(3f));
        root.SetBorder(YogaEdge.All, 2f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(40f);
        root_child0.Height = YogaValue.Point(40f);
        root_child0.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(8f));
        root_child0.SetBorder(YogaEdge.All, 2f);
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.Width = YogaValue.Point(20f);
        root_child0_child0.Height = YogaValue.Point(25f);
        root_child0_child0.SetPadding(YogaEdge.All, YogaValue.Point(3f));
        root_child0_child0.SetBorder(YogaEdge.All, 6f);
        root_child0.InsertChild(root_child0_child0, 0);
        var root_child0_child0_child0 = new YogaNode(config);
        root_child0_child0_child0.Width = YogaValue.Point(10f);
        root_child0_child0_child0.Height = YogaValue.Point(5f);
        root_child0_child0_child0.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root_child0_child0_child0.SetPadding(YogaEdge.All, YogaValue.Point(1f));
        root_child0_child0_child0.SetBorder(YogaEdge.All, 1f);
        root_child0_child0.InsertChild(root_child0_child0_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(5f, root_child0.LayoutX);
        Assert.Equal(5f, root_child0.LayoutY);
        Assert.Equal(60f, root_child0.LayoutWidth);
        Assert.Equal(60f, root_child0.LayoutHeight);
        Assert.Equal(10f, root_child0_child0.LayoutX);
        Assert.Equal(10f, root_child0_child0.LayoutY);
        Assert.Equal(20f, root_child0_child0.LayoutWidth);
        Assert.Equal(25f, root_child0_child0.LayoutHeight);
        Assert.Equal(9f, root_child0_child0_child0.LayoutX);
        Assert.Equal(9f, root_child0_child0_child0.LayoutY);
        Assert.Equal(14f, root_child0_child0_child0.LayoutWidth);
        Assert.Equal(9f, root_child0_child0_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(35f, root_child0.LayoutX);
        Assert.Equal(5f, root_child0.LayoutY);
        Assert.Equal(60f, root_child0.LayoutWidth);
        Assert.Equal(60f, root_child0.LayoutHeight);
        Assert.Equal(30f, root_child0_child0.LayoutX);
        Assert.Equal(10f, root_child0_child0.LayoutY);
        Assert.Equal(20f, root_child0_child0.LayoutWidth);
        Assert.Equal(25f, root_child0_child0.LayoutHeight);
        Assert.Equal(-3f, root_child0_child0_child0.LayoutX);
        Assert.Equal(9f, root_child0_child0_child0.LayoutY);
        Assert.Equal(14f, root_child0_child0_child0.LayoutWidth);
        Assert.Equal(9f, root_child0_child0_child0.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Flex_Basis_Row()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.FlexBasis = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(25f);
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(5f));
        root_child0.SetBorder(YogaEdge.All, 10f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(30f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(50f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(30f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Flex_Basis_Column()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.FlexBasis = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(25f);
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(5f));
        root_child0.SetBorder(YogaEdge.All, 10f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_Padding_Start()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.SetPadding(YogaEdge.Start, YogaValue.Point(5f));
        root.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(105f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(105f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Padding_Start()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.SetPadding(YogaEdge.Start, YogaValue.Point(5f));
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_Padding_End()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.SetPadding(YogaEdge.End, YogaValue.Point(5f));
        root.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(105f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(105f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Padding_End()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.SetPadding(YogaEdge.End, YogaValue.Point(5f));
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_Border_Start()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.SetBorder(YogaEdge.Start, 5f);
        root.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(105f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(105f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Border_Start()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.SetBorder(YogaEdge.Start, 5f);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Content_Box_Border_End()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.SetBorder(YogaEdge.End, 5f);
        root.Style.BoxSizing = YogaBoxSizing.ContentBox;
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(105f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(105f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
    }

    [Fact]
    public void Box_Sizing_Border_Box_Border_End()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.SetBorder(YogaEdge.End, 5f);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
    }

}
