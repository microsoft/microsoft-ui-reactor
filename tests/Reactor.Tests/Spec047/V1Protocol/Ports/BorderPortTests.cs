using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Core.V1Protocol.Handlers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec047.V1Protocol.Ports;

/// <summary>
/// Spec 047 §14 Phase 1 (1.14) — Border port tests.
/// </summary>
public class BorderPortTests
{
    [Fact]
    public void Flag_On_Registers_BorderHandler_Automatically()
    {
        var rec = new Reconciler(logger: null, useV1Protocol: true);
        Assert.Throws<InvalidOperationException>(
            () => rec.RegisterHandler<BorderElement, Microsoft.UI.Xaml.Controls.Border>(
                new BorderHandler()));
    }

    [Fact]
    public void Border_Handler_Declares_SingleContent_Strategy()
    {
        var handler = new BorderHandler();
        var strategy = ((IElementHandler<BorderElement, Microsoft.UI.Xaml.Controls.Border>)handler).Children;
        Assert.NotNull(strategy);
        Assert.IsType<SingleContent<BorderElement, Microsoft.UI.Xaml.Controls.Border>>(strategy);
    }

    [Fact(Skip = "Requires WinUI dispatcher; covered in AppTests.Host SelfTest/Fixtures/Spec047V1ProtocolFixtures.cs (1.14)")]
    public void Border_Child_Reconciles_Through_SingleContent_Strategy()
    {
        // TODO(AppTests.Host): mount BorderElement with a TextBlock child →
        // assert ctrl.Child is the mounted UIElement.
        // Update with a different child → strategy dispatches the swap.
        // Modifier interaction: .Padding(10).Background(brush) honored.
    }
}
