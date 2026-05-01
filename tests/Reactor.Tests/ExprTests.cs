using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 033 §5 — <c>Expr(Func&lt;Element?&gt;)</c> escape hatch for inline
/// block-expression bodies inside a DSL tree.
/// </summary>
public class ExprTests
{
    [Fact]
    public void Expr_Returns_LambdaResult_When_NonNull()
    {
        var produced = TextBlock("hello");
        var result = Expr(() => produced);
        Assert.Same(produced, result);
    }

    [Fact]
    public void Expr_Returns_EmptyElement_When_LambdaReturnsNull()
    {
        var result = Expr(() => null);
        Assert.Same(EmptyElement.Instance, result);
    }

    [Fact]
    public void Expr_Throws_ArgumentNullException_For_NullRender()
    {
        Assert.Throws<ArgumentNullException>("render", () => Expr(null!));
    }

    [Fact]
    public void Expr_Propagates_Exceptions_From_Lambda_Without_Wrapping()
    {
        var sentinel = new InvalidOperationException("from-lambda");
        var thrown = Assert.Throws<InvalidOperationException>(() =>
            Expr(() => throw sentinel));
        Assert.Same(sentinel, thrown);
    }

    [Fact]
    public void Expr_Is_Pure_Composition_NoBoundary()
    {
        // Expr returns the same Element instance the lambda produced,
        // not a wrapper Element. (Spec: "no node, no hook scope, no
        // memoization. Identical to inlining the lambda yourself.")
        var inner = TextBlock("x");
        var outer = Expr(() => inner);
        Assert.Same(inner, outer);
        Assert.IsType<TextBlockElement>(outer);
    }

    [Fact]
    public void Expr_Locals_Captured_By_Lambda_Do_Not_Leak_Across_Invocations()
    {
        // The lambda runs exactly once per Expr call: a captured counter
        // increments by 1, never more. Documents that Expr does not pin
        // the lambda for replay.
        var calls = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            Expr(() => { calls.Add(i); return null; });
        }
        Assert.Equal(new[] { 0, 1, 2 }, calls);
    }
}
