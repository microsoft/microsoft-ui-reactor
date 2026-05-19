using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

internal static class NavigationFixtures
{
    // ── Existing: tab switching with buttons (UseState) ──────────────────

    internal class TabSwitchingComponent : Component
    {
        public override Element Render()
        {
            var (tab, setTab) = UseState("Home");

            return VStack(
                HStack(
                    Button("Home", () => setTab("Home"))
                        .IsEnabled(!(tab == "Home")).AutomationId("HomeTab"),
                    Button("Settings", () => setTab("Settings"))
                        .IsEnabled(!(tab == "Settings")).AutomationId("SettingsTab"),
                    Button("About", () => setTab("About"))
                        .IsEnabled(!(tab == "About")).AutomationId("AboutTab")
                ),
                Border(
                    tab switch
                    {
                        "Home" => VStack(
                            TextBlock("Welcome Home").AutomationId("HomeTitle"),
                            TextBlock("This is the home page.").AutomationId("HomeContent")),
                        "Settings" => VStack(
                            TextBlock("Settings Page").AutomationId("SettingsTitle"),
                            TextBlock("Configure your preferences.").AutomationId("SettingsContent")),
                        "About" => VStack(
                            TextBlock("About Page").AutomationId("AboutTitle"),
                            TextBlock("Version 1.0").AutomationId("AboutVersion")),
                        _ => Empty()
                    }
                ).Padding(16).AutomationId("TabContent")
            );
        }
    }

    internal static Element TabSwitching(RenderContext ctx) =>
        Component<TabSwitchingComponent>();

    // ── Multi-level navigation (UseNavigation + NavigationHost) ──────────

    abstract record NavRoute;
    sealed record NavHome : NavRoute;
    sealed record NavList : NavRoute;
    sealed record NavDetail(int Id) : NavRoute;
    sealed record NavRelated(int SourceId) : NavRoute;

    internal class MultiLevelNavComponent : Component
    {
        public override Element Render()
        {
            var nav = UseNavigation<NavRoute>(new NavHome());

            return VStack(
                HStack(8,
                    Button("Back", () => nav.GoBack())
                        .IsEnabled(nav.CanGoBack)
                        .AutomationId("BackBtn"),
                    TextBlock($"Depth: {nav.Depth}").AutomationId("NavDepth")
                ),
                NavigationHost(nav, route => route switch
                {
                    NavHome => VStack(
                        TextBlock("Home").AutomationId("PageTitle"),
                        Button("Go to List", () => nav.Navigate(new NavList()))
                            .AutomationId("GoListBtn")),
                    NavList => VStack(
                        TextBlock("Item List").AutomationId("PageTitle"),
                        Button("View Detail #1", () => nav.Navigate(new NavDetail(1)))
                            .AutomationId("GoDetail1Btn"),
                        Button("View Detail #2", () => nav.Navigate(new NavDetail(2)))
                            .AutomationId("GoDetail2Btn")),
                    NavDetail d => VStack(
                        TextBlock($"Detail #{d.Id}").AutomationId("PageTitle"),
                        Button("Related Items", () => nav.Navigate(new NavRelated(d.Id)))
                            .AutomationId("GoRelatedBtn")),
                    NavRelated r => VStack(
                        TextBlock($"Related to #{r.SourceId}").AutomationId("PageTitle"),
                        Button("Back to Home", () => nav.Reset(new NavHome()))
                            .AutomationId("ResetBtn")),
                    _ => TextBlock("Unknown")
                }) with { Transition = NavigationTransition.None }
            );
        }
    }

    internal static Element MultiLevelNav(RenderContext ctx) =>
        Component<MultiLevelNavComponent>();

    // ── Navigation with guard ───────────────────────────────────────────

    internal class NavGuardComponent : Component
    {
        public override Element Render()
        {
            var nav = UseNavigation<string>("page-a");

            return VStack(
                HStack(8,
                    Button("Go to A", () => nav.Navigate("page-a"))
                        .AutomationId("GoABtn"),
                    Button("Go to B", () => nav.Navigate("page-b"))
                        .AutomationId("GoBBtn"),
                    TextBlock($"Current: {nav.CurrentRoute}").AutomationId("CurrentRoute")
                ),
                NavigationHost(nav, route => route switch
                {
                    "page-a" => Component<PageA>(),
                    "page-b" => Component<PageB>(),
                    _ => TextBlock("Unknown")
                }) with { Transition = NavigationTransition.None }
            );
        }
    }

    class PageA : Component
    {
        public override Element Render()
        {
            return VStack(
                TextBlock("Page A").AutomationId("PageTitle"),
                TextBlock("No guard on this page.").AutomationId("GuardStatus")
            );
        }
    }

    class PageB : Component
    {
        public override Element Render()
        {
            var (dirty, setDirty) = UseState(false);

            UseNavigationLifecycle(
                onNavigatingFrom: ctx =>
                {
                    if (dirty) ctx.Cancel();
                });

            return VStack(
                TextBlock("Page B").AutomationId("PageTitle"),
                TextBlock(dirty ? "Guard: ACTIVE" : "Guard: inactive").AutomationId("GuardStatus"),
                Button("Toggle Guard", () => setDirty(!dirty)).AutomationId("ToggleGuardBtn")
            );
        }
    }

    internal static Element NavGuard(RenderContext ctx) =>
        Component<NavGuardComponent>();

    // ── NavigationView + NavigationHost integration ─────────────────────

    abstract record ViewRoute;
    sealed record ViewHome : ViewRoute;
    sealed record ViewSettings : ViewRoute;
    sealed record ViewAbout : ViewRoute;

    internal class NavViewIntegrationComponent : Component
    {
        public override Element Render()
        {
            var nav = UseNavigation<ViewRoute>(new ViewHome());

            return (NavigationView(
                [
                    NavItem("Home", tag: "home"),
                    NavItem("Settings", tag: "settings"),
                    NavItem("About", tag: "about"),
                ],
                NavigationHost(nav, route => route switch
                {
                    ViewHome => TextBlock("Home Content").AutomationId("PageContent"),
                    ViewSettings => TextBlock("Settings Content").AutomationId("PageContent"),
                    ViewAbout => TextBlock("About Content").AutomationId("PageContent"),
                    _ => TextBlock("Unknown")
                }) with { Transition = NavigationTransition.None }
            ).WithNavigation(nav,
                route => route switch
                {
                    ViewHome => "home",
                    ViewSettings => "settings",
                    ViewAbout => "about",
                    _ => null,
                },
                tag => tag switch
                {
                    "settings" => new ViewSettings(),
                    "about" => new ViewAbout(),
                    _ => new ViewHome(),
                }) with
            {
                IsSettingsVisible = false,
            }).AutomationId("NavViewHost");
        }
    }

    internal static Element NavViewIntegration(RenderContext ctx) =>
        Component<NavViewIntegrationComponent>();
}
