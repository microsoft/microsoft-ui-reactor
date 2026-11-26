---
title: "Paging"
description: "Types for infinite and paged resources."
---

**Learn more:** [Async Resources](/microsoft-ui-reactor/main/guides/async-resources), [Data System](/microsoft-ui-reactor/main/controls/data-system)

## `BlockStatus` [#blockstatus]

Status of a cached block.

```csharp
public enum BlockStatus
```

Namespace: `Microsoft.UI.Reactor.Data`.

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `Loading` | `0` | Block is currently being loaded from the data source. |
| `Loaded` | `1` | Block data is available. |
| `Failed` | `2` | Block load failed. |

## `CacheBlock<T>` [#cacheblockt]

A cached block of data from a paged data source.

```csharp
public record CacheBlock<T>(int BlockIndex, IReadOnlyList<T> Items, BlockStatus Status, string? Error = null)
```

Namespace: `Microsoft.UI.Reactor.Data`.

### Constructors [#constructors]

#### `CacheBlock` [#cacheblock]

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

### Properties [#properties]

#### `BlockIndex` [#blockindex]

```csharp
public int BlockIndex  // get, init
```

#### `Error` [#error]

```csharp
public string? Error  // get, init
```

#### `Items` [#items]

```csharp
public IReadOnlyList<T> Items  // get, init
```

#### `Status` [#status]

```csharp
public BlockStatus Status  // get, init
```

### Methods [#methods]

#### `LoadingBlock` [#loadingblock]

Empty loading placeholder block.

```csharp
public static CacheBlock<T> LoadingBlock(int index)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `index` | `int` |  |

## `DataPage<T>` [#datapaget]

A page of data from a data source.

```csharp
public record DataPage<T>(IReadOnlyList<T> Items, string? ContinuationToken = null, int? TotalCount = null)
```

Namespace: `Microsoft.UI.Reactor.Data`.

### Type parameters [#type-parameters]

| Name | Description |
| --- | --- |
| `T` | The item type. |

### Constructors [#constructors_1]

#### `DataPage` [#datapage]

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

### Properties [#properties_1]

#### `ContinuationToken` [#continuationtoken]

Token for fetching the next page. Null means this is the last page.

```csharp
public string? ContinuationToken  // get, init
```

#### `Items` [#items_1]

The items in this page.

```csharp
public IReadOnlyList<T> Items  // get, init
```

#### `TotalCount` [#totalcount]

Total number of items matching the current filters (if the source supports counting). Null if the source doesn't support server-side counting.

```csharp
public int? TotalCount  // get, init
```

## `DataPageCache<T>` [#datapagecachet]

Caches pages of data from IDataSource, keyed by sort/filter state + page index. Evicts blocks outside the viewport via LRU when max capacity is reached.

Follows a pull model (matching Compose Paging 3): accessing a row index that falls in an unloaded block triggers the fetch. The virtualizer tells the cache which blocks are needed; the cache returns immediately for loaded blocks and initiates async loads for missing ones.

```csharp
public class DataPageCache<T>
```

Namespace: `Microsoft.UI.Reactor.Data`.

### Constructors [#constructors_2]

#### `DataPageCache` [#datapagecache]

Creates a new DataPageCache.

```csharp
public DataPageCache(IDataSource<T> source, int blockSize = 50, int maxBlocks = 20)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `source` | [`IDataSource<T>`](/microsoft-ui-reactor/main/reference/data/IDataSource) |  | The data source to cache. |
| `blockSize` | `int` | `50` | Number of items per block (page size). Default 50. |
| `maxBlocks` | `int` | `20` | Maximum number of blocks to keep in cache. Default 20. |

### Properties [#properties_2]

#### `BlockSize` [#blocksize]

Number of items per block.

```csharp
public int BlockSize  // get
```

#### `CachedBlockCount` [#cachedblockcount]

Number of blocks currently in the cache.

```csharp
public int CachedBlockCount  // get
```

#### `CurrentState` [#currentstate]

