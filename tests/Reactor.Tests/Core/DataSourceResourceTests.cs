using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Data.Providers;
using Microsoft.UI.Reactor.Hooks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Core;

public class DataSourceResourceTests
{
    private sealed record Row(int Id);

    private sealed class InlineDispatcher : IHookDispatcher
    {
        public void Post(Action action) => action();
    }

    // Minimal in-memory IDataSource<int> for test purposes.
    private sealed class InMemorySource : IDataSource<int>
    {
        public int CallCount;
        private readonly int _totalItems;
        private readonly int _pageSize;

        public InMemorySource(int total, int pageSize)
        {
            _totalItems = total;
            _pageSize = pageSize;
        }

        public DataSourceCapabilities Capabilities => DataSourceCapabilities.ServerCount;

        public RowKey GetRowKey(int item) => item;

        public Task<DataPage<int>> GetPageAsync(DataRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref CallCount);
            int start = request.ContinuationToken is null ? 0 : int.Parse(request.ContinuationToken);
            int count = Math.Min(_pageSize, Math.Max(0, _totalItems - start));
            var items = Enumerable.Range(start, count).ToList();
            string? next = start + count >= _totalItems ? null : (start + count).ToString();
            return Task.FromResult(new DataPage<int>(items, next, _totalItems));
        }
    }

    [Fact]
    public void UseDataSource_Projects_IDataSource_To_InfiniteResource()
    {
        var cache = new QueryCache();
        var source = new InMemorySource(total: 50, pageSize: 10);
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        var resource = ctx.UseDataSource(source, new DataRequest { PageSize = 10 }, cache,
            new InfiniteResourceOptions(PageSize: 10), new InlineDispatcher());

        Assert.Equal(50, resource.TotalCount);
        Assert.Equal(1, source.CallCount); // first page

        resource.FetchNext();
        Assert.Equal(2, source.CallCount);
        // With a known TotalCount, Items.Count reports the full length with placeholder
        // slots for unloaded pages. The first 20 are loaded; slot 25 is still a placeholder.
        Assert.Equal(50, resource.Items.Count);
        Assert.Equal(0, resource.Items[0]);   // item 0
        Assert.Equal(19, resource.Items[19]); // last loaded item
    }

    [Fact]
    public void Deps_Derived_From_Request_Change_Restart_Pagination()
    {
        var cache = new QueryCache();
        var source = new InMemorySource(total: 50, pageSize: 10);
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        ctx.UseDataSource(source, new DataRequest { PageSize = 10, SearchQuery = "a" }, cache,
            new InfiniteResourceOptions(PageSize: 10), new InlineDispatcher());
        Assert.Equal(1, source.CallCount);

        ctx.BeginRender(() => { });
        ctx.UseDataSource(source, new DataRequest { PageSize = 10, SearchQuery = "b" }, cache,
            new InfiniteResourceOptions(PageSize: 10), new InlineDispatcher());

        // Different search query → deps change → new first page fetch.
        Assert.Equal(2, source.CallCount);
    }

    [Theory]
    [InlineData(10, null, 10)]
    [InlineData(10, 20, 20)]
    [InlineData(20, 10, 10)]
    [InlineData(10, 10, 10)]
    [InlineData(0, null, 1)]
    [InlineData(10, 0, 1)]
    public void UseDataSource_Uses_One_PageSize_For_Requests_Cursors_And_Row_Positions(
        int requestPageSize, int? optionsPageSize, int expectedPageSize)
    {
        using var cache = new QueryCache();
        var rows = Enumerable.Range(0, 101).Select(id => new Row(id)).ToArray();
        var source = new ListDataSource<Row>(rows, row => row.Id);
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        var resource = ctx.UseDataSource(source, new DataRequest { PageSize = requestPageSize }, cache,
            optionsPageSize is { } size ? new InfiniteResourceOptions(PageSize: size) : null,
            new InlineDispatcher());

        Assert.Equal(expectedPageSize, resource.Items.TakeWhile(row => row is not null).Count());
        resource.EnsureRange(0, rows.Length - 1);

        Assert.Equal(rows.Length, resource.TotalCount);
        Assert.Equal(rows.Length, resource.Items.Count);
        for (int i = 0; i < rows.Length; i++)
            Assert.Equal(rows[i], resource.Items[i]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void UseDataSource_Preserves_Options_Across_Renders_And_Prefix_Changes(int pageSize)
    {
        using var cache = new QueryCache();
        var rows = Enumerable.Range(0, 21).Select(id => new Row(id)).ToArray();
        var source = new ListDataSource<Row>(rows, row => row.Id);
        var ctx = new RenderContext();
        var options = new InfiniteResourceOptions(PageSize: pageSize, MaxLoadedPages: 1,
            StaleTime: TimeSpan.FromMinutes(2), CacheTime: TimeSpan.FromMinutes(3), CacheKeyPrefix: "first");
        string? latestKey = null;
        cache.EntryChanged += key => latestKey = key;

        InfiniteResource<Row> Render(InfiniteResourceOptions currentOptions)
        {
            ctx.BeginRender(() => { });
            var resource = ctx.UseDataSource(source, new DataRequest { PageSize = 10 }, cache,
                currentOptions, new InlineDispatcher());
            ctx.FlushEffects();
            return resource;
        }

        try
        {
            var resource = Render(options);
            Assert.Same(resource, Render(options));
            resource.FetchNext();
            Assert.Equal(rows[Math.Max(1, pageSize)], resource.Items[Math.Max(1, pageSize)]);
            Assert.Null(resource.Items[0]); // MaxLoadedPages still evicts the first page.

            var restarted = Render(options with { CacheKeyPrefix = "second" });
            Assert.NotSame(resource, restarted);
            Assert.Equal(rows[0], restarted.Items[0]);
            Assert.StartsWith("second/", latestKey!);
            Assert.True(cache.TryGet<Page<Row, string>>(latestKey!, out var entry));
            Assert.Equal(options.StaleTime, entry.StaleTime);
            Assert.Equal(pageSize, options.PageSize);
        }
        finally { ctx.RunCleanups(); }
    }

    /// <summary>
    /// Regression: deep scroll against an offset-paginated <c>IDataSource&lt;T&gt;</c> used
    /// to hang because <c>EnsureRange</c> claimed in-flight slots for unreachable
    /// pages and <c>RequestPage</c> bailed without clearing the claim or chaining
    /// forward. With the <c>CursorFromPageIndex</c> short-circuit baked into
    /// <c>UseDataSource</c>, the cursor for any page is computed directly from
    /// its index, so only the pages that actually cover the requested range get
    /// fetched — no sequential walk through intermediate pages.
    /// </summary>
    [Fact]
    public void EnsureRange_Deep_Scroll_Skips_Intermediate_Pages()
    {
        var cache = new QueryCache();
        var source = new InMemorySource(total: 1000, pageSize: 10);
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        var resource = ctx.UseDataSource(
            source,
            new DataRequest { PageSize = 10 },
            cache,
            new InfiniteResourceOptions(PageSize: 10),
            new InlineDispatcher());

        // Page 0 is fetched eagerly.
        Assert.Equal(1, source.CallCount);

        // Scroll to row 200 — pages 20..22 cover the visible range. With the
        // offset-based cursor short-circuit, only pages 20, 21, 22 get fetched.
        resource.EnsureRange(200, 220);

        // 1 (page 0) + 3 (pages 20-22) = 4.
        Assert.Equal(4, source.CallCount);

        // The tail of the requested range should be populated.
        Assert.Equal(200, resource.Items[200]);
        Assert.Equal(220, resource.Items[220]);
    }
}
