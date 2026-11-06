using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Data.Providers;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Tests.Diagnostics;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Core;

[Collection("DataSourceDiagnostics")]
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

    [Fact]
    public void UseDataSource_Uses_Default_Options_PageSize_When_Only_StaleTime_Is_Set()
    {
        using var cache = new QueryCache();
        var rows = Enumerable.Range(0, 101).Select(id => new Row(id)).ToArray();
        var source = new ListDataSource<Row>(rows, row => row.Id);
        var options = new InfiniteResourceOptions(StaleTime: TimeSpan.FromMinutes(2));
        var ctx = new RenderContext();
        string? latestKey = null;
        cache.EntryChanged += key => latestKey = key;
        ctx.BeginRender(() => { });

        try
        {
            var resource = ctx.UseDataSource(source, new DataRequest { PageSize = 200 }, cache,
                options, new InlineDispatcher());
            ctx.FlushEffects();

            Assert.Equal(50, resource.Items.TakeWhile(row => row is not null).Count());
            Assert.True(cache.TryGet<Page<Row, string>>(latestKey!, out var entry));
            Assert.Equal(options.StaleTime, entry.StaleTime);
            resource.EnsureRange(0, rows.Length - 1);
            Assert.Equal(rows.Length, resource.Items.Count);
            for (int i = 0; i < rows.Length; i++)
                Assert.Equal(rows[i], resource.Items[i]);
        }
        finally { ctx.RunCleanups(); }
    }

    [Theory]
    [InlineData(10, 20, true)]
    [InlineData(20, 10, true)]
    [InlineData(10, 20, false)]
    [InlineData(0, 20, true)]
    public void UseDataSource_Keeps_Initial_PageSize_Across_Renders_And_Query_Restarts(
        int initialPageSize, int nextPageSize, bool includeOptions)
    {
        using var cache = new QueryCache();
        var rows = Enumerable.Range(0, 101).Select(id => new Row(id)).ToArray();
        var source = new ListDataSource<Row>(rows, row => row.Id);
        var ctx = new RenderContext();
        int expectedPageSize = Math.Max(1, initialPageSize);

        InfiniteResource<Row> Render(IDataSource<Row> currentSource, int pageSize)
        {
            ctx.BeginRender(() => { });
            var resource = ctx.UseDataSource(currentSource, new DataRequest { PageSize = pageSize }, cache,
                includeOptions ? new InfiniteResourceOptions(PageSize: pageSize) : null,
                new InlineDispatcher());
            ctx.FlushEffects();
            return resource;
        }

        try
        {
            var resource = Render(source, initialPageSize);
            Assert.Equal(expectedPageSize, resource.Items.TakeWhile(row => row is not null).Count());
            Assert.Same(resource, Render(source, nextPageSize));
            resource.EnsureRange(0, rows.Length - 1);
            for (int i = 0; i < rows.Length; i++)
                Assert.Equal(rows[i], resource.Items[i]);

            var restarted = Render(new ListDataSource<Row>(rows, row => row.Id), nextPageSize);
            Assert.NotSame(resource, restarted);
            Assert.Equal(expectedPageSize, restarted.Items.TakeWhile(row => row is not null).Count());
            restarted.EnsureRange(0, rows.Length - 1);
            for (int i = 0; i < rows.Length; i++)
                Assert.Equal(rows[i], restarted.Items[i]);
        }
        finally { ctx.RunCleanups(); }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(0)]
    public void UseDataSource_Keeps_Initial_PageSize_After_First_Render_Throws(int initialPageSize)
    {
        using var cache = new QueryCache();
        var rows = Enumerable.Range(0, 101).Select(id => new Row(id)).ToArray();
        var source = new ListDataSource<Row>(rows, row => row.Id);
        var ctx = new RenderContext();
        var pending = new PendingScope();
        var contextScope = new ContextScope();
        contextScope.Push(new Dictionary<ContextBase, object?> { [AppContexts.PendingScope] = pending });
        var failure = new InvalidOperationException("Pending notification failed during initialization.");
        void FailInitialization() => throw failure;
        pending.Changed += FailInitialization;

        InfiniteResource<Row> Render(int pageSize)
        {
            ctx.BeginRender(() => { }, contextScope);
            var resource = ctx.UseDataSource(source, new DataRequest { PageSize = pageSize }, cache,
                new InfiniteResourceOptions(PageSize: pageSize), new InlineDispatcher());
            ctx.FlushEffects();
            return resource;
        }

        try
        {
            // This retry scenario requires Register to notify synchronously during state construction.
            var exception = Record.Exception(() => Render(initialPageSize));
            Assert.True(pending.Count == 1,
                "The first render must synchronously register the hook with PendingScope before notifying.");
            Assert.Same(failure, exception);
            pending.Changed -= FailInitialization;

            var resource = Render(20);
            Assert.Equal(Math.Max(1, initialPageSize), resource.Items.TakeWhile(row => row is not null).Count());
            resource.EnsureRange(0, rows.Length - 1);
            for (int i = 0; i < rows.Length; i++)
                Assert.Equal(rows[i], resource.Items[i]);
        }
        finally
        {
            pending.Changed -= FailInitialization;
            ctx.RunCleanups();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void UseDataSource_Preserves_Options_Across_Renders_And_Prefix_Changes(int pageSize)
    {
        var now = DateTime.UtcNow;
        using var cache = new QueryCache { UtcNow = () => now };
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

            var restarted = Render(options with { PageSize = 20, CacheKeyPrefix = "second" });
            Assert.NotSame(resource, restarted);
            Assert.Equal(Math.Max(1, pageSize), restarted.Items.TakeWhile(row => row is not null).Count());
            Assert.Equal(rows[0], restarted.Items[0]);
            Assert.StartsWith("second/", latestKey!);
            Assert.True(cache.TryGet<Page<Row, string>>(latestKey!, out var entry));
            Assert.Equal(options.StaleTime, entry.StaleTime);

            // Verify that the adapter preserves CacheTime: after unsubscribing, the page
            // remains cached until just before expiry and is evicted when that time is reached.
            var pageKey = latestKey!;
            ctx.RunCleanups(); // CacheTime starts when the last subscriber leaves.
            now += options.CacheTime!.Value - TimeSpan.FromTicks(1);
            cache.EvictNow();
            Assert.True(cache.TryGet<Page<Row, string>>(pageKey, out _));
            now += TimeSpan.FromTicks(1);
            cache.EvictNow();
            Assert.False(cache.TryGet<Page<Row, string>>(pageKey, out _));
        }
        finally { ctx.RunCleanups(); }
    }

    [Theory]
    [InlineData(true)]
#if !DEBUG
    // The Debug mirror keeps warnings enabled even without an event listener.
    [InlineData(false)]
#endif
    public void UseDataSource_Reports_PageSize_Changes_Once(bool listenInitially)
    {
        using var cache = new QueryCache();
        var source = new ListDataSource<Row>(Enumerable.Range(0, 40).Select(id => new Row(id)), row => row.Id);
        var ctx = new RenderContext();

        void Render(int pageSize, string prefix = "first")
        {
            ctx.BeginRender(() => { });
            ctx.UseDataSource(source, new DataRequest { PageSize = pageSize }, cache,
                new InfiniteResourceOptions(PageSize: pageSize, CacheKeyPrefix: prefix), new InlineDispatcher());
            ctx.FlushEffects();
        }

        using var initialCapture = listenInitially
            ? ReactorTraceCollector.Capture(EventLevel.Warning, ReactorEventSource.Keywords.Errors) : null;
        if (!listenInitially)
            Assert.False(DiagnosticLog.IsWarningEnabled, "The delayed-listener test requires warnings to be disabled initially.");

        try
        {
            Render(10);
            if (initialCapture is not null)
                Assert.DoesNotContain(initialCapture.ByName(nameof(ReactorEventSource.Warning)),
                    e => (e.Payload[1] as string) == "UseDataSource.PageSize");
            Render(20);

            using var lateCapture = listenInitially ? null
                : ReactorTraceCollector.Capture(EventLevel.Warning, ReactorEventSource.Keywords.Errors);
            Render(20);
            Render(30);
            Render(30, "second");

            var warning = Assert.Single((initialCapture ?? lateCapture!).ByName(nameof(ReactorEventSource.Warning)),
                e => (e.Payload[1] as string) == "UseDataSource.PageSize");
            Assert.Equal(LogCategory.Reactor.ToString(), warning.Payload[0]);
            Assert.Equal(EventLevel.Warning, warning.Level);
            Assert.Contains("retains 10", Assert.IsType<string>(warning.Payload[2]));
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
