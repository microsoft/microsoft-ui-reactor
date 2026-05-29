using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Handlers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec047.V1Protocol.Ports;

/// <summary>
/// Spec 047 §14 Phase 1 (1.13) — TextBox port tests.
/// </summary>
public class TextBoxPortTests
{
    [Fact]
    public void Flag_On_Registers_TextBoxHandler_Automatically()
    {
        var rec = new Reconciler();
        Assert.Throws<InvalidOperationException>(
            () => rec.RegisterHandler<TextBoxElement, Microsoft.UI.Xaml.Controls.TextBox>(
                new TextBoxHandler()));
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
