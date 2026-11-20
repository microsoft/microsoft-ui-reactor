using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Core;

public class UseInfiniteResourceTests
{
    private sealed class InlineDispatcher : IHookDispatcher
    {
        public void Post(Action action) => action();
    }

    private static QueryCache NewCache()
    {
        var cache = new QueryCache();
        var t = DateTime.UtcNow;
        cache.UtcNow = () => t;
        return cache;
    }

    /// <summary>
    /// Synthetic fetcher producing sequential integer pages with cursor paging.
    /// Cursor is the next start index as a string; null means end.
    /// </summary>
    private static Func<string?, CancellationToken, Task<Page<int, string>>> FakeFetcher(
        int totalItems, int pageSize, int? reportedTotal = null)
    {
        return (cursor, ct) =>
        {
            int start = cursor is null ? 0 : int.Parse(cursor);
            int count = Math.Min(pageSize, Math.Max(0, totalItems - start));
            var items = Enumerable.Range(start, count).ToList();
            string? next = start + count >= totalItems ? null : (start + count).ToString();
            return Task.FromResult(new Page<int, string>(items, next, reportedTotal));
        };
    }

    // ────────── First render ──────────

    [Fact]
    public void First_Render_Schedules_First_Page_And_Reports_Loading()
    {
        var cache = NewCache();
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var resource = ctx.UseInfiniteResource(FakeFetcher(100, 20), cache, Array.Empty<object>(),
            new InfiniteResourceOptions(PageSize: 20), new InlineDispatcher());

        // Fetcher is sync-complete in our InlineDispatcher world, so Loading collapses to Idle
        // by the time the hook returns.
        Assert.IsType<LoadState.Idle>(resource.LoadState);
        Assert.Equal(20, resource.Items.Count);
        Assert.True(resource.HasMore);
    }

    [Fact]
    public void Single_Page_At_End_Transitions_To_EndOfList()
    {
        var cache = NewCache();
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var resource = ctx.UseInfiniteResource(FakeFetcher(10, 20), cache, Array.Empty<object>(),
            new InfiniteResourceOptions(PageSize: 20), new InlineDispatcher());

        Assert.IsType<LoadState.EndOfList>(resource.LoadState);
        Assert.False(resource.HasMore);
        Assert.Equal(10, resource.Items.Count);
    }

    // ────────── ItemAt / EnsureRange ──────────

    [Fact]
    public void ItemAt_On_Loaded_Page_Returns_Item()
    {
        var cache = NewCache();
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var resource = ctx.UseInfiniteResource(FakeFetcher(100, 20), cache, Array.Empty<object>(),
            new InfiniteResourceOptions(PageSize: 20), new InlineDispatcher());

        Assert.Equal(5, resource.ItemAt(5));
    }

    [Fact]
    public void ItemAt_Beyond_Known_End_Returns_Null_And_Does_Not_Fetch()
    {
        var cache = NewCache();
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var resource = ctx.UseInfiniteResource(FakeFetcher(10, 20, reportedTotal: 10), cache, Array.Empty<object>(),
            new InfiniteResourceOptions(PageSize: 20), new InlineDispatcher());

        Assert.Equal(0, resource.ItemAt(999));
        Assert.IsType<LoadState.EndOfList>(resource.LoadState);
    }

    [Fact]
    public void ItemAt_On_Unloaded_Page_Triggers_Fetch()
    {
        var cache = NewCache();
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        int pageCalls = 0;
        Func<string?, CancellationToken, Task<Page<int, string>>> fetcher = (c, _) =>
        {
            Interlocked.Increment(ref pageCalls);
            int start = c is null ? 0 : int.Parse(c);
            int count = 20;
            var items = Enumerable.Range(start, count).ToList();
            return Task.FromResult(new Page<int, string>(items, (start + count).ToString()));
        };

        var resource = ctx.UseInfiniteResource(fetcher, cache, Array.Empty<object>(),
            new InfiniteResourceOptions(PageSize: 20), new InlineDispatcher());

        // Access into page 2 triggers loading of pages 0, 1, 2.
        resource.ItemAt(45);

        Assert.Equal(3, pageCalls);
        Assert.Equal(45, resource.ItemAt(45));
    }

