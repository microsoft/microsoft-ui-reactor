# IKeyedDataSource\<T\>

A data source that supports fetching individual items by key.

```csharp
public interface IKeyedDataSource<T> : IDataSource<T>
```

Namespace: `Microsoft.UI.Reactor.Data`.

**Learn more:** [Data System](../../data-system.md)

## Methods

### `GetByKeyAsync`

```csharp
public Task<T?> GetByKeyAsync(RowKey key, CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `key` | [`RowKey`](descriptors.md#rowkey) |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |

### `GetByKeysAsync`

```csharp
public Task<IReadOnlyList<T>> GetByKeysAsync(IEnumerable<RowKey> keys, CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `keys` | `IEnumerable<RowKey>` |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |
