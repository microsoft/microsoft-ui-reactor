
# Reactor for XAML Developers

If you already know XAML, Reactor is not a different Windows UI stack. It still
renders real WinUI controls. The shift is in **how** you describe the UI: instead
of splitting a page across XAML, bindings, converters, and code-behind, you
return the UI directly from C# and let Reactor keep the native control tree in
sync.

## The Mental Model Shift

Think of Reactor as "WinUI controls, but expressed like a function of state."

| In XAML | In Reactor |
|---------|------------|
| `Page`, `UserControl`, `Window` markup | A `Component` with `Render()` |
| `{Binding Name}` | Plain C# variable usage: `TextBlock(name)` |
| `Mode=TwoWay` | Controlled input: `TextField(name, setName)` |
| `DataContext` | Local hook state, typed props, or [context](context.md) |
| `ICommand` | Lambdas, methods, or [commands](commanding.md) |
| `StackPanel` | [`VStack`](layout.md) or [`HStack`](layout.md) |
| `Grid.Row`, `Grid.Column` | `.Grid(row: ..., column: ...)` |
| `StaticResource` / `ThemeResource` | [`Theme`](styling.md) tokens and fluent modifiers |
| `Frame.Navigate(...)` | [`UseNavigation`](navigation.md) + `NavigationHost` |

The important difference is that Reactor does not ask you to describe *bindings*.
It asks you to describe the **current UI**. When the state changes, `Render()`
runs again and Reactor updates only the native WinUI controls that changed.

## Rewriting a Familiar Form

Here is the kind of XAML page many WinUI developers start with:

```xml
<StackPanel Spacing="12" Padding="24">
  <TextBlock Text="Customer" FontSize="24" FontWeight="SemiBold" />

  <TextBox Header="Name"
           Text="{Binding Name, Mode=TwoWay}" />

  <TextBox Header="Email"
           Text="{Binding Email, Mode=TwoWay}" />

  <CheckBox Content="Email me updates"
            IsChecked="{Binding WantsUpdates, Mode=TwoWay}" />

  <Button Content="Save"
          Command="{Binding SaveCommand}" />
</StackPanel>
```

In Reactor, the same screen becomes a component:

```csharp
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
```

What changed:

- **Bindings became state variables.** `Name`, `Email`, and `WantsUpdates` live in `UseState`.
- **Two-way input became explicit.** `TextField(name, setName)` makes data flow obvious.
- **The command became normal C#.** The save button uses a lambda instead of XAML command wiring.
- **Derived UI stayed inline.** `canSave` is just a local expression, not a converter or extra property.

That is the Reactor pattern in one screen: **state at the top, UI returned at the
bottom**.

## Layout Feels Familiar, but Smaller

Most XAML layouts translate directly, but Reactor pushes you toward a smaller set
of composition primitives.

| XAML | Reactor |
|------|---------|
| `StackPanel Orientation="Vertical"` | `VStack(...)` |
| `StackPanel Orientation="Horizontal"` | `HStack(...)` |
| `Grid` with row/column definitions | `Grid(columns: ..., rows: ..., ...)` |
| `Border` | `Border(child)` |
| `ScrollViewer` | `ScrollView(child)` |

This XAML:

```xml
<Grid ColumnSpacing="12" RowSpacing="8">
  <Grid.ColumnDefinitions>
    <ColumnDefinition Width="Auto" />
    <ColumnDefinition Width="*" />
  </Grid.ColumnDefinitions>

  <Grid.RowDefinitions>
    <RowDefinition Height="Auto" />
    <RowDefinition Height="Auto" />
  </Grid.RowDefinitions>

  <TextBlock Grid.Row="0" Grid.Column="0" Text="First name" />
  <TextBox Grid.Row="0" Grid.Column="1" />
  <TextBlock Grid.Row="1" Grid.Column="0" Text="Last name" />
  <TextBox Grid.Row="1" Grid.Column="1" />
</Grid>
```

becomes:

```csharp
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
```

The layout idea is the same. The difference is that the child placement lives in
fluent modifiers instead of attached properties written in markup.

## Bindings Turn into State, Props, or Plain Expressions

XAML developers often look for the Reactor equivalent of `Binding`. There is no
single replacement, because bindings usually solve several different problems.

Use this rule of thumb instead:

