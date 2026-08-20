using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Pins the invariants that make the shared <see cref="EmptyElement.Instance"/>
/// singleton safe to alias — it is returned by <c>Empty()</c> and by the empty
/// arms of <c>When</c> / <c>If</c> / <c>Expr</c>, so a single instance routinely
/// appears at many positions in one tree and in the same position across renders.
///
/// <para>Aliasing an <see cref="Element"/> is only dangerous when the instance can
/// acquire per-instance identity: a mounted control, a <c>ReactorState</c>
/// (<c>ReactorAttached.StateProperty</c>), a pool entry, a component node, or a
/// child-reconciler key. <see cref="EmptyElement"/> acquires none of those, because
/// <c>Reconciler.Mount</c> — the single choke point from element to control — maps it
/// to <c>null</c> unconditionally, and every children path filters it out first.
/// These tests fail if either half of that invariant regresses (e.g. someone
/// registers a control handler for <see cref="EmptyElement"/>, or drops the filter),
/// which is exactly when the singleton would have to become per-call.</para>
/// </summary>
public class EmptyElementAliasingTests
{
    private static void NoOp() { }

    [Fact]
    public void Empty_Returns_The_Shared_Singleton()
    {
        Assert.Same(EmptyElement.Instance, Empty());
        Assert.Same(Empty(), Empty());
    }

    [Fact]
    public void When_If_And_Expr_All_Share_That_Same_Empty_Arm()
    {
        Assert.Same(EmptyElement.Instance, When(false, () => TextBlock("x")));
        Assert.Same(EmptyElement.Instance, If(false, () => TextBlock("x")));
        Assert.Same(EmptyElement.Instance, Expr(() => null));
    }

    [Fact]
    public void Mounting_The_Singleton_Never_Produces_A_Control()
    {
        // Mount is the only element -> control transition. Returning null here is
        // what makes aliasing safe: with no control there is no ReactorState to
        // share, nothing to pool, and no component node to collide.
        var reconciler = new Reconciler();

        Assert.Null(reconciler.Mount(Empty(), NoOp));
        Assert.Null(reconciler.Mount(Empty(), NoOp)); // same instance, mounted twice
    }

    [Fact]
    public void Reconciling_One_Aliased_Singleton_Against_Itself_Produces_No_Control()
    {
        var reconciler = new Reconciler();

        Assert.Null(reconciler.Reconcile(Empty(), Empty(), null, NoOp));
    }

    [Fact]
    public void Modifiers_Copy_The_Singleton_Rather_Than_Mutating_It()
    {
        var modified = Empty().Margin(16).WithKey("k");

        Assert.NotSame(EmptyElement.Instance, modified);
        Assert.Null(EmptyElement.Instance.Modifiers);
        Assert.Null(EmptyElement.Instance.Key);
    }

    [Fact]
    public void ShallowEquals_Cannot_Distinguish_The_Singleton_From_A_Fresh_Instance()
    {
        // The reconciler's element-level skip gate is structural, not reference
        // based, so handing out the singleton instead of a fresh EmptyElement
        // changes no reconciliation decision.
        Assert.True(Element.ShallowEquals(EmptyElement.Instance, new EmptyElement()));
        Assert.True(Element.ShallowEquals(new EmptyElement(), new EmptyElement()));
    }

    [Fact]
    public void The_Same_Singleton_At_Several_Sibling_Positions_Collapses_Away()
    {
        // Two aliased references in one child list must not confuse child
        // filtering — both are dropped, leaving only the real child.
        var stack = VStack(Empty(), TextBlock("a"), Empty());

        Assert.Single(stack.Children);
        Assert.IsType<TextBlockElement>(stack.Children[0]);
    }
}
