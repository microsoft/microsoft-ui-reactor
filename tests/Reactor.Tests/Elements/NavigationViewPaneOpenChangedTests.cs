using System;
using Microsoft.UI.Reactor.Core;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.Elements;

/// <summary>
/// Issue #916 — <c>NavigationViewElement.IsPaneOpen</c> could be written but never
/// reported back, so a controlled pane state desynced whenever the control opened or
/// closed the pane itself (light dismiss, adaptive display-mode changes). These cover the
/// record-level half of the fix; the live wiring — the callback actually firing when the
/// realized control moves its pane — is proved by the <c>NavPane_*</c> selftest fixtures.
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

        // …and the sibling callbacks must stay in the disjunct: clearing only the pane
        // callback while SelectedTagChanged/BackRequested are still wired must keep
        // HasCallbacks true (fails if the disjunct is narrowed to OnPaneOpenChanged alone).
        var siblings = EmptyNav().SelectedTagChanged(_ => { }).BackRequested(() => { });
        Assert.True(siblings.PaneOpenChanged(_ => { }).PaneOpenChanged(null).HasCallbacks);
        Assert.True(EmptyNav().SelectedTagChanged(_ => { }).HasCallbacks);
        Assert.True(EmptyNav().BackRequested(() => { }).HasCallbacks);
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
    public void IsPaneOpen_Pair_Sets_Both_State_And_Handler()
    {
        // The paired overload exists so the two halves can't drift apart — issue #916 is
        // exactly what happens when IsPaneOpen is set without the companion handler.
        Action<bool> handler = _ => { };

        var closed = EmptyNav().IsPaneOpen(false, handler);
        Assert.False(closed.IsPaneOpen);
        Assert.Same(handler, closed.OnPaneOpenChanged);
        Assert.True(closed.HasCallbacks);

        // Both halves track the argument independently (a pair that ignored either
        // argument, or aliased one to the other, fails here).
        Assert.True(EmptyNav().IsPaneOpen(true, handler).IsPaneOpen);
        Assert.NotEqual(
            EmptyNav().IsPaneOpen(true, handler).IsPaneOpen,
            EmptyNav().IsPaneOpen(false, handler).IsPaneOpen);

        // Same shape on SplitView, so authors can move between the two controls.
        var split = SplitView().IsPaneOpen(false, handler);
        Assert.False(split.IsPaneOpen);
        Assert.Same(handler, split.OnPaneOpenChanged);
    }
}
