using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Handlers;
using Microsoft.UI.Reactor.Hosting;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec047.V1Protocol.Ports;

/// <summary>
/// Spec 047 §14 Phase 1 (1.11–1.15) — V1 registration cross-cutting tests.
///
/// <para>V1 is the unconditional production path, so every fresh
/// <see cref="Reconciler"/> automatically registers the built-in handlers and
/// the Xaml interop bridges. These tests assert that registration shape: the
/// registry exposes the built-ins (re-registration throws per §13 Q17) and the
/// XamlHost/XamlPage bridges are owned by V1 auto-registration.</para>
/// </summary>
public class V1OnRegistrationTests
{
    [Fact]
    public void Ctor_Registers_All_Five_Handlers()
    {
        var rec = new Reconciler();

        // Each of the five built-in handlers should have been registered
        // by the ctor. A second registration must throw.
        Assert.Throws<InvalidOperationException>(() =>
            rec.RegisterHandler<ToggleSwitchElement, Microsoft.UI.Xaml.Controls.ToggleSwitch>(
                new ToggleSwitchHandler()));
        Assert.Throws<InvalidOperationException>(() =>
            rec.RegisterHandler<SliderElement, Microsoft.UI.Xaml.Controls.Slider>(
                new SliderHandler()));
        Assert.Throws<InvalidOperationException>(() =>
            rec.RegisterHandler<TextBoxElement, Microsoft.UI.Xaml.Controls.TextBox>(
                new TextBoxHandler()));
        Assert.Throws<InvalidOperationException>(() =>
            rec.RegisterHandler<BorderElement, Microsoft.UI.Xaml.Controls.Border>(
                new BorderHandler()));
        Assert.Throws<InvalidOperationException>(() =>
            rec.RegisterHandler<ListViewElement, Microsoft.UI.Xaml.Controls.ListView>(
                new ListViewHandler()));
    }

    [Fact]
    public void Ctor_Auto_Registers_Xaml_Interop_Bridges()
    {
        // Spec 047 §14 Phase 4 (4.0.5): XamlHost/XamlPage are owned by V1
        // auto-registration, so a fresh reconciler routes them through V1
        // without the app calling XamlInterop.Register.
        var rec = new Reconciler();
        Assert.True(rec.IsElementTypeRegistered(typeof(XamlPageElement)));
        Assert.True(rec.IsElementTypeRegistered(typeof(XamlHostElement)));
    }

    [Fact]
    public void XamlInterop_Register_Does_Not_Clash()
    {
        // Spec 047 §14 Phase 4 (4.0.5): XamlInterop.Register stays a safe public
        // API — it skips the already-owned types instead of tripping the §13 Q17
        // duplicate-registration guard.
        var rec = new Reconciler();
        var ex = Record.Exception(() => XamlInterop.Register(rec));
        Assert.Null(ex);
    }
}
