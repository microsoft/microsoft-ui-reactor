using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Animation;

/// <summary>
/// Spec 066 §5.3: how a modifier write moves. <c>AnimationHelper.ResolvePolicy</c> picks the
/// ambient <c>WithAnimation</c> curve, then <c>.Animate()</c> for the channels its flags name, then a
/// declared <c>.TranslationTransition()</c> with the XAML curve and its axis mask.
/// </summary>
public class TransitionPolicyTests
{
    private static readonly AnimationHelper.TransitionTiming Translation =
        new(TimeSpan.FromMilliseconds(450), Vector3TransitionComponents.X);

    private const Vector3TransitionComponents All =
        Vector3TransitionComponents.X | Vector3TransitionComponents.Y | Vector3TransitionComponents.Z;

    [Fact]
    public void A_declared_TranslationTransition_eases_with_the_XAML_curve_and_keeps_its_mask()
    {
        var policy = AnimationHelper.ResolvePolicy(null, null, AnimateProperty.Offset, Translation);
        Assert.Equal(new EaseCurve(TimeSpan.FromMilliseconds(450), new Easing(0.8f, 0f, 0.2f, 1f)), policy.Curve);
        Assert.Equal(Vector3TransitionComponents.X, policy.Components);
    }

    [Fact]
    public void WithAnimation_wins_over_the_transition_and_animates_every_axis()
    {
        var ambient = Curve.Linear(100);
        var policy = AnimationHelper.ResolvePolicy(ambient, new AnimationConfig(Curve.Ease(200)), AnimateProperty.Offset, Translation);
        Assert.Same(ambient, policy.Curve);
        Assert.Equal(All, policy.Components);
    }

    [Fact]
    public void Animate_wins_only_for_the_channels_its_flags_name()
    {
        var animate = Curve.Ease(200);
        Assert.Same(animate, AnimationHelper.ResolvePolicy(null, new AnimationConfig(animate, AnimateProperty.Offset), AnimateProperty.Offset, Translation).Curve);

        // .Animate(Opacity) no longer overrides a declared Translation transition (§6.1).
        var narrowed = AnimationHelper.ResolvePolicy(null, new AnimationConfig(animate, AnimateProperty.Opacity), AnimateProperty.Offset, Translation);
        Assert.IsType<EaseCurve>(narrowed.Curve);
        Assert.NotSame(animate, narrowed.Curve);

        // And a channel it excludes jumps when nothing else declares a curve.
        Assert.Null(AnimationHelper.ResolvePolicy(null, new AnimationConfig(animate, AnimateProperty.Opacity), AnimateProperty.Scale, null).Curve);
    }

    [Fact]
    public void Nothing_declared_means_a_direct_write()
        => Assert.Null(AnimationHelper.ResolvePolicy(null, null, AnimateProperty.Offset, null).Curve);

    [Fact]
    public void A_zero_duration_transition_still_yields_a_valid_composition_duration()
    {
        var policy = AnimationHelper.ResolvePolicy(null, null, AnimateProperty.Offset, new(TimeSpan.Zero, All));
        Assert.True(((EaseCurve)policy.Curve!).Duration > TimeSpan.Zero);
    }
}
