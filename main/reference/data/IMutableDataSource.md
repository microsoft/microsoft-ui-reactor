---
title: "IMutableDataSource<T>"
description: "A mutable data source that supports CRUD operations."
sidebar:
  label: "IMutableDataSource"
---

```csharp
public interface IMutableDataSource<T> : IDataSource<T>
```

Namespace: `Microsoft.UI.Reactor.Data`.

**Learn more:** [Data System](/microsoft-ui-reactor/main/controls/data-system)

## Methods [#methods]

### `CreateAsync` [#createasync]

```csharp
public Task<T> CreateAsync(T item, CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `item` | `T` |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |

### `DeleteAsync` [#deleteasync]

```csharp
public Task DeleteAsync(RowKey key, CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `key` | [`RowKey`](/microsoft-ui-reactor/main/reference/data/descriptors#rowkey) |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |

### `UpdateAsync` [#updateasync]

```csharp
public Task<T> UpdateAsync(RowKey key, T item, CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `key` | [`RowKey`](/microsoft-ui-reactor/main/reference/data/descriptors#rowkey) |  |  |
| `item` | `T` |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |
