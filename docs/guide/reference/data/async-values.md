# Async values

The value a resource hook returns while it loads, after it loads and after it fails.

**Learn more:** [Async Resources](../../async-resources.md)

## `AsyncValue<T>`

Algebraic data type representing the four observable states of an async fetch: [`AsyncValue<T>.Loading`](#asyncvaluetloading), [`AsyncValue<T>.Data`](#asyncvaluetdata), [`AsyncValue<T>.Error`](#asyncvalueterror), and [`AsyncValue<T>.Reloading`](#asyncvaluetreloading). Designed for exhaustive pattern matching with C# switch expressions: the hierarchy is `closed`, so a C# 15+ `switch` that handles all four cases needs no discard arm, and omitting a case is a CS8509 warning naming the missing case.

```csharp
public abstract record AsyncValue<T>
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Remarks

See spec `docs/specs/020-async-resources-design.md` §5 for the design rationale and §6.2 for the transition rules driven by `UseResource`.

### Type parameters

| Name | Description |
| --- | --- |
| `T` | The value type produced by the fetcher. |

### Constructors

#### `AsyncValue`

```csharp
protected AsyncValue()
```

### Methods

#### `Match`

Convenience match that renders the `loading` arm for [`AsyncValue<T>.Reloading`](#asyncvaluetreloading) by default. Pass `reloading` explicitly to show stale content while a refresh is in progress (stale-while-revalidate).

```csharp
public TResult Match<TResult>(Func<TResult> loading, Func<T, TResult> loaded, Func<Exception, TResult> error, Func<T, TResult>? reloading = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `loading` | `Func<TResult>` |  |  |
| `loaded` | `Func<T, TResult>` |  |  |
| `error` | `Func<Exception, TResult>` |  |  |
| `reloading` | `Func<T, TResult>?` | `null` |  |

**Remarks**

Omitting `reloading` means a refresh shows the loading UI, so authors don't have to duplicate the loading arm. Provide `reloading` to keep the previous value visible while revalidating.

The success arm is named `loaded`. This is a source-breaking rename from the previous `data` parameter for named-argument callers; a back-compat overload is impossible because overloads cannot differ by parameter name alone.

Allocates a delegate per arm at the call site; prefer a `switch` expression in per-row render paths (see spec §5.1).

### `AsyncValue<T>.Data`

Fetch succeeded; `Value` is authoritative. Transitions to [`AsyncValue<T>.Reloading`](#asyncvaluetreloading) when a refetch starts, or back to [`AsyncValue<T>.Data`](#asyncvaluetdata) on cache hit within `StaleTime`.

```csharp
public sealed record Data(T Value) : AsyncValue<T>
```

#### Constructors

##### `Data`

Fetch succeeded; `Value` is authoritative. Transitions to [`AsyncValue<T>.Reloading`](#asyncvaluetreloading) when a refetch starts, or back to [`AsyncValue<T>.Data`](#asyncvaluetdata) on cache hit within `StaleTime`.

```csharp
public Data(T Value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `T` |  |

#### Properties

##### `Value`

```csharp
public T Value  // get, init
```

### `AsyncValue<T>.Error`

Fetch failed. Any prior data is discarded — `UseResource` does not retain stale values across a failure. If you need "last known good", wrap the fetcher.

```csharp
public sealed record Error(Exception Exception) : AsyncValue<T>
```

#### Constructors

##### `Error`

Fetch failed. Any prior data is discarded — `UseResource` does not retain stale values across a failure. If you need "last known good", wrap the fetcher.

```csharp
public Error(Exception Exception)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Exception` | `Exception` |  |

#### Properties

##### `Exception`

```csharp
public Exception Exception  // get, init
```

### `AsyncValue<T>.Loading`

First fetch in flight; no prior data is available. Transitions to [`AsyncValue<T>.Data`](#asyncvaluetdata) or [`AsyncValue<T>.Error`](#asyncvalueterror).

```csharp
public sealed record Loading : AsyncValue<T>
```

#### Constructors

##### `Loading`

```csharp
public Loading()
```

#### Fields

##### `Instance`

Singleton instance — [`AsyncValue<T>.Loading`](#asyncvaluetloading) has no payload.

```csharp
public static readonly AsyncValue<T>.Loading Instance
```

### `AsyncValue<T>.Reloading`

Refetching with the previous value still observable (stale-while-revalidate). The `Previous` payload is intentionally non-nullable so call sites can render the stale tree without null-guards. Components may choose to dim, overlay a spinner, or render identically to [`AsyncValue<T>.Data`](#asyncvaluetdata) — §5.1.

```csharp
public sealed record Reloading(T Previous) : AsyncValue<T>
```

#### Constructors

##### `Reloading`

Refetching with the previous value still observable (stale-while-revalidate). The `Previous` payload is intentionally non-nullable so call sites can render the stale tree without null-guards. Components may choose to dim, overlay a spinner, or render identically to [`AsyncValue<T>.Data`](#asyncvaluetdata) — §5.1.

```csharp
public Reloading(T Previous)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Previous` | `T` |  |

#### Properties

##### `Previous`

```csharp
public T Previous  // get, init
```

## `LoadState`

The four discrete load states of an [`InfiniteResource<TItem>`](paging.md#infiniteresourcetitem). Mirrors Android Paging 3's `LoadState`. The hierarchy is `closed`, so a C# 15+ `switch` over all four cases is exhaustive without a discard arm.

```csharp
public abstract record LoadState
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `LoadState`

```csharp
protected LoadState()
```

### `LoadState.EndOfList`

Server reported `NextCursor == null` — no further pages.

```csharp
public sealed record EndOfList : LoadState
```

#### Constructors

##### `EndOfList`

```csharp
public EndOfList()
```

#### Fields

##### `Instance`

```csharp
public static readonly LoadState.EndOfList Instance
```

### `LoadState.Error`

Most recent fetch failed; call `Retry()` to retry the failed page.

```csharp
public sealed record Error(Exception Exception) : LoadState
```

#### Constructors

##### `Error`

Most recent fetch failed; call `Retry()` to retry the failed page.

```csharp
public Error(Exception Exception)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Exception` | `Exception` |  |

#### Properties

##### `Exception`

```csharp
public Exception Exception  // get, init
```

### `LoadState.Idle`

No fetch pending; `HasMore` determines whether more pages are available.

```csharp
public sealed record Idle : LoadState
```

#### Constructors

##### `Idle`

```csharp
public Idle()
```

#### Fields

##### `Instance`

```csharp
public static readonly LoadState.Idle Instance
```

### `LoadState.Loading`

A page fetch is in flight.

```csharp
public sealed record Loading : LoadState
```

#### Constructors

##### `Loading`

```csharp
public Loading()
```

#### Fields

##### `Instance`

```csharp
public static readonly LoadState.Loading Instance
```
