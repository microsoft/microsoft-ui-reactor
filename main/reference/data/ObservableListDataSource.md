---
title: "ObservableListDataSource<T>"
description: "An observable data source that watches an ObservableCollection for changes and fires DataChanged when items are added, removed, or modified. The collection is the authoritative source — mutations go through the collection, not through the data source's CRUD methods."
sidebar:
  label: "ObservableListDataSource"
---

```csharp
public class ObservableListDataSource<T> : ListDataSource<T>, IObservableDataSource<T>, IDataSource<T>, IDisposable
```

Namespace: `Microsoft.UI.Reactor.Data.Providers`. Inherits from [`ListDataSource<T>`](/microsoft-ui-reactor/main/reference/data/ListDataSource).

**Learn more:** [Data System](/microsoft-ui-reactor/main/controls/data-system)

## Constructors [#constructors]

### `ObservableListDataSource` [#observablelistdatasource]

```csharp
public ObservableListDataSource(ObservableCollection<T> collection, Func<T, RowKey> getRowKey)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `collection` | `ObservableCollection<T>` |  |
| `getRowKey` | `Func<T, RowKey>` |  |

## Methods [#methods]

### `Dispose` [#dispose]

Releases the resources that this object holds.

```csharp
public void Dispose()
```

## Events [#events]

### `DataChanged` [#datachanged]

```csharp
public event EventHandler? DataChanged
```
