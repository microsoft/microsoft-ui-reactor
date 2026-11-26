---
title: "UseDataSource"
seo:
  description: "Projects an IDataSource<T> through UseInfiniteResource, surfacing its pages as an InfiniteResource<TItem>. Deps are identity-based on source and value-based on the request descriptors, so a new sort/filter/search restarts the pagination cleanly."
---

Projects an [`IDataSource<T>`](/microsoft-ui-reactor/main/reference/data/IDataSource) through `UseInfiniteResource`, surfacing its pages as an [`InfiniteResource<TItem>`](/microsoft-ui-reactor/main/reference/data/paging#infiniteresourcetitem). Deps are identity-based on `source` and value-based on the request descriptors, so a new sort/filter/search restarts the pagination cleanly.

```csharp
public static InfiniteResource<T> UseDataSource<T>(this RenderContext ctx, IDataSource<T> source, DataRequest request, InfiniteResourceOptions? options = null, IHookDispatcher? dispatcher = null)
public static InfiniteResource<T> UseDataSource<T>(this RenderContext ctx, IDataSource<T> source, DataRequest request, QueryCache cache, InfiniteResourceOptions? options = null, IHookDispatcher? dispatcher = null)
protected InfiniteResource<TItem> UseDataSource<TItem>(IDataSource<TItem> source, DataRequest request, InfiniteResourceOptions? options = null)
```

An extension method on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext), declared in `DataSourceResourceExtensions`.

