using Microsoft.UI.Reactor.Animation;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for AnimationScope thread-static context management.
/// Verifies nesting behavior, scope restoration, and async fallback paths.
/// </summary>
public class AnimationScopeTests
{
    [Fact]
    public void WithAnimation_Sets_And_Restores_Current()
    {
        Assert.Null(AnimationScope.Current);
        Assert.False(AnimationScope.HasScope);

        var curve = Curve.Ease(200);
        AnimationScope.WithAnimation(curve, () =>
        {
            Assert.Same(curve, AnimationScope.Current);
            Assert.True(AnimationScope.HasScope);
        });

        Assert.Null(AnimationScope.Current);
        Assert.False(AnimationScope.HasScope);
    }

    [Fact]
    public void WithAnimation_Nesting_Restores_Outer()
    {
        var outer = Curve.Ease(300);
        var inner = Curve.Ease(100);

        AnimationScope.WithAnimation(outer, () =>
        {
            Assert.Same(outer, AnimationScope.Current);
            AnimationScope.WithAnimation(inner, () =>
            {
                Assert.Same(inner, AnimationScope.Current);
            });
            Assert.Same(outer, AnimationScope.Current);
        });

        Assert.Null(AnimationScope.Current);
    }

    [Fact]
    public void WithAnimation_Null_Curve_Suppresses_Animation()
    {
        var outer = Curve.Ease(200);
        AnimationScope.WithAnimation(outer, () =>
        {
            Assert.Same(outer, AnimationScope.Current);
            AnimationScope.WithAnimation(null, () =>
            {
                Assert.Null(AnimationScope.Current);
                Assert.True(AnimationScope.HasScope); // scope is active even with null curve
            });
            Assert.Same(outer, AnimationScope.Current);
        });
    }

    [Fact]
    public void WithAnimation_Restores_On_Exception()
    {
        var curve = Curve.Ease(200);
        try
        {
            AnimationScope.WithAnimation(curve, () =>
            {
                throw new InvalidOperationException("test");
            });
        }
        catch (InvalidOperationException) { }

        Assert.Null(AnimationScope.Current);
        Assert.False(AnimationScope.HasScope);
    }

    [Fact]
    public void PushScope_PopScope_Sets_And_Clears()
    {
        var curve = Curve.Spring(0.8f, 0.05f);
        AnimationScope.PushScope(curve);
        Assert.Same(curve, AnimationScope.Current);
        Assert.True(AnimationScope.HasScope);

        AnimationScope.PopScope();
        Assert.Null(AnimationScope.Current);
        Assert.False(AnimationScope.HasScope);
    }

    [Fact]
    public async Task WithAnimationAsync_No_Compositor_Returns_CompletedTask()
    {
        // In unit tests, CompositorProvider.Current is null, so async falls back to sync
        bool ran = false;
        var task = AnimationScope.WithAnimationAsync(
            Curve.Linear(100),
            () => { ran = true; });

        Assert.True(task.IsCompleted);
        Assert.True(ran);
        await task; // should not throw
    }

    [Fact]
    public void Curve_Factory_Methods()
    {
        var spring = Curve.Spring(0.9f, 0.03f);
        Assert.IsType<SpringCurve>(spring);

        var ease = Curve.Ease(300, Easing.EaseInOut);
        Assert.IsType<EaseCurve>(ease);
        Assert.Equal(TimeSpan.FromMilliseconds(300), ((EaseCurve)ease).Duration);

        var linear = Curve.Linear(150);
        Assert.IsType<LinearCurve>(linear);
        Assert.Equal(TimeSpan.FromMilliseconds(150), ((LinearCurve)linear).Duration);
    }

    [Fact]
    public void Easing_Presets_Have_Expected_Values()
    {
        Assert.Equal(0f, Easing.Linear.X1);
        Assert.Equal(1f, Easing.Linear.X2);
        Assert.NotEqual(0f, Easing.EaseIn.X1); // 0.42
    }
}
