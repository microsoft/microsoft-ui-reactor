---
title: "ResourceBuilder"
seo:
  description: "Fluent builder for per-control resource overrides (WinUI lightweight styling). Each Set() call registers a resource key override that will be injected into the control's FrameworkElement.Resources dictionary at mount time."
---

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

**Learn more:** [Styling and Theming](/microsoft-ui-reactor/main/guides/styling)

## Constructors [#constructors]

### `ResourceBuilder` [#resourcebuilder_1]

```csharp
public ResourceBuilder()
```

## Methods [#methods]

### `Set` [#set]

#### `Set(string, ThemeRef)` [#setstring-themeref]

Sets a theme-reactive resource override that re-resolves on theme change.

```csharp
public ResourceBuilder Set(string key, ThemeRef themeRef)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `themeRef` | [`ThemeRef`](/microsoft-ui-reactor/main/reference/theming/ThemeRef) |  |

#### `Set(string, CornerRadius)` [#setstring-cornerradius]

Sets a corner radius resource override (e.g., `ControlCornerRadius`).

```csharp
public ResourceBuilder Set(string key, CornerRadius value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `CornerRadius` |  |

#### `Set(string, GridLength)` [#setstring-gridlength]

Sets a grid length resource override (e.g., `ContentDialogButtonSpacing`).

```csharp
public ResourceBuilder Set(string key, GridLength value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `GridLength` |  |

#### `Set(string, Brush)` [#setstring-brush]

Sets a brush resource override.

```csharp
public ResourceBuilder Set(string key, Brush brush)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `brush` | `Brush` |  |

#### `Set(string, FontFamily)` [#setstring-fontfamily]

Sets a font family resource override (e.g., `ContentControlThemeFontFamily`).

```csharp
public ResourceBuilder Set(string key, FontFamily value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `FontFamily` |  |

#### `Set(string, Thickness)` [#setstring-thickness]

Sets a thickness resource override (e.g., `BreadcrumbBarChevronPadding`).

```csharp
public ResourceBuilder Set(string key, Thickness value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `Thickness` |  |

#### `Set(string, bool)` [#setstring-bool]

Sets a Boolean resource override (e.g., `GridViewItemSelectionCheckMarkVisualEnabled`).

```csharp
public ResourceBuilder Set(string key, bool value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `bool` |  |

#### `Set(string, double)` [#setstring-double]

Sets a numeric resource override (e.g., `BreadcrumbBarItemThemeFontSize`).

```csharp
public ResourceBuilder Set(string key, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `double` |  |

#### `Set(string, string)` [#setstring-string]

Sets a color resource override from a hex string (#RRGGBB / #AARRGGBB) or named color (see [`BrushHelper.Parse`](/microsoft-ui-reactor/main/reference/theming/BrushHelper#parse)). Anything else — including a font name under a `*FontFamily` key — silently stores a gray brush; the REACTOR_DYM_006 analyzer flags constant values that do.

```csharp
public ResourceBuilder Set(string key, string color)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `color` | `string` |  |

#### `Set(string, FontWeight)` [#setstring-fontweight]

Sets a font weight resource override (e.g., `BreadcrumbBarItemFontWeight`).

```csharp
public ResourceBuilder Set(string key, FontWeight value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `FontWeight` |  |

### `SetColor` [#setcolor]

Sets a literal color resource override, not a brush. Use the string or [`Brush`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.brush) overload for resources that expect a brush.

```csharp
public ResourceBuilder SetColor(string key, Color value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `Color` |  |
