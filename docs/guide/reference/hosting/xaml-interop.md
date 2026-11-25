# XAML interop

Helpers for hosting Reactor inside an existing XAML app.

**Learn more:** [Reactor for XAML Developers](../../xaml-developers.md)

## `PageHelper`

Helper methods for hosting Reactor components inside XAML Pages that participate in Frame-based navigation.

IMPORTANT: WinUI's Frame.Navigate requires pages to have XAML metadata (IXamlType registration). Code-only Page subclasses — including generic base classes like ReactorPage\<T\> — crash with a null access violation in ActivationAPI::ActivateInstance because GetXamlTypeNoRef() returns null.

The correct pattern for Reactor pages in Frame navigation:

1\. Create a minimal .xaml file for the page: \<Page x:Class="MyApp.Pages.ButtonPage" ... /\>

2\. In the code-behind, use PageHelper to mount the component:

public sealed partial class ButtonPage: Page \{ private ReactorHostControl? _host;

public ButtonPage() \{ InitializeComponent(); \}

protected override void OnNavigatedTo(NavigationEventArgs e) \{ base.OnNavigatedTo(e); _host = PageHelper.Mount\<MyComponent\>(this, e); \}

protected override void OnNavigatedFrom(NavigationEventArgs e) \{ base.OnNavigatedFrom(e); PageHelper.Unmount(ref _host); \} \}

```csharp
public static class PageHelper
```

Namespace: `Microsoft.UI.Reactor.Hosting`.

### Methods

#### `Mount`

##### `Mount<TComponent>(Page, NavigationEventArgs)`

Creates a ReactorHostControl, mounts the component, and sets it as the page's content. Passes navigation parameters as props if the component accepts them.

```csharp
public static ReactorHostControl Mount<TComponent>(Page page, NavigationEventArgs e) where TComponent : Component, new()
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `page` | `Page` |  |
| `e` | `NavigationEventArgs` |  |

##### `Mount<TComponent, TProps>(Page, NavigationEventArgs)`

Creates a ReactorHostControl, mounts the component with typed props, and sets it as the page's content.

```csharp
public static ReactorHostControl Mount<TComponent, TProps>(Page page, NavigationEventArgs e) where TComponent : Component<TProps>, new()
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `page` | `Page` |  |
| `e` | `NavigationEventArgs` |  |

#### `Unmount`

Disposes the ReactorHostControl and clears the reference.

```csharp
public static void Unmount(ref ReactorHostControl? host)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `host` | [`ReactorHostControl?`](ReactorHostControl.md) |  |

## `XamlInterop`

Registers the reverse-embedding element types with a Reconciler. Call this once during app startup or ReactorHostControl initialization.

Usage: XamlInterop.Register(reconciler);

Then in a Reactor component (the factories record a call site for the source map; the record constructors work too, without one): XamlPage(typeof(ExistingXamlPage), "param") XamlHost(() =\> new MyUserControl(), ctrl =\> ((MyUserControl)ctrl).Value = 42)

```csharp
public static class XamlInterop
```

Namespace: `Microsoft.UI.Reactor.Hosting`.

### Methods

#### `Register`

```csharp
public static void Register(Reconciler reconciler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `reconciler` | [`Reconciler`](../extensibility/Reconciler.md) |  |
