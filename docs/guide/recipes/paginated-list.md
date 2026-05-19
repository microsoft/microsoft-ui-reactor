
# Recipe: Paginated List

A paginated list is four states sharing one source of truth: an initial
load, a populated list, a "load the next page" affordance, and a terminal
"no more results." [`UseInfiniteResource`](../async-resources.md) owns
all four — the component below pattern-matches on `LoadState` and reads
`Items` directly; there is no local `UseState` for loading or errors.

## Primitives

| Concern | API |
|---|---|
| Cursor-paginated fetch | [`UseInfiniteResource`](../async-resources.md) |
| Page payload + cursor | [`Page<TItem, TCursor>`](../async-resources.md) |
| Lifecycle discriminator | `LoadState.Loading / Idle / EndOfList / Error` |
| Flat sparse view | `InfiniteResource.Items` (nulls are unloaded slots) |
| Manual page advance | `commits.FetchNext()` / `commits.Retry()` |

### Data + fetcher

```csharp
// The recipe is API-shape-agnostic: the fetcher returns a Page<TItem, TCursor>
// regardless of whether the backend is REST, gRPC, or — as here — an in-process
// fake. The cursor is whatever the server hands back; null signals end-of-list.
record Commit(string Sha, string Message);

static class FakeApi
{
    private static readonly Commit[] All = Enumerable.Range(0, 23)
        .Select(i => new Commit($"sha-{i:000}", $"Refactor module {i}"))
        .ToArray();

    public static async Task<Page<Commit, string>> GetCommitsAsync(string? cursor, CancellationToken ct)
    {
        await Task.Delay(450, ct);              // simulate network latency
        const int pageSize = 5;
        int offset = cursor is null ? 0 : int.Parse(cursor);
        var slice = All.Skip(offset).Take(pageSize).ToArray();
        int next = offset + slice.Length;
        string? nextCursor = next >= All.Length ? null : next.ToString();
        return new Page<Commit, string>(slice, nextCursor, TotalCount: All.Length);
    }
}
```

The fetcher's only contract is `(cursor, ct) -> Task<Page<TItem, TCursor>>`.
The cursor is opaque to Reactor — pass back whatever your server speaks
(an offset, an opaque continuation string, a record id). A null
`NextCursor` is how you signal end-of-list; that's the only way `LoadState`
transitions to `EndOfList`.

### One hook call

```csharp
// UseInfiniteResource owns the fetch lifecycle: cancellation on deps-change,
// dedup of in-flight pages, a flat sparse `Items` list (null = unloaded slot),
// and a `LoadState` discriminator the UI pattern-matches against.
var commits = UseInfiniteResource<Commit, string>(
    fetchPage: (cursor, ct) => FakeApi.GetCommitsAsync(cursor, ct),
    deps: new object[] { "commits" });
```

`UseInfiniteResource` registers the fetcher, owns the cancellation token,
and survives re-renders. The `deps` array is the cache key — change it
(e.g. when a filter flips) and the hook cancels in-flight pages, drops
the page table, and refetches from page 0. See
[`UseState`](../hooks.md) when you need a sibling piece of UI state
(filter, sort) — it pairs naturally because the setter triggers a
re-render and `deps` then drives the restart.

### Derive UI state from the resource

```csharp
// The hook exposes three observable signals the UI cares about:
//   - LoadState  — Loading / Idle / EndOfList / Error
//   - Items      — sparse flat list (null entries are in-flight or unloaded)
//   - HasMore    — false once the server reported a null NextCursor
// Everything below derives from these — no local UseState for "is loading"
// or "did it fail"; the hook is the source of truth.
var loadedItems = commits.Items.OfType<Commit>().ToArray();
var isInitialLoad = commits.LoadState is LoadState.Loading && loadedItems.Length == 0;
var error = commits.LoadState as LoadState.Error;
var atEnd = commits.LoadState is LoadState.EndOfList;
var loadingMore = commits.LoadState is LoadState.Loading && loadedItems.Length > 0;
```

