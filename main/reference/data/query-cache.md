---
title: "Query cache"
description: "The process-wide cache that resource hooks share."
---

**Learn more:** [Async Resources](/microsoft-ui-reactor/main/guides/async-resources)

## `CacheEntry<T>` [#cacheentryt]

```csharp
public sealed record CacheEntry<T>(T Value, DateTime FetchedAt, TimeSpan StaleTime, int SubscriberCount)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors [#constructors]

#### `CacheEntry` [#cacheentry]

```csharp
public CacheEntry(T Value, DateTime FetchedAt, TimeSpan StaleTime, int SubscriberCount)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `T` |  |
| `FetchedAt` | `DateTime` |  |
| `StaleTime` | `TimeSpan` |  |
| `SubscriberCount` | `int` |  |

### Properties [#properties]

#### `FetchedAt` [#fetchedat]

```csharp
public DateTime FetchedAt  // get, init
```

#### `StaleTime` [#staletime]

```csharp
public TimeSpan StaleTime  // get, init
```

#### `SubscriberCount` [#subscribercount]

```csharp
public int SubscriberCount  // get, init
```

#### `Value` [#value]

```csharp
public T Value  // get, init
```

## `QueryCache` [#querycache]

Process-wide query cache with TTL, ref-counted subscriptions, and pattern invalidation. Shared across components via [`Context<T>`](/microsoft-ui-reactor/main/reference/components/Context) — see [`AppContexts.QueryCache`](/microsoft-ui-reactor/main/reference/components/AppContexts#querycache).

```csharp
public sealed class QueryCache : IDisposable
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Remarks [#remarks]

**Threading.** All public methods are safe to call from any thread. Slots are protected by a per-key lock; [`QueryCache.EntryChanged`](#entrychanged) fires either inline or on the configured [`QueryCache.DispatcherPost`](#dispatcherpost) callback (set by `ReactorHost` at bootstrap). Tests can leave it null to observe events inline.

**Eviction.** A single shared timer (started lazily on first subscription) polls all slots every [`QueryCache.EvictionPollInterval`](#evictionpollinterval). Slots whose `SubscriberCount == 0` for longer than their `CacheTime` are evicted. Using one timer keeps the entry count's impact on system timers O(1).

### Constructors [#constructors_1]

#### `QueryCache` [#querycache_1]

```csharp
public QueryCache()
```

### Properties [#properties_1]

#### `Count` [#count]

Snapshot the current cache size. Diagnostic only — do not use for correctness.

```csharp
public int Count  // get
```

#### `DispatcherPost` [#dispatcherpost]

Injected marshaller for [`QueryCache.EntryChanged`](#entrychanged). Null → fires inline.

```csharp
public Action<Action>? DispatcherPost  // get, set
```

#### `EvictionPollInterval` [#evictionpollinterval]

How often the eviction timer checks every slot for expiry. Default 1s.

```csharp
public static TimeSpan EvictionPollInterval  // get, set
```

#### `UtcNow` [#utcnow]

Clock override for deterministic tests.

```csharp
public Func<DateTime> UtcNow  // get, set
```

### Methods [#methods]

#### `Clear` [#clear]

Removes every entry. Subscribers are retained. Fires [`QueryCache.EntryChanged`](#entrychanged) once per key that actually held a value.

```csharp
public void Clear()
```

#### `Dispose` [#dispose]

Releases the resources that this object holds.

```csharp
public void Dispose()
```

#### `EvictNow` [#evictnow]

Test / diagnostic hook: drive the eviction sweep explicitly rather than waiting for the background [`QueryCache.EvictionPollInterval`](#evictionpollinterval) timer. Returns the set of keys that were evicted on this pass. Safe to call from any thread.

```csharp
public IReadOnlyList<string> EvictNow()
```

**Remarks**

Production code should not need this — subscribers keep entries alive and the shared timer evicts zero-subscriber entries past their `CacheTime`. It's exposed publicly so framerate / stress tests can make eviction deterministic.

#### `Invalidate` [#invalidate]

Removes the entry (if any) for `key`. Subscribers are retained — the next [`QueryCache.Set`](#set) will see their count. Fires [`QueryCache.EntryChanged`](#entrychanged) if there was an entry to remove.

```csharp
public void Invalidate(string key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |

#### `InvalidatePattern` [#invalidatepattern]

Invalidates every key that starts with `keyPrefix`. O(n) in the number of cached keys — document the pattern prefix carefully if you store a lot of unrelated keys under one cache.

```csharp
public void InvalidatePattern(string keyPrefix)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `keyPrefix` | `string` |  |

#### `Set` [#set]

Stores or replaces the entry for `key`. Preserves any existing subscriber count on the slot. Fires [`QueryCache.EntryChanged`](#entrychanged).

```csharp
public void Set<T>(string key, T value, TimeSpan staleTime, TimeSpan cacheTime)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `T` |  |
| `staleTime` | `TimeSpan` |  |
| `cacheTime` | `TimeSpan` |  |

#### `Subscribe` [#subscribe]

Increments the subscriber count on `key`. Cancels any pending eviction for the slot. Returns the new subscriber count.

```csharp
public int Subscribe(string key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |

#### `TryGet` [#tryget]

Retrieves the current entry for `key` if present and typed as `T`. Returns false on miss or on type-mismatch. Staleness is a caller-side decision — the entry's [`CacheEntry.FetchedAt`](#fetchedat) and [`CacheEntry.StaleTime`](#staletime) are both returned on a hit.

```csharp
public bool TryGet<T>(string key, out CacheEntry<T> entry)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `entry` | [`CacheEntry<T>`](#cacheentryt) |  |

#### `Unsubscribe` [#unsubscribe]

Decrements the subscriber count. When it reaches zero, starts the eviction clock based on the slot's most recent [`QueryCache.Set`](#set) `cacheTime`. Throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) if called on a slot that has no subscribers — this catches hook-logic bugs that would otherwise hide underflow forever.

```csharp
public int Unsubscribe(string key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |

### Events [#events]

#### `EntryChanged` [#entrychanged]

Fires when an entry is added, replaced, invalidated, or evicted.

```csharp
public event Action<string>? EntryChanged
```
