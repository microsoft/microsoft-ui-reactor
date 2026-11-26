# Elements: Shapes

Rectangles, ellipses, lines, paths and brushes.

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Styling and Theming](../../styling.md)

## `EllipseElement`

The element record that describes a WinUI [`Ellipse`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.shapes.ellipse) control.

```csharp
public record EllipseElement : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Ellipse`](../factories/shapes.md#ellipse)

**Modifiers:** [`Fill`](../modifiers/shapes.md#fill), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Fill` | `Brush?` | Maps `Ellipse.Fill`. |
| `Setters` | `Action<Ellipse>[]` | Imperative escape hatch run after every Mount/Update. |
| `Stroke` | `Brush?` | Maps `Ellipse.Stroke`. |
| `StrokeThickness` | `double?` | Maps `Ellipse.StrokeThickness`. |

| Member | Description |
| --- | --- |
| `Ellipse(Brush?, Brush?, double?)` | Creates a [`EllipseElement`](#ellipseelement) (and guarantees the handler is registered). |
| `Set(Action<Ellipse>)` | Appends an action run against the live [`EllipseElement.Ellipse`](#ellipseelement) after every Mount/Update (imperative escape hatch). Chainable. |

## `LineElement`

The element record that describes a WinUI [`Line`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.shapes.line) control.

```csharp
public record LineElement : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Line`](../factories/shapes.md#line)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set), [`Stroke`](../modifiers/shapes.md#stroke), [`StrokeThickness`](../modifiers/shapes.md#strokethickness)

| Property | Type | Description |
| --- | --- | --- |
| `Stroke` | `Brush?` |  |
| `StrokeThickness` | `double` |  |
| `X1` | `double` |  |
| `X2` | `double` |  |
| `Y1` | `double` |  |
| `Y2` | `double` |  |

## `PathElement`

The element record that describes a WinUI [`Path`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.shapes.path) control.

```csharp
public record PathElement : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Path2D`](../factories/shapes.md#path2d)

**Modifiers:** [`Fill`](../modifiers/shapes.md#fill), [`FillRule`](../modifiers/shapes.md#fillrule), [`Set`](../modifiers/native-properties.md#set), [`Stroke`](../modifiers/shapes.md#stroke), [`StrokeDashArray`](../modifiers/shapes.md#strokedasharray), [`StrokeDashCap`](../modifiers/shapes.md#strokedashcap), [`StrokeDashOffset`](../modifiers/shapes.md#strokedashoffset), [`StrokeEndLineCap`](../modifiers/shapes.md#strokeendlinecap), [`StrokeLineJoin`](../modifiers/shapes.md#strokelinejoin), [`StrokeMiterLimit`](../modifiers/shapes.md#strokemiterlimit), [`StrokeStartLineCap`](../modifiers/shapes.md#strokestartlinecap), [`StrokeThickness`](../modifiers/shapes.md#strokethickness)

| Property | Type | Description |
| --- | --- | --- |
| `Data` | `Geometry?` | Pre-parsed WinUI Geometry. When null, the reconciler resolves from [`PathElement.PathDataString`](#pathelement). Callers that construct PathElement directly (not via D3Path) can set this for non-SVG geometries. |
| `Fill` | `Brush?` |  |
| `FillRule` | `FillRule` | How interior regions are determined for fills. Defaults to `EvenOdd`. |
| `PathDataString` | `string?` | The path markup string (SVG / XAML path syntax). When set, geometry is converted lazily by the reconciler with WinUI's native path-markup converter — only when mounting or when the string changes between renders — so no COM Geometry is created on every tree build. Markup the platform rejects (for example a NaN coordinate) falls back to [`PathElement.Data`](#pathelement), or draws nothing. |
| `RenderTransform` | `Transform?` |  |
| `Stroke` | `Brush?` |  |
| `StrokeDashArray` | `DoubleCollection?` |  |
| `StrokeDashCap` | `PenLineCap` | Cap rendered on dashes when [`PathElement.StrokeDashArray`](#pathelement) is set. Defaults to `Flat`. |
| `StrokeDashOffset` | `double` | Distance into the dash pattern at which to begin drawing. Defaults to 0. |
| `StrokeEndLineCap` | `PenLineCap` | Cap rendered at the end of an open stroke. Defaults to `Flat`. |
| `StrokeLineJoin` | `PenLineJoin` | Join style between two connected stroke segments. Defaults to `Miter`. |
| `StrokeMiterLimit` | `double` | Maximum extent of a miter join relative to half the stroke thickness. Defaults to 10. |
| `StrokeStartLineCap` | `PenLineCap` | Cap rendered at the start of an open stroke. Defaults to `Flat`. |
| `StrokeThickness` | `double` |  |

## `RectangleElement`

The element record that describes a WinUI [`Rectangle`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.shapes.rectangle) control.

```csharp
public record RectangleElement : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Rectangle`](../factories/shapes.md#rectangle)

**Modifiers:** [`Fill`](../modifiers/shapes.md#fill), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Fill` | `Brush?` | Maps `Rectangle.Fill`. |
| `RadiusX` | `double?` | Maps `Rectangle.RadiusX`. |
| `RadiusY` | `double?` | Maps `Rectangle.RadiusY`. |
| `Setters` | `Action<Rectangle>[]` | Imperative escape hatch run after every Mount/Update. |
| `Stroke` | `Brush?` | Maps `Rectangle.Stroke`. |
| `StrokeThickness` | `double?` | Maps `Rectangle.StrokeThickness`. |

| Member | Description |
| --- | --- |
| `Rectangle(Brush?, double?, double?, Brush?, double?)` | Creates a [`RectangleElement`](#rectangleelement) (and guarantees the handler is registered). |
| `Set(Action<Rectangle>)` | Appends an action run against the live [`RectangleElement.Rectangle`](#rectangleelement) after every Mount/Update (imperative escape hatch). Chainable. |
