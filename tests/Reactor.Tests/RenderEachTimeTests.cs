using System;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 033 §4 — <c>RenderEachTime(ctx => …)</c> is the explicit replacement for
/// the obsolete <c>Func(ctx => …)</c>: an inline function component with its own
/// hook scope that re-renders on every parent render. These tests live at the
/// surface layer; full reconciler-driven re-render assertions are covered by
/// the existing memoization self-host tests.
/// </summary>
public class RenderEachTimeTests
{
    [Fact]
    public void RenderEachTime_Produces_FuncElement()
    {
        var el = RenderEachTime(_ => TextBlock("hi"));
        Assert.NotNull(el);
        Assert.IsType<FuncElement>(el);
    }

    [Fact]
    public void RenderEachTime_Captures_Render_Delegate()
    {
        // The factory does not invoke the lambda eagerly — it stores it for
        // the reconciler to call during render. Verifies the delegate is
        // captured but not yet executed.
        bool invoked = false;
        var el = RenderEachTime(_ => { invoked = true; return TextBlock("x"); });
        Assert.False(invoked);
        Assert.NotNull(el);
    }

#pragma warning disable CS0618 // Validating that obsolete Func and RenderEachTime produce equivalent element shapes
    [Fact]
    public void Func_And_RenderEachTime_Produce_Same_Element_Type()
    {
        var f = Func(_ => TextBlock("x"));
        var r = RenderEachTime(_ => TextBlock("x"));
        Assert.Equal(f.GetType(), r.GetType());
    }
#pragma warning restore CS0618
}
