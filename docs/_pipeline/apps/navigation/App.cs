using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

ReactorApp.Run<NavigationApp>("Navigation", width: 800, height: 700
#if DEBUG
    , preview: true
#endif
);

internal sealed partial class DocsFrameDemoPage : Page
{
    public DocsFrameDemoPage()
    {
        Content = new TextBlock { Text = "Inside Frame" };
    }
}

// <snippet:route-enum>
enum Route { Home, Settings, Profile, Details }
// </snippet:route-enum>

// <snippet:basic-navigation>
class BasicNavDemo : Component
{
    public override Element Render()
    {
        var nav = UseNavigation(Route.Home);

        return VStack(12,
            SubHeading($"Current: {nav.CurrentRoute}"),
            TextBlock($"Stack depth: {nav.Depth}"),
            HStack(8,
                Button("Home", () => nav.Navigate(Route.Home)),
                Button("Settings", () => nav.Navigate(Route.Settings)),
                Button("Profile", () => nav.Navigate(Route.Profile)),
                Button("Back", () => nav.GoBack())
                    .IsEnabled(nav.CanGoBack)
            ),
            NavigationHost(nav, route => route switch
            {
                Route.Home => TextBlock("Welcome home!").FontSize(18).Padding(16),
                Route.Settings => TextBlock("Settings page").FontSize(18).Padding(16),
                Route.Profile => TextBlock("Your profile").FontSize(18).Padding(16),
                _ => TextBlock("Not found").Padding(16)
            })
        ).Padding(24);
    }
}
// </snippet:basic-navigation>

// <snippet:navigation-view>
class NavViewDemo : Component
{
    public override Element Render()
    {
        var nav = UseNavigation(Route.Home);
        return NavigationView(
            [
                NavItem("Home", icon: "Home", tag: "Home"),
                NavItem("Settings", icon: "Setting", tag: "Settings"),
                NavItem("Profile", icon: "Contact", tag: "Profile")
            ],
            content: NavigationHost(nav, route => route switch
            {
                Route.Home => VStack(12, Heading("Home"),
                    TextBlock("Welcome to the app."),
                    Button("Go to Settings",
                        () => nav.Navigate(Route.Settings))).Padding(24),
                Route.Settings => VStack(12, Heading("Settings"),
                    TextBlock("Configure your preferences."),
                    Button("Back", () => nav.GoBack())).Padding(24),
                Route.Profile => VStack(12, Heading("Profile"),
                    TextBlock("View your profile info.")).Padding(24),
                _ => TextBlock("Not found").Padding(24)
            })
        );
    }
}
// </snippet:navigation-view>

// <snippet:stack-operations>
class StackOperationsDemo : Component
{
    public override Element Render()
    {
        var nav = UseNavigation(Route.Home);

        return VStack(12,
            SubHeading($"Current: {nav.CurrentRoute}"),
            TextBlock($"Back stack: {nav.BackStack.Count}"),
            TextBlock($"Forward stack: {nav.ForwardStack.Count}"),
            HStack(8,
                Button("Navigate", () =>
                    nav.Navigate(Route.Settings)),
                Button("Replace", () =>
                    nav.Replace(Route.Profile)),
                Button("Reset", () =>
                    nav.Reset(Route.Home)),
                Button("Back", () => nav.GoBack())
                    .IsEnabled(nav.CanGoBack),
                Button("Forward", () => nav.GoForward())
                    .IsEnabled(nav.CanGoForward)
            ),
            NavigationHost(nav, route =>
                TextBlock($"Page: {route}")
                    .FontSize(18).Padding(16))
        ).Padding(24);
    }
}
// </snippet:stack-operations>

// <snippet:lifecycle>
class LifecyclePage : Component
{
    public override Element Render()
    {
        var (log, updateLog) = UseReducer(new List<string>());

        UseNavigationLifecycle(
            onNavigatedTo: ctx =>
                updateLog(l => [.. l,
                    $"Arrived from {ctx.PreviousRoute}"]),
            onNavigatingFrom: ctx =>
                updateLog(l => [.. l,
                    $"Leaving to {ctx.TargetRoute}"]),
            onNavigatedFrom: ctx =>
                updateLog(l => [.. l,
                    $"Left for {ctx.TargetRoute}"])
        );

        return VStack(8,
            SubHeading("Lifecycle Events"),
            VStack(4,
                log.TakeLast(5).Select(entry =>
                    TextBlock(entry).FontSize(12).Opacity(0.7)
                ).ToArray()
            )
        ).Padding(16);
    }
}
// </snippet:lifecycle>

