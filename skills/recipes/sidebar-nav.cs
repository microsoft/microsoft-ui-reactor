// Recipe: sidebar navigation between pages.
//
// Pattern: route enum → UseNavigation handle at the root → NavigationView
// supplies the sidebar UI, items keyed by string tag → NavigationHost renders
// the matched page → .WithNavigation(nav, routeToTag, tagToRoute) wires it up
// so clicking a sidebar item calls nav.Navigate(route). Pages can pull the
// shared handle via UseNavigation<Route>().

// In this clone, run `mur pack-local` once. Bump the version below to match
// whatever `mur pack-local` printed (default: 0.0.0-local). For a real NuGet
// consumer, set Version to a published Microsoft.UI.Reactor release.
#:package Microsoft.UI.Reactor@0.0.0-local
#:package Microsoft.WindowsAppSDK@2.0.1
#:property OutputType=WinExe
#:property TargetFramework=net10.0-windows10.0.22621.0
#:property UseWinUI=true
#:property WindowsPackageType=None

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<Shell>("Sidebar demo", width: 1000, height: 700);

enum Route { Home, Library, Settings }

class Shell : Component
{
    static string ToTag(Route r) => r.ToString().ToLowerInvariant();
    static Route ToRoute(string t) => Enum.Parse<Route>(t, ignoreCase: true);

    public override Element Render()
    {
        var nav = UseNavigation(Route.Home);

        return NavigationView(
            [
                NavItem("Home",     icon: "", tag: ToTag(Route.Home)),
                NavItem("Library",  icon: "", tag: ToTag(Route.Library)),
                NavItem("Settings", icon: "", tag: ToTag(Route.Settings)),
            ],
            NavigationHost(nav, route => route switch
            {
                Route.Home     => Component<HomePage>(),
                Route.Library  => Component<LibraryPage>(),
                Route.Settings => Component<SettingsPage>(),
                _ => TextBlock("Not found")
            })
        ).WithNavigation(nav, ToTag, ToRoute);
    }
}

class HomePage : Component
{
    public override Element Render() =>
        VStack(12, Heading("Home"), TextBlock("Welcome.")).Padding(24);
}

class LibraryPage : Component
{
    public override Element Render() =>
        VStack(12, Heading("Library"), TextBlock("Your stuff.")).Padding(24);
}

class SettingsPage : Component
{
    public override Element Render()
    {
        var nav = this.UseNavigation<Route>();
        return VStack(12,
            Heading("Settings"),
            Button("Back to Home", () => nav.Navigate(Route.Home))
        ).Padding(24);
    }
}
