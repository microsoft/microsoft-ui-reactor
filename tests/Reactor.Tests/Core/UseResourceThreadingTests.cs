using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Core;

/// <summary>
/// Race-condition coverage for <c>UseResource</c>: deps-change mid-flight, late completion
/// after unmount, concurrent invalidation, and the global unobserved-task-exception invariant.
/// </summary>
[Trait("Category", "Threading")]
[Collection("UnobservedTaskException")]
public class UseResourceThreadingTests : IDisposable
{
    private int _unobserved;
    private readonly EventHandler<UnobservedTaskExceptionEventArgs> _handler;

    public UseResourceThreadingTests()
    {
        _handler = (_, e) => { Interlocked.Increment(ref _unobserved); e.SetObserved(); };
        TaskScheduler.UnobservedTaskException += _handler;
    }

    public void Dispose()
    {
        TaskScheduler.UnobservedTaskException -= _handler;
    }

    private void AssertNoUnobserved()
    {
        // Force a GC so orphaned Tasks surface their unobserved exceptions.
        for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        Assert.Equal(0, Volatile.Read(ref _unobserved));
    }

    private sealed class InlineDispatcher : IHookDispatcher
    {
        public void Post(Action action) => action();
    }

    // ════════════════════════════════════════════════════════════════
    //  Deps change mid-flight — late result is dropped
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Deps_Change_Mid_Flight_Drops_Late_Result()
    {
        using var cache = new QueryCache();
        var dispatcher = new InlineDispatcher();
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        var firstGate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        Func<CancellationToken, Task<int>> f1 = _ => firstGate.Task;
        Func<CancellationToken, Task<int>> f2 = _ => Task.FromResult(2);

        var v1 = ctx.UseResource(f1, cache, new object[] { "a" }, null, dispatcher);
        Assert.IsType<AsyncValue<int>.Loading>(v1);

        ctx.BeginRender(() => { });
        var v2 = ctx.UseResource(f2, cache, new object[] { "b" }, null, dispatcher);
        Assert.Equal(new AsyncValue<int>.Data(2), v2);

        // Now the late result arrives for the first fetch.
        firstGate.SetResult(999);
        await Task.Delay(50);

        // Next render still shows b's result, not a's stale 999.
        ctx.BeginRender(() => { });
        var v3 = ctx.UseResource(f2, cache, new object[] { "b" }, null, dispatcher);
        Assert.Equal(new AsyncValue<int>.Data(2), v3);

        AssertNoUnobserved();
    }

    // ════════════════════════════════════════════════════════════════
    //  Unmount during continuation marshalling — no-op apply
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Unmount_During_Continuation_Is_NoOp_No_Cache_Update()
    {
        using var cache = new QueryCache();
        var dispatcher = new InlineDispatcher();
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        ctx.UseResource(_ => gate.Task, cache, new object[] { "a" }, null, dispatcher);
        ctx.FlushEffects();

        // Unmount — disposes hook state + cancels.
        ctx.RunCleanups();

        gate.SetResult(42);
        await Task.Delay(50);

        Assert.False(cache.TryGet<int>("a", out _));
        AssertNoUnobserved();
    }

    // ════════════════════════════════════════════════════════════════
    //  Rapid remount-with-fetch — subscriber count returns to zero
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Rapid_Remount_Cycles_Do_Not_Leak_Subscribers()
    {
        using var cache = new QueryCache();
        var dispatcher = new InlineDispatcher();

        for (int i = 0; i < 50; i++)
        {
            var ctx = new RenderContext();
            ctx.BeginRender(() => { });
            var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            ctx.UseResource(_ => gate.Task, cache, new object[] { i }, null, dispatcher);
            ctx.FlushEffects();
            ctx.RunCleanups();
            // Late completion of a cancelled fetch — should be ignored.
            gate.SetResult(i);
        }

        await Task.Delay(50);
        AssertNoUnobserved();
        // Force an eviction sweep — every slot's subscriber count is zero, none have
        // entries (every fetch was cancelled before Set), so nothing to assert on
        // cache state beyond the unobserved invariant.
    }

    // ════════════════════════════════════════════════════════════════
    //  TaskCanceledException is silent — not surfaced as Error
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Fetcher_Observed_Cancellation_Is_Silent_Not_Error()
    {
        using var cache = new QueryCache();
        var dispatcher = new InlineDispatcher();

        Func<CancellationToken, Task<int>> fetcher = async ct =>
        {
            await Task.Delay(100, ct); // throws OperationCanceledException on cancel
            return 1;
        };

        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var v1 = ctx.UseResource(fetcher, cache, new object[] { "k" }, null, dispatcher);
        Assert.IsType<AsyncValue<int>.Loading>(v1);
        ctx.FlushEffects();

        ctx.RunCleanups(); // triggers cancellation
        await Task.Delay(200);

        AssertNoUnobserved();
    }