There is no `(loading, setLoading)` and no `(error, setError)` in the
component. `LoadState` is the discriminator; `Items.Count` is the
loaded-anything-yet predicate. Deriving those locally on every render
is fine — the work is pure C# and the reconciler skips slots that
didn't change.

### Render the four states

```csharp
Element body;
if (isInitialLoad)
{
    body = TextBlock("Loading…").Opacity(0.6).Padding(20);
}
else if (error is not null && loadedItems.Length == 0)
{
    body = VStack(8,
        TextBlock($"Couldn't load commits: {error.Exception.Message}")
            .Foreground("#C42B1C"),
        Button("Retry", () => commits.Retry())
    ).Padding(20);
}
else if (loadedItems.Length == 0)
{
    body = TextBlock("No commits yet.").Opacity(0.6).Padding(20);
}
else
{
    body = VStack(2,
        loadedItems.Select(c =>
            HStack(8,
                TextBlock(c.Sha).Opacity(0.5).Width(72),
                TextBlock(c.Message)
            ).Padding(6)
        ).ToArray()
    );
}

// The footer is the load-more sentinel: a button while there's another page,
// a label once the server reported end-of-list, and a Retry on per-page error.
Element footer = atEnd
    ? TextBlock("— end of list —").Opacity(0.5).Padding(12)
    : error is not null && loadedItems.Length > 0
        ? Button($"Retry — {error.Exception.Message}", () => commits.Retry()).Padding(8)
        : Button(
            loadingMore ? "Loading more…" : $"Load more ({commits.EstimatedRemaining} remaining)",
            () => commits.FetchNext()
          ).IsEnabled(!loadingMore).Padding(8);

return VStack(0,
    Heading($"Commits ({commits.TotalCount ?? loadedItems.Length})").Padding(20),
    body,
    footer
).Width(400);
```

![Paginated list after two pages and a Load More button](images/recipe-paginated-list/loaded.png)

The body branches on three predicates: initial load (skeleton), first-page
error with zero loaded items (full-screen retry), or a populated list.
The footer is the sentinel — a `Button("Load more")` while pages remain,
a "— end of list —" label once the server reported `NextCursor == null`,
or a per-page retry when a follow-up page errored after a successful first
load. Loaded items survive across re-renders because they live inside the
hook's `Items` view, not in local state.

## Tips

**Don't reach for `UseState` to remember pages.** The hook's `Items` is
the page store. Mirroring it into a local list duplicates state — and the
local copy will go stale across the next deps-change refetch.

**Cursor paging is serial; offset paging is parallel.** Cursor mode
chains fetches because page *N*'s cursor lives in page *N-1*. If your
server speaks offsets, use the `cursorFromPageIndex` parameter on
[`UseInfiniteResource`](../async-resources.md) so deep scrolls fetch
pages concurrently.

**Promote to a virtualizer at row counts that matter.** A button-driven
load-more is right for ~5-200 rows. Once the list grows past the
viewport's worth of pages, drive fetches from a virtualizer's
`ItemAt(i)` (see [Collections](../collections.md)) — the same hook
backs both flows.

**Distinguish first-page error from follow-up error.** A failure on the
initial fetch should take the whole list area; a failure loading page 3
should only replace the footer button. The recipe branches on
`loadedItems.Length` to choose between them.

## Next Steps

- **[Async Resources](../async-resources.md)** — Full state-machine
  reference for `UseInfiniteResource`, including `Refresh()` and the
  Pending fallback story.
- **[Collections](../collections.md)** — Promote the recipe's
  `VStack`-of-rows to a virtualized `ListView<T>` when the page count
  outpaces the viewport.
- **[Data System](../data-system.md)** — When the source already speaks
  `IDataSource<T>`, use `UseDataSource` to bridge onto this hook with
  offset-based parallel paging.
- **[Recipe: Master-detail](master-detail.md)** — Sibling recipe — pair
  this list with a detail pane by lifting `selectedId` into a parent
  component.
- **[Recipes index](index.md)** — Back to the gallery.
