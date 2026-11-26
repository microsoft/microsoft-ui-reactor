# IDataSource\<T\>

A read-only data source that provides paged access to items.

```csharp
public interface IDataSource<T>
```

Namespace: `Microsoft.UI.Reactor.Data`.

**Learn more:** [Data System](../../data-system.md)

## Type parameters

| Name | Description |
| --- | --- |
| `T` | The item type. |

## Properties

### `Capabilities`

Declares what this data source can do server-side.

```csharp
public abstract DataSourceCapabilities Capabilities  // get
```

## Methods

### `GetPageAsync`

Fetches a page of data matching the request.

```csharp
public Task<DataPage<T>> GetPageAsync(DataRequest request, CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `request` | [`DataRequest`](descriptors.md#datarequest) |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |

### `GetRowKey`

Gets the stable key for an item.

```csharp
public RowKey GetRowKey(T item)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `item` | `T` |  |
