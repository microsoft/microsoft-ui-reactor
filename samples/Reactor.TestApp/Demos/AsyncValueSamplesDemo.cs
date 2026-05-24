using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

// ═══════════════════════════════════════════════════════════════════════
//  AsyncValueSamples — exercises every UseResource state machine path.
//  Each scenario is a self-contained class component so the hook slot order
//  and deps lifetime are isolated. The shared QueryCache for scenarios 1d/1e
//  is parented via AppContexts.QueryCache in the hosting code.
// ═══════════════════════════════════════════════════════════════════════

enum AsyncValueScenario
{
    DeterministicFetcher,
    SyncComplete,
    DepsChangeCancel,
    SiblingsSharedKey,
    CacheHitAcrossRemount,
    InfiniteScroll,
    SearchInfinite,
    InfiniteRefresh,
    DataSourceAdapter,
}

class AsyncValueSamplesDemo : Component
{
    static readonly (AsyncValueScenario Key, string Label)[] Scenarios =
    [
        (AsyncValueScenario.DeterministicFetcher, "1a. Deterministic fetcher (succeed / fail / cancel)"),
        (AsyncValueScenario.SyncComplete, "1b. Sync-complete fetcher (no Loading flash)"),
        (AsyncValueScenario.DepsChangeCancel, "1c. Deps-change cancellation (text input drives deps)"),
        (AsyncValueScenario.SiblingsSharedKey, "1d. Two siblings, one explicit CacheKey"),
        (AsyncValueScenario.CacheHitAcrossRemount, "1e. Cache hit across remount"),
        (AsyncValueScenario.InfiniteScroll, "2a. Infinite scroll (UseInfiniteResource)"),
        (AsyncValueScenario.SearchInfinite, "2b. Search-as-you-type (deps + infinite)"),
        (AsyncValueScenario.InfiniteRefresh, "2c. Refresh() an infinite list"),
        (AsyncValueScenario.DataSourceAdapter, "2d. UseDataSource (IDataSource adapter)"),
    ];

