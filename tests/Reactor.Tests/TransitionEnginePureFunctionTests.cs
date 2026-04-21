using System.Numerics;
using Microsoft.UI.Reactor.Navigation;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for TransitionEngine's pure helper functions: ReverseDirection and GetSlideOffsets.
/// These are the core direction-resolution and offset-calculation algorithms used by all
/// slide and spring-slide transitions during page navigation.
/// </summary>
public class TransitionEnginePureFunctionTests
{
    // ════════════════════════════════════════════════════════════════
    //  ReverseDirection — used to flip slide direction on GoBack
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(SlideDirection.FromRight, SlideDirection.FromLeft)]
    [InlineData(SlideDirection.FromLeft, SlideDirection.FromRight)]
    [InlineData(SlideDirection.FromBottom, SlideDirection.FromTop)]
    [InlineData(SlideDirection.FromTop, SlideDirection.FromBottom)]
    public void ReverseDirection_Flips_Direction(SlideDirection input, SlideDirection expected)
    {
        var result = TransitionEngine.ReverseDirection(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReverseDirection_RoundTrips()
    {
        foreach (var dir in new[] { SlideDirection.FromRight, SlideDirection.FromLeft, SlideDirection.FromBottom, SlideDirection.FromTop })
        {
            var reversed = TransitionEngine.ReverseDirection(dir);
            var restored = TransitionEngine.ReverseDirection(reversed);
            Assert.Equal(dir, restored);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  GetSlideOffsets — computes outgoing end and incoming start positions
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetSlideOffsets_FromRight_Moves_Along_X_Axis()
    {
        var (outEnd, inStart) = TransitionEngine.GetSlideOffsets(SlideDirection.FromRight, 200f);
        Assert.Equal(new Vector3(-200, 0, 0), outEnd);
        Assert.Equal(new Vector3(200, 0, 0), inStart);
    }

    [Fact]
    public void GetSlideOffsets_FromLeft_Moves_Along_Negative_X()
    {
        var (outEnd, inStart) = TransitionEngine.GetSlideOffsets(SlideDirection.FromLeft, 200f);
        Assert.Equal(new Vector3(200, 0, 0), outEnd);
        Assert.Equal(new Vector3(-200, 0, 0), inStart);
    }

    [Fact]
    public void GetSlideOffsets_FromBottom_Moves_Along_Y_Axis()
    {
        var (outEnd, inStart) = TransitionEngine.GetSlideOffsets(SlideDirection.FromBottom, 150f);
        Assert.Equal(new Vector3(0, -150, 0), outEnd);
        Assert.Equal(new Vector3(0, 150, 0), inStart);
    }

    [Fact]
    public void GetSlideOffsets_FromTop_Moves_Along_Negative_Y()
    {
        var (outEnd, inStart) = TransitionEngine.GetSlideOffsets(SlideDirection.FromTop, 150f);
        Assert.Equal(new Vector3(0, 150, 0), outEnd);
        Assert.Equal(new Vector3(0, -150, 0), inStart);
    }

    [Fact]
    public void GetSlideOffsets_CustomDistance_Scales_Correctly()
    {
        var (outEnd, inStart) = TransitionEngine.GetSlideOffsets(SlideDirection.FromRight, 500f);
        Assert.Equal(new Vector3(-500, 0, 0), outEnd);
        Assert.Equal(new Vector3(500, 0, 0), inStart);
    }

    [Fact]
    public void GetSlideOffsets_OutEnd_And_InStart_Are_Opposite_Directions()
    {
        foreach (var dir in new[] { SlideDirection.FromRight, SlideDirection.FromLeft, SlideDirection.FromBottom, SlideDirection.FromTop })
        {
            var (outEnd, inStart) = TransitionEngine.GetSlideOffsets(dir, 200f);
            Assert.Equal(Vector3.Zero, outEnd + inStart);
        }
    }

    [Fact]
    public void GetSlideOffsets_DefaultDistance_Uses_200()
    {
        var (outEnd, inStart) = TransitionEngine.GetSlideOffsets(SlideDirection.FromRight);
        Assert.Equal(new Vector3(-200, 0, 0), outEnd);
        Assert.Equal(new Vector3(200, 0, 0), inStart);
    }

    // ════════════════════════════════════════════════════════════════
    //  Transition type records — semantic validation
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void SpringSlideTransition_Has_Reasonable_Defaults()
    {
        var spring = new SpringSlideTransition();
        Assert.True(spring.DampingRatio > 0);
        Assert.True(spring.Period > 0);
    }

    [Fact]
    public void SlideTransition_Direction_Default_Is_FromRight()
    {
        var slide = new SlideTransition();
        Assert.Equal(SlideDirection.FromRight, slide.Direction);
    }

    [Fact]
    public void ConnectedTransition_Can_Be_Created()
    {
        var connected = new ConnectedTransition { AnimationKey = "hero" };
        Assert.NotNull(connected);
        Assert.Equal("hero", connected.AnimationKey);
    }
}
