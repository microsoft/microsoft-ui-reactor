# ReactorApplication

Application subclass that implements IXamlMetadataProvider so the native XAML schema context can resolve managed types from XBF theme resources. No App.xaml needed — XamlControlsResources are loaded programmatically. The IXamlMetadataProvider implementation delegates to the WinUI controls' built-in provider so that custom control types (TextCommandBarFlyout, etc.) can be instantiated from XBF theme resources.

```csharp
public class ReactorApplication : Application, IXamlMetadataProvider, IComponentConnector
```

Namespace: `Microsoft.UI.Reactor`. Inherits from [`Application`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.application).

**Learn more:** [Windows](../../windows.md)

## Constructors

### `ReactorApplication`

```csharp
public ReactorApplication()
```

## Properties

### `OnUnhandledException`

Optional callback for unhandled exceptions. If set, called before deciding whether to handle. Return true to mark the exception as handled; return false (or leave null) to let it crash.

```csharp
public static Func<Exception, bool>? OnUnhandledException  // get, set
```

**Remarks**

Also receives render errors an app's [`RenderErrorHandler`](../components/render-errors.md#rendererrorhandler) routes here with [`RenderError.Propagate`](../components/render-errors.md#propagate). That call is made by Reactor itself, not by WinUI, so it happens even when the app's `Application` is not a [`ReactorApplication`](ReactorApplication.md) (for example a XAML app hosting `ReactorHostControl`). See [`RenderError.Propagate`](../components/render-errors.md#propagate) for what happens when it returns false.

## Methods

### `GetXamlType`

```csharp
public IXamlType GetXamlType(string fullName)
public IXamlType GetXamlType(Type type)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fullName` | `string` |  |
| `type` | `Type` |  |

### `GetXmlnsDefinitions`

```csharp
public XmlnsDefinition[] GetXmlnsDefinitions()
```

### `OnLaunched`

```csharp
protected override void OnLaunched(LaunchActivatedEventArgs args)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `args` | `LaunchActivatedEventArgs` |  |
