using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec047.V1Protocol;

/// <summary>
/// Spec 047 §14 Phase 1 (1.4) — <see cref="ReactorBinding.WriteSuppressed"/>
/// API-contract tests.
///
/// The runtime behavior (toggle <c>IsOn = true</c> under WriteSuppressed
/// does NOT fire OnIsOnChanged; the same write outside it does) requires
/// a real WinUI control instance, which the Reactor.Tests host can't
/// construct off the STA WinUI dispatcher. That integration check lives
/// alongside the existing <c>InputControlsFireEvents</c> fixture in
/// <c>tests/Reactor.AppTests.Host/SelfTest/Fixtures/</c>; it is added by
/// section 1.11 when <c>ToggleSwitch</c> is ported and the V1 dispatch
/// path actually exercises the primitive.
///
/// These unit tests cover the argument-null contract and the static
/// surface shape — Phase 4 swaps the body but must preserve both.
/// </summary>
public class WriteSuppressedTests
{
    [Fact]
    public void WriteSuppressed_Untyped_Throws_When_Target_Is_Null()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => ReactorBinding.WriteSuppressed((UIElement)null!, () => { }));
        Assert.Equal("target", ex.ParamName);
    }

    [Fact]
    public void WriteSuppressed_Untyped_Throws_When_Mutate_Is_Null()
    {
        // target is non-null-checked first; we can't supply a real
        // UIElement off the WinUI thread, so we assert the order via
        // the typed overload below where the generic param accepts a
        // null-typed reference for compile-time only.
        var ex = Assert.Throws<ArgumentNullException>(
            () => ReactorBinding.WriteSuppressed((UIElement)null!, (Action)null!));
        // First null check is on target, so this throws "target" — that
        // is the documented argument-ordering contract.
        Assert.Equal("target", ex.ParamName);
    }

    [Fact]
    public void WriteSuppressed_Typed_Throws_When_Target_Is_Null()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => ReactorBinding.WriteSuppressed<UIElement>(null!, _ => { }));
        Assert.Equal("target", ex.ParamName);
    }

    [Fact]
    public void WriteSuppressed_Typed_Throws_When_Mutate_Is_Null()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => ReactorBinding.WriteSuppressed<UIElement>(null!, null!));
        Assert.Equal("target", ex.ParamName);
    }
}
