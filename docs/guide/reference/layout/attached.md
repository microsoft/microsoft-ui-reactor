# Attached layout properties

Records that hold the attached properties set by the layout modifiers.

**Learn more:** [Layout](../../layout.md)

## `CanvasAttached`

Attached property data for Canvas children. Set via .Canvas(left:, top:) extension.

```csharp
public record CanvasAttached(double Left = 0, double Top = 0)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Remarks

[`CanvasAttached.AnchorX`](#anchorx) / [`CanvasAttached.AnchorY`](#anchory) are 0..1 fractions of the element's rendered size that are subtracted from [`CanvasAttached.Left`](#left)/[`CanvasAttached.Top`](#top) after layout. Anchor 0,0 (default) keeps the legacy top-left positioning. Anchor 0.5,0.5 centers the element on (Left, Top); 1,1 anchors at bottom-right. Useful for chart labels and other elements that need to align around a logical point rather than their top-left corner.

### Constructors

#### `CanvasAttached`

Attached property data for Canvas children. Set via .Canvas(left:, top:) extension.

```csharp
public CanvasAttached(double Left = 0, double Top = 0)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Left` | `double` | `0` |  |
| `Top` | `double` | `0` |  |

**Remarks**

[`CanvasAttached.AnchorX`](#anchorx) / [`CanvasAttached.AnchorY`](#anchory) are 0..1 fractions of the element's rendered size that are subtracted from [`CanvasAttached.Left`](#left)/[`CanvasAttached.Top`](#top) after layout. Anchor 0,0 (default) keeps the legacy top-left positioning. Anchor 0.5,0.5 centers the element on (Left, Top); 1,1 anchors at bottom-right. Useful for chart labels and other elements that need to align around a logical point rather than their top-left corner.

### Properties

#### `AnchorX`

Horizontal anchor as a 0..1 fraction of the element's rendered width.

```csharp
public double AnchorX  // get, init
```

#### `AnchorY`

Vertical anchor as a 0..1 fraction of the element's rendered height.

```csharp
public double AnchorY  // get, init
```

#### `Left`

```csharp
public double Left  // get, init
```

#### `Top`

```csharp
public double Top  // get, init
```

## `FlexAttached`

Attached property data for Flex children. Set via .Flex(grow:, shrink:, ...) extension.

```csharp
public record FlexAttached(double Grow = 0, double Shrink = 1, double? Basis = null, double? MinWidth = null, double? MinHeight = null, FlexAlign? AlignSelf = null, FlexPositionType Position = FlexPositionType.Relative, double? Left = null, double? Top = null, double? Right = null, double? Bottom = null)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `FlexAttached`

Attached property data for Flex children. Set via .Flex(grow:, shrink:, ...) extension.