**Learn more:** [Data System](/microsoft-ui-reactor/main/controls/data-system), [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## Overloads [#overloads]

| Overload | Description |
| --- | --- |
| [`UseDataSource<T>(RenderContext, IDataSource<T>, DataRequest, InfiniteResourceOptions?, IHookDispatcher?)`](#usedatasourcetrendercontext-idatasourcet-datarequest-infiniteresourceoptions-ihookdispatcher) | Overload that reads the ambient `QueryCache` from `AppContexts.QueryCache`. |
| [`UseDataSource<T>(RenderContext, IDataSource<T>, DataRequest, QueryCache, InfiniteResourceOptions?, IHookDispatcher?)`](#usedatasourcetrendercontext-idatasourcet-datarequest-querycache-infiniteresourceoptions-ihookdispatcher) | Projects an `IDataSource<T>` through `UseInfiniteResource`, surfacing its pages as an `InfiniteResource<TItem>`. |
| [`UseDataSource<TItem>(IDataSource<TItem>, DataRequest, InfiniteResourceOptions?)`](#usedatasourcetitemidatasourcetitem-datarequest-infiniteresourceoptions) | Projects a data source into a paged resource using the ambient query cache. |

### `UseDataSource<T>(RenderContext, IDataSource<T>, DataRequest, InfiniteResourceOptions?, IHookDispatcher?)` [#usedatasourcetrendercontext-idatasourcet-datarequest-infiniteresourceoptions-ihookdispatcher]

Overload that reads the ambient [`QueryCache`](/microsoft-ui-reactor/main/reference/data/query-cache#querycache) from [`AppContexts.QueryCache`](/microsoft-ui-reactor/main/reference/components/AppContexts#querycache). [`ReactorHost`](/microsoft-ui-reactor/main/reference/hosting/ReactorHost) installs a process-wide default cache at startup; tests or subtrees may override it via `.Provide(AppContexts.QueryCache, customCache)`.

```csharp
public static InfiniteResource<T> UseDataSource<T>(this RenderContext ctx, IDataSource<T> source, DataRequest request, InfiniteResourceOptions? options = null, IHookDispatcher? dispatcher = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `source` | [`IDataSource<T>`](/microsoft-ui-reactor/main/reference/data/IDataSource) |  |  |
| `request` | [`DataRequest`](/microsoft-ui-reactor/main/reference/data/descriptors#datarequest) |  |  |
| `options` | [`InfiniteResourceOptions?`](/microsoft-ui-reactor/main/reference/data/paging#infiniteresourceoptions) | `null` |  |
| `dispatcher` | [`IHookDispatcher?`](/microsoft-ui-reactor/main/reference/hooks/hook-types#ihookdispatcher) | `null` |  |

**Remarks**

The initial page size comes from `options.PageSize` (default 50) when `options` is supplied, or `request.PageSize` otherwise, and is clamped to at least 1. This size stays fixed across renders and query restarts. To change it, have the parent remount the calling component with a different key, e.g. `.WithKey($"tasks:{pageSize}")`, or increment a parent-owned reset version used by `.WithKey($"tasks:{resetVersion}")`.

### `UseDataSource<T>(RenderContext, IDataSource<T>, DataRequest, QueryCache, InfiniteResourceOptions?, IHookDispatcher?)` [#usedatasourcetrendercontext-idatasourcet-datarequest-querycache-infiniteresourceoptions-ihookdispatcher]

Projects an [`IDataSource<T>`](/microsoft-ui-reactor/main/reference/data/IDataSource) through `UseInfiniteResource`, surfacing its pages as an [`InfiniteResource<TItem>`](/microsoft-ui-reactor/main/reference/data/paging#infiniteresourcetitem). Deps are identity-based on `source` and value-based on the request descriptors, so a new sort/filter/search restarts the pagination cleanly.

```csharp
public static InfiniteResource<T> UseDataSource<T>(this RenderContext ctx, IDataSource<T> source, DataRequest request, QueryCache cache, InfiniteResourceOptions? options = null, IHookDispatcher? dispatcher = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `source` | [`IDataSource<T>`](/microsoft-ui-reactor/main/reference/data/IDataSource) |  |  |
| `request` | [`DataRequest`](/microsoft-ui-reactor/main/reference/data/descriptors#datarequest) |  |  |
| `cache` | [`QueryCache`](/microsoft-ui-reactor/main/reference/data/query-cache#querycache) |  |  |
| `options` | [`InfiniteResourceOptions?`](/microsoft-ui-reactor/main/reference/data/paging#infiniteresourceoptions) | `null` |  |
| `dispatcher` | [`IHookDispatcher?`](/microsoft-ui-reactor/main/reference/hooks/hook-types#ihookdispatcher) | `null` |  |

**Remarks**

The initial page size comes from `options.PageSize` (default 50) when `options` is supplied, or `request.PageSize` otherwise, and is clamped to at least 1. This size stays fixed across renders and query restarts. To change it, have the parent remount the calling component with a different key, e.g. `.WithKey($"tasks:{pageSize}")`, or increment a parent-owned reset version used by `.WithKey($"tasks:{resetVersion}")`.

### `UseDataSource<TItem>(IDataSource<TItem>, DataRequest, InfiniteResourceOptions?)` [#usedatasourcetitemidatasourcetitem-datarequest-infiniteresourceoptions]

Projects a data source into a paged resource using the ambient query cache.

```csharp
protected InfiniteResource<TItem> UseDataSource<TItem>(IDataSource<TItem> source, DataRequest request, InfiniteResourceOptions? options = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `source` | [`IDataSource<TItem>`](/microsoft-ui-reactor/main/reference/data/IDataSource) |  |  |
| `request` | [`DataRequest`](/microsoft-ui-reactor/main/reference/data/descriptors#datarequest) |  |  |
| `options` | [`InfiniteResourceOptions?`](/microsoft-ui-reactor/main/reference/data/paging#infiniteresourceoptions) | `null` |  |

**Remarks**

The initial page size comes from `options.PageSize` (default 50) when `options` is supplied, or `request.PageSize` otherwise, and is clamped to at least 1. This size stays fixed across renders and query restarts. To change it, have the parent remount the calling component with a different key, e.g. `.WithKey($"tasks:{pageSize}")`, or increment a parent-owned reset version used by `.WithKey($"tasks:{resetVersion}")`.
