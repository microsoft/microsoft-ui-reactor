---
title: "IDataSource<T>"
description: "A read-only data source that provides paged access to items."
sidebar:
  label: "IDataSource"
---

```csharp
public interface IDataSource<T>
```

Namespace: `Microsoft.UI.Reactor.Data`.

**Learn more:** [Data System](/microsoft-ui-reactor/main/controls/data-system)

## Type parameters [#type-parameters]

| Name | Description |
| --- | --- |
| `T` | The item type. |

## Properties [#properties]

### `Capabilities` [#capabilities]

Declares what this data source can do server-side.

```csharp
public abstract DataSourceCapabilities Capabilities  // get
```

## Methods [#methods]

### `GetPageAsync` [#getpageasync]

Fetches a page of data matching the request.

```csharp
public Task<DataPage<T>> GetPageAsync(DataRequest request, CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `request` | [`DataRequest`](/microsoft-ui-reactor/main/reference/data/descriptors#datarequest) |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |

### `GetRowKey` [#getrowkey]

Gets the stable key for an item.

```csharp
public RowKey GetRowKey(T item)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `item` | `T` |  |
