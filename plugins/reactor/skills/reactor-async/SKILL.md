---
name: reactor-async
description: "Async data in Reactor — `UseResource` (single fetch), `UseMutation` (writes + optimistic updates), `UseInfiniteResource` (cursor pagination), and `Pending` (bubble-up fallback). Use when fetching, caching, paginating, or mutating server data."
---

# Async Resources in Reactor

Reactor replaces the `UseState + UseEffect + manual-cancellation` dance with
three hooks. **Reads return `AsyncValue<T>`, writes return a `Mutation`
handle.** The hook owns the cancellation token, dedups across siblings,
caches results in a shared `QueryCache`, and drops late results on unmount.

Available on any `Component` subclass directly; on `RenderContext` as
extension methods for function components (`Memo(ctx => ...)` or
`RenderEachTime(ctx => ...)`).

## 1. UseResource — single fetch

```csharp
AsyncValue<T> UseResource<T>(
    Func<CancellationToken, Task<T>> fetcher,
    object[] deps,
    ResourceOptions? options = null);
```

`AsyncValue<T>` is a sealed 4-state record: `Loading`, `Data(T Value)`,
`Error(Exception)`, `Reloading(T Previous)` (stale-while-revalidate).
Pattern-match with a `switch`:

```csharp
class UserCard : Component
{
    public override Element Render()
    {
        var user = UseResource(
            ct => Api.GetUserAsync(userId, ct),
            deps: new object[] { userId });

        return user switch
        {
            AsyncValue<User>.Loading   => TextBlock("Loading…"),
            AsyncValue<User>.Data d    => TextBlock(d.Value.Name).Bold(),
            AsyncValue<User>.Reloading r => TextBlock(r.Previous.Name).Opacity(0.5),
            AsyncValue<User>.Error e   => TextBlock($"Error: {e.Exception.Message}"),
            _ => Empty()
        };
    }
}
```

Or use `user.Match<Element>(loading, data, error, reloading?)` — `reloading`
defaults to `data` so stale values stay visible.

**Rules:**
- Fetcher must be **idempotent** (reads only — the analyzer `REACTOR_HOOKS_006`
  warns on names like `Post*`, `Create*`, `Delete*`, `Update*`, `Save*`, `Send*`).
  For writes, use `UseMutation`.
- Fetcher must observe `ct` — return a cancelled task or check
  `ct.IsCancellationRequested`. Cancellation fires on deps change, unmount,
  and cache invalidation.
- `deps` must be value-comparable. Don't pass new lambdas or freshly
  constructed collections each render — memoize with `UseMemo` or use
  scalar values.

**`ResourceOptions` (all optional):**

| Option | Default | Meaning |
|---|---|---|
| `StaleTime` | `0s` | Within this window, return `Data` without refetching. After, enter `Reloading` and refetch. |
| `CacheTime` | `5m` | Keep entry in cache this long after zero subscribers. |
| `RetryCount` | `0` | Exponential backoff: 100 × 2^attempt ms. |
| `RefetchOnMount` | `true` | Refetch even on cache hit. |
| `RefetchOnWindowFocus` | `false` | Invalidate stale entries on window activation. |
| `CacheKey` | `null` | Override auto-key. Default: `"{hookId}/{hash(deps)}"`. |

## 2. UseMutation — writes with optimistic updates

```csharp
Mutation<TInput, TResult> UseMutation<TInput, TResult>(
    Func<TInput, CancellationToken, Task<TResult>> mutator,
    MutationOptions<TInput, TResult>? options = null);
```

The returned `Mutation` is stable across renders. Properties:
`IsPending`, `Error?`, `LastResult?`. Call `mutation.RunAsync(input)` from
click handlers; call `mutation.Reset()` to clear error/result.

```csharp
var (todos, setTodos) = UseState<IReadOnlyList<Todo>>([]);

var addTodo = UseMutation<TodoInput, Todo>(
    mutator: (input, ct) => Api.AddTodoAsync(input, ct),
    options: new MutationOptions<TodoInput, Todo>(
        // Synchronous — lands in the next render before the network call.
        OnOptimistic: input =>
            setTodos([.. todos, new Todo(input.Title, IsTemporary: true)]),
        OnSuccess: (todo, _) =>
            setTodos([.. todos.Select(t => t.IsTemporary ? todo : t)]),
        OnError: (_, input) =>
            setTodos([.. todos.Where(t => t.Title != input.Title)]),
        // Any sibling UseResource subscribed to these keys refetches.
        InvalidateKeys: ["todos/list"]));

return VStack(
    Button("Add", () => _ = addTodo.RunAsync(new TodoInput("New")))
        .Disabled(addTodo.IsPending));
```

- `OnOptimistic` runs synchronously on the caller. If it throws, the mutator
  never fires — prevents half-applied state.
