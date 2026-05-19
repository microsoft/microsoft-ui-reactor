# Async Resources — Implementation Reference

**Scope:** This paper walks the actual implementation of Reactor's async-resource
system as it exists on `feat/async-resources-phase1`. It is the companion to the
design spec at [`docs/specs/020-async-resources-design.md`](../specs/020-async-resources-design.md):
the spec defines *what* the system should do and *why*; this paper documents
*how* the code does it, state-by-state, and walks the threading and race-condition
analysis line by line.

Where the code diverges from the intent of the spec, or where the walkthrough
surfaced a latent bug, the issue is captured in a **BUG** or **GAP** box in the
relevant section. The paper always describes the *desired* state of the system;
the boxes record delta to current code so that a follow-up pass can close them.

**Files covered**

| File | Role |
|---|---|
| `src/Reactor/Core/AsyncValue.cs` | The four-state ADT every read hook returns |
| `src/Reactor/Core/InfiniteResource.cs` | Pull-model paginated view + `LoadState` + `Page` |
| `src/Reactor/Core/QueryCache.cs` | Process-wide ref-counted cache with TTL eviction |
| `src/Reactor/Core/FocusRevalidationService.cs` | Window-activation invalidation sweep |
| `src/Reactor/Core/ReactorFeatureFlags.cs` | Rollout flags (`FocusRevalidation`, `UseHookBasedPaging`) |
| `src/Reactor/Hooks/UseResource.cs` | Single-value fetch hook, owns `ResourceHookState<T>` |
| `src/Reactor/Hooks/UseInfiniteResource.cs` | Paginated hook, owns `InfiniteHookState<TItem, TCursor>` |
| `src/Reactor/Hooks/UseMutation.cs` | Write hook with optimistic / invalidate / callbacks |
| `src/Reactor/Hooks/PendingScope.cs` | Ref-counted loading set for bubble-up fallback |
| `src/Reactor/Hooks/Pending.cs` | Component that hosts a `PendingScope` and flips visibility |
| `src/Reactor/Data/DataSourceResourceExtensions.cs` | `IDataSource<T>` → `UseInfiniteResource` adapter |

---

## 1. Architectural overview

The async system is layered. Each layer has a narrow contract, and each
successive layer consumes only the layer below. This lets us reason about
threading locally:

```
          ┌──────────────────────────────────────────────────────────┐
          │  Render code (Component.Render)                          │
          │  returns an Element tree built from AsyncValue<T> matches│
          └──────────────────────┬───────────────────────────────────┘
                                 │  calls ctx.UseResource / UseInfiniteResource / UseMutation
          ┌──────────────────────▼───────────────────────────────────┐
          │  Hook layer                                              │
          │  • UseResourceCore          → ResourceHookState<T>       │
          │  • UseInfiniteResourceCore  → InfiniteHookState<I,C>     │
          │  • UseMutationCore          → MutationHookState<I,R>     │
          │  Owns: hook identity, CTS, dispatcher, rerender tick,    │
          │  PendingScope registration, focus enrollment.            │
          └──────────────────────┬───────────────────────────────────┘
                                 │  reads/writes string keys
          ┌──────────────────────▼───────────────────────────────────┐
          │  QueryCache                                              │
          │  ConcurrentDictionary<string, Slot>; Slot has per-key    │
          │  lock, ref-count, CacheEntry<T> payload, CacheTime,      │
          │  ZeroSubscribersAt. Single shared eviction Timer.        │
          └──────────────────────┬───────────────────────────────────┘
                                 │  EntryChanged event (key-scoped)
          ┌──────────────────────▼───────────────────────────────────┐
          │  Cross-cutting services                                   │
          │  • FocusRevalidationService.RevalidateNow()               │
          │  • UseMutation(InvalidateKeys) / cache.Invalidate         │
          │  • UseInfiniteResource page cache (same QueryCache)       │
          └──────────────────────────────────────────────────────────┘
```

Three invariants hold across the whole system and are the foundation of the
race-condition argument in §11:

1. **Render is UI-thread-affine.** `RenderContext.BeginRender` captures
   `Environment.CurrentManagedThreadId` (`RenderContext.cs:21`), and all hook
   setters `AssertUIThread` in DEBUG builds unless `threadSafe: true` is passed.
   Hook registration and the initial synchronous work of `UseResource` /
   `UseInfiniteResource` both happen inside this render, and therefore on the UI
   thread.
