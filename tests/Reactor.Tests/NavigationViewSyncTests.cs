using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Phase 7 tests: NavigationView and TitleBar auto-sync with NavigationHandle
/// via the <c>WithNavigation</c> extension methods.
/// </summary>
public class NavigationViewSyncTests
{
    private abstract record Route;
    private sealed record Home : Route;
    private sealed record Detail(int Id) : Route;
    private sealed record Settings : Route;

    private static string? RouteToTag(Route route) => route switch
    {
        Home => "home",
        Settings => "settings",
        _ => null,
    };

    private static Route TagToRoute(string tag) => tag switch
    {
        "home" => new Home(),
        "settings" => new Settings(),
        _ => throw new ArgumentException($"Unknown tag: {tag}"),
    };

    // ════════════════════════════════════════════════════════════════
    //  NavigationView.WithNavigation — SelectedTag sync
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void WithNavigation_Sets_SelectedTag_From_CurrentRoute()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);

        var el = NavigationView([NavItem("Home", tag: "home"), NavItem("Settings", tag: "settings")])
            .WithNavigation(nav, RouteToTag, TagToRoute);

        Assert.Equal("home", el.SelectedTag);
    }

    [Fact]
    public void WithNavigation_SelectedTag_Updates_After_Navigation()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);

        nav.Navigate(new Settings());

        var el = NavigationView([NavItem("Home", tag: "home"), NavItem("Settings", tag: "settings")])
            .WithNavigation(nav, RouteToTag, TagToRoute);

        Assert.Equal("settings", el.SelectedTag);
    }

    [Fact]
    public void WithNavigation_SelectedTag_Is_Null_For_Unmapped_Route()
    {
        var stack = new NavigationStack<Route>(new Detail(1));
        var nav = new NavigationHandle<Route>(stack);

        var el = NavigationView([NavItem("Home", tag: "home"), NavItem("Settings", tag: "settings")])
            .WithNavigation(nav, RouteToTag, TagToRoute);

        Assert.Null(el.SelectedTag);
    }

    // ════════════════════════════════════════════════════════════════
    //  NavigationView.WithNavigation — IsBackEnabled
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void WithNavigation_IsBackEnabled_False_When_No_BackStack()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);

        var el = NavigationView([NavItem("Home", tag: "home")])
            .WithNavigation(nav, RouteToTag, TagToRoute);

        Assert.False(el.IsBackEnabled);
    }

    [Fact]
    public void WithNavigation_IsBackEnabled_True_When_CanGoBack()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);
        nav.Navigate(new Settings());

        var el = NavigationView([NavItem("Home", tag: "home"), NavItem("Settings", tag: "settings")])
            .WithNavigation(nav, RouteToTag, TagToRoute);

        Assert.True(el.IsBackEnabled);
    }

    // ════════════════════════════════════════════════════════════════
    //  NavigationView.WithNavigation — OnSelectedTagChanged
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void WithNavigation_OnSelectedTagChanged_Navigates_To_Route()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);

        var el = NavigationView([NavItem("Home", tag: "home"), NavItem("Settings", tag: "settings")])
            .WithNavigation(nav, RouteToTag, TagToRoute);

        el.OnSelectedTagChanged!("settings");

        Assert.IsType<Settings>(nav.CurrentRoute);
        Assert.True(nav.CanGoBack);
    }

    [Fact]
    public void WithNavigation_OnSelectedTagChanged_Ignores_Null_Tag()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);

        var el = NavigationView([NavItem("Home", tag: "home")])
            .WithNavigation(nav, RouteToTag, TagToRoute);

        el.OnSelectedTagChanged!(null);

        Assert.IsType<Home>(nav.CurrentRoute);
        Assert.False(nav.CanGoBack);
    }

    [Fact]
    public void WithNavigation_OnSelectedTagChanged_Skips_Navigation_When_Route_Unchanged()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);

        int navigatedCount = 0;
        nav.Navigated += _ => navigatedCount++;

        var el = NavigationView([NavItem("Home", tag: "home")])
            .WithNavigation(nav, RouteToTag, TagToRoute);

        // Selecting the already-active route should not trigger navigation
        el.OnSelectedTagChanged!("home");

        Assert.Equal(0, navigatedCount);
        Assert.IsType<Home>(nav.CurrentRoute);
    }

    // ════════════════════════════════════════════════════════════════
    //  NavigationView.WithNavigation — OnBackRequested
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void WithNavigation_OnBackRequested_Calls_GoBack()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);
        nav.Navigate(new Settings());

        var el = NavigationView([NavItem("Home", tag: "home"), NavItem("Settings", tag: "settings")])
            .WithNavigation(nav, RouteToTag, TagToRoute);

        el.OnBackRequested!();

        Assert.IsType<Home>(nav.CurrentRoute);
    }

    [Fact]
    public void WithNavigation_OnBackRequested_NoOp_When_Cannot_GoBack()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);

        var el = NavigationView([NavItem("Home", tag: "home")])
            .WithNavigation(nav, RouteToTag, TagToRoute);

        // Should not throw
        el.OnBackRequested!();

        Assert.IsType<Home>(nav.CurrentRoute);
    }

    // ════════════════════════════════════════════════════════════════
    //  NavigationView.WithNavigation — preserves other properties
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void WithNavigation_Preserves_MenuItems_And_Content()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);
        var content = TextBlock("Content");
        var items = new[] { NavItem("Home", tag: "home") };

        var el = NavigationView(items, content)
            .WithNavigation(nav, RouteToTag, TagToRoute);

        Assert.Same(items, el.MenuItems);
        Assert.Same(content, el.Content);
    }

    [Fact]
    public void WithNavigation_Can_Be_Combined_With_Other_Modifiers()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);

        var el = NavigationView([NavItem("Home", tag: "home")])
            .WithNavigation(nav, RouteToTag, TagToRoute)
            .PaneTitle("My App");

        Assert.Equal("home", el.SelectedTag);
        Assert.Equal("My App", el.PaneTitle);
    }

    // ════════════════════════════════════════════════════════════════
    //  TitleBar.WithNavigation
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void TitleBar_WithNavigation_Sets_BackButton_Visible_When_CanGoBack()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);
        nav.Navigate(new Settings());

        var el = TitleBar("My App")
            .WithNavigation(nav);

        Assert.True(el.IsBackButtonVisible);
        Assert.True(el.IsBackButtonEnabled);
    }

    [Fact]
    public void TitleBar_WithNavigation_Hides_BackButton_When_Cannot_GoBack()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);

        var el = TitleBar("My App")
            .WithNavigation(nav);

        Assert.False(el.IsBackButtonVisible);
        Assert.False(el.IsBackButtonEnabled);
    }

    [Fact]
    public void TitleBar_WithNavigation_OnBackRequested_Calls_GoBack()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);
        nav.Navigate(new Settings());

        var el = TitleBar("My App")
            .WithNavigation(nav);

        el.OnBackRequested!();

        Assert.IsType<Home>(nav.CurrentRoute);
    }

    [Fact]
    public void TitleBar_WithNavigation_Preserves_Title()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);

        var el = TitleBar("My App")
            .Subtitle("Subtitle")
            .WithNavigation(nav);

        Assert.Equal("My App", el.Title);
        Assert.Equal("Subtitle", el.Subtitle);
    }
}