- **Value owned by this component:** [`UseState`](hooks.md)
- **Complex local updates:** [`UseReducer`](hooks.md)
- **Input from a parent:** typed props via [`Component<TProps>`](components.md)
- **Computed value:** ordinary local C# expression
- **Shared app state:** [`context`](context.md) or a higher-level parent component
- **Existing MVVM object:** [`UseObservable`](advanced.md) or `UseObservableTree`

That is why Reactor code often looks simpler than XAML. A label like
`TextBlock($"{firstName} {lastName}")` is already "bound" because `Render()`
re-runs whenever the relevant state changes.

## Events and Commands Are Just C#

You do not need a special command layer for every button click.

- `Button("Save", Save)` is the direct equivalent of a button command.
- `Button("Refresh", async () => await ReloadAsync())` works for async actions.
- `TextField(text, setText)` replaces both `TextChanged` wiring and two-way binding.

If you want richer busy/error behavior, Reactor also has a dedicated
[Commanding](commanding.md) API. But the default is deliberately small: use a
method or lambda first, then add command abstractions when they actually help.

## Navigation Without `Frame`

Reactor navigation is still WinUI navigation in spirit, but it is declared in the
component tree instead of being driven by imperative `Frame.Navigate(...)` calls.

```csharp
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
```

Instead of keeping a `Frame` reference and pushing pages into it, you keep a typed
navigation handle in component state and render the current page through
`NavigationHost`. That keeps navigation decisions in the same declarative flow as
the rest of the UI.

## You Can Keep MVVM While Migrating

You do **not** have to throw away existing `INotifyPropertyChanged` view models on
day one. Reactor has a bridge specifically for migration:

```csharp
class ObservableTreeDemo : Component
{
    private static readonly SettingsViewModel _vm = new();

    public override Element Render()
    {
        var vm = UseObservableTree(_vm);

        return VStack(12,
            SubHeading("UseObservableTree"),
            TextField(vm.UserName, v => vm.UserName = v,
                header: "User Name"),
            ToggleSwitch(vm.DarkMode, v => vm.DarkMode = v,
                header: "Dark Mode"),
            Slider(vm.FontSize, 10, 32, v => vm.FontSize = (int)v),
            TextBlock($"Preview: {vm.UserName}")
                .FontSize(vm.FontSize).Bold()
        ).Padding(24);
    }
}
```

`UseObservableTree` subscribes to your existing view model and triggers a
re-render when it changes. That lets you migrate screen-by-screen:

1. Keep the current view model.
2. Replace the XAML view with a Reactor component.
3. Move simple screens from view-model state to hooks later, if you want.

This is usually the least risky way to adopt Reactor in an existing codebase.

## What You Usually Stop Writing

Most XAML developers notice the same things disappear first:

- **No `DataContext` plumbing** for ordinary screens
- **No value converters** for simple formatting or visibility rules
- **No code-behind just to mirror control state**
- **No separate markup file** for routine UI composition
- **Fewer tiny view models** whose only job was exposing bindable properties

The replacement is not "more framework." It is usually just **more ordinary C#**.

## A Practical Migration Path

If you are moving an existing WinUI app to Reactor, this tends to work well:

1. Start with one leaf page, not the whole app shell.
2. Rebuild the layout with `VStack`, `HStack`, `Grid`, and `Border`.
3. Replace bindings with `UseState`, props, or `UseObservableTree`.
4. Inline trivial converters as local expressions.
5. Keep existing services and view models until the Reactor version is stable.
6. Extract reusable UI into small components once the page works.

The goal is not to "port XAML syntax into C#." The goal is to adopt Reactor's
state-driven model while preserving your WinUI knowledge.

## Tips for XAML Developers

**Think "render the current truth."** In XAML, you often describe relationships
between properties. In Reactor, you usually compute the current value directly and
return it.

**Prefer state over control references.** If changing something should update the
screen, store the value in `UseState` instead of reaching into a control instance.

**Use small components where you used `UserControl`.** The same decomposition
instinct still applies; the only change is that the reusable unit is a C#
component rather than a XAML file.

**Keep MVVM only where it earns its keep.** Existing observable objects migrate
well, but many small "binding-only" view models become unnecessary once your UI is
already C#.

## Next Steps

- **[Getting Started](getting-started.md)** — build your first Reactor app from scratch
- **[Components](components.md)** — break UI into reusable typed components
- **[Hooks](hooks.md)** — learn `UseState`, `UseReducer`, `UseEffect`, and the core render model
- **[Layout](layout.md)** — map more WinUI layout patterns into Reactor primitives
- **[Advanced Patterns](advanced.md)** — bridge existing MVVM state with `UseObservableTree`
