using Microsoft.UI.Reactor.Layout;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.YogaGenerated;

/// <summary>
/// Ported from yoga/tests/generated/YGFlexBasisFitContentTest.cpp
/// </summary>
public class YogaFlexBasisFitContentTest
{
    [Fact]
    public void Container_Child_Overflows_Definite_Parent_Column()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(300f);
        var root_child0 = new YogaNode(config);
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.Height = YogaValue.Point(500f);
        root_child0_child0.Width = YogaValue.Point(50f);
        root_child0.InsertChild(root_child0_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(300f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(200f, root_child0.LayoutWidth);
        Assert.Equal(500f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(50f, root_child0_child0.LayoutWidth);
        Assert.Equal(500f, root_child0_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(300f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(200f, root_child0.LayoutWidth);
        Assert.Equal(500f, root_child0.LayoutHeight);
        Assert.Equal(150f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(50f, root_child0_child0.LayoutWidth);
        Assert.Equal(500f, root_child0_child0.LayoutHeight);
    }

    [Fact]
    public void Container_Child_Overflows_Definite_Parent_Row()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(300f);
        root.Height = YogaValue.Point(200f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.Width = YogaValue.Point(500f);
        root_child0_child0.Height = YogaValue.Point(50f);
        root_child0.InsertChild(root_child0_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(300f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(500f, root_child0.LayoutWidth);
        Assert.Equal(200f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(500f, root_child0_child0.LayoutWidth);
        Assert.Equal(50f, root_child0_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(300f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(-200f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(500f, root_child0.LayoutWidth);
        Assert.Equal(200f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(500f, root_child0_child0.LayoutWidth);
        Assert.Equal(50f, root_child0_child0.LayoutHeight);
    }

    [Fact]
    public void Container_Child_Within_Bounds_Column()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(300f);
        var root_child0 = new YogaNode(config);
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.Height = YogaValue.Point(100f);
        root_child0_child0.Width = YogaValue.Point(50f);
        root_child0.InsertChild(root_child0_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(300f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(200f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(50f, root_child0_child0.LayoutWidth);
        Assert.Equal(100f, root_child0_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(300f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(200f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(150f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(50f, root_child0_child0.LayoutWidth);
        Assert.Equal(100f, root_child0_child0.LayoutHeight);
    }

    [Fact]
    public void Multiple_Container_Children_Overflow_Column()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(300f);
        var root_child0 = new YogaNode(config);
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.Height = YogaValue.Point(400f);
        root_child0.InsertChild(root_child0_child0, 0);
        var root_child1 = new YogaNode(config);
        root.InsertChild(root_child1, 1);
        var root_child1_child0 = new YogaNode(config);
        root_child1_child0.Height = YogaValue.Point(500f);
        root_child1.InsertChild(root_child1_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(300f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(200f, root_child0.LayoutWidth);
        Assert.Equal(400f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(200f, root_child0_child0.LayoutWidth);
        Assert.Equal(400f, root_child0_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(400f, root_child1.LayoutY);
        Assert.Equal(200f, root_child1.LayoutWidth);
        Assert.Equal(500f, root_child1.LayoutHeight);
        Assert.Equal(0f, root_child1_child0.LayoutX);
        Assert.Equal(0f, root_child1_child0.LayoutY);
        Assert.Equal(200f, root_child1_child0.LayoutWidth);
        Assert.Equal(500f, root_child1_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(300f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(200f, root_child0.LayoutWidth);
        Assert.Equal(400f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(200f, root_child0_child0.LayoutWidth);
        Assert.Equal(400f, root_child0_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(400f, root_child1.LayoutY);
        Assert.Equal(200f, root_child1.LayoutWidth);
        Assert.Equal(500f, root_child1.LayoutHeight);
        Assert.Equal(0f, root_child1_child0.LayoutX);
        Assert.Equal(0f, root_child1_child0.LayoutY);
        Assert.Equal(200f, root_child1_child0.LayoutWidth);
        Assert.Equal(500f, root_child1_child0.LayoutHeight);
    }

    [Fact]
    public void Scroll_Container_Column()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(300f);
        root.Overflow = YogaOverflow.Scroll;
        var root_child0 = new YogaNode(config);
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.Height = YogaValue.Point(500f);
        root_child0.InsertChild(root_child0_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(300f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(200f, root_child0.LayoutWidth);
        Assert.Equal(500f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(200f, root_child0_child0.LayoutWidth);
        Assert.Equal(500f, root_child0_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(300f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(200f, root_child0.LayoutWidth);
        Assert.Equal(500f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(200f, root_child0_child0.LayoutWidth);
        Assert.Equal(500f, root_child0_child0.LayoutHeight);
    }

    [Fact]
    public void Explicit_And_Container_Children_Column()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(300f);
        var root_child0 = new YogaNode(config);
        root_child0.Height = YogaValue.Point(100f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root.InsertChild(root_child1, 1);
        var root_child1_child0 = new YogaNode(config);
        root_child1_child0.Height = YogaValue.Point(500f);
        root_child1.InsertChild(root_child1_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(300f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(200f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(100f, root_child1.LayoutY);
        Assert.Equal(200f, root_child1.LayoutWidth);
        Assert.Equal(500f, root_child1.LayoutHeight);
        Assert.Equal(0f, root_child1_child0.LayoutX);
        Assert.Equal(0f, root_child1_child0.LayoutY);
        Assert.Equal(200f, root_child1_child0.LayoutWidth);
        Assert.Equal(500f, root_child1_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(300f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(200f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(100f, root_child1.LayoutY);
        Assert.Equal(200f, root_child1.LayoutWidth);
        Assert.Equal(500f, root_child1.LayoutHeight);
        Assert.Equal(0f, root_child1_child0.LayoutX);
        Assert.Equal(0f, root_child1_child0.LayoutY);
        Assert.Equal(200f, root_child1_child0.LayoutWidth);
        Assert.Equal(500f, root_child1_child0.LayoutHeight);
    }

}
