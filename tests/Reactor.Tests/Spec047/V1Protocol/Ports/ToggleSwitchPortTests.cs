using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Handlers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec047.V1Protocol.Ports;

/// <summary>
/// Spec 047 §14 Phase 1 (1.11) — ToggleSwitch port tests.
///
/// <para>The behavior-fires-the-event tests require a WinUI dispatcher and
/// live in <c>tests/Reactor.AppTests.Host/SelfTest/Fixtures/Spec047V1ProtocolFixtures.cs</c>.
/// Here we exercise the registration contract:</para>
/// <list type="bullet">
///   <item>The handler registers automatically when <c>UseV1Protocol = true</c>.</item>
///   <item>Re-registering on the same flag-ON reconciler throws (Q17 dedupe).</item>
///   <item>With the flag OFF, no V1 handler is registered (legacy path stays).</item>
/// </list>
/// </summary>
public class ToggleSwitchPortTests
{
    [Fact]
    public void Flag_On_Registers_ToggleSwitchHandler_Automatically()
    {
        var rec = new Reconciler(logger: null, useV1Protocol: true);
        Assert.True(rec.UseV1Protocol);
        // Trying to register the same element type a second time must throw
        // because the ctor already registered it.
        Assert.Throws<InvalidOperationException>(
            () => rec.RegisterHandler<ToggleSwitchElement, Microsoft.UI.Xaml.Controls.ToggleSwitch>(
                new ToggleSwitchHandler()));
    }

    [Fact]
    public void Flag_Off_Skips_Built_In_Registration()
    {
        var rec = new Reconciler(logger: null, useV1Protocol: false);
        Assert.False(rec.UseV1Protocol);
        // Built-in registration is gated on the flag — when OFF, the v1
        // dispatch is skipped entirely (the legacy MountToggleSwitch handles
        // it). Manual RegisterHandler still works as the public author surface,
        // but it has no effect at dispatch time on a flag-OFF reconciler.
        rec.RegisterHandler<ToggleSwitchElement, Microsoft.UI.Xaml.Controls.ToggleSwitch>(
            new ToggleSwitchHandler());
        // No throw — first registration is allowed (flag-OFF ctor skipped the
        // automatic registration).
    }

    [Fact(Skip = "Requires WinUI dispatcher; covered in AppTests.Host SelfTest/Fixtures/Spec047V1ProtocolFixtures.cs (1.11)")]
    public void Mount_Through_V1_Path_Produces_Correct_IsOn()
    {
        // TODO(AppTests.Host): with the WinUI dispatcher available, mount a
        // ToggleSwitchElement(IsOn: true) through the V1 path, assert the
        // returned WinUI.ToggleSwitch has IsOn == true. Then reconcile with
        // IsOn: false and assert IsOn == false.
    }

    [Fact(Skip = "Requires WinUI dispatcher; covered in AppTests.Host SelfTest/Fixtures/Spec047V1ProtocolFixtures.cs (1.11)")]
    public void Set_Driven_Write_Has_Zero_Fire_Count()
    {
        // TODO(AppTests.Host): the §8.2 carve-out invariant —
        //   var el = new ToggleSwitchElement(IsOn: false, OnIsOnChanged: _ => fireCount++)
        //       .Set(ts => ts.IsOn = true);
        // Mount → fireCount == 0 (ApplySetters scope drops the echo).
    }
}