    [Fact]
    public void EnsureRange_Loads_Overlapping_Pages()
    {
        var cache = NewCache();
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        int calls = 0;
        Func<string?, CancellationToken, Task<Page<int, string>>> fetcher = (c, _) =>
        {
            Interlocked.Increment(ref calls);
            int start = c is null ? 0 : int.Parse(c);
            var items = Enumerable.Range(start, 10).ToList();
            return Task.FromResult(new Page<int, string>(items, (start + 10).ToString()));
        };

        var resource = ctx.UseInfiniteResource(fetcher, cache, Array.Empty<object>(),
            new InfiniteResourceOptions(PageSize: 10), new InlineDispatcher());

        int beforeCalls = calls;
        resource.EnsureRange(15, 34); // pages 1, 2, 3 (page 0 already loaded on first render)
        Assert.Equal(beforeCalls + 3, calls);
    }

    // ────────── FetchNext / Retry / Refresh ──────────

    [Fact]
    public void FetchNext_Advances_With_Cursor()
    {
        var cache = NewCache();
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var resource = ctx.UseInfiniteResource(FakeFetcher(100, 10), cache, Array.Empty<object>(),
            new InfiniteResourceOptions(PageSize: 10), new InlineDispatcher());

        Assert.Equal(10, resource.Items.Count);
        resource.FetchNext();
        Assert.Equal(20, resource.Items.Count);
        resource.FetchNext();
        Assert.Equal(30, resource.Items.Count);
    }

    [Fact]
    public void Retry_On_Error_Refetches_Failed_Page()
    {
        var cache = NewCache();
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        int call = 0;
        Func<string?, CancellationToken, Task<Page<int, string>>> fetcher = (c, _) =>
        {
            call++;
            if (call == 1) return Task.FromException<Page<int, string>>(new InvalidOperationException("fail"));
            int start = c is null ? 0 : int.Parse(c);
            return Task.FromResult(new Page<int, string>(
                Enumerable.Range(start, 10).ToList(), (start + 10).ToString()));
        };

        var resource = ctx.UseInfiniteResource(fetcher, cache, Array.Empty<object>(),
            new InfiniteResourceOptions(PageSize: 10), new InlineDispatcher());

        Assert.IsType<LoadState.Error>(resource.LoadState);
        resource.Retry();
        Assert.IsType<LoadState.Idle>(resource.LoadState);
        Assert.Equal(10, resource.Items.Count);
    }

    // ────────── Deps change ──────────

    [Fact]
    public void Deps_Change_Resets_Pagination()
    {
        var cache = NewCache();
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var r1 = ctx.UseInfiniteResource(FakeFetcher(100, 10), cache, new object[] { "search:a" },
            new InfiniteResourceOptions(PageSize: 10), new InlineDispatcher());
        r1.FetchNext();
        Assert.Equal(20, r1.Items.Count);

        ctx.BeginRender(() => { });
        var r2 = ctx.UseInfiniteResource(FakeFetcher(100, 10), cache, new object[] { "search:b" },
            new InfiniteResourceOptions(PageSize: 10), new InlineDispatcher());

        // Fresh resource for the new deps.
        Assert.Equal(10, r2.Items.Count);
    }

