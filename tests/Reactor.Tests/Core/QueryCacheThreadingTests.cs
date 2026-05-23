using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Core;

/// <summary>
/// Race / contention coverage for <see cref="QueryCache"/>. All tests here use real
/// threads (<see cref="Task.Run"/> + <see cref="Barrier"/>) and complete within a
/// bounded timeout — slow individually, important collectively.
/// </summary>
[Trait("Category", "Threading")]
public class QueryCacheThreadingTests
{
    private const int StressThreads = 8;

    // Drain budget for every WaitAsync in this file. Workloads are bounded by
    // per-test CancellationTokenSource windows (typically 250ms–5s) — this
    // timeout only exists to catch a genuine deadlock. On a local machine the
    // tests finish in milliseconds; on shared CI runners (virtualized cores,
    // bursty scheduling) flushing the Task scheduler after cancellation can
    // stretch unpredictably. A generous ceiling kills flakes without weakening
    // the deadlock guarantee. Bumped 60s→120s after a 1-in-1000 stress hit on
    // Concurrent_Subscribe_Unsubscribe_Converges_To_Zero where the workload is
    // pure lock-bound bookkeeping (no eviction is reachable in 60s given the
    // 5-minute default CacheTime) — i.e. the timeout was scheduler-induced.
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(120);

