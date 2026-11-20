# Factories: Shapes

Rectangles, ellipses, lines, paths and brushes.

Each factory method returns an element record. Call them through `using static Microsoft.UI.Reactor.Factories;`.

**Learn more:** [Styling and Theming](../../styling.md)

## `AcrylicBrush`

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

## `Ellipse`

```csharp
EllipseElement Ellipse()
```

**Returns:** [`EllipseElement`](../elements/shapes.md#ellipseelement).

## `Line`

```csharp
LineElement Line(double x1, double y1, double x2, double y2)
```

**Returns:** [`LineElement`](../elements/shapes.md#lineelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x1` | `double` |  |
| `y1` | `double` |  |
| `x2` | `double` |  |
| `y2` | `double` |  |

## `Path2D`

```csharp
PathElement Path2D()
```

**Returns:** [`PathElement`](../elements/shapes.md#pathelement).

## `Rectangle`

```csharp
RectangleElement Rectangle()
```

**Returns:** [`RectangleElement`](../elements/shapes.md#rectangleelement).
