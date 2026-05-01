using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 033 §6 — declarative <c>SystemBackdrop</c> modifier. These tests cover
/// the modifier-storage and value-equality surface; reconciler-side application
/// to a <c>Window</c> is exercised by the self-host suite (it requires a real
/// host). The materializer mapping is tested via <c>internals visible</c>
/// elsewhere; here we confirm that two equal kinds round-trip equal records
/// (so the applier's diff sees them as no-change).
/// </summary>
public class BackdropModifierTests
{
    [Fact]
    public void Backdrop_Modifier_Stores_Kind_On_Element_Modifiers()
    {
        var el = VStack().Backdrop(BackdropKind.Mica);
        Assert.NotNull(el.Modifiers?.Backdrop);
        Assert.Equal(BackdropKind.Mica, el.Modifiers!.Backdrop!.Kind);
        Assert.Null(el.Modifiers.Backdrop.Factory);
    }

    [Fact]
    public void Backdrop_Modifier_Stores_Factory_On_Element_Modifiers()
    {
        Func<SystemBackdrop?> factory = () => null;
        var el = VStack().Backdrop(factory);
        Assert.NotNull(el.Modifiers?.Backdrop);
        Assert.Same(factory, el.Modifiers!.Backdrop!.Factory);
        Assert.Null(el.Modifiers.Backdrop.Kind);
    }

    [Fact]
    public void Backdrop_Modifier_Throws_On_Null_Element()
    {
        Element? el = null;
        Assert.Throws<ArgumentNullException>(() => el!.Backdrop(BackdropKind.Mica));
    }

    [Fact]
    public void Backdrop_Modifier_Throws_On_Null_Factory()
    {
        var el = VStack();
        Assert.Throws<ArgumentNullException>(() => el.Backdrop((Func<SystemBackdrop?>)null!));
    }

    [Fact]
    public void Backdrop_Same_Kind_Twice_Compares_Equal()
    {
        var a = BackdropChoice.Of(BackdropKind.Mica);
        var b = BackdropChoice.Of(BackdropKind.Mica);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Backdrop_Different_Kinds_Compare_NotEqual()
    {
        var a = BackdropChoice.Of(BackdropKind.Mica);
        var b = BackdropChoice.Of(BackdropKind.DesktopAcrylic);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Backdrop_Factory_Choice_Equality_Is_Reference_Based()
    {
        Func<SystemBackdrop?> f = () => null;
        var a = BackdropChoice.Of(f);
        var b = BackdropChoice.Of(f);
        Assert.Equal(a, b); // same delegate reference
        var c = BackdropChoice.Of(() => null); // different delegate
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Backdrop_Modifier_Replacing_Same_Kind_Keeps_Modifiers_Equal()
    {
        var a = VStack().Backdrop(BackdropKind.Mica);
        var b = VStack().Backdrop(BackdropKind.Mica);
        // Modifiers records compare by value — the diff path used by the host's
        // applier (kind == kind) sees no change, exercised by the applier tests.
        Assert.Equal(a.Modifiers!.Backdrop, b.Modifiers!.Backdrop);
    }
}
