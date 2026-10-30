using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Core;

public class ResourceDependencyIdentityTests
{
    private sealed class InlineDispatcher : IHookDispatcher
    {
        public void Post(Action action) => action();
    }

    private sealed class QueuedDispatcher : IHookDispatcher
    {
        private readonly Queue<Action> _actions = new();
        public void Post(Action action) => _actions.Enqueue(action);
        public void Drain() { while (_actions.TryDequeue(out var action)) action(); }
    }

    // Offset cursors and sequential items, as in UseInfiniteResourceTests.FakeFetcher.
    // Start above zero so an unloaded placeholder cannot look like a fetched item.
    private static Func<string?, CancellationToken, Task<Page<int, string>>> FakeFetcher(
        int itemOffset, List<int> requests, int totalCount = 30)
        => (cursor, _) =>
        {
            int start = cursor is null ? 0 : int.Parse(cursor);
            requests.Add(start);
            return Task.FromResult(new Page<int, string>(
                Enumerable.Range(itemOffset + start, 10).ToArray(),
                start + 10 < totalCount ? (start + 10).ToString() : null, totalCount));
        };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resource_Colliding_Deps_Refetch_Instead_Of_Returning_Cached_Data(bool asyncCompletion)
    {
        using var cache = new QueryCache();
        var ctx = new RenderContext();
        var dispatcher = new InlineDispatcher();
        var pending = new TaskCompletionSource<int>();
        var options = new ResourceOptions(StaleTime: TimeSpan.FromDays(1));
        string? key = null;
        cache.EntryChanged += changed => key = changed;
        int calls = 0;
        AsyncValue<int> Render(object[] deps, int value, Task<int>? task = null)
        {
            ctx.BeginRender(() => { });
            var result = ctx.UseResource(_ => { calls++; return task ?? Task.FromResult(value); },
                cache, deps, options, dispatcher);
            ctx.FlushEffects();
            return result;
        }
        try
        {
            // 31 * 1 + 32 == 31 * 2 + 1: different deps, same generated cache key.
            var first = Render([1, 32], 42, asyncCompletion ? pending.Task : null);
            if (asyncCompletion)
            {
                Assert.IsType<AsyncValue<int>.Loading>(first);
                pending.SetResult(42);
            }
            else Assert.Equal(new AsyncValue<int>.Data(42), first);
            Assert.Equal(new AsyncValue<int>.Data(99), Render([2, 1], 99));
            Assert.Equal(new AsyncValue<int>.Data(99), Render([2, 1], 999));
            Assert.Equal(2, calls); // Equal deps in a new array still hit the cache.
            Assert.Equal(new AsyncValue<int>.Data(42), Render([1, 32], 42));
            Assert.Equal(3, calls);
            Assert.Equal(1, cache.Count);
            Assert.NotNull(key);
            Assert.True(cache.TryGet<int>(key, out var entry));
            Assert.Equal(1, entry.SubscriberCount);
        }
        finally { ctx.RunCleanups(); }
    }

    [Fact]
    public void Resource_Cancelled_Colliding_Query_Refetches_After_Queued_Cache_Notification()
    {
        using var cache = new QueryCache();
        var ctx = new RenderContext();
        var dispatcher = new InlineDispatcher();
        var notifications = new QueuedDispatcher();
        // Pin the defensive case where a cache notification arrives after a deps change.
        cache.DispatcherPost = notifications.Post;
        var options = new ResourceOptions(StaleTime: TimeSpan.FromDays(1));
        int calls = 0, rerenders = 0;
        AsyncValue<int> Render(object[] deps, Task<int> task)
        {
            ctx.BeginRender(() => rerenders++);
            var result = ctx.UseResource(_ => { calls++; return task; }, cache, deps, options, dispatcher);
            ctx.FlushEffects();
            return result;
        }
        try
        {
            Assert.Equal(new AsyncValue<int>.Data(42), Render([1, 32], Task.FromResult(42)));
            // Cancellation keeps the previous Data, while the cache still belongs to [1, 32].
            Assert.Equal(new AsyncValue<int>.Data(42),
                Render([2, 1], Task.FromCanceled<int>(new CancellationToken(canceled: true))));
            Assert.Equal(2, calls);
            Assert.Equal(0, rerenders);

            // The old write's delayed notification must detect the mismatched deps.
            notifications.Drain();
            Assert.Equal(1, rerenders);
            Assert.Equal(new AsyncValue<int>.Data(99), Render([2, 1], Task.FromResult(99)));
            Assert.Equal(3, calls);

            // The matching write must not schedule another render.
            notifications.Drain();
            Assert.Equal(1, rerenders);
        }
        finally { ctx.RunCleanups(); }
    }

