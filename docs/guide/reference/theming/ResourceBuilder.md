# ResourceBuilder

Fluent builder for per-control resource overrides (WinUI lightweight styling). Each `Set()` call registers a resource key override that will be injected into the control's [`FrameworkElement.Resources`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement.resources) dictionary at mount time.

Lightweight styling lets you override built-in control resources (e.g., `ButtonBackground`, `ButtonBackgroundPointerOver`) without replacing the entire control template. The control's [`VisualStateManager`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.visualstatemanager) continues to function normally, so hover/pressed/disabled states respect the overrides automatically.

**Example — brand-colored button:**

```csharp
Button("Submit").Resources(r => r
    .Set("ButtonBackground", "#0078D4")
    .Set("ButtonBackgroundPointerOver", "#106EBE")
    .Set("ButtonBackgroundPressed", "#005A9E")
    .Set("ButtonForeground", "#FFFFFF"))
```

```csharp
public sealed class ResourceBuilder
```

Namespace: `Microsoft.UI.Reactor.Elements`.

**Learn more:** [Styling and Theming](../../styling.md)

## Constructors

### `ResourceBuilder`

```csharp
public ResourceBuilder()
```

## Methods

### `Set`

#### `Set(string, ThemeRef)`

Sets a theme-reactive resource override that re-resolves on theme change.

```csharp
public ResourceBuilder Set(string key, ThemeRef themeRef)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `themeRef` | [`ThemeRef`](ThemeRef.md) |  |

#### `Set(string, CornerRadius)`

Sets a corner radius resource override (e.g., `ControlCornerRadius`).

```csharp
public ResourceBuilder Set(string key, CornerRadius value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `CornerRadius` |  |

#### `Set(string, GridLength)`

Sets a grid length resource override (e.g., `ContentDialogButtonSpacing`).

```csharp
public ResourceBuilder Set(string key, GridLength value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `GridLength` |  |

#### `Set(string, Brush)`

Sets a brush resource override.

```csharp
public ResourceBuilder Set(string key, Brush brush)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `brush` | `Brush` |  |

#### `Set(string, FontFamily)`

Sets a font family resource override (e.g., `ContentControlThemeFontFamily`).

```csharp
public ResourceBuilder Set(string key, FontFamily value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `FontFamily` |  |

#### `Set(string, Thickness)`

Sets a thickness resource override (e.g., `BreadcrumbBarChevronPadding`).

```csharp
public ResourceBuilder Set(string key, Thickness value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `Thickness` |  |

#### `Set(string, bool)`

Sets a Boolean resource override (e.g., `GridViewItemSelectionCheckMarkVisualEnabled`).

```csharp
public ResourceBuilder Set(string key, bool value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `bool` |  |

#### `Set(string, double)`

Sets a numeric resource override (e.g., `BreadcrumbBarItemThemeFontSize`).

```csharp
public ResourceBuilder Set(string key, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `double` |  |

#### `Set(string, string)`

Sets a color resource override from a hex string (#RRGGBB / #AARRGGBB) or named color (see [`BrushHelper.Parse`](BrushHelper.md#parse)). Anything else — including a font name under a `*FontFamily` key — silently stores a gray brush; the REACTOR_DYM_006 analyzer flags constant values that do.

```csharp
public ResourceBuilder Set(string key, string color)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `color` | `string` |  |

#### `Set(string, FontWeight)`

Sets a font weight resource override (e.g., `BreadcrumbBarItemFontWeight`).

```csharp
public ResourceBuilder Set(string key, FontWeight value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `FontWeight` |  |

### `SetColor`

Sets a literal color resource override, not a brush. Use the string or [`Brush`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.brush) overload for resources that expect a brush.

```csharp
public ResourceBuilder SetColor(string key, Color value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `Color` |  |
