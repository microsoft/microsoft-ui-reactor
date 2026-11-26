---
title: "Attached layout properties"
description: "Records that hold the attached properties set by the layout modifiers."
---

**Learn more:** [Layout](/microsoft-ui-reactor/main/guides/layout)

## `CanvasAttached` [#canvasattached]

Attached property data for Canvas children. Set via .Canvas(left:, top:) extension.

```csharp
public record CanvasAttached(double Left = 0, double Top = 0)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Remarks [#remarks]

[`CanvasAttached.AnchorX`](#anchorx) / [`CanvasAttached.AnchorY`](#anchory) are 0..1 fractions of the element's rendered size that are subtracted from [`CanvasAttached.Left`](#left)/[`CanvasAttached.Top`](#top) after layout. Anchor 0,0 (default) keeps the legacy top-left positioning. Anchor 0.5,0.5 centers the element on (Left, Top); 1,1 anchors at bottom-right. Useful for chart labels and other elements that need to align around a logical point rather than their top-left corner.

### Constructors [#constructors]

#### `CanvasAttached` [#canvasattached_1]

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

### Properties [#properties]

#### `AnchorX` [#anchorx]

Horizontal anchor as a 0..1 fraction of the element's rendered width.

```csharp
public double AnchorX  // get, init
```

#### `AnchorY` [#anchory]

Vertical anchor as a 0..1 fraction of the element's rendered height.

```csharp
public double AnchorY  // get, init
```

#### `Left` [#left]

```csharp
public double Left  // get, init
```

#### `Top` [#top]

```csharp
public double Top  // get, init
```

## `FlexAttached` [#flexattached]

Attached property data for Flex children. Set via .Flex(grow:, shrink:, ...) extension.

```csharp
public record FlexAttached(double Grow = 0, double Shrink = 1, double? Basis = null, double? MinWidth = null, double? MinHeight = null, FlexAlign? AlignSelf = null, FlexPositionType Position = FlexPositionType.Relative, double? Left = null, double? Top = null, double? Right = null, double? Bottom = null)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors [#constructors_1]

#### `FlexAttached` [#flexattached_1]

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
| `Position` | [`FlexPositionType`](/microsoft-ui-reactor/main/reference/layout/flex-enums#flexpositiontype) | `FlexPositionType.Relative` |  |
| `Left` | `double?` | `null` |  |
| `Top` | `double?` | `null` |  |
| `Right` | `double?` | `null` |  |
| `Bottom` | `double?` | `null` |  |

### Properties [#properties_1]

#### `AlignSelf` [#alignself]

```csharp
public FlexAlign? AlignSelf  // get, init
```

#### `Basis` [#basis]

```csharp
public double? Basis  // get, init
```

#### `Bottom` [#bottom]

```csharp
public double? Bottom  // get, init
```

#### `Grow` [#grow]

```csharp
public double Grow  // get, init
```

#### `Left` [#left_1]

```csharp
public double? Left  // get, init
```

#### `MinHeight` [#minheight]

```csharp
public double? MinHeight  // get, init
```

#### `MinWidth` [#minwidth]

```csharp
public double? MinWidth  // get, init
```

#### `Position` [#position]

```csharp
public FlexPositionType Position  // get, init
```

#### `Right` [#right]

```csharp
public double? Right  // get, init
```

#### `Shrink` [#shrink]

```csharp
public double Shrink  // get, init
```

#### `Top` [#top_1]

```csharp
public double? Top  // get, init
```

## `GridAttached` [#gridattached]

Attached property data for Grid children. Set via .Grid(row:, column:) extension.

```csharp
public record GridAttached(int Row = 0, int Column = 0, int RowSpan = 1, int ColumnSpan = 1)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors [#constructors_2]

#### `GridAttached` [#gridattached_1]

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

### Properties [#properties_2]

#### `Column` [#column]

```csharp
public int Column  // get, init
```

#### `ColumnSpan` [#columnspan]

```csharp
public int ColumnSpan  // get, init
```

#### `Row` [#row]

```csharp
public int Row  // get, init
```

#### `RowSpan` [#rowspan]

```csharp
public int RowSpan  // get, init
```

## `GridDefinition` [#griddefinition]

```csharp
public record GridDefinition(GridSize[] Columns, GridSize[] Rows)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors [#constructors_3]

#### `GridDefinition` [#griddefinition_1]

##### `GridDefinition(GridSize[], GridSize[])` [#griddefinitiongridsize-gridsize]

```csharp
public GridDefinition(GridSize[] Columns, GridSize[] Rows)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Columns` | [`GridSize[]`](/microsoft-ui-reactor/main/reference/layout/GridSize) |  |
| `Rows` | [`GridSize[]`](/microsoft-ui-reactor/main/reference/layout/GridSize) |  |

##### `GridDefinition(string[], string[])` [#griddefinitionstring-string]

Construct a [`GridDefinition`](#griddefinition) from the strongly-typed [`GridSize`](/microsoft-ui-reactor/main/reference/layout/GridSize) form. Track strings are produced via [`GridSize.ToString`](/microsoft-ui-reactor/main/reference/layout/GridSize) using `CultureInfo.InvariantCulture`. Spec 033 §1.

```csharp
public GridDefinition(string[] columns, string[] rows)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `columns` | `string[]` |  |
| `rows` | `string[]` |  |

**Exceptions**

| Exception | Condition |
| --- | --- |
| [`ArgumentNullException`](https://learn.microsoft.com/dotnet/api/system.argumentnullexception) | Thrown when either array is null. |

### Properties [#properties_3]

#### `Columns` [#columns]

```csharp
public GridSize[] Columns  // get, init
```

#### `Rows` [#rows]

```csharp
public GridSize[] Rows  // get, init
```

## `RelativePanelAttached` [#relativepanelattached]

Attached property data for RelativePanel children. Set via .RelativePanel(...) extension.

```csharp
public record RelativePanelAttached(string Name)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors [#constructors_4]

#### `RelativePanelAttached` [#relativepanelattached_1]

Attached property data for RelativePanel children. Set via .RelativePanel(...) extension.

```csharp
public RelativePanelAttached(string Name)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Name` | `string` |  |

### Properties [#properties_4]

#### `Above` [#above]

```csharp
public string? Above  // get, init
```

#### `AlignBottomWith` [#alignbottomwith]

```csharp
public string? AlignBottomWith  // get, init
```

#### `AlignBottomWithPanel` [#alignbottomwithpanel]

```csharp
public bool AlignBottomWithPanel  // get, init
```

#### `AlignHorizontalCenterWith` [#alignhorizontalcenterwith]

```csharp
public string? AlignHorizontalCenterWith  // get, init
```

#### `AlignHorizontalCenterWithPanel` [#alignhorizontalcenterwithpanel]

```csharp
public bool AlignHorizontalCenterWithPanel  // get, init
```

#### `AlignLeftWith` [#alignleftwith]

```csharp
public string? AlignLeftWith  // get, init
```

#### `AlignLeftWithPanel` [#alignleftwithpanel]

```csharp
public bool AlignLeftWithPanel  // get, init
```

#### `AlignRightWith` [#alignrightwith]

```csharp
public string? AlignRightWith  // get, init
```

#### `AlignRightWithPanel` [#alignrightwithpanel]

```csharp
public bool AlignRightWithPanel  // get, init
```

#### `AlignTopWith` [#aligntopwith]

```csharp
public string? AlignTopWith  // get, init
```

#### `AlignTopWithPanel` [#aligntopwithpanel]

```csharp
public bool AlignTopWithPanel  // get, init
```

#### `AlignVerticalCenterWith` [#alignverticalcenterwith]

```csharp
public string? AlignVerticalCenterWith  // get, init
```

#### `AlignVerticalCenterWithPanel` [#alignverticalcenterwithpanel]

```csharp
public bool AlignVerticalCenterWithPanel  // get, init
```

#### `Below` [#below]

```csharp
public string? Below  // get, init
```

#### `LeftOf` [#leftof]

```csharp
public string? LeftOf  // get, init
```

#### `Name` [#name]

```csharp
public string Name  // get, init
```

#### `RightOf` [#rightof]

```csharp
public string? RightOf  // get, init
```

## `WrapGridAttached` [#wrapgridattached]

Attached property data for VariableSizedWrapGrid children. Set via `.WrapGridColumnSpan(int)` / `.WrapGridRowSpan(int)` extensions.

```csharp
public record WrapGridAttached(int RowSpan = 1, int ColumnSpan = 1)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors [#constructors_5]

#### `WrapGridAttached` [#wrapgridattached_1]

Attached property data for VariableSizedWrapGrid children. Set via `.WrapGridColumnSpan(int)` / `.WrapGridRowSpan(int)` extensions.

```csharp
public WrapGridAttached(int RowSpan = 1, int ColumnSpan = 1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `RowSpan` | `int` | `1` |  |
| `ColumnSpan` | `int` | `1` |  |

### Properties [#properties_5]

#### `ColumnSpan` [#columnspan_1]

```csharp
public int ColumnSpan  // get, init
```

#### `RowSpan` [#rowspan_1]

```csharp
public int RowSpan  // get, init
```
