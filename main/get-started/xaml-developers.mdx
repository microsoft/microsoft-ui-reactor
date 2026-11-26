---
title: "Coming from XAML"
description: "If you know XAML, you already know most of Reactor. Reactor renders the same WinUI controls, with the same layout panels, theme resources and input events. What changes is how you describe them."
---

In XAML you split a page across markup,
bindings, converters, styles and code-behind. In Reactor you write one C#
component. Its `Render()` method returns the UI for the current state. When the
state changes, Reactor runs `Render()` again and updates only the controls that
changed. This page maps each XAML and MVVM concept to its Reactor form, shows
the two side by side, and explains how to adopt Reactor one screen at a time.
For the general mental model, read [Thinking in Reactor](/microsoft-ui-reactor/main/get-started/thinking-in-reactor)
first.

![A XAML page split across markup, code-behind, a view model, converters and bindings becomes one Reactor component whose Render method returns the UI from hook state and plain C# expressions](/microsoft-ui-reactor/main/images/xaml-developers/mental-model-shift.svg)

## Concept map [#concept-map]

| XAML or MVVM | Reactor | Learn more |
|---|---|---|
| `Page`, `UserControl` | A `Component` class with a `Render()` method | [Components](/microsoft-ui-reactor/main/guides/components) |
| Code-behind | Code in `Render()`, plus normal methods and lambdas | [Components](/microsoft-ui-reactor/main/guides/components) |
| `{Binding}`, `{x:Bind}`, `INotifyPropertyChanged` | State from `UseState` or `UseReducer`, read in `Render()` | [Bindings become state](#bindings-become-state) |
| `Mode=TwoWay` | A value and a callback: `TextBox(name, setName)` | [Forms and Input](/microsoft-ui-reactor/main/controls/forms) |
| `IValueConverter` | A C# expression in `Render()` | [Bindings become state](#bindings-become-state) |
| `DataContext` | Props from the parent, or context for shared values | [Context](/microsoft-ui-reactor/main/guides/context) |
| `DependencyProperty` | A field on a props record, or hook state | [DependencyProperty](#dependencyproperty-becomes-a-prop-or-a-hook) |
| `DataTemplate` | A method or lambda that returns an element | [Collections](/microsoft-ui-reactor/main/controls/collections) |
| `Style`, `Setter`, `BasedOn` | Modifiers and helper methods | [Styling and Theming](/microsoft-ui-reactor/main/guides/styling) |
| `ThemeResource`, `ResourceDictionary` | `Theme` tokens, `Theme.Ref(key)` and `.Resources(...)` | [Theming Tokens](/microsoft-ui-reactor/main/guides/theming-tokens) |
| `ICommand` | A method or lambda, or a `Command` record | [Commanding](/microsoft-ui-reactor/main/guides/commanding) |
| `x:Name` | `UseElementRef<T>()` and `.Ref(...)` | [Advanced Patterns](/microsoft-ui-reactor/main/guides/advanced) |
| `VisualStateManager` | Branches in `Render()`, plus `.InteractionStates(...)` | [Animation](/microsoft-ui-reactor/main/guides/animation) |
| `Storyboard` | `.Animate(...)`, `.Transition(...)` and `AnimationScope.WithAnimation(...)` | [Animation](/microsoft-ui-reactor/main/guides/animation) |
| `Visibility` with a converter | `When(condition, ...)`, a `?:` expression, or `.IsVisible(bool)` | [Bindings become state](#bindings-become-state) |
| `Frame.Navigate(...)` | `UseNavigation` and `NavigationHost` | [Navigation](/microsoft-ui-reactor/main/guides/navigation) |
| `StackPanel`, `Grid`, attached properties | `VStack`, `HStack`, `Grid` and `.Grid(row:, column:)` | [Layout](/microsoft-ui-reactor/main/guides/layout) |

The rest of the page walks through these rows with code. Reactor has no
separate binding, style or template system. Each row becomes ordinary C#.

## A familiar form, rewritten [#a-familiar-form-rewritten]

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

In Reactor, the same screen is one component:

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
            TextBox(name, setName, header: "Name"),
            TextBox(email, setEmail, header: "Email"),
            CheckBox(wantsUpdates, setWantsUpdates, label: "Email me updates"),
            HStack(8,
                Button("Save", () => { }).IsEnabled(canSave),
                TextBlock(canSave ? "Ready to save" : "Complete all required fields")
                    .Opacity(0.7)
            )
        ).Width(360);
    }
}
```

What changed:

- **Bindings became state.** `name`, `email` and `wantsUpdates` live in `UseState`.
- **Two-way input became explicit.** `TextBox(name, setName)` shows the value and
  the callback that changes it.
- **The command became normal C#.** The button takes a lambda, not a command binding.
- **Derived values stayed inline.** `canSave` is a local expression, not a converter
  or an extra view-model property.

That is the Reactor pattern in one screen: state at the top, UI returned at the
bottom.

## Bindings become state [#bindings-become-state]

![XAML pull-based binding, where a BindingExpression listens to PropertyChanged and reads the view-model getter, next to Reactor push from state, where a UseState setter runs Render and the reconciler writes the new value to the WinUI control](/microsoft-ui-reactor/main/images/xaml-developers/push-vs-pull.svg)

In XAML, a binding is an object. `{Binding Name}` creates a `BindingExpression`
that listens to `PropertyChanged` on the view model. When the event fires, the
binding reads the new value and writes it to the control. The control pulls the
value.

Reactor has no binding objects. `TextBlock(name)` reads the current value of the
`name` variable and puts it in the element. When you call `setName`, Reactor runs
`Render()` again, compares the new elements with the old ones, and writes the new
text to the same `TextBlock`. The state pushes the value to the control. The
[Reactivity Model](/microsoft-ui-reactor/main/how-it-works/reactivity-model) page explains this cycle in depth.

A binding usually does one of several jobs. Pick the Reactor tool by the job:

- **A value this component owns:** [`UseState`](/microsoft-ui-reactor/main/guides/hooks).
- **Several values that change together:** [`UseReducer`](/microsoft-ui-reactor/main/guides/hooks).
- **A value from a parent:** typed props with [`Component<TProps>`](/microsoft-ui-reactor/main/guides/components).
- **A computed value or a converter:** a C# expression in `Render()`.
- **Visibility:** `When(condition, () => ...)`, a `?:` expression, or `.IsVisible(bool)`.
- **State that many components share:** [Context](/microsoft-ui-reactor/main/guides/context), or state in a common parent.
- **An existing MVVM object:** `UseObservable` or `UseObservableTree`, described
  in [Keep your view models](#keep-your-view-models).

This is why Reactor code often looks shorter than XAML. A label like
`TextBlock($"{firstName} {lastName}")` is already "bound", because `Render()` runs
again whenever that state changes.

> **Caveat:** There are no binding modes in Reactor. Do not look for `Mode=OneWay`,
> `Mode=OneTime` or `Mode=TwoWay`. Every render reads the current state, and every
> edit calls your callback.
> 
> - For `TwoWay`, pass the value and a setter: `TextBox(name, setName)`.
> - For a value the user must not edit, show it in a `TextBlock`, or call
>   `.IsReadOnly()` on the `TextBox`.
> - For `OneTime`, keep the first value. `UseRef(value)` uses its argument only on
>   the first render, so `ref.Current` keeps that value until you change it.
> 
> Also, do not attach a `{Binding}` to a control that Reactor created, and do not
> set its properties from an effect or through a ref. Reactor does not know about
> those writes, so a later render can overwrite them. Drive the property from
> state instead.

## Layout [#layout]

Most XAML layouts translate directly. Reactor uses a small set of layout
primitives:

| XAML | Reactor |
|------|---------|
| `StackPanel Orientation="Vertical"` | `VStack(...)` |
| `StackPanel Orientation="Horizontal"` | `HStack(...)` |
| `Grid` with row and column definitions | `Grid(columns: ..., rows: ..., ...)` |
| `Border` | `Border(child)` |
| `ScrollViewer` (classic) | `ScrollViewer(child)` |
| `ScrollView` (modern) | `ScrollView(child)` |

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
            TextBox("", _ => { }).AutomationName("First name").Grid(row: 0, column: 1),
            TextBlock("Last name").Bold().Grid(row: 1, column: 0),
            TextBox("", _ => { }).AutomationName("Last name").Grid(row: 1, column: 1)
        ) with
        {
            ColumnSpacing = 12,
            RowSpacing = 8
        };
    }
}
```

The layout is the same. Attached properties such as `Grid.Row` become fluent
modifiers such as `.Grid(row: 1, column: 0)`. See [Layout](/microsoft-ui-reactor/main/guides/layout) for the full
set of panels.

## DataTemplate becomes a method [#datatemplate-becomes-a-method]

In XAML, a `DataTemplate` describes how to show one item:

```xml
<ItemsRepeater ItemsSource="{x:Bind ViewModel.Products}">
  <ItemsRepeater.Layout>
    <StackLayout Spacing="8" />
  </ItemsRepeater.Layout>
  <ItemsRepeater.ItemTemplate>
    <DataTemplate x:DataType="local:Product">
      <Border Style="{StaticResource CardSurfaceStyle}">
        <StackPanel Spacing="4">
          <TextBlock Text="{x:Bind Name}" FontSize="16" FontWeight="SemiBold" />
          <TextBlock Text="{x:Bind Category}"
                     Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
        </StackPanel>
      </Border>
    </DataTemplate>
  </ItemsRepeater.ItemTemplate>
</ItemsRepeater>
```

In Reactor, the template is a method that returns an element:

```csharp
// The "template" is an ordinary method that returns an Element.
// ForEach calls it once for each item, on every render.
static Element ProductList(IReadOnlyList<Product> items) =>
    VStack(8, ForEach(items, item => Card(item).WithKey(item.Id.ToString())));

static Element Card(Product item) =>
    CardSurface(VStack(4,
        TextBlock(item.Name).FontSize(16).SemiBold(),
        TextBlock(item.Category).Foreground(Theme.SecondaryText)));
```

`Card` is the template. `ForEach` calls it for each item on every render and
returns the results as children of the `VStack`. `.WithKey(...)` gives each item
a stable identity, so Reactor matches items across renders even when the list is
sorted or filtered. See [Reconciliation](/microsoft-ui-reactor/main/how-it-works/reconciliation) for how keys work.

`ForEach` is for short lists inside a stack. It creates every item and does not
virtualize. For long or scrolling lists, use a typed collection such as
`ListView<T>`, `GridView<T>` or `LazyVStack<T>`. Each takes your items, a key
selector and an item function. See [Collections](/microsoft-ui-reactor/main/controls/collections) to choose one.

## Styles become methods and modifiers [#styles-become-methods-and-modifiers]

A XAML `Style` applies a set of property setters to a control:

```xml
<Page.Resources>
  <Style x:Key="CardSurfaceStyle" TargetType="Border">
    <Setter Property="Background"
            Value="{ThemeResource CardBackgroundFillColorDefaultBrush}" />
    <Setter Property="CornerRadius" Value="8" />
    <Setter Property="Padding" Value="16" />
  </Style>
</Page.Resources>

<Border Style="{StaticResource CardSurfaceStyle}">
  <TextBlock Text="Hello" />
</Border>
```

In Reactor, a shared style is a method that applies modifiers:

```csharp
// A shared "style" is a plain method that wraps a child and applies
// modifiers. There is no registry and no TargetType check.
static Element CardSurface(Element child) =>
    Border(child)
        .Background(Theme.CardBackground)
        .CornerRadius(8)
        .Padding(16);
```

Call `CardSurface(TextBlock("Hello"))` wherever you used the style. The method is
plain C#, so `BasedOn` becomes one method that calls another. There is no style
registry and no `TargetType` check.

For WinUI's built-in named styles, use the named-style fluents:

- `.AccentButton()` and `.SubtleButton()` on buttons.
- `.TextLink()` on a `HyperlinkButton`.
- `.Informational()`, `.Success()`, `.Warning()` and `.Error()` on an `InfoBar`.

To restyle one control the way WinUI lightweight styling does, override its
resource keys with `.Resources(...)`. The next section shows an example. See
[Styling and Theming](/microsoft-ui-reactor/main/guides/styling) for the full styling surface.

## Theme resources become Theme tokens [#theme-resources-become-theme-tokens]

In XAML, `{ThemeResource}` picks a brush from the active light or dark theme
dictionary:

```xml
<StackPanel Spacing="12">
  <Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
          BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
          BorderThickness="1" CornerRadius="8" Padding="16">
    <StackPanel Spacing="4">
      <TextBlock Text="Storage" FontWeight="SemiBold" />
      <TextBlock Text="12 GB of 50 GB used"
                 Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
      <TextBlock Text="Last synced 5 minutes ago"
                 Foreground="{ThemeResource TextFillColorTertiaryBrush}" />
    </StackPanel>
  </Border>

  <Border BorderBrush="{ThemeResource ControlStrongStrokeColorDefaultBrush}"
          BorderThickness="1" CornerRadius="8" Padding="12">
    <TextBlock Text="Sync is paused" />
  </Border>

  <Button Content="Delete">
    <Button.Resources>
      <StaticResource x:Key="ButtonBackground" ResourceKey="SystemFillColorCriticalBrush" />
      <SolidColorBrush x:Key="ButtonForeground" Color="White" />
    </Button.Resources>
  </Button>
</StackPanel>
```

In Reactor, the same resources come from `Theme`:

```csharp
class ThemeResourceExample : Component
{
    public override Element Render() =>
        VStack(12,
            // Named tokens for common {ThemeResource} brushes.
            Border(VStack(4,
                    TextBlock("Storage").SemiBold(),
                    TextBlock("12 GB of 50 GB used").Foreground(Theme.SecondaryText),
                    TextBlock("Last synced 5 minutes ago").Foreground(Theme.TertiaryText)))
                .Background(Theme.CardBackground)
                .BorderBrush(Theme.CardStroke)
                .BorderThickness(1)
                .CornerRadius(8)
                .Padding(16),
            // Any other theme resource, by its key.
            Border(TextBlock("Sync is paused"))
                .BorderBrush(Theme.Ref("ControlStrongStrokeColorDefaultBrush"))
                .BorderThickness(1)
                .CornerRadius(8)
                .Padding(12),
            // A resource override for one control and its children.
            Button("Delete", () => { })
                .Resources(r => r
                    .Set("ButtonBackground", Theme.SystemCritical)
                    .Set("ButtonForeground", "#FFFFFF")));
}
```

- `Theme` has named tokens for the common WinUI brushes, such as
  `Theme.CardBackground` and `Theme.SecondaryText`.
- `Theme.Ref(key)` reaches any other resource key, including keys your app
  defines.
- `.Resources(...)` overrides resource keys for one control and its children,
  like `Button.Resources` in XAML.
- A token modifier such as `.Background(Theme.Accent)` sets the property
  through a `{ThemeResource}` reference, so WinUI picks the brush for the
  control's own theme. A dark subtree in a light app gets the dark brush, as in
  XAML.

When the user switches between light and dark, Reactor clears its cache of
resolved brushes and renders again. Every token picks up the new theme, and you
do not handle `ActualThemeChanged` yourself. See
[Theming Tokens](/microsoft-ui-reactor/main/guides/theming-tokens) for the full list of tokens.

## Commands and events are C# [#commands-and-events-are-c]

In XAML, a button usually binds to an `ICommand` on the view model:

```xml
<TextBox Header="Document" Text="{x:Bind ViewModel.Text, Mode=TwoWay}" />
<Button Content="Save" Command="{x:Bind ViewModel.Save}">
  <Button.KeyboardAccelerators>
    <KeyboardAccelerator Modifiers="Control" Key="S" />
  </Button.KeyboardAccelerators>
</Button>
```

Most Reactor buttons do not need a command. `Button("Save", Save)` calls a
method, and `Button("+", () => setCount(count + 1))` runs a lambda. When one
action needs a label, an icon, a shortcut and an enabled state in one place, use
a `Command` record:

```csharp
class BasicCommandExample : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState("Hello, World!");
        var (saved, setSaved) = UseState(false);

        var saveCmd = new Command
        {
            Label = "Save",
            Execute = () => setSaved(true),
            CanExecute = !saved,
            Icon = SymbolIcon("Save"),
            Accelerator = Accelerator(VirtualKey.S, VirtualKeyModifiers.Control)
        };

        return VStack(12,
            TextBox(text, v => { setText(v); setSaved(false); }, header: "Document")
                .Width(400),
            HStack(8,
                Button(saveCmd),
                When(saved, () => TextBlock("Saved!").Foreground(Theme.SystemSuccess))
            )
        ).Padding(24);
    }
}
```

`CanExecute` is a plain `bool` that you compute in `Render()`. There is no
`CanExecuteChanged` event to raise: when `saved` changes, the component renders
again with the new value. See [Commanding](/microsoft-ui-reactor/main/guides/commanding) for async commands
with a busy state.

### Event attributes become fluents [#event-attributes-become-fluents]

Every WinUI event attribute has a matching Reactor fluent. For most events,
the fluent is the Reactor property name without the leading `On`. A few
fluents simplify the WinUI event shape. For example, a XAML `CheckBox` has
separate `Checked`, `Unchecked` and `Indeterminate` events, and Reactor gives
you one `IsCheckedChanged` callback.

| WinUI XAML event | Reactor fluent |
|------------------|----------------|
| `<Button Click="OnClick"/>` | `Button("…").Click(handler)` |
| `<TextBox TextChanged="OnTextChanged"/>` | `TextBox(text, setText).Changed(handler)` |
| `<ListView SelectionChanged="OnSelectionChanged"/>` | `ListView<T>(...).SelectionChanged(handler)` |
| `<ComboBox SelectionChanged="OnSelectionChanged"/>` | `ComboBox(...).SelectedIndexChanged(handler)`. Reactor passes the selected index, not the event args. |
| `<CheckBox Checked="…" Unchecked="…"/>` | `CheckBox(value, setValue).IsCheckedChanged(handler)`. Reactor passes one `bool`. |

The element property keeps the `On` prefix, so code that sets it in an object
initializer still compiles:

```csharp
class EventsFluentExample : Component
{
    public override Element Render()
    {
        Action handler = () => { /* clicked */ };

        return VStack(8,
            // Property-init still works:
            new ButtonElement("Save") { OnClick = handler },
            // Preferred fluent:
            Button("Save").Click(handler)
        );
    }
}
```

The fluent drops the `On` because C# treats `el.OnClick(handler)` as a call to
the delegate stored in the `OnClick` property, not as an extension method.
[Spec 039](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/specs/039-property-and-event-scrub.md)
§0.1 records this decision. Passing `null` to any of these fluents clears the
handler.

## x:Name becomes a ref [#xname-becomes-a-ref]

In XAML, `x:Name` gives code-behind a field for a control:

```xml
<TextBox x:Name="NameField" PlaceholderText="Name" />
<Button Content="Focus the field" Click="OnFocusClick" />
```

The `OnFocusClick` handler then calls `NameField.Focus(FocusState.Programmatic)`.
In Reactor, ask for a ref with `UseElementRef<T>()` and attach it with `.Ref(...)`:

```csharp
class ElementRefFocusDemo : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("");
        var fieldRef = this.UseElementRef<TextBox>();

        return VStack(12,
            SubHeading("Imperative focus via ElementRef<T>"),
            TextBox(name, setName, placeholderText: "Name")
                .AutomationName("Name")
                .Ref(fieldRef),
            Button("Focus the field", () =>
                fieldRef.Current?.Focus(FocusState.Programmatic))
        ).Padding(24);
    }
}
```

`fieldRef.Current` is the real `TextBox` once it is mounted, and `null` before
that. Use refs for imperative calls such as focus and scrolling. Do not use them
to set properties: put those values in state. See
[Advanced Patterns](/microsoft-ui-reactor/main/guides/advanced) for more on refs.

## Visual states become branches in Render [#visual-states-become-branches-in-render]

In XAML, the `VisualStateManager` switches between named states:

```xml
<VisualStateManager.VisualStateGroups>
  <VisualStateGroup x:Name="LoadStates">
    <VisualState x:Name="Loading">
      <VisualState.Setters>
        <Setter Target="Spinner.Visibility" Value="Visible" />
        <Setter Target="ErrorText.Visibility" Value="Collapsed" />
      </VisualState.Setters>
    </VisualState>
    <VisualState x:Name="Failed">
      <VisualState.Setters>
        <Setter Target="Spinner.Visibility" Value="Collapsed" />
        <Setter Target="ErrorText.Visibility" Value="Visible" />
      </VisualState.Setters>
    </VisualState>
  </VisualStateGroup>
</VisualStateManager.VisualStateGroups>
```

Code-behind then calls `VisualStateManager.GoToState(this, "Failed", true)`.
In Reactor, the state is a normal value, and each visual state is a branch:

```csharp
enum LoadState { Loading, Loaded, Failed }

class VisualStatesExample : Component
{
    public override Element Render()
    {
        var (state, setState) = UseState(LoadState.Loading);

        // Each visual state is a branch in Render, not a named VisualState.
        Element status = state switch
        {
            LoadState.Loading => ProgressRing(),
            LoadState.Failed => TextBlock("Could not load the orders.")
                .Foreground(Theme.SystemCritical),
            _ => TextBlock("12 orders loaded."),
        };

        return VStack(12,
            status,
            HStack(8,
                Button("Loading", () => setState(LoadState.Loading)),
                Button("Loaded", () => setState(LoadState.Loaded)),
                Button("Failed", () => setState(LoadState.Failed))),
            // Pointer states run on the compositor, with no re-render.
            Border(TextBlock("Hover or press me"))
                .Padding(12).CornerRadius(8).Background(Theme.CardBackground)
                .InteractionStates(s => s.PointerOver(scale: 1.03f).Pressed(scale: 0.97f)));
    }
}
```

You change the state with a setter, and the UI for that state is whatever
`Render()` returns. Pointer-over and pressed feedback does not need state at all.
`.InteractionStates(...)` runs those animations on the compositor, without a
render. See [Animation](/microsoft-ui-reactor/main/guides/animation) for the options.

## Storyboards become animation modifiers [#storyboards-become-animation-modifiers]

In XAML, a `Storyboard` animates dependency properties over time. You start it
from code or from a `VisualState`. In Reactor, you describe the animation on the
element:

- `.Animate(curve)` animates changes to visual properties, such as opacity,
  scale, rotation and translation, when their values change between renders.
- `.Transition(transition)` plays an enter animation when an element appears,
  and an exit animation before it is removed.
- `AnimationScope.WithAnimation(curve, () => ...)` animates the visual changes
  that one state update causes.

See [Animation](/microsoft-ui-reactor/main/guides/animation) for when to use each one.

## Navigation without Frame [#navigation-without-frame]

Reactor navigation is declared in the component tree. You do not call
`Frame.Navigate(...)` on a `Frame` reference.

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

`UseNavigation` returns a typed navigation handle that lives in component state.
`NavigationHost` renders the page for the current route. Navigation decisions use
the same state-driven flow as the rest of the UI. See [Navigation](/microsoft-ui-reactor/main/guides/navigation)
for parameters, back navigation and nested hosts.

## DependencyProperty becomes a prop or a hook [#dependencyproperty-becomes-a-prop-or-a-hook]

In XAML, you register a `DependencyProperty` when a control needs a property
that can be bound, styled or animated. In Reactor, you do not register
properties:

- A value that a parent passes in is a field on the props record of a
  [`Component<TProps>`](/microsoft-ui-reactor/main/guides/components).
- A value the component owns is hook state, such as `UseState`.
- A derived value is a local variable in `Render()`. If it is expensive to
  compute, wrap it in [`UseMemo`](/microsoft-ui-reactor/main/guides/hooks).

Reactor still sets dependency properties on the WinUI controls it creates. Your
components just do not need their own.

## Adopting Reactor gradually [#adopting-reactor-gradually]

You do not have to rewrite your app to use Reactor. Reactor and XAML can share
one window in both directions, and existing view models keep working.

### Keep your view models [#keep-your-view-models]

Reactor has hooks for existing `INotifyPropertyChanged` view models:

```csharp
class ObservableTreeDemo : Component
{
    private static readonly SettingsViewModel _vm = new();

    public override Element Render()
    {
        var vm = UseObservableTree(_vm);

        return VStack(12,
            SubHeading("UseObservableTree"),
            TextBox(vm.UserName, v => vm.UserName = v,
                header: "User Name"),
            ToggleSwitch(vm.DarkMode, v => vm.DarkMode = v,
                header: "Dark Mode"),
            Slider(vm.FontSize, 10, 32, v => vm.FontSize = (int)v)
                .AutomationName("Font size"),
            TextBlock($"Preview: {vm.UserName}")
                .FontSize(vm.FontSize).Bold()
        ).Padding(24);
    }
}
```

`UseObservableTree` subscribes to the view model and renders the component again
when a property changes. It also follows nested objects that implement
`INotifyPropertyChanged`. `UseObservable` watches only the object you pass to it.
With these hooks you can replace a XAML view first and move its state into hooks
later, or never.

### Host Reactor in a XAML page [#host-reactor-in-a-xaml-page]

`ReactorHostControl` is an ordinary WinUI control, so you can declare it in a
page's markup:

```xml
<Page xmlns:reactor="using:Microsoft.UI.Reactor.Hosting"
      xmlns:local="using:MyApp">
    <reactor:ReactorHostControl ComponentType="local:StatsCard" />
</Page>
```

`ComponentType` names a component with a public parameterless constructor. It
only works for a type named in markup, because that is what makes the XAML
compiler generate a trim-safe way to create it. If you set it from code, the host
shows an error in place of the component. From code, set `ComponentFactory`, or
give the host an `x:Name` and call `Mount(...)`. Use these when the component
needs constructor arguments or view-model state.

### Host XAML inside Reactor [#host-xaml-inside-reactor]

The other direction, an existing XAML page or control inside a Reactor tree,
uses `XamlPage(...)` and `XamlHost(...)`:

```csharp
class XamlInteropExample : Component
{
    public override Element Render()
    {
        var (count, _) = UseState(0);

        return VStack(8,
            XamlPage(typeof(LegacySettingsPage), parameter: "general"),   // a Frame navigated to the page
            XamlHost(() => new MyUserControl(), c => ((MyUserControl)c).Value = count,
                     typeKey: "MyUserControl")                            // any FrameworkElement
        );
    }
}
```

- `XamlPage` hosts a `Frame` navigated to the page. It navigates again when the
  page type or the parameter changes.
- `XamlHost` hosts any `FrameworkElement`. Its factory runs once, on mount. Its
  updater runs after creation and on every render.
- Give unrelated `XamlHost` calls different `typeKey` values, so Reactor never
  updates one host into another.

Reactor does not manage the children of the hosted control. Use the factories
instead of the `XamlPageElement` and `XamlHostElement` constructors: the
factories record the call site, so inspectors can map the hosted control back to
your code.

### WPF and WinForms apps [#wpf-and-winforms-apps]

- **WinForms:** the `Reactor.Interop.WinForms` package has `XamlIslandControl`,
  a WinForms control that hosts a Reactor component. See
  [WinForms Interop](/microsoft-ui-reactor/main/guides/winforms-interop).
- **WPF:** there is no WPF host control yet. You can host a component through
  `DesktopWindowXamlSource` inside an `HwndHost`. See
  [WPF Interop](/microsoft-ui-reactor/main/guides/wpf-interop).
- **Window settings:** for WPF and UWP window options such as `ResizeMode`,
  `Topmost` and `WindowStyle`, the [Windows](/microsoft-ui-reactor/main/guides/windows) page has a mapping table.

### A migration path [#a-migration-path]

1. Start with one leaf page, not the app shell.
2. Rebuild its layout with `VStack`, `HStack`, `Grid` and `Border`.
3. Replace bindings with `UseState`, props or `UseObservableTree`.
4. Replace simple converters with C# expressions.
5. Keep existing services and view models until the Reactor page is stable.
6. Extract reusable parts into small components once the page works.

The goal is not to copy XAML syntax into C#. The goal is to use Reactor's
state-driven model and keep your WinUI knowledge.

## What you usually stop writing [#what-you-usually-stop-writing]

- `DataContext` plumbing for ordinary screens.
- Value converters for formatting and visibility.
- Code-behind that only mirrors control state.
- A separate markup file for each screen.
- Small view models whose only job is to expose bindable properties.

What replaces them is ordinary C#.

## Patterns [#patterns]

### Porting a XAML page [#porting-a-xaml-page]

1. **List the view-model properties the page uses.** Each becomes `UseState` in
   the component, or stays in the view model behind `UseObservableTree`.
2. **Rebuild the markup tree.** The outer panel becomes `VStack`, `HStack` or
   `Grid`. Styles become modifier chains or helper methods.
3. **Replace bindings with reads.** `{Binding Name}` becomes the `name`
   variable. `Mode=TwoWay` becomes a value and a setter.
4. **Replace commands.** An `ICommand` becomes a method or lambda. Use a
   [`Command`](/microsoft-ui-reactor/main/guides/commanding) record when the action needs a label, an icon or a
   shortcut.

### UserControl becomes a component [#usercontrol-becomes-a-component]

You reach for a `UserControl` when a screen has a reusable part with its own
state. In Reactor, that part is a [component](/microsoft-ui-reactor/main/guides/components): one C# class with
a `Render()` method and, if it needs input, a props record. The XAML version
needs a `.xaml` file, a `.xaml.cs` file and often a `DataContext`. The Reactor
version is one class.

### DataContext becomes props or context [#datacontext-becomes-props-or-context]

In XAML, controls inherit a `DataContext` from their parents. In Reactor, a
parent passes data to a child as props, so each component's inputs are visible
in its type. For a value that many components deep in the tree need, such as the
signed-in user or app settings, use [Context](/microsoft-ui-reactor/main/guides/context) instead of passing it
through every level.

## Common Mistakes [#common-mistakes]

### Implementing INotifyPropertyChanged on a component [#implementing-inotifypropertychanged-on-a-component]

```csharp
// Don't:
// Reactor never listens to PropertyChanged on a component.
public class ProfilePage : Component, INotifyPropertyChanged
{
    private string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
```

Reactor does not subscribe to `PropertyChanged` on a component. A component
renders again when one of its hook setters runs or its parent renders it again.
Setting `Name` changes the field, and nothing on screen updates. Keep the value
in hook state instead, `var (name, setName) = UseState("");`, and the setter is
the change signal. The `REACTOR_STATE_001` analyzer warns when a component
implements `INotifyPropertyChanged`.

### Setting properties on a control Reactor owns [#setting-properties-on-a-control-reactor-owns]

If you get a control through a ref and set one of its properties, or attach a
`{Binding}` to it, you create a second source of truth. Reactor writes a
property again only when its value in the element changes, so your value can
stay for a while and then disappear. Put the value in state and let `Render()`
set it. Use refs only for imperative calls such as focus.

### Looking for a DependencyProperty system [#looking-for-a-dependencyproperty-system]

Reactor has no `DependencyProperty.Register`, no property metadata and no
`AffectsMeasure`. If you look for the equivalent of `AffectsArrange`, the answer
is that `Render()` already ran and Reactor already applied the layout changes.
Compute values as local variables in `Render()`, and cache expensive ones with
[`UseMemo`](/microsoft-ui-reactor/main/guides/hooks).

### Looking for a Reactor XAML compiler [#looking-for-a-reactor-xaml-compiler]

Reactor has no markup for its elements and no XAML loader for them. There is no
"Reactor XAML" format. Everything inside a Reactor tree is C#. To mix the two,
use [`ReactorHostControl`](#host-reactor-in-a-xaml-page) in a XAML page, or
[`XamlPage` and `XamlHost`](#host-xaml-inside-reactor) in a Reactor tree.

## Tips [#tips]

**Render the current truth.** In XAML, you often describe relationships between
properties. In Reactor, you compute the current value and return the UI for it.

**Prefer state over control references.** If a change should update the screen,
store the value in `UseState` instead of reaching into a control.

**Use a component where you used a `UserControl`.** The same instinct to split
the UI into reusable parts still applies.

**Do not translate XAML line by line.** A page with several user controls and
many bindings often becomes one component with a few hooks.

**Keep MVVM only where it helps.** Existing view models migrate well with
`UseObservableTree`. Many small view models that only exposed bindable
properties are no longer needed.

## Next Steps [#next-steps]

- [Thinking in Reactor](/microsoft-ui-reactor/main/get-started/thinking-in-reactor): the mental model behind this page.
- [Your first app](/microsoft-ui-reactor/main/get-started/first-app): build a Reactor app from scratch.
- [Components](/microsoft-ui-reactor/main/guides/components): props, children and composition.
- [Hooks](/microsoft-ui-reactor/main/guides/hooks): `UseState`, `UseReducer`, `UseEffect` and the hook rules.
- [Layout](/microsoft-ui-reactor/main/guides/layout): more WinUI layout patterns in Reactor.
- [Reactivity Model](/microsoft-ui-reactor/main/how-it-works/reactivity-model): how a state change becomes a control update.
- [WinForms Interop](/microsoft-ui-reactor/main/guides/winforms-interop) and [WPF Interop](/microsoft-ui-reactor/main/guides/wpf-interop): host
  Reactor in an existing desktop app.
