# Async Resources — Design Specification

**Date:** April 2026
**Status:** Draft / Proposal
**Author:** Chris Anderson
**Related specs:** [009 State & Components](009-state-and-components-design.md),
[017 Data System](017-data-system-design.md)

---

## Table of Contents

1. [Problem Statement](#1-problem-statement)
2. [Goals and Non-Goals](#2-goals-and-non-goals)
3. [Prior Art](#3-prior-art)
4. [Design Overview](#4-design-overview)
5. [`AsyncValue<T>` — the core ADT](#5-asyncvaluet--the-core-adt)
6. [`UseResource` — single async fetch](#6-useresource--single-async-fetch)
7. [`UseInfiniteResource` — paginated fetch](#7-useinfiniteresource--paginated-fetch)
8. [`UseMutation` — async writes with optimistic updates](#8-usemutation--async-writes-with-optimistic-updates)
9. [The Query Cache](#9-the-query-cache)
10. [`Pending` element — optional bubble-up fallback](#10-pending-element--optional-bubble-up-fallback)
11. [DataGrid Integration](#11-datagrid-integration)
12. [Use Cases We Cover Well](#12-use-cases-we-cover-well)
13. [Where This Doesn't Fit](#13-where-this-doesnt-fit)
14. [Design Decisions (D1–D18)](#14-design-decisions)
15. [Open Questions](#15-open-questions)
16. [Implementation Phases](#16-implementation-phases)

---

## 1. Problem Statement

Microsoft.UI.Reactor (Reactor) apps overwhelmingly follow a single shape:

```
UI action → async HTTP call → update UI state
```

Today, every component that talks to a backend re-implements this loop with
`UseState` + `UseEffect` + `Task.Run`. This is workable for trivial cases and
actively harmful for anything non-trivial:

1. **Cancellation is frequently wrong.** `UseEffect` hands the dev a cleanup
   function but does not hand them a `CancellationToken`, so most fetches race
   stale requests against fresh ones. Component authors rediscover this every
   time.
2. **Loading, error, and stale states are hand-rolled per call-site**, usually
   as three separate `UseState` slots (`data`, `loading`, `error`) with ad-hoc
   state machines between them. There is no convention for "we had data, we're
   refetching".
3. **There is no request deduplication or caching across components.** Two
   siblings that read the same user profile make two HTTP calls. Navigation
   away and back always refetches from scratch.
4. **The only component that solves this properly is DataGrid**, via a private
   `DataPageCache<T>` (`Reactor/Data/DataPageCache.cs`) that gives it
   LRU-evicted block caching, pull-model fetch scheduling, `LoadingBlock`
   placeholders, and a scroll-settle debounce. None of this is reusable — if
   `LazyVStack` or `TreeView` want paginated data, they have to rebuild it.

The proposal: extract the pattern that DataGrid has already proven works, and
expose it as a hook-level primitive (`UseResource` / `UseInfiniteResource`)
backed by a shared query cache. DataGrid then *consumes* the primitive rather
than owning a private copy of it.

This is the missing counterpart to spec 009 (which solved local and tree-scoped
state) and spec 017 (which defined `IDataSource<T>`). Spec 009 never addressed
"state that comes from I/O"; spec 017 defined the data-source contract but
left consumption outside DataGrid undefined. This spec bridges them.

---

## 2. Goals and Non-Goals

### Goals

1. **One idiomatic primitive for async state** — `AsyncValue<T>` — that
   components pattern-match exhaustively, no matter the source (HTTP, file I/O,
   long-running compute).
2. **Automatic cancellation on deps-change / unmount.** The dev never sees a
   `CancellationToken` they forgot to thread through; the hook owns it.
3. **Dedup and caching across components** via a process-wide query cache
   keyed on the hook's `deps`. Two siblings asking for the same thing get one
   request; navigating away and back within a TTL gets instant re-render.
4. **Stale-while-revalidate** as the default UX: during refetch, show the
   previous `Data` (not a spinner), but mark it `Reloading` so components can
   opt into a dimming effect.
5. **Unify DataGrid's private paging cache with the public hook surface** —
   same cache, same invalidation, same cancellation story.
6. **Play nicely with existing `IDataSource<T>`**: a paged data source should
   drop into `UseInfiniteResource` with no adapter code.

### Non-Goals

- **React-style Suspense.** We considered throw-a-Task and rejected it (D1).
  The ADT approach is more idiomatic in C# and doesn't require reconciler
  surgery.
- **Streaming / `IAsyncEnumerable`.** A separate `UseStream<T>` hook is future
  work. Shoehorning streams into a resource shape makes both worse.
- **Disk-backed or cross-session cache.** Query cache is in-process, lost on
  app exit. If we need persistence, it layers on top.
- **Replacing `IDataSource<T>`.** The `IDataSource<T>` contract stays exactly
  as it is; this spec adds a *consumer* of it, not a replacement.
- **A global state store (Redux/MobX style).** Query cache is *not* a store —
  it is a cache of server-owned state. Client state still lives in
  `UseState`, `UseReducer`, and `Context<T>`.

---

## 3. Prior Art

We surveyed seven frameworks and three dominant mental models.

### 3.1 Suspense / throw-a-promise

- **React** — `<Suspense fallback>` catches child components that throw a
  pending promise; the new `use(promise)` hook makes this first-class. Pairs
  with `useTransition` / `useDeferredValue` for stale-while-fresh.
- **Solid.js** — `createResource(source, fetcher)` returns a signal with
  `.loading` / `.error` / `.latest` and integrates with `<Suspense>` natively.
  Arguably the cleanest implementation of the pattern.
- **Vue** — `<Suspense>` with `#default` / `#fallback` slots, plus
  `defineAsyncComponent` for code-split async components.

**Why we don't adopt this directly:** throwing a `Task` in C# is awkward
(there's no special-case unwinding the reconciler can catch without reflection
tricks), and Reactor's reconciler is not fiber-based — it cannot discard and
retry a partial render. The mental model is also further from what Reactor
devs already write.

### 3.2 AsyncValue / sealed-state ADT

- **Flutter + Riverpod** — `AsyncValue<T>` with `.when(data:, loading:,
  error:)` and `AsyncData` / `AsyncLoading` / `AsyncError` subtypes. Exhaustive
  matching is idiomatic Dart and idiomatic C#.
- **Elm** — `RemoteData a e` with `NotAsked | Loading | Failure e | Success a`.
  The purest form of the pattern — pending is data, not control flow.
- **Jetpack Compose** — convention of sealed Kotlin classes
  (`UiState.Loading | Success | Error`) matched with `when`.

**Why we adopt this:** C# pattern matching on sealed records is the most
ergonomic shape C# gives us, and it is the one Reactor devs already use for
domain modeling. It has zero reconciler implications.

### 3.3 Resource primitive

- **Solid.js** — `createResource` (already cited).
- **Jetpack Compose** — `produceState(initial, key) { value = fetch() }`
  returns `State<T>` with coroutine lifecycle tied to composition.
- **SwiftUI** — `.task(id:)` modifier ties an async task to a view's lifetime;
  auto-cancels on `id` change or disappear.
- **Android Paging 3** — `Pager` / `PagingSource` / `PagingData` / `LoadState`.
  The closest direct analog of what `DataPageCache` already does. Supplies
  placeholders, prefetch, retry, and refresh as first-class concerns.
- **TanStack Query (React/Vue/Solid/Svelte)** — `useQuery` /
  `useInfiniteQuery` with a query-key cache, stale-while-revalidate,
  background refetch, request coalescing, retry with backoff, and focus-based
  revalidation. The industry leader for this pattern.
- **SWR** — similar to TanStack Query with a smaller API: `useSWR(key,
  fetcher)`, `useSWRInfinite`, cache, focus revalidation.

**Why we adopt this:** a hook that owns the `CancellationToken`, the cache
key, and the `AsyncValue<T>` together is strictly better than three
disconnected `UseState` slots. Paging 3's shape is nearly identical to our
existing `DataPageCache`, which tells us the design has already earned its
keep inside Reactor — we just haven't exposed it.

### 3.4 Synthesis

The dominant production pattern is **resource primitive + ADT + shared cache**
— Solid, TanStack Query, and Paging 3 all converge on it. We adopt that
pattern, skipping Suspense.

---

## 4. Design Overview

Four new public surfaces:

| Surface | Shape | Role |
|---|---|---|
| `AsyncValue<T>` | Sealed record hierarchy | The ADT every hook returns and every component matches on |
| `UseResource<T>` | Hook | Owns one async task keyed on deps; returns `AsyncValue<T>` |
| `UseInfiniteResource<TPage, TCursor>` | Hook | Cursor-paged, placeholder-aware; wraps `IDataSource<T>` or a custom fetcher |
| `UseMutation<TIn, TOut>` | Hook | Async write with optimistic-update and rollback, mirrors `DataGridState.BeginAsyncCommit` |

Plus one infrastructure piece:

| Surface | Shape | Role |
|---|---|---|
| `QueryCache` | Singleton, `Context<T>`-overridable | Process-wide cache keyed on `(hookId, deps)` with TTL and invalidation |

And one optional element:

| Surface | Shape | Role |
|---|---|---|
| `Pending` | Element wrapper | Bubble-up fallback — renders `fallback` if any `AsyncValue` descendant is `Loading` |

The hooks sit alongside the existing hook roster from spec 009 (`UseState`,
`UseEffect`, `UseMemo`, `UseRef`, `UseContext`, `UseReducer`). Storage uses
the same `RenderContext` hook-slot machinery; no reconciler changes required.

---

## 5. `AsyncValue<T>` — the core ADT

```csharp
public abstract record AsyncValue<T>
{
    /// <summary>First fetch in flight; no prior data.</summary>
    public sealed record Loading : AsyncValue<T>;

    /// <summary>Fetch succeeded; value is authoritative.</summary>
    public sealed record Data(T Value) : AsyncValue<T>;

    /// <summary>Fetch failed; prior data (if any) is discarded.</summary>
    public sealed record Error(Exception Exception) : AsyncValue<T>;

    /// <summary>Refetching with stale data still on screen (stale-while-revalidate).</summary>
    public sealed record Reloading(T Previous) : AsyncValue<T>;

    // Convenience shorthand — see §5.1.
    public TResult Match<TResult>(
        Func<TResult> loading,
        Func<T, TResult> data,
        Func<Exception, TResult> error,
        Func<T, TResult>? reloading = null)
        => this switch
        {
            Loading       => loading(),
            Data d        => data(d.Value),
            Error e       => error(e.Exception),
            Reloading r   => (reloading ?? data)(r.Previous),
            _             => throw new UnreachableException()
        };
}
```

### 5.1 Matching on `AsyncValue<T>`

The idiomatic form is a C# switch expression. The compiler enforces
exhaustiveness on the sealed hierarchy (CS8509), pattern destructuring
pulls the payload out directly, and there are no delegate allocations per
render — which matters when the same `Match` runs per-row inside a
virtualized `DataGrid`.

```csharp
return user switch
{
    AsyncValue<User>.Loading          => Skeleton().Height(120),
    AsyncValue<User>.Data(var u)      => VStack(Heading(u.Name), Text(u.Email)),
    AsyncValue<User>.Reloading(var u) => VStack(Heading(u.Name), Text(u.Email)).Opacity(0.6),
    AsyncValue<User>.Error(var ex)    => Text($"Failed: {ex.Message}").Foreground(Red),
};
```

The switch also supports guards (`Data d when d.Value.IsStale => ...`),
per-arm debugging breakpoints, and partial matches where the compiler
warns on the uncovered cases — all things a helper method can't give you.

**When to use `.Match()`:** as a convenience when (a) you explicitly want
`Reloading` to render the same tree as `Data` and don't want to duplicate
the expression, or (b) you're in a non-hot-path site where the two or
three delegate allocations per call are irrelevant and you prefer the
named-argument reading. Everywhere else, prefer the switch.

```csharp
// Both read Reloading as Data — fallback is automatic.
return user.Match(
    loading: () => Skeleton().Height(120),
    data:    u  => VStack(Heading(u.Name), Text(u.Email)),
    error:   e  => Text($"Failed: {e.Message}").Foreground(Red));
```

### Why four states, not three

We debated collapsing `Reloading` into `Data` with an `IsStale` flag. Rejected
because C# exhaustive-match warnings lose their teeth when the interesting
distinction lives inside a property, and because refetch-dimming is a
*render-time* decision that belongs in the type, not the data. See D3.

### Why `Reloading` carries `Previous`, not `T?`

`null` is ambiguous (is there a prior value or not?) and the type doesn't
constrain callers to render the stale data. By naming it `Previous` we signal
intent — "there *is* a value; show it dimmed if you want, or show a spinner
if you don't".

### Non-goal: recovery hints on `Error`

`Error` carries only `Exception`. No "retry count", "last known good", or
"error category" fields. If call sites want that, they wrap the fetcher. The
hook primitive stays small.

---

## 6. `UseResource` — single async fetch

### 6.1 Signature

```csharp
public AsyncValue<T> UseResource<T>(
    Func<CancellationToken, Task<T>> fetcher,
    object[] deps,
    ResourceOptions? options = null);

public sealed record ResourceOptions(
    TimeSpan? StaleTime = null,        // cache hit within this returns immediately
    TimeSpan? CacheTime = null,        // evict from cache after this idle
    int RetryCount = 0,                // automatic retries with exponential backoff
    bool RefetchOnMount = true,        // default true; false = cache-only
    string? CacheKey = null);          // override auto-derived key
```

### 6.2 Behavior

1. On first render, the hook computes a cache key = `CacheKey ?? (calling hook
   identity + deps)` and looks it up in `QueryCache`.
2. **Cache miss** → invoke `fetcher(ct)` once. If the returned `Task<T>` is
   already in the `RanToCompletion` state (synchronous/hot-cached fetchers,
   in-memory test doubles), unwrap it and return `Data(result)` on this same
   render — no `Loading` flash. If the task is faulted synchronously, return
   `Error(exception)` directly. Otherwise, return `Loading` synchronously and
   schedule the continuation on the thread pool.
3. **Cache hit, fresh** (age ≤ `StaleTime`) → return `Data(cached)` without
   fetching.
4. **Cache hit, stale** → return `Reloading(cached)` and kick off a refetch.
5. When an async fetch completes, the continuation marshals back to the UI
   dispatcher captured at hook registration, stores the result in the cache,
   and triggers re-render of every component subscribed to that key.
6. When `deps` change, **cancel** the in-flight token for the old key and
   re-evaluate from step 1 with the new key. The old result stays in cache.
7. On unmount, cancel the in-flight token and unsubscribe from the cache key.
   If this was the last subscriber, start the `CacheTime` eviction timer. A
   fetch that was already cached before unmount remains in cache until the
   timer expires; a fetch still in-flight at unmount is cancelled and its
   result is dropped (see D15).

### 6.3 Example

```csharp
class UserProfile : Component<UserProfileProps>
{
    public override Element Render()
    {
        var user = UseResource(
            fetcher: ct => Api.GetUserAsync(Props.UserId, ct),
            deps: [Props.UserId]);

        return user switch
        {
            AsyncValue<User>.Loading          => Skeleton().Height(120),
            AsyncValue<User>.Data(var u)      => VStack(Heading(u.Name), Text(u.Email)),
            AsyncValue<User>.Reloading(var u) => VStack(Heading(u.Name), Text(u.Email)).Opacity(0.6),
            AsyncValue<User>.Error(var ex)    => Text($"Failed: {ex.Message}").Foreground(Red),
        };
    }
}
```

See §5.1 for when `.Match()` is the better tool (explicit
`Reloading`-as-`Data` fallback; cold paths where allocations don't matter).

### 6.4 Design points

- `deps` is `object[]` to match `UseEffect` / `UseMemo` convention from spec
  009. Equality uses `ValueEqualityComparer` (same as `UseMemo`).
- **Threading.** `Render()` runs on the UI thread (enforced by
  `RenderContext.AssertUIThread`, `RenderContext.cs:17-22`). The hook invokes
  `fetcher(ct)` *from* the UI thread, but the fetcher body must do its actual
  work on the thread pool (e.g., `Task.Run`, `HttpClient.SendAsync`). The hook
  captures the UI dispatcher at registration time and uses it to marshal the
  continuation — DataGrid's per-caller `DispatcherQueue.TryEnqueue` pattern
  (`DataGridComponent.cs:54`) moves into the hook, so consumers no longer
  hand-roll it.
- **Sync-complete fast path.** A fetcher that returns an already-completed
  `Task<T>` (in-memory cache, pre-seeded test data, `ValueTask`-fast-path)
  resolves to `Data(result)` inside the *same* render that created the hook.
  No intermediate `Loading` state, no flicker. The transition `Loading →
  Data` happens across two renders only when the fetch is actually
  asynchronous.
- **Re-render short-circuit.** When a cache entry changes, subscribed
  components re-render, but the hook compares the new `AsyncValue<T>` against
  the last value it returned (record equality) and skips re-render if equal.
  This matches the reconciler's memoization conventions and keeps
  background-refresh churn from causing render storms.
- The `CancellationToken` passed to the fetcher is cancelled when (a) `deps`
  change, (b) the component unmounts, or (c) the cache entry is manually
  invalidated.
- `fetcher` may throw; the thrown exception becomes `Error(exception)`. We
  do not special-case `TaskCanceledException` — if the token fires, the
  subscriber has already gone away, so the result is dropped silently.

---

## 7. `UseInfiniteResource` — paginated fetch

This is the generalization of `DataPageCache<T>` that DataGrid uses today.
The cache, LRU eviction, and `BlockLoaded` event move into a shared
implementation; `UseInfiniteResource` is the hook face of it.

### 7.1 Signature

```csharp
public InfiniteResource<TItem> UseInfiniteResource<TItem, TCursor>(
    Func<TCursor?, CancellationToken, Task<Page<TItem, TCursor>>> fetchPage,
    object[] deps,
    InfiniteResourceOptions? options = null);

public sealed record Page<TItem, TCursor>(
    IReadOnlyList<TItem> Items,
    TCursor? NextCursor,
    int? TotalCount = null);

public sealed class InfiniteResource<TItem>
{
    public IReadOnlyList<TItem?> Items { get; }   // flattened, null = placeholder slot
    public int? TotalCount { get; }
    public int EstimatedRemaining { get; }
    public LoadState LoadState { get; }           // Loading | Idle | EndOfList | Error(e)
    public bool HasMore { get; }

    // Pull-model access for virtualized controls (LazyVStack, VirtualList,
    // TreeView, DataGrid). Returns the item if its page is cached, or null
    // if the slot is still loading. Calling ItemAt on an unloaded index
    // triggers (or coalesces into) a fetch for the containing page.
    public TItem? ItemAt(int index);
    public void EnsureRange(int firstIndex, int lastIndex);

    public void FetchNext();
    public void Retry();
    public void Refresh();                         // invalidate cache, refetch from page 1
}

public abstract record LoadState
{
    public sealed record Loading : LoadState;
    public sealed record Idle : LoadState;
    public sealed record EndOfList : LoadState;
    public sealed record Error(Exception Exception) : LoadState;
}
```

### 7.2 Behavior

1. On first render, returns `Items = []`, `LoadState = Loading`, and
   schedules the first page fetch with `cursor = null`.
2. When a page completes, appends its items to `Items`, stores `NextCursor`,
   and sets `LoadState = HasMore ? Idle : EndOfList`.
3. `FetchNext()` is a no-op if already fetching or `HasMore` is false.
   Otherwise fires a new fetch with the last-known cursor.
4. **Placeholder slots**: `Items` is length `LoadedItemCount + placeholdersForInflightPages`.
   Consumers can treat `null` as "a row that will appear soon" (DataGrid uses
   this exact shape today — see `DataGridComponent.cs:490`).
5. **Pull-model access**: `ItemAt(i)` returns the item at `i` if its page is
   loaded, else `null` and schedules a fetch for the containing page (coalesced
   with any in-flight request for the same page). `EnsureRange(first, last)`
   is the batched form used by virtualized controls when the viewport shifts.
   Both are no-ops past the known end of the list.
6. **Deps change** → cancel in-flight pages, clear `Items`, restart from
   page 1. Previous pages stay in cache under the old key for fast back-nav.
7. **`Refresh()`** → keep deps, but invalidate the cache entry and refetch.

### 7.3 Adapter for `IDataSource<T>`

The existing data-source contract drops in with one helper:

```csharp
public static class DataSourceResource
{
    public static InfiniteResource<T> UseDataSource<T>(
        this RenderContext ctx,
        IDataSource<T> source,
        DataRequest request,
        InfiniteResourceOptions? options = null)
        => ctx.UseInfiniteResource<T, string>(
            fetchPage: async (cursor, ct) =>
            {
                var req = request with { ContinuationToken = cursor };
                var page = await source.GetPageAsync(req, ct);
                return new Page<T, string>(page.Items, page.ContinuationToken, page.TotalCount);
            },
            deps: [source, request.Sort, request.Filter, request.Search]);
}
```

This means today's `GraphQLDataSource` and `ListDataSource` work with the
new hooks without modification.

### 7.4 Consumption from virtualized controls

The hook intentionally serves *all* virtualized list controls in the
framework, not just `DataGrid`. The existing controls split on consumption
shape:

| Control | Shape | How it consumes `InfiniteResource<T>` |
|---|---|---|
| `DataGrid` | Pull (range-based) | `EnsureRange(first, last)` on viewport change; `ItemAt(i)` during row render |
| `LazyVStack` / `LazyHStack` | Pull (per-index `viewBuilder`) | `ItemAt(i)` inside the view builder; `null` → render placeholder |
| `VirtualListComponent` | Pull (`RenderItem(index)`) | Same as `LazyVStack` |
| `TreeView` (when it lands) | Pull (per-node lazy expand) | Each expanded node holds its own `InfiniteResource<TChild>` |
| `StackPanel` / `VStack` | Eager | Use `UseResource<IReadOnlyList<T>>`, not `UseInfiniteResource`. The whole list is `AsyncValue`-matched at the container level; no placeholder story per-item. |

"Render a placeholder when the slot is still loading" collapses to the same
contract everywhere: the view builder gets `T?`, and `null` means *render
your loading presentation* (skeleton, shimmer, spinner — the control's
choice). No per-control async-awareness is required beyond that.

Non-virtualized containers (`StackPanel`, `VStack`, `HStack`) deliberately
don't participate in the infinite-resource contract — for a short list, the
right shape is `UseResource<IReadOnlyList<T>>` and a single
`AsyncValue.Match` at the container, not a per-slot placeholder dance.

---

## 8. `UseMutation` — async writes with optimistic updates

Generalizes `DataGridState.BeginAsyncCommit` / `CompleteAsyncCommit` /
`FailAsyncCommit` (`Reactor/Controls/DataGrid/DataGridState.cs:1028-1077`).

### 8.1 Signature

```csharp
public Mutation<TInput, TResult> UseMutation<TInput, TResult>(
    Func<TInput, CancellationToken, Task<TResult>> mutator,
    MutationOptions<TInput, TResult>? options = null);

public sealed record MutationOptions<TInput, TResult>(
    Action<TInput>? OnOptimistic = null,
    Action<TResult, TInput>? OnSuccess = null,
    Action<Exception, TInput>? OnError = null,
    string[]? InvalidateKeys = null);   // query-cache keys to invalidate on success

public sealed class Mutation<TInput, TResult>
{
    public bool IsPending { get; }
    public Exception? Error { get; }
    public TResult? LastResult { get; }

    public Task<TResult> RunAsync(TInput input);
    public void Reset();
}
```

### 8.2 Example

```csharp
var save = UseMutation<Employee, Employee>(
    mutator: (e, ct) => Api.UpdateEmployeeAsync(e, ct),
    options: new(
        OnOptimistic: e => cache.Patch(e.Key, e),
        OnError: (ex, e) => cache.Revert(e.Key),
        InvalidateKeys: ["employees.list"]));

Button("Save", () => save.RunAsync(edited)).IsEnabled(!save.IsPending);
```

### 8.3 Why a separate hook, not "write support on UseResource"

Reads and writes have different identities, different lifetimes, and different
cardinalities (one read feeds render; many writes flow through a single
button over the session). Merging them produces an awkward "sometimes it's a
value, sometimes it's a callback" API. Both TanStack Query (`useQuery` vs
`useMutation`) and SWR (`useSWR` vs `useSWRMutation`) reached the same
conclusion. See D7.

---

## 9. The Query Cache

### 9.1 Shape

```csharp
public sealed class QueryCache
{
    public bool TryGet<T>(string key, out CacheEntry<T> entry);
    public void Set<T>(string key, T value, TimeSpan staleTime, TimeSpan cacheTime);
    public void Invalidate(string key);
    public void InvalidatePattern(string keyPrefix);
    public void Clear();
    public event Action<string>? EntryChanged;
}

public sealed record CacheEntry<T>(
    T Value,
    DateTime FetchedAt,
    TimeSpan StaleTime,
    int SubscriberCount);
```

### 9.2 Key derivation

Cache keys are `$"{CallerHookId}/{DepsHash}"` unless the user overrides via
`ResourceOptions.CacheKey`. `CallerHookId` is derived at hook-registration
time from the component type + hook index — stable across renders, unique
per call-site. `DepsHash` is the existing `ValueEqualityComparer` hash
(spec 009 §4.2).

Two different components calling the *same* fetcher will get different cache
keys unless they supply an explicit `CacheKey`. This matches TanStack Query's
philosophy (queries are identified by key, not by function).

### 9.3 Scope

The cache is a `Context<QueryCache>` (spec 009 §1). `ReactorApp.Run` installs
a default instance at the root; tests can override it with a fresh instance
per test, and apps can install nested caches if they want scoping (e.g., a
"logged-in user" cache that wipes on sign-out).

### 9.4 Invalidation strategies

- **Manual** — `cache.Invalidate("employees.list")` after a mutation.
- **Declarative** — `MutationOptions.InvalidateKeys` does the above for you.
- **Pattern** — `cache.InvalidatePattern("employees.")` clears all keys
  starting with `employees.`.
- **TTL** — `StaleTime` / `CacheTime` on `ResourceOptions`.
- **Focus revalidation** — *deferred to phase 3.* TanStack Query refetches
  queries when the window regains focus. For a WinUI desktop app, the
  equivalent is window-activated; we'll evaluate whether users actually want
  this.

---

## 10. `Pending` element — optional bubble-up fallback

Some UI scenarios genuinely want "render this entire subtree as a fallback
while *anything* inside it is loading" — e.g., a master-detail view where the
detail pane should skeleton until all its sub-queries resolve.

The ADT approach gives you this explicitly at each call site, which is
usually what you want. For the "I just want one spinner for the whole tree"
case, we offer an opt-in element:

```csharp
Pending(
    fallback: Skeleton().Height(400),
    child:    VStack(
                  Heading($"User: {user.Name}"),
                  Component<UserDetails>(new { Props.UserId }),
                  Component<UserActivity>(new { Props.UserId })));
```

### 10.1 Mechanism

`Pending` consumes a `PendingScope` context and provides a fresh one to its
subtree. Every `UseResource` / `UseInfiniteResource` under that subtree
registers with the nearest ancestor scope. `Pending` renders `fallback`
instead of `child` iff any registered resource is `Loading` (not
`Reloading` — that's explicitly the "we have stale data, show it" case).

### 10.2 Why this is *not* Suspense

- It does not unwind rendering. The subtree renders normally; we just choose
  which rendered tree to show.
- It does not require reconciler changes — only an ambient context.
- It is opt-in. The ADT is the default, `Pending` is the convenience.
- It composes with the ADT: inside a `Pending`, individual components can
  still match on `AsyncValue` and override the bubble-up for specific
  children.

See D9 for why we rejected making this the default.

---

## 11. DataGrid Integration

DataGrid today owns its async machinery privately. After this spec lands, it
will *consume* the shared infrastructure. The migration is straightforward
because the shapes already line up:

| DataGrid internal (today) | Becomes (after) |
|---|---|
| `DataPageCache<T>` with `_blocks`, `_inflight`, `_lruOrder` | Private implementation moves behind `UseInfiniteResource` |
| `CacheBlock<T>.LoadingBlock(index)` | `InfiniteResource<T>.Items[i] == null` placeholder slot |
| `BlockLoaded` event | Re-render via hook subscription — no event needed |
| `state.EnsureRangeLoaded(first, last)` (`DataGridComponent.cs:391`) | `resource.EnsureRange(first, last)` on `InfiniteResource` |
| `BeginAsyncCommit` / `CompleteAsyncCommit` / `FailAsyncCommit` | `UseMutation` with `OnOptimistic` / `OnSuccess` / `OnError` |
| 32ms settle timer (`DataGridComponent.cs:56-90`) | Stays in DataGrid — it's a rendering concern, not a data one |

### 11.1 The new `DataGridComponent` shape

```csharp
public override Element Render()
{
    var page = this.UseDataSource(Props.Source, Props.Request);

    // Scroll settle, placeholder rendering, virtualization — unchanged.
    // Just plug `page.Items`, `page.TotalCount`, `page.EnsureRange` in.
    ...
}
```

### 11.2 What we do *not* change

- `IDataSource<T>`, `DataRequest`, `DataPage<T>`, `RowKey` — all unchanged.
- `DataSourceCapabilities` flags (ServerSort, ServerFilter, …) — unchanged.
- DataGrid's own visual rendering path — unchanged.
- `DataGridState`'s edit/validation/selection logic — unchanged.

### 11.3 Migration risk

Medium. The 32ms settle timer and the scroll-driven prefetch range are
subtle, and the existing `DataPageCache` has test coverage that must
continue to pass. Plan:

1. Implement `QueryCache` + `UseResource` standalone (no DataGrid dependency).
2. Implement `UseInfiniteResource` using the extracted cache logic, with
   parity tests against `DataPageCache`.
3. Port `DataGrid` to `UseInfiniteResource` behind a feature flag; run both
   paths in CI until parity is proven.
4. Delete `DataPageCache`.

### 11.4 Knock-on win

Once `UseInfiniteResource` exists, `LazyVStack`, `TreeView`, and any future
virtualized control gets paginated-HTTP support "for free". The `ValueList`
component in the regedit sample (`samples/apps/regedit/Components/ValueList.cs`)
would be the first candidate.

---

## 12. Use Cases We Cover Well

### 12.1 "Fetch a thing" (the 80% case)

Every dialog that loads one record. `UseResource` with `deps: [id]`. Done.

### 12.2 Paginated HTTP-backed lists

DataGrid is the motivating case, but the pattern applies anywhere the user
scrolls and more data arrives. `UseInfiniteResource` with cursor-based paging.

### 12.3 Search-as-you-type

`UseResource` with `deps: [query]` plus a debounced `query` in `UseState` gives
you cancellation of stale searches for free. Today's code has to hand-roll
the debounce *and* the cancellation; this collapses to one hook.

### 12.4 Optimistic writes with rollback

`UseMutation` with `OnOptimistic` / `OnError`. Replaces the ad-hoc
`BeginAsyncCommit` machinery in DataGrid and generalizes it to any form.

### 12.5 Shared state across siblings

Three sibling components each want `currentUser`. All three call
`UseResource(getCurrentUser, [])` — one HTTP call, three re-renders, no
prop-drilling, no context-provider ceremony.

### 12.6 Back/forward navigation

User opens a list, clicks an item, goes back. Without a cache, the list
refetches. With the query cache (and default 5-minute `StaleTime`), the list
appears instantly.

### 12.7 Tests

Tests can install a mock `QueryCache` via `Context<T>` override and pre-seed
it with `Data(value)` entries, avoiding the async dance entirely.

---

## 13. Where This Doesn't Fit

Being honest about where the primitive is wrong:

### 13.1 Streaming / `IAsyncEnumerable`

`UseResource` models a single-valued future. Streams (server-sent events,
`IAsyncEnumerable<T>`, SignalR, WebSocket push) need a different hook —
`UseStream<T>` — with different semantics (accumulated buffer, reset on
disconnect, optional merge strategy). Trying to fit streams into
`AsyncValue<T>` gives you an awkward "is this the final value or just the
latest?" question at every call site. **Out of scope; separate spec.**

### 13.2 Long-running operations with progress

A file upload, a build job, a large export. `AsyncValue<T>` has no place to
report "45% complete". For these, the right shape is an `UseOperation<T,
TProgress>` hook or a first-class progress observable. **Out of scope;
`AsyncValue<T>` stays intentionally small.**

### 13.3 Bidirectional sync (conflict resolution)

CRDTs, OT, "this field was edited by another user". Query-cache invalidation
assumes the server is authoritative and the client reconciles by refetch.
Anything more sophisticated needs a real sync engine (Fluid, Automerge,
Yjs). **Out of scope.**

### 13.4 Fire-and-forget side effects

`Api.LogMetricAsync()` in a click handler doesn't want a hook, a cache, or
an `AsyncValue`. It wants a naked `async void` call or a `Task.Run`. Forcing
these through `UseMutation` would be over-engineering. The guidance:
mutations only when the UI cares about the result or the pending state.

### 13.5 Transactional multi-call flows

"Create order, then create line items, then commit, roll back all on
failure." `UseMutation` wraps a single call. Multi-step transactions need
either a composite fetcher (one `async` method that does all three and is
treated as one write) or a workflow/state-machine primitive. We lean toward
the former — if you need transactional semantics, put them in the
server-side call — but this is an area where the hook will tempt people to
build chained `UseMutation` trees that don't actually have transactional
semantics. Document explicitly.

### 13.6 Server-pushed invalidation

If the server sends "this row changed, refetch it", today's design needs a
bit of glue: a long-running connection that calls
`cache.Invalidate(key)`. Workable, but not turnkey. A future spec can define
an `IDataSource<T>.DataChanged` → automatic-invalidation bridge, building
on the existing `IObservableDataSource<T>`.

### 13.7 Component-tree-local cache scoping

The query cache is a `Context<QueryCache>`, so you can shadow it in a
subtree — but the UX of "this dialog has its own cache" is poorly
understood. If users start nesting caches heavily to get scoping, we've
built the wrong abstraction. Ship with one default cache; let patterns
emerge.

### 13.8 Non-idempotent reads

`UseResource` assumes the fetcher is idempotent — it will be called multiple
times for the same inputs (cache miss, focus revalidation, retry). If your
`GET /api/random-quote` genuinely returns different data each call, caching
is actively wrong and you want `UseState` + an explicit button handler.

---

## 14. Design Decisions

### D1. ADT over Suspense

**Decision:** `AsyncValue<T>` sealed record hierarchy, matched with `switch`,
is the primary surface. No thrown-`Task` / Suspense.

**Rationale:** C# exhaustive-match warnings on sealed records give compile-time
guarantees Suspense can't. The reconciler stays dumb (no fiber-style unwind
and retry). The mental model matches what Reactor devs already write. The
ergonomic gap vs. React Suspense is small once you've written the switch
expression (see §5.1).

**Considered:** A throw-Task mechanism where the reconciler catches
`AwaitException` and re-renders when the task completes. Rejected: requires
non-trivial reconciler changes, doesn't compose with C# async/await
semantics, and Riverpod / Compose have shown the ADT path scales fine to
large apps.

### D2. Four states, not three

**Decision:** `Loading | Data | Error | Reloading`.

**Rationale:** `Reloading` carries `Previous`, enabling stale-while-revalidate
as a type-level concept. Components choose whether to dim, show a spinner,
or just show the stale value. If we collapsed it into `Data + IsStale`, the
distinction would hide in a property and exhaustive-match would miss it.

### D3. No `Initial` / `NotAsked` state

**Decision:** There is no `NotAsked` state (unlike Elm's `RemoteData`). Every
`UseResource` starts fetching on first render (unless `RefetchOnMount: false`).

**Rationale:** If you don't want to fetch, don't mount the component, or use
a separate boolean-gated pattern. Adding `NotAsked` doubles the state space
to four-plus-one with no real use case inside a hook-based framework.

### D4. `deps` is `object[]`, not a typed tuple

**Decision:** Match the existing `UseEffect` / `UseMemo` signatures from spec 009.

**Rationale:** Consistency beats type-safety here. Reactor hooks all share this
shape; introducing a typed variant for `UseResource` would fragment the API.
If a dev wants type safety, they wrap in a custom hook.

### D5. Query cache is a `Context`, not a static singleton

**Decision:** `QueryCache` is installed via context (spec 009 §1), with a
default instance at the app root.

**Rationale:** Testability is the driver — unit tests need a fresh cache per
test. Also leaves the door open for scoped caches (per-window, per-tenant)
without an API change.

### D6. Key derivation from hook call-site + deps, not just deps

**Decision:** Default cache key is `$"{CallerHookId}/{DepsHash}"`.

**Rationale:** Two different components fetching with the same deps (say,
`[userId]`) usually want *separate* cache entries, because they fetch
different things. Forcing them to share a cache by default leads to subtle
bugs. Devs who *do* want sharing pass an explicit `CacheKey: "user"`.

This differs from TanStack Query (which uses dev-supplied keys) but matches
our hook-identity model better. Worth revisiting after real usage.

### D7. `UseMutation` is separate from `UseResource`

**Decision:** Writes are a different primitive.

**Rationale:** Reads return a value; writes return a trigger function. Reads
subscribe to a cache key; writes sometimes invalidate several. Reads are
one-per-component; writes are one-per-button. Merging them produces a
sometimes-value-sometimes-callback API that's harder to teach.

### D8. Cancellation tokens are hook-owned, not exposed

**Decision:** `fetcher: (CancellationToken) => Task<T>`. The dev uses the
token inside the fetcher but does not construct or dispose it.

**Rationale:** The single most common bug in today's `UseEffect`-based async
is forgetting to pass and observe a cancellation token. Making the token
mandatory in the fetcher signature (can't be omitted, since it's the only
parameter) fixes this by construction.

### D9. `Pending` is opt-in, not the default

**Decision:** ADT matching at each call site is the default; `Pending` is an
element wrapper you opt into.

**Rationale:** Bubble-up fallback is the wrong default for Reactor's audience.
Desktop-app developers come from WPF/WinForms, where local loading indicators
are normal and "entire page disappears into a spinner" is jarring. Making
`Pending` opt-in means you have to consciously choose the blunter UX.

### D10. No automatic retry by default

**Decision:** `RetryCount = 0` by default. Devs opt into retries.

**Rationale:** Automatic retries hide bugs. A failing endpoint should surface
an error on the first failure, not on the fourth. Retries are the right
default for *transient* network failures, not for API errors, and we can't
tell them apart from the hook. Make the behavior explicit.

### D11. TTLs default to TanStack Query's values

**Decision:** `StaleTime` defaults to `TimeSpan.Zero`, `CacheTime` to 5
minutes.

**Rationale:** Zero stale-time means "always refetch on mount, but dedup
concurrent requests", which is the safest default (no unexpected staleness).
Five-minute cache time handles back/forward nav well. Numbers match
TanStack Query's defaults because the industry has converged on them.

### D12. Cache is in-process only

**Decision:** No persistence layer. Lost on app exit.

**Rationale:** Persistence has platform-specific gotchas (disk quota,
encryption of sensitive data, cache-poisoning risks). Solving it adds a lot
of surface area for little win in the common case. If a consumer really
wants persistence, they supply a custom `QueryCache` via context.

### D13. `UseInfiniteResource` is cursor-based, not offset-based

**Decision:** `fetchPage` takes `TCursor?`, not `int skip`.

**Rationale:** Cursor pagination works for both cursor-native APIs (GraphQL
Relay, OData) and offset-native APIs (where `TCursor = int`). Offset-only
doesn't work in reverse. Matches `IDataSource<T>.GetPageAsync`'s existing
`ContinuationToken`.

### D14. Resources are not observable in the Rx sense

**Decision:** `UseResource` returns an `AsyncValue<T>` snapshot per render, not
an `IObservable<AsyncValue<T>>`.

**Rationale:** Reactor's reactivity model is "re-render on state change", not
"subscribe to an observable". Keeping resources inside that model keeps
debugging simple and matches every other hook. If someone needs Rx, they can
bridge at the fetcher level.

### D15. Unmount cancels in-flight fetches; no `KeepAliveOnUnmount`

**Decision:** When the last subscriber of a cache key unmounts, any in-flight
fetch for that key is cancelled and its partial result is dropped. Already-
completed entries stay in the cache until `CacheTime` expires; in-flight work
does not. There is no opt-in `KeepAliveOnUnmount` flag.

**Rationale:** Tab switches and navigations where "the fetch should survive"
are better modelled by hoisting the data owner above the tab boundary — the
`UseResource` lives in the persistent parent, and both tabs read from the
same cache entry. That pattern is already idiomatic in Reactor (context
providers, shared parent state) and doesn't need a hook-level knob.

A `KeepAliveOnUnmount` flag would also raise a subtle reconnection problem:
when a component remounts while a detached fetch is still pending, the hook
has to identify and attach to the in-flight task from cache. Doable, but it
trades the current crisp lifecycle (subscriber-count drops to zero →
cancel) for a fuzzier one (cancel *unless someone remounts first*), and the
observable behavior now depends on precise timing of unmount-and-remount.
Not worth the complexity until a concrete use case requires it.

**Considered:** A per-hook `KeepAliveOnUnmount: true` that detaches the fetch
and lets it populate the cache even with zero subscribers. Rejected for v1
on the grounds above. Re-evaluate if real usage produces a pattern that
genuinely can't be solved by hoisting.

### D16. Cache keys are flat strings, not structured arrays

**Decision:** `ResourceOptions.CacheKey` is `string?`, and
`MutationOptions.InvalidateKeys` is `string[]`. No array-keyed (TanStack
`['user', userId, 'profile']`) variant.

**Rationale:** The main argument for structured keys is prefix-invalidation
ergonomics. `QueryCache.InvalidatePattern(string prefix)` already covers
that use case without committing the entire API to array-shaped keys, and
flat strings compose cleanly with interpolation (`$"user/{id}/profile"`).
If structured keys prove necessary later, they're an additive change —
flat strings are the conservative v1 baseline.

### D17. Scroll preservation on `Refresh()` is a DataGrid concern

**Decision:** `InfiniteResource<T>.Refresh()` handles data reload only. The
viewport-level "preserve scroll across refresh" UX sits in the consuming
control (DataGrid today, LazyVStack / TreeView when they land).

**Rationale:** Scroll position is a rendering / viewport concern that varies
per control (row height, virtualization window, selection restore,
measurement timing). Pushing it into the hook would make `InfiniteResource`
care about things it shouldn't — and each control would still want to
override the policy anyway. DataGrid already owns analogous viewport work
for its 32ms settle timer (`DataGridComponent.cs:56-90`); scroll-preserve
on refresh is a small extension of that.

**Hook contract needed to support this:** `InfiniteResource<T>` exposes
(a) stable `Items` length during refresh where the server's total count
hasn't changed (the consumer may want to keep showing current rows until
the new page 1 lands — phase 3 can decide whether that's opt-in via a
`RefreshMode` enum or the default), (b) a discrete `LoadState` transition
on refresh start so consumers can snapshot scroll state, and (c) item
identity via `RowKey` (spec 017) so re-arrivals can be matched against
pre-refresh rows.

### D18. Persisted resources serialize `Data` only

**Decision:** When a resource hook is combined with spec 009's `PersistState`
/ `UsePersisted` mechanism, only the `Data(Value)` case is serialized. On
remount, the hook rehydrates to `Data(persistedValue)` and then behaves
like a cache hit: fresh (skip fetch) if within `StaleTime`, stale (return
`Reloading(persisted)` + refetch) otherwise.

**Rationale:** `Loading` and `Reloading` are tied to a `CancellationToken`
and an in-flight `Task<T>` — neither survives an unmount, let alone a
process restart. `Error` is rarely what you want to replay. `Data` is the
only state with meaningful continuity. Persisting anything else invites
subtle bugs ("why am I stuck in Loading forever after a reload?").

---

## 15. Open Questions

1. **Focus revalidation (window-activated refetch).** TanStack Query's
   signature feature: on `CoreWindow.Activated` (and
   `CoreApplication.Resuming`), iterate the cache and refetch anything
   past its `StaleTime`, deduping concurrent requests. Long-running
   dashboards (HeadTrax-style tools left open all day) benefit strongly;
   short-session tools (regedit) benefit little and risk unwanted
   background traffic. Tentative plan: defer to phase 4 behind a feature
   flag, off by default, with `ResourceOptions.RefetchOnWindowFocus` as
   the per-query opt-out. Throttle default ~30s so rapid Alt-Tabbing
   doesn't thrash. Revisit when we have real-app usage data.

(Other questions from the original draft — structured vs flat cache keys,
scroll preservation on `Refresh()`, persistence story for `AsyncValue<T>`,
first dogfood target — have been resolved; see D16, D17, D18, and §16
Phase 1 respectively.)

---

## 16. Implementation Phases

### Phase 1 — Foundation (no DataGrid dependency)

1. `AsyncValue<T>` record hierarchy + `.Match()` convenience.
2. `QueryCache` with TTL, invalidate, context-installable.
3. `UseResource<T>` hook with cancellation, stale-while-revalidate.
4. Unit tests: cache hit/miss, deps-change cancellation, stale-while-revalidate
   transitions, error path, sync-complete fast path (§6.2, §6.4).
5. **TestApp dogfood — `AsyncValueSamples` page.** A dedicated sample page
   added to the existing TestApp, not a new app. The page is structured as
   layered scenarios that can be exercised interactively and captured in
   the TestApp's existing snapshot tests:
   - **1a. Deterministic fake fetcher.** `Task.Delay(ms)` + configurable
     succeed / fail / cancel buttons. Validates each `AsyncValue` state
     transition visually.
   - **1b. Sync-complete fetcher.** Returns `Task.FromResult` directly.
     Confirms no `Loading` flash on first render (D16 neighbor).
   - **1c. Deps-change cancellation.** Text input drives `deps`; type fast,
     confirm only the last request's result lands.
   - **1d. Two siblings, one cache key.** Validates dedup + shared re-render.
   - **1e. Cache hit across remount.** Unmount/remount within `CacheTime`,
     confirm instant `Data` (or `Reloading` past `StaleTime`).

**Exit criteria:** A dev can write a single-fetch component end-to-end using
only `UseResource`, no `UseEffect + UseState` plumbing. All five
`AsyncValueSamples` scenarios pass in the TestApp's snapshot suite.

### Phase 2 — Infinite (still TestApp-only)

1. `UseInfiniteResource<TItem, TCursor>` on top of the phase-1 cache.
2. `DataSourceResource.UseDataSource` adapter for `IDataSource<T>`.
3. Pull-model API (`ItemAt`, `EnsureRange`) per §7.1.
4. Parity tests against `DataPageCache<T>`: same LRU eviction, same
   placeholder semantics, same cancellation on deps change.
5. **TestApp dogfood — extend `AsyncValueSamples`.** Same page, more layers:
   - **2a. `LazyVStack` backed by `UseInfiniteResource`** — the smallest
     virtualized consumer. Validates pull-model via `ItemAt`, placeholder
     rendering on `null` slots, and scroll-driven page fetch.
   - **2b. Search-as-you-type over an infinite list.** Deps-change + pull
     model combined; confirms that stale pages cancel cleanly mid-scroll.
   - **2c. `Refresh()` with scroll preservation** (consumer-side — LazyVStack
     captures scroll before refresh, restores after; see D17).
   - **2d. Port the regedit `ValueList`** (in-memory, low-risk) as the
     first real-world consumer.

**Exit criteria:** `UseInfiniteResource` passes every scenario
`DataPageCache<T>` passes today (parity tests) plus the TestApp scenarios
above. regedit `ValueList` is on the new hook. Still no DataGrid
dependency at this point — HeadTrax `EmployeeGrid` port happens in phase 3
alongside the DataGrid migration.

### Phase 3 — Mutations & DataGrid migration

1. `UseMutation<TIn, TOut>` with optimistic / error callbacks and
   `InvalidateKeys`.
2. Port `DataGridComponent` to `UseInfiniteResource` behind a feature flag.
3. Run both the old (`DataPageCache`) and new (`UseInfiniteResource`)
   paths in CI; assert identical reconciliation results on the selfhost
   test suite.
4. Port `DataGridState.BeginAsyncCommit` family to `UseMutation`.
5. Port HeadTrax `EmployeeGrid` — the first real-HTTP, real-DataGrid
   consumer — once the feature flag is enabled by default.
6. Delete `DataPageCache` once HeadTrax has been stable on the new path
   through at least one release cycle.

**Exit criteria:** No public API changes to DataGrid; `DataPageCache.cs`
deleted; all selfhost DataGrid tests green on the new path; HeadTrax
`EmployeeGrid` ported and in production.

### Phase 4 — Polish

1. `Pending` element.
2. Focus revalidation (per §15 Q1), feature-flagged and off by default,
   with `ResourceOptions.RefetchOnWindowFocus` per-query opt-out.
3. **Analyzer diagnostics — expanded work item.** Resource hooks inherit
   the rules-of-hooks problem from spec 009, but the analyzer coverage is
   thin today across *all* hooks, not just these. Treat this as a broader
   deliverable — tracked in `Reactor.Analyzers` under new `REACTOR_HOOKS_*`
   diagnostic IDs:
   - **Conditional hook calls.** `UseResource` / `UseState` / any
     `Use*` inside `if`, `for`, `while`, `try`, or early-return branches.
   - **Out-of-order hook calls across renders.** Hook call-site ordering
     differs between two render paths of the same component.
   - **Missing deps.** Value captured in the `fetcher` (or
     `UseEffect` / `UseMemo` lambda) that isn't in `deps`.
   - **Non-stable deps.** New object literal, new array, or new lambda
     passed as a dep each render (causes unnecessary refetches /
     re-runs).
   - **Hook called outside `Render()` or a custom-hook function.**
   - **`UseResource` with a non-idempotent fetcher** (heuristic —
     fetcher name matches `GenerateRandom`, `Create`, `Post`, etc.).

   Scope note: the first three diagnostics are pre-existing gaps that
   should ship before or alongside this spec so resource hooks aren't
   the first concrete motivator. Worth filing as its own tracking
   issue / spec amendment if the list keeps growing.
4. Docs: porting guide from `UseEffect + UseState` to `UseResource`; a
   separate cookbook entry for infinite scroll.
5. Evaluate `UseStream<T>` scope and file a follow-up spec if warranted.

---

## Appendix A — API cheat-sheet

```csharp
// Read a single value.
AsyncValue<User> user = UseResource(
    fetcher: ct => Api.GetUserAsync(id, ct),
    deps: [id]);

// Read a paginated list.
InfiniteResource<Employee> page = UseInfiniteResource<Employee, string>(
    fetchPage: (cursor, ct) => Api.ListEmployees(cursor, ct),
    deps: [query, department]);

// Adapt an IDataSource<T>.
InfiniteResource<Row> rows = this.UseDataSource(source, request);

// Write with optimistic update.
Mutation<Employee, Employee> save = UseMutation(
    mutator: (e, ct) => Api.UpdateAsync(e, ct),
    options: new(
        OnOptimistic: e => cache.Patch(e),
        OnError:      (ex, e) => cache.Revert(e),
        InvalidateKeys: ["employees.list"]));

// Manual cache control.
var cache = UseContext(AppContexts.QueryCache);
cache.Invalidate("employees.list");

// Bubble-up fallback (opt-in).
Pending(fallback: Skeleton(), child: complexSubtree);
```

## Appendix B — Comparables cited

| Framework | Primitive | How it maps to our design |
|---|---|---|
| TanStack Query | `useQuery`, `useInfiniteQuery`, `useMutation`, query cache | Closest overall match; defaults stolen |
| Solid.js | `createResource`, `<Suspense>` | ADT + cancellation semantics |
| Android Paging 3 | `Pager`, `PagingSource`, `PagingData`, `LoadState` | `UseInfiniteResource` shape |
| Flutter + Riverpod | `AsyncValue<T>`, `.when()` | The ADT |
| Elm | `RemoteData a e` | Conceptual ancestor of the ADT |
| Jetpack Compose | `produceState`, `LaunchedEffect`, sealed `UiState` | Hook + ADT pattern |
| SwiftUI | `.task(id:)`, `AsyncImage.phase` | Hook-owned cancellation |
| React | `<Suspense>`, `use()`, `useTransition` | Considered and rejected (D1) |
| SWR | `useSWR`, `useSWRMutation` | Cache + dedup story |

## Appendix C — Files affected

### New

- `Reactor/Core/AsyncValue.cs` — the ADT
- `Reactor/Core/Hooks/UseResource.cs`
- `Reactor/Core/Hooks/UseInfiniteResource.cs`
- `Reactor/Core/Hooks/UseMutation.cs`
- `Reactor/Core/QueryCache.cs`
- `Reactor/Elements/Pending.cs`

### Modified

- `Reactor/Core/RenderContext.cs` — register new hooks alongside `UseState` /
  `UseEffect` / `UseMemo`
- `Reactor/Controls/DataGrid/DataGridComponent.cs` — consume
  `UseInfiniteResource`
- `Reactor/Controls/DataGrid/DataGridState.cs` — lift `BeginAsyncCommit` into
  `UseMutation`

### Deleted (phase 3)

- `Reactor/Data/DataPageCache.cs` — replaced by `UseInfiniteResource`
  internals

### Unchanged

- `Reactor/Data/IDataSource.cs` — contract stays identical
- `Reactor/Data/DataRequest.cs`, `DataPage.cs`, `RowKey.cs`

---

## Appendix D — Deferred: `UseStream<T>` (and why)

A fourth hook — `UseStream<T>(IAsyncEnumerable<T>)` surfacing each yielded value
through `AsyncValue<T>` — was sketched in an early draft. We **did not ship it**
in Phase 4 and do not plan to until a concrete consumer exists. The reasoning:

1. **Zero in-repo callers.** A survey of `samples/apps/*`, the HeadTrax demos,
   the regedit sample, the `A11yShowcase`, and the TestApp demos found no
   `IAsyncEnumerable`, no SignalR hub handler, and no SSE consumer. Shipping a
   hook for a use case that has zero concrete callers widens the hook surface
   area without paying rent.
2. **`UseResource` + `UseEffect` already handles one-offs.** A component that
   needs to consume a stream once can open it from `UseEffect`, pipe each
   value into `UseState`, and read through whichever hook is the display
   primitive. It's five lines of glue. When a second consumer appears and the
   glue starts to repeat, that's the signal to generalize.
3. **Stream semantics are richer than what the ADT encodes.** Real stream
   consumers need backpressure control, retry-on-disconnect policy, and
   replay-on-mount semantics — none of which fit cleanly into the existing
   `AsyncValue` four-state shape. A `UseStream<T>` that ignores those concerns
   would ship a deceiving name. If we revisit, the spec should be its own
   paper, not an appendix to this one.

If a pattern emerges (two or more real consumers repeat the same glue), file a
follow-up spec. Until then this appendix is the canonical record of the
decision.
