# GridSizeExtensions

Fluent extension methods for [`GridSize`](GridSize.md) to set the optional `Min` and `Max` properties.

```csharp
public static class GridSizeExtensions
```

Namespace: `Microsoft.UI.Reactor`.

**Learn more:** [Layout](../../layout.md)

## Methods

### `MaxSize`

Sets the `Max` property of a [`GridSize`](GridSize.md) instance.

```csharp
public static GridSize MaxSize(this GridSize size, double max)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `max` | `double` | The maximum size value to set. |

**Returns:** A new [`GridSize`](GridSize.md) instance with the `Max` property set.

### `MinSize`

Sets the `Min` property of a [`GridSize`](GridSize.md) instance.

```csharp
public static GridSize MinSize(this GridSize size, double min)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `min` | `double` | The minimum size value to set. |

**Returns:** A new [`GridSize`](GridSize.md) instance with the `Min` property set.
