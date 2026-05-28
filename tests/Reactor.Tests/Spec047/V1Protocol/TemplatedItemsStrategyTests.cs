using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec047.V1Protocol;

/// <summary>
/// Spec 047 §14 Phase 3 close-out — <see cref="TemplatedItems{TItem,TElement,TControl}"/>
/// shape tests.
///
/// <para>End-to-end binding behavior (realization, keyed diff, container
/// refresh) requires a live WinUI dispatcher and a realized
/// <c>ListView</c>; that is covered by <c>Reactor.AppTests.Host</c> self-test
/// fixtures landing alongside the G2 ListView&lt;T&gt;/GridView&lt;T&gt;
/// descriptor port. The xUnit surface here exercises the public shape and
/// the non-generic <see cref="ITemplatedItemsStrategy"/> dispatch contract
/// the engine relies on.</para>
/// </summary>
public class TemplatedItemsStrategyTests
{
    private record TestEl(IReadOnlyList<string> Items) : Element;

    private static readonly Func<string, int, string> IdentityKey = static (s, _) => s;
    private static readonly Func<string, int, Element> StubBuilder = static (s, _) => new TestEl(global::System.Array.Empty<string>());

    [Fact]
    public void TemplatedItems_Record_Carries_Lambdas()
    {
        var strategy = new TemplatedItems<string, TestEl, FrameworkElement>(
            GetItems: el => el.Items,
            KeySelector: IdentityKey,
            BuildItemView: StubBuilder);

        var el = new TestEl(new[] { "a", "b", "c" });
        var items = strategy.GetItems(el);
        Assert.Equal(3, items.Count);
        Assert.Equal("a", items[0]);
        Assert.Equal("b", strategy.KeySelector("b", 1));
        Assert.NotNull(strategy.BuildItemView("anything", 0));
    }

    [Fact]
    public void TemplatedItems_Implements_NonGeneric_Marker()
    {
        // The dispatcher in V1HandlerAdapter routes via this interface
        // because the closed-(TElement,TControl) generic context cannot
        // pattern-match an open TItem positional case.
        var strategy = new TemplatedItems<string, TestEl, FrameworkElement>(
            GetItems: el => el.Items,
            KeySelector: IdentityKey,
            BuildItemView: StubBuilder);
        Assert.IsAssignableFrom<ITemplatedItemsStrategy>(strategy);
        Assert.IsAssignableFrom<ChildrenStrategy<TestEl, FrameworkElement>>(strategy);
    }

    [Fact]
    public void TemplatedItems_Record_Equality_Holds_For_Same_Lambdas()
    {
        // Strategy records participate in OwnPropsEqual fast paths — two
        // strategies with the same delegate references must compare equal.
        var get = (Func<TestEl, IReadOnlyList<string>>)(el => el.Items);
        var a = new TemplatedItems<string, TestEl, FrameworkElement>(get, IdentityKey, StubBuilder);
        var b = new TemplatedItems<string, TestEl, FrameworkElement>(get, IdentityKey, StubBuilder);
        Assert.Equal(a, b);
    }

    [Fact]
    public void TemplatedItems_With_Distinct_Delegates_Compare_Unequal()
    {
        var a = new TemplatedItems<string, TestEl, FrameworkElement>(
            el => el.Items, IdentityKey, StubBuilder);
        var b = new TemplatedItems<string, TestEl, FrameworkElement>(
            el => el.Items, IdentityKey, StubBuilder); // distinct lambda instance
        // Different lambda instances → different delegates → records unequal.
        // This is the same record-equality contract as Panel<> et al.
        Assert.NotEqual(a, b);
    }
}
