using System.Numerics;
using Microsoft.UI.Reactor.Navigation;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for TransitionEngine's pure helper functions: ReverseDirection and GetSlideOffsets —
/// the direction-resolution and offset math shared by the slide and spring-slide transitions.
///
/// <para>
/// The WinUI motion constants and the Push/Pop plan builders are covered separately, with
/// citations into the WinUI sources they were taken from, in
/// <see cref="WinUiNavigationMotionParityTests"/>.
/// </para>
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
    public void SlideTransition_Direction_Default_Matches_WinUI_FromBottom()
    {
        var slide = new SlideTransition();
        Assert.Equal(SlideDirection.FromBottom, slide.Direction);
    }

    [Fact]
    public void Default_Slide_Uses_WinUI_Specification()
    {
        Assert.True(TransitionEngine.UsesWinUISlideSpecification(new SlideTransition()));
    }

    [Fact]
    public void FromTop_Slide_Preserves_Reactor_Behavior()
    {
        Assert.False(TransitionEngine.UsesWinUISlideSpecification(
            new SlideTransition { Direction = SlideDirection.FromTop }));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Customized_Slide_Preserves_Reactor_Behavior(
        bool customizeDuration, bool customizeDistance)
    {
        var slide = new SlideTransition
        {
            Duration = customizeDuration ? TimeSpan.FromMilliseconds(400) : null,
            Distance = customizeDistance ? 300f : null,
        };

        Assert.False(TransitionEngine.UsesWinUISlideSpecification(slide));
    }

    [Theory]
    [InlineData(SlideDirection.FromLeft, NavigationMode.Push, 150f, -200f)]
    [InlineData(SlideDirection.FromRight, NavigationMode.Push, -150f, 200f)]
    [InlineData(SlideDirection.FromLeft, NavigationMode.Pop, -200f, 150f)]
    [InlineData(SlideDirection.FromRight, NavigationMode.Pop, 200f, -150f)]
    public void HorizontalSlidePlan_Reverses_Offsets_Between_Push_And_Pop(
        SlideDirection direction, NavigationMode mode, float outX, float inX)
    {
        var plan = TransitionEngine.GetHorizontalSlidePlan(direction, mode);

        Assert.Equal(new Vector3(outX, 0, 0), plan.OutEnd);
        Assert.Equal(new Vector3(inX, 0, 0), plan.InStart);
    }

    [Fact]
    public void DrillInPlan_Push_Grows_Outgoing_And_Shrinks_Incoming()
    {
        var plan = TransitionEngine.GetDrillInPlan(NavigationMode.Push);

        Assert.Equal(1.04f, plan.OutEndScale);
        Assert.Equal(0.94f, plan.InStartScale);
        Assert.Equal(TimeSpan.FromMilliseconds(100), plan.OutScaleDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(100), plan.OutOpacityDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(783), plan.InScaleDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(333), plan.InOpacityDuration);
        Assert.Equal(new Vector2(0.1f, 0.9f), plan.InScaleEasingControlPoint1);
        Assert.Equal(new Vector2(0.2f, 1.0f), plan.InScaleEasingControlPoint2);
    }

    [Fact]
    public void DrillInPlan_Pop_Shrinks_Outgoing_And_Grows_Incoming()
    {
        var plan = TransitionEngine.GetDrillInPlan(NavigationMode.Pop);

        Assert.Equal(0.96f, plan.OutEndScale);
        Assert.Equal(1.06f, plan.InStartScale);
        Assert.Equal(TimeSpan.FromMilliseconds(100), plan.OutScaleDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(100), plan.OutOpacityDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(333), plan.InScaleDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(333), plan.InOpacityDuration);
        Assert.Equal(new Vector2(0.12f, 0.0f), plan.InScaleEasingControlPoint1);
        Assert.Equal(new Vector2(0.0f, 1.0f), plan.InScaleEasingControlPoint2);
    }

    [Fact]
    public void DrillIn_Opacity_Easing_Constants_Are_Pinned()
    {
        Assert.Equal(new Vector2(0.17f, 0.17f), TransitionEngine.DrillInOpacityEasingControlPoint1);
        Assert.Equal(new Vector2(0.0f, 1.0f), TransitionEngine.DrillInOpacityEasingControlPoint2);
    }

    [Fact]
    public void Custom_DrillIn_Duration_Preserves_Reactor_Behavior()
    {
        var drill = new DrillInTransition { Duration = TimeSpan.FromMilliseconds(400) };

        Assert.False(TransitionEngine.UsesWinUIDrillInSpecification(drill));
    }

    [Fact]
    public void ConnectedTransition_Can_Be_Created()
    {
        var connected = new ConnectedTransition { AnimationKey = "hero" };
        Assert.NotNull(connected);
        Assert.Equal("hero", connected.AnimationKey);
    }
}
