using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selfhost coverage for <c>UseInfiniteResource</c> under a real dispatcher. These
/// fixtures drive the pull-model (<c>ItemAt</c>) and imperative (<c>EnsureRange</c>,
/// <c>Refresh</c>) entry points, and verify that cancellation, deps-change, and
/// refresh behave correctly when marshalling completes on the real
/// <see cref="Microsoft.UI.Dispatching.DispatcherQueue"/>.
/// </summary>
internal static class AsyncInfiniteResourceFixtures
{
    // ────────── Shared fake paged source ──────────

    static async Task<Page<string, string>> FetchPageAsync(
        string? cursor, string query, int pageSize, CancellationToken ct,
        Action<int>? onPageFetched = null, int delayMs = 10)
    {
        if (delayMs > 0) await Task.Delay(delayMs, ct);
        int start = cursor is null ? 0 : int.Parse(cursor);

        // Total pool: 200 items scoped by query. For non-empty query we filter.
        const int Total = 200;
        IEnumerable<int> all = Enumerable.Range(0, Total);
        if (!string.IsNullOrEmpty(query))
            all = all.Where(i => i.ToString().Contains(query));

        var filtered = all.ToList();
        if (start >= filtered.Count)
            return new Page<string, string>(Array.Empty<string>(), null, filtered.Count);

        var slice = filtered.Skip(start).Take(pageSize)
            .Select(i => string.IsNullOrEmpty(query)
                ? $"item-{i:000}"
                : $"q={query}|item-{i:000}")
            .ToList();
        int end = start + slice.Count;
        string? next = end >= filtered.Count ? null : end.ToString();
        onPageFetched?.Invoke(start / pageSize);
        return new Page<string, string>(slice, next, filtered.Count);
    }

    // ════════════════════════════════════════════════════════════════════
    //  InfiniteBasic — scroll through pages in order
    // ════════════════════════════════════════════════════════════════════

