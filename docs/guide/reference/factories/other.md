# Factories: Other

Controls that do not fit one of the other areas.

Each factory method returns an element record. These are on `Microsoft.UI.Reactor.Advanced.Factories` in the `Microsoft.UI.Reactor.Advanced` package, so add `using static Microsoft.UI.Reactor.Advanced.Factories;`.

## `AreaSeries`

Creates an area series.

```csharp
ChartAreaSeries AreaSeries(IEnumerable xValues, IEnumerable yValues, string? title = null, ChartAxis? xAxis = null, ChartAxis? yAxis = null)
```

Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.

**Returns:** `ChartAreaSeries`.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `xValues` | `IEnumerable` |  | X values: strings or numbers (plotted as categories), or dates. |
| `yValues` | `IEnumerable` |  | Y values, one per X value. |
| `title` | `string?` | `null` | Legend title. |
| `xAxis` | `ChartAxis?` | `null` | Category or date-time axis; inferred from `xValues` when `null`. |
| `yAxis` | `ChartAxis?` | `null` | Value axis; a shared linear axis when `null`. |

## `BarSeries`

Creates a bar series. Bars are horizontal unless you call `.Vertical()` for columns.

```csharp
ChartBarSeries BarSeries(IEnumerable xValues, IEnumerable yValues, string? title = null, ChartAxis? xAxis = null, ChartAxis? yAxis = null)
```

Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.

**Returns:** `ChartBarSeries`.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `xValues` | `IEnumerable` |  | X values: strings or numbers (plotted as categories), or dates. |
| `yValues` | `IEnumerable` |  | Y values, one per X value. |
| `title` | `string?` | `null` | Legend title. |
| `xAxis` | `ChartAxis?` | `null` | Category or date-time axis; inferred from `xValues` when `null`. |
| `yAxis` | `ChartAxis?` | `null` | Value axis; a shared linear axis when `null`. |

## `BoundColumn`

Creates a column bound to the row property at `path` through a classic WinUI `{Binding}`; edits are written into the row object. Suits a mutable, `INotifyPropertyChanged` model — prefer [`TextColumn`](#textcolumn) for immutable rows.

```csharp
TableBoundColumn BoundColumn(object header, string path, double? width = null)
```

Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.

**Returns:** `TableBoundColumn`.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `header` | `object` |  | Header content (usually a string). |
| `path` | `string` |  | Property path of the bound value, for example `"Name"`. |
| `width` | `double?` | `null` | Optional width in pixels. |

## `CategoryAxis`

Creates a category axis for string or numeric X values.

```csharp
ChartCategoryAxis CategoryAxis(string? label = null)
```

Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.

**Returns:** `ChartCategoryAxis`.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `label` | `string?` | `null` | Axis title. |

## `Chart`

Creates a WinUI `Chart` that plots the given series.

```csharp
ChartElement Chart(params ChartSeries[] series)
```

Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.

**Returns:** [`ChartElement`](../elements/other.md#chartelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `series` | `ChartSeries[]` | Series in drawing order (see [`LineSeries`](#lineseries), [`BarSeries`](#barseries), [`AreaSeries`](#areaseries)). |

**Remarks**

Requires the Windows App SDK experimental channel; the API may change.

## `DateTimeAxis`

Creates a time axis for [`DateTimeOffset`](https://learn.microsoft.com/dotnet/api/system.datetimeoffset) X values.

```csharp
ChartDateTimeAxis DateTimeAxis(string? label = null, DateTimeOffset? minimum = null, DateTimeOffset? maximum = null)
```

Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.

**Returns:** `ChartDateTimeAxis`.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `label` | `string?` | `null` | Axis title. |
| `minimum` | `DateTimeOffset?` | `null` | Lower bound; computed from the data when `null`. |
| `maximum` | `DateTimeOffset?` | `null` | Upper bound; computed from the data when `null`. |

## `LineSeries`

Creates a line series.

```csharp
ChartLineSeries LineSeries(IEnumerable xValues, IEnumerable yValues, string? title = null, ChartAxis? xAxis = null, ChartAxis? yAxis = null)
```

Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.

**Returns:** `ChartLineSeries`.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `xValues` | `IEnumerable` |  | X values: strings or numbers (plotted as categories), or dates. |
| `yValues` | `IEnumerable` |  | Y values, one per X value. |
| `title` | `string?` | `null` | Legend title. |
| `xAxis` | `ChartAxis?` | `null` | Category or date-time axis; inferred from `xValues` when `null`. |
| `yAxis` | `ChartAxis?` | `null` | Value axis; a shared linear axis when `null`. |

## `LinearAxis`

Creates a numeric value axis, for a series' Y values.

```csharp
ChartLinearAxis LinearAxis(string? label = null, double? minimum = null, double? maximum = null)
```

Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.

**Returns:** `ChartLinearAxis`.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `label` | `string?` | `null` | Axis title. |
| `minimum` | `double?` | `null` | Lower bound; computed from the data when `null`. |
| `maximum` | `double?` | `null` | Upper bound; computed from the data when `null`. |

## `TableView`

Creates a WinUI `TableView`: a virtualized table over `itemsSource` with the given columns.

```csharp
TableViewElement TableView(IEnumerable? itemsSource, params TableColumn[] columns)
```

Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.

**Returns:** [`TableViewElement`](../elements/other.md#tableviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `itemsSource` | `IEnumerable?` | The rows — usually an immutable array or list of records from component state; replace it to change the rows. A user-managed observable collection is bound directly. |
| `columns` | `TableColumn[]` | The columns, in display order (see [`TextColumn`](#textcolumn), [`TemplateColumn`](#templatecolumn) and [`BoundColumn`](#boundcolumn)). |

**Remarks**

Requires the Windows App SDK experimental channel; the API may change.

## `TemplateColumn`

Creates a column whose cells are Reactor elements rendered from each row of type `T`. Rows of another type render an empty cell.

```csharp
TableTemplateColumn TemplateColumn<T>(object header, Func<T, Element?> cell, double? width = null)
```

Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.

**Returns:** `TableTemplateColumn`.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `header` | `object` |  | Header content (usually a string). |
| `cell` | `Func<T, Element?>` |  | Renders the cell for a row. |
| `width` | `double?` | `null` | Optional width in pixels. |

**Type parameters**

| Name | Description |
| --- | --- |
| `T` | The row item type. |

## `TextColumn`

Creates a text column that reads its text from each row of type `T` in C# — no binding, no `INotifyPropertyChanged`, NativeAOT-safe. It sorts by that text, and is editable when `onEdit` is set: a committed edit calls it with the row and the new text, so you can update your state immutably.

```csharp
TableTextColumn TextColumn<T>(object header, Func<T, string?> text, Action<T, string>? onEdit = null, double? width = null)
```

Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.

**Returns:** `TableTextColumn`.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `header` | `object` |  | Header content (usually a string). |
| `text` | `Func<T, string?>` |  | Reads the cell text from a row. |
| `onEdit` | `Action<T, string>?` | `null` | Commits an edit; `null` keeps the column read-only. |
| `width` | `double?` | `null` | Optional width in pixels. |

**Type parameters**

| Name | Description |
| --- | --- |
| `T` | The row type. |
