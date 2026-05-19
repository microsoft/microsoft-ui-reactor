using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<PaginatedListApp>("Paginated List Recipe", width: 420, height: 520
#if DEBUG
    , preview: true
#endif
);

class PaginatedListApp : Component
{
    public override Element Render() => Component<CommitFeed>();
}

// <snippet:data>
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
// </snippet:data>

class CommitFeed : Component
{
    public override Element Render()
    {
        // <snippet:fetch>
        // UseInfiniteResource owns the fetch lifecycle: cancellation on deps-change,
        // dedup of in-flight pages, a flat sparse `Items` list (null = unloaded slot),
        // and a `LoadState` discriminator the UI pattern-matches against.
        var commits = UseInfiniteResource<Commit, string>(
            fetchPage: (cursor, ct) => FakeApi.GetCommitsAsync(cursor, ct),
            deps: new object[] { "commits" });
        // </snippet:fetch>

        // <snippet:state>
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
        // </snippet:state>

        // <snippet:render>
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
        // </snippet:render>
    }
}