Gets the current sort/filter state. Changing this via [`DataPageCache.SetState`](#setstate) invalidates all cached blocks.

```csharp
public DataRequest CurrentState  // get
```

#### `MaxBlocks` [#maxblocks]

Maximum number of blocks in cache before LRU eviction.

```csharp
public int MaxBlocks  // get
```

#### `TotalCount` [#totalcount_1]

Total known row count from the last data source response.

```csharp
public int? TotalCount  // get
```

### Methods [#methods_1]

#### `GetBlock` [#getblock]

Get the block containing the given block index. Returns cached data immediately for loaded blocks, or initiates an async fetch for missing blocks and returns a loading placeholder.

```csharp
public CacheBlock<T> GetBlock(int blockIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `blockIndex` | `int` |  |

#### `GetBlockAsync` [#getblockasync]

Get the block containing the given block index asynchronously. If the block is cached, returns immediately. Otherwise waits for the fetch.

```csharp
public ValueTask<CacheBlock<T>> GetBlockAsync(int blockIndex, CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `blockIndex` | `int` |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |

#### `GetBlockStatus` [#getblockstatus]

Get the status of a block.

```csharp
public BlockStatus GetBlockStatus(int blockIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `blockIndex` | `int` |  |

#### `GetItem` [#getitem]

Get the item at a specific row index, or default if not loaded. Triggers an async fetch for missing blocks.

```csharp
public T? GetItem(int rowIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `rowIndex` | `int` |  |

#### `Invalidate` [#invalidate]

Invalidate all cached blocks (e.g., after sort/filter change).

```csharp
public void Invalidate()
```

#### `IsLoaded` [#isloaded]

Check if a row index is loaded.

```csharp
public bool IsLoaded(int rowIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `rowIndex` | `int` |  |

#### `PeekItem` [#peekitem]

Read-only peek: returns the item if its block is already loaded, or default if not. Does NOT trigger a fetch for missing blocks. Safe to call during a render pass.

```csharp
public T? PeekItem(int rowIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `rowIndex` | `int` |  |

#### `RequestBlock` [#requestblock]

Request that a block be loaded if it isn't already cached or in-flight. The fetch is deferred via Task.Yield so it never runs synchronously in the caller's stack frame, but stays on the caller's thread (UI thread). Fires [`DataPageCache.BlockLoaded`](#blockloaded) on completion.

```csharp
public void RequestBlock(int blockIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `blockIndex` | `int` |  |

#### `SetState` [#setstate]

Update the sort/filter state. Invalidates all cached blocks.

```csharp
public void SetState(DataRequest state)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `state` | [`DataRequest`](/microsoft-ui-reactor/main/reference/data/descriptors#datarequest) |  |

### Events [#events]

#### `BlockLoaded` [#blockloaded]

Fires when a block finishes loading (success or failure).

```csharp
public event Action<int>? BlockLoaded
```

## `InfiniteResource<TItem>` [#infiniteresourcetitem]

Pull-model paginated result view. Each call-site of `UseInfiniteResource` owns a single [`InfiniteResource<TItem>`](#infiniteresourcetitem); virtualized controls call [`InfiniteResource.ItemAt`](#itemat) / [`InfiniteResource.EnsureRange`](#ensurerange) per row to drive fetches, and render `null` slots as placeholders.

```csharp
public sealed class InfiniteResource<TItem>
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Remarks [#remarks]

All mutating methods are UI-thread-affined. Internally the implementation shares a lock for structural mutations (page table, cursor, load state). Callers should not enumerate [`InfiniteResource.Items`](#items_2) from a background thread.

### Properties [#properties_3]

#### `EstimatedRemaining` [#estimatedremaining]

Approximate number of items not yet loaded. 0 once [`InfiniteResource.HasMore`](#hasmore) is false.

```csharp
public int EstimatedRemaining  // get
```

#### `HasMore` [#hasmore]

True iff the server has not yet reported `NextCursor == null`.

```csharp
public bool HasMore  // get
```

#### `Items` [#items_2]

Flat, virtualized view. Indices beyond loaded content are either null-placeholders (in-flight or about-to-fetch) or, past the known end, not returned.

```csharp
public IReadOnlyList<TItem?> Items  // get
```

#### `LoadState` [#loadstate]

Current load state — updated on every fetch lifecycle event.

```csharp
public LoadState LoadState  // get
```

#### `TotalCount` [#totalcount_2]

Total items as reported by the server, or null when unknown.

```csharp
public int? TotalCount  // get
```

### Methods [#methods_2]

#### `EnsureRange` [#ensurerange]

Eagerly ensures every page covering `[firstIndex, lastIndex]` is loaded (or in-flight). Dedups against existing in-flight fetches.

```csharp
public void EnsureRange(int firstIndex, int lastIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `firstIndex` | `int` |  |
| `lastIndex` | `int` |  |

#### `FetchNext` [#fetchnext]

Fetches the next page using the last-known cursor. No-op if load-state is not [`LoadState.Idle`](/microsoft-ui-reactor/main/reference/data/async-values#loadstateidle) or if [`InfiniteResource.HasMore`](#hasmore) is false.

```csharp
public void FetchNext()
```

#### `ItemAt` [#itemat]

Returns the item at `index` or null when the slot is in-flight / unloaded. Triggers a coalesced fetch when the slot's page is not yet requested, unless the index is past the known end.

```csharp
public TItem? ItemAt(int index)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `index` | `int` |  |

#### `Refresh` [#refresh]

Invalidate all cached pages and refetch from page 1. Preserves the current [`InfiniteResource.TotalCount`](#totalcount_2) — consumers that want to snapshot scroll state should do so inside their [`LoadState.Loading`](/microsoft-ui-reactor/main/reference/data/async-values#loadstateloading) render.

```csharp
public void Refresh()
```

#### `Retry` [#retry]

Retry the most recent failed page fetch. No-op if the current load-state is not [`LoadState.Error`](/microsoft-ui-reactor/main/reference/data/async-values#loadstateerror).

```csharp
public void Retry()
```

## `InfiniteResourceOptions` [#infiniteresourceoptions]

Tuning for `UseInfiniteResource`. Defaults: 50 items per page, no LRU cap (keeps every loaded page), 5-minute `CacheTime`, 0 `StaleTime`.

```csharp
public sealed record InfiniteResourceOptions(int PageSize = 50, int? MaxLoadedPages = null, TimeSpan? StaleTime = null, TimeSpan? CacheTime = null, string? CacheKeyPrefix = null)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors [#constructors_3]

#### `InfiniteResourceOptions` [#infiniteresourceoptions_1]

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

### Properties [#properties_4]

#### `CacheKeyPrefix` [#cachekeyprefix]

Overrides the prefix for page cache keys; null uses a prefix unique to this hook. Page keys still include the dependency hash, initial page size, and page index. Changing deps cancels in-flight page requests, resets the page set, and loads the first page for the new deps, even if the prefix is unchanged.

```csharp
public string? CacheKeyPrefix  // get, init
```

#### `CacheTime` [#cachetime]

Retention after the last subscriber leaves; null uses five minutes.

```csharp
public TimeSpan? CacheTime  // get, init
```

#### `MaxLoadedPages` [#maxloadedpages]

Maximum retained pages; null keeps all loaded pages.

```csharp
public int? MaxLoadedPages  // get, init
```

#### `PageSize` [#pagesize]

Number of items per page.

```csharp
public int PageSize  // get, init
```

#### `StaleTime` [#staletime]

Stale-time setting; null uses zero.

```csharp
public TimeSpan? StaleTime  // get, init
```

### Fields [#fields]

#### `Default` [#default]

```csharp
public static readonly InfiniteResourceOptions Default
```

## `Page<TItem, TCursor>` [#pagetitem-tcursor]

One page of paginated results, as returned by a `UseInfiniteResource` fetcher.

```csharp
public sealed record Page<TItem, TCursor>(IReadOnlyList<TItem> Items, TCursor? NextCursor, int? TotalCount = null)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors [#constructors_4]

#### `Page` [#page]

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

### Properties [#properties_5]

#### `Items` [#items_3]

The page payload.

```csharp
public IReadOnlyList<TItem> Items  // get, init
```

#### `NextCursor` [#nextcursor]

Cursor for the next page; null signals the end of the list.

```csharp
public TCursor? NextCursor  // get, init
```

#### `TotalCount` [#totalcount_3]

Server-reported total (when known); enables stable virtualization.

```csharp
public int? TotalCount  // get, init
```
