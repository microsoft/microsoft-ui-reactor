# Modifiers: Layout and spacing

Size, margin, padding, alignment and the attached layout properties.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Layout](../../layout.md), [Flex Layout](../../flex-layout.md)

## `Canvas`

```csharp
T Canvas<T>(this T el, double left = 0, double top = 0) where T : Element
T Canvas<T>(this T el, double left, double top, double anchorX, double anchorY) where T : Element
```

Declared in `CanvasExtensions`.

| Overload | Description |
| --- | --- |
| `Canvas<T>(T, double, double)` | Sets Canvas attached properties (left, top) on this element. Only meaningful when the element is a child of a Canvas. |
| `Canvas<T>(T, double, double, double, double)` | Sets Canvas attached properties with an anchor. `anchorX`/`anchorY` are 0..1 fractions of the element's rendered size; the reconciler subtracts that fraction of the actual width/height from `left`/`top` after layout. 0,0 = top-left (legacy), 0.5,0.5 = centered on (left, top), 1,1 = bottom-right. |

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `left` | `double` | `0` |  |
| `top` | `double` | `0` |  |
| `anchorX` | `double` |  |  |
| `anchorY` | `double` |  |  |

## `Center`

```csharp
T Center<T>(this T el) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

## `CenterAt`

Centers this element on (`x`, `y`) within its parent Canvas. Equivalent to `.Canvas(x, y, anchorX: 0.5, anchorY: 0.5)`. Position is recomputed after layout, so the element does not need a known size at construction time.

```csharp
T CenterAt<T>(this T el, double x, double y) where T : Element
```

Declared in `CanvasExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `double` |  |
| `y` | `double` |  |

## `Flex`

```csharp
T Flex<T>(this T el, double grow = 0, double shrink = 1, double? basis = null, double? minWidth = null, double? minHeight = null, FlexAlign? alignSelf = null, FlexPositionType position = FlexPositionType.Relative, double? left = null, double? top = null, double? right = null, double? bottom = null) where T : Element
```

Declared in `FlexExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `grow` | `double` | `0` |  |
| `shrink` | `double` | `1` |  |
| `basis` | `double?` | `null` |  |
| `minWidth` | `double?` | `null` |  |
| `minHeight` | `double?` | `null` |  |
| `alignSelf` | `FlexAlign?` | `null` |  |
| `position` | [`FlexPositionType`](../layout/flex-enums.md#flexpositiontype) | `FlexPositionType.Relative` |  |
| `left` | `double?` | `null` |  |
| `top` | `double?` | `null` |  |
| `right` | `double?` | `null` |  |
| `bottom` | `double?` | `null` |  |

## `FlexPadding`

```csharp
FlexElement FlexPadding(this FlexElement el, double uniform)
FlexElement FlexPadding(this FlexElement el, double horizontal, double vertical)
FlexElement FlexPadding(this FlexElement el, double left, double top, double right, double bottom)
```

Declared in `ElementExtensions`.

**Applies to:** [`FlexElement`](../elements/layout.md#flexelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `uniform` | `double` |  |
| `horizontal` | `double` |  |
| `vertical` | `double` |  |
| `left` | `double` |  |
| `top` | `double` |  |
| `right` | `double` |  |
| `bottom` | `double` |  |

## `Grid`

Sets Grid attached properties (row, column, spans) on this element. Only meaningful when the element is a child of a Grid.

```csharp
T Grid<T>(this T el, int row = 0, int column = 0, int rowSpan = 1, int columnSpan = 1) where T : Element
```

Declared in `GridExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `row` | `int` | `0` |  |
| `column` | `int` | `0` |  |
| `rowSpan` | `int` | `1` |  |
| `columnSpan` | `int` | `1` |  |

## `HAlign`

```csharp
T HAlign<T>(this T el, HorizontalAlignment alignment) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `alignment` | `HorizontalAlignment` |  |

## `Height`

```csharp
T Height<T>(this T el, double height) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `height` | `double` |  |

## `HorizontalAlignment`

```csharp
T HorizontalAlignment<T>(this T el, HorizontalAlignment alignment) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `alignment` | `HorizontalAlignment` |  |

## `HorizontalContentAlignment`

```csharp
T HorizontalContentAlignment<T>(this T el, HorizontalAlignment alignment) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `alignment` | `HorizontalAlignment` |  |

## `IsVisible`

Sets whether the element is visible or collapsed.