    [Fact]
    public void Resource_Reused_Mutated_Array_Cancels_Colliding_Request_And_Drops_Queued_Completion()
    {
        using var cache = new QueryCache();
        var ctx = new RenderContext();
        var dispatcher = new QueuedDispatcher();
        var old = new TaskCompletionSource<string>();
        CancellationToken token = default;
        int newCalls = 0;
        object[] deps = [1, 32];
        try
        {
            ctx.BeginRender(() => { });
            ctx.UseResource(ct => { token = ct; return old.Task; }, cache, deps, dispatcher: dispatcher);
            ctx.FlushEffects();
            old.SetResult("old"); // Completion queued before the deps switch.

            deps[0] = 2;
            deps[1] = 1; // Same caller array, same deps hash as [1, 32].
            ctx.BeginRender(() => { });
            var current = ctx.UseResource(_ => { newCalls++; return Task.FromResult("new"); },
                cache, deps, dispatcher: dispatcher);
            Assert.True(token.IsCancellationRequested);
            dispatcher.Drain();

            ctx.BeginRender(() => { });
            var after = ctx.UseResource(_ => Task.FromResult("unexpected"), cache, deps, dispatcher: dispatcher);
            Assert.Equal(1, newCalls);
            Assert.Equal(new AsyncValue<string>.Data("new"), current);
            Assert.Equal(new AsyncValue<string>.Data("new"), after);
        }
        finally { ctx.RunCleanups(); }
    }

    [Fact]
    public void Resource_Reused_Mutated_Array_With_Unchanged_Hash_Refetches_Without_Leaking_Data()
    {
        using var cache = new QueryCache();
        var ctx = new RenderContext();
        var dispatcher = new InlineDispatcher();
        object[] deps = [1, 32];
        var options = new ResourceOptions(StaleTime: TimeSpan.FromDays(1));
        int calls = 0;
        AsyncValue<int> Render(int value)
        {
            ctx.BeginRender(() => { });
            var result = ctx.UseResource(_ => { calls++; return Task.FromResult(value); }, cache, deps, options, dispatcher);
            ctx.FlushEffects();
            return result;
        }
        try
        {
            Assert.Equal(new AsyncValue<int>.Data(42), Render(42));
            deps[0] = 2;
            deps[1] = 1;
            Assert.Equal(new AsyncValue<int>.Data(99), Render(99));
            deps[0] = 1;
            deps[1] = 32;
            Assert.Equal(new AsyncValue<int>.Data(42), Render(42));
            Assert.Equal(3, calls);
            Assert.Equal(1, cache.Count);
        }
        finally { ctx.RunCleanups(); }
    }

    [Fact]
    public void Resource_Explicit_Key_Remains_The_Callers_Shared_Identity()
    {
        using var cache = new QueryCache();
        var first = new RenderContext();
        var second = new RenderContext();
        var options = new ResourceOptions(CacheKey: "shared-order", StaleTime: TimeSpan.FromDays(1));
        int calls = 0;
        try
        {
            first.BeginRender(() => { });
            first.UseResource(_ => { calls++; return Task.FromResult("shared"); }, cache, [1, 32], options, new InlineDispatcher());
            first.FlushEffects();
            second.BeginRender(() => { });
            var value = second.UseResource(_ => { calls++; return Task.FromResult("other"); }, cache, [2, 1], options, new InlineDispatcher());
            second.FlushEffects();
            Assert.Equal(new AsyncValue<string>.Data("shared"), value);
            Assert.Equal(1, calls);
            Assert.True(cache.TryGet<string>("shared-order", out var entry));
            Assert.Equal(2, entry.SubscriberCount);
        }
        finally { first.RunCleanups(); second.RunCleanups(); }
    }