    internal class InfiniteBasic(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cache = new QueryCache();
            InfiniteResource<string>? res = null;
            int pageFetches = 0;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var r = ctx.UseInfiniteResource<string, string>(
                    fetchPage: (cursor, ct) => FetchPageAsync(cursor, "", 25, ct,
                        onPageFetched: _ => Interlocked.Increment(ref pageFetches)),
                    cache: cache,
                    deps: Array.Empty<object>(),
                    options: new InfiniteResourceOptions(PageSize: 25, CacheKeyPrefix: "basic"));
                res = r;
                return TextBlock($"count: {r.Items.Count} total: {r.TotalCount?.ToString() ?? "?"}");
            });

            // Let the first page resolve. The fetcher's 10ms Task.Delay can resolve
            // during a Render()'s 16ms wall-clock breathing-room, queueing the Apply
            // continuation (which sets TotalCount) on the dispatcher just after
            // WaitForIdleAsync returned. Pump a third time to drain it.
            await Harness.Render();
            await Harness.Render();
            await Harness.Render();

            H.Check($"InfiniteBasic_Page1Loaded (fetches={pageFetches})",
                pageFetches >= 1 && res!.Items.Count >= 25);
            H.Check($"InfiniteBasic_TotalKnown ({res!.TotalCount})",
                res!.TotalCount == 200);

            // Pull next 4 pages by walking ItemAt (simulating virtualized scroll).
            for (int i = 0; i < 5; i++)
            {
                _ = res!.ItemAt(25 * i + 12);
                await Harness.Render();
                await Harness.Render();
            }

            H.Check($"InfiniteBasic_MultiplePagesFetched (got {pageFetches})",
                pageFetches >= 5);

            // Items list should contain at least the first 5 pages worth.
            H.Check($"InfiniteBasic_ItemsContiguous ({res!.Items.Count} items)",
                res!.Items.Count >= 125);
            H.Check("InfiniteBasic_FirstItemSet", res!.Items[0] == "item-000");
            H.Check("InfiniteBasic_Row100Set", res!.Items[100] == "item-100");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  InfinitePlaceholder — ItemAt on an unloaded page returns null and
    //  triggers a fetch; once it resolves the value is visible.
    // ════════════════════════════════════════════════════════════════════

    internal class InfinitePlaceholder(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cache = new QueryCache();
            InfiniteResource<string>? res = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var r = ctx.UseInfiniteResource<string, string>(
                    fetchPage: (cursor, ct) => FetchPageAsync(cursor, "", 25, ct, delayMs: 30),
                    cache: cache,
                    deps: Array.Empty<object>(),
                    options: new InfiniteResourceOptions(PageSize: 25, CacheKeyPrefix: "placeholder"));
                res = r;
                return TextBlock($"count: {r.Items.Count}");
            });

            // Let page 1 load.
            await Harness.Render();
            await Harness.Render();

            // Peek a slot on the next page (index 30 → page 1) — the slot should return
            // null and the hook should begin fetching page 1 sequentially after page 0.
            // (Cursor-based paging doesn't support arbitrary jump-ahead: each page depends
            //  on the prior page's cursor, so pages must resolve in order.)
            var ret = res!.ItemAt(30);
            H.Check("InfinitePlaceholder_PlaceholderReturned", ret is null);

            // Repeat calls should dedup — the page is in-flight or queued.
            for (int i = 0; i < 10; i++) _ = res!.ItemAt(30);

            // Now let the fetch resolve. Give the dispatcher a few frames plus an
            // explicit render with extra wall-clock slack to cover the 30ms fetcher.
            for (int i = 0; i < 4; i++) await Harness.Render();
            await Harness.Render(200);

            H.Check($"InfinitePlaceholder_Resolved ({res!.ItemAt(30) ?? "<null>"})",
                res!.ItemAt(30) == "item-030");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  InfiniteRefresh — Refresh cancels in-flight work, clears the page
    //  table, and restarts from page 0 with fresh data.
    // ════════════════════════════════════════════════════════════════════

    internal class InfiniteRefresh(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cache = new QueryCache();
            int fetchEpoch = 0;

            InfiniteResource<string>? res = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var epochRef = ctx.UseRef(0);
                var r = ctx.UseInfiniteResource<string, string>(
                    fetchPage: async (cursor, ct) =>
                    {
                        int epoch = Interlocked.Increment(ref fetchEpoch);
                        await Task.Delay(10, ct);
                        int start = cursor is null ? 0 : int.Parse(cursor);
                        var items = Enumerable.Range(start, 20)
                            .Select(i => $"epoch{epoch}:{i:000}")
                            .ToList();
                        string? next = (start + 20 >= 100) ? null : (start + 20).ToString();
                        return new Page<string, string>(items, next, 100);
                    },
                    cache: cache,
                    deps: Array.Empty<object>(),
                    options: new InfiniteResourceOptions(PageSize: 20, CacheKeyPrefix: "refresh"));
                res = r;
                return TextBlock($"items: {r.Items.Count}");
            });

            await Harness.Render();
            await Harness.Render();
            await Harness.Render();

            H.Check($"InfiniteRefresh_InitialLoaded (items={res!.Items.Count})",
                res!.Items.Count >= 20);
            int firstEpoch = fetchEpoch;
            string? firstItem = res!.Items[0];
            H.Check("InfiniteRefresh_FirstEpochMarker",
                firstItem is not null && firstItem.StartsWith($"epoch{firstEpoch}"));

            // Load page 2 too.
            _ = res!.ItemAt(25);
            await Harness.Render();
            await Harness.Render();

            // Now Refresh — should cancel/clear and start over.
            res!.Refresh();
            // Refresh races with re-render; let the first new page land.
            for (int i = 0; i < 6; i++) await Harness.Render();

            H.Check($"InfiniteRefresh_NewFetches ({fetchEpoch})",
                fetchEpoch > firstEpoch);
            // After refresh, the first item should carry a new epoch marker.
            var newFirst = res!.Items.Count > 0 ? res!.Items[0] : null;
            H.Check($"InfiniteRefresh_FreshFirstPage ({newFirst})",
                newFirst is not null &&
                !newFirst.StartsWith($"epoch{firstEpoch}:") &&
                newFirst.Contains(":000"));
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  InfiniteDepsChange — deps change mid-paging; stale pages cancel.
    // ════════════════════════════════════════════════════════════════════

    internal class InfiniteDepsChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cache = new QueryCache();
            int fetchesStarted = 0;
            int fetchesCancelled = 0;

            InfiniteResource<string>? res = null;
            Action<string>? setQuery = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (query, set) = ctx.UseState("");
                setQuery = set;

                var r = ctx.UseInfiniteResource<string, string>(
                    fetchPage: async (cursor, ct) =>
                    {
                        Interlocked.Increment(ref fetchesStarted);
                        ct.Register(() => Interlocked.Increment(ref fetchesCancelled));
                        // Long-ish fetch so we can interrupt it reliably.
                        await Task.Delay(200, ct);
                        return await FetchPageAsync(cursor, query, 25, ct, delayMs: 0);
                    },
                    cache: cache,
                    deps: new object[] { query },
                    options: new InfiniteResourceOptions(PageSize: 25, CacheKeyPrefix: "depschange"));
                res = r;
                return TextBlock($"q={query}|count={r.Items.Count}");
            });

            await Harness.Render();

            // Drive query changes quickly so each deps value's fetch gets cancelled.
            foreach (var q in new[] { "1", "2", "3", "4" })
            {
                setQuery!(q);
                await Harness.Render();
            }

            // Let the final deps value's fetch resolve.
            for (int i = 0; i < 10; i++) await Harness.Render();

            H.Check($"InfiniteDepsChange_MultipleFetches (started={fetchesStarted})",
                fetchesStarted >= 4);
            H.Check($"InfiniteDepsChange_StaleCancelled (cancelled={fetchesCancelled})",
                fetchesCancelled >= 3);
            // Last query was "4" — ensure we rendered the filtered results for it and
            // no leftover values from prior queries made it into the items list.
            H.Check($"InfiniteDepsChange_FinalQueryResults (count={res!.Items.Count})",
                res!.Items.Count > 0 &&
                res!.Items.All(s => s is null || s.StartsWith("q=4|")));
        }
    }
}