    public override Element Render()
    {
        var (scenario, setScenario) = UseState(AsyncValueScenario.DeterministicFetcher);

        return ScrollView(VStack(12,
            Heading("UseResource scenarios"),
            TextBlock("Every arm of the AsyncValue<T> state machine exercised under a real dispatcher."),

            ComboBox(
                Scenarios.Select(s => TextBlock(s.Label) as Element).ToArray(),
                Array.IndexOf(Scenarios, Scenarios.First(s => s.Key == scenario)),
                i => setScenario(Scenarios[i].Key)
            ).Width(460),

            Border(
                scenario switch
                {
                    AsyncValueScenario.DeterministicFetcher => Component<DeterministicFetcherScenario>(),
                    AsyncValueScenario.SyncComplete => Component<SyncCompleteScenario>(),
                    AsyncValueScenario.DepsChangeCancel => Component<DepsChangeCancelScenario>(),
                    AsyncValueScenario.SiblingsSharedKey => Component<SiblingsSharedKeyScenario>(),
                    AsyncValueScenario.CacheHitAcrossRemount => Component<CacheHitRemountScenario>(),
                    AsyncValueScenario.InfiniteScroll => Component<InfiniteScrollScenario>(),
                    AsyncValueScenario.SearchInfinite => Component<SearchInfiniteScenario>(),
                    AsyncValueScenario.InfiniteRefresh => Component<InfiniteRefreshScenario>(),
                    AsyncValueScenario.DataSourceAdapter => Component<DataSourceAdapterScenario>(),
                    _ => TextBlock("Select a scenario"),
                }
            ).Padding(16).CornerRadius(8).Background(SubtleFill).Margin(horizontal: 0, vertical: 8)
        ));
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  1a — Deterministic fetcher: succeed, fail, cancel
// ═══════════════════════════════════════════════════════════════════════

class DeterministicFetcherScenario : Component
{
    enum Mode { Succeed, Fail, Slow }

    public override Element Render()
    {
        var (mode, setMode) = UseState(Mode.Succeed);
        var (runId, setRunId) = UseState(0);

        var result = UseResource(async ct =>
        {
            await Task.Delay(500, ct);
            return mode switch
            {
                Mode.Succeed => $"ok (run {runId})",
                Mode.Slow => $"slow ok (run {runId})",
                Mode.Fail => throw new InvalidOperationException($"boom (run {runId})"),
                _ => "",
            };
        }, new object[] { mode, runId });

        return VStack(8,
            SubHeading("1a. Deterministic fetcher"),
            HStack(8,
                Button("Succeed", () => { setMode(Mode.Succeed); setRunId(runId + 1); }),
                Button("Fail", () => { setMode(Mode.Fail); setRunId(runId + 1); }),
                Button("Slow", () => { setMode(Mode.Slow); setRunId(runId + 1); })
            ),
            TextBlock(DescribeValue(result)).SemiBold()
        );
    }

    static string DescribeValue(AsyncValue<string> v) => v switch
    {
        AsyncValue<string>.Loading => "⏳ Loading…",
        AsyncValue<string>.Data d => $"✅ Data: {d.Value}",
        AsyncValue<string>.Reloading r => $"♻️ Reloading (prev: {r.Previous})",
        AsyncValue<string>.Error e => $"❌ Error: {e.Exception.Message}",
        _ => "?",
    };
}

// ═══════════════════════════════════════════════════════════════════════
//  1b — Sync-complete fetcher: Task.FromResult → no Loading flash
// ═══════════════════════════════════════════════════════════════════════

class SyncCompleteScenario : Component
{
    public override Element Render()
    {
        var result = UseResource(
            _ => Task.FromResult("from Task.FromResult (resolved before hook returns)"),
            Array.Empty<object>());

        return VStack(8,
            SubHeading("1b. Sync-complete fetcher"),
            TextBlock("A fetcher returning an already-completed task skips the Loading state entirely."),
            TextBlock(result switch
            {
                AsyncValue<string>.Loading => "⏳ Loading… (should not appear!)",
                AsyncValue<string>.Data d => $"✅ {d.Value}",
                AsyncValue<string>.Error e => $"❌ {e.Exception.Message}",
                AsyncValue<string>.Reloading r => $"♻️ {r.Previous}",
                _ => "?",
            }).SemiBold()
        );
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  1c — Deps-change cancellation: text input drives deps
// ═══════════════════════════════════════════════════════════════════════

class DepsChangeCancelScenario : Component
{
    public override Element Render()
    {
        var (query, setQuery) = UseState("");

        var result = UseResource(async ct =>
        {
            await Task.Delay(400, ct);
            // If we reach here without cancellation, return a digest of the query.
            return string.IsNullOrEmpty(query) ? "<empty>" : $"processed: {query.ToUpperInvariant()}";
        }, new object[] { query });

        return VStack(8,
            SubHeading("1c. Deps-change cancellation"),
            TextBlock("Each keystroke cancels the previous fetch and starts a new one — only the last lands."),
            TextBox(query, v => setQuery(v ?? ""), placeholderText: "type here…"),
            TextBlock(result switch
            {
                AsyncValue<string>.Loading => "⏳ Loading…",
                AsyncValue<string>.Data d => $"✅ {d.Value}",
                AsyncValue<string>.Reloading r => $"♻️ Reloading (prev: {r.Previous})",
                AsyncValue<string>.Error e => $"❌ {e.Exception.Message}",
                _ => "?",
            }).SemiBold()
        );
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  1d — Two siblings, one explicit CacheKey → share a single fetch
// ═══════════════════════════════════════════════════════════════════════

class SiblingsSharedKeyScenario : Component
{
    static int _sharedCallCount;

    public override Element Render()
    {
        return VStack(8,
            SubHeading("1d. Siblings share cache via explicit CacheKey"),
            TextBlock($"Total fetcher invocations across both siblings: {_sharedCallCount}"),
            Component<SharedKeySibling>(),
            Component<SharedKeySibling>()
        );
    }

    class SharedKeySibling : Component
    {
        public override Element Render()
        {
            var result = UseResource(async _ =>
            {
                Interlocked.Increment(ref _sharedCallCount);
                await Task.Delay(250);
                return $"shared payload (invocation {_sharedCallCount})";
            },
            Array.Empty<object>(),
            new ResourceOptions(CacheKey: "demo/shared", StaleTime: TimeSpan.FromMinutes(5)));

            return Border(
                TextBlock(result switch
                {
                    AsyncValue<string>.Loading => "⏳ Loading…",
                    AsyncValue<string>.Data d => $"✅ {d.Value}",
                    AsyncValue<string>.Reloading r => $"♻️ {r.Previous}",
                    AsyncValue<string>.Error e => $"❌ {e.Exception.Message}",
                    _ => "?",
                })
            ).Padding(8).CornerRadius(4).Background(SubtleFill);
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  1e — Cache hit across remount: toggle visibility within StaleTime
// ═══════════════════════════════════════════════════════════════════════

class CacheHitRemountScenario : Component
{
    public override Element Render()
    {
        var (visible, setVisible) = UseState(true);

        return VStack(8,
            SubHeading("1e. Cache hit across remount"),
            TextBlock("Hide the fetcher component, then show it again within StaleTime — the cached value returns synchronously with no Loading flash."),
            Button(visible ? "Hide" : "Show", () => setVisible(!visible)),
            visible
                ? (Element)Component<RemountChild>()
                : TextBlock("(hidden)").Foreground(TertiaryText)
        );
    }

    class RemountChild : Component
    {
        public override Element Render()
        {
            var result = UseResource(async _ =>
            {
                await Task.Delay(600);
                return $"fetched at {DateTime.Now:HH:mm:ss.fff}";
            },
            Array.Empty<object>(),
            new ResourceOptions(
                CacheKey: "demo/remount-key",
                StaleTime: TimeSpan.FromMinutes(1),
                CacheTime: TimeSpan.FromMinutes(5)));

            return Border(
                TextBlock(result switch
                {
                    AsyncValue<string>.Loading => "⏳ First load — takes 600ms.",
                    AsyncValue<string>.Data d => $"✅ {d.Value} (cached)",
                    AsyncValue<string>.Reloading r => $"♻️ {r.Previous}",
                    AsyncValue<string>.Error e => $"❌ {e.Exception.Message}",
                    _ => "?",
                })
            ).Padding(8).CornerRadius(4).Background(SubtleFill);
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  Shared infrastructure — fake paged data source
// ═══════════════════════════════════════════════════════════════════════

static class FakeCityDirectory
{
    static readonly string[] Cities =
    [
        "Seattle","San Francisco","New York","Austin","Boston","London","Dublin",
        "Tokyo","Singapore","Bangalore","Toronto","Berlin","Sydney","Denver",
        "Chicago","Paris","Amsterdam","Zurich","Copenhagen","Stockholm","Oslo",
        "Helsinki","Vienna","Prague","Madrid","Lisbon","Barcelona","Milan","Rome",
        "Athens","Cairo","Nairobi","Cape Town","Lagos","Mumbai","Delhi","Shanghai",
        "Beijing","Hong Kong","Seoul","Osaka","Manila","Bangkok","Jakarta",
        "Hanoi","Dubai","Tel Aviv","Istanbul","Moscow","St. Petersburg",
    ];

    public static string Label(int index) => $"{index:000000}  {Cities[index % Cities.Length]}";

    public const int Total = 10_000;

    public static async Task<Page<string, string>> FetchPageAsync(string? cursor, string query, int pageSize, CancellationToken ct)
    {
        await Task.Delay(200, ct);

        int start = cursor is null ? 0 : int.Parse(cursor);

        IEnumerable<int> candidates = Enumerable.Range(0, Total);
        if (!string.IsNullOrWhiteSpace(query))
            candidates = candidates.Where(i => Label(i).Contains(query, StringComparison.OrdinalIgnoreCase));

        var all = candidates.ToList();
        if (start >= all.Count) return new Page<string, string>(Array.Empty<string>(), null, all.Count);

        var page = all.Skip(start).Take(pageSize).Select(Label).ToList();
        int end = start + page.Count;
        string? next = end >= all.Count ? null : end.ToString();
        return new Page<string, string>(page, next, all.Count);
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  2a — Infinite scroll via UseInfiniteResource + VirtualList
// ═══════════════════════════════════════════════════════════════════════

class InfiniteScrollScenario : Component
{
    public override Element Render()
    {
        var resource = UseInfiniteResource<string, string>(
            fetchPage: (cursor, ct) => FakeCityDirectory.FetchPageAsync(cursor, query: "", pageSize: 50, ct),
            deps: Array.Empty<object>(),
            options: new InfiniteResourceOptions(PageSize: 50, CacheKeyPrefix: "demo/infinite-scroll"));

        int total = resource.TotalCount ?? resource.Items.Count;
        bool hasMore = resource.HasMore;
        int listLen = total + (hasMore && resource.TotalCount is null ? 1 : 0);

        return VStack(8,
            SubHeading("2a. Infinite scroll (UseInfiniteResource)"),
            TextBlock($"Server has {resource.TotalCount?.ToString() ?? "?"} rows. LoadState = {LoadLabel(resource.LoadState)}. Scroll to page in more."),

            Border(
                VirtualList(
                    itemCount: listLen,
                    renderItem: i =>
                    {
                        var value = resource.ItemAt(i);
                        var label = value ?? "⏳ loading…";
                        return HStack(8,
                            TextBlock($"{i,5}").Width(60).Foreground(TertiaryText),
                            TextBlock(label).Flex(grow: 1)
                        );
                    },
                    itemHeight: 28,
                    spacing: 1,
                    getItemKey: i => i.ToString()
                ).Flex(grow: 1)
            ).Height(360).CornerRadius(4).Background(SubtleFill)
        );
    }

    static string LoadLabel(LoadState s) => s switch
    {
        LoadState.Loading => "⏳ Loading",
        LoadState.Idle => "✅ Idle",
        LoadState.EndOfList => "🏁 EndOfList",
        LoadState.Error e => $"❌ {e.Exception.Message}",
        _ => "?",
    };
}

// ═══════════════════════════════════════════════════════════════════════
//  2b — Search-as-you-type over an infinite list
// ═══════════════════════════════════════════════════════════════════════

class SearchInfiniteScenario : Component
{
    public override Element Render()
    {
        var (query, setQuery) = UseState("");

        var resource = UseInfiniteResource<string, string>(
            fetchPage: (cursor, ct) => FakeCityDirectory.FetchPageAsync(cursor, query, pageSize: 25, ct),
            deps: new object[] { query },
            options: new InfiniteResourceOptions(PageSize: 25, CacheKeyPrefix: "demo/search-infinite"));

        int total = resource.TotalCount ?? resource.Items.Count;
        bool hasMore = resource.HasMore;
        int listLen = total + (hasMore && resource.TotalCount is null ? 1 : 0);

        return VStack(8,
            SubHeading("2b. Search-as-you-type"),
            TextBlock("Type to filter. Each keystroke cancels the pending fetch; only the matching deps' pages land."),
            TextBox(query, v => setQuery(v ?? ""), placeholderText: "e.g. Seattle"),
            TextBlock($"Matches: {resource.TotalCount?.ToString() ?? "…"}  /  LoadState: {SearchInfiniteLabel(resource.LoadState)}"),

            Border(
                VirtualList(
                    itemCount: listLen,
                    renderItem: i =>
                    {
                        var value = resource.ItemAt(i);
                        return HStack(8,
                            TextBlock($"{i,4}").Width(50).Foreground(TertiaryText),
                            TextBlock(value ?? "⏳ loading…").Flex(grow: 1)
                        );
                    },
                    itemHeight: 28,
                    spacing: 1,
                    getItemKey: i => i.ToString()
                ).Flex(grow: 1)
            ).Height(300).CornerRadius(4).Background(SubtleFill)
        );
    }

    static string SearchInfiniteLabel(LoadState s) => s switch
    {
        LoadState.Loading => "⏳",
        LoadState.Idle => "✅",
        LoadState.EndOfList => "🏁",
        LoadState.Error e => $"❌ {e.Exception.Message}",
        _ => "?",
    };
}

// ═══════════════════════════════════════════════════════════════════════
//  2c — Refresh() an infinite list
// ═══════════════════════════════════════════════════════════════════════

class InfiniteRefreshScenario : Component
{
    public override Element Render()
    {
        var epoch = UseRef(0);

        var resource = UseInfiniteResource<string, string>(
            fetchPage: async (cursor, ct) =>
            {
                await Task.Delay(200, ct);
                int start = cursor is null ? 0 : int.Parse(cursor);
                int stamp = epoch.Current;
                var items = Enumerable.Range(start, 25)
                    .Select(i => $"#{i:000} — epoch {stamp}")
                    .ToList();
                string? next = (start + 25 >= 500) ? null : (start + 25).ToString();
                return new Page<string, string>(items, next, 500);
            },
            deps: Array.Empty<object>(),
            options: new InfiniteResourceOptions(PageSize: 25, CacheKeyPrefix: "demo/infinite-refresh"));

        return VStack(8,
            SubHeading("2c. Refresh() an infinite list"),
            TextBlock("Refresh cancels in-flight fetches, clears the page table, and restarts from page 0."),

            HStack(8,
                Button("Refresh", () =>
                {
                    epoch.Current = epoch.Current + 1;
                    resource.Refresh();
                }),
                TextBlock($"LoadState: {RefreshLabel(resource.LoadState)}    epoch: {epoch.Current}")
            ),

            Border(
                VirtualList(
                    itemCount: Math.Max(resource.Items.Count, 1),
                    renderItem: i => TextBlock(resource.ItemAt(i) ?? "⏳ loading…"),
                    itemHeight: 28,
                    spacing: 1,
                    getItemKey: i => i.ToString()
                ).Flex(grow: 1)
            ).Height(260).CornerRadius(4).Background(SubtleFill)
        );
    }

    static string RefreshLabel(LoadState s) => s switch
    {
        LoadState.Loading => "⏳ Loading",
        LoadState.Idle => "✅ Idle",
        LoadState.EndOfList => "🏁 EndOfList",
        LoadState.Error e => $"❌ {e.Exception.Message}",
        _ => "?",
    };
}

// ═══════════════════════════════════════════════════════════════════════
//  2d — UseDataSource adapter over IDataSource
// ═══════════════════════════════════════════════════════════════════════

sealed class InMemoryNameSource : Microsoft.UI.Reactor.Data.IDataSource<string>
{
    static readonly string[] All = Enumerable.Range(0, 2_000)
        .Select(i => $"{i:0000} · Row {i}")
        .ToArray();

    public Microsoft.UI.Reactor.Data.DataSourceCapabilities Capabilities =>
        Microsoft.UI.Reactor.Data.DataSourceCapabilities.ServerCount |
        Microsoft.UI.Reactor.Data.DataSourceCapabilities.ServerSearch;

    public Microsoft.UI.Reactor.Data.RowKey GetRowKey(string item) => item;

    public async Task<Microsoft.UI.Reactor.Data.DataPage<string>> GetPageAsync(
        Microsoft.UI.Reactor.Data.DataRequest request, CancellationToken ct = default)
    {
        await Task.Delay(200, ct);
        var filtered = All.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(request.SearchQuery))
            filtered = filtered.Where(s => s.Contains(request.SearchQuery, StringComparison.OrdinalIgnoreCase));
        var list = filtered.ToList();

        int start = request.ContinuationToken is null ? 0 : int.Parse(request.ContinuationToken);
        if (start >= list.Count)
            return new Microsoft.UI.Reactor.Data.DataPage<string>(Array.Empty<string>(), null, list.Count);
        var page = list.Skip(start).Take(request.PageSize).ToList();
        int end = start + page.Count;
        string? next = end >= list.Count ? null : end.ToString();
        return new Microsoft.UI.Reactor.Data.DataPage<string>(page, next, list.Count);
    }
}

class DataSourceAdapterScenario : Component
{
    static readonly InMemoryNameSource Source = new();

    public override Element Render()
    {
        var (search, setSearch) = UseState("");

        // Adapt IDataSource → UseInfiniteResource directly. This is what
        // DataSourceResourceExtensions.UseDataSource does internally — inlined here so the
        // demo doesn't reach through the Component's internal RenderContext.
        var resource = UseInfiniteResource<string, string>(
            fetchPage: async (cursor, ct) =>
            {
                var page = await Source.GetPageAsync(
                    new Microsoft.UI.Reactor.Data.DataRequest
                    {
                        PageSize = 50,
                        SearchQuery = search,
                        ContinuationToken = cursor,
                    }, ct);
                return new Page<string, string>(page.Items, page.ContinuationToken, page.TotalCount);
            },
            deps: new object[] { Source, search },
            options: new InfiniteResourceOptions(PageSize: 50, CacheKeyPrefix: "demo/datasource-adapter"));

        int total = resource.TotalCount ?? resource.Items.Count;
        bool hasMore = resource.HasMore;
        int listLen = total + (hasMore && resource.TotalCount is null ? 1 : 0);

        return VStack(8,
            SubHeading("2d. UseDataSource (IDataSource adapter)"),
            TextBlock("UseDataSource projects any IDataSource<T> into an InfiniteResource<T> — existing data-ecosystem interfaces work with the hook surface."),
            TextBox(search, v => setSearch(v ?? ""), placeholderText: "search rows…"),
            TextBlock($"Matches: {resource.TotalCount?.ToString() ?? "…"}"),

            Border(
                VirtualList(
                    itemCount: listLen,
                    renderItem: i => TextBlock(resource.ItemAt(i) ?? "⏳ loading…"),
                    itemHeight: 28,
                    spacing: 1,
                    getItemKey: i => i.ToString()
                ).Flex(grow: 1)
            ).Height(300).CornerRadius(4).Background(SubtleFill)
        );
    }
}