- `InvalidateKeys` fires on success only, **not** on error.
- Overlapping `RunAsync` calls are allowed; each has its own token. Gate
  with `IsPending` if you need serial semantics.
- Unmount cancels the mutator's token. `OnError` is silent on cancellation.

## 3. UseInfiniteResource — cursor pagination

```csharp
InfiniteResource<TItem> UseInfiniteResource<TItem, TCursor>(
    Func<TCursor?, CancellationToken, Task<Page<TItem, TCursor>>> fetchPage,
    object[] deps,
    InfiniteResourceOptions? options = null);  // PageSize default 50
```

**Pull model.** The virtualizer calls `resource.ItemAt(i)` per visible row;
the hook fetches whichever page covers `i`, dedups, and caches. Unfetched
or in-flight slots return `null` (render a shimmer).

```csharp
var commits = UseInfiniteResource<Commit, string>(
    fetchPage: async (cursor, ct) =>
    {
        var (items, next, total) = await Api.GetCommitsPageAsync(cursor, ct);
        return new Page<Commit, string>(items, next, total);
    },
    deps: new object[] { repoId });

// In your virtualizer's renderItem:
var commit = commits.ItemAt(rowIndex);
return commit is null
    ? TextBlock("…").Opacity(0.4)
    : TextBlock($"{commit.Sha} — {commit.Message}");
```

**Resource API:**

| Member | Purpose |
|---|---|
| `Items : IReadOnlyList<TItem?>` | Flat virtual array; null = placeholder. |
| `TotalCount?`, `HasMore`, `EstimatedRemaining` | Metadata. |
| `LoadState` | `Loading` / `Idle` / `EndOfList` / `Error(ex)` — aggregate of most recent page fetch. |
| `ItemAt(index)` | Get item; triggers page fetch if uncached. |
| `EnsureRange(first, last)` | Batch prefetch — call from `onVisibleRangeChanged`. |
| `FetchNext()` | Manually fetch the next page. |
| `Retry()` | Retry the most recent failed page only. |
| `Refresh()` | Invalidate all pages and refetch from page 0 (pull-to-refresh). |

Footer UI off `LoadState`:

```csharp
commits.LoadState switch
{
    LoadState.Loading       => ProgressRing(),
    LoadState.Idle when !commits.HasMore => TextBlock("End of list"),
    LoadState.Error e       => Button("Retry", () => commits.Retry()),
    _ => Empty()
}
```

**Notes:**
- Cursor paging is serial — page N-1 must land before page N (the cursor
  lives in N-1's payload). For parallel offset paging, use an offset as
  the cursor type.
- Deps change cancels all in-flight pages, unsubscribes cache keys, and
  restarts from page 0.

## 4. Pending — bubble-up fallback

Render a single fallback for a subtree whose components each load
independent resources:

```csharp
PendingFactory.Pending(
    fallback: TextBlock("Loading dashboard…").Opacity(0.5),
    child: VStack(
        Component<UserHeader>(),    // has UseResource
        Component<RecentActivity>(),// has UseInfiniteResource
        Component<Stats>()          // has UseResource
    ))
```

- Both trees mount — the child's hooks run in the background.
- Fallback shows while **any** nested resource is in `Loading`.
- `Reloading` (refetch with data present) does **not** re-trigger the
  fallback. No flash on revalidation.

## 5. Gotchas

**Non-idempotent fetcher (`REACTOR_HOOKS_006`).** The analyzer flags
`UseResource(ct => api.PostComment(...))`. Reads retry and refetch; use
`UseMutation` for writes.

**Unstable deps.** `deps: new object[] { userId }` allocates a fresh array
each render — the deps-change detector compares element-wise, so scalar
`userId` is fine, but a fresh `new Filter { ... }` object is not.
Memoize complex deps:

```csharp
var deps = UseMemo(() => new object[] { filter, sort }, filter, sort);
var items = UseResource(ct => Api.SearchAsync(filter, sort, ct), deps);
```

**Don't share `CacheKey` across unrelated hooks.** Auto-key keeps siblings
independent. Explicit `CacheKey` is for two distant subtrees that should
observe the same entry.

**Live-mutation overlay.** `UseInfiniteResource.Items` is server-sourced
and immutable — the hook re-renders when pages land, not when you edit a
row. Keep optimistic edits in a separate `Dictionary<int, T>` overlay that
takes precedence over `Items[i]` at read time.

**`AsyncValue.Match` allocates** one delegate per arm. In per-row render
paths (inside a virtualized list), prefer a `switch` expression for
exhaustiveness checking and zero per-call allocation.

## 6. See also

- `docs/guide/async-resources-cookbook.md` — task-oriented recipes.
- `docs/reference/async-system.md` — full state-machine reference, race
  analysis, known bugs/gaps.
- `docs/specs/020-async-resources-design.md` — the design spec.