// <snippet:page-transitions>
class PageTransitionsDemo : Component
{
    public override Element Render()
    {
        var nav = UseNavigation(Route.Home);

        return VStack(12,
            SubHeading("Page Transitions"),
            HStack(8,
                Button("Home", () => nav.Navigate(Route.Home)),
                Button("Settings", () => nav.Navigate(Route.Settings)),
                Button("Profile", () => nav.Navigate(Route.Profile)),
                Button("Back", () => nav.GoBack())
                    .IsEnabled(nav.CanGoBack)
            ),
            NavigationHost(nav, route => route switch
            {
                Route.Home => VStack(8,
                    TextBlock("Home").FontSize(24).Bold(),
                    TextBlock("Slide transition on navigate")).Padding(16),
                Route.Settings => VStack(8,
                    TextBlock("Settings").FontSize(24).Bold(),
                    TextBlock("DrillIn transition to detail")).Padding(16),
                _ => TextBlock($"{route}").FontSize(18).Padding(16)
            }) with { Transition = NavigationTransition.DrillIn() }
        ).Padding(24);
    }
}
// </snippet:page-transitions>

// <snippet:deep-linking>
class DeepLinkingDemo : Component
{
    public override Element Render()
    {
        var map = UseMemo(() => new DeepLinkMap<Route>()
            .Map("/", _ => Route.Home)
            .Map("/settings", _ => Route.Settings)
            .Map("/profile", _ => Route.Profile));

        var (result, setResult) = UseState("(none)");

        return VStack(12,
            SubHeading("Deep Linking"),
            HStack(8,
                Button("Resolve /", () =>
                    setResult($"/ -> {map.Resolve("/").Matched}")),
                Button("Resolve /settings", () =>
                    setResult($"/settings -> {map.Resolve("/settings").Matched}")),
                Button("Resolve /unknown", () =>
                    setResult($"/unknown -> {map.Resolve("/unknown").Matched}"))
            ),
            TextBlock($"Result: {result}").FontSize(14).Opacity(0.7)
        ).Padding(24);
    }
}
// </snippet:deep-linking>

// <snippet:deep-link-query>
class DeepLinkQueryDemo : Component
{
    public override Element Render()
    {
        var (info, setInfo) = UseState("(none)");

        // RouteArgs are available inside the factory lambda —
        // capture typed params and query values there
        var map = UseMemo(() => new DeepLinkMap<Route>()
            .Map("/", _ => Route.Home)
            .Map("/settings", _ => Route.Settings)
            .Map("/users/{id:int}/posts/{postId:int}",
                args =>
                {
                    var userId = args.Get<int>("id");
                    var postId = args.Get<int>("postId");
                    var sort = args.Query<string>("sort");
                    setInfo($"userId={userId}, postId={postId}, sort={sort}");
                    return Route.Details;
                },
                () => new[] { Route.Home })
        );

        return VStack(12,
            SubHeading("Deep Link Query Params"),
            Button("Resolve /users/42/posts/7?sort=date", () =>
                map.Resolve("/users/42/posts/7?sort=date")),
            TextBlock($"Captured: {info}").FontSize(14).Opacity(0.7)
        ).Padding(24);
    }
}
// </snippet:deep-link-query>

// <snippet:state-serialization>
class StateSerializationDemo : Component
{
    public override Element Render()
    {
        var nav = UseNavigation(Route.Home);
        var (savedJson, setSavedJson) = UseState<string?>(null);

        return VStack(12,
            SubHeading("State Serialization"),
            HStack(8,
                Button("Navigate", () =>
                    nav.Navigate(Route.Settings)),
                Button("Save State", () =>
                    setSavedJson(System.Text.Json.JsonSerializer.Serialize(
                        nav.GetState(), DocsNavJsonContext.Default.NavigationStateRoute))),
                Button("Restore State", () =>
                {
                    if (savedJson is not null)
                    {
                        var state = System.Text.Json.JsonSerializer.Deserialize(
                            savedJson, DocsNavJsonContext.Default.NavigationStateRoute);
                        if (state is not null) nav.SetState(state);
                    }
                }).IsEnabled(savedJson is not null)
            ),
            TextBlock($"Current: {nav.CurrentRoute}"),
            TextBlock($"Saved: {savedJson?[..Math.Min(50, savedJson?.Length ?? 0)] ?? "(none)"}")
                .FontSize(12).Opacity(0.6),
            NavigationHost(nav, route =>
                TextBlock($"Page: {route}").Padding(16))
        ).Padding(24);
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(NavigationState<Route>))]
partial class DocsNavJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
// </snippet:state-serialization>

