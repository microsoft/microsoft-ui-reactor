# Control modifiers: Shapes

Modifiers that only the controls in the Shapes area have.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Styling and Theming](../../styling.md)

## `Fill`

```csharp
EllipseElement Fill(this EllipseElement el, Brush brush)
PathElement Fill(this PathElement el, Brush brush)
RectangleElement Fill(this RectangleElement el, Brush brush)
```

Declared in `ElementExtensions`.

**Applies to:** [`EllipseElement`](../elements/shapes.md#ellipseelement), [`PathElement`](../elements/shapes.md#pathelement), [`RectangleElement`](../elements/shapes.md#rectangleelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `brush` | `Brush` |  |

## `FillRule`

How interior regions are determined for fills.

```csharp
PathElement FillRule(this PathElement el, FillRule rule)
```

Declared in `ElementExtensions`.

**Applies to:** [`PathElement`](../elements/shapes.md#pathelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `rule` | `FillRule` |  |

## `Stroke`

```csharp
LineElement Stroke(this LineElement el, Brush brush)
PathElement Stroke(this PathElement el, Brush brush)
```

Declared in `ElementExtensions`.

**Applies to:** [`LineElement`](../elements/shapes.md#lineelement), [`PathElement`](../elements/shapes.md#pathelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `brush` | `Brush` |  |

## `StrokeDashArray`

```csharp
PathElement StrokeDashArray(this PathElement el, params double[] dashes)
```

Declared in `ElementExtensions`.

**Applies to:** [`PathElement`](../elements/shapes.md#pathelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `dashes` | `double[]` |  |

## `StrokeDashCap`

Cap rendered on dashes when `StrokeDashArray` is set.

```csharp
PathElement StrokeDashCap(this PathElement el, PenLineCap cap)
```

Declared in `ElementExtensions`.

**Applies to:** [`PathElement`](../elements/shapes.md#pathelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `cap` | `PenLineCap` |  |

## `StrokeDashOffset`

Distance into the dash pattern at which to begin drawing.

```csharp
PathElement StrokeDashOffset(this PathElement el, double offset)
```

Declared in `ElementExtensions`.

**Applies to:** [`PathElement`](../elements/shapes.md#pathelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `offset` | `double` |  |

## `StrokeEndLineCap`

Cap rendered at the end of an open stroke.

```csharp
PathElement StrokeEndLineCap(this PathElement el, PenLineCap cap)
```

Declared in `ElementExtensions`.

**Applies to:** [`PathElement`](../elements/shapes.md#pathelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `cap` | `PenLineCap` |  |

## `StrokeLineJoin`

Join style between two connected stroke segments.

```csharp
PathElement StrokeLineJoin(this PathElement el, PenLineJoin join)
```

Declared in `ElementExtensions`.

**Applies to:** [`PathElement`](../elements/shapes.md#pathelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `join` | `PenLineJoin` |  |

## `StrokeMiterLimit`

Maximum extent of a miter join relative to half the stroke thickness.

```csharp
PathElement StrokeMiterLimit(this PathElement el, double limit)
```

Declared in `ElementExtensions`.

**Applies to:** [`PathElement`](../elements/shapes.md#pathelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `limit` | `double` |  |

## `StrokeStartLineCap`

Cap rendered at the start of an open stroke.

```csharp
PathElement StrokeStartLineCap(this PathElement el, PenLineCap cap)
```

Declared in `ElementExtensions`.

**Applies to:** [`PathElement`](../elements/shapes.md#pathelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `cap` | `PenLineCap` |  |

## `StrokeThickness`

```csharp
LineElement StrokeThickness(this LineElement el, double thickness)
PathElement StrokeThickness(this PathElement el, double thickness)
```

Declared in `ElementExtensions`.

**Applies to:** [`LineElement`](../elements/shapes.md#lineelement), [`PathElement`](../elements/shapes.md#pathelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `thickness` | `double` |  |