    [Fact]
    public void Resource_Explicit_Key_Does_Not_Cancel_Or_Refetch_When_Deps_Change()
    {
        using var cache = new QueryCache();
        var ctx = new RenderContext();
        var dispatcher = new InlineDispatcher();
        var pending = new TaskCompletionSource<int>();
        var options = new ResourceOptions(CacheKey: "x");
        CancellationToken firstToken = default;
        int calls = 0;
        AsyncValue<int> Render(object[] deps)
        {
            ctx.BeginRender(() => { });
            var result = ctx.UseResource(ct =>
            {
                if (calls++ == 0) firstToken = ct;
                return pending.Task;
            }, cache, deps, options, dispatcher);
            ctx.FlushEffects();
            return result;
        }
        try
        {
            Assert.IsType<AsyncValue<int>.Loading>(Render([1]));
            Assert.True(firstToken.CanBeCanceled);
            Assert.IsType<AsyncValue<int>.Loading>(Render([2]));
            Assert.Equal(1, calls);
            Assert.False(firstToken.IsCancellationRequested);

            pending.SetResult(42);
            Assert.Equal(new AsyncValue<int>.Data(42), Render([2]));
            Assert.Equal(1, calls);
        }
        finally { ctx.RunCleanups(); }
    }

    [Fact]
    public void Infinite_Colliding_Deps_Cancel_And_Drop_Late_Pages()
    {
        using var cache = new QueryCache();
        var ctx = new RenderContext();
        var old = new TaskCompletionSource<Page<string, string>>();
        var dispatcher = new InlineDispatcher();
        CancellationToken token = default;
        var options = new InfiniteResourceOptions(PageSize: 1);
        try
        {
            ctx.BeginRender(() => { });
            ctx.UseInfiniteResource<string, string>((_, ct) => { token = ct; return old.Task; },
                cache, [1, 32], options, dispatcher);
            ctx.FlushEffects();
            ctx.BeginRender(() => { });
            var current = ctx.UseInfiniteResource<string, string>((_, _) =>
                Task.FromResult(new Page<string, string>(["new"], null, 1)),
                cache, [2, 1], options, dispatcher);
            Assert.True(token.IsCancellationRequested);
            old.SetResult(new Page<string, string>(["old"], null, 1));
            Assert.Equal("new", current.Items[0]);
        }
        finally { ctx.RunCleanups(); }
    }

