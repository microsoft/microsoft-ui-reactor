using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 033 §3 — typed <see cref="ElementRef{T}"/> wrapper, hook, and modifier
/// overload. Mount-time behavior (population, type-mismatch debug-fail) is
/// covered by self-host fixtures; these tests cover the headless-safe
/// surface: identity, conversions, and hook stability.
/// </summary>
public class TypedElementRefTests
{
    [Fact]
    public void TypedRef_Records_ExpectedType_On_InnerRef()
    {
        var typed = TypedElementRef.Create<Button>();
        ElementRef inner = typed; // implicit conversion
        Assert.Equal(typeof(Button), inner.ExpectedType);
    }

    [Fact]
    public void TypedRef_Current_Is_Null_Before_Mount()
    {
        var typed = TypedElementRef.Create<Button>();
        Assert.Null(typed.Current);
    }

    // Mount-time population behavior (Current returns the typed cast when the
    // inner is populated; null when populated with the wrong type) is exercised
    // in self-host fixtures because instantiating concrete WinUI controls
    // requires a XAML host. The headless surface below covers the remainder.

    [Fact]
    public void TypedRef_ImplicitConversion_To_Untyped_Returns_Inner()
    {
        var typed = TypedElementRef.Create<Button>();
        ElementRef inner1 = typed;
        ElementRef inner2 = typed;
        // Implicit conversion does not allocate a new ElementRef each time.
        Assert.Same(inner1, inner2);
    }

    [Fact]
    public void TypedRef_ImplicitConversion_From_Null_Throws()
    {
        ElementRef<Button>? nullRef = null;
        Assert.Throws<ArgumentNullException>(() => { ElementRef _ = nullRef!; });
    }

    [Fact]
    public void TypedRef_ToString_Is_Diagnostic_Friendly()
    {
        var typed = TypedElementRef.Create<TextBox>();
        Assert.Equal("ElementRef<TextBox>", typed.ToString());
    }

    [Fact]
    public void UseElementRef_Returns_Stable_Instance_Across_Renders()
    {
        var ctx = new RenderContext();

        ctx.BeginRender(() => { });
        var ref1 = ctx.UseElementRef<Button>();

        ctx.BeginRender(() => { });
        var ref2 = ctx.UseElementRef<Button>();

        Assert.Same(ref1, ref2);
    }

    [Fact]
    public void UseElementRef_InnerRef_Is_Also_Stable_Across_Renders()
    {
        var ctx = new RenderContext();

        ctx.BeginRender(() => { });
        ElementRef inner1 = ctx.UseElementRef<Button>();

        ctx.BeginRender(() => { });
        ElementRef inner2 = ctx.UseElementRef<Button>();

        // Reconciler populates the inner; the inner identity must survive
        // re-renders so the populated _current is observed by every subsequent
        // render's typed projection.
        Assert.Same(inner1, inner2);
    }

    [Fact]
    public void UseElementRef_Two_Calls_In_Same_Component_Yield_Distinct_Refs()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var a = ctx.UseElementRef<Button>();
        var b = ctx.UseElementRef<TextBox>();
        Assert.NotSame((object)a, (object)b);
        ElementRef innerA = a;
        ElementRef innerB = b;
        Assert.NotSame(innerA, innerB);
    }

    [Fact]
    public void Ref_Modifier_Typed_Overload_Stores_Inner_Untyped_Ref()
    {
        var typed = TypedElementRef.Create<Button>();
        // Use TextBlock factory: Button factory parameter doesn't construct
        // a real control here (it just returns an Element), but TextBlock is
        // closer to a no-side-effect path.
        var el = Microsoft.UI.Reactor.Factories.TextBlock("hi").Ref(typed);
        // Modifier carries the *inner* untyped ref so the reconciler's existing
        // mount path keeps working. ExpectedType then drives the DEBUG assertion.
        Assert.NotNull(el.Modifiers?.Ref);
        Assert.Equal(typeof(Button), el.Modifiers!.Ref!.ExpectedType);
    }

    [Fact]
    public void Untyped_Ref_ExpectedType_Is_Null()
    {
        var untyped = new ElementRef();
        Assert.Null(untyped.ExpectedType);
    }
}
