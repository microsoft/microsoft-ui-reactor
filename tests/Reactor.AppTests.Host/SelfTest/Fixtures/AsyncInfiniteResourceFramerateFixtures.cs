using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Framerate-level regression canaries for <c>UseInfiniteResource</c>. Each fixture
/// drives ~60 frames of scroll / range / refresh mutations and asserts the invariants
/// listed in <c>docs/specs/tasks/async-resources-implementation.md</c> §2.5.
/// </summary>
/// <remarks>
/// The selfhost harness uses <see cref="Harness.Render"/> as a per-frame tick (wait-for-
/// idle + compositor breath). The shared <c>FetchPage</c> helper mirrors the fixture
/// used by <see cref="AsyncInfiniteResourceFixtures"/>.
/// </remarks>
internal static class AsyncInfiniteResourceFramerateFixtures
{
    const int Frames = 60;
    const int TotalRows = 1000;
    const int PageSize = 25;

    static async Task<Page<string, string>> FetchPageAsync(
        string? cursor, CancellationToken ct, int delayMs,
        Action<int>? onPageFetched = null)
    {
        if (delayMs > 0) await Task.Delay(delayMs, ct);
        int start = cursor is null ? 0 : int.Parse(cursor);
        if (start >= TotalRows)
            return new Page<string, string>(Array.Empty<string>(), null, TotalRows);

        int take = Math.Min(PageSize, TotalRows - start);
        var slice = Enumerable.Range(start, take).Select(i => $"row-{i:0000}").ToList();
        int end = start + take;
        string? next = end >= TotalRows ? null : end.ToString();
        onPageFetched?.Invoke(start / PageSize);
        return new Page<string, string>(slice, next, TotalRows);
    }

    // ════════════════════════════════════════════════════════════════════
    //  ScrollFlood — 60Hz scroll across 1000 rows; ItemAt calls coalesce
    //  and total in-flight pages stays bounded.
    // ════════════════════════════════════════════════════════════════════

