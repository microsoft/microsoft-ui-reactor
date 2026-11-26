# Column attributes

Attributes that change how a property shows as a column.

**Learn more:** [Data System](../../data-system.md)

## `ColumnPinAttribute`

Pins a column to the left or right edge of a DataGrid.

```csharp
public class ColumnPinAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Data`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

### Constructors

#### `ColumnPinAttribute`

Pins a column to the left or right edge of a DataGrid.

```csharp
public ColumnPinAttribute(PinPosition position)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `position` | [`PinPosition`](#pinposition) |  |

### Properties

#### `Position`

```csharp
public PinPosition Position  // get
```

## `ColumnWidthAttribute`

Specifies the preferred column width for a property when displayed in a DataGrid.

```csharp
public class ColumnWidthAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Data`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

### Constructors

#### `ColumnWidthAttribute`

Specifies the preferred column width for a property when displayed in a DataGrid.

```csharp
public ColumnWidthAttribute(double width)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `width` | `double` |  |

### Properties

#### `MaxWidth`

```csharp
public double MaxWidth  // get, set
```

#### `MinWidth`

```csharp
public double MinWidth  // get, set
```

#### `Width`

```csharp
public double Width  // get
```

## `NotFilterableAttribute`

Marks a column as not filterable in a DataGrid.

```csharp
public class NotFilterableAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Data`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

### Constructors

#### `NotFilterableAttribute`

```csharp
public NotFilterableAttribute()
```

## `NotSortableAttribute`

Marks a column as not sortable in a DataGrid.

```csharp
public class NotSortableAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Data`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

### Constructors

#### `NotSortableAttribute`

```csharp
public NotSortableAttribute()
```

## `PinPosition`

Controls column pinning in data grids.

```csharp
public enum PinPosition
```

Namespace: `Microsoft.UI.Reactor.Data`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `None` | `0` |  |
| `Left` | `1` |  |
| `Right` | `2` |  |