2. **Async completions land on the dispatcher.** Every async continuation in the
   async system (`UseResource.ScheduleCompletion`, `UseInfiniteResource`'s page
   completion, `UseMutation`'s mutator continuation) posts through
   `IHookDispatcher.Post` before touching hook state. The dispatcher is
   captured at hook registration time with `DispatcherQueue.GetForCurrentThread()`
   (`UseResource.cs:168-176`). In unit tests where no WinUI dispatcher exists,
   `Post` calls fall through to inline invocation on the completion thread; the
   tests serialize by driving `ctx.BeginRender` / `ctx.FlushEffects` manually.
3. **The QueryCache is the only shared mutable state that spans hooks.** Any
   cross-hook coordination (dedup, invalidation, focus revalidation) flows
   through cache keys and the `EntryChanged` event.

---

## 2. The core ADT — `AsyncValue<T>`

`AsyncValue.cs:15-72`. Four sealed records under an abstract record, constructor
closed with `private protected`:

| Case | Payload | When entered |
|---|---|---|
| `Loading` | *(singleton)* | First render, cache miss, fetch in flight, no prior data |
| `Data(T Value)` | Fresh value | Fetch succeeded, or cache hit inside `StaleTime` |
| `Error(Exception)` | The exception | Fetch failed (or retries exhausted); prior data discarded |
| `Reloading(T Previous)` | Last-good value | Cache hit past `StaleTime`, OR a refetch started with `Data` on screen |

`Match` is a non-hot-path convenience that lets `Reloading` fall through to the
`data:` lambda by default (`AsyncValue.cs:59-71`). The spec's §5.1 prefers a C#
`switch` expression per render; the switch gets exhaustiveness checking and
avoids one delegate allocation per case.

Note that `Loading` is a singleton (`Loading.Instance`) and carries no payload —
allocation-free on transition. The hook exploits this in `StartAttempt` to
reset state without allocating.

---

## 3. The cache — `QueryCache`

`QueryCache.cs:49-335`. This is the only shared mutable state in the entire
async system. Everything else is either (a) per-hook instance state or (b)
immutable records pulled from this cache. The cache's correctness drives the
correctness of the system.

### 3.1 Shape

```
ConcurrentDictionary<string, Slot>
  Slot {
    readonly object Lock          // per-slot mutex
    object?  Entry                // actually a CacheEntry<T> — type erased
    int      SubscriberCount
    TimeSpan CacheTime
    DateTime? ZeroSubscribersAt
    bool     IsEvicted
  }
```

The `ConcurrentDictionary` gives us lock-free lookup of the slot; the
per-`Slot` `Lock` serializes mutation of `Entry`, `SubscriberCount`,
`ZeroSubscribersAt`, and `IsEvicted`. This is finer-grained than a cache-wide
lock and avoids head-of-line blocking when many hooks touch distinct keys.

`CacheEntry<T>` (line 26) is a C# `record` and, through its explicit
`ICacheEntry` interface (line 18), lets the cache manipulate `SubscriberCount`
without knowing `T`. The interface's `WithSubscriberCount` returns a fresh
record via `with { SubscriberCount = count }` — the entry itself is immutable;
only the slot's `Entry` reference rotates.

### 3.2 Subscribe / Unsubscribe — the eviction ref-count

```
Subscribe(key):                         Unsubscribe(key):
  loop:                                   lock(slot.Lock):
    slot = _slots.GetOrAdd(key, new Slot)   assert SubscriberCount > 0
    lock(slot.Lock):                         SubscriberCount--
      if slot.IsEvicted continue  ─┐         if SubscriberCount == 0:
      SubscriberCount++            │            ZeroSubscribersAt = now
      ZeroSubscribersAt = null     │
      EnsureEvictionTimer()        │
      return                       │
                                   │
 EvictNow() (every 1s):            │
   for each slot:                  │
     lock(slot.Lock):              │
       if SubscriberCount == 0     │
       and ZeroSubscribersAt       │
       and now - t >= CacheTime:   │
         slot.IsEvicted = true  ───┘  // visible to retrying Subscribe above
         _slots.Remove(key)
```

Three races are prevented by design:

**Race A — Subscribe races Eviction.** If `EvictNow` is inside `lock(slot.Lock)`
and about to set `IsEvicted = true`, a concurrent `Subscribe` that already won
`GetOrAdd(key)` (getting the same `Slot`) must wait on the lock. When it enters
the critical section it sees `IsEvicted == true`, `continue`s the outer loop,
and does a fresh `GetOrAdd` — which will insert a brand new `Slot` because
`EvictNow` has removed the old one from the dictionary. Net effect: the
subscribe never increments a dead slot's counter.

**Race B — Set races Eviction.** Same mechanism as Race A. `Set` also has
`while (true) { slot = _slots.GetOrAdd(...); lock { if (slot.IsEvicted)
continue; ... } }` (line 97). The invariant is "any code that mutates a slot
under its Lock must re-check `IsEvicted` and retry on another slot."

**Race C — Unsubscribe under zero.** `Unsubscribe` throws
`InvalidOperationException` on a missing slot or `SubscriberCount <= 0`. This
is defensive — the invariant "every Subscribe has exactly one paired
Unsubscribe" is enforced by `UseResource` / `UseInfiniteResource` hook
lifecycle, and a violation indicates a hook-logic bug. Failing loud catches
it early (the tests rely on this; see `QueryCacheTests`).

### 3.3 Eviction timer

`EnsureEvictionTimer` (line 296) lazily starts one shared `System.Threading.Timer`
on first `Subscribe`. The timer fires every `EvictionPollInterval` (default 1s,
mutable for tests) and calls `EvictNow`. **Exactly one timer** serves the entire
cache, giving O(1) pressure on the OS timer wheel regardless of slot count.

`EvictNow` holds `slot.Lock` across the dictionary `Remove`, so racing
`Subscribe` / `Set` operators either (a) win the lock before eviction, and
raise `SubscriberCount` > 0 so the slot is no longer eligible to evict; or (b)
observe `IsEvicted = true` and retry with a fresh slot. The `ICollection.Remove`
overload (line 243) compares by `KeyValuePair`, so a concurrent `TryAdd` that
replaces the slot doesn't see its fresh slot removed.

`EvictNow` is also exposed publicly (line 226) to let framerate and stress
tests drive eviction deterministically rather than waiting for the 1-second
polling interval.

### 3.4 `EntryChanged` event

The cache fires `EntryChanged(key)` on: `Set`, `Invalidate`, `InvalidatePattern`
(one fire per affected key), `Clear`, `EvictNow`. The firing path
(`FireEntryChanged`, line 307) supports an injected `DispatcherPost` callback
— tests leave it null for inline invocation; the production bootstrap (see
`ReactorHost`, not in this branch's scope but wired in phase 3) sets it to
marshal to the UI dispatcher so handlers are single-threaded.

Handlers (the per-hook `OnEntryChanged` in `ResourceHookState`) capture the
handler reference before invocation, so unsubscribing a handler mid-event does
not race the event dispatch.

### 3.5 Context scoping

`AppContexts.QueryCache` (`QueryCache.cs:347`) is a `Context<QueryCache>` with
a fresh default instance installed at class init. Tests can override by
`.Provide(AppContexts.QueryCache, customCache)` on an ancestor; this is how
unit tests get a fresh cache per test. Hosts that want multi-tenant caches
install one per tenant the same way.

---

## 4. `UseResource` — single async fetch

### 4.1 Hook-slot layout

`UseResourceCore` (`UseResource.cs:107-160`) registers in slot order:

1. `UseRef<string?>` — `hookIdRef.Current`. A GUID generated once on first
   render; survives every subsequent render for this component instance and
   forms the `{hookId}/{depsHash}` cache key.
2. `UseRef<ResourceHookState<T>?>` — the per-hook mutable state.
3. `UseReducer(0, threadSafe: true)` — the rerender tick. Thread-safe because
   the completion handler fires from either the dispatcher or the thread pool
   (when no dispatcher is installed), and must be safe to call off-UI.
4. `UseContext(AppContexts.PendingScope)` — nearest ancestor `PendingScope`,
   or null.
5. `UseContext(AppContexts.FocusRevalidation)` — the service the hook will
   enroll its cache key with if `RefetchOnWindowFocus` is set.
6. `UseEffect(() => () => state.Dispose())` — a one-shot cleanup registered at
   mount. `UseEffect` with an implicit empty dep array runs once; its cleanup
   fires when the component unmounts (`RenderContext.RunCleanups`).

The five `Use*` calls before `UseEffect` means every `UseResource` consumes
six hook slots. As a consequence, **`UseResource` obeys the rules-of-hooks**:
it must be called unconditionally, in the same order, every render.

### 4.2 Per-render logic (the state machine)

```
UseResourceCore:
  state = stateRef.Current ??= new ResourceHookState(...)
  newKey = options.CacheKey ?? $"{hookId}/{depsHash(deps)}"

  firstRender = state.LastDeps is null
  depsChanged = !firstRender && state.CacheKey != newKey

  if firstRender or depsChanged:
      state.TransitionToKey(newKey, deps)     // cancels old CTS, unsubscribe/subscribe
      EnterKey(state, fetcher, options)       // dispatch on cache state
  else:
      ReconcileWithCache(state, fetcher, options)

  return state.LastValue
```

`TransitionToKey` (`UseResource.cs:447-465`):
1. Cancels any in-flight CTS for the old key.
2. If the old key was non-empty and different, calls `Cache.Unsubscribe(oldKey)`
   and `_focusService?.Unenroll(oldKey)`.
3. Updates `CacheKey` to `newKey`, calls `Cache.Subscribe(newKey)`, enrolls with
   focus service if needed, snapshots `LastDeps`.

`EnterKey` (`UseResource.cs:178-207`) is the new-key dispatcher:
- `Cache.TryGet<T>(key, out entry)`:
  - Age `<= StaleTime` → `state.LastValue = Data(entry.Value)`. Done.
  - Age `>  StaleTime` → `state.LastValue = Reloading(entry.Value)`, then
    `BeginFetch` to refresh.
- Cache miss + `RefetchOnMount: false` → `state.LastValue = Loading.Instance`,
  no fetch.
- Cache miss + default → `BeginFetch`.

`BeginFetch` (`UseResource.cs:226-236`) dedupes on `state.InFlight`, disposes
any old CTS, mints a new `CancellationTokenSource`, and calls
`StartAttempt(..., attempt: 0, inlineSyncResult: true)`.

### 4.3 `StartAttempt` — the sync-complete fast path

`StartAttempt` (`UseResource.cs:238-298`) is where the spec's "no Loading
flash" property is implemented. It invokes the fetcher inside a try / catch,
then inspects the returned `Task<T>`:

| Task state (initial attempt only) | Action |
|---|---|
| `IsCompletedSuccessfully` | `cache.Set(...)`, `LastValue = Data(v)`, `InFlight = false`. Same render. |
| `IsCanceled` | Keep `Data` if we had one, else `Loading`. No throw. |
| `IsFaulted` | `HandleFailure` (retry or `Error`). Same render on terminal. |
| Pending | `LastValue = switch { Data d → Reloading(d.Value); Reloading r → r; _ → Loading }`. Mark `InFlight = true`. Schedule continuation via `ScheduleCompletion`. |

On retries (attempts > 0), `inlineSyncResult` is `false`, which changes two
things: we never overwrite `LastValue` to an intermediate `Loading/Reloading`
inside this call (the retry is invisible from the UI's point of view beyond
staying in whatever state it was), and failure of the final attempt triggers
`state.RequestRerender()` to flush `Error` to screen.

**GAP — cross-key data leakage on deps change.** When deps change → new cache
key → cache miss, `BeginFetch → StartAttempt` runs the pending-path switch
which maps `Data d` (from the *previous* key) to `Reloading(d.Value)`. That
surfaces the old query's data as the new query's stale-while-revalidate
previous value. The spec §6.2 step 6 is explicit that the old result should
*not* bleed into the new key's UI:

> When deps change, cancel the in-flight token for the old key and re-evaluate
> from step 1 with the new key. The old result stays in cache.

The desired fix is to reset `state.LastValue = Loading.Instance` inside
`TransitionToKey` (before `EnterKey` runs) whenever the transition is not a
first-render. The sync-complete fast path in `StartAttempt` still wins (it
overwrites to `Data(v)` on the same render), and the cache-hit-fresh path in
`EnterKey` still wins (it overwrites to `Data(entry.Value)`). Only the
cache-miss-pending case gets the correction, which is precisely where the
leakage occurs today.

### 4.4 `ScheduleCompletion` — the async path

`UseResource.cs:324-365`. The continuation is attached with
`TaskContinuationOptions.ExecuteSynchronously`, so when the task completes the
continuation runs on the thread that completed the task (thread pool, typically).
The continuation body is a closure that builds `Apply()` and then dispatches it:

```csharp
if (state.Dispatcher is { } disp) disp.Post(Apply);
else Apply();
```

`Apply` runs on the dispatcher thread (UI) when a dispatcher is present. It
performs, in order:

1. `if (state.IsDisposed) return;` — drop silently.
2. `if (ct.IsCancellationRequested) return;` — deps changed or unmount; drop
   silently per spec §6.4.
3. `if (t.IsCanceledOrDropped()) return;` — cancellation via the token or an
   unwrapped `OperationCanceledException` in a faulted task. Also drop silently.
4. `if (t.IsFaulted)` → `HandleFailure(...)` with `inlineSyncResult: false`.
5. Success path: `cache.Set(key, value, staleTime, cacheTime)` →
   `next = Data(value)` → record-equality-compare to `state.LastValue` →
   assign → `state.InFlight = false` → `state.RequestRerender()` only if
   actually changed. The equality skip prevents a render storm when the cache
   is updated with a value identical to the one already rendered (common with
   polling sources).

The **three-gate** drop sequence (IsDisposed, IsCancellationRequested,
IsCanceledOrDropped) is the race-free unmount/deps-change guarantee. Any one
of them is sufficient to cause the result to be ignored; all three checks
together close the window where a late-arriving result could clobber the
current state.

### 4.5 Retry with exponential backoff

`HandleFailure` (`UseResource.cs:300-322`) decides retry-vs-terminate based on
`attempt < options.RetryCount`. If retrying, it schedules a `System.Threading.Timer`
via `state.ScheduleRetry` with a delay of `100 * (1 << attempt)` ms — so 100,
200, 400, 800 ms for attempts 0..3. The timer's callback re-enters
`StartAttempt(..., attempt + 1, inlineSyncResult: false)`.

`ScheduleRetry` (`UseResource.cs:467-479`) captures `Cts?.Token` at schedule
time, not at fire time, so a deps-change that cancels the old CTS between
scheduling and firing causes the retry to no-op (`if (ct.IsCancellationRequested)
return;` at line 475). The timer is self-disposing in its own callback.

### 4.6 Cache invalidation propagation

`ResourceHookState` subscribes to `cache.EntryChanged` at construction
(`UseResource.cs:424`). The handler (line 430):

```csharp
private void OnEntryChanged(string key)
{
    if (IsDisposed) return;
    if (!string.Equals(key, CacheKey, StringComparison.Ordinal)) return;
    if (LastValue is AsyncValue<T>.Data && !Cache.TryGet<T>(key, out _))
        RequestRerender();
}
```

The narrow trigger — `LastValue is Data && cache entry is gone` — means the
hook only reacts to *invalidations* (mutation side-effects, focus revalidation,
pattern clears). A redundant `Set` that writes an identical value to the
existing entry is ignored here because the cache still has the entry. A
fresh `Set` with a *different* value *also* doesn't trigger a rerender through
this path; instead, the next render picks up the new entry via
`ReconcileWithCache` (which at present only refetches on gone-entry, so a
different-value Set from another hook would not flow to this hook until its
own deps change).

**GAP — sibling-updates-value visibility.** If hook A and hook B share a cache
key (via explicit `CacheKey`) and hook A completes a fetch, hook B's
`OnEntryChanged` is called but the narrow condition ignores it. Hook B
therefore does not re-render with hook A's new value until its own render
happens for another reason. This is arguably fine today (the spec's §12.5
"shared state across siblings" only works cleanly when both hooks start
rendering at similar times), but the path from *cache is updated* →
*subscribed hooks rerender with the new value* is not plumbed. A phase-2 fix
is to broaden `OnEntryChanged` to `RequestRerender()` whenever the cached
entry's `Value` differs from `(LastValue as Data)?.Value`.

### 4.7 Unmount teardown

`ResourceHookState.Dispose` (`UseResource.cs:481-495`):

1. Sets `IsDisposed = true` (plain bool — see §11 race C for the narrow edge).
2. Unregisters the cache `EntryChanged` handler.
3. Cancels and disposes `Cts`.
4. Unsubscribes the cache key and (if enrolled) the focus service.
5. Unregisters from the `PendingScope`.

Fire order: `RenderContext.RunCleanups` iterates `_hooks` and fires each
`EffectHookState.Cleanup` in hook-order. The cleanup we registered
(`UseResource.cs:141`) invokes `state.Dispose()`, so `Dispose` runs exactly
once per unmount. The `if (IsDisposed) return;` guard at line 483 makes the
call idempotent.

Because `Cts.Cancel()` happens synchronously inside `Dispose`, any in-flight
fetcher whose body observes the token sees `IsCancellationRequested == true`
immediately. The fetcher is expected to propagate via `OperationCanceledException`
or return a `Task<T>` in the `Canceled` state; either is handled by the
`IsCanceledOrDropped` drop gate in `Apply`. See the
`Unmount_Cancels_InFlight_And_Drops_Late_Result` test.

### 4.8 State diagram — `UseResource<T>`

```
                       ┌────────┐
    first render,      │        │   cache hit (age ≤ StaleTime),
    cache miss,        │        │   OR async completion success
    RefetchOnMount=F   │        │
            ┌──────────► Loading├────────────────────────────────┐
            │          │        │                                │
            │          └───┬────┘                                │
   ┌────────┴──┐           │ BeginFetch (pending)                │
   │ NotAsked* │           │                                     │
   │ (never    │           ▼                                     ▼
   │  observ.) │        ┌─────────┐  async completion       ┌─────────┐
   └───────────┘        │         │  (success, cts live)    │         │
                        │ Loading │───────────────────────► │  Data   │◄──┐
   cache hit (stale)    │ pending │                         │         │   │
     OR Data→refetch    │         │  async completion       └────┬────┘   │ cache-hit
            ┌───────────┴─────────┴──── (failure, retries       │        │ fresh, or
            ▼                           exhausted)               │        │ refetched
       ┌──────────┐                         │                    │        │ same value
       │          │◄────────────────────────┤                    │        │
       │Reloading │                         ▼                    │        │
       │(prev)    │                    ┌──────────┐              │        │
       └────┬─────┘                    │          │              │        │
            │  async success           │  Error   │              │        │
            │  (new value)             │          │              │        │
            └──────────────────────────┼──────────┘              │        │
                                       │                         │        │
  Data ── deps-change / cache invalid ─┴──────► BeginFetch ──────┴────────┘
  ↑  (Data is stable while cache entry exists and deps unchanged)
  │
  └── Error → no auto-recovery; a subsequent deps change or a manual
      cache Invalidate triggers BeginFetch again.
```

Legend: boxes are observable `AsyncValue<T>` states; arrows are transitions
driven by either render-time decisions (cache lookups, deps change) or async
completions. `*NotAsked*` is not an `AsyncValue<T>` case — it's shown to make
explicit that the hook has no pre-fetch state; first render always enters
`Loading` (or `Data` on the sync-complete fast path).

---

## 5. `UseInfiniteResource` — paginated fetch

### 5.1 Conceptual model

`UseInfiniteResource` keeps an independent `AsyncValue`-style state *per page*
but exposes a single `InfiniteResource<TItem>` handle to the render code. The
handle carries:

- `Items : IReadOnlyList<TItem?>` — flat virtual index. `null` at an index
  means "the page that contains this index is either in-flight, or about to
  be fetched."
- `LoadState : Loading | Idle | EndOfList | Error(e)` — aggregate of the most
  recent page fetch.
- `TotalCount?`, `HasMore`, `EstimatedRemaining` — convenience derivatives.
- `ItemAt(int)`, `EnsureRange(int, int)`, `FetchNext()`, `Retry()`, `Refresh()`
  — the pull-model API that virtualized list controls call.

The hook's role is to translate `ItemAt` / `EnsureRange` pulls into page
fetches, dedup concurrent pulls, cache per-page results in the shared
`QueryCache`, and restart cleanly on deps change / refresh / unmount.

### 5.2 Dual-layer caching

Pages are cached in two places:

1. **`InfiniteResource._pages` dictionary** (`InfiniteResource.cs:69`) — the
   hot-path accessor for `ItemAt`. Subject to LRU eviction when
   `InfiniteResourceOptions.MaxLoadedPages` is set. Holds `PageSlot(IReadOnlyList<TItem>?)`
   — `null` items means in-flight placeholder.
2. **`QueryCache` under `{keyPrefix}/page:{n}`** — the warm-path. Survives
   LRU eviction from `_pages`; survives unmount-and-remount inside `CacheTime`.
   `InfiniteHookState.SubscribeKey` (line 331) ref-counts every page key that
   this hook has ever loaded or started loading, so the cache retains them
   until unmount.

On `RequestPage`, the hook always checks the `QueryCache` first
(`UseInfiniteResource.cs:221`). A hit warm-starts the page into `_pages` via
`Resource.ApplyPageResult`, skipping the network round-trip entirely. This is
the back/forward-nav-is-instant property.

### 5.3 `ItemAt` — claim-before-release pattern

The single most subtle code path is the ItemAt → fetch dispatch. Full text:

```csharp
public TItem? ItemAt(int index)
{
    TItem? result = default;
    bool scheduleFetch = false;
    int pageToFetch = -1;

    lock (_lock)
    {
        if (index < 0) return default;
        int pageIndex = index / _options.PageSize;
        if (_pages.TryGetValue(pageIndex, out var slot)) { /* ... return cached / null */ }
        if (_totalCount is { } total && index >= total) return default;

        MarkPageInFlightLocked(pageIndex);   // <-- claims the slot inside the lock
        scheduleFetch = true;
        pageToFetch = pageIndex;
    }

    if (scheduleFetch) _pageRequestedCallback?.Invoke(pageToFetch);
    return default;
}
```

Two `ItemAt` calls from two different indices in the same page race like this:
- Caller A enters the lock, sees `_pages` has no slot for `pageIndex`, calls
  `MarkPageInFlightLocked(pageIndex)` which writes `_pages[pageIndex] = new
  PageSlot(null)`, releases the lock, calls back to the hook.
- Caller B enters the lock, sees `_pages` *does* have a slot (with `Items =
  null`), follows the cached-slot branch, returns `default` without scheduling.

So the "in-flight placeholder" is visible to concurrent callers and dedupes
them — even before the hook's `_pageRequestedCallback` has returned. This is
why the spec's "one fetch per page" guarantee holds under pull-model race.

The hook's `RequestPage` (`UseInfiniteResource.cs:213`) also dedupes via
`_pageCts.ContainsKey(pageIndex)`, giving a belt-and-suspenders defense: the
resource-side claim prevents duplicate callback invocations, and the
hook-side check prevents duplicate `CancellationTokenSource` creation.

### 5.4 Cursor-chained page fetch

Page N ≥ 1 needs the cursor returned by page N-1. `RequestPage` handles this
by recursing (`UseInfiniteResource.cs:239-252`):

```csharp
if (!HasLoadedPage(pageIndex - 1))
{
    RequestPage(pageIndex - 1);
    if (!HasLoadedPage(pageIndex - 1)) return;   // N-1 still pending — drop
}
cursor = Resource.GetCursor<TCursor>();
```

If the recursive call for N-1 completes synchronously (sync-complete fetcher
or cache hit), the guard passes and we proceed to fetch N. If it remains
pending, we return and rely on the completion of N-1 plus a subsequent
`ItemAt` / `EnsureRange` / `FetchNext` to re-kick N.

**BUG — page N dropped when page N-1 is in flight.** The `return;` at line
244 is silent. If no subsequent caller triggers page N, it is never fetched.
Virtualized list controls usually call `EnsureRange` whenever the viewport
shifts, which re-triggers, so in practice this self-heals — but the
hook-level correctness argument wants an explicit "on page N-1 completion,
flush any pending follow-on pages that were dropped for want of cursor."
Today no such queue exists.

The desired fix is a small `HashSet<int> _pendingCursorChainedPages` in
`InfiniteHookState`. When `RequestPage` drops a higher-page fetch, it adds
`pageIndex` to the set. `CommitSuccess` for page N-1 pops any queued
followers for page N and re-invokes `RequestPage`. The set stays empty in
the happy path so there is zero overhead for the common case.

### 5.5 `EnsureRange` — batched viewport pulls

`InfiniteResource.cs:172-196` computes the page range under the lock, claims
each not-yet-loaded slot with `MarkPageInFlightLocked`, collects the page
indices, then releases the lock and invokes the callbacks serially outside
the lock. Same claim-before-release property as `ItemAt`.

When `_totalCount` is known (page 0 reported it), `lastIndex` is clamped to
`total - 1` so we never request pages past the known end.

### 5.6 `Refresh` — invalidate and restart

`InfiniteHookState.Refresh` (`UseInfiniteResource.cs:337-364`):

1. Cancel every in-flight page CTS.
2. `Cache.Invalidate(key)` for every subscribed page key — fires
   `EntryChanged` once per key.
3. `Cache.Unsubscribe` each key and clear `_subscribedKeys`.
4. `Resource = CreateResource()` — drops a brand-new `InfiniteResource<TItem>`
   in place of the old one. Consumers that re-read `state.Resource` after the
   rerender get the fresh handle. Consumers that captured a reference to the
   old handle will see it frozen at whatever state it was in at refresh time.
5. Mark `PendingScope` loading, rerender, then `RequestPage(0)`.

**GAP — scroll preservation contract is consumer-owned but not documented
here.** Spec D17 says `InfiniteResource.Refresh()` is data-only and the
control owns scroll restoration. The hook exposes the `LoadState` transition
(Idle → Loading in the new resource) and `RowKey`-based item identity via
the `IDataSource<T>` adapter. Today, the new-resource swap happens before
the `_rerender()` call, so a consumer that snapshots scroll state inside
its `LoadState.Loading` render will actually be reading the *old* resource's
final state on the render before the one where the new resource appears.
Depending on scroll-snapshot timing, this may be fine or may need a second
render to settle. Worth verifying with a LazyVStack framerate fixture once
one exists.

### 5.7 State diagram — `InfiniteResource<T>.LoadState`

```
                      ┌────────────────────┐
                      │  Loading           │◄──────────── Refresh() (any state → Loading)
                      │  (initial; page 0  │
    page N fetch      │  in-flight; or     │    Retry() from Error
    starts via        │  any page N+1      │      │
    RequestPage/      │  via ItemAt)       │      │
    ItemAt/Ensure     │                    │      ▼
    Range/FetchNext   │                    │ ┌─────────┐
           ─────────► │                    │ │         │
                      └───┬────────────────┘ │  Error  │
                          │                  │ (pageIx)│
           success        │                  │         │
           (NextCursor    ▼                  └────┬────┘
           non-null)  ┌─────────┐                 │ page fetch failed
           ─────────► │  Idle   │◄────────────────┘
                      │         │
                      │ (items  │  FetchNext() / ItemAt() on unfetched range
                      │ visible)│  ──► back to Loading
                      └────┬────┘
                success    │
                (NextCursor│
                 == null)  │
                           ▼
                      ┌──────────┐
                      │EndOfList │  (terminal; HasMore == false)
                      └──────────┘
```

Per-page concurrency: multiple pages may be in flight simultaneously (e.g.,
`EnsureRange` spans three uncached pages). `LoadState` is the *most recent*
start; it is `Loading` while any fetch is in flight, transitions to `Idle`
only after the last in-flight page completes, and to `Error` on the most
recent failed page.

### 5.8 Deps-change restart

`UseInfiniteResourceCore` line 73: compare `state.KeyPrefix` to the freshly-
computed `newKeyPrefix`. On change, `state.TransitionToDeps(newPrefix, newDeps)`:

1. Cancels and disposes every in-flight `_pageCts`.
2. Unsubscribes every entry in `_subscribedKeys` and clears the set.
3. Updates `KeyPrefix`, `LastDeps`.
4. Creates a fresh `InfiniteResource<TItem>` for the new deps. The old
   resource's `Items` array and `_pages` dictionary are gone as soon as
   the hook's `state.Resource` reference rotates — assuming no consumer
   holds a long-lived reference, the GC reclaims them immediately.
5. Sets PendingScope loading back to true; calls `KickOffFirstPage()` which
   enters `RequestPage(0)`.

Previous-key pages remain in `QueryCache` under their old prefix until
`CacheTime` expires, so a deps change back to the previous value within the
window hits cache and re-populates instantly.

### 5.9 `IDataSource<T>` adapter

`DataSourceResourceExtensions.UseDataSource` (`DataSourceResourceExtensions.cs:18-37`)
is a thin hook that projects any `IDataSource<T>` into `UseInfiniteResource`:

```csharp
return ctx.UseInfiniteResource<T, string>(
    fetchPage: async (cursor, ct) =>
    {
        var req = request with { ContinuationToken = cursor };
        var page = await source.GetPageAsync(req, ct).ConfigureAwait(false);
        return new Page<T, string>(page.Items, page.ContinuationToken, page.TotalCount);
    },
    cache: cache,
    deps: new object[] { source, request.Sort ?? (object)"", request.Filters ?? (object)"", request.SearchQuery ?? "" },
    options: options, dispatcher: dispatcher);
```

Deps include the source *identity* plus the request's sort/filter/search —
any change restarts pagination, which is the correct behavior for an
`IDataSource<T>`.

---

## 6. `UseMutation` — async writes

### 6.1 Lifetimes

Unlike reads, mutations have a 1:N relationship between the hook and the call:
one `Mutation<TInput, TResult>` handle is returned per hook slot and per
component lifetime, and it can be `RunAsync(input)`-invoked arbitrarily many
times across that lifetime. Each `RunAsync` call has its own linked
`CancellationTokenSource`.

`MutationHookState` (`UseMutation.cs:160-345`) owns:
- `_unmountCts` — a single CTS cancelled on component unmount.
- `_pendingCount` — number of concurrently-in-flight mutations.
- `_lastResult`, `_error` — whichever finishes last wins.
- `Mutator`, `Options` — rotated every render so the latest closures win
  (same convention as `UseCallback`).

### 6.2 Run sequence

```
RunAsync(input):
  if IsDisposed: return Task.FromCanceled
  try options?.OnOptimistic?(input)       // synchronous on caller thread
  catch: return Task.FromException
  callCts = LinkedTokenSource(_unmountCts.Token)
  pendingCount++, RequestRerender()
  tcs = new TaskCompletionSource
  try inner = Mutator(input, callCts.Token)
  catch: FinishFailure(..) + tcs.SetException
  inner.ContinueWith(ExecuteSynchronously):
      Apply():
        if cancelled:   FinishCancelled(...); tcs.TrySetCanceled
        elif faulted:   FinishFailure(...);   tcs.TrySetException
        else:           FinishSuccess(...);   tcs.TrySetResult
      if dispatcher: dispatcher.Post(Apply) else Apply()
  return tcs.Task
```

`OnOptimistic` runs synchronously on the caller. If it throws, the mutator
never runs — preventing the half-applied state where the cache is patched
but the server call failed to even start.

`FinishSuccess` calls `cache.Invalidate(key)` for each key in
`Options.InvalidateKeys` *before* firing `OnSuccess`. That ordering matters:
an `OnSuccess` that re-reads the cache (to confirm the mutation landed) sees
the entries already invalidated, rather than reading stale values that would
cause a flicker on the next render.

Overlapping `RunAsync` calls each get their own `callCts`; both complete in
completion order. There is no built-in serialization — if the caller wants
"only one in flight at a time," they gate the `RunAsync` invocation
themselves (e.g., `Button.Disabled(mut.IsPending)`).

### 6.3 Unmount

`MutationHookState.Dispose`:
- `_isDisposed = true`.
- `_unmountCts.Cancel()` — every linked `callCts` observes cancellation
  through the linked-token mechanism; in-flight mutators see
  `ct.IsCancellationRequested == true` and should cooperatively cancel.
- `_unmountCts.Dispose()`.

`RunAsync` called post-Dispose returns `Task.FromCanceled` immediately rather
than firing callbacks on a dead component.

---

## 7. `Pending` — bubble-up fallback

### 7.1 Data structure

`PendingScope` (`PendingScope.cs:20-79`) is a thread-safe dictionary from
opaque `object` token → `bool` loading-state, plus a `Changed` event. Each
hook inside the scope uses `this` as the token and calls
`Register(this, isLoading: true)` on construction, `SetLoading(this, false)`
as soon as its state leaves `Loading`, and `Unregister(this)` on unmount.

`AnyLoading` is a linear scan of the dictionary's values — `O(n)` in the
number of resources inside the scope. For the typical scope size (a few
resources per modal / page), this is irrelevant. For very dense trees it
could be replaced with a running counter, but that's premature today.

### 7.2 Scope plumbing

`PendingComponent.Render` (`Pending.cs:44-77`):

1. `UseRef<PendingScope?>` — the per-instance scope. Never shared, so
   disposing a `Pending` cleans up its own scope.
2. `UseReducer(0, threadSafe: true)` — rerender tick.
3. `UseEffect` to subscribe/unsubscribe `scope.Changed`. Re-arms the handler
   on every render so a stale handler from a previous mount can't fire.
4. Emits a 1×1 `Grid` with both child and fallback mounted, with
   `Visible(!showFallback)` / `Visible(showFallback)` on the two branches,
   and `.Provide(AppContexts.PendingScope, scope)` to expose the scope to the
   subtree.

The key design decision: **both trees stay mounted.** The child's
`UseResource` hooks remain active while the fallback is on screen, continue
their fetches, and when they flip out of `Loading`, the scope fires `Changed`,
the component re-renders, and visibility flips. This is what makes `Pending`
*not Suspense* — there is no unwinding, no re-render, no reconciler work.

### 7.3 `Loading` vs `Reloading`

Per spec §10.1, only `AsyncValue.Loading` (not `Reloading`) counts as "still
fetching" for the bubble-up. `ResourceHookState.NotifyPending`
(`UseResource.cs:440-445`) encodes this:

```csharp
private void NotifyPending()
{
    if (PendingScope is null) return;
    bool loading = _lastValue is AsyncValue<T>.Loading;
    PendingScope.SetLoading(this, loading);
}
```

`Reloading(prev)` means "we have something to show" — the subtree renders
normally, and if a parent `Pending` flipped to fallback during the initial
load, a refetch won't flip it back. This matches TanStack Query's
`isLoading` vs `isFetching` distinction.

For `UseInfiniteResource`, `NotifyPending` (`UseInfiniteResource.cs:160-168`)
applies the same rule at page-set granularity: "loading" only when
`LoadState is Loading && Items.Count == 0`. Once any page has landed, the
list is considered renderable and subsequent page fetches do not re-trigger
the fallback.

### 7.4 State diagram — `Pending`

```
       ┌────────────────────────────────┐
       │  PendingScope.AnyLoading?      │
       └─────┬──────────────────────┬───┘
             │ false                │ true
             ▼                      ▼
   ┌──────────────────┐   ┌─────────────────────┐
   │ render child     │   │ render fallback     │
   │ child subtree    │   │ child subtree still │
   │ visible          │   │ mounted but Visible=│
   │                  │   │ false; hooks run    │
   └──────┬───────────┘   └─────────┬───────────┘
          │                         │
          │  any child resource     │  all resources leave
          │  enters Loading         │  Loading (Data / Error /
          │  (e.g. deps change      │  Reloading count as "done")
          │   on a shared-deps      │
          │   pattern)              │
          └────────►                │
                                   ◄┘
                       scope.Changed fires → re-render
```

Nested `Pending`s are independent: each `PendingComponent` provides a fresh
scope via `AppContexts.PendingScope`, so a descendant hook registers only
with its *nearest* ancestor `PendingComponent`, not every ancestor.

---

## 8. `FocusRevalidationService`

`FocusRevalidationService.cs:23-121`. Off by default
(`ReactorFeatureFlags.FocusRevalidation = false` — `ReactorFeatureFlags.cs:40`).
When enabled and opted into per-hook via `ResourceOptions.RefetchOnWindowFocus
= true`, the service tracks the set of cache keys under observation and
invalidates the ones past their `StaleTime` on window-activation events.

**Invariants:**

- Enrollment is opt-in and opt-out on the hook side
  (`ResourceHookState.TransitionToKey` enrolls the new key; `Dispose` and
  previous-key transitions unenroll).
- `RevalidateNow` is throttled: returns `Array.Empty<string>()` if it was
  called within `ThrottleWindow` (default 30s) of the previous call. This
  prevents Alt-Tab thrashing from firing a sweep on every focus transition.
  `RevalidateNowForce` bypasses the throttle for tests.
- The enrolled set is snapshotted out of the lock before iteration so a
  handler that `Unenrolls` itself mid-sweep does not mutate the
  live collection.
- The sweep works only through `QueryCache.TryGetFetchedAt` — a non-generic
  metadata peek that reads the entry's age without knowing `T`. This lets the
  service stay generic-free.

The actual OS event plumbing (`CoreWindow.Activated`, `CoreApplication.Resuming`)
is not in this phase-1 branch; it's wired in phase 4 per the spec. The service
exists standalone so hooks can enroll against the right contract today, and
the plumbing switches on later.

---

## 8.1 Threading model — current state and one rejected alternative

Every mutable type the async system owns is protected by an internal
`Monitor` lock or a `threadSafe: true` reducer. Today's split:

| Component | Sync mechanism | Reasoning |
|---|---|---|
| `QueryCache` slot | Per-slot `Monitor` lock (`QueryCache.cs`) | Cross-cutting shared state; eviction runs on a thread-pool timer; tests use the cache without a dispatcher. Decoupling the cache from UI affinity is a feature. |
| `QueryCache._timerLock` | `Monitor` + `Interlocked` | Timer create/dispose is rare and can run from any thread. |
| `MutationHookState._lock` | `Monitor` lock | `RunAsync` is intentionally callable from any thread; the lock protects `_pendingCount` / `_lastResult` / `_error` against the cross-thread caller. |
| `InfiniteResource._lock` | `Monitor` lock | Documented thread-safe contract; `UseInfiniteResourceThreadingTests` drive `ItemAt` / `EnsureRange` from background threads to verify it. Production callers (virtualized list controls during layout) are UI-thread-affined, but the contract is the broader one. |
| `PendingScope._lock` | `Monitor` lock | All production callers are UI-thread-affined, but the no-dispatcher edge (headless host, certain test paths) can fire `SetLoading` from a Task completion thread. The lock keeps that path safe in Release as well as DEBUG. |
| `FocusRevalidationService._lock` | `Monitor` lock | Same shape as `PendingScope`. WinUI's activation/resume callbacks fire on the UI thread, but the lock keeps misuse from corrupting the enrolled set. |
| `UseResource` / `UseInfiniteResource` / `UseMutation` rerender reducer | `threadSafe: true` | The hook continuation `Apply` runs on the dispatcher thread in production, but the test-suite `InlineDispatcher` runs `Apply` on whatever thread completed the underlying `Task`. The `threadSafe` reducer is what makes those test paths safe. |
| `Pending`'s rerender reducer | `threadSafe: true` | `PendingScope.Changed` *should* fire on the UI thread, but the no-dispatcher edge can land it on a thread-pool thread; the `threadSafe` reducer is the rerender-path safety net. |

The locks are uniformly uncontested in production — the UI thread reaches
them through the dispatcher and competing background-thread callers only
appear in test fixtures that deliberately exercise the thread-safe contract.
Keeping them is cheap (a few ns per uncontested `Monitor` take) and guarantees
serialization in **all** builds, not just DEBUG.

### Rejected alternative: replace UI-affined locks with dispatcher affinity

A previous attempt (PR #93, branch `async/dispatcher-affinity`) proposed
flipping `PendingScope` and `FocusRevalidationService` from internal locks to
DEBUG-only thread-affinity assertions. The framing was "the UI thread,
reached through `IHookDispatcher.Post`, *is* the synchronization mechanism —
defensive locks are redundant." The change was rejected after review. The
reasoning, captured here so the same proposal doesn't get re-litigated:

- **No measurable benefit.** Both locks are uncontested and off the hot
  path. `PendingScope` is touched once per hook lifecycle event; the
  `FocusRevalidationService` lives behind a feature flag that is off by
  default. Removing them is not a perf win.
- **Net correctness regression in Release builds.** A
  `[Conditional("DEBUG")]` `AssertOwnerThread()` is compiled away in Release.
  The lock guaranteed serialization unconditionally; the assertion does not.
  The no-dispatcher edge case (`state.Dispatcher == null`, headless hosts,
  certain test paths where `UseResource` applies completions inline on the
  Task completion thread) is real enough that the same PR kept
  `Pending`'s rerender reducer `threadSafe: true` as belt-and-suspenders —
  but `PendingScope`'s dictionary itself had no equivalent fallback. Trading
  unconditional serialization for "it shouldn't happen, and if it does, only
  DEBUG catches it" is a worse contract than what we have.
- **Two paradigms, not one.** The proposal explicitly deferred migrating
  `QueryCache`, `MutationHookState`, `InfiniteResource`, and the three async
  reducers (each defer was load-bearing — `InfiniteResource`'s lock backs a
  documented thread-safe contract that threading tests deliberately
  exercise; the reducers' `threadSafe: true` exists because the test
  `InlineDispatcher` runs `Apply` off the UI thread). The end state would
  have been a codebase with two synchronization paradigms (lock-based and
  affinity-based) where it previously had one — harder to reason about, not
  easier.
- **Test-side regression.** The proposal rewrote three `PendingTests` to
  drop `await Task.Delay(...)` settle-loops, replacing them with a hard
  dependency on default-mode `TaskCompletionSource` running continuations
  inline on `SetResult`. That couples the tests to an SDK behavior detail —
  anyone later wrapping `SetResult` in `Task.Run` or constructing the TCS
  with `RunContinuationsAsynchronously` would get a mystery affinity-assert
  failure.

If a future migration wants to fully embrace "the dispatcher is the
synchronization mechanism," it should land all subsystems at once
(including a marshalling test dispatcher that lets the reducers drop
`threadSafe: true`), not partially. Until then the locks stay.

One artifact from that branch is worth keeping in mind even though we did
not adopt it: a typed `IHookDispatcher.InvokeAsync<T>(Func<T>)` extension
that wraps `Post` in a `TaskCompletionSource<T>` so background code can
read UI-affined state and get a value back. We chose not to add it
speculatively — there is no current caller — but it is the right shape
for the rare cross-thread read should one ever need it.

---

## 9. Threading and race-condition argument

The overall property we want to prove is:

> **Safety.** For every call to a user-supplied `fetcher` or `mutator`, exactly
> one of the following is true at the time the call's result or exception is
> surfaced:
> - The result is applied to the cache + hook state and the component re-renders.
> - The result is silently dropped because the hook unmounted, deps changed, or
>   the token was cancelled.
>
> Results are never applied to state in a stale key, never double-applied,
> and never cause a race-through against the render thread.

The argument is built from five layers.

### Layer 1 — Render is single-threaded

`RenderContext.BeginRender` captures the UI thread id; setters assert in DEBUG.
Hook registration runs on that thread. The synchronous portion of
`UseResourceCore` (hook-id, state creation, key derivation, `TransitionToKey`,
`EnterKey`, `StartAttempt`-pending-decision) all runs while the render is in
flight, i.e., on the UI thread. Nothing from another thread can enter the
hook's state mutation paths during this window.

### Layer 2 — Async completions cross one dispatcher boundary

`ScheduleCompletion` attaches a continuation with
`TaskContinuationOptions.ExecuteSynchronously`. The continuation's body then
posts `Apply` through the injected `IHookDispatcher` (or calls it inline if
none). In production, the dispatcher is WinUI's `DispatcherQueue`, which
serializes enqueued actions onto the UI thread. So `Apply` — the *only* code
path that writes `state.LastValue` / `state.InFlight` / `state.Cts` after the
initial render — runs on the UI thread.

In unit tests where no dispatcher is present, `Apply` runs inline on whichever
thread completed the Task. The tests ensure single-threaded access by driving
the render loop manually with deterministic `TaskCompletionSource`-backed
fetchers.

### Layer 3 — Three-gate drop sequence for late results

Inside `Apply`:

```csharp
if (state.IsDisposed) return;                    // gate 1
if (ct.IsCancellationRequested) return;          // gate 2
if (t.IsCanceledOrDropped()) return;             // gate 3
```

Gate 1 catches unmount: `Dispose` sets `IsDisposed = true` before any
subsequent teardown, and sets it *before* cancelling the CTS, so a
continuation that races Dispose will see either `IsDisposed = true` OR an
already-cancelled token (usually both).

Gate 2 catches deps-change: `TransitionToKey` cancels the old CTS before
substituting a new one. The continuation captured the old `ct` at schedule
time, so a late result on the old token is dropped.

Gate 3 catches the case where the fetcher's task faulted with
`OperationCanceledException` unwrapped — we don't surface cancellation as
`Error`.

All three gates together close the window. The only window where a race
*could* clobber state is between `cache.Set(key, value, ...)` at line 354 and
`state.LastValue = next2` at line 357, during which a concurrent Dispose
would set `IsDisposed = true`. In practice this window is on the same thread
(UI thread) if a dispatcher is present — Dispose is itself called during
`RunCleanups`, which runs during the UI thread's render / unmount cycle —
so no actual race exists. In the dispatcher-less test harness, the tests
avoid interleaving Dispose with completion by calling `RunCleanups`
explicitly after draining the dispatcher queue.

### Layer 4 — Cache mutations are per-key locked

`QueryCache` operations are described in §3. The key argument: every mutation
to a `Slot` holds `slot.Lock`, and the `IsEvicted` flag plus the while-loop
retry pattern in `Subscribe` / `Set` makes the (dictionary insert, slot
mutate, dictionary remove) sequence effectively atomic from the caller's
perspective. A `Subscribe` and a concurrent `EvictNow` either serialize such
that the Subscribe increments a live slot, or the Subscribe observes
`IsEvicted=true` and retries on a fresh slot.

### Layer 5 — Rerender requests are thread-safe

The rerender path uses `UseReducer(0, threadSafe: true)`, which takes a lock
and uses `EqualityComparer.Default.Equals` for change detection. A rerender
requested from a background thread is serialized into the reducer and then
flows through the reconciler on its next scheduled tick. No state is mutated
outside the reducer's lock on that path.

### 9.1 Known narrow races / edges

**`IsDisposed` is a plain `bool`, not `volatile` or interlocked.** On x86 /
x64 CLR, aligned `bool` writes are atomic and visible across threads without
explicit memory barriers, but the .NET memory model technically allows
reorder. In practice: (a) the hook's `Dispose` runs on the UI thread as part
of the reconciler's unmount flow; (b) the continuation observing `IsDisposed`
has already crossed the dispatcher boundary, which includes a memory fence;
(c) if the test harness has no dispatcher, the single-threaded test loop
prevents concurrent access. So while not strictly lock-free-correct in the
ECMA memory model, the property holds under the threading topology the code
documents.

**`state.InFlight` is a plain `bool`.** Same argument. Reads happen during
render (UI thread); the one cross-thread write is in the continuation's
`Apply`, which also runs on the UI thread through the dispatcher. The test
harness paths run single-threaded.

**`QueryCache.EntryChanged`'s invocation list.** C# events use a
copy-on-subscribe invocation list; `+=` / `-=` are non-destructive. The
handler reference captured at `FireEntryChanged` time is stable across the
invocation even if another thread unsubscribes. The one remaining window —
unsubscribe-then-dispose between capturing the handler reference and
invoking it — is guarded by the handler's own `IsDisposed` check.

---

## 10. End-to-end walkthrough — a typical Render → Fetch → Re-render

To tie everything together, trace one interaction from the spec's §6.3 example
(`UserProfile` fetching a user by id):

```
1. Render #1 on UI thread:
   a. UserProfile.Render() runs; calls ctx.UseResource(fetcher, [userId]).
   b. UseResourceCore allocates hookId (GUID), creates ResourceHookState,
      registers UseEffect cleanup, captures DispatcherQueue for current thread.
   c. TransitionToKey: newKey = "a1b2.../hash(userId)". No prior key.
      Cache.Subscribe(newKey) → SubscriberCount = 1, Slot created.
      PendingScope (if any) gets Register(state, isLoading: true).
   d. EnterKey: cache miss; RefetchOnMount=true → BeginFetch.
   e. BeginFetch → StartAttempt(attempt=0, inline=true):
      i.   Create CTS, capture token.
      ii.  fetcher(ct) returns a pending Task<User>.
      iii. Task not complete → pending branch:
           LastValue = switch over prior LastValue → Loading.Instance.
           InFlight = true.
           ScheduleCompletion: attach continuation with ExecuteSynchronously.
   f. Render returns Loading; reconciler renders Skeleton.

2. Network returns (thread pool):
   a. Task transitions to RanToCompletion with the User payload.
   b. Continuation fires on a thread-pool thread.
   c. Continuation body posts Apply to IHookDispatcher.Post → queued on UI.

3. UI thread drains dispatcher:
   a. Apply() runs:
      - state.IsDisposed false; ct not cancelled; task not cancelled.
      - cache.Set(key, user, staleTime, cacheTime): slot.Entry = new
        CacheEntry<User>(user, now, staleTime, SubscriberCount=1).
        FireEntryChanged(key) → our own OnEntryChanged fires, sees
        LastValue=Loading (not Data), does not trigger a redundant rerender.
      - next = new Data(user); record-equality vs Loading → changed → assign.
      - NotifyPending: not Loading → PendingScope.SetLoading(state, false).
      - InFlight = false; RequestRerender() → tick++ → reducer triggers
        reconciler re-render.

4. Render #2 on UI thread:
   a. UserProfile.Render() runs; ctx.UseResource(fetcher, [userId]).
   b. stateRef.Current already set.
   c. Same key, same deps → not firstRender, not depsChanged.
   d. ReconcileWithCache: LastValue is Data AND cache still has the entry →
      no-op.
   e. Return state.LastValue = Data(user).
   f. Render returns VStack(Heading(user.Name), Text(user.Email)).

5. Later, user navigates away:
   a. Reconciler calls RunCleanups on UserProfile's RenderContext.
   b. UseEffect cleanup fires: state.Dispose().
   c. Dispose: IsDisposed = true; unsubscribe cache EntryChanged;
      Cts.Cancel(); Cts.Dispose(); Cache.Unsubscribe(key) → SubscriberCount
      = 0 → ZeroSubscribersAt = now. PendingScope.Unregister(state).
   d. The entry stays in the cache until CacheTime expires. If the user
      navigates back within CacheTime, render #1 of the next mount hits
      EnterKey's cache-hit path and returns Data(user) on the same render,
      no Loading flash.
```

Every step runs on the UI thread (or crosses exactly one dispatcher
boundary). The only piece of shared state is the `QueryCache` slot for this
key, which serializes access via its per-slot lock.

---

## 11. Bug / gap summary (for the follow-up pass)

| # | Location | Class | Description |
|---|---|---|---|
| 1 | `UseResource.cs:289-294` | BUG | Cross-key data leakage on deps change. When deps change and the new key is a cache miss, `StartAttempt`'s inline-pending switch maps the old key's `Data(old)` to `Reloading(old)`, surfacing the old query's value as the new query's stale-while-revalidate previous. Fix: reset `state.LastValue = Loading.Instance` in `TransitionToKey` before `EnterKey` runs, for non-first renders. See §4.3. |
| 2 | `UseInfiniteResource.cs:239-244` | BUG | Dropped follower page when N-1 is in flight. `RequestPage(N)` recurses into `RequestPage(N-1)`; if N-1 is already in-flight, the call silently returns and page N is never fetched. In practice virtualized controls re-request via `EnsureRange`, but the correctness argument wants a `_pendingCursorChainedPages` set that flushes on `CommitSuccess`. See §5.4. |
| 3 | `UseResource.cs:430-438` | GAP | Sibling-updates-value not plumbed. `OnEntryChanged` only fires a rerender when an entry is *removed*. A fresh `Set` with a different value from a sibling hook is not delivered to this hook until its own deps change. Broaden the condition to "`LastValue is Data && cache value differs from LastValue.Value`." See §4.6. |
| 4 | `UseInfiniteResource.cs:337-364` | GAP | `Refresh` replaces `state.Resource` before rerender. Consumers that snapshot scroll state during the `LoadState.Loading` render may be one frame out of sync relative to the resource-swap. Verify with a LazyVStack framerate fixture once one exists; may need a two-phase refresh (mark old resource as refreshing → render → swap in new resource → render). See §5.6. |
| 5 | `UseResource.cs:395-400` | MINOR | `LastValue` property setter always calls `NotifyPending` even when value didn't change. Pending scopes fire redundant `Changed` events on stale-while-revalidate re-applies. Trivial optimization; wrap setter with equality check. |
| 6 | `UseResource.cs:289-295` (retry inline path) | MINOR | When `StartAttempt` runs on a retry (`inlineSyncResult=false`), the pending-branch `LastValue` switch is skipped entirely. That is intentional (don't re-flash Loading on a retry), but it means `NotifyPending` isn't called, so `PendingScope` is not re-armed if the retry somehow reverts the state to Loading. In practice it can't, because a retry starts from an Error which doesn't participate in Pending anyway. Worth a short comment pointing this out. |
| 7 | `QueryCache.cs:267-283` | MINOR | `TryGetFetchedAt` returns false on missing slot OR non-`ICacheEntry` Entry. The doc comment says "should be impossible through the public API," which is true today. If generic invariants later change (e.g., a second payload wrapper), this becomes a silent-skip. Consider asserting non-null `ICacheEntry` when slot is present. |

None of the items above are blockers. Items 1 and 2 are the only ones with
user-visible semantic effects; the rest are about defense-in-depth or future
hardening.

---

## 12. Quick reference

### Feature flags (`ReactorFeatureFlags.cs`)

- `UseHookBasedPaging` (default `false`) — DataGrid reads paged data through
  `UseInfiniteResource` rather than the legacy `DataPageCache`. Flips on in
  phase 3.
- `FocusRevalidation` (default `false`) — enable window-activation sweep.
  Per-hook opt-in via `ResourceOptions.RefetchOnWindowFocus`.

### Defaults matrix

| Knob | Default | Source |
|---|---|---|
| `ResourceOptions.StaleTime` | `TimeSpan.Zero` | `UseResource.cs:20` |
| `ResourceOptions.CacheTime` | 5 min | `UseResource.cs:21` |
| `ResourceOptions.RetryCount` | 0 | `UseResource.cs:14` |
| `ResourceOptions.RefetchOnMount` | `true` | `UseResource.cs:15` |
| `ResourceOptions.RefetchOnWindowFocus` | `false` | `UseResource.cs:17` |
| `InfiniteResourceOptions.PageSize` | 50 | `InfiniteResource.cs:42` |
| `InfiniteResourceOptions.MaxLoadedPages` | `null` (unbounded) | `InfiniteResource.cs:43` |
| `QueryCache.EvictionPollInterval` | 1 s | `QueryCache.cs:52` |
| `FocusRevalidationService.ThrottleWindow` | 30 s | `FocusRevalidationService.cs:33` |
| Retry backoff | `100 * 2^attempt` ms | `UseResource.cs:310` |

### Public API shapes

```csharp
// Single fetch.
AsyncValue<T> UseResource<T>(
    Func<CancellationToken, Task<T>> fetcher,
    object[] deps,
    ResourceOptions? options = null);

// Paginated fetch.
InfiniteResource<TItem> UseInfiniteResource<TItem, TCursor>(
    Func<TCursor?, CancellationToken, Task<Page<TItem, TCursor>>> fetchPage,
    object[] deps,
    InfiniteResourceOptions? options = null);

// Write.
Mutation<TInput, TResult> UseMutation<TInput, TResult>(
    Func<TInput, CancellationToken, Task<TResult>> mutator,
    MutationOptions<TInput, TResult>? options = null);

// IDataSource<T> adapter.
InfiniteResource<T> UseDataSource<T>(
    this RenderContext ctx,
    IDataSource<T> source,
    DataRequest request,
    QueryCache cache,
    InfiniteResourceOptions? options = null);

// Bubble-up fallback.
Element Pending(Element fallback, Element child);
```

### Contexts (`AppContexts` in `QueryCache.cs:341-366`)

- `Context<QueryCache> QueryCache` — the ambient cache. Override per test /
  per subtree with `.Provide`.
- `Context<PendingScope?> PendingScope` — nearest ancestor scope, or null.
- `Context<FocusRevalidationService?> FocusRevalidation` — nearest service.

---

## 13. See also

- [`docs/specs/020-async-resources-design.md`](../specs/020-async-resources-design.md)
  — the design specification; this reference paper's intent.
- [`docs/guide/hooks-internals.md`](../guide/hooks-internals.md) — the
  hook / state foundation that async resources build on.
- [`docs/guide/reconciliation.md`](../guide/reconciliation.md) — how
  component unmount / effect-cleanup / rerender flows work, which this
  system depends on.
- `tests/Reactor.Tests/Core/UseResourceTests.cs` and siblings —
  deterministic test harness that exercises every transition described here.
- `tests/Reactor.AppTests.Host/SelfTest/Fixtures/AsyncResource*Fixtures.cs` —
  framerate and snapshot selfhost fixtures built on top of the hook surface.
