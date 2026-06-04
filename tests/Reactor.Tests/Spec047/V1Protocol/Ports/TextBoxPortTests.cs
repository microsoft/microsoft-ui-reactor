using System;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec047.V1Protocol.Ports;

/// <summary>
/// Spec 047 §14 Phase 1 (1.13) — TextBox port tests.
/// </summary>
public class TextBoxPortTests
{
    [Fact]
    public void BuiltIn_TextBoxDescriptor_In_Global_Registry()
    {
        // Spec 050 — test-only BuiltInHandlerBootstrap module initializer
        // now registers TextBox through the descriptor-backed handler.
        Assert.True(Microsoft.UI.Reactor.Core.V1Protocol.ControlRegistry.TryResolve(
            typeof(TextBoxElement), out _));
    }

    [Fact(Skip = "Requires WinUI dispatcher; covered in AppTests.Host SelfTest/Fixtures/Spec047V1ProtocolFixtures.cs (1.13)")]
    public void Typing_Fires_OnChanged_Once()
    {
        // TODO(AppTests.Host): simulate typing → OnChanged fires with new text.
    }

    [Fact(Skip = "Requires WinUI dispatcher; covered in AppTests.Host SelfTest/Fixtures/Spec047V1ProtocolFixtures.cs (1.13)")]
    public void Programmatic_Text_Write_Does_Not_Round_Trip()
    {
        // TODO(AppTests.Host): reconcile with new Value → OnChanged does NOT fire.
    }
}
