using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Core;

/// <summary>
/// Unit tests driving <c>UseResource</c> through <see cref="RenderContext"/> directly —
/// no WinUI controls, no real dispatcher. The fetcher is a controllable
/// <see cref="TaskCompletionSource{T}"/> so completion ordering is deterministic.
/// </summary>
public class UseResourceTests
{
    // ════════════════════════════════════════════════════════════════
    //  Test harness
    // ════════════════════════════════════════════════════════════════

    /// <summary>Synchronous dispatcher: runs posted actions inline.</summary>
    private sealed class InlineDispatcher : IHookDispatcher
    {
        public int PostCount;
        public void Post(Action action) { PostCount++; action(); }
    }

    /// <summary>Deferred dispatcher: queues actions for manual draining.</summary>
    private sealed class QueuedDispatcher : IHookDispatcher
    {
        private readonly Queue<Action> _pending = new();
        public void Post(Action action) { lock (_pending) _pending.Enqueue(action); }
        public int Drain()
        {
            int n = 0;
            while (true)
            {
                Action? a;
                lock (_pending)
                {
                    if (_pending.Count == 0) return n;
                    a = _pending.Dequeue();
                }
                a();
                n++;
            }
        }
    }

    private static RenderContext NewCtx(out int rerenders)
    {
        var ctx = new RenderContext();
        int count = 0;
        ctx.BeginRender(() => count++);
        rerenders = count;
        return ctx;
    }

    private static QueryCache NewCache()
    {
        var cache = new QueryCache();
        var t = DateTime.UtcNow;
        cache.UtcNow = () => t;
        return cache;
    }

    // Render once inside a helper that exposes the hook's state through an adapter.
    private static (AsyncValue<T> Value, RenderContext Ctx, QueryCache Cache, IHookDispatcher Dispatcher) Render<T>(
        Func<CancellationToken, Task<T>> fetcher,
        object[] deps,
        ResourceOptions? options = null,
        QueryCache? cache = null,
        IHookDispatcher? dispatcher = null)
    {
        var c = cache ?? NewCache();
        var d = dispatcher ?? new InlineDispatcher();
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var v = ctx.UseResource(fetcher, c, deps, options, d);
        ctx.FlushEffects();
        return (v, ctx, c, d);
    }

    // ════════════════════════════════════════════════════════════════
    //  Loading / Data / Error
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Pending_Task_Returns_Loading()
    {
        var tcs = new TaskCompletionSource<int>();
        var r = Render(_ => tcs.Task, Array.Empty<object>());
        Assert.IsType<AsyncValue<int>.Loading>(r.Value);
    }

    [Fact]
    public void Sync_Completed_Task_Returns_Data_Same_Render_No_Flash()
    {
        var r = Render(_ => Task.FromResult(42), Array.Empty<object>());
        Assert.Equal(new AsyncValue<int>.Data(42), r.Value);
    }

    [Fact]
    public void Sync_Faulted_Task_Returns_Error_Same_Render()
    {
        var r = Render(_ => Task.FromException<int>(new InvalidOperationException("x")), Array.Empty<object>());
        var err = Assert.IsType<AsyncValue<int>.Error>(r.Value);
        Assert.IsType<InvalidOperationException>(err.Exception);
    }

    [Fact]
    public void Fetcher_Throws_Synchronously_Returns_Error()
    {
        var r = Render<int>(_ => throw new InvalidOperationException("boom"), Array.Empty<object>());
        var err = Assert.IsType<AsyncValue<int>.Error>(r.Value);
        Assert.Equal("boom", err.Exception.Message);
    }

    [Fact]
    public async Task Async_Completion_Transitions_Loading_To_Data_Across_Renders()
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ctx = new RenderContext();
        int rerenders = 0;
        ctx.BeginRender(() => rerenders++);
        var cache = NewCache();
        var dispatcher = new InlineDispatcher();

