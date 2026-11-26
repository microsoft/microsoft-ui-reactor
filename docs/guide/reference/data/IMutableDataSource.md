# IMutableDataSource\<T\>

A mutable data source that supports CRUD operations.

```csharp
public interface IMutableDataSource<T> : IDataSource<T>
```

Namespace: `Microsoft.UI.Reactor.Data`.

**Learn more:** [Data System](../../data-system.md)

## Methods

### `CreateAsync`

```csharp
public Task<T> CreateAsync(T item, CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `item` | `T` |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |

### `DeleteAsync`

```csharp
public Task DeleteAsync(RowKey key, CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `key` | [`RowKey`](descriptors.md#rowkey) |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |

### `UpdateAsync`

```csharp
public Task<T> UpdateAsync(RowKey key, T item, CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `key` | [`RowKey`](descriptors.md#rowkey) |  |  |
| `item` | `T` |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |
