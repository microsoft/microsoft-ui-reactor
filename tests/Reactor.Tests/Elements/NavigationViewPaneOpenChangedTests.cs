using System;
using Microsoft.UI.Reactor.Core;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.Elements;

/// <summary>
/// Issue #916 — <c>NavigationViewElement.IsPaneOpen</c> could be written but never
/// reported back, so a controlled pane state desynced whenever the control opened or
/// closed the pane itself (light dismiss, adaptive display-mode changes). These cover the
/// record-level half of the fix; the live event wiring is proved by the
/// <c>NavigationViewPaneOpenChanged*</c> selftest fixtures.
/// </summary>
public class NavigationViewPaneOpenChangedTests
{
    private static NavigationViewElement EmptyNav() =>
        NavigationView(Array.Empty<NavigationViewItemData>());

    [Fact]
    public void OnPaneOpenChanged_Alone_Flips_HasCallbacks()
    {
        // The subscription gate (and the #153 Tag-refresh fast path) keys off HasCallbacks:
        // if OnPaneOpenChanged is missing from the disjunct, a NavigationView wired ONLY for
        // pane changes never gets its element pointer refreshed and the callback goes stale.
        Assert.False(EmptyNav().HasCallbacks);
        Assert.True(EmptyNav().PaneOpenChanged(_ => { }).HasCallbacks);
        Assert.False(EmptyNav().PaneOpenChanged(_ => { }).PaneOpenChanged(null).HasCallbacks);
    }

    [Fact]
    public void PaneOpenChanged_Leaves_Sibling_Callbacks_And_IsPaneOpen_Untouched()
    {
        Action<string?> tagHandler = _ => { };
        Action backHandler = () => { };
        var el = (EmptyNav() with { IsPaneOpen = false })
            .SelectedTagChanged(tagHandler)
            .BackRequested(backHandler)
            .PaneOpenChanged(_ => { });

        Assert.Same(tagHandler, el.OnSelectedTagChanged);
        Assert.Same(backHandler, el.OnBackRequested);
        Assert.False(el.IsPaneOpen);
    }

    [Fact]
    public void Callback_Value_Feeds_Back_Into_A_Controlled_IsPaneOpen()
    {
        // Models the issue's repro without WinUI. Each `with { IsPaneOpen = state }` is one
        // render; the reconciler only writes the DP when consecutive elements differ, so the
        // oracle is the sign of that diff after the control closed its own pane.
        bool state = true;
        var render1 = EmptyNav() with { IsPaneOpen = state };

        // ── With the fix: the control's close is pushed back into state.
        var wired = render1.PaneOpenChanged(open => state = open);
        wired.OnPaneOpenChanged!(false);                       // light dismiss closed the pane
        Assert.False(state);
        var render2 = wired with { IsPaneOpen = state };
        state = !state;                                        // one title-bar toggle click
        var render3 = render2 with { IsPaneOpen = state };
        Assert.False(render2.IsPaneOpen);
        Assert.True(render3.IsPaneOpen);                       // false → true: the pane reopens

        // ── Pre-fix control: no callback, so state never learns about the close and the
        // single toggle writes the value the control is already at.
        bool stale = true;
        var staleRender1 = EmptyNav() with { IsPaneOpen = stale };
        stale = !stale;                                        // same single toggle click
        var staleRender2 = staleRender1 with { IsPaneOpen = stale };
        Assert.False(staleRender2.IsPaneOpen);                 // true → false: pane stays closed
        Assert.NotEqual(staleRender2.IsPaneOpen, render3.IsPaneOpen);
    }
}
