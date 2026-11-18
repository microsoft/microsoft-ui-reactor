using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Xunit;
using static Microsoft.UI.Reactor.Factories;
using XamlTransition = Microsoft.UI.Xaml.Media.Animation.Transition;

namespace Microsoft.UI.Reactor.Tests;

public class AnimationExtrasEqualityTests
{
    [Theory]
    [InlineData(nameof(Element.AnimationConfig))]
    [InlineData(nameof(Element.LayoutAnimation))]
    [InlineData(nameof(Element.ImplicitTransitions))]
    [InlineData(nameof(Element.ThemeTransitions))]
    [InlineData(nameof(Element.InteractionStates))]
    [InlineData(nameof(Element.KeyframeAnimations))]
    [InlineData(nameof(Element.StaggerConfig))]
    [InlineData(nameof(Element.ElementTransition))]
    [InlineData(nameof(Element.ConnectedAnimationKey))]
    public void Configuration_Changes_DeclineSkip_While_Equal_Values_Skip(string property)
    {
        var (firstValue, equalValue, changedValue) = Values(property);
        var first = WithValue(property, firstValue);
        var equal = WithValue(property, equalValue);
        var changed = WithValue(property, changedValue);
        var cleared = WithValue(property, null);

        Assert.True(Element.ShallowEquals(first, equal));
        Assert.True(Element.CanSkipUpdate(first, equal));
        Assert.Equal(first, equal);
        Assert.Equal(first.GetHashCode(), equal.GetHashCode());
        foreach (var other in new[] { changed, cleared })
        {
            Assert.False(Element.ShallowEquals(first, other));
            Assert.False(Element.ShallowEquals(other, first));
            Assert.False(Element.CanSkipUpdate(first, other));
            Assert.False(Element.CanSkipUpdate(other, first));
        }
        Assert.True(Element.ShallowEquals(Button("Go"), cleared));
        Assert.False(Element.HasBehavioralExtras(cleared));
    }

    [Fact]
    public void Clearing_One_Animation_Preserves_Other_Extras_And_The_Original_Record()
    {
        var original = Button("Go").Grid(row: 2)
            .LayoutAnimation(TimeSpan.FromMilliseconds(200)).ConnectedAnimation("hero")
            with { CallSite = new SourceLocation("Example.cs", 7, 0) };
        var copy = original with { LayoutAnimation = null };

        Assert.NotNull(original.LayoutAnimation);
        Assert.Null(copy.LayoutAnimation);
        Assert.Equal("hero", copy.ConnectedAnimationKey);
        Assert.Same(original.Attached, copy.Attached);
        Assert.Equal(original.CallSite, copy.CallSite);
        Assert.True(Element.HasBehavioralExtras(copy));

        var cleared = copy with { ConnectedAnimationKey = null, Attached = null };
        Assert.False(Element.HasBehavioralExtras(cleared));
        Assert.Equal(original.CallSite, cleared.CallSite);
        Assert.Equal(Button("Go"), cleared);
        Assert.Equal(Button("Go").GetHashCode(), cleared.GetHashCode());
    }

    private static (object First, object Equal, object? Changed) Values(string property)
    {
        switch (property)
        {
            case nameof(Element.AnimationConfig):
                return (new AnimationConfig(Curve.Ease(200)), new AnimationConfig(Curve.Ease(200)), new AnimationConfig(Curve.Ease(800)));
            case nameof(Element.LayoutAnimation):
                return (new LayoutAnimationConfig { Duration = TimeSpan.FromMilliseconds(200) },
                    new LayoutAnimationConfig { Duration = TimeSpan.FromMilliseconds(200) },
                    new LayoutAnimationConfig { Duration = TimeSpan.FromMilliseconds(800) });
            case nameof(Element.ImplicitTransitions):
                // Native transition values are exercised in the WinUI selftest.
                return (new ImplicitTransitions(), new ImplicitTransitions(), null);
            case nameof(Element.ThemeTransitions):
                // Arrays keep reference equality, including two distinct empty arrays.
                var children = new XamlTransition[0];
                return (new ThemeTransitions { Children = children }, new ThemeTransitions { Children = children },
                    new ThemeTransitions { Children = new XamlTransition[0] });
            case nameof(Element.InteractionStates):
                return (new InteractionStatesConfig(PointerOver: new(Opacity: 0.5f)),
                    new InteractionStatesConfig(PointerOver: new(Opacity: 0.5f)),
                    new InteractionStatesConfig(PointerOver: new(Opacity: 0.8f)));
            case nameof(Element.KeyframeAnimations):
                var entries = new[] { new KeyframeEntry("pulse", 1, new KeyframeAnimationDef()) };
                return (entries, entries, entries.ToArray());
            case nameof(Element.StaggerConfig):
                return (new StaggerConfig(TimeSpan.FromMilliseconds(20)), new StaggerConfig(TimeSpan.FromMilliseconds(20)),
                    new StaggerConfig(TimeSpan.FromMilliseconds(40)));
            case nameof(Element.ElementTransition):
                return (new ElementTransition(Transition.Fade, Curve.Ease(200)),
                    new ElementTransition(Transition.Fade, Curve.Ease(200)),
                    new ElementTransition(Transition.Fade, Curve.Ease(800)));
            case nameof(Element.ConnectedAnimationKey):
                return ("hero", new string("hero".ToCharArray()), "other");
            default:
                throw new ArgumentOutOfRangeException(nameof(property));
        }
    }

    private static Element WithValue(string property, object? value) => property switch
    {
        nameof(Element.AnimationConfig) => Button("Go") with { AnimationConfig = (AnimationConfig?)value },
        nameof(Element.LayoutAnimation) => Button("Go") with { LayoutAnimation = (LayoutAnimationConfig?)value },
        nameof(Element.ImplicitTransitions) => Button("Go") with { ImplicitTransitions = (ImplicitTransitions?)value },
        nameof(Element.ThemeTransitions) => Button("Go") with { ThemeTransitions = (ThemeTransitions?)value },
        nameof(Element.InteractionStates) => Button("Go") with { InteractionStates = (InteractionStatesConfig?)value },
        nameof(Element.KeyframeAnimations) => Button("Go") with { KeyframeAnimations = (KeyframeEntry[]?)value },
        nameof(Element.StaggerConfig) => Button("Go") with { StaggerConfig = (StaggerConfig?)value },
        nameof(Element.ElementTransition) => Button("Go") with { ElementTransition = (ElementTransition?)value },
        nameof(Element.ConnectedAnimationKey) => Button("Go") with { ConnectedAnimationKey = (string?)value },
        _ => throw new ArgumentOutOfRangeException(nameof(property)),
    };
}
