# Sort and filter descriptors

Requests, sort, filter and field descriptors that data sources receive.

**Learn more:** [Data System](../../data-system.md)

## `DataRequest`

Describes a data fetch request with paging, sorting, filtering, and search.

```csharp
public record DataRequest
```

Namespace: `Microsoft.UI.Reactor.Data`.

### Constructors

#### `DataRequest`

```csharp
public DataRequest()
```

### Properties

#### `ContinuationToken`

Token for fetching the next page. Null for the first page.

```csharp
public string? ContinuationToken  // get, init
```

#### `Filters`

Filter operations to apply (AND-ed together).

```csharp
public IReadOnlyList<FilterDescriptor>? Filters  // get, init
```

#### `PageSize`

Number of items per page. Default 50.

```csharp
public int PageSize  // get, init
```

#### `SearchQuery`

Free-text search query.

```csharp
public string? SearchQuery  // get, init
```

#### `Select`

Fields to include in the result (projection). Null = all fields.

```csharp
public IReadOnlyList<string>? Select  // get, init
```

#### `Sort`

Sort operations to apply.

```csharp
public IReadOnlyList<SortDescriptor>? Sort  // get, init
```

## `FieldDescriptor`

Describes a single field within a decomposed type. Unified descriptor used by PropertyGrid, DataGrid, and FormField.

```csharp
public record FieldDescriptor
```

Namespace: `Microsoft.UI.Reactor.Data`.

### Constructors

#### `FieldDescriptor`

```csharp
public FieldDescriptor()
```

> **Obsolete.** Constructors of types with required members are not supported in this version of your compiler.

### Properties

#### `AsyncValidators`

Asynchronous validators for this field.

```csharp
public IReadOnlyList<IAsyncValidator>? AsyncValidators  // get, init
```

#### `Category`

Category for grouping. Null = default/uncategorized.

```csharp
public string? Category  // get, init
```

#### `CellRenderer`

Custom cell renderer for grid display.

```csharp
public Func<object, Element>? CellRenderer  // get, init
```

#### `Description`

Help text shown as tooltip or description.

```csharp
public string? Description  // get, init
```

#### `DisplayName`

Display label shown in grids and form fields.

```csharp
public string? DisplayName  // get, init
```

#### `Editor`

Custom editor factory for this specific field. Takes (value, onChange) and returns an Element.

```csharp
public Func<object, Action<object>, Element>? Editor  // get, init
```

#### `FieldType`

The CLR type of this field's value.

```csharp
public Type FieldType  // get, init
```

#### `Filterable`

Whether this column can be filtered. Default true.

```csharp
public bool Filterable  // get, init
```

#### `Flex`

Flex grow factor for column sizing.

```csharp
public double? Flex  // get, init
```

#### `FormatValue`

Custom value formatter for grid display.

```csharp
public Func<object?, string>? FormatValue  // get, init
```

#### `GetValue`

Gets the current value from the owner object.

```csharp
public Func<object, object?> GetValue  // get, init
```

#### `IsReadOnly`

Whether this field is read-only in editors.

```csharp
public bool IsReadOnly  // get, init
```

#### `MaxWidth`

Maximum column width in pixels.

```csharp
public double? MaxWidth  // get, init
```

#### `MinWidth`

Minimum column width in pixels.

```csharp
public double? MinWidth  // get, init
```

#### `Name`

Field name (used as key in accessors and Compose dictionary).

```csharp
public string Name  // get, init
```

#### `Order`

Declaration order for stable sorting.

```csharp
public int Order  // get, init
```

#### `Pin`

Pin position for fixed columns.

```csharp
public PinPosition Pin  // get, init
```

#### `SetValue`

Sets the value on the owner object and returns the (possibly new) owner. For mutable properties: mutates in place and returns the same reference. For immutable properties: constructs a new object and returns it. Null for truly read-only fields (no setter, no constructor match).

```csharp
public Func<object, object?, object>? SetValue  // get, init
```

#### `Sortable`

Whether this column can be sorted. Default true.

```csharp
public bool Sortable  // get, init
```

#### `Validators`

Synchronous validators for this field.

```csharp
public IReadOnlyList<IValidator>? Validators  // get, init
```

#### `Width`

Preferred column width in pixels.

```csharp
public double? Width  // get, init
```

## `FilterDescriptor`

Describes a filter operation on a field.

```csharp
public record FilterDescriptor(string Field, FilterOperator Operator, object? Value = null, object? ValueTo = null)
```

Namespace: `Microsoft.UI.Reactor.Data`.

### Constructors

#### `FilterDescriptor`

Describes a filter operation on a field.

```csharp
public FilterDescriptor(string Field, FilterOperator Operator, object? Value = null, object? ValueTo = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Field` | `string` |  |  |
| `Operator` | [`FilterOperator`](#filteroperator) |  |  |
| `Value` | `object?` | `null` |  |
| `ValueTo` | `object?` | `null` |  |

### Properties

#### `Field`

```csharp
public string Field  // get, init
```

#### `Operator`

```csharp
public FilterOperator Operator  // get, init
```

#### `Value`

```csharp
public object? Value  // get, init
```

#### `ValueTo`

```csharp
public object? ValueTo  // get, init
```

## `FilterOperator`

Filter operators for data queries.

```csharp
public enum FilterOperator
```

Namespace: `Microsoft.UI.Reactor.Data`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Equals` | `0` | Returns whether this object equals another object. |
| `NotEquals` | `1` |  |
| `Contains` | `2` |  |
| `StartsWith` | `3` |  |
| `EndsWith` | `4` |  |
| `GreaterThan` | `5` |  |
| `GreaterThanOrEqual` | `6` |  |
| `LessThan` | `7` |  |
| `LessThanOrEqual` | `8` |  |
| `Between` | `9` |  |
| `In` | `10` |  |
| `IsNull` | `11` |  |
| `IsNotNull` | `12` |  |

## `RowKey`

A strongly-typed key for identifying rows in a data source.

```csharp
public readonly record struct RowKey(string Value)
```

Namespace: `Microsoft.UI.Reactor.Data`.

### Constructors

#### `RowKey`

A strongly-typed key for identifying rows in a data source.

```csharp
public RowKey(string Value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `string` |  |

### Properties

#### `Value`

```csharp
public string Value  // get, init
```

### Operators

#### `op_Implicit`

```csharp
public static implicit operator RowKey(Guid value)
public static implicit operator RowKey(int value)
public static implicit operator RowKey(string value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `Guid` or `int` or `string` |  |

## `SortDescriptor`

Describes a sort operation on a field.

```csharp
public record SortDescriptor(string Field, SortDirection Direction = SortDirection.Ascending)
```

Namespace: `Microsoft.UI.Reactor.Data`.

### Constructors

#### `SortDescriptor`

Describes a sort operation on a field.

```csharp
public SortDescriptor(string Field, SortDirection Direction = SortDirection.Ascending)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Field` | `string` |  |  |
| `Direction` | [`SortDirection`](#sortdirection) | `SortDirection.Ascending` |  |

### Properties

#### `Direction`

```csharp
public SortDirection Direction  // get, init
```

#### `Field`

```csharp
public string Field  // get, init
```

## `SortDirection`

Sort direction for data queries.

```csharp
public enum SortDirection
```

Namespace: `Microsoft.UI.Reactor.Data`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Ascending` | `0` |  |
| `Descending` | `1` |  |
