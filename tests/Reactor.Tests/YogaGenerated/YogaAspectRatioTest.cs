using Microsoft.UI.Reactor.Layout;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.YogaGenerated;

/// <summary>
/// Ported from yoga/tests/generated/YGAspectRatioTest.cpp
/// </summary>
public class YogaAspectRatioTest
{

    [Fact]
    public void Zero_Aspect_Ratio_Behaves_Like_Auto()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(300f);
        root.Height = YogaValue.Point(300f);
        var root_child0 = new YogaNode(config);
        root_child0.AspectRatio = 0f;
        root_child0.Width = YogaValue.Point(50f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(300f, root.LayoutWidth);
        Assert.Equal(300f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(300f, root.LayoutWidth);
        Assert.Equal(300f, root.LayoutHeight);
        Assert.Equal(250f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
    }

}