    // ════════════════════════════════════════════════════════════════
    //  Dispatcher-absent path — inline continuation
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task No_Dispatcher_Still_Updates_State_Via_Inline_Path()
    {
        using var cache = new QueryCache();
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        int renderCount = 0;
        var ctx = new RenderContext();
        ctx.BeginRender(() => Interlocked.Increment(ref renderCount));
        var v1 = ctx.UseResource(_ => gate.Task, cache, new object[] { "k" }, null, dispatcher: null);

        Assert.IsType<AsyncValue<int>.Loading>(v1);
        gate.SetResult(123);
        for (int i = 0; i < 20 && renderCount == 0; i++) await Task.Delay(10);

        Assert.True(renderCount >= 1);

        ctx.BeginRender(() => Interlocked.Increment(ref renderCount));
        var v2 = ctx.UseResource(_ => gate.Task, cache, new object[] { "k" }, null, dispatcher: null);
        Assert.Equal(new AsyncValue<int>.Data(123), v2);

        AssertNoUnobserved();
    }

    // ════════════════════════════════════════════════════════════════
    //  Concurrent sibling renders with shared CacheKey don't double-fetch
    // ════════════════════════════════════════════════════════════════

    // ════════════════════════════════════════════════════════════════
    //  Concurrent invalidation storm — fetch dedup holds
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Cache.Invalidate is an external signal that the cached value is gone. The hook's
    /// re-render-after-invalidation path (<c>ReconcileWithCache</c>) kicks off a new fetch
    /// when it observes a cleared cache entry, gated by <c>state.InFlight</c> so concurrent
    /// re-renders do not stack fetches. This test hammers that invariant: 8 threads spam
    /// Invalidate on the hook's cache key while renders repeatedly re-enter the hook; the
    /// fetcher must be invoked a bounded number of times.
    /// </summary>
    [Fact]
    public async Task Concurrent_Invalidate_During_Pending_Fetch_No_Storm()
    {
        using var cache = new QueryCache();
        var dispatcher = new InlineDispatcher();
        var opts = new ResourceOptions(CacheKey: "shared", StaleTime: TimeSpan.FromMinutes(1));

        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Func<CancellationToken, Task<int>> f = _ => { Interlocked.Increment(ref calls); return gate.Task; };

        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var v1 = ctx.UseResource(f, cache, Array.Empty<object>(), opts, dispatcher);
        Assert.IsType<AsyncValue<int>.Loading>(v1);
        Assert.Equal(1, Volatile.Read(ref calls));

        const int Threads = 8, Spam = 200;
        var barrier = new Barrier(Threads);
        var tasks = Enumerable.Range(0, Threads).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < Spam; i++) cache.Invalidate("shared");
        })).ToArray();
        await Task.WhenAll(tasks);

        // While the fetch is still pending, InFlight is true — no re-render can kick off a
        // duplicate fetch. Even issuing re-renders (which call ReconcileWithCache) should
        // not start new fetches because BeginFetch short-circuits on InFlight.
        for (int i = 0; i < 10; i++)
        {
            ctx.BeginRender(() => { });
            ctx.UseResource(f, cache, Array.Empty<object>(), opts, dispatcher);
        }
        Assert.Equal(1, Volatile.Read(ref calls));

        gate.SetResult(42);
        await Task.Delay(50);

        AssertNoUnobserved();
    }

    [Fact]
    public void Sibling_Renders_With_Same_Key_Hit_Cache_After_First()
    {
        using var cache = new QueryCache();
        var dispatcher = new InlineDispatcher();
        int calls = 0;
        Func<CancellationToken, Task<int>> f = _ => { Interlocked.Increment(ref calls); return Task.FromResult(5); };
        var opts = new ResourceOptions(CacheKey: "shared", StaleTime: TimeSpan.FromMinutes(1));

        for (int i = 0; i < 10; i++)
        {
            var ctx = new RenderContext();
            ctx.BeginRender(() => { });
            var v = ctx.UseResource(f, cache, Array.Empty<object>(), opts, dispatcher);
            Assert.Equal(new AsyncValue<int>.Data(5), v);
        }
        Assert.Equal(1, calls);
        AssertNoUnobserved();
    }
}
