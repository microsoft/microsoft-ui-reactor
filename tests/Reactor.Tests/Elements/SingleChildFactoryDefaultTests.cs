using System.Reflection;
using Microsoft.UI.Reactor.Core;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.Elements;

/// <summary>
/// The single-child container factories whose leading <see cref="Element"/>
/// parameter is optional, so each is callable with zero arguments —
/// <c>Border()</c>, <c>ScrollViewer()</c>, … Every one of them maps to a WinUI
/// control whose content/child slot is itself nullable (an empty
/// <c>Border</c> is a divider, an empty <c>ItemContainer</c> is a placeholder
/// cell), and the descriptor-driven <c>SingleContent</c> dispatch already
/// null-guards mount, update, and unmount.
///
/// <para>Deliberately excluded (see the accompanying "still required" test):
/// factories whose leading element is the decorator's anchor
/// (<c>MenuFlyout</c>, <c>CommandBarFlyout</c>), factories with a second
/// required element (<c>Flyout</c>, <c>SemanticZoom</c>, <c>ErrorBoundary</c>),
/// and <c>Popup</c> / <c>ContentFlyout</c>, whose hand-coded mount paths
/// dereference the child without a null check.</para>
/// </summary>
public class SingleChildFactoryDefaultTests
{
    public static TheoryData<string> OptionalLeadingElement => new()
    {
        "Border", "ScrollViewer", "ScrollView", "Viewbox", "ItemContainer",
        "RefreshContainer", "SwipeControl", "ParallaxView", "Card",
    };

    // Negative control for the probe below: these factories keep a REQUIRED
    // leading Element, so `HasDefaultValue` is not trivially true for every
    // Element-typed parameter in Factories.
    public static TheoryData<string> RequiredLeadingElement => new()
    {
        "Flyout", "SemanticZoom", "MenuFlyout", "Popup", "ContentFlyout",
    };

    [Theory]
    [MemberData(nameof(OptionalLeadingElement))]
    public void LeadingElementParameter_IsOptional_AndDefaultsToNull(string factory)
    {
        var first = LeadingParameter(factory);

        Assert.Equal(typeof(Element), first.ParameterType);
        Assert.True(first.HasDefaultValue,
            $"Factories.{factory}'s '{first.Name}' parameter must keep its `= null` default — "
            + "without it the factory is not callable with zero arguments.");
        Assert.Null(first.DefaultValue);
    }

    [Theory]
    [MemberData(nameof(RequiredLeadingElement))]
    public void LeadingElementParameter_StaysRequired(string factory)
    {
        var first = LeadingParameter(factory);

        Assert.Equal(typeof(Element), first.ParameterType);
        Assert.False(first.HasDefaultValue,
            $"Factories.{factory} takes its leading element as the thing it decorates or "
            + "mounts unguarded — it must not silently become zero-argument callable.");
    }

    [Fact]
    public void ZeroArgumentCall_LeavesTheChildSlotEmpty()
    {
        Assert.Null(Border().Child);
        Assert.Null(ScrollViewer().Child);
        Assert.Null(ScrollView().Child);
        Assert.Null(Viewbox().Child);
        Assert.Null(ItemContainer().Child);
        Assert.Null(ParallaxView().Child);
        Assert.Null(RefreshContainer().Content);
        Assert.Null(SwipeControl().Content);
        Assert.Null(Card().Child);
    }

    // Positive control for the assertions above: the same slots are populated
    // when a child IS supplied, so `Assert.Null` there measures the default
    // rather than a slot that is null no matter what.
    [Fact]
    public void ExplicitChild_StillReachesTheChildSlot()
    {
        var child = TextBlock("x");

        Assert.Same(child, Border(child).Child);
        Assert.Same(child, ScrollViewer(child).Child);
        Assert.Same(child, ScrollView(child).Child);
        Assert.Same(child, Viewbox(child).Child);
        Assert.Same(child, ItemContainer(child).Child);
        Assert.Same(child, ParallaxView(child).Child);
        Assert.Same(child, RefreshContainer(child).Content);
        Assert.Same(child, SwipeControl(child).Content);
        Assert.Same(child, Card(child).Child);
    }

    // A zero-argument factory is exactly the shape that invites caching a
    // singleton. These records expose `init` accessors, so a shared instance
    // would let one caller's initializer leak into every other call site.
    [Fact]
    public void ZeroArgumentCall_ReturnsAFreshInstanceEachTime()
    {
        Assert.NotSame(Border(), Border());
        Assert.NotSame(ScrollViewer(), ScrollViewer());
        Assert.NotSame(ScrollView(), ScrollView());
        Assert.NotSame(Viewbox(), Viewbox());
        Assert.NotSame(ItemContainer(), ItemContainer());
        Assert.NotSame(ParallaxView(), ParallaxView());
        Assert.NotSame(RefreshContainer(), RefreshContainer());
        Assert.NotSame(SwipeControl(), SwipeControl());
        Assert.NotSame(Card(), Card());
    }

    // Trailing options keep working when the leading child is omitted — the
    // whole point of the shape is `RefreshContainer(onRefreshRequested: ...)`.
    [Fact]
    public void TrailingOptions_BindByName_WithTheChildOmitted()
    {
        global::System.Action noop = static () => { };
        var items = new[] { new SwipeItemData("Delete") };

        Assert.Same(noop, RefreshContainer(onRefreshRequested: noop).OnRefreshRequested);
        Assert.Equal(12, ParallaxView(verticalShift: 12).VerticalShift);
        Assert.Same(items, SwipeControl(leftItems: items).LeftItems);
    }

    private static ParameterInfo LeadingParameter(string factory)
    {
        var method = typeof(Factories).GetMethod(
            factory, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        return method!.GetParameters()[0];
    }
}