    [Fact]
    public void Infinite_Shared_Prefix_Checks_Each_Page_And_Foreign_Predecessors()
    {
        using var cache = new QueryCache();
        var a = new RenderContext();
        var b = new RenderContext();
        var returningA = new RenderContext();
        var requestsA = new List<int>();
        var requestsB = new List<int>();
        InfiniteResource<int> Render(RenderContext target, object[] deps, int itemOffset, List<int> requests)
        {
            target.BeginRender(() => { });
            var resource = target.UseInfiniteResource(FakeFetcher(itemOffset, requests, totalCount: 40), cache, deps,
                new InfiniteResourceOptions(PageSize: 10, CacheKeyPrefix: "orders"), new InlineDispatcher());
            target.FlushEffects();
            return resource;
        }
        try
        {
            var resourceA = Render(a, [1, 32], 100, requestsA);
            resourceA.FetchNext();
            var resourceB = Render(b, [2, 1], 200, requestsB);
            Assert.Equal(200, resourceB.ItemAt(0));

            // Jump to page 2: cached A1 must not count as B's loaded page 1.
            resourceB.EnsureRange(20, 20);
            Assert.Equal(210, resourceB.ItemAt(10));
            Assert.Equal(220, resourceB.ItemAt(20));
            Assert.Equal(new[] { 0, 10, 20 }, requestsB);
            Assert.Equal(100, resourceA.ItemAt(0));
            Assert.Equal(110, resourceA.ItemAt(10));

            // A still owns its loaded pages even though B replaced their shared cache entries.
            Assert.Equal(20, resourceA.EstimatedRemaining);
            resourceA.FetchNext();
            Assert.Equal(new[] { 0, 10, 20 }, requestsA);
            Assert.Equal(10, resourceA.EstimatedRemaining);
            Assert.Equal(120, resourceA.ItemAt(20));

            var again = Render(returningA, [1, 32], 100, requestsA);
            again.FetchNext();
            Assert.Equal(100, again.ItemAt(0));
            Assert.Equal(110, again.ItemAt(10));
            Assert.Equal(new[] { 0, 10, 20, 0, 10 }, requestsA);
            Assert.Equal(3, cache.Count);
        }
        finally { a.RunCleanups(); b.RunCleanups(); returningA.RunCleanups(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Infinite_Evicted_Page_Uses_Its_Predecessors_Cursor(bool localPredecessor)
    {
        using var cache = new QueryCache();
        var ctx = new RenderContext();
        var requests = new List<int>();
        string? pageOneKey = null;
        cache.EntryChanged += key =>
        {
            if (key.EndsWith("/page:1", StringComparison.Ordinal)) pageOneKey = key;
        };
        try
        {
            ctx.BeginRender(() => { });
            var resource = ctx.UseInfiniteResource(FakeFetcher(1000, requests, totalCount: 60),
                cache, [], new InfiniteResourceOptions(PageSize: 10, MaxLoadedPages: 2),
                new InlineDispatcher());
            ctx.FlushEffects();
            resource.FetchNext();

            if (localPredecessor)
                Assert.Equal(1000, resource.ItemAt(0)); // Keep page 0 when page 2 evicts page 1.
            resource.FetchNext();
            if (!localPredecessor)
                resource.FetchNext(); // Keep pages 2/3; page 0 is only in the shared cache.

            Assert.NotNull(pageOneKey);
            Assert.Equal(localPredecessor ? new[] { 0, 10, 20 } : new[] { 0, 10, 20, 30 }, requests);
            if (localPredecessor) cache.Clear();
            else cache.Invalidate(pageOneKey);

            resource.EnsureRange(10, 19);

            Assert.Equal(1010, resource.ItemAt(10));
            Assert.Equal(1019, resource.ItemAt(19));
            Assert.Equal(localPredecessor ? new[] { 0, 10, 20, 10 } : new[] { 0, 10, 20, 30, 10 }, requests);
        }
        finally { ctx.RunCleanups(); }
    }

    [Fact]
    public void Infinite_Reused_Mutated_Array_With_Unchanged_Hash_Refetches_Each_Page()
    {
        using var cache = new QueryCache();
        var ctx = new RenderContext();
        object[] deps = [1, 32];
        var requests = new List<int>();
        InfiniteResource<int> Render(int itemOffset)
        {
            ctx.BeginRender(() => { });
            var resource = ctx.UseInfiniteResource(FakeFetcher(itemOffset, requests), cache, deps,
                new InfiniteResourceOptions(PageSize: 10), new InlineDispatcher());
            ctx.FlushEffects();
            resource.FetchNext();
            return resource;
        }
        try
        {
            var first = Render(100);
            Assert.Equal(100, first.ItemAt(0));
            Assert.Equal(110, first.ItemAt(10));
            deps[0] = 2;
            deps[1] = 1;
            var second = Render(200);
            Assert.Equal(200, second.ItemAt(0));
            Assert.Equal(210, second.ItemAt(10));
            deps[0] = 1;
            deps[1] = 32;
            var returning = Render(100);
            Assert.Equal(100, returning.ItemAt(0));
            Assert.Equal(110, returning.ItemAt(10));
            Assert.Equal(210, second.ItemAt(10));
            Assert.Equal(new[] { 0, 10, 0, 10, 0, 10 }, requests);
            Assert.Equal(2, cache.Count);
        }
        finally { ctx.RunCleanups(); }
    }

    [Fact]
    public void Infinite_Async_Completions_With_A_Shared_Hash_Do_Not_Leak_Into_A_Peer()
    {
        using var cache = new QueryCache();
        var a = new RenderContext();
        var b = new RenderContext();
        var returningA = new RenderContext();
        var pendingA = new TaskCompletionSource<Page<string, string>>();
        var pendingB = new TaskCompletionSource<Page<string, string>>();
        var options = new InfiniteResourceOptions(PageSize: 1, CacheKeyPrefix: "orders");
        try
        {
            a.BeginRender(() => { });
            var resourceA = a.UseInfiniteResource<string, string>((_, _) => pendingA.Task,
                cache, [1, 32], options, new InlineDispatcher());
            a.FlushEffects();
            b.BeginRender(() => { });
            var resourceB = b.UseInfiniteResource<string, string>((_, _) => pendingB.Task,
                cache, [2, 1], options, new InlineDispatcher());
            b.FlushEffects();
            pendingA.SetResult(new Page<string, string>(["A"], null, 1));
            pendingB.SetResult(new Page<string, string>(["B"], null, 1));
            Assert.Equal("A", resourceA.ItemAt(0));
            Assert.Equal("B", resourceB.ItemAt(0));
            int calls = 0;
            returningA.BeginRender(() => { });
            var again = returningA.UseInfiniteResource<string, string>((_, _) =>
            {
                calls++;
                return Task.FromResult(new Page<string, string>(["A-new"], null, 1));
            }, cache, [1, 32], options, new InlineDispatcher());
            returningA.FlushEffects();
            Assert.Equal("A-new", again.ItemAt(0));
            Assert.Equal(1, calls);
        }
        finally { a.RunCleanups(); b.RunCleanups(); returningA.RunCleanups(); }
    }
}