```csharp
T IsVisible<T>(this T el, bool isVisible) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `isVisible` | `bool` |  |

## `Margin`

```csharp
RichTextParagraph Margin(this RichTextParagraph paragraph, double uniform)
T Margin<T>(this T el, Thickness thickness) where T : Element
T Margin<T>(this T el, double uniform) where T : Element
RichTextParagraph Margin(this RichTextParagraph paragraph, double horizontal, double vertical)
T Margin<T>(this T el, double horizontal, double vertical) where T : Element
RichTextParagraph Margin(this RichTextParagraph paragraph, double left = 0, double top = 0, double right = 0, double bottom = 0)
T Margin<T>(this T el, double left = 0, double top = 0, double right = 0, double bottom = 0) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element, [`RichTextParagraph`](../controls/rich-text.md#richtextparagraph).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `uniform` | `double` |  |  |
| `thickness` | `Thickness` |  |  |
| `horizontal` | `double` |  |  |
| `vertical` | `double` |  |  |
| `left` | `double` | `0` |  |
| `top` | `double` | `0` |  |
| `right` | `double` | `0` |  |
| `bottom` | `double` | `0` |  |

## `MarginInlineEnd`

```csharp
T MarginInlineEnd<T>(this T el, double value) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `double` |  |

## `MarginInlineStart`

```csharp
T MarginInlineStart<T>(this T el, double value) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `double` |  |

## `MaxHeight`

```csharp
T MaxHeight<T>(this T el, double h) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `h` | `double` |  |

## `MaxWidth`

```csharp
T MaxWidth<T>(this T el, double w) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `w` | `double` |  |

## `MinHeight`

```csharp
T MinHeight<T>(this T el, double h) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `h` | `double` |  |

## `MinWidth`

```csharp
T MinWidth<T>(this T el, double w) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `w` | `double` |  |

## `Padding`

```csharp
T Padding<T>(this T el, Thickness thickness) where T : Element
T Padding<T>(this T el, double uniform) where T : Element
T Padding<T>(this T el, double horizontal, double vertical) where T : Element
T Padding<T>(this T el, double left = 0, double top = 0, double right = 0, double bottom = 0) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `thickness` | `Thickness` |  |  |
| `uniform` | `double` |  |  |
| `horizontal` | `double` |  |  |
| `vertical` | `double` |  |  |
| `left` | `double` | `0` |  |
| `top` | `double` | `0` |  |
| `right` | `double` | `0` |  |
| `bottom` | `double` | `0` |  |

## `PaddingInlineEnd`

```csharp
T PaddingInlineEnd<T>(this T el, double value) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `double` |  |

## `PaddingInlineStart`

```csharp
T PaddingInlineStart<T>(this T el, double value) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `double` |  |

## `RelativePanel`

Sets RelativePanel attached properties on this element. Only meaningful when the element is a child of a RelativePanel.

```csharp
T RelativePanel<T>(this T el, string name, string? rightOf = null, string? below = null, string? leftOf = null, string? above = null, string? alignLeftWith = null, string? alignRightWith = null, string? alignTopWith = null, string? alignBottomWith = null, string? alignHorizontalCenterWith = null, string? alignVerticalCenterWith = null, bool alignLeftWithPanel = false, bool alignRightWithPanel = false, bool alignTopWithPanel = false, bool alignBottomWithPanel = false, bool alignHorizontalCenterWithPanel = false, bool alignVerticalCenterWithPanel = false) where T : Element
```

Declared in `RelativePanelExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `name` | `string` |  |  |
| `rightOf` | `string?` | `null` |  |
| `below` | `string?` | `null` |  |
| `leftOf` | `string?` | `null` |  |
| `above` | `string?` | `null` |  |
| `alignLeftWith` | `string?` | `null` |  |
| `alignRightWith` | `string?` | `null` |  |
| `alignTopWith` | `string?` | `null` |  |
| `alignBottomWith` | `string?` | `null` |  |
| `alignHorizontalCenterWith` | `string?` | `null` |  |
| `alignVerticalCenterWith` | `string?` | `null` |  |
| `alignLeftWithPanel` | `bool` | `false` |  |
| `alignRightWithPanel` | `bool` | `false` |  |
| `alignTopWithPanel` | `bool` | `false` |  |
| `alignBottomWithPanel` | `bool` | `false` |  |
| `alignHorizontalCenterWithPanel` | `bool` | `false` |  |
| `alignVerticalCenterWithPanel` | `bool` | `false` |  |

## `Size`

```csharp
T Size<T>(this T el, double width, double height) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `width` | `double` |  |
| `height` | `double` |  |

## `VAlign`

```csharp
T VAlign<T>(this T el, VerticalAlignment alignment) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `alignment` | `VerticalAlignment` |  |

## `VerticalAlignment`

```csharp
T VerticalAlignment<T>(this T el, VerticalAlignment alignment) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `alignment` | `VerticalAlignment` |  |

## `VerticalContentAlignment`

```csharp
T VerticalContentAlignment<T>(this T el, VerticalAlignment alignment) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `alignment` | `VerticalAlignment` |  |

## `Visible`

Sets whether the element is visible or collapsed.

```csharp
T Visible<T>(this T el, bool isVisible) where T : Element
```

Declared in `ElementExtensions`.

> **Obsolete.** Use IsVisible() — aligns with WinUI boolean modifier naming (see #268).

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `isVisible` | `bool` |  |

## `Width`

```csharp
T Width<T>(this T el, double width) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `width` | `double` |  |

## `WrapGridColumnSpan`

Sets `VariableSizedWrapGrid.ColumnSpan` on this child. Only meaningful when the element is a child of a [`WrapGridElement`](../elements/layout.md#wrapgridelement).

```csharp
T WrapGridColumnSpan<T>(this T el, int columnSpan) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `columnSpan` | `int` |  |

## `WrapGridRowSpan`

Sets `VariableSizedWrapGrid.RowSpan` on this child. Only meaningful when the element is a child of a [`WrapGridElement`](../elements/layout.md#wrapgridelement).

```csharp
T WrapGridRowSpan<T>(this T el, int rowSpan) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `rowSpan` | `int` |  |
