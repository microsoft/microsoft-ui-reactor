# ThemeResource

Lookup helpers for WinUI theme resources. These resolve against Application.Current.Resources, which includes all merged dictionaries (XamlControlsResources, custom themes, etc.).

Usage: var brush = ThemeResource.Brush("TextFillColorSecondaryBrush"); var radius = ThemeResource.CornerRadius("OverlayCornerRadius");

```csharp
public static class ThemeResource
```

Namespace: `Microsoft.UI.Reactor`.

**Learn more:** [Theming Tokens](../../theming-tokens.md)

## Methods

### `Brush`

```csharp
public static Brush Brush(string key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |

### `CornerRadius`

```csharp
public static CornerRadius CornerRadius(string key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |

### `Double`

```csharp
public static double Double(string key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |

### `Get`

Try to look up a resource, returning default if not found.

```csharp
public static T Get<T>(string key, T defaultValue = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `key` | `string` |  |  |
| `defaultValue` | `T` | `default` |  |

### `Thickness`

```csharp
public static Thickness Thickness(string key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
