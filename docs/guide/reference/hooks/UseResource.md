# UseResource

Runs an async fetch keyed on `deps`, returning an [`AsyncValue<T>`](../data/async-values.md#asyncvaluet) that tracks the fetch's lifecycle. The hook owns the cancellation token, stores results in `cache`, and re-renders when new results land.

```csharp
public static AsyncValue<T> UseResource<T>(this RenderContext ctx, Func<CancellationToken, Task<T>> fetcher, object[] deps, ResourceOptions? options = null, IHookDispatcher? dispatcher = null)
public static AsyncValue<T> UseResource<T>(this RenderContext ctx, Func<CancellationToken, Task<T>> fetcher, QueryCache cache, object[] deps, ResourceOptions? options = null, IHookDispatcher? dispatcher = null)
protected AsyncValue<T> UseResource<T>(Func<CancellationToken, Task<T>> fetcher, object[] deps, ResourceOptions? options = null)
protected AsyncValue<T> UseResource<T>(Func<CancellationToken, Task<T>> fetcher, QueryCache cache, object[] deps, ResourceOptions? options = null)
```

An extension method on [`RenderContext`](../components/RenderContext.md), declared in `UseResourceExtensions`.

**Learn more:** [Async Resources](../../async-resources.md), [Effects and Lifecycle](../../effects.md), [Hooks](../../hooks.md)

## Overloads

| Overload | Description |
| --- | --- |
| [`UseResource<T>(RenderContext, Func<CancellationToken, Task<T>>, object[], ResourceOptions?, IHookDispatcher?)`](#useresourcetrendercontext-funccancellationtoken-taskt-object-resourceoptions-ihookdispatcher) | Overload that reads the ambient `QueryCache` from `AppContexts.QueryCache`. |
| [`UseResource<T>(RenderContext, Func<CancellationToken, Task<T>>, QueryCache, object[], ResourceOptions?, IHookDispatcher?)`](#useresourcetrendercontext-funccancellationtoken-taskt-querycache-object-resourceoptions-ihookdispatcher) | Runs an async fetch keyed on `deps`, returning an `AsyncValue<T>` that tracks the fetch's lifecycle. |
| [`UseResource<T>(Func<CancellationToken, Task<T>>, object[], ResourceOptions?)`](#useresourcetfunccancellationtoken-taskt-object-resourceoptions) |  |
| [`UseResource<T>(Func<CancellationToken, Task<T>>, QueryCache, object[], ResourceOptions?)`](#useresourcetfunccancellationtoken-taskt-querycache-object-resourceoptions) |  |

### `UseResource<T>(RenderContext, Func<CancellationToken, Task<T>>, object[], ResourceOptions?, IHookDispatcher?)`

Overload that reads the ambient [`QueryCache`](../data/query-cache.md#querycache) from [`AppContexts.QueryCache`](../components/AppContexts.md#querycache). [`ReactorHost`](../hosting/ReactorHost.md) installs a process-wide default cache at startup; tests or subtrees may override it via `.Provide(AppContexts.QueryCache, customCache)`.

```csharp
public static AsyncValue<T> UseResource<T>(this RenderContext ctx, Func<CancellationToken, Task<T>> fetcher, object[] deps, ResourceOptions? options = null, IHookDispatcher? dispatcher = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `fetcher` | `Func<CancellationToken, Task<T>>` |  |  |
| `deps` | `object[]` |  |  |
| `options` | [`ResourceOptions?`](hook-types.md#resourceoptions) | `null` |  |
| `dispatcher` | [`IHookDispatcher?`](hook-types.md#ihookdispatcher) | `null` |  |

### `UseResource<T>(RenderContext, Func<CancellationToken, Task<T>>, QueryCache, object[], ResourceOptions?, IHookDispatcher?)`

Runs an async fetch keyed on `deps`, returning an [`AsyncValue<T>`](../data/async-values.md#asyncvaluet) that tracks the fetch's lifecycle. The hook owns the cancellation token, stores results in `cache`, and re-renders when new results land.

```csharp
public static AsyncValue<T> UseResource<T>(this RenderContext ctx, Func<CancellationToken, Task<T>> fetcher, QueryCache cache, object[] deps, ResourceOptions? options = null, IHookDispatcher? dispatcher = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `fetcher` | `Func<CancellationToken, Task<T>>` |  |  |
| `cache` | [`QueryCache`](../data/query-cache.md#querycache) |  |  |
| `deps` | `object[]` |  |  |
| `options` | [`ResourceOptions?`](hook-types.md#resourceoptions) | `null` |  |
| `dispatcher` | [`IHookDispatcher?`](hook-types.md#ihookdispatcher) | `null` |  |

**Remarks**

**Sync-complete fast path.** If `fetcher` returns an already-completed task, this call returns `Data(result)` on the same render, with no transient `Loading` flash.

**Dispatcher.** The hook captures the dispatcher at registration time (`DispatcherQueue.GetForCurrentThread()`). In unit tests without a WinUI dispatcher, continuations run inline on the thread-pool thread that completed the fetch.

### `UseResource<T>(Func<CancellationToken, Task<T>>, object[], ResourceOptions?)`

```csharp
protected AsyncValue<T> UseResource<T>(Func<CancellationToken, Task<T>> fetcher, object[] deps, ResourceOptions? options = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `fetcher` | `Func<CancellationToken, Task<T>>` |  |  |
| `deps` | `object[]` |  |  |
| `options` | [`ResourceOptions?`](hook-types.md#resourceoptions) | `null` |  |

### `UseResource<T>(Func<CancellationToken, Task<T>>, QueryCache, object[], ResourceOptions?)`

```csharp
protected AsyncValue<T> UseResource<T>(Func<CancellationToken, Task<T>> fetcher, QueryCache cache, object[] deps, ResourceOptions? options = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `fetcher` | `Func<CancellationToken, Task<T>>` |  |  |
| `cache` | [`QueryCache`](../data/query-cache.md#querycache) |  |  |
| `deps` | `object[]` |  |  |
| `options` | [`ResourceOptions?`](hook-types.md#resourceoptions) | `null` |  |
