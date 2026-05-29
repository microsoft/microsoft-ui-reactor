// id: sidebar-nav
// intent: typed sidebar routing with NavigationView and NavigationHost
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<Shell>("Sidebar Navigation", width: 1000, height: 700);

enum Route { Home, Library, Settings }

class Shell : Component
{
    static string ToTag(Route route) => route.ToString().ToLowerInvariant();
    static Route ToRoute(string tag) => Enum.Parse<Route>(tag, ignoreCase: true);

    public override Element Render()
    {
        var nav = UseNavigation(Route.Home);

        return NavigationView(
            [
                NavItem("Home", icon: "", tag: ToTag(Route.Home)),
                NavItem("Library", icon: "", tag: ToTag(Route.Library)),
                NavItem("Settings", icon: "", tag: ToTag(Route.Settings)),
            ],
            NavigationHost(nav, route => route switch
            {
                Route.Home => Component<HomePage>(),
                Route.Library => Component<LibraryPage>(),
                Route.Settings => Component<SettingsPage>(),
                _ => TextBlock("Not found")
            }))
            .WithNavigation(nav, ToTag, ToRoute);
    }
}

class HomePage : Component
{
    public override Element Render() =>
        VStack(12,
            Heading("Home"),
            TextBlock("Welcome."))
        .Padding(24);
}

class LibraryPage : Component
{
    public override Element Render() =>
        VStack(12,
            Heading("Library"),
            TextBlock("Your stuff."))
        .Padding(24);
}

class SettingsPage : Component
{
    public override Element Render()
    {
        var nav = this.UseNavigation<Route>();

        return VStack(12,
            Heading("Settings"),
            Button("Back to Home", () => nav.Navigate(Route.Home)))
        .Padding(24);
    }
}
