---
title: "Flex enums"
description: "Enums that control the flex layout."
---

**Learn more:** [Flex Layout](/microsoft-ui-reactor/main/guides/flex-layout)

## `FlexAlign` [#flexalign]

```csharp
public enum FlexAlign
```

Namespace: `Microsoft.UI.Reactor.Layout`.

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `Auto` | `0` |  |
| `FlexStart` | `1` |  |
| `Center` | `2` |  |
| `FlexEnd` | `3` |  |
| `Stretch` | `4` |  |
| `Baseline` | `5` |  |
| `SpaceBetween` | `6` |  |
| `SpaceAround` | `7` |  |
| `SpaceEvenly` | `8` |  |
| `Start` | `9` |  |
| `End` | `10` |  |

## `FlexDirection` [#flexdirection]

```csharp
public enum FlexDirection
```

Namespace: `Microsoft.UI.Reactor.Layout`.

### Values [#values_1]

| Name | Value | Description |
| --- | --- | --- |
| `Column` | `0` |  |
| `ColumnReverse` | `1` |  |
| `Row` | `2` |  |
| `RowReverse` | `3` |  |

## `FlexJustify` [#flexjustify]

```csharp
public enum FlexJustify
```

Namespace: `Microsoft.UI.Reactor.Layout`.

### Values [#values_2]

| Name | Value | Description |
| --- | --- | --- |
| `Auto` | `0` |  |
| `FlexStart` | `1` |  |
| `Center` | `2` |  |
| `FlexEnd` | `3` |  |
| `SpaceBetween` | `4` |  |
| `SpaceAround` | `5` |  |
| `SpaceEvenly` | `6` |  |
| `Stretch` | `7` |  |
| `Start` | `8` |  |
| `End` | `9` |  |

## `FlexLayoutDirection` [#flexlayoutdirection]

```csharp
public enum FlexLayoutDirection
```

Namespace: `Microsoft.UI.Reactor.Layout`.

### Values [#values_3]

| Name | Value | Description |
| --- | --- | --- |
| `Inherit` | `0` |  |
| `LTR` | `1` |  |
| `RTL` | `2` |  |

## `FlexPanel` [#flexpanel]

A WinUI3 Panel that implements CSS Flexbox layout using the Yoga layout engine. Can be used standalone in XAML or through the Reactor framework.

```csharp
public class FlexPanel : Panel
```

Namespace: `Microsoft.UI.Reactor.Layout`. Inherits from [`Panel`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.panel).

### Constructors [#constructors]

#### `FlexPanel` [#flexpanel_1]

```csharp
public FlexPanel()
```

### Properties [#properties]

#### `AlignContent` [#aligncontent]

```csharp
public FlexAlign AlignContent  // get, set
```

#### `AlignItems` [#alignitems]

```csharp
public FlexAlign AlignItems  // get, set
```

#### `ColumnGap` [#columngap]

```csharp
public double ColumnGap  // get, set
```

#### `Direction` [#direction]

```csharp
public FlexDirection Direction  // get, set
```

#### `FlexPadding` [#flexpadding]

```csharp
public Thickness FlexPadding  // get, set
```

#### `JustifyContent` [#justifycontent]

```csharp
public FlexJustify JustifyContent  // get, set
```

#### `LayoutDirection` [#layoutdirection]

```csharp
public FlexLayoutDirection LayoutDirection  // get, set
```

#### `RowGap` [#rowgap]

```csharp
public double RowGap  // get, set
```

#### `Wrap` [#wrap]

```csharp
public FlexWrap Wrap  // get, set
```

### Methods [#methods]

#### `ArrangeOverride` [#arrangeoverride]

