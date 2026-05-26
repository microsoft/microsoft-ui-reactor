using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Core.V1Protocol.Handlers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec047.V1Protocol.Ports;

/// <summary>
/// Spec 047 §14 Phase 1 (1.15) — ListView port tests.
/// Path B port (delegate + shape declaration); see ListViewHandler XML doc.
/// </summary>
public class ListViewPortTests
{
    [Fact]
    public void Flag_On_Registers_ListViewHandler_Automatically()
    {
        var rec = new Reconciler(logger: null, useV1Protocol: true);
        Assert.Throws<InvalidOperationException>(
            () => rec.RegisterHandler<ListViewElement, Microsoft.UI.Xaml.Controls.ListView>(
                new ListViewHandler()));
    }

    [Fact]
    public void ListView_Handler_Declares_ItemsHost_Strategy()
    {
        var handler = new ListViewHandler();
        var strategy = ((IElementHandler<ListViewElement, Microsoft.UI.Xaml.Controls.ListView>)handler).Children;
        Assert.NotNull(strategy);
        Assert.IsType<ItemsHost<ListViewElement, Microsoft.UI.Xaml.Controls.ListView>>(strategy);
    }

    [Fact(Skip = "Requires WinUI dispatcher + virtualized scrolling; covered in AppTests.Host SelfTest/Fixtures/Spec047V1ProtocolFixtures.cs (1.15)")]
    public void Pool_Survival_Across_1000_Item_Scroll()
    {
        // TODO(AppTests.Host): scroll 1000 items through a 20-row viewport;
        // assert pool rent/return cycle works correctly under the v1 protocol;
        // no residual state between rentals (Q18 correctness).
    }

    [Fact(Skip = "Requires WinUI dispatcher; covered in AppTests.Host SelfTest/Fixtures/Spec047V1ProtocolFixtures.cs (1.15)")]
    public void Selection_Change_Fires_Callback()
    {
        // TODO(AppTests.Host): mount a 5-item ListView, programmatically set
        // SelectedIndex via reconcile, assert OnSelectedIndexChanged fires.
    }
}
