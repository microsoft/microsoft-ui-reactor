using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Core.V1Protocol.Handlers;
using Microsoft.UI.Reactor.Hosting;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec047.V1Protocol.Ports;

/// <summary>
/// Spec 048 §3.4 — V1 registration cross-cutting tests.
///
/// <para>The Reconciler no longer eagerly registers built-in handlers; each
/// handler installs itself in the global <see cref="ControlRegistry"/> on
/// first factory touch (per-control <c>Reg&lt;&gt;</c> / <c>RegDecorator&lt;&gt;</c>
/// cctor latch), or on explicit <see cref="ControlRegistry.Register{TElement, TControl}(System.Func{IElementHandler{TElement, TControl}})"/>.
/// In the test environment, <c>tests/_shared/BuiltInHandlerBootstrap.cs</c>
/// runs as a <c>[ModuleInitializer]</c> and touches every built-in shim
/// before any test runs.</para>
/// </summary>
public class V1OnRegistrationTests
{
    [Fact]
    public void Global_Registry_Has_All_Five_Builtins()
    {
        Assert.True(ControlRegistry.TryResolve(typeof(ToggleSwitchElement), out _));
        Assert.True(ControlRegistry.TryResolve(typeof(SliderElement), out _));
        Assert.True(ControlRegistry.TryResolve(typeof(TextBoxElement), out _));
        Assert.True(ControlRegistry.TryResolve(typeof(BorderElement), out _));
        Assert.True(ControlRegistry.TryResolve(typeof(ListViewElement), out _));
    }

    [Fact]
    public void Global_Registry_Has_Xaml_Interop_Bridges()
    {
        // XamlPageElement / XamlHostElement install themselves into the
        // global ControlRegistry from their static ctors (see XamlInterop.cs).
        // The test bootstrap explicitly primes these entries — note that
        // a bare `typeof(XamlPageElement)` is NOT sufficient to trigger
        // the type initializer; only constructing an instance or
        // accessing a static member does. The assertion below passes
        // because of the bootstrap-driven registration, not the typeof.
        Assert.True(ControlRegistry.TryResolve(typeof(XamlPageElement), out _));
        Assert.True(ControlRegistry.TryResolve(typeof(XamlHostElement), out _));
    }

    [Fact]
    public void XamlInterop_Register_Does_Not_Clash()
    {
        // Spec 048 §3.4: XamlInterop.Register stays a safe public API — its
        // IsElementTypeRegistered guard skips already-owned types instead of
        // tripping the §13 Q17 duplicate-registration guard. Per-host
        // _typeRegistry / _v1Handlers are empty on a fresh reconciler, so
        // Register populates _typeRegistry (precedence arm 2) — the global
        // ControlRegistry entry (precedence arm 3) stays as a no-op fallback.
        var rec = new Reconciler();
        var ex = Record.Exception(() => XamlInterop.Register(rec));
        Assert.Null(ex);
    }
}

