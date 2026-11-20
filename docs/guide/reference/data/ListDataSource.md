# ListDataSource\<T\>

In-memory data source backed by a list. Supports client-side sorting, filtering, text search, paging, and CRUD mutations.

```csharp
public class ListDataSource<T> : IDataSource<T>, IMutableDataSource<T>
```

Namespace: `Microsoft.UI.Reactor.Data.Providers`.

**Learn more:** [Data System](../../data-system.md)

## Constructors

### `ListDataSource`

For subclasses that manage their own item list.

```csharp
public ListDataSource(IEnumerable<T> items, Func<T, RowKey> getRowKey)
protected ListDataSource(List<T> items, Func<T, RowKey> getRowKey, bool directReference)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `items` | `IEnumerable<T>` or `List<T>` |  |
| `getRowKey` | `Func<T, RowKey>` |  |
| `directReference` | `bool` |  |

## Properties

### `Capabilities`

```csharp
public DataSourceCapabilities Capabilities  // get
```

### `Items`

Direct access to the backing list for subclasses.

```csharp
protected List<T> Items  // get
```

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

### `GetPageAsync`

```csharp
public Task<DataPage<T>> GetPageAsync(DataRequest request, CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `request` | [`DataRequest`](descriptors.md#datarequest) |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |

### `GetRowKey`

```csharp
public RowKey GetRowKey(T item)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `item` | `T` |  |

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
