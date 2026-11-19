using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class ScrollBehaviorTests
{
    [Theory]
    [InlineData(940, 1000, 60, true)]
    [InlineData(939, 1000, 60, false)]
    [InlineData(0, 0, 0, true)]
    [InlineData(1000, 1000, 0, true)]
    [InlineData(double.NaN, 1000, 60, false)]
    public void FollowingUsesDistanceFromTail(double offset, double extent, double threshold, bool expected)
        => Assert.Equal(expected, ItemsViewScrollBehavior.IsNearTail(offset, extent, threshold));

    [Fact]
    public void AnnotationLookupUsesOffsetNotListPosition()
    {
        ScrollAnnotation[] annotations = [new(300, "Last"), new(0, "Start"), new(20, "Near start")];
        Assert.Equal("Near start", AnnotatedScrollBehavior.Nearest(annotations, 30)?.Label);
        Assert.Equal("Last", AnnotatedScrollBehavior.Nearest(annotations, 1000)?.Label);
        Assert.Equal("Start", AnnotatedScrollBehavior.Nearest(annotations, -10)?.Label);
        Assert.Null(AnnotatedScrollBehavior.Nearest(annotations, double.NaN));
        Assert.Null(AnnotatedScrollBehavior.Nearest([], 10));
    }

    [Theory]
    [InlineData(50, 800, 160)]
    [InlineData(900, 800, 640)]
    [InlineData(350, 800, 350)]
    [InlineData(500, 300, 252)]
    [InlineData(160, 120, 72)]
    public void PaneBoundsLeaveContentAccessible(double requested, double viewport, double expected)
        => Assert.Equal(expected, SplitViewResizeBehavior.Constrain(requested, new PaneResizeOptions(), viewport));

    [Theory]
    [InlineData(-1, 200, 16)]
    [InlineData(300, 200, 16)]
    [InlineData(100, double.PositiveInfinity, 16)]
    [InlineData(100, 200, 0)]
    public void InvalidPaneBoundsAreRejected(double minimum, double maximum, double step)
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            SplitViewResizeBehavior.Validate(new(minimum, maximum, step)));
}
