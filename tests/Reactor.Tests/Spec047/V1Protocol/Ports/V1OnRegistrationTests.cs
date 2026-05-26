using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Handlers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec047.V1Protocol.Ports;

/// <summary>
/// Spec 047 §14 Phase 1 (1.11–1.15) — V1-flag-ON cross-cutting tests.
///
/// <para>Validates that with the global <c>Reactor.UseV1Protocol</c>
/// switch ON, fresh reconcilers automatically register the five ported
/// handlers, the registry exposes them via re-registration throws, and
/// switching the flag OFF restores the legacy-only registration shape.</para>
///
/// <para>Both tests mutate the process-wide <see cref="AppContext"/>
/// switch — xUnit's default parallel scheduler races them against each
/// other and against any other test that touches the same switch. Pin
/// them to a non-parallel collection so the switch transitions are
/// serial.</para>
/// </summary>
[CollectionDefinition(nameof(Spec047V1FlagCollection), DisableParallelization = true)]
public sealed class Spec047V1FlagCollection { }

[Collection(nameof(Spec047V1FlagCollection))]
public class V1OnRegistrationTests
{
    private const string SwitchName = "Reactor.UseV1Protocol";

    [Fact]
    public void AppContext_Switch_On_Registers_All_Five_Handlers()
    {
        AppContext.TryGetSwitch(SwitchName, out var prev);
        AppContext.SetSwitch(SwitchName, true);
        try
        {
            var rec = new Reconciler();
            Assert.True(rec.UseV1Protocol);

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
        finally
        {
            AppContext.SetSwitch(SwitchName, prev);
        }
    }

    [Fact]
    public void AppContext_Switch_Off_Skips_Built_In_Registration()
    {
        AppContext.SetSwitch(SwitchName, false);
        var rec = new Reconciler();
        Assert.False(rec.UseV1Protocol);
        // With the flag OFF, the ctor doesn't auto-register — so a manual
        // registration of one of the built-in element types is allowed.
        rec.RegisterHandler<ToggleSwitchElement, Microsoft.UI.Xaml.Controls.ToggleSwitch>(
            new ToggleSwitchHandler());
    }
}