```csharp
public FlexAttached(double Grow = 0, double Shrink = 1, double? Basis = null, double? MinWidth = null, double? MinHeight = null, FlexAlign? AlignSelf = null, FlexPositionType Position = FlexPositionType.Relative, double? Left = null, double? Top = null, double? Right = null, double? Bottom = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Grow` | `double` | `0` |  |
| `Shrink` | `double` | `1` |  |
| `Basis` | `double?` | `null` |  |
| `MinWidth` | `double?` | `null` |  |
| `MinHeight` | `double?` | `null` |  |
| `AlignSelf` | `FlexAlign?` | `null` |  |
| `Position` | [`FlexPositionType`](flex-enums.md#flexpositiontype) | `FlexPositionType.Relative` |  |
| `Left` | `double?` | `null` |  |
| `Top` | `double?` | `null` |  |
| `Right` | `double?` | `null` |  |
| `Bottom` | `double?` | `null` |  |

### Properties

#### `AlignSelf`

```csharp
public FlexAlign? AlignSelf  // get, init
```

#### `Basis`

```csharp
public double? Basis  // get, init
```

#### `Bottom`

```csharp
public double? Bottom  // get, init
```

#### `Grow`

```csharp
public double Grow  // get, init
```

#### `Left`

```csharp
public double? Left  // get, init
```

#### `MinHeight`

```csharp
public double? MinHeight  // get, init
```

#### `MinWidth`

```csharp
public double? MinWidth  // get, init
```

#### `Position`

```csharp
public FlexPositionType Position  // get, init
```

#### `Right`

```csharp
public double? Right  // get, init
```

#### `Shrink`

```csharp
public double Shrink  // get, init
```

#### `Top`

```csharp
public double? Top  // get, init
```

## `GridAttached`

Attached property data for Grid children. Set via .Grid(row:, column:) extension.

```csharp
public record GridAttached(int Row = 0, int Column = 0, int RowSpan = 1, int ColumnSpan = 1)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `GridAttached`

Attached property data for Grid children. Set via .Grid(row:, column:) extension.

```csharp
public GridAttached(int Row = 0, int Column = 0, int RowSpan = 1, int ColumnSpan = 1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Row` | `int` | `0` |  |
| `Column` | `int` | `0` |  |
| `RowSpan` | `int` | `1` |  |
| `ColumnSpan` | `int` | `1` |  |

### Properties

#### `Column`

```csharp
public int Column  // get, init
```

#### `ColumnSpan`

```csharp
public int ColumnSpan  // get, init
```

#### `Row`

```csharp
public int Row  // get, init
```

#### `RowSpan`

```csharp
public int RowSpan  // get, init
```

## `GridDefinition`

```csharp
public record GridDefinition(GridSize[] Columns, GridSize[] Rows)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `GridDefinition`

Construct a [`GridDefinition`](#griddefinition) from the strongly-typed [`GridSize`](GridSize.md) form. Track strings are produced via [`GridSize.ToString`](GridSize.md) using `CultureInfo.InvariantCulture`. Spec 033 §1.

```csharp
public GridDefinition(GridSize[] Columns, GridSize[] Rows)
public GridDefinition(string[] columns, string[] rows)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Columns` | [`GridSize[]`](GridSize.md) |  |
| `Rows` | [`GridSize[]`](GridSize.md) |  |
| `columns` | `string[]` |  |
| `rows` | `string[]` |  |

**Exceptions**

| Exception | Condition |
| --- | --- |
| [`ArgumentNullException`](https://learn.microsoft.com/dotnet/api/system.argumentnullexception) | Thrown when either array is null. |

### Properties

#### `Columns`

```csharp
public GridSize[] Columns  // get, init
```

#### `Rows`

```csharp
public GridSize[] Rows  // get, init
```

## `RelativePanelAttached`

Attached property data for RelativePanel children. Set via .RelativePanel(...) extension.

```csharp
public record RelativePanelAttached(string Name)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `RelativePanelAttached`

Attached property data for RelativePanel children. Set via .RelativePanel(...) extension.

```csharp
public RelativePanelAttached(string Name)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Name` | `string` |  |

### Properties

#### `Above`

```csharp
public string? Above  // get, init
```

#### `AlignBottomWith`

```csharp
public string? AlignBottomWith  // get, init
```

#### `AlignBottomWithPanel`

```csharp
public bool AlignBottomWithPanel  // get, init
```

#### `AlignHorizontalCenterWith`

```csharp
public string? AlignHorizontalCenterWith  // get, init
```

#### `AlignHorizontalCenterWithPanel`

```csharp
public bool AlignHorizontalCenterWithPanel  // get, init
```

#### `AlignLeftWith`

```csharp
public string? AlignLeftWith  // get, init
```

#### `AlignLeftWithPanel`

```csharp
public bool AlignLeftWithPanel  // get, init
```

#### `AlignRightWith`

```csharp
public string? AlignRightWith  // get, init
```

#### `AlignRightWithPanel`

```csharp
public bool AlignRightWithPanel  // get, init
```

#### `AlignTopWith`

```csharp
public string? AlignTopWith  // get, init
```

#### `AlignTopWithPanel`

```csharp
public bool AlignTopWithPanel  // get, init
```

#### `AlignVerticalCenterWith`

```csharp
public string? AlignVerticalCenterWith  // get, init
```

#### `AlignVerticalCenterWithPanel`

```csharp
public bool AlignVerticalCenterWithPanel  // get, init
```

#### `Below`

```csharp
public string? Below  // get, init
```

#### `LeftOf`

```csharp
public string? LeftOf  // get, init
```

#### `Name`

```csharp
public string Name  // get, init
```

#### `RightOf`

```csharp
public string? RightOf  // get, init
```

## `WrapGridAttached`

Attached property data for VariableSizedWrapGrid children. Set via `.WrapGridColumnSpan(int)` / `.WrapGridRowSpan(int)` extensions.

```csharp
public record WrapGridAttached(int RowSpan = 1, int ColumnSpan = 1)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `WrapGridAttached`

Attached property data for VariableSizedWrapGrid children. Set via `.WrapGridColumnSpan(int)` / `.WrapGridRowSpan(int)` extensions.

```csharp
public WrapGridAttached(int RowSpan = 1, int ColumnSpan = 1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `RowSpan` | `int` | `1` |  |
| `ColumnSpan` | `int` | `1` |  |

### Properties

#### `ColumnSpan`

```csharp
public int ColumnSpan  // get, init
```

#### `RowSpan`

```csharp
public int RowSpan  // get, init
```
