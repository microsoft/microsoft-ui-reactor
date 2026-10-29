using Microsoft.UI.Reactor.Navigation;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class ConnectedNavigationHistoryTests
{
    [Fact]
    public void IndependentPageTransitionAndCardKeyTravelTogetherThroughHistory()
    {
        var nav = new NavigationHandle<string>(new NavigationStack<string>("list"));
        var host = (INavigationHandle)nav;
        var drill = NavigationTransition.DrillIn();
        var fade = NavigationTransition.Fade(TimeSpan.FromMilliseconds(350));
        nav.Navigate("detail", new() { Transition = drill, ConnectedAnimationKey = "first" });
        nav.Navigate("child", new() { Transition = fade, ConnectedAnimationKey = "second" });
        host.PendingTransitionOverride = null;
        host.PendingConnectedAnimationKey = null;
        Assert.True(nav.GoBack());
        Assert.Same(fade, host.PendingTransitionOverride);
        Assert.Equal("second", host.PendingConnectedAnimationKey);
        Assert.True(nav.GoBack());
        Assert.Same(drill, host.PendingTransitionOverride);
        Assert.Equal("first", host.PendingConnectedAnimationKey);
        Assert.True(nav.GoForward());
        Assert.Same(drill, host.PendingTransitionOverride);
        Assert.Equal("first", host.PendingConnectedAnimationKey);
        Assert.True(nav.GoForward());
        Assert.Same(fade, host.PendingTransitionOverride);
        Assert.Equal("second", host.PendingConnectedAnimationKey);
    }

    [Fact]
    public void IndependentOptionsRespectCancellationReplacementAndRestore()
    {
        var stack = new NavigationStack<string>("list");
        var nav = new NavigationHandle<string>(stack);
        var host = (INavigationHandle)nav;
        nav.Navigate("detail", new() { Transition = NavigationTransition.None, ConnectedAnimationKey = "hero" });
        stack.Guard = ctx => ctx.Cancel();
        Assert.False(nav.Navigate("wrong", new() { Transition = NavigationTransition.Fade(), ConnectedAnimationKey = "wrong" }));
        Assert.Null(host.PendingConnectedAnimationKey);
        Assert.Null(host.PendingTransitionOverride);
        stack.Guard = null;
        Assert.True(nav.GoBack());
        Assert.Equal("hero", host.PendingConnectedAnimationKey);
        Assert.Same(NavigationTransition.None, host.PendingTransitionOverride);
        Assert.True(nav.GoForward());
        Assert.True(nav.Replace("replacement"));
        Assert.Null(host.PendingConnectedAnimationKey);
        Assert.True(nav.GoBack());
        Assert.Null(host.PendingConnectedAnimationKey);
        nav.SetState(new(["a"], "b", ["c"]));
        Assert.True(nav.GoBack());
        Assert.Null(host.PendingConnectedAnimationKey);
        Assert.Null(host.PendingTransitionOverride);
    }

    [Fact]
    public void BackAndForwardRememberTheSelectedEdgeNotTheLatestKey()
    {
        var nav = new NavigationHandle<string>(new NavigationStack<string>("list"));
        var host = (INavigationHandle)nav;
        nav.Navigate("detail", new() { Transition = NavigationTransition.Connected("first") });
        nav.Navigate("child", new() { Transition = NavigationTransition.Connected("second") });
        host.PendingTransitionOverride = null;
        Assert.True(nav.GoBack());
        Assert.Equal("second", Assert.IsType<ConnectedTransition>(host.PendingTransitionOverride).AnimationKey);
        Assert.True(nav.GoBack());
        Assert.Equal("first", Assert.IsType<ConnectedTransition>(host.PendingTransitionOverride).AnimationKey);
        Assert.True(nav.GoForward());
        Assert.Equal("first", Assert.IsType<ConnectedTransition>(host.PendingTransitionOverride).AnimationKey);
        Assert.True(nav.GoForward());
        Assert.Equal("second", Assert.IsType<ConnectedTransition>(host.PendingTransitionOverride).AnimationKey);
    }

    [Fact]
    public void CancelledNavigationDoesNotChangeReturnAnimation()
    {
        var stack = new NavigationStack<string>("list");
        var nav = new NavigationHandle<string>(stack);
        nav.Navigate("detail", new() { Transition = NavigationTransition.Connected("hero") });
        stack.Guard = context => context.Cancel();
        Assert.False(nav.Navigate("other", new() { Transition = NavigationTransition.Connected("wrong") }));
        Assert.False(nav.GoBack());
        stack.Guard = null;
        Assert.True(nav.GoBack());
        Assert.Equal("hero", Assert.IsType<ConnectedTransition>(((INavigationHandle)nav).PendingTransitionOverride).AnimationKey);
    }

    [Fact]
    public void RestoredStateAndReplacementDoNotReuseStaleEdges()
    {
        var nav = new NavigationHandle<string>(new NavigationStack<string>("list"));
        var host = (INavigationHandle)nav;
        nav.Navigate("detail", new() { Transition = NavigationTransition.Connected("hero") });
        nav.Replace("unrelated");
        nav.GoBack();
        Assert.Null(host.PendingTransitionOverride);
        nav.SetState(new(["a", "b"], "c", ["d"]));
        Assert.True(nav.GoBack());
        Assert.Null(host.PendingTransitionOverride);
        Assert.True(nav.GoForward());
        Assert.Null(host.PendingTransitionOverride);
        Assert.True(nav.GoForward());
        Assert.Null(host.PendingTransitionOverride);
    }

    [Fact]
    public void MultiPopAndNewPushKeepHistoryAligned()
    {
        var nav = new NavigationHandle<string>(new NavigationStack<string>("a"));
        var host = (INavigationHandle)nav;
        nav.Navigate("b", new() { Transition = NavigationTransition.Connected("ab") });
        nav.Navigate("c", new() { Transition = NavigationTransition.Connected("bc") });
        Assert.True(nav.PopTo(route => route == "a"));
        Assert.Null(host.PendingTransitionOverride);
        Assert.True(nav.GoForward());
        Assert.Equal("ab", Assert.IsType<ConnectedTransition>(host.PendingTransitionOverride).AnimationKey);
        nav.Navigate("new");
        Assert.False(nav.CanGoForward);
        Assert.True(nav.GoBack());
        Assert.Null(host.PendingTransitionOverride);
    }
}
