---
title: "Control modifiers: Shapes"
description: "Modifiers that only the controls in the Shapes area have."
sidebar:
  label: "Shapes"
---

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Styling and Theming](/microsoft-ui-reactor/main/guides/styling)

## `Fill` [#fill]

```csharp
EllipseElement Fill(this EllipseElement el, Brush brush)
PathElement Fill(this PathElement el, Brush brush)
RectangleElement Fill(this RectangleElement el, Brush brush)
```

Declared in `ElementExtensions`.

**Applies to:** [`EllipseElement`](/microsoft-ui-reactor/main/reference/elements/shapes#ellipseelement), [`PathElement`](/microsoft-ui-reactor/main/reference/elements/shapes#pathelement), [`RectangleElement`](/microsoft-ui-reactor/main/reference/elements/shapes#rectangleelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `brush` | `Brush` |  |

## `FillRule` [#fillrule]

How interior regions are determined for fills.

```csharp
PathElement FillRule(this PathElement el, FillRule rule)
```

Declared in `ElementExtensions`.

**Applies to:** [`PathElement`](/microsoft-ui-reactor/main/reference/elements/shapes#pathelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `rule` | `FillRule` |  |

## `Stroke` [#stroke]

```csharp
LineElement Stroke(this LineElement el, Brush brush)
PathElement Stroke(this PathElement el, Brush brush)
```

Declared in `ElementExtensions`.

**Applies to:** [`LineElement`](/microsoft-ui-reactor/main/reference/elements/shapes#lineelement), [`PathElement`](/microsoft-ui-reactor/main/reference/elements/shapes#pathelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `brush` | `Brush` |  |

## `StrokeDashArray` [#strokedasharray]

```csharp
PathElement StrokeDashArray(this PathElement el, params double[] dashes)
```

Declared in `ElementExtensions`.

**Applies to:** [`PathElement`](/microsoft-ui-reactor/main/reference/elements/shapes#pathelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `dashes` | `double[]` |  |

## `StrokeDashCap` [#strokedashcap]

Cap rendered on dashes when `StrokeDashArray` is set.

```csharp
PathElement StrokeDashCap(this PathElement el, PenLineCap cap)
```

Declared in `ElementExtensions`.

**Applies to:** [`PathElement`](/microsoft-ui-reactor/main/reference/elements/shapes#pathelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `cap` | `PenLineCap` |  |

## `StrokeDashOffset` [#strokedashoffset]

Distance into the dash pattern at which to begin drawing.

```csharp
PathElement StrokeDashOffset(this PathElement el, double offset)
```

Declared in `ElementExtensions`.

**Applies to:** [`PathElement`](/microsoft-ui-reactor/main/reference/elements/shapes#pathelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `offset` | `double` |  |

## `StrokeEndLineCap` [#strokeendlinecap]

Cap rendered at the end of an open stroke.

```csharp
PathElement StrokeEndLineCap(this PathElement el, PenLineCap cap)
```

Declared in `ElementExtensions`.

**Applies to:** [`PathElement`](/microsoft-ui-reactor/main/reference/elements/shapes#pathelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `cap` | `PenLineCap` |  |

## `StrokeLineJoin` [#strokelinejoin]

Join style between two connected stroke segments.

```csharp
PathElement StrokeLineJoin(this PathElement el, PenLineJoin join)
```

Declared in `ElementExtensions`.

**Applies to:** [`PathElement`](/microsoft-ui-reactor/main/reference/elements/shapes#pathelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `join` | `PenLineJoin` |  |

## `StrokeMiterLimit` [#strokemiterlimit]

Maximum extent of a miter join relative to half the stroke thickness.

```csharp
PathElement StrokeMiterLimit(this PathElement el, double limit)
```

Declared in `ElementExtensions`.

**Applies to:** [`PathElement`](/microsoft-ui-reactor/main/reference/elements/shapes#pathelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `limit` | `double` |  |

## `StrokeStartLineCap` [#strokestartlinecap]

Cap rendered at the start of an open stroke.

```csharp
PathElement StrokeStartLineCap(this PathElement el, PenLineCap cap)
```

Declared in `ElementExtensions`.

**Applies to:** [`PathElement`](/microsoft-ui-reactor/main/reference/elements/shapes#pathelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `cap` | `PenLineCap` |  |

## `StrokeThickness` [#strokethickness]

```csharp
LineElement StrokeThickness(this LineElement el, double thickness)
PathElement StrokeThickness(this PathElement el, double thickness)
```

Declared in `ElementExtensions`.

**Applies to:** [`LineElement`](/microsoft-ui-reactor/main/reference/elements/shapes#lineelement), [`PathElement`](/microsoft-ui-reactor/main/reference/elements/shapes#pathelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `thickness` | `double` |  |
