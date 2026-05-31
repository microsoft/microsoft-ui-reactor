using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class NavigationFixtures
{
    // ── Existing: tab switching with buttons (UseState) ──────────────────

    internal class TabSwitching(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tab, setTab) = ctx.UseState("Home");

                return VStack(
                    HStack(
                        Button("Home", () => setTab("Home"))
                            .IsEnabled(!(tab == "Home")),
                        Button("Settings", () => setTab("Settings"))
                            .IsEnabled(!(tab == "Settings")),
                        Button("About", () => setTab("About"))
                            .IsEnabled(!(tab == "About"))
                    ),
                    Border(
                        tab switch
                        {
                            "Home" => VStack(TextBlock("Welcome Home"), TextBlock("This is the home page.")),
                            "Settings" => VStack(TextBlock("Settings Page"), TextBlock("Configure your preferences.")),
                            "About" => VStack(TextBlock("About Page"), TextBlock("Version 1.0")),
                            _ => Empty()
                        }
                    ).Padding(16)
                );
            });

            await Harness.Render();

            H.Check("Navigation_TabSwitching_DefaultTabIsHome",
                H.FindText("Welcome Home") is not null);

            H.Check("Navigation_TabSwitching_HomeButtonDisabled", () =>
            {
                var btn = H.FindButton("Home");
                return btn is not null && !btn.IsEnabled;
            });

            H.ClickButton("Settings");
            await Harness.Render();

            H.Check("Navigation_TabSwitching_SettingsContentShown",
                H.FindText("Settings Page") is not null);

            H.Check("Navigation_TabSwitching_HomeContentGone",
                H.FindText("Welcome Home") is null);

            H.ClickButton("About");
            await Harness.Render();

            H.Check("Navigation_TabSwitching_AboutContentShown",
                H.FindText("Version 1.0") is not null);

            H.ClickButton("Home");
            await Harness.Render();

            H.Check("Navigation_TabSwitching_BackToHome",
                H.FindText("Welcome Home") is not null);
        }
    }

    // ── NavigationHost renders initial route ─────────────────────────────

    internal class NavHostRendersInitial(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var nav = ctx.UseNavigation<string>("home");

                return NavigationHost(nav, route => route switch
                {
                    "home" => TextBlock("Home Page Content"),
                    "settings" => TextBlock("Settings Content"),
                    _ => TextBlock("Unknown"),
                }) with { Transition = NavigationTransition.None };
            });

            await Harness.Render();

            H.Check("NavHost_RendersInitialRoute",
                H.FindText("Home Page Content") is not null);
        }
    }

    // ── Navigate changes rendered content ────────────────────────────────

    internal class NavHostContentSwap(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            NavigationHandle<string>? navRef = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var nav = ctx.UseNavigation<string>("home");
                navRef = nav;

                return VStack(
                    Button("Go to Settings", () => nav.Navigate("settings")),
                    NavigationHost(nav, route => route switch
                    {
                        "home" => TextBlock("Home Page"),
                        "settings" => TextBlock("Settings Page"),
                        _ => TextBlock("Unknown"),
                    }) with { Transition = NavigationTransition.None }
                );
            });

            await Harness.Render();

            H.Check("NavHost_InitialContent",
                H.FindText("Home Page") is not null);

            H.ClickButton("Go to Settings");
            await Harness.Render();

            H.Check("NavHost_NavigateChangesContent",
                H.FindText("Settings Page") is not null);

            H.Check("NavHost_OldContentRemoved",
                H.FindText("Home Page") is null);
        }
    }

    // ── GoBack restores previous content ─────────────────────────────────

    internal class NavHostGoBack(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var nav = ctx.UseNavigation<string>("home");

                return VStack(
                    HStack(8,
                        Button("Go to Settings", () => nav.Navigate("settings")),
                        Button("Back", () => nav.GoBack())
                    ),
                    NavigationHost(nav, route => route switch
                    {
                        "home" => TextBlock("Home Page"),
                        "settings" => TextBlock("Settings Page"),
                        _ => TextBlock("Unknown"),
                    }) with { Transition = NavigationTransition.None }
                );
            });

            await Harness.Render();
            H.Check("NavHost_GoBack_InitialHome",
                H.FindText("Home Page") is not null);

            H.ClickButton("Go to Settings");
            await Harness.Render();
            H.Check("NavHost_GoBack_AtSettings",
                H.FindText("Settings Page") is not null);

            H.ClickButton("Back");
            await Harness.Render();
            H.Check("NavHost_GoBack_RestoredHome",
                H.FindText("Home Page") is not null);

            H.Check("NavHost_GoBack_SettingsGone",
                H.FindText("Settings Page") is null);
        }
    }

    // ── Lifecycle hooks fire in correct order ────────────────────────────

    // Static event log shared between fixture and lifecycle components
    internal static readonly List<string> LifecycleEvents = new();

    internal class NavHostLifecycleOrder(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            LifecycleEvents.Clear();

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var nav = ctx.UseNavigation<string>("page-a");

                return VStack(
                    Button("Go to B", () => nav.Navigate("page-b")),
                    Button("Go to A", () => nav.Navigate("page-a")),
                    NavigationHost(nav, route => route switch
                    {
                        "page-a" => Component<LifecyclePageA>(),
                        "page-b" => Component<LifecyclePageB>(),
                        _ => TextBlock("Unknown"),
                    }) with { Transition = NavigationTransition.None }
                );
            });

            await Harness.Render();

            // Navigate from A to B
            LifecycleEvents.Clear();
            H.ClickButton("Go to B");
            await Harness.Render();

            // Expected lifecycle order: navigatingFrom(A) → navigatedTo(B) → navigatedFrom(A)
            H.Check("NavHost_Lifecycle_NavigatingFromFired",
                LifecycleEvents.Any(e => e.StartsWith("navigatingFrom:A")));

            H.Check("NavHost_Lifecycle_NavigatedToFired",
                LifecycleEvents.Any(e => e.StartsWith("navigatedTo:B")));

            H.Check("NavHost_Lifecycle_NavigatedFromFired",
                LifecycleEvents.Any(e => e.StartsWith("navigatedFrom:A")));

            // navigatingFrom should come before navigatedTo
            var navFromIdx = LifecycleEvents.FindIndex(e => e.StartsWith("navigatingFrom:A"));
            var navToIdx = LifecycleEvents.FindIndex(e => e.StartsWith("navigatedTo:B"));
            H.Check("NavHost_Lifecycle_CorrectOrder",
                navFromIdx >= 0 && navToIdx >= 0 && navFromIdx < navToIdx);
        }
    }

    class LifecyclePageA : Component
    {
        public override Element Render()
        {
            UseNavigationLifecycle(
                onNavigatedTo: ctx => LifecycleEvents.Add("navigatedTo:A"),
                onNavigatingFrom: ctx => LifecycleEvents.Add("navigatingFrom:A"),
                onNavigatedFrom: ctx => LifecycleEvents.Add("navigatedFrom:A"));
            return TextBlock("Page A");
        }
    }

    class LifecyclePageB : Component
    {
        public override Element Render()
        {
            UseNavigationLifecycle(
                onNavigatedTo: ctx => LifecycleEvents.Add("navigatedTo:B"),
                onNavigatingFrom: ctx => LifecycleEvents.Add("navigatingFrom:B"),
                onNavigatedFrom: ctx => LifecycleEvents.Add("navigatedFrom:B"));
            return TextBlock("Page B");
        }
    }

    // ── Nested navigation works independently ────────────────────────────

    internal class NavHostNested(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var outerNav = ctx.UseNavigation<string>("outer-a");

                return VStack(
                    HStack(8,
                        Button("Outer A", () => outerNav.Navigate("outer-a")),
                        Button("Outer B", () => outerNav.Navigate("outer-b"))
                    ),
                    NavigationHost(outerNav, route => route switch
                    {
                        "outer-a" => TextBlock("Outer A Content"),
                        "outer-b" => new NestedNavComponent().AsElement(),
                        _ => TextBlock("Unknown"),
                    }) with { Transition = NavigationTransition.None }
                );
            });

            await Harness.Render();
            H.Check("NavHost_Nested_OuterInitial",
                H.FindText("Outer A Content") is not null);

            // Navigate to outer-b which has its own inner navigation
            H.ClickButton("Outer B");
            await Harness.Render();
            H.Check("NavHost_Nested_InnerInitial",
                H.FindText("Inner Page 1") is not null);

            // Navigate inner
            H.ClickButton("Inner 2");
            await Harness.Render();
            H.Check("NavHost_Nested_InnerNavigated",
                H.FindText("Inner Page 2") is not null);

            // Navigate back to outer-a — inner nav should be independent
            H.ClickButton("Outer A");
            await Harness.Render();
            H.Check("NavHost_Nested_BackToOuter",
                H.FindText("Outer A Content") is not null);
        }
    }

    class NestedNavComponent : Component
    {
        public override Element Render()
        {
            var nav = UseNavigation<string>("inner-1");

            return VStack(
                HStack(8,
                    Button("Inner 1", () => nav.Navigate("inner-1", new NavigateOptions { PushToBackStack = false })),
                    Button("Inner 2", () => nav.Navigate("inner-2", new NavigateOptions { PushToBackStack = false }))
                ),
                NavigationHost(nav, route => route switch
                {
                    "inner-1" => TextBlock("Inner Page 1"),
                    "inner-2" => TextBlock("Inner Page 2"),
                    _ => TextBlock("Unknown"),
                }) with { Transition = NavigationTransition.None }
            );
        }

        public Element AsElement() => Component<NestedNavComponent>();
    }

    // ── Page caching: round-trips the cached control on navigate-back ─────
    //
    // Exercises the Update body's cache branches: a page leaving the host is
    // stored (FinalizeOldPage cache arm) instead of unmounted, and navigating
    // back to its route restores the SAME mounted control (cache-hit arm in
    // place of a fresh Mount). State held in the cached page's component tree
    // (UseState counter) survives the round-trip — proof it was restored from
    // cache rather than remounted (a fresh mount would reset the counter to 0).

    internal class NavHostCacheMode(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var nav = ctx.UseNavigation<string>("home");

                return VStack(
                    HStack(8,
                        Button("Go Other", () => nav.Navigate("other")),
                        Button("Go Home", () => nav.Navigate("home"))
                    ),
                    NavigationHost(nav, route => route switch
                    {
                        "home" => Component<CacheCounterPage>(),
                        "other" => TextBlock("Other Page"),
                        _ => TextBlock("Unknown"),
                    }) with
                    {
                        Transition = NavigationTransition.None,
                        CacheMode = NavigationCacheMode.Required,
                        CacheSize = 5,
                    }
                );
            });

            await Harness.Render();
            H.Check("NavHost_Cache_InitialCountZero",
                H.FindText("Count: 0") is not null);

            // Bump the home page's local state to a non-default value
            H.ClickButton("Increment");
            await Harness.Render();
            H.ClickButton("Increment");
            await Harness.Render();
            H.Check("NavHost_Cache_CountIncremented",
                H.FindText("Count: 2") is not null);

            // Navigate away — the home page is cached, not unmounted
            H.ClickButton("Go Other");
            await Harness.Render();
            H.Check("NavHost_Cache_OtherShown",
                H.FindText("Other Page") is not null);
            H.Check("NavHost_Cache_HomeRemovedFromTree",
                H.FindText("Count: 2") is null);

            // Navigate back — cache hit restores the same control with state intact
            H.ClickButton("Go Home");
            await Harness.Render();
            H.Check("NavHost_Cache_RestoredFromCache_StatePreserved",
                H.FindText("Count: 2") is not null);
            H.Check("NavHost_Cache_OtherRemoved",
                H.FindText("Other Page") is null);
        }
    }

    class CacheCounterPage : Component
    {
        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            return VStack(
                TextBlock($"Count: {count}"),
                Button("Increment", () => setCount(count + 1))
            );
        }
    }
}