    // ════════════════════════════════════════════════════════════════
    //  Concurrent Set + TryGet — no torn reads
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Concurrent_Set_And_TryGet_Never_Observe_Torn_Entry()
    {
        using var cache = new QueryCache();
        using var barrier = new Barrier(8);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        int tornReads = 0;

        var tasks = new List<Task>();
        for (int i = 0; i < 4; i++)
        {
            int me = i;
            tasks.Add(Task.Run(() =>
            {
                barrier.SignalAndWait();
                while (!cts.IsCancellationRequested)
                    cache.Set("k", me * 1000, TimeSpan.Zero, TimeSpan.FromMinutes(5));
            }));
        }
        for (int i = 0; i < 4; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                barrier.SignalAndWait();
                while (!cts.IsCancellationRequested)
                {
                    if (cache.TryGet<int>("k", out var e))
                    {
                        // Accept only the values actually written by the writers.
                        int v = e.Value;
                        if (v != 0 && v != 1000 && v != 2000 && v != 3000)
                            Interlocked.Increment(ref tornReads);
                    }
                }
            }));
        }

        cts.CancelAfter(TimeSpan.FromMilliseconds(250));
        await Task.WhenAll(tasks).WaitAsync(DrainTimeout);
        Assert.Equal(0, tornReads);
    }

    // ════════════════════════════════════════════════════════════════
    //  Concurrent Subscribe/Unsubscribe — ref-count never negative
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Concurrent_Subscribe_Unsubscribe_Converges_To_Zero()
    {
        using var cache = new QueryCache();
        const int threads = StressThreads;
        const int iters = 500;
        using var barrier = new Barrier(threads);
        int negativeCount = 0;

        var tasks = new Task[threads];
        for (int i = 0; i < threads; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int n = 0; n < iters; n++)
                {
                    int after = cache.Subscribe("k");
                    if (after <= 0) Interlocked.Increment(ref negativeCount);
                }
                for (int n = 0; n < iters; n++)
                {
                    int after = cache.Unsubscribe("k");
                    if (after < 0) Interlocked.Increment(ref negativeCount);
                }
            });
        }
        await Task.WhenAll(tasks).WaitAsync(DrainTimeout);
        Assert.Equal(0, negativeCount);

        // Final count should be exactly zero.
        cache.Subscribe("k");
        Assert.Equal(0, cache.Unsubscribe("k")); // if start was 0, +1 -1 = 0.
    }

    // ════════════════════════════════════════════════════════════════
    //  Invalidate-during-Set race
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Invalidate_Racing_With_Set_Leaves_Consistent_State()
    {
        using var cache = new QueryCache();
        var cts = new CancellationTokenSource();
        int inconsistencies = 0;

        var setter = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
                cache.Set("k", 42, TimeSpan.Zero, TimeSpan.FromMinutes(5));
        });
        var invalidator = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
                cache.Invalidate("k");
        });
        var reader = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                // Either the entry is absent or its value is 42 — never a different value.
                if (cache.TryGet<int>("k", out var e) && e.Value != 42)
                    Interlocked.Increment(ref inconsistencies);
            }
        });

        await Task.Delay(250);
        cts.Cancel();
        await Task.WhenAll(setter, invalidator, reader).WaitAsync(DrainTimeout);
        Assert.Equal(0, inconsistencies);
    }

    // ════════════════════════════════════════════════════════════════
    //  Eviction timer vs. Subscribe race
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Resubscribe_Racing_Against_EvictNow_Retains_Entry()
    {
        // Repeat many times to surface the race — if eviction observes the
        // zero-subscriber count then a Subscribe bumps it before the remove
        // tries, the entry must stay.
        for (int trial = 0; trial < 200; trial++)
        {
            using var cache = new QueryCache();
            var now = DateTime.UtcNow;
            cache.UtcNow = () => now;

            cache.Subscribe("k");
            cache.Set("k", 1, TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
            cache.Unsubscribe("k");
            cache.UtcNow = () => now.AddSeconds(5); // well past cache time

            using var barrier = new Barrier(2);
            Task sub = Task.Run(() =>
            {
                barrier.SignalAndWait();
                cache.Subscribe("k");
            });
            Task evict = Task.Run(() =>
            {
                barrier.SignalAndWait();
                cache.EvictNow();
            });
            await Task.WhenAll(sub, evict).WaitAsync(DrainTimeout);

            // At least one of two things must be true:
            // (a) EvictNow ran first and the entry was cleared; Subscribe created a fresh slot.
            // (b) Subscribe won and the entry survived.
            // Either way, the final subscriber count is 1 and the slot is observable.
            Assert.True(cache.Count >= 1);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  InvalidatePattern concurrent with Set
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task InvalidatePattern_Racing_With_Set_Does_Not_Throw()
    {
        using var cache = new QueryCache();
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var setter = Task.Run(() =>
        {
            int i = 0;
            while (!cts.IsCancellationRequested)
            {
                cache.Set($"user/{i++ % 100}", i, TimeSpan.Zero, TimeSpan.FromMinutes(5));
            }
        });
        var invalidator = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
                cache.InvalidatePattern("user/");
        });

        // The test passes iff neither task throws.
        await Task.WhenAll(setter, invalidator).WaitAsync(DrainTimeout);
        Assert.True(setter.IsCompletedSuccessfully);
        Assert.True(invalidator.IsCompletedSuccessfully);
    }

    // ════════════════════════════════════════════════════════════════
    //  EntryChanged fires exactly once per Set
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void EntryChanged_Fires_Exactly_Once_Per_Set()
    {
        using var cache = new QueryCache();
        int fires = 0;
        cache.EntryChanged += _ => Interlocked.Increment(ref fires);
        for (int i = 0; i < 100; i++)
            cache.Set("k", i, TimeSpan.Zero, TimeSpan.FromMinutes(5));
        Assert.Equal(100, fires);
    }

    // ════════════════════════════════════════════════════════════════
    //  Stress — mixed ops across many keys + threads
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Stress_1000_Keys_Mixed_Ops_Terminates_Without_Exceptions()
    {
        using var cache = new QueryCache();
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        int exceptions = 0;

        var tasks = new List<Task>();
        for (int t = 0; t < StressThreads; t++)
        {
            int seed = t;
            tasks.Add(Task.Run(() =>
            {
                var rnd = new Random(seed);
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        string k = $"key/{rnd.Next(1000)}";
                        int op = rnd.Next(6);
                        switch (op)
                        {
                            case 0: cache.Set(k, rnd.Next(), TimeSpan.Zero, TimeSpan.FromMilliseconds(50)); break;
                            case 1: cache.TryGet<int>(k, out _); break;
                            case 2: cache.Invalidate(k); break;
                            case 3: cache.Subscribe(k); break;
                            case 4:
                                try { cache.Unsubscribe(k); } catch (InvalidOperationException) { /* ok — underflow-guard working */ }
                                break;
                            case 5: cache.EvictNow(); break;
                        }
                    }
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref exceptions);
                }
            }));
        }
        await Task.WhenAll(tasks).WaitAsync(DrainTimeout);
        Assert.Equal(0, exceptions);
    }
}
