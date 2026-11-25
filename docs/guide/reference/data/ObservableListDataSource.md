# ObservableListDataSource\<T\>

An observable data source that watches an ObservableCollection for changes and fires DataChanged when items are added, removed, or modified. The collection is the authoritative source — mutations go through the collection, not through the data source's CRUD methods.

```csharp
public class ObservableListDataSource<T> : ListDataSource<T>, IObservableDataSource<T>, IDataSource<T>, IDisposable
```

Namespace: `Microsoft.UI.Reactor.Data.Providers`. Inherits from [`ListDataSource<T>`](ListDataSource.md).

**Learn more:** [Data System](../../data-system.md)

## Constructors

### `ObservableListDataSource`

```csharp
public ObservableListDataSource(ObservableCollection<T> collection, Func<T, RowKey> getRowKey)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `collection` | `ObservableCollection<T>` |  |
| `getRowKey` | `Func<T, RowKey>` |  |

## Methods

### `Dispose`

Releases the resources that this object holds.

```csharp
public void Dispose()
```

## Events

### `DataChanged`

```csharp
public event EventHandler? DataChanged
```