    internal class ScrollFlood(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cache = new QueryCache();
            int pageFetches = 0;
            int maxInFlight = 0;
            int inFlight = 0;

            InfiniteResource<string>? res = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var r = ctx.UseInfiniteResource<string, string>(
                    fetchPage: async (cursor, ct) =>
                    {
                        int live = Interlocked.Increment(ref inFlight);
                        int cur;
                        do { cur = maxInFlight; if (live <= cur) break; }
                        while (Interlocked.CompareExchange(ref maxInFlight, live, cur) != cur);

                        try
                        {
                            return await FetchPageAsync(cursor, ct, delayMs: 5,
                                onPageFetched: _ => Interlocked.Increment(ref pageFetches));
                        }
                        finally { Interlocked.Decrement(ref inFlight); }
                    },
                    cache: cache,
                    deps: Array.Empty<object>(),
                    options: new InfiniteResourceOptions(PageSize: PageSize, CacheKeyPrefix: "scrollflood"));
                res = r;
                return TextBlock($"c={r.Items.Count}");
            });

            await Harness.Render();

            // Simulate 60Hz scroll: each frame ask for a visible window that advances by
            // ~12 rows (roughly half a page) — this hits both loaded and placeholder slots.
            var rng = new Random(42);
            for (int f = 0; f < Frames; f++)
            {
                int top = Math.Min(f * 12 + rng.Next(0, 4), TotalRows - 50);
                int bottom = top + 40; // 40-row visible window
                // Ask each slot in the window (what a virtualized list does).
                for (int i = top; i <= bottom; i++) _ = res!.ItemAt(i);
                await Harness.Render();
            }

            // Let the pipeline drain.
            for (int i = 0; i < 10; i++) await Harness.Render();

            // Expected pages touched ≈ (last scroll bottom) / PageSize + 1
            int expectedMaxPages = TotalRows / PageSize;
            H.Check($"ScrollFlood_PageFetchesBounded (fetches={pageFetches}, maxPages={expectedMaxPages})",
                pageFetches > 0 && pageFetches <= expectedMaxPages);

            // Dedup invariant: across Frames*41 = ~2460 ItemAt calls, we shouldn't have
            // more than a couple of pages in-flight simultaneously (cursor-chained paging
            // tends to serialize, but this also verifies nothing unbounds the fetch pool).
            H.Check($"ScrollFlood_MaxInFlightBounded (max={maxInFlight})",
                maxInFlight <= 4);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  RapidEnsureRange — 4 EnsureRange calls per frame with jittered
    //  ranges; no duplicate page fetches, no torn Items.Count observed.
    // ════════════════════════════════════════════════════════════════════

    internal class RapidEnsureRange(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cache = new QueryCache();
            var fetchedPages = new global::System.Collections.Concurrent.ConcurrentBag<int>();
            int tornReads = 0;

            InfiniteResource<string>? res = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var r = ctx.UseInfiniteResource<string, string>(
                    fetchPage: (cursor, ct) => FetchPageAsync(cursor, ct, delayMs: 3,
                        onPageFetched: p => fetchedPages.Add(p)),
                    cache: cache,
                    deps: Array.Empty<object>(),
                    options: new InfiniteResourceOptions(PageSize: PageSize, CacheKeyPrefix: "rapidrange"));
                res = r;
                return TextBlock($"c={r.Items.Count}");
            });

            await Harness.Render();

            var rng = new Random(7);
            int prevCount = 0;
            for (int f = 0; f < Frames; f++)
            {
                // 4 jittered ranges per frame, all overlapping.
                for (int k = 0; k < 4; k++)
                {
                    int first = rng.Next(0, TotalRows - 100);
                    int last = first + 40 + rng.Next(0, 30);
                    res!.EnsureRange(first, Math.Min(last, TotalRows - 1));
                }

                // Torn read probe: Items.Count should be monotonic-non-decreasing at the
                // frame boundary (in-flight slots don't shrink the count).
                int curCount = res!.Items.Count;
                if (curCount < prevCount) Interlocked.Increment(ref tornReads);
                prevCount = curCount;

                await Harness.Render();
            }

            for (int i = 0; i < 10; i++) await Harness.Render();

            // Dedup invariant: a page index appears at most a small number of times in
            // the fetched log (cursor retries may double-fire near cancellation seams).
            var byPage = fetchedPages.GroupBy(p => p).ToDictionary(g => g.Key, g => g.Count());
            int maxPerPage = byPage.Count == 0 ? 0 : byPage.Values.Max();
            H.Check($"RapidEnsureRange_PagesCoalesced (maxPerPage={maxPerPage})",
                maxPerPage <= 2);

            H.Check($"RapidEnsureRange_NoTornCountObserved (torn={tornReads})",
                tornReads == 0);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  RefreshMidScroll — Refresh() on every 10th frame while scrolling;
    //  no NullReferenceException from torn state, items converge.
    // ════════════════════════════════════════════════════════════════════

    internal class RefreshMidScroll(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Task.Run(() =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });

            int unobserved = 0;
            EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
            { Interlocked.Increment(ref unobserved); e.SetObserved(); };
            TaskScheduler.UnobservedTaskException += handler;

            try
            {
                var cache = new QueryCache();
                int epoch = 0;
                int nullRefErrors = 0;

                InfiniteResource<string>? res = null;
                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var r = ctx.UseInfiniteResource<string, string>(
                        fetchPage: async (cursor, ct) =>
                        {
                            int e = Interlocked.Increment(ref epoch);
                            await Task.Delay(4, ct);
                            int start = cursor is null ? 0 : int.Parse(cursor);
                            if (start >= TotalRows)
                                return new Page<string, string>(Array.Empty<string>(), null, TotalRows);
                            int take = Math.Min(PageSize, TotalRows - start);
                            var slice = Enumerable.Range(start, take)
                                .Select(i => $"e{e}|row-{i:0000}").ToList();
                            string? next = start + take >= TotalRows ? null : (start + take).ToString();
                            return new Page<string, string>(slice, next, TotalRows);
                        },
                        cache: cache,
                        deps: Array.Empty<object>(),
                        options: new InfiniteResourceOptions(PageSize: PageSize, CacheKeyPrefix: "refresh"));
                    res = r;
                    return TextBlock($"c={r.Items.Count}");
                });

                await Harness.Render();

                for (int f = 0; f < Frames; f++)
                {
                    int top = (f * 8) % (TotalRows - 50);
                    // Torn-state probe: read items during the frame. These reads must not
                    // throw even when Refresh() has swapped the resource instance under us.
                    try
                    {
                        for (int i = top; i < top + 40; i++) _ = res!.ItemAt(i);
                    }
                    catch (NullReferenceException) { Interlocked.Increment(ref nullRefErrors); }

                    if (f % 10 == 9) res!.Refresh();
                    await Harness.Render();
                }

                // Let everything settle.
                for (int i = 0; i < 12; i++) await Harness.Render();

                H.Check($"RefreshMidScroll_NoNullRefs (count={nullRefErrors})",
                    nullRefErrors == 0);
                H.Check($"RefreshMidScroll_NoUnobserved (got {unobserved})", unobserved == 0);

                // Final content comes from the latest epoch only.
                string? first = res!.Items.FirstOrDefault(s => s is not null);
                H.Check($"RefreshMidScroll_FinalEpochConsistent (first={first ?? "<null>"})",
                    first is not null && first.StartsWith($"e{epoch}|"));
            }
            finally { TaskScheduler.UnobservedTaskException -= handler; }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  LruChurn — working set > MaxLoadedPages; scroll forward and back.
    //  LRU cap holds; no loaded-page resurrection as placeholder.
    // ════════════════════════════════════════════════════════════════════

    internal class LruChurn(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const int Cap = 4;
            var cache = new QueryCache();

            InfiniteResource<string>? res = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var r = ctx.UseInfiniteResource<string, string>(
                    fetchPage: (cursor, ct) => FetchPageAsync(cursor, ct, delayMs: 2),
                    cache: cache,
                    deps: Array.Empty<object>(),
                    options: new InfiniteResourceOptions(
                        PageSize: PageSize,
                        MaxLoadedPages: Cap,
                        CacheKeyPrefix: "lruchurn"));
                res = r;
                return TextBlock($"c={r.Items.Count}");
            });

            await Harness.Render();

            // Warm up the first few pages sequentially.
            for (int i = 0; i < 5; i++)
            {
                _ = res!.ItemAt(i * PageSize);
                for (int j = 0; j < 3; j++) await Harness.Render();
            }

            int maxConcurrentLoaded = 0;
            // Now scroll forward and backward every frame; each frame samples the loaded
            // pages count (pages with non-null items) and asserts the cap is respected.
            for (int f = 0; f < Frames; f++)
            {
                int anchor = (f % 2 == 0) ? (f * PageSize * 2) : (f * PageSize / 2);
                anchor = Math.Clamp(anchor, 0, TotalRows - 100);
                for (int i = anchor; i < anchor + 80; i++) _ = res!.ItemAt(i);
                await Harness.Render();

                // Count loaded pages — non-null items indicate a loaded page slot.
                int loaded = 0;
                var items = res!.Items;
                for (int p = 0; p < (items.Count + PageSize - 1) / PageSize; p++)
                {
                    int s = p * PageSize;
                    if (s < items.Count && items[s] is not null) loaded++;
                }
                if (loaded > maxConcurrentLoaded) maxConcurrentLoaded = loaded;
            }

            // Cap is soft (recently-touched pages may all be loaded simultaneously while
            // older ones get evicted). Assert we stay within 2× the cap plus 1 in-flight.
            H.Check($"LruChurn_CapHonoured (max={maxConcurrentLoaded}, cap={Cap})",
                maxConcurrentLoaded <= Cap + 1);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ParallelPages — pages complete under load from multiple sources
    //  (FetchNext + ItemAt prefetch). Each page lands in its own slot with
    //  its expected items even as completion order varies. Under cursor
    //  paging the hook serializes page fetches, so the "parallel" aspect
    //  here is the race between completion callbacks and fresh fetch
    //  scheduling on every frame, not overlapping fetchers in-flight.
    // ════════════════════════════════════════════════════════════════════

    internal class ParallelPages(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const int RequestedPages = 20;
            var cache = new QueryCache();
            // Per-page random delay so completion latency varies across the run.
            var rng = new Random(0xC0FFEE);
            var delays = Enumerable.Range(0, RequestedPages)
                .Select(_ => 1 + rng.Next(15)).ToArray();
            int fetcherCalls = 0;

            InfiniteResource<string>? res = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var r = ctx.UseInfiniteResource<string, string>(
                    fetchPage: async (cursor, ct) =>
                    {
                        Interlocked.Increment(ref fetcherCalls);
                        int start = cursor is null ? 0 : int.Parse(cursor);
                        int pageIndex = start / PageSize;
                        int d = pageIndex < delays.Length ? delays[pageIndex] : 5;
                        await Task.Delay(d, ct);
                        return await FetchPageAsync(cursor, ct, delayMs: 0);
                    },
                    cache: cache,
                    deps: Array.Empty<object>(),
                    options: new InfiniteResourceOptions(PageSize: PageSize, CacheKeyPrefix: "parallel"));
                res = r;
                return TextBlock($"c={r.Items.Count}");
            });

            await Harness.Render();

            // Drive the cursor-paged cascade via FetchNext. Speculative ItemAt-based
            // marking would claim future slots without actually requesting them (the
            // cursor isn't known at claim time), so the cascade would stall. FetchNext
            // respects the LoadState.Idle gate and advances one page per idle frame.
            // Items.Count reflects TotalCount once the first page lands, so we instead
            // poll non-null slot coverage to decide when enough pages have loaded.
            for (int f = 0; f < 400; f++)
            {
                if (res!.LoadState is LoadState.Idle && res!.HasMore) res!.FetchNext();
                await Harness.Render();
                if (LoadedNonNullCount(res!) >= RequestedPages * PageSize) break;
            }

            static int LoadedNonNullCount(InfiniteResource<string> r)
            {
                int n = 0;
                var items = r.Items;
                int limit = Math.Min(items.Count, RequestedPages * PageSize);
                for (int i = 0; i < limit; i++) if (items[i] is not null) n++;
                return n;
            }

            // Invariant 1: each loaded slot holds its expected value — no torn items.
            int mismatches = 0;
            int firstMismatch = -1;
            var items = res!.Items;
            int checkTo = Math.Min(items.Count, RequestedPages * PageSize);
            for (int i = 0; i < checkTo; i++)
            {
                var v = items[i];
                if (v is null) continue;
                if (v != $"row-{i:0000}")
                {
                    mismatches++;
                    if (firstMismatch < 0) firstMismatch = i;
                }
            }

            H.Check($"ParallelPages_NoTornItems (mismatches={mismatches}, first={firstMismatch})",
                mismatches == 0);

            // Invariant 2: fetches land in order — at least 20 pages loaded through the
            // cascade, each page exercised exactly once (no duplicate fetch storms).
            int loadedCount = items.Take(checkTo).Count(s => s is not null);
            H.Check($"ParallelPages_PagesLoaded (loaded={loadedCount}, target={RequestedPages * PageSize})",
                loadedCount >= RequestedPages * PageSize);

            H.Check($"ParallelPages_NoFetchStorm (fetcherCalls={fetcherCalls}, target={RequestedPages})",
                fetcherCalls <= RequestedPages + 2);
        }
    }
}
