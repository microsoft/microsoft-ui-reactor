# Paging

Types for infinite and paged resources.

**Learn more:** [Async Resources](../../async-resources.md), [Data System](../../data-system.md)

## `BlockStatus`

Status of a cached block.

```csharp
public enum BlockStatus
```

Namespace: `Microsoft.UI.Reactor.Data`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Loading` | `0` | Block is currently being loaded from the data source. |
| `Loaded` | `1` | Block data is available. |
| `Failed` | `2` | Block load failed. |

## `CacheBlock<T>`

A cached block of data from a paged data source.

```csharp
public record CacheBlock<T>(int BlockIndex, IReadOnlyList<T> Items, BlockStatus Status, string? Error = null)
```

Namespace: `Microsoft.UI.Reactor.Data`.

### Constructors

#### `CacheBlock`

A cached block of data from a paged data source.

```csharp
public CacheBlock(int BlockIndex, IReadOnlyList<T> Items, BlockStatus Status, string? Error = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `BlockIndex` | `int` |  |  |
| `Items` | `IReadOnlyList<T>` |  |  |
| `Status` | [`BlockStatus`](#blockstatus) |  |  |
| `Error` | `string?` | `null` |  |

### Properties

#### `BlockIndex`

```csharp
public int BlockIndex  // get, init
```

#### `Error`

```csharp
public string? Error  // get, init
```

#### `Items`

```csharp
public IReadOnlyList<T> Items  // get, init
```

#### `Status`

```csharp
public BlockStatus Status  // get, init
```

### Methods

#### `LoadingBlock`

Empty loading placeholder block.

```csharp
public static CacheBlock<T> LoadingBlock(int index)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `index` | `int` |  |

## `DataPage<T>`

A page of data from a data source.

```csharp
public record DataPage<T>(IReadOnlyList<T> Items, string? ContinuationToken = null, int? TotalCount = null)
```

Namespace: `Microsoft.UI.Reactor.Data`.

### Type parameters

| Name | Description |
| --- | --- |
| `T` | The item type. |

### Constructors

#### `DataPage`

A page of data from a data source.

```csharp
public DataPage(IReadOnlyList<T> Items, string? ContinuationToken = null, int? TotalCount = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Items` | `IReadOnlyList<T>` |  | The items in this page. |
| `ContinuationToken` | `string?` | `null` | Token for fetching the next page. Null means this is the last page. |
| `TotalCount` | `int?` | `null` | Total number of items matching the current filters (if the source supports counting). Null if the source doesn't support server-side counting. |

**Type parameters**

| Name | Description |
| --- | --- |
| `T` | The item type. |

### Properties

#### `ContinuationToken`

Token for fetching the next page. Null means this is the last page.

```csharp
public string? ContinuationToken  // get, init
```

#### `Items`

The items in this page.

```csharp
public IReadOnlyList<T> Items  // get, init
```

#### `TotalCount`

Total number of items matching the current filters (if the source supports counting). Null if the source doesn't support server-side counting.

```csharp
public int? TotalCount  // get, init
```

## `DataPageCache<T>`

Caches pages of data from IDataSource, keyed by sort/filter state + page index. Evicts blocks outside the viewport via LRU when max capacity is reached.

Follows a pull model (matching Compose Paging 3): accessing a row index that falls in an unloaded block triggers the fetch. The virtualizer tells the cache which blocks are needed; the cache returns immediately for loaded blocks and initiates async loads for missing ones.

```csharp
public class DataPageCache<T>
```

Namespace: `Microsoft.UI.Reactor.Data`.

### Constructors

#### `DataPageCache`

Creates a new DataPageCache.

```csharp
public DataPageCache(IDataSource<T> source, int blockSize = 50, int maxBlocks = 20)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `source` | [`IDataSource<T>`](IDataSource.md) |  | The data source to cache. |
| `blockSize` | `int` | `50` | Number of items per block (page size). Default 50. |
| `maxBlocks` | `int` | `20` | Maximum number of blocks to keep in cache. Default 20. |

### Properties

#### `BlockSize`

Number of items per block.

```csharp
public int BlockSize  // get
```

#### `CachedBlockCount`

Number of blocks currently in the cache.

```csharp
public int CachedBlockCount  // get
```

#### `CurrentState`

Gets the current sort/filter state. Changing this via [`DataPageCache.SetState`](#setstate) invalidates all cached blocks.

```csharp
public DataRequest CurrentState  // get
```

#### `MaxBlocks`

Maximum number of blocks in cache before LRU eviction.

```csharp
public int MaxBlocks  // get
```

#### `TotalCount`

Total known row count from the last data source response.

```csharp
public int? TotalCount  // get
```

### Methods

#### `GetBlock`

Get the block containing the given block index. Returns cached data immediately for loaded blocks, or initiates an async fetch for missing blocks and returns a loading placeholder.

```csharp
public CacheBlock<T> GetBlock(int blockIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `blockIndex` | `int` |  |

#### `GetBlockAsync`

Get the block containing the given block index asynchronously. If the block is cached, returns immediately. Otherwise waits for the fetch.

```csharp
public ValueTask<CacheBlock<T>> GetBlockAsync(int blockIndex, CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `blockIndex` | `int` |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |

#### `GetBlockStatus`

Get the status of a block.

```csharp
public BlockStatus GetBlockStatus(int blockIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `blockIndex` | `int` |  |

#### `GetItem`

Get the item at a specific row index, or default if not loaded. Triggers an async fetch for missing blocks.

```csharp
public T? GetItem(int rowIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `rowIndex` | `int` |  |

#### `Invalidate`

Invalidate all cached blocks (e.g., after sort/filter change).

```csharp
public void Invalidate()
```

#### `IsLoaded`

Check if a row index is loaded.

```csharp
public bool IsLoaded(int rowIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `rowIndex` | `int` |  |

#### `PeekItem`

Read-only peek: returns the item if its block is already loaded, or default if not. Does NOT trigger a fetch for missing blocks. Safe to call during a render pass.

```csharp
public T? PeekItem(int rowIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `rowIndex` | `int` |  |

#### `RequestBlock`

Request that a block be loaded if it isn't already cached or in-flight. The fetch is deferred via Task.Yield so it never runs synchronously in the caller's stack frame, but stays on the caller's thread (UI thread). Fires [`DataPageCache.BlockLoaded`](#blockloaded) on completion.

```csharp
public void RequestBlock(int blockIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `blockIndex` | `int` |  |

#### `SetState`

Update the sort/filter state. Invalidates all cached blocks.

```csharp
public void SetState(DataRequest state)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `state` | [`DataRequest`](descriptors.md#datarequest) |  |

### Events

#### `BlockLoaded`

Fires when a block finishes loading (success or failure).

```csharp
public event Action<int>? BlockLoaded
```

## `InfiniteResource<TItem>`

Pull-model paginated result view. Each call-site of `UseInfiniteResource` owns a single [`InfiniteResource<TItem>`](#infiniteresourcetitem); virtualized controls call [`InfiniteResource.ItemAt`](#itemat) / [`InfiniteResource.EnsureRange`](#ensurerange) per row to drive fetches, and render `null` slots as placeholders.

```csharp
public sealed class InfiniteResource<TItem>
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Remarks

All mutating methods are UI-thread-affined. Internally the implementation shares a lock for structural mutations (page table, cursor, load state). Callers should not enumerate [`InfiniteResource.Items`](#items_2) from a background thread.

### Properties

#### `EstimatedRemaining`

Approximate number of items not yet loaded. 0 once [`InfiniteResource.HasMore`](#hasmore) is false.

```csharp
public int EstimatedRemaining  // get
```

#### `HasMore`

True iff the server has not yet reported `NextCursor == null`.

```csharp
public bool HasMore  // get
```

#### `Items`

Flat, virtualized view. Indices beyond loaded content are either null-placeholders (in-flight or about-to-fetch) or, past the known end, not returned.

```csharp
public IReadOnlyList<TItem?> Items  // get
```

#### `LoadState`

Current load state — updated on every fetch lifecycle event.

```csharp
public LoadState LoadState  // get
```

#### `TotalCount`

Total items as reported by the server, or null when unknown.

```csharp
public int? TotalCount  // get
```

### Methods

#### `EnsureRange`

Eagerly ensures every page covering `[firstIndex, lastIndex]` is loaded (or in-flight). Dedups against existing in-flight fetches.

```csharp
public void EnsureRange(int firstIndex, int lastIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `firstIndex` | `int` |  |
| `lastIndex` | `int` |  |

#### `FetchNext`

Fetches the next page using the last-known cursor. No-op if load-state is not [`LoadState.Idle`](async-values.md#loadstateidle) or if [`InfiniteResource.HasMore`](#hasmore) is false.

```csharp
public void FetchNext()
```

#### `ItemAt`

Returns the item at `index` or null when the slot is in-flight / unloaded. Triggers a coalesced fetch when the slot's page is not yet requested, unless the index is past the known end.

```csharp
public TItem? ItemAt(int index)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `index` | `int` |  |

#### `Refresh`

Invalidate all cached pages and refetch from page 1. Preserves the current [`InfiniteResource.TotalCount`](#totalcount_2) — consumers that want to snapshot scroll state should do so inside their [`LoadState.Loading`](async-values.md#loadstateloading) render.

```csharp
public void Refresh()
```

#### `Retry`

Retry the most recent failed page fetch. No-op if the current load-state is not [`LoadState.Error`](async-values.md#loadstateerror).

```csharp
public void Retry()
```

## `InfiniteResourceOptions`

Tuning for `UseInfiniteResource`. Defaults: 50 items per page, no LRU cap (keeps every loaded page), 5-minute `CacheTime`, 0 `StaleTime`.

```csharp
public sealed record InfiniteResourceOptions(int PageSize = 50, int? MaxLoadedPages = null, TimeSpan? StaleTime = null, TimeSpan? CacheTime = null, string? CacheKeyPrefix = null)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `InfiniteResourceOptions`

Tuning for `UseInfiniteResource`. Defaults: 50 items per page, no LRU cap (keeps every loaded page), 5-minute `CacheTime`, 0 `StaleTime`.

```csharp
public InfiniteResourceOptions(int PageSize = 50, int? MaxLoadedPages = null, TimeSpan? StaleTime = null, TimeSpan? CacheTime = null, string? CacheKeyPrefix = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `PageSize` | `int` | `50` | Number of items per page. |
| `MaxLoadedPages` | `int?` | `null` | Maximum retained pages; null keeps all loaded pages. |
| `StaleTime` | `TimeSpan?` | `null` | Stale-time setting; null uses zero. |
| `CacheTime` | `TimeSpan?` | `null` | Retention after the last subscriber leaves; null uses five minutes. |
| `CacheKeyPrefix` | `string?` | `null` | Overrides the prefix for page cache keys; null uses a prefix unique to this hook. Page keys still include the dependency hash, initial page size, and page index. Changing deps cancels in-flight page requests, resets the page set, and loads the first page for the new deps, even if the prefix is unchanged. |

### Properties

#### `CacheKeyPrefix`

Overrides the prefix for page cache keys; null uses a prefix unique to this hook. Page keys still include the dependency hash, initial page size, and page index. Changing deps cancels in-flight page requests, resets the page set, and loads the first page for the new deps, even if the prefix is unchanged.

```csharp
public string? CacheKeyPrefix  // get, init
```

#### `CacheTime`

Retention after the last subscriber leaves; null uses five minutes.

```csharp
public TimeSpan? CacheTime  // get, init
```

#### `MaxLoadedPages`

Maximum retained pages; null keeps all loaded pages.

```csharp
public int? MaxLoadedPages  // get, init
```

#### `PageSize`

Number of items per page.

```csharp
public int PageSize  // get, init
```

#### `StaleTime`

Stale-time setting; null uses zero.

```csharp
public TimeSpan? StaleTime  // get, init
```

### Fields

#### `Default`

```csharp
public static readonly InfiniteResourceOptions Default
```

## `Page<TItem, TCursor>`

One page of paginated results, as returned by a `UseInfiniteResource` fetcher.

```csharp
public sealed record Page<TItem, TCursor>(IReadOnlyList<TItem> Items, TCursor? NextCursor, int? TotalCount = null)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `Page`

One page of paginated results, as returned by a `UseInfiniteResource` fetcher.

```csharp
public Page(IReadOnlyList<TItem> Items, TCursor? NextCursor, int? TotalCount = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Items` | `IReadOnlyList<TItem>` |  | The page payload. |
| `NextCursor` | `TCursor?` |  | Cursor for the next page; null signals the end of the list. |
| `TotalCount` | `int?` | `null` | Server-reported total (when known); enables stable virtualization. |

### Properties

#### `Items`

The page payload.

```csharp
public IReadOnlyList<TItem> Items  // get, init
```

#### `NextCursor`

Cursor for the next page; null signals the end of the list.

```csharp
public TCursor? NextCursor  // get, init
```

#### `TotalCount`

Server-reported total (when known); enables stable virtualization.

```csharp
public int? TotalCount  // get, init
```
