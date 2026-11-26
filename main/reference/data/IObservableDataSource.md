---
title: "IObservableDataSource<T>"
description: "A data source that fires events when data changes."
sidebar:
  label: "IObservableDataSource"
---

```csharp
public interface IObservableDataSource<T> : IDataSource<T>
```

Namespace: `Microsoft.UI.Reactor.Data`.

**Learn more:** [Data System](/microsoft-ui-reactor/main/controls/data-system)

## Events [#events]

### `DataChanged` [#datachanged]

```csharp
public event EventHandler? DataChanged
```
