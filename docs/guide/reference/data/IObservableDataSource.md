# IObservableDataSource\<T\>

A data source that fires events when data changes.

```csharp
public interface IObservableDataSource<T> : IDataSource<T>
```

Namespace: `Microsoft.UI.Reactor.Data`.

**Learn more:** [Data System](../../data-system.md)

## Events

### `DataChanged`

```csharp
public event EventHandler? DataChanged
```