// <snippet:diagnostics>
class DiagnosticsDemo : Component
{
    public override Element Render()
    {
        var nav = UseNavigation(Route.Home);
        var (log, updateLog) = UseReducer(new List<string>());

        UseEffect(() =>
        {
            EventHandler<NavigationDiagnosticEvent> onRequested =
                (_, e) => updateLog(l => [.. l, $"Requested: {e.From} → {e.To}"]);
            EventHandler<NavigationDiagnosticEvent> onCompleted =
                (_, e) => updateLog(l => [.. l, $"Completed: {e.To}"]);

            NavigationDiagnostics.NavigationRequested += onRequested;
            NavigationDiagnostics.NavigationCompleted += onCompleted;
            return () =>
            {
                NavigationDiagnostics.NavigationRequested -= onRequested;
                NavigationDiagnostics.NavigationCompleted -= onCompleted;
            };
        });

        return VStack(12,
            SubHeading("Navigation Diagnostics"),
            HStack(8,
                Button("Home", () => nav.Navigate(Route.Home)),
                Button("Settings", () => nav.Navigate(Route.Settings))
            ),
            VStack(4, log.TakeLast(6).Select(
                entry => TextBlock(entry).FontSize(11).Opacity(0.6)
            ).ToArray()),
            NavigationHost(nav, route =>
                TextBlock($"Page: {route}").Padding(16))
        ).Padding(24);
    }
}
// </snippet:diagnostics>

// <snippet:page-caching>
class PageCachingDemo : Component
{
    public override Element Render()
    {
        var nav = UseNavigation(Route.Home);

        return VStack(12,
            SubHeading("Page Caching"),
            TextBlock("Text input is preserved across navigations."),
            HStack(8,
                Button("Home", () => nav.Navigate(Route.Home)),
                Button("Settings", () => nav.Navigate(Route.Settings)),
                Button("Back", () => nav.GoBack())
                    .IsEnabled(nav.CanGoBack)
            ),
            NavigationHost(nav, route => route switch
            {
                Route.Home => CachedPage("Home"),
                Route.Settings => CachedPage("Settings"),
                _ => TextBlock($"{route}").Padding(16)
            }) with
            {
                CacheMode = NavigationCacheMode.Enabled,
                CacheSize = 5
            }
        ).Padding(24);
    }

    static Element CachedPage(string name) =>
        VStack(8,
            TextBlock(name).FontSize(20).Bold(),
            TextBox("", _ => { }, placeholderText: "Type here — state persists")
        ).Padding(16);
}
// </snippet:page-caching>

// <snippet:tab-navigation>
class TabNavDemo : Component
{
    public override Element Render()
    {
        return TabView(
            Tab("Documents",
                VStack(12,
                    TextBlock("Your documents appear here."),
                    Button("New Document", () => { })
                ).Padding(24)
            ),
            Tab("Recent",
                VStack(12,
                    TextBlock("Recently opened files."),
                    TextBlock("No recent files.").Opacity(0.5)
                ).Padding(24)
            ),
            Tab("Shared",
                VStack(12,
                    TextBlock("Files shared with you."),
                    TextBlock("Nothing shared yet.").Opacity(0.5)
                ).Padding(24)
            )
        );
    }
}
// </snippet:tab-navigation>

// <snippet:nav-selected-tag-changed>
class SelectedTagChangedDemo : Component
{
    public override Element Render()
    {
        var nav = UseNavigation(Route.Home);
        var (lastTag, setLastTag) = UseState<string?>(null);

        return NavigationView(
            [
                NavItem("Home", icon: "Home", tag: "Home"),
                NavItem("Settings", icon: "Setting", tag: "Settings")
            ],
            content: VStack(12,
                Heading(lastTag ?? "Home"),
                TextBlock("Last selected tag: " + (lastTag ?? "(none)"))
                    .Opacity(0.6)
            ).Padding(24)
        ).SelectedTagChanged(tag =>
        {
            setLastTag(tag);
            if (tag == "Settings") nav.Navigate(Route.Settings);
        });
    }
}
// </snippet:nav-selected-tag-changed>

// <snippet:frame-events>
class FrameEventsDemo : Component
{
    public override Element Render()
    {
        var (log, updateLog) = UseReducer(new List<string>());

        return VStack(8,
            SubHeading("Frame events"),
            Frame(sourcePageType: typeof(DocsFrameDemoPage))
                .Navigating(target =>
                    updateLog(l => [.. l, $"Navigating to {target.Name}"]))
                .Navigated(target =>
                    updateLog(l => [.. l, $"Arrived at {target.Name}"]))
                .NavigationFailed((target, ex) =>
                    updateLog(l => [.. l, $"Failed {target.Name}: {ex.Message}"]))
                .Height(300),
            VStack(4, log.TakeLast(6).Select(
                entry => TextBlock(entry).FontSize(11).Opacity(0.6)
            ).ToArray())
        ).Padding(24);
    }
}
// </snippet:frame-events>

class NavigationApp : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(24,
                Heading("Navigation"),
                Component<BasicNavDemo>(),
                Component<StackOperationsDemo>(),
                Component<PageTransitionsDemo>(),
                Component<DeepLinkingDemo>(),
                Component<PageCachingDemo>(),
                Component<TabNavDemo>(),
                Component<FrameEventsDemo>(),
                Component<SelectedTagChangedDemo>()
            ).Padding(24)
        );
    }
}
