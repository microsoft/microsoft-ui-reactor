using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Xunit;
using static Microsoft.UI.Reactor.Factories;
using SlideNavigationTransitionEffect =
    global::Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionEffect;

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

        el.OnSelectedTagChangedWithTransition!("settings", NavigationTransition.Default);

        Assert.IsType<Settings>(nav.CurrentRoute);
        Assert.True(nav.CanGoBack);
    }

    [Theory]
    [InlineData(SlideNavigationTransitionEffect.FromLeft, SlideDirection.FromLeft)]
    [InlineData(SlideNavigationTransitionEffect.FromRight, SlideDirection.FromRight)]
    [InlineData(SlideNavigationTransitionEffect.FromBottom, SlideDirection.FromBottom)]
    public void Recommended_Slide_Transition_Maps_WinUI_Direction(
        SlideNavigationTransitionEffect effect,
        SlideDirection expectedDirection)
    {
        var transition = NavigationViewElement.GetRecommendedSlideTransition(effect);

        var slide = Assert.IsType<SlideTransition>(transition);
        Assert.Equal(expectedDirection, slide.Direction);
    }

    [Fact]
    public void Missing_Recommendation_Leaves_The_Host_Transition_Alone()
    {
        // A null recommendation means "WinUI didn't tell us", not "WinUI asked for entrance".
        // It must stay null: WithNavigation turns a non-null value into an explicit
        // NavigateOptions.Transition, which outranks NavigationHost's own Transition.
        Assert.Null(NavigationViewElement.GetRecommendedNavigationTransition(null));
    }

    [Fact]
    public void Missing_Recommendation_Does_Not_Override_The_Hosts_Transition()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);

        var el = NavigationView([NavItem("Home", tag: "home"), NavItem("Settings", tag: "settings")])
            .WithNavigation(nav, RouteToTag, TagToRoute);

        el.OnSelectedTagChangedWithTransition!("settings", null);

        Assert.IsType<Settings>(nav.CurrentRoute);
        Assert.Null(((INavigationHandle)nav).PendingTransitionOverride);
    }

    [Fact]
    public void WithNavigation_Recommended_Transition_Is_Passed_To_NavigationHost()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);
        var transition = NavigationTransition.Slide(SlideDirection.FromRight);

        var el = NavigationView([NavItem("Home", tag: "home"), NavItem("Settings", tag: "settings")])
            .WithNavigation(nav, RouteToTag, TagToRoute);

        el.OnSelectedTagChangedWithTransition!("settings", transition);

        Assert.IsType<Settings>(nav.CurrentRoute);
        Assert.Same(transition, ((INavigationHandle)nav).PendingTransitionOverride);
    }

    [Fact]
    public void WithNavigation_Settings_Uses_Recommended_Transition()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);
        var transition = NavigationTransition.Slide(SlideDirection.FromLeft);

        var el = NavigationView([NavItem("Home", tag: "home")])
            .WithNavigation(nav, RouteToTag, TagToRoute, () => new Settings());

        el.OnSettingsSelectedWithTransition!(transition);

        Assert.IsType<Settings>(nav.CurrentRoute);
        Assert.Same(transition, ((INavigationHandle)nav).PendingTransitionOverride);
    }

    [Fact]
    public void WithNavigation_Unchanged_Route_Does_Not_Leave_Transition_Override()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);
        var transition = NavigationTransition.Slide(SlideDirection.FromRight);

        var el = NavigationView([NavItem("Home", tag: "home")])
            .WithNavigation(nav, RouteToTag, TagToRoute);

        el.OnSelectedTagChangedWithTransition!("home", transition);

        Assert.Null(((INavigationHandle)nav).PendingTransitionOverride);
        Assert.False(nav.CanGoBack);
    }

    [Fact]
    public void WithNavigation_OnSelectedTagChanged_Ignores_Null_Tag()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);

        var el = NavigationView([NavItem("Home", tag: "home")])
            .WithNavigation(nav, RouteToTag, TagToRoute);

        el.OnSelectedTagChangedWithTransition!(null, NavigationTransition.Default);

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
        el.OnSelectedTagChangedWithTransition!("home", NavigationTransition.Default);

        Assert.Equal(0, navigatedCount);
        Assert.IsType<Home>(nav.CurrentRoute);
    }

    // ════════════════════════════════════════════════════════════════
    //  NavigationView.WithNavigation — the built-in settings item
    // ════════════════════════════════════════════════════════════════

    // TagToRoute throws on tags it does not know, which is the normal shape for
    // that delegate (it must return a non-null TRoute for every string). So
    // WithNavigation must never hand it the SettingsTag sentinel: doing so
    // crashed every existing caller the moment the user picked settings.
    [Fact]
    public void WithNavigation_Without_SettingsRoute_Does_Not_Wire_Settings()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);

        var el = NavigationView([NavItem("Home", tag: "home")])
            .WithNavigation(nav, RouteToTag, TagToRoute);

        Assert.Null(el.OnSettingsSelected);
        // The tag path must not route the sentinel either.
        Assert.Null(el.OnSettingsSelectedWithTransition);
        Assert.IsType<Home>(nav.CurrentRoute);
    }

    [Fact]
    public void WithNavigation_With_SettingsRoute_Navigates_On_Settings_Selected()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);

        var el = NavigationView([NavItem("Home", tag: "home")])
            .WithNavigation(nav, RouteToTag, TagToRoute, () => new Settings());

        Assert.NotNull(el.OnSettingsSelectedWithTransition);
        el.OnSettingsSelectedWithTransition!(NavigationTransition.Default);

        Assert.IsType<Settings>(nav.CurrentRoute);
    }

    [Fact]
    public void WithNavigation_SettingsRoute_Skips_Navigation_When_Route_Unchanged()
    {
        var stack = new NavigationStack<Route>(new Settings());
        var nav = new NavigationHandle<Route>(stack);

        int navigatedCount = 0;
        nav.Navigated += _ => navigatedCount++;

        var el = NavigationView([NavItem("Home", tag: "home")])
            .WithNavigation(nav, RouteToTag, TagToRoute, () => new Settings());

        el.OnSettingsSelectedWithTransition!(NavigationTransition.Default);

        Assert.Equal(0, navigatedCount);
    }

    [Fact]
    public void Public_SelectedTagChanged_After_WithNavigation_Overrides_AutoNavigation()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);
        string? selectedTag = null;
        var transition = NavigationTransition.Slide(SlideDirection.FromRight);

        var el = NavigationView([NavItem("Home", tag: "home"), NavItem("Settings", tag: "settings")])
            .WithNavigation(nav, RouteToTag, TagToRoute)
            .SelectedTagChanged(tag => selectedTag = tag);

        NavigationViewElement.DispatchSelectionChanged(el, false, "settings", transition);

        Assert.Equal("settings", selectedTag);
        Assert.IsType<Home>(nav.CurrentRoute);
        Assert.Null(((INavigationHandle)nav).PendingTransitionOverride);
    }

    [Fact]
    public void Public_SettingsSelected_After_WithNavigation_Overrides_Only_Settings_Navigation()
    {
        var stack = new NavigationStack<Route>(new Home());
        var nav = new NavigationHandle<Route>(stack);
        var settingsInvocations = 0;
        var transition = NavigationTransition.Slide(SlideDirection.FromRight);

        var el = NavigationView([NavItem("Home", tag: "home"), NavItem("Settings", tag: "settings")])
            .WithNavigation(nav, RouteToTag, TagToRoute, () => new Settings())
            .SettingsSelected(() => settingsInvocations++);

        NavigationViewElement.DispatchSelectionChanged(
            el, true, NavigationViewElement.SettingsTag, transition);

        Assert.Equal(1, settingsInvocations);
        Assert.IsType<Home>(nav.CurrentRoute);

        NavigationViewElement.DispatchSelectionChanged(el, false, "settings", transition);

        Assert.IsType<Settings>(nav.CurrentRoute);
        Assert.Same(transition, ((INavigationHandle)nav).PendingTransitionOverride);
    }

    // Returning SettingsTag from routeToTag is how the settings item shows as
    // selected; SelectedTag must carry the sentinel through untouched.
    [Fact]
    public void WithNavigation_SelectedTag_Can_Be_The_Settings_Sentinel()
    {
        var stack = new NavigationStack<Route>(new Settings());
        var nav = new NavigationHandle<Route>(stack);

        var el = NavigationView([NavItem("Home", tag: "home")])
            .WithNavigation(
                nav,
                r => r is Settings ? NavigationViewElement.SettingsTag : RouteToTag(r),
                TagToRoute);

        Assert.Equal(NavigationViewElement.SettingsTag, el.SelectedTag);
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