```csharp
protected override Size ArrangeOverride(Size finalSize)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `finalSize` | `Size` |  |

#### `GetAlignSelf` [#getalignself]

```csharp
public static FlexAlign GetAlignSelf(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `GetBasis` [#getbasis]

```csharp
public static double GetBasis(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `GetBottom` [#getbottom]

```csharp
public static double GetBottom(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `GetGrow` [#getgrow]

```csharp
public static double GetGrow(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `GetLeft` [#getleft]

```csharp
public static double GetLeft(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `GetMinHeight` [#getminheight]

```csharp
public static double GetMinHeight(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `GetMinWidth` [#getminwidth]

```csharp
public static double GetMinWidth(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `GetPosition` [#getposition]

```csharp
public static FlexPositionType GetPosition(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `GetRight` [#getright]

```csharp
public static double GetRight(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `GetShrink` [#getshrink]

```csharp
public static double GetShrink(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `GetTop` [#gettop]

```csharp
public static double GetTop(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `MeasureOverride` [#measureoverride]

```csharp
protected override Size MeasureOverride(Size availableSize)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `availableSize` | `Size` |  |

#### `SetAlignSelf` [#setalignself]

```csharp
public static void SetAlignSelf(UIElement el, FlexAlign value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | [`FlexAlign`](#flexalign) |  |

#### `SetBasis` [#setbasis]

```csharp
public static void SetBasis(UIElement el, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | `double` |  |

#### `SetBottom` [#setbottom]

```csharp
public static void SetBottom(UIElement el, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | `double` |  |

#### `SetGrow` [#setgrow]

```csharp
public static void SetGrow(UIElement el, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | `double` |  |

#### `SetLeft` [#setleft]

```csharp
public static void SetLeft(UIElement el, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | `double` |  |

#### `SetMinHeight` [#setminheight]

```csharp
public static void SetMinHeight(UIElement el, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | `double` |  |

#### `SetMinWidth` [#setminwidth]

```csharp
public static void SetMinWidth(UIElement el, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | `double` |  |

#### `SetPosition` [#setposition]

```csharp
public static void SetPosition(UIElement el, FlexPositionType value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | [`FlexPositionType`](#flexpositiontype) |  |

#### `SetRight` [#setright]

```csharp
public static void SetRight(UIElement el, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | `double` |  |

#### `SetShrink` [#setshrink]

```csharp
public static void SetShrink(UIElement el, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | `double` |  |

#### `SetTop` [#settop]

```csharp
public static void SetTop(UIElement el, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | `double` |  |

### Fields [#fields]

#### `AlignContentProperty` [#aligncontentproperty]

```csharp
public static readonly DependencyProperty AlignContentProperty
```

#### `AlignItemsProperty` [#alignitemsproperty]

```csharp
public static readonly DependencyProperty AlignItemsProperty
```

#### `AlignSelfProperty` [#alignselfproperty]

```csharp
public static readonly DependencyProperty AlignSelfProperty
```

#### `BasisProperty` [#basisproperty]

```csharp
public static readonly DependencyProperty BasisProperty
```

#### `BottomProperty` [#bottomproperty]

```csharp
public static readonly DependencyProperty BottomProperty
```

#### `ColumnGapProperty` [#columngapproperty]

```csharp
public static readonly DependencyProperty ColumnGapProperty
```

#### `DirectionProperty` [#directionproperty]

```csharp
public static readonly DependencyProperty DirectionProperty
```

#### `FlexMinHeightProperty` [#flexminheightproperty]

```csharp
public static readonly DependencyProperty FlexMinHeightProperty
```

#### `FlexMinWidthProperty` [#flexminwidthproperty]

```csharp
public static readonly DependencyProperty FlexMinWidthProperty
```

#### `FlexPaddingProperty` [#flexpaddingproperty]

```csharp
public static readonly DependencyProperty FlexPaddingProperty
```

#### `GrowProperty` [#growproperty]

```csharp
public static readonly DependencyProperty GrowProperty
```

#### `JustifyContentProperty` [#justifycontentproperty]

```csharp
public static readonly DependencyProperty JustifyContentProperty
```

#### `LayoutDirectionProperty` [#layoutdirectionproperty]

```csharp
public static readonly DependencyProperty LayoutDirectionProperty
```

#### `LeftProperty` [#leftproperty]

```csharp
public static readonly DependencyProperty LeftProperty
```

#### `PositionProperty` [#positionproperty]

```csharp
public static readonly DependencyProperty PositionProperty
```

#### `RightProperty` [#rightproperty]

```csharp
public static readonly DependencyProperty RightProperty
```

#### `RowGapProperty` [#rowgapproperty]

```csharp
public static readonly DependencyProperty RowGapProperty
```

#### `ShrinkProperty` [#shrinkproperty]

```csharp
public static readonly DependencyProperty ShrinkProperty
```

#### `TopProperty` [#topproperty]

```csharp
public static readonly DependencyProperty TopProperty
```

#### `WrapProperty` [#wrapproperty]

```csharp
public static readonly DependencyProperty WrapProperty
```

## `FlexPositionType` [#flexpositiontype]

```csharp
public enum FlexPositionType
```

Namespace: `Microsoft.UI.Reactor.Layout`.

### Values [#values_4]

| Name | Value | Description |
| --- | --- | --- |
| `Static` | `0` |  |
| `Relative` | `1` |  |
| `Absolute` | `2` |  |

## `FlexWrap` [#flexwrap]

```csharp
public enum FlexWrap
```

Namespace: `Microsoft.UI.Reactor.Layout`.

### Values [#values_5]

| Name | Value | Description |
| --- | --- | --- |
| `NoWrap` | `0` |  |
| `Wrap` | `1` |  |
| `WrapReverse` | `2` |  |
