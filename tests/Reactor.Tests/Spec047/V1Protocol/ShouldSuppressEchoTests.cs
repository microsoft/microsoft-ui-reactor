using System;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec047.V1Protocol;

/// <summary>
/// Spec 062 §14 — <see cref="ReactorBinding.ShouldSuppressEcho(UIElement)"/>
/// API-contract tests. This is the public read-side counterpart of
/// <see cref="ReactorBinding.WriteSuppressed(UIElement, Action)"/>, emitted by
/// the wrapper generator for a <c>[WrapControlled(Deferred = true)]</c> prop so a
/// deferred-controlled generated wrapper compiles against Reactor's public
/// surface alone (no <c>InternalsVisibleTo</c>).
///
/// <para>The runtime token/scope semantics (consume-once counter, non-consuming
/// setter scope) require a live WinUI control off the STA dispatcher, so they are
/// pinned headlessly on the internal <see cref="Reconciler.ReactorState"/> overload
/// by <c>ChangeEchoSuppressorStateTests</c> — which the public UIElement method
/// delegates to after a single attached-DP read — and end-to-end by the
/// <c>Spec062DeferredControlledFixtures</c> selftest against the external
/// generated wrapper. These unit tests cover the argument-null contract and the
/// frozen public ABI shape (§14 additive-only discipline).</para>
/// </summary>
public class ShouldSuppressEchoTests
{
    [Fact]
    public void ShouldSuppressEcho_Throws_When_Target_Is_Null()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => ReactorBinding.ShouldSuppressEcho((UIElement)null!));
        Assert.Equal("target", ex.ParamName);
    }

    // ── Stable-ABI guard (spec 062 §14) ──────────────────────────────────
    // The method is part of the frozen contract the wrapper generator's emitted
    // code binds to. If its visibility, static-ness, return type, or single
    // UIElement parameter ever change, generated deferred-controlled wrappers in
    // external assemblies break at compile time — pin the shape here so a
    // signature change is caught in Reactor's own unit suite first. (typeof does
    // not construct a WinUI object, so this stays headless-safe.)

    private static MethodInfo ShouldSuppressEchoMethod =>
        typeof(ReactorBinding).GetMethod(
            nameof(ReactorBinding.ShouldSuppressEcho),
            BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "ReactorBinding.ShouldSuppressEcho(UIElement) must remain a public static method (spec 062 §14 frozen ABI).");

    [Fact]
    public void ShouldSuppressEcho_Is_Public_Static()
    {
        var m = ShouldSuppressEchoMethod;
        Assert.True(m.IsPublic, "ShouldSuppressEcho must be public (frozen ABI).");
        Assert.True(m.IsStatic, "ShouldSuppressEcho must be static (frozen ABI).");
    }

    [Fact]
    public void ShouldSuppressEcho_Returns_Bool()
    {
        Assert.Equal(typeof(bool), ShouldSuppressEchoMethod.ReturnType);
    }

    [Fact]
    public void ShouldSuppressEcho_Takes_Single_UIElement_Parameter()
    {
        var ps = ShouldSuppressEchoMethod.GetParameters();
        var p = Assert.Single(ps);
        Assert.Equal(typeof(UIElement), p.ParameterType);
        Assert.Equal("target", p.Name);
    }
}
