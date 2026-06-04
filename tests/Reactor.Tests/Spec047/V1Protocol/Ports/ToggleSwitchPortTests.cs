using System;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec047.V1Protocol.Ports;

/// <summary>
/// Spec 047 §14 Phase 1 (1.11) — ToggleSwitch port tests.
///
/// <para>The behavior-fires-the-event tests require a WinUI dispatcher and
/// live in <c>tests/Reactor.AppTests.Host/SelfTest/Fixtures/Spec047V1ProtocolFixtures.cs</c>.
/// Here we exercise the registration contract: spec 048 §3.4 makes the
/// built-ins lazy-globally-registered — touching the factory (or the
/// test-only <c>BuiltInHandlerBootstrap</c> module initializer) installs the
/// handler in the process-wide <see cref="V1Protocol.ControlRegistry"/>.</para>
/// </summary>
public class ToggleSwitchPortTests
{
    [Fact]
    public void BuiltIn_ToggleSwitchDescriptor_In_Global_Registry()
    {
        // Spec 050 — the test-only BuiltInHandlerBootstrap module initializer
        // now registers ToggleSwitch through the descriptor-backed handler.
        // Production code reaches the same state by calling the factory.
        Assert.True(Microsoft.UI.Reactor.Core.V1Protocol.ControlRegistry.TryResolve(
            typeof(ToggleSwitchElement), out _));
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
