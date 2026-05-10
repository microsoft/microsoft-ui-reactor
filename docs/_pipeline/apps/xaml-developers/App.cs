using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<XamlDevelopersShowcase>("Reactor for XAML Developers", width: 720, height: 620
#if DEBUG
    , preview: true
#endif
);

enum TutorialRoute
{
    Home,
    Settings,
    Account,
}

class XamlDevelopersShowcase : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(24,
                Heading("Reactor for XAML Developers"),
                Caption("Small showcase app used by the docs pipeline."),
                Component<TutorialFormPage>(),
                Component<GridTranslationPage>(),
                Component<TutorialNavigationPage>()
            ).Padding(24)
        );
    }
}

// <snippet:form-page>
class TutorialFormPage : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("");
        var (email, setEmail) = UseState("");
        var (wantsUpdates, setWantsUpdates) = UseState(true);
        var canSave = !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(email);

        return VStack(12,
            SubHeading("Customer"),
            TextField(name, setName, header: "Name"),
            TextField(email, setEmail, header: "Email"),
            CheckBox(wantsUpdates, setWantsUpdates, label: "Email me updates"),
            HStack(8,
                Button("Save", () => { }).Disabled(!canSave),
                TextBlock(canSave ? "Ready to save" : "Complete all required fields")
                    .Opacity(0.7)
            )
        ).Width(360);
    }
}
// </snippet:form-page>

// <snippet:grid-form>
class GridTranslationPage : Component
{
    public override Element Render()
    {
        return Grid(
            columns: [GridSize.Auto, GridSize.Star()],
            rows: [GridSize.Auto, GridSize.Auto],
            TextBlock("First name").Bold().Grid(row: 0, column: 0),
            TextField("", _ => { }).Grid(row: 0, column: 1),
            TextBlock("Last name").Bold().Grid(row: 1, column: 0),
            TextField("", _ => { }).Grid(row: 1, column: 1)
        ) with
        {
            ColumnSpacing = 12,
            RowSpacing = 8
        };
    }
}
// </snippet:grid-form>

// <snippet:nav-shell>
class TutorialNavigationPage : Component
{
    public override Element Render()
    {
        var nav = UseNavigation(TutorialRoute.Home);

        return Border(
            NavigationView(
                [
                    NavItem("Home", icon: "Home", tag: "Home"),
                    NavItem("Settings", icon: "Setting", tag: "Settings"),
                    NavItem("Account", icon: "Contact", tag: "Account")
                ],
                content: NavigationHost(nav, route => route switch
                {
                    TutorialRoute.Home => VStack(8,
                        Heading("Home"),
                        TextBlock("This is the shell root."),
                        Button("Go to Settings", () => nav.Navigate(TutorialRoute.Settings))
                    ).Padding(24),
                    TutorialRoute.Settings => VStack(8,
                        Heading("Settings"),
                        TextBlock("Typed routes replace imperative Frame calls."),
                        Button("Back", () => nav.GoBack())
                    ).Padding(24),
                    TutorialRoute.Account => VStack(8,
                        Heading("Account"),
                        TextBlock("A second page in the same shell.")
                    ).Padding(24),
                    _ => TextBlock("Not found").Padding(24)
                })
            )
        ).Height(320).Background(Theme.CardBackground).CornerRadius(8);
    }
}
// </snippet:nav-shell>
