# Flex enums

Enums that control the flex layout.

**Learn more:** [Flex Layout](../../flex-layout.md)

## `FlexAlign`

```csharp
public enum FlexAlign
```

Namespace: `Microsoft.UI.Reactor.Layout`.

### Values

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

## `FlexDirection`

```csharp
public enum FlexDirection
```

Namespace: `Microsoft.UI.Reactor.Layout`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Column` | `0` |  |
| `ColumnReverse` | `1` |  |
| `Row` | `2` |  |
| `RowReverse` | `3` |  |

## `FlexJustify`

```csharp
public enum FlexJustify
```

Namespace: `Microsoft.UI.Reactor.Layout`.

### Values

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

## `FlexLayoutDirection`

```csharp
public enum FlexLayoutDirection
```

Namespace: `Microsoft.UI.Reactor.Layout`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Inherit` | `0` |  |
| `LTR` | `1` |  |
| `RTL` | `2` |  |

## `FlexPanel`

A WinUI3 Panel that implements CSS Flexbox layout using the Yoga layout engine. Can be used standalone in XAML or through the Reactor framework.

```csharp
public class FlexPanel : Panel
```

Namespace: `Microsoft.UI.Reactor.Layout`. Inherits from [`Panel`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.panel).

### Constructors

#### `FlexPanel`

```csharp
public FlexPanel()
```

### Properties

#### `AlignContent`

```csharp
public FlexAlign AlignContent  // get, set
```

#### `AlignItems`

```csharp
public FlexAlign AlignItems  // get, set
```

#### `ColumnGap`

```csharp
public double ColumnGap  // get, set
```

#### `Direction`

```csharp
public FlexDirection Direction  // get, set
```

#### `FlexPadding`

```csharp
public Thickness FlexPadding  // get, set
```

#### `JustifyContent`

```csharp
public FlexJustify JustifyContent  // get, set
```

#### `LayoutDirection`

```csharp
public FlexLayoutDirection LayoutDirection  // get, set
```

#### `RowGap`

```csharp
public double RowGap  // get, set
```

#### `Wrap`

```csharp
public FlexWrap Wrap  // get, set
```

### Methods

#### `ArrangeOverride`

```csharp
protected override Size ArrangeOverride(Size finalSize)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `finalSize` | `Size` |  |

#### `GetAlignSelf`

```csharp
public static FlexAlign GetAlignSelf(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `GetBasis`

```csharp
public static double GetBasis(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `GetBottom`

```csharp
public static double GetBottom(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `GetGrow`

```csharp
public static double GetGrow(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `GetLeft`

```csharp
public static double GetLeft(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `GetMinHeight`

```csharp
public static double GetMinHeight(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `GetMinWidth`

```csharp
public static double GetMinWidth(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `GetPosition`

```csharp
public static FlexPositionType GetPosition(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `GetRight`

```csharp
public static double GetRight(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `GetShrink`

```csharp
public static double GetShrink(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `GetTop`

```csharp
public static double GetTop(UIElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |

#### `MeasureOverride`

```csharp
protected override Size MeasureOverride(Size availableSize)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `availableSize` | `Size` |  |

#### `SetAlignSelf`

```csharp
public static void SetAlignSelf(UIElement el, FlexAlign value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | [`FlexAlign`](#flexalign) |  |

#### `SetBasis`

```csharp
public static void SetBasis(UIElement el, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | `double` |  |

#### `SetBottom`

```csharp
public static void SetBottom(UIElement el, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | `double` |  |

#### `SetGrow`

```csharp
public static void SetGrow(UIElement el, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | `double` |  |

#### `SetLeft`

```csharp
public static void SetLeft(UIElement el, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | `double` |  |

#### `SetMinHeight`

```csharp
public static void SetMinHeight(UIElement el, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | `double` |  |

#### `SetMinWidth`

```csharp
public static void SetMinWidth(UIElement el, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | `double` |  |

#### `SetPosition`

```csharp
public static void SetPosition(UIElement el, FlexPositionType value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | [`FlexPositionType`](#flexpositiontype) |  |

#### `SetRight`

```csharp
public static void SetRight(UIElement el, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | `double` |  |

#### `SetShrink`

```csharp
public static void SetShrink(UIElement el, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | `double` |  |

#### `SetTop`

```csharp
public static void SetTop(UIElement el, double value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `el` | `UIElement` |  |
| `value` | `double` |  |

### Fields

#### `AlignContentProperty`

```csharp
public static readonly DependencyProperty AlignContentProperty
```

#### `AlignItemsProperty`

```csharp
public static readonly DependencyProperty AlignItemsProperty
```

#### `AlignSelfProperty`

```csharp
public static readonly DependencyProperty AlignSelfProperty
```

#### `BasisProperty`

```csharp
public static readonly DependencyProperty BasisProperty
```

#### `BottomProperty`

```csharp
public static readonly DependencyProperty BottomProperty
```

#### `ColumnGapProperty`

```csharp
public static readonly DependencyProperty ColumnGapProperty
```

#### `DirectionProperty`

```csharp
public static readonly DependencyProperty DirectionProperty
```

#### `FlexMinHeightProperty`

```csharp
public static readonly DependencyProperty FlexMinHeightProperty
```

#### `FlexMinWidthProperty`

```csharp
public static readonly DependencyProperty FlexMinWidthProperty
```

#### `FlexPaddingProperty`

```csharp
public static readonly DependencyProperty FlexPaddingProperty
```

#### `GrowProperty`

```csharp
public static readonly DependencyProperty GrowProperty
```

#### `JustifyContentProperty`

```csharp
public static readonly DependencyProperty JustifyContentProperty
```

#### `LayoutDirectionProperty`

```csharp
public static readonly DependencyProperty LayoutDirectionProperty
```

#### `LeftProperty`

```csharp
public static readonly DependencyProperty LeftProperty
```

#### `PositionProperty`

```csharp
public static readonly DependencyProperty PositionProperty
```

#### `RightProperty`

```csharp
public static readonly DependencyProperty RightProperty
```

#### `RowGapProperty`

```csharp
public static readonly DependencyProperty RowGapProperty
```

#### `ShrinkProperty`

```csharp
public static readonly DependencyProperty ShrinkProperty
```

#### `TopProperty`

```csharp
public static readonly DependencyProperty TopProperty
```

#### `WrapProperty`

```csharp
public static readonly DependencyProperty WrapProperty
```

## `FlexPositionType`

```csharp
public enum FlexPositionType
```

Namespace: `Microsoft.UI.Reactor.Layout`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Static` | `0` |  |
| `Relative` | `1` |  |
| `Absolute` | `2` |  |

## `FlexWrap`

```csharp
public enum FlexWrap
```

Namespace: `Microsoft.UI.Reactor.Layout`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `NoWrap` | `0` |  |
| `Wrap` | `1` |  |
| `WrapReverse` | `2` |  |
