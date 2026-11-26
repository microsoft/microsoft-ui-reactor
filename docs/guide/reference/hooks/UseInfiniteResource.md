# UseInfiniteResource

Returns the [`InfiniteResource<TItem>`](../data/paging.md#infiniteresourcetitem) owned by this hook slot. The resource's state is driven by `fetchPage`; `deps` controls cache-keying and deps-change restart.

```csharp
public static InfiniteResource<TItem> UseInfiniteResource<TItem, TCursor>(this RenderContext ctx, Func<TCursor?, CancellationToken, Task<Page<TItem, TCursor>>> fetchPage, object[] deps, InfiniteResourceOptions? options = null, IHookDispatcher? dispatcher = null, Func<int, TCursor?>? cursorFromPageIndex = null)
public static InfiniteResource<TItem> UseInfiniteResource<TItem, TCursor>(this RenderContext ctx, Func<TCursor?, CancellationToken, Task<Page<TItem, TCursor>>> fetchPage, QueryCache cache, object[] deps, InfiniteResourceOptions? options = null, IHookDispatcher? dispatcher = null, Func<int, TCursor?>? cursorFromPageIndex = null)
protected InfiniteResource<TItem> UseInfiniteResource<TItem, TCursor>(Func<TCursor?, CancellationToken, Task<Page<TItem, TCursor>>> fetchPage, object[] deps, InfiniteResourceOptions? options = null)
```

An extension method on [`RenderContext`](../components/RenderContext.md), declared in `UseInfiniteResourceExtensions`.

**Learn more:** [Async Resources](../../async-resources.md), [Hooks](../../hooks.md)

## Overloads

| Overload | Description |
| --- | --- |
| [`UseInfiniteResource<TItem, TCursor>(RenderContext, Func<TCursor?, CancellationToken, Task<Page<TItem, TCursor>>>, object[], InfiniteResourceOptions?, IHookDispatcher?, Func<int, TCursor?>?)`](#useinfiniteresourcetitem-tcursorrendercontext-functcursor-cancellationtoken-taskpagetitem-tcursor-object-infiniteresourceoptions-ihookdispatcher-funcint-tcursor) | Overload that reads the ambient `QueryCache` from `AppContexts.QueryCache`. |
| [`UseInfiniteResource<TItem, TCursor>(RenderContext, Func<TCursor?, CancellationToken, Task<Page<TItem, TCursor>>>, QueryCache, object[], InfiniteResourceOptions?, IHookDispatcher?, Func<int, TCursor?>?)`](#useinfiniteresourcetitem-tcursorrendercontext-functcursor-cancellationtoken-taskpagetitem-tcursor-querycache-object-infiniteresourceoptions-ihookdispatcher-funcint-tcursor) | Returns the `InfiniteResource<TItem>` owned by this hook slot. |
| [`UseInfiniteResource<TItem, TCursor>(Func<TCursor?, CancellationToken, Task<Page<TItem, TCursor>>>, object[], InfiniteResourceOptions?)`](#useinfiniteresourcetitem-tcursorfunctcursor-cancellationtoken-taskpagetitem-tcursor-object-infiniteresourceoptions) |  |

### `UseInfiniteResource<TItem, TCursor>(RenderContext, Func<TCursor?, CancellationToken, Task<Page<TItem, TCursor>>>, object[], InfiniteResourceOptions?, IHookDispatcher?, Func<int, TCursor?>?)`

Overload that reads the ambient [`QueryCache`](../data/query-cache.md#querycache) from [`AppContexts.QueryCache`](../components/AppContexts.md#querycache). [`ReactorHost`](../hosting/ReactorHost.md) installs a process-wide default cache at startup; tests or subtrees may override it via `.Provide(AppContexts.QueryCache, customCache)`.

```csharp
public static InfiniteResource<TItem> UseInfiniteResource<TItem, TCursor>(this RenderContext ctx, Func<TCursor?, CancellationToken, Task<Page<TItem, TCursor>>> fetchPage, object[] deps, InfiniteResourceOptions? options = null, IHookDispatcher? dispatcher = null, Func<int, TCursor?>? cursorFromPageIndex = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `fetchPage` | `Func<TCursor?, CancellationToken, Task<Page<TItem, TCursor>>>` |  |  |
| `deps` | `object[]` |  |  |
| `options` | [`InfiniteResourceOptions?`](../data/paging.md#infiniteresourceoptions) | `null` |  |
| `dispatcher` | [`IHookDispatcher?`](hook-types.md#ihookdispatcher) | `null` |  |
| `cursorFromPageIndex` | `Func<int, TCursor?>?` | `null` |  |

### `UseInfiniteResource<TItem, TCursor>(RenderContext, Func<TCursor?, CancellationToken, Task<Page<TItem, TCursor>>>, QueryCache, object[], InfiniteResourceOptions?, IHookDispatcher?, Func<int, TCursor?>?)`

Returns the [`InfiniteResource<TItem>`](../data/paging.md#infiniteresourcetitem) owned by this hook slot. The resource's state is driven by `fetchPage`; `deps` controls cache-keying and deps-change restart.

```csharp
public static InfiniteResource<TItem> UseInfiniteResource<TItem, TCursor>(this RenderContext ctx, Func<TCursor?, CancellationToken, Task<Page<TItem, TCursor>>> fetchPage, QueryCache cache, object[] deps, InfiniteResourceOptions? options = null, IHookDispatcher? dispatcher = null, Func<int, TCursor?>? cursorFromPageIndex = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `fetchPage` | `Func<TCursor?, CancellationToken, Task<Page<TItem, TCursor>>>` |  |  |
| `cache` | [`QueryCache`](../data/query-cache.md#querycache) |  |  |
| `deps` | `object[]` |  |  |
| `options` | [`InfiniteResourceOptions?`](../data/paging.md#infiniteresourceoptions) | `null` |  |
| `dispatcher` | [`IHookDispatcher?`](hook-types.md#ihookdispatcher) | `null` |  |
| `cursorFromPageIndex` | `Func<int, TCursor?>?` | `null` |  |

### `UseInfiniteResource<TItem, TCursor>(Func<TCursor?, CancellationToken, Task<Page<TItem, TCursor>>>, object[], InfiniteResourceOptions?)`

```csharp
protected InfiniteResource<TItem> UseInfiniteResource<TItem, TCursor>(Func<TCursor?, CancellationToken, Task<Page<TItem, TCursor>>> fetchPage, object[] deps, InfiniteResourceOptions? options = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `fetchPage` | `Func<TCursor?, CancellationToken, Task<Page<TItem, TCursor>>>` |  |  |
| `deps` | `object[]` |  |  |
| `options` | [`InfiniteResourceOptions?`](../data/paging.md#infiniteresourceoptions) | `null` |  |