    [Fact]
    public void Page_Cache_Keeps_Initial_Size_Across_Options_And_Prefix_Changes()
    {
        using var cache = NewCache();
        var ctx = new RenderContext();
        var returning = new RenderContext();
        int calls = 0;
        var inner = FakeFetcher(101, 10, reportedTotal: 101);
        Task<Page<int, string>> Fetch(string? cursor, CancellationToken token)
        {
            calls++;
            return inner(cursor, token);
        }

        InfiniteResource<int> Render(RenderContext target, int pageSize, string prefix)
        {
            target.BeginRender(() => { });
            var resource = target.UseInfiniteResource<int, string>(Fetch, cache, [],
                new InfiniteResourceOptions(PageSize: pageSize, CacheKeyPrefix: prefix), new InlineDispatcher());
            target.FlushEffects();
            return resource;
        }

        try
        {
            var first = Render(ctx, 10, "first");
            Assert.Same(first, Render(ctx, 20, "first"));
            Assert.Equal(1, calls);

            var restarted = Render(ctx, 20, "second");
            Assert.NotSame(first, restarted);
            Assert.Equal(9, restarted.ItemAt(9));
            restarted.EnsureRange(0, 19);
            Assert.Equal(19, restarted.ItemAt(19));
            Assert.Equal(3, calls);

            // A fresh size-10 hook must reuse the restarted hook's size-10 pages.
            var shared = Render(returning, 10, "second");
            shared.EnsureRange(0, 19);
            Assert.Equal(19, shared.ItemAt(19));
            Assert.Equal(3, calls);
        }
        finally { ctx.RunCleanups(); returning.RunCleanups(); }
    }

    // ────────── LRU eviction ──────────

    [Fact]
    public void LRU_Evicts_Oldest_Page_Over_Cap()
    {
        var cache = NewCache();
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        // Start item IDs at 100 so default(int)=0 is an unambiguous "placeholder" signal.
        Func<string?, CancellationToken, Task<Page<int, string>>> fetcher = (c, _) =>
        {
            int start = c is null ? 100 : int.Parse(c);
            var items = Enumerable.Range(start, 10).ToList();
            return Task.FromResult(new Page<int, string>(items, (start + 10).ToString()));
        };

        var resource = ctx.UseInfiniteResource(fetcher, cache, Array.Empty<object>(),
            new InfiniteResourceOptions(PageSize: 10, MaxLoadedPages: 3), new InlineDispatcher());

        resource.FetchNext(); // page 1 → items 110..119
        resource.FetchNext(); // page 2 → items 120..129
        resource.FetchNext(); // page 3 — evicts page 0

        Assert.Equal(0, resource.Items[0]);   // page 0 evicted: placeholder
        Assert.Equal(110, resource.Items[10]); // page 1 still loaded
    }

    // ────────── Unmount ──────────

    [Fact]
    public void Unmount_Cancels_InFlight_Pages()
    {
        var cache = NewCache();
        var dispatcher = new InlineDispatcher();
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        var gate = new TaskCompletionSource<Page<int, string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<string?, CancellationToken, Task<Page<int, string>>> fetcher = (_, _) => gate.Task;
        var resource = ctx.UseInfiniteResource(fetcher, cache, Array.Empty<object>(),
            new InfiniteResourceOptions(PageSize: 10), dispatcher);
        ctx.FlushEffects();

        Assert.IsType<LoadState.Loading>(resource.LoadState);
        ctx.RunCleanups();

        gate.SetResult(new Page<int, string>(new[] { 1, 2, 3 }, null));
        // Nothing should have updated the resource after unmount — no assertion beyond
        // no crash. Load state stays at Loading since we unmounted.
    }

    // ────────── Refresh() length preservation (D17 stable-length contract) ──────────

    /// <summary>
    /// D17 stable-length contract: once the server reports a <c>TotalCount</c>, a
    /// <c>Refresh()</c> that returns a page of the same shape must preserve the
    /// <c>Items.Count</c> length so consumer-side scroll position remains valid across
    /// the refetch. The list is a sparse virtualized view keyed by index — shrinking it
    /// would force the UI to remap row positions mid-scroll.
    /// </summary>
    [Fact]
    public void Refresh_With_Same_TotalCount_Preserves_Items_Length()
    {
        var cache = NewCache();
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        var resource = ctx.UseInfiniteResource(FakeFetcher(100, 10, reportedTotal: 100), cache,
            Array.Empty<object>(),
            new InfiniteResourceOptions(PageSize: 10), new InlineDispatcher());

        Assert.Equal(100, resource.Items.Count); // length set from TotalCount
        int beforeCount = resource.Items.Count;

        resource.Refresh();

        // After Refresh the resource is a fresh instance but the length should still be 100
        // because the fetcher reports the same TotalCount on page 0.
        Assert.Equal(beforeCount, resource.Items.Count);
        Assert.Equal(0, resource.Items[0]);
    }
}