        var v1 = ctx.UseResource(_ => tcs.Task, cache, Array.Empty<object>(), null, dispatcher);
        Assert.IsType<AsyncValue<int>.Loading>(v1);

        tcs.SetResult(7);
        // Continuation is scheduled async (TCS.RunContinuationsAsynchronously) — poll briefly.
        for (int i = 0; i < 20 && rerenders == 0; i++) await Task.Delay(10);
        Assert.True(rerenders >= 1, $"Expected a rerender; got {rerenders}.");

        // Next render picks up the new value.
        ctx.BeginRender(() => rerenders++);
        var v2 = ctx.UseResource(_ => tcs.Task, cache, Array.Empty<object>(), null, dispatcher);
        Assert.Equal(new AsyncValue<int>.Data(7), v2);
    }

    // ════════════════════════════════════════════════════════════════
    //  Cache hit fresh / stale
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Cache_Hit_Fresh_Skips_Fetch()
    {
        var cache = NewCache();
        var options = new ResourceOptions(StaleTime: TimeSpan.FromMinutes(10), CacheKey: "shared-k");

        // First render populates the cache (sync-complete).
        var r1 = Render(_ => Task.FromResult(42), Array.Empty<object>(), options, cache);
        Assert.Equal(new AsyncValue<int>.Data(42), r1.Value);

        int fetcherCalls = 0;
        Func<CancellationToken, Task<int>> fetcher = _ => { fetcherCalls++; return Task.FromResult(999); };

        // Second render in a *new* context using the same explicit cache key → sibling.
        var r2 = Render(fetcher, Array.Empty<object>(), options, cache);

        Assert.Equal(new AsyncValue<int>.Data(42), r2.Value);
        Assert.Equal(0, fetcherCalls);
    }

    [Fact]
    public void Cache_Hit_Stale_Returns_Reloading_And_Refetches()
    {
        var cache = NewCache();
        var now = cache.UtcNow();
        var options = new ResourceOptions(StaleTime: TimeSpan.FromSeconds(1), CacheKey: "k");

        // Seed cache.
        cache.Set("k", 42, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5));
        cache.UtcNow = () => now + TimeSpan.FromSeconds(30); // past stale

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var r = Render(_ => tcs.Task, new object[] { "k" }, options, cache);

        var reloading = Assert.IsType<AsyncValue<int>.Reloading>(r.Value);
        Assert.Equal(42, reloading.Previous);
    }

    [Fact]
    public void RefetchOnMount_False_On_Cache_Miss_Returns_Loading_And_Does_Not_Fetch()
    {
        int calls = 0;
        Func<CancellationToken, Task<int>> fetcher = _ => { calls++; return Task.FromResult(42); };

        var r = Render(fetcher, Array.Empty<object>(),
            new ResourceOptions(RefetchOnMount: false, CacheKey: "never"));
        Assert.IsType<AsyncValue<int>.Loading>(r.Value);
        Assert.Equal(0, calls);
    }

    // ════════════════════════════════════════════════════════════════
    //  Deps change — cancellation
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Deps_Change_Cancels_Previous_Fetch()
    {
        var cache = NewCache();
        var dispatcher = new InlineDispatcher();
        CancellationToken captured1 = default;
        CancellationToken captured2 = default;
        var gate = new TaskCompletionSource<int>();

        Func<CancellationToken, Task<int>> fetcher1 = ct => { captured1 = ct; return gate.Task; };
        Func<CancellationToken, Task<int>> fetcher2 = ct => { captured2 = ct; return Task.FromResult(2); };

        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var v1 = ctx.UseResource(fetcher1, cache, new object[] { "a" }, null, dispatcher);
        Assert.IsType<AsyncValue<int>.Loading>(v1);

        // Deps change.
        ctx.BeginRender(() => { });
        var v2 = ctx.UseResource(fetcher2, cache, new object[] { "b" }, null, dispatcher);

        Assert.True(captured1.IsCancellationRequested, "First fetch's token should be cancelled on deps change.");
        Assert.False(captured2.IsCancellationRequested);
        // Second fetcher is sync-complete → Data on same render.
        Assert.Equal(new AsyncValue<int>.Data(2), v2);

        // Late completion of the first task must not affect state.
        gate.SetResult(999);
        await Task.Yield();

        ctx.BeginRender(() => { });
        var v3 = ctx.UseResource(fetcher2, cache, new object[] { "b" }, null, dispatcher);
        Assert.Equal(new AsyncValue<int>.Data(2), v3); // not 999
    }

    // ════════════════════════════════════════════════════════════════
    //  Unmount cancellation
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Unmount_Cancels_InFlight_And_Drops_Late_Result()
    {
        var cache = NewCache();
        var dispatcher = new InlineDispatcher();
        CancellationToken captured = default;
        var gate = new TaskCompletionSource<int>();

        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var v = ctx.UseResource(ct => { captured = ct; return gate.Task; }, cache, new object[] { "a" }, null, dispatcher);
        ctx.FlushEffects();

        Assert.IsType<AsyncValue<int>.Loading>(v);

        // Unmount — RunCleanups triggers the UseEffect cleanup that disposes hook state.
        ctx.RunCleanups();
        Assert.True(captured.IsCancellationRequested);

        // Cache no longer has a subscriber for the key.
        gate.SetResult(42);
        await Task.Yield();

        // Cache is empty for the key (no Set happened because cancel fired first).
        Assert.False(cache.TryGet<int>("a", out _));
    }

    // ════════════════════════════════════════════════════════════════
    //  Retry with backoff
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Retry_Invokes_Fetcher_Multiple_Times_And_Settles_On_Success()
    {
        var cache = NewCache();
        var dispatcher = new InlineDispatcher();
        // The fetcher runs on Timer/threadpool callbacks, so all access to `calls`
        // must be synchronized — Interlocked on the increment, Volatile on reads.
        int calls = 0;
        Func<CancellationToken, Task<int>> fetcher = _ =>
        {
            int n = Interlocked.Increment(ref calls);
            return n < 3 ? Task.FromException<int>(new InvalidOperationException("transient")) : Task.FromResult(42);
        };

        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        ctx.UseResource(fetcher, cache, Array.Empty<object>(),
            new ResourceOptions(RetryCount: 3), dispatcher);
        ctx.FlushEffects();

        // `calls` increments ahead of the dispatcher.Post that publishes state, so
        // polling on `calls` alone races the state transition. Poll the rendered
        // AsyncValue instead — re-rendering with identical deps just reads the
        // current hook state and does not re-invoke the fetcher.
        AsyncValue<int>? probe = null;
        var sw = global::System.Diagnostics.Stopwatch.StartNew();
        // Budget covers retry backoff (100ms + 200ms = 300ms minimum) plus
        // threadpool/timer scheduling slack on a loaded CI runner.
        while (sw.Elapsed < TimeSpan.FromSeconds(30))
        {
            ctx.BeginRender(() => { });
            probe = ctx.UseResource(fetcher, cache, Array.Empty<object>(),
                new ResourceOptions(RetryCount: 3), dispatcher);
            if (probe is AsyncValue<int>.Data) break;
            await Task.Delay(25);
        }

        Assert.Equal(3, Volatile.Read(ref calls));
        Assert.Equal(new AsyncValue<int>.Data(42), probe);
    }

    [Fact]
    public async Task Retry_Exhausted_Surfaces_Final_Error()
    {
        var cache = NewCache();
        var dispatcher = new InlineDispatcher();
        // See sibling test: synchronized access required because the fetcher runs
        // on Timer/threadpool callbacks during retry.
        int calls = 0;
        Func<CancellationToken, Task<int>> fetcher = _ =>
        {
            int n = Interlocked.Increment(ref calls);
            return Task.FromException<int>(new InvalidOperationException($"attempt{n}"));
        };

        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        ctx.UseResource(fetcher, cache, Array.Empty<object>(),
            new ResourceOptions(RetryCount: 2), dispatcher);
        ctx.FlushEffects();

        // Poll the rendered state, not the call counter. Budget covers retry
        // backoff plus threadpool/timer scheduling slack on a loaded CI runner.
        AsyncValue<int>? probe = null;
        var sw = global::System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(30))
        {
            ctx.BeginRender(() => { });
            probe = ctx.UseResource(fetcher, cache, Array.Empty<object>(),
                new ResourceOptions(RetryCount: 2), dispatcher);
            if (probe is AsyncValue<int>.Error) break;
            await Task.Delay(25);
        }

        Assert.Equal(3, Volatile.Read(ref calls));
        var err = Assert.IsType<AsyncValue<int>.Error>(probe);
        Assert.Contains("attempt", err.Exception.Message);
    }

    // ════════════════════════════════════════════════════════════════
    //  Siblings share cache via explicit CacheKey
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Two_Siblings_With_Same_Explicit_Key_Share_Cache()
    {
        var cache = NewCache();
        var opts = new ResourceOptions(CacheKey: "shared", StaleTime: TimeSpan.FromMinutes(1));

        int calls = 0;
        Func<CancellationToken, Task<int>> fetcher = _ => { calls++; return Task.FromResult(100); };

        var ctx1 = new RenderContext();
        ctx1.BeginRender(() => { });
        var v1 = ctx1.UseResource(fetcher, cache, Array.Empty<object>(), opts, new InlineDispatcher());
        Assert.Equal(new AsyncValue<int>.Data(100), v1);

        // Sibling 2 — uses same key, same cache, within StaleTime → cache hit, no fetch.
        var ctx2 = new RenderContext();
        ctx2.BeginRender(() => { });
        var v2 = ctx2.UseResource(fetcher, cache, Array.Empty<object>(), opts, new InlineDispatcher());
        Assert.Equal(new AsyncValue<int>.Data(100), v2);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Two_Siblings_With_Auto_Keys_Do_Not_Share_By_Default()
    {
        var cache = NewCache();
        int calls = 0;
        Func<CancellationToken, Task<int>> fetcher = _ => { calls++; return Task.FromResult(100); };

        var ctx1 = new RenderContext();
        ctx1.BeginRender(() => { });
        ctx1.UseResource(fetcher, cache, new object[] { "id" }, null, new InlineDispatcher());
        var ctx2 = new RenderContext();
        ctx2.BeginRender(() => { });
        ctx2.UseResource(fetcher, cache, new object[] { "id" }, null, new InlineDispatcher());

        Assert.Equal(2, calls);
    }

    // ════════════════════════════════════════════════════════════════
    //  No-dispatcher fallback
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Null_Dispatcher_Runs_Continuation_Inline()
    {
        var cache = NewCache();
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        // The rerender callback runs on the threadpool when the fetch task completes.
        // Under heavy parallel test load the threadpool can be starved long enough that
        // a fixed-budget poll loop misses it, so signal a TCS from the callback and
        // await that instead. The Volatile read after the await observes the final count.
        int rerendered = 0;
        var rerenderedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var ctx = new RenderContext();
        ctx.BeginRender(() =>
        {
            Volatile.Write(ref rerendered, 1);
            rerenderedTcs.TrySetResult();
        });
        ctx.UseResource(_ => tcs.Task, cache, Array.Empty<object>(), null, dispatcher: null);

        tcs.SetResult(7);

        await rerenderedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, Volatile.Read(ref rerendered));
    }

    // ════════════════════════════════════════════════════════════════
    //  Ambient-cache overload (reads AppContexts.QueryCache)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Ambient_Cache_Overload_Uses_AppContexts_Default()
    {
        // Sync-complete path — the cache-less overload resolves the QueryCache via
        // ctx.UseContext(AppContexts.QueryCache). With no ContextScope installed, that
        // returns the process-wide default cache. We assert the entry lands there.
        var defaultCache = AppContexts.QueryCache.DefaultValue;
        var key = $"ambient-cache-test/{Guid.NewGuid():N}";
        defaultCache.Invalidate(key);

        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var v = ctx.UseResource(
            _ => Task.FromResult(99),
            new object[] { key },
            new ResourceOptions(CacheKey: key, StaleTime: TimeSpan.FromMinutes(1)),
            new InlineDispatcher());

        Assert.Equal(new AsyncValue<int>.Data(99), v);
        Assert.True(defaultCache.TryGet<int>(key, out var entry));
        Assert.Equal(99, entry.Value);

        // Cleanup — don't pollute subsequent tests.
        defaultCache.Invalidate(key);
    }

    // ════════════════════════════════════════════════════════════════
    //  Re-render short-circuit — identical AsyncValue does not rerender
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fast-path: when a fetch completes and the new value is record-equal to <c>LastValue</c>,
    /// the hook short-circuits and does not call <c>requestRerender</c>. This is how back-to-
    /// back identical polls avoid a render storm.
    /// </summary>
    [Fact]
    public async Task Identical_Data_Across_Refetches_Skips_Rerender()
    {
        var cache = NewCache();
        int rerenders = 0;
        var dispatcher = new InlineDispatcher();
        var ctx = new RenderContext();
        ctx.BeginRender(() => Interlocked.Increment(ref rerenders));

        // First render: sync-complete with value 5 lands in Data(5) — no rerender requested
        // because the state settled inside the hook body.
        var v1 = ctx.UseResource(
            _ => Task.FromResult(5), cache, new object[] { "k" }, null, dispatcher);
        Assert.Equal(new AsyncValue<int>.Data(5), v1);
        int baseline = rerenders;

        // Invalidate the cache entry so the next render re-fetches. Fetcher returns the same
        // value (5) — the hook's record-equality compare should not trigger a rerender.
        cache.Invalidate("k");

        ctx.BeginRender(() => Interlocked.Increment(ref rerenders));
        // Force an async fetch this time so the continuation runs on the continuation thread.
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = ctx.UseResource(_ => tcs.Task, cache, new object[] { "k" }, null, dispatcher);
        tcs.SetResult(5);
        for (int i = 0; i < 20 && rerenders == baseline; i++) await Task.Delay(10);

        // If the short-circuit held, rerenders stayed the same across the refetch since the
        // continuation landed the same Data(5) as what LastValue already held.
        Assert.Equal(baseline, rerenders);
    }

    // ════════════════════════════════════════════════════════════════
    //  Unmount after completion — cache retains entry
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// After a successful fetch has landed in the cache, unmounting the hook must not clear
    /// the cache entry — another subscriber or a remount within <c>CacheTime</c> should still
    /// see the value. (The QueryCache eviction tests cover the subscriber-count mechanics
    /// end-to-end; this test verifies the hook itself doesn't drop data on teardown.)
    /// </summary>
    [Fact]
    public void Unmount_After_Completion_Retains_Cache_Entry()
    {
        var cache = NewCache();
        var dispatcher = new InlineDispatcher();
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        var v = ctx.UseResource(
            _ => Task.FromResult(123), cache, new object[] { "retain" },
            new ResourceOptions(CacheKey: "retain", StaleTime: TimeSpan.FromMinutes(1)),
            dispatcher);
        ctx.FlushEffects();

        Assert.Equal(new AsyncValue<int>.Data(123), v);
        Assert.True(cache.TryGet<int>("retain", out var before));
        Assert.Equal(123, before.Value);

        // Unmount — the hook's CTS disposes, but the cache entry must remain.
        ctx.RunCleanups();

        Assert.True(cache.TryGet<int>("retain", out var after));
        Assert.Equal(123, after.Value);
    }
}
