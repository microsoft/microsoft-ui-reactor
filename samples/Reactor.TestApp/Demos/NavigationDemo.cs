using System.Diagnostics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

abstract record NavRoute;
sealed record NavHome : NavRoute;
sealed record NavDetail(int Id) : NavRoute;
sealed record NavSettings : NavRoute;

class NavigationDemo : Component
{
    public override Element Render()
    {
        var nav = UseNavigation<NavRoute>(new NavHome());
        var (transition, setTransition) = UsePersisted("navTransition", "Slide", PersistedScope.Window);

        var activeTransition = transition switch
        {
            "Fade" => NavigationTransition.Fade(),
            "DrillIn" => NavigationTransition.DrillIn(),
            "Spring" => NavigationTransition.Spring(),
            "None" => NavigationTransition.None,
            _ => NavigationTransition.Slide()
        };

        return VStack(12,
            Heading("Navigation Demo"),
            TextBlock($"Route: {nav.CurrentRoute}  |  Depth: {nav.Depth}  |  Back stack: {nav.BackStack.Count}"),

            HStack(8,
                Button("Back", () => nav.GoBack()).IsEnabled(nav.CanGoBack),
                Button("Forward", () => nav.GoForward()).IsEnabled(nav.CanGoForward),
                Button("Reset", () => nav.Reset(new NavHome())).IsEnabled(!(nav.CurrentRoute is NavHome && nav.Depth == 1))
            ),

            // Transition selector (persisted across tab switches)
            HStack(8,
                TextBlock("Transition:").VAlign(VerticalAlignment.Center),
                ComboBox(["Slide", "Fade", "DrillIn", "Spring", "None"],
                    Array.IndexOf(new[] { "Slide", "Fade", "DrillIn", "Spring", "None" }, transition),
                    i => setTransition(new[] { "Slide", "Fade", "DrillIn", "Spring", "None" }[i])
                ).Width(140)
            ),

            NavigationHost(nav, route => route switch
            {
                NavHome => Component<NavHomePage>(),
                NavDetail d => Component<NavDetailPage, int>(d.Id),
                NavSettings => Component<NavSettingsPage>(),
                _ => TextBlock("Unknown route"),
            }) with { Transition = activeTransition }
        );
    }
}

class NavHomePage : Component
{
    public override Element Render()
    {
        var nav = UseNavigation<NavRoute>();
        var (visitCount, setVisitCount) = UsePersisted("homeVisits", 0, PersistedScope.Window);

        UseNavigationLifecycle(
            onNavigatedTo: ctx => setVisitCount(visitCount + 1));

        return VStack(8,
            SubHeading("Home Page"),
            TextBlock($"Visit count: {visitCount} (persisted across navigations)").Foreground(SecondaryText),
            TextBlock("Select an item to view details:"),
            VStack(4,
                Button("Item #1", () => nav.Navigate(new NavDetail(1))),
                Button("Item #2", () => nav.Navigate(new NavDetail(2))),
                Button("Item #3", () => nav.Navigate(new NavDetail(3)))
            ),
            Button("Go to Settings", () => nav.Navigate(new NavSettings()))
        );
    }
}

class NavDetailPage : Component<int>
{
    public override Element Render()
    {
        var nav = UseNavigation<NavRoute>();
        var id = Props;
        var (notes, setNotes) = UsePersisted($"detail-notes-{id}", "", PersistedScope.Window);

        UseNavigationLifecycle(
            onNavigatingFrom: ctx =>
            {
                // Example: could block navigation if notes are unsaved
            });

        return VStack(8,
            SubHeading($"Detail Page — Item #{id}"),
            TextBlock($"Viewing details for item {id}."),
            TextBox(notes, setNotes)
                .Set(t => t.PlaceholderText = "Notes (persisted via UsePersisted)"),
            HStack(8,
                Button("Home", () => nav.Reset(new NavHome())),
                id < 3
                    ? Button($"Next (Item #{id + 1})", () => nav.Navigate(new NavDetail(id + 1)))
                    : Empty()
            )
        );
    }
}

class NavSettingsPage : Component
{
    public override Element Render()
    {
        var nav = UseNavigation<NavRoute>();

        UseNavigationLifecycle(
            onNavigatedTo: _ => Debug.WriteLine("[Nav] Settings: navigated to"),
            onNavigatedFrom: _ => Debug.WriteLine("[Nav] Settings: navigated from"));

        return VStack(8,
            SubHeading("Settings Page"),
            TextBlock("Application settings would go here."),
            TextBlock("Lifecycle hooks log to debug output.").Foreground(TertiaryText),
            Button("Back to Home", () => nav.Reset(new NavHome()))
        );
    }
}
