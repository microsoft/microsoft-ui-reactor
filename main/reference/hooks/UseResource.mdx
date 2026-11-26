---
title: "UseResource"
seo:
  description: "Runs an async fetch keyed on deps, returning an AsyncValue<T> that tracks the fetch's lifecycle. The hook owns the cancellation token, stores results in cache, and re-renders when new results land."
---

Runs an async fetch keyed on `deps`, returning an [`AsyncValue<T>`](/microsoft-ui-reactor/main/reference/data/async-values#asyncvaluet) that tracks the fetch's lifecycle. The hook owns the cancellation token, stores results in `cache`, and re-renders when new results land.

```csharp
public static AsyncValue<T> UseResource<T>(this RenderContext ctx, Func<CancellationToken, Task<T>> fetcher, object[] deps, ResourceOptions? options = null, IHookDispatcher? dispatcher = null)
public static AsyncValue<T> UseResource<T>(this RenderContext ctx, Func<CancellationToken, Task<T>> fetcher, QueryCache cache, object[] deps, ResourceOptions? options = null, IHookDispatcher? dispatcher = null)
protected AsyncValue<T> UseResource<T>(Func<CancellationToken, Task<T>> fetcher, object[] deps, ResourceOptions? options = null)
protected AsyncValue<T> UseResource<T>(Func<CancellationToken, Task<T>> fetcher, QueryCache cache, object[] deps, ResourceOptions? options = null)
```

An extension method on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext), declared in `UseResourceExtensions`.

**Learn more:** [Async Resources](/microsoft-ui-reactor/main/guides/async-resources), [Effects and Lifecycle](/microsoft-ui-reactor/main/guides/effects), [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## Overloads [#overloads]

| Overload | Description |
| --- | --- |
| [`UseResource<T>(RenderContext, Func<CancellationToken, Task<T>>, object[], ResourceOptions?, IHookDispatcher?)`](#useresourcetrendercontext-funccancellationtoken-taskt-object-resourceoptions-ihookdispatcher) | Overload that reads the ambient `QueryCache` from `AppContexts.QueryCache`. |
| [`UseResource<T>(RenderContext, Func<CancellationToken, Task<T>>, QueryCache, object[], ResourceOptions?, IHookDispatcher?)`](#useresourcetrendercontext-funccancellationtoken-taskt-querycache-object-resourceoptions-ihookdispatcher) | Runs an async fetch keyed on `deps`, returning an `AsyncValue<T>` that tracks the fetch's lifecycle. |
| [`UseResource<T>(Func<CancellationToken, Task<T>>, object[], ResourceOptions?)`](#useresourcetfunccancellationtoken-taskt-object-resourceoptions) |  |
| [`UseResource<T>(Func<CancellationToken, Task<T>>, QueryCache, object[], ResourceOptions?)`](#useresourcetfunccancellationtoken-taskt-querycache-object-resourceoptions) |  |

### `UseResource<T>(RenderContext, Func<CancellationToken, Task<T>>, object[], ResourceOptions?, IHookDispatcher?)` [#useresourcetrendercontext-funccancellationtoken-taskt-object-resourceoptions-ihookdispatcher]

Overload that reads the ambient [`QueryCache`](/microsoft-ui-reactor/main/reference/data/query-cache#querycache) from [`AppContexts.QueryCache`](/microsoft-ui-reactor/main/reference/components/AppContexts#querycache). [`ReactorHost`](/microsoft-ui-reactor/main/reference/hosting/ReactorHost) installs a process-wide default cache at startup; tests or subtrees may override it via `.Provide(AppContexts.QueryCache, customCache)`.

```csharp
public static AsyncValue<T> UseResource<T>(this RenderContext ctx, Func<CancellationToken, Task<T>> fetcher, object[] deps, ResourceOptions? options = null, IHookDispatcher? dispatcher = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `fetcher` | `Func<CancellationToken, Task<T>>` |  |  |
| `deps` | `object[]` |  |  |
| `options` | [`ResourceOptions?`](/microsoft-ui-reactor/main/reference/hooks/hook-types#resourceoptions) | `null` |  |
| `dispatcher` | [`IHookDispatcher?`](/microsoft-ui-reactor/main/reference/hooks/hook-types#ihookdispatcher) | `null` |  |

### `UseResource<T>(RenderContext, Func<CancellationToken, Task<T>>, QueryCache, object[], ResourceOptions?, IHookDispatcher?)` [#useresourcetrendercontext-funccancellationtoken-taskt-querycache-object-resourceoptions-ihookdispatcher]

Runs an async fetch keyed on `deps`, returning an [`AsyncValue<T>`](/microsoft-ui-reactor/main/reference/data/async-values#asyncvaluet) that tracks the fetch's lifecycle. The hook owns the cancellation token, stores results in `cache`, and re-renders when new results land.

```csharp
public static AsyncValue<T> UseResource<T>(this RenderContext ctx, Func<CancellationToken, Task<T>> fetcher, QueryCache cache, object[] deps, ResourceOptions? options = null, IHookDispatcher? dispatcher = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `fetcher` | `Func<CancellationToken, Task<T>>` |  |  |
| `cache` | [`QueryCache`](/microsoft-ui-reactor/main/reference/data/query-cache#querycache) |  |  |
| `deps` | `object[]` |  |  |
| `options` | [`ResourceOptions?`](/microsoft-ui-reactor/main/reference/hooks/hook-types#resourceoptions) | `null` |  |
| `dispatcher` | [`IHookDispatcher?`](/microsoft-ui-reactor/main/reference/hooks/hook-types#ihookdispatcher) | `null` |  |

**Remarks**

**Sync-complete fast path.** If `fetcher` returns an already-completed task, this call returns `Data(result)` on the same render, with no transient `Loading` flash.

**Dispatcher.** The hook captures the dispatcher at registration time (`DispatcherQueue.GetForCurrentThread()`). In unit tests without a WinUI dispatcher, continuations run inline on the thread-pool thread that completed the fetch.

### `UseResource<T>(Func<CancellationToken, Task<T>>, object[], ResourceOptions?)` [#useresourcetfunccancellationtoken-taskt-object-resourceoptions]

```csharp
protected AsyncValue<T> UseResource<T>(Func<CancellationToken, Task<T>> fetcher, object[] deps, ResourceOptions? options = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `fetcher` | `Func<CancellationToken, Task<T>>` |  |  |
| `deps` | `object[]` |  |  |
| `options` | [`ResourceOptions?`](/microsoft-ui-reactor/main/reference/hooks/hook-types#resourceoptions) | `null` |  |

### `UseResource<T>(Func<CancellationToken, Task<T>>, QueryCache, object[], ResourceOptions?)` [#useresourcetfunccancellationtoken-taskt-querycache-object-resourceoptions]

```csharp
protected AsyncValue<T> UseResource<T>(Func<CancellationToken, Task<T>> fetcher, QueryCache cache, object[] deps, ResourceOptions? options = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `fetcher` | `Func<CancellationToken, Task<T>>` |  |  |
| `cache` | [`QueryCache`](/microsoft-ui-reactor/main/reference/data/query-cache#querycache) |  |  |
| `deps` | `object[]` |  |  |
| `options` | [`ResourceOptions?`](/microsoft-ui-reactor/main/reference/hooks/hook-types#resourceoptions) | `null` |  |
