---
title: "Factories: Shapes"
description: "Rectangles, ellipses, lines, paths and brushes."
sidebar:
  label: "Shapes"
---

Each factory method returns an element record. Most are on `Factories`, so `using static Microsoft.UI.Reactor.Factories;` covers them. A factory declared on another class names the `using static` it needs.

**Learn more:** [Styling and Theming](/microsoft-ui-reactor/main/guides/styling)

## `AcrylicBrush` [#acrylicbrush]

Creates a new AcrylicBrush. This allocates a WinRT DependencyObject on every call. On hot paths (e.g., inside Render methods), cache the result with `UseMemo`:

```csharp
var brush = ctx.UseMemo(() => AcrylicBrush(color, 0.8), color);
```

```csharp
AcrylicBrush AcrylicBrush(Color tintColor, double tintOpacity = 0.8, Color? fallbackColor = null, double? tintLuminosityOpacity = null)
```

**Returns:** [`AcrylicBrush`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.acrylicbrush).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `tintColor` | `Color` |  |  |
| `tintOpacity` | `double` | `0.8` |  |
| `fallbackColor` | `Color?` | `null` |  |
| `tintLuminosityOpacity` | `double?` | `null` |  |

## `Ellipse` [#ellipse]

```csharp
EllipseElement Ellipse()
```

**Returns:** [`EllipseElement`](/microsoft-ui-reactor/main/reference/elements/shapes#ellipseelement).

## `Line` [#line]

```csharp
LineElement Line(double x1, double y1, double x2, double y2)
```

**Returns:** [`LineElement`](/microsoft-ui-reactor/main/reference/elements/shapes#lineelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x1` | `double` |  |
| `y1` | `double` |  |
| `x2` | `double` |  |
| `y2` | `double` |  |

## `Path2D` [#path2d]

```csharp
PathElement Path2D()
```

**Returns:** [`PathElement`](/microsoft-ui-reactor/main/reference/elements/shapes#pathelement).

## `Rectangle` [#rectangle]

```csharp
RectangleElement Rectangle()
```

**Returns:** [`RectangleElement`](/microsoft-ui-reactor/main/reference/elements/shapes#rectangleelement).
