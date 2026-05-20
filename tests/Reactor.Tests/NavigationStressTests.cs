using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Reactor.Navigation;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Stress and edge-case tests for the navigation subsystem.
///
/// Covers rapid forward/back cycles, cache churn, concurrent cache access,
/// guard cancellation under load, deep PopTo, serialization round-trips with
/// large stacks, deep link resolution stress, query strings, optional params,
/// wildcards, and NavigatingToContext validation.
/// </summary>
public partial class NavigationStressTests
{
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [JsonDerivedType(typeof(Home), "home")]
    [JsonDerivedType(typeof(Detail), "detail")]
    [JsonDerivedType(typeof(Settings), "settings")]
    [JsonDerivedType(typeof(Profile), "profile")]
    private abstract record Route;
    private sealed record Home : Route;
    private sealed record Detail(int Id) : Route;
    private sealed record Settings : Route;
    private sealed record Profile(string Name) : Route;

    [JsonSerializable(typeof(NavigationState<Route>))]
    private partial class StressJsonContext : JsonSerializerContext { }

    private static CachedPage MakePage(NavigationCacheMode cacheMode = NavigationCacheMode.Enabled) =>
        new() { MountedControl = null!, CacheMode = cacheMode };

    private static NavigationHandle<Route> CreateHandle(Route initial)
    {
        var stack = new NavigationStack<Route>(initial);
        return new NavigationHandle<Route>(stack);
    }

    // ════════════════════════════════════════════════════════════════
    //  1. Rapid forward/back cycles
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Rapid_Forward_Back_Cycles_Preserve_State()
    {
        var stack = new NavigationStack<Route>(new Home());

        // Push 100 routes
        for (int i = 0; i < 100; i++)
            stack.Push(new Detail(i));

        Assert.Equal(101, stack.Depth);
        Assert.Equal(new Detail(99), stack.Current);

        // Pop all 100
        for (int i = 0; i < 100; i++)
            Assert.True(stack.Pop());

        Assert.IsType<Home>(stack.Current);
        Assert.Equal(1, stack.Depth);
        Assert.Equal(100, stack.ForwardStack.Count);

        // Push 100 again
        for (int i = 200; i < 300; i++)
            stack.Push(new Detail(i));

        Assert.Equal(101, stack.Depth);
        Assert.Equal(new Detail(299), stack.Current);
        Assert.Empty(stack.ForwardStack); // Push clears forward stack
        Assert.Equal(100, stack.BackStack.Count);
        Assert.IsType<Home>(stack.BackStack[0]);
        Assert.Equal(new Detail(298), stack.BackStack[^1]);
    }

    [Fact]
    public void Rapid_Forward_Back_Via_Handle_Fires_All_Events()
    {
        var handle = CreateHandle(new Home());
        int eventCount = 0;
        handle.Navigated += _ => eventCount++;

        for (int i = 0; i < 100; i++)
            handle.Navigate(new Detail(i));

        Assert.Equal(100, eventCount);

        for (int i = 0; i < 100; i++)
            handle.GoBack();

        Assert.Equal(200, eventCount);
        Assert.IsType<Home>(handle.CurrentRoute);
    }

    // ════════════════════════════════════════════════════════════════
    //  2. Cache churn under load
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Cache_Churn_Under_Load_Respects_MaxSize()
    {
        int evictions = 0;
        var cache = new NavigationCache(5, _ => evictions++);

        for (int i = 0; i < 100; i++)
            cache.Add(new Detail(i), MakePage());

        Assert.True(cache.Count <= 5);
        Assert.Equal(95, evictions); // 100 adds - 5 remaining = 95 evictions

        // Verify the last 5 entries are present (most recently added)
        for (int i = 95; i < 100; i++)
            Assert.True(cache.TryGet(new Detail(i), out _));

        // Verify early entries were evicted
        for (int i = 0; i < 90; i++)
            Assert.False(cache.TryGet(new Detail(i), out _));
    }

    [Fact]
    public void Cache_Churn_With_Mixed_Routes_Does_Not_Throw()
    {
        var cache = new NavigationCache(5);
        var ex = Record.Exception(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                cache.Add(new Detail(i), MakePage());
                cache.Add(new Profile($"User{i}"), MakePage());
                cache.TryGet(new Detail(i), out _);
                if (i % 3 == 0) cache.Remove(new Profile($"User{i}"));
            }
        });

        Assert.Null(ex);
        Assert.True(cache.Count <= 5);
    }

    // ════════════════════════════════════════════════════════════════
    //  3. Concurrent cache operations
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Concurrent_Cache_Operations_Do_Not_Throw()
    {
        var cache = new NavigationCache(10);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var tasks = new List<Task>();

        // Writer tasks — add entries
        for (int t = 0; t < 4; t++)
        {
            int taskId = t;
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 50; i++)
                    cache.Add(new Detail(taskId * 1000 + i), MakePage());
            }));
        }

        // Reader tasks — try to get entries
        for (int t = 0; t < 4; t++)
        {
            int taskId = t;
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 50; i++)
                    cache.TryGet(new Detail(taskId * 1000 + i), out _);
            }));
        }

        // Remover tasks
        for (int t = 0; t < 2; t++)
        {
            int taskId = t;
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 50; i++)
                    cache.Remove(new Detail(taskId * 1000 + i));
            }));
        }

        await Task.WhenAll(tasks);

        // Count should be valid (non-negative, within max size)
        Assert.True(cache.Count >= 0);
        Assert.True(cache.Count <= 10);
    }

    // ════════════════════════════════════════════════════════════════
    //  4. Guard cancellation under rapid navigation
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Guard_Cancels_Every_Other_Navigation_Halves_Depth()
    {
        var stack = new NavigationStack<Route>(new Home());
        int pushAttempt = 0;

        stack.Guard = ctx =>
        {
            pushAttempt++;
            if (pushAttempt % 2 == 0)
                ctx.Cancel();
        };

        for (int i = 0; i < 50; i++)
            stack.Push(new Detail(i));

        // Guard cancels every even attempt (2nd, 4th, ...),
        // so ~25 pushes succeed + 1 initial route = depth ~26
        Assert.Equal(26, stack.Depth);
        Assert.Equal(25, stack.BackStack.Count);
    }

    [Fact]
    public void Guard_Cancellation_Does_Not_Corrupt_Forward_Stack()
    {
        var stack = new NavigationStack<Route>(new Home());

        // Build a forward stack
        for (int i = 0; i < 10; i++)
            stack.Push(new Detail(i));
        for (int i = 0; i < 10; i++)
            stack.Pop();

        Assert.Equal(10, stack.ForwardStack.Count);

        // Now block all pushes
        stack.Guard = ctx => ctx.Cancel();

        for (int i = 100; i < 150; i++)
            stack.Push(new Detail(i));

        // Forward stack should be untouched since all pushes were cancelled
        Assert.Equal(10, stack.ForwardStack.Count);
        Assert.IsType<Home>(stack.Current);
    }

    // ════════════════════════════════════════════════════════════════
    //  5. PopTo with deep stack
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void PopTo_Bottom_Of_Deep_Stack()
    {
        var stack = new NavigationStack<Route>(new Home());

        for (int i = 0; i < 99; i++)
            stack.Push(new Detail(i));

        Assert.Equal(100, stack.Depth);

        // PopTo the Home at the very bottom
        var result = stack.PopTo(r => r is Home);

        Assert.True(result);
        Assert.IsType<Home>(stack.Current);
        Assert.Equal(1, stack.Depth);
        Assert.Empty(stack.BackStack);
        // Forward stack: current was Detail(98), then Detail(97)..Detail(0) from back stack
        Assert.Equal(99, stack.ForwardStack.Count);
    }

    [Fact]
    public void PopTo_Middle_Of_Deep_Stack()
    {
        var stack = new NavigationStack<Route>(new Home());

        for (int i = 0; i < 100; i++)
            stack.Push(new Detail(i));

        // PopTo Detail(50) which is at index 51 in the back stack (Home at 0, Detail(0) at 1, ...)
        var result = stack.PopTo(r => r is Detail d && d.Id == 50);

        Assert.True(result);
        Assert.Equal(new Detail(50), stack.Current);
        // Back stack: Home, Detail(0)..Detail(49) = 51 entries
        Assert.Equal(51, stack.BackStack.Count);
        // Forward stack: Detail(99) (was current), Detail(98)..Detail(51) = 49 entries
        Assert.Equal(49, stack.ForwardStack.Count);
    }

    // ════════════════════════════════════════════════════════════════
    //  6. State snapshot / restore round-trip with large stack
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void State_Snapshot_RoundTrip_With_Large_Stack()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);

        for (int i = 0; i < 49; i++)
            handle.Navigate(new Detail(i));

        // Go back a few to populate forward stack
        for (int i = 0; i < 5; i++)
            handle.GoBack();

        Assert.Equal(45, handle.Depth);
        Assert.Equal(44, handle.BackStack.Count);
        Assert.Equal(5, handle.ForwardStack.Count);

        var snapshot = handle.GetState();

        // Restore into a fresh handle
        var stack2 = new NavigationStack<Route>(new Home());
        var handle2 = new NavigationHandle<Route>(stack2);
        handle2.SetState(snapshot);

        Assert.Equal(handle.CurrentRoute, handle2.CurrentRoute);
        Assert.Equal(handle.Depth, handle2.Depth);
        Assert.Equal(handle.BackStack.Count, handle2.BackStack.Count);
        Assert.Equal(handle.ForwardStack.Count, handle2.ForwardStack.Count);

        // Verify all back stack routes are preserved
        for (int i = 0; i < handle.BackStack.Count; i++)
            Assert.Equal(handle.BackStack[i], handle2.BackStack[i]);

        // Verify all forward stack routes are preserved
        for (int i = 0; i < handle.ForwardStack.Count; i++)
            Assert.Equal(handle.ForwardStack[i], handle2.ForwardStack[i]);
    }

    [Fact]
    public void State_Snapshot_RoundTrip_Preserves_Polymorphic_Types_Via_Json()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);

        handle.Navigate(new Detail(42));
        handle.Navigate(new Settings());
        handle.Navigate(new Profile("Alice"));

        // App-side: caller picks JSON via their own source-gen context.
        var json = JsonSerializer.Serialize(handle.GetState(), StressJsonContext.Default.NavigationStateRoute);

        var stack2 = new NavigationStack<Route>(new Home());
        var handle2 = new NavigationHandle<Route>(stack2);
        var restored = JsonSerializer.Deserialize(json, StressJsonContext.Default.NavigationStateRoute);
        Assert.NotNull(restored);
        handle2.SetState(restored);

        Assert.IsType<Profile>(handle2.CurrentRoute);
        Assert.Equal(new Profile("Alice"), handle2.CurrentRoute);
        Assert.IsType<Home>(handle2.BackStack[0]);
        Assert.Equal(new Detail(42), handle2.BackStack[1]);
        Assert.IsType<Settings>(handle2.BackStack[2]);
    }

    // ════════════════════════════════════════════════════════════════
    //  7. Deep link resolution stress
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DeepLink_Resolution_Stress_With_Many_Patterns()
    {
        var map = new DeepLinkMap<Route>();

        // Register 50 patterns
        for (int i = 0; i < 50; i++)
        {
            int captured = i;
            map.Map($"/section{i}/{{id:int}}", args => new Detail(args.Get<int>("id") + captured * 1000));
        }

        // Resolve 100 URIs — first 50 should match, next 50 should not
        for (int i = 0; i < 50; i++)
        {
            var result = map.Resolve($"/section{i}/42");
            Assert.True(result.Matched);
            Assert.Equal(new Detail(42 + i * 1000), result.Routes[0]);
        }

        for (int i = 50; i < 100; i++)
        {
            var result = map.Resolve($"/section{i}/42");
            Assert.False(result.Matched);
        }
    }

    [Fact]
    public void DeepLink_Resolution_With_BackStack_Stress()
    {
        var map = new DeepLinkMap<Route>();

        for (int i = 0; i < 20; i++)
        {
            int captured = i;
            map.Map($"/page{i}/{{id:int}}",
                args => new Detail(args.Get<int>("id")),
                () => new Route[] { new Home() });
        }

        for (int i = 0; i < 20; i++)
        {
            var result = map.Resolve($"/page{i}/99");
            Assert.True(result.Matched);
            Assert.Equal(2, result.Routes.Length);
            Assert.IsType<Home>(result.Routes[0]);
            Assert.Equal(new Detail(99), result.Routes[1]);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  8. DeepLinkMap query string support
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DeepLink_Query_String_Extracts_Parameters()
    {
        string? capturedTab = null;
        int capturedPage = 0;

        var map = new DeepLinkMap<Route>()
            .Map("/users/{id:int}", args =>
            {
                capturedTab = args.Query<string>("tab");
                capturedPage = args.Query<int>("page");
                return new Detail(args.Get<int>("id"));
            });

        var result = map.Resolve("/users/42?tab=settings&page=2");

        Assert.True(result.Matched);
        Assert.Equal(new Detail(42), result.Routes[0]);
        Assert.Equal("settings", capturedTab);
        Assert.Equal(2, capturedPage);
    }

    [Fact]
    public void DeepLink_Query_String_Returns_Default_For_Missing_Params()
    {
        string? capturedTab = null;
        int capturedPage = -1;

        var map = new DeepLinkMap<Route>()
            .Map("/users/{id:int}", args =>
            {
                capturedTab = args.Query<string>("tab");
                capturedPage = args.Query<int>("page", -1);
                return new Detail(args.Get<int>("id"));
            });

        var result = map.Resolve("/users/42");

        Assert.True(result.Matched);
        Assert.Null(capturedTab);
        Assert.Equal(-1, capturedPage);
    }

    [Fact]
    public void DeepLink_Query_String_With_Uri_Object()
    {
        string? capturedSort = null;

        var map = new DeepLinkMap<Route>()
            .Map("/users/{id:int}", args =>
            {
                capturedSort = args.Query<string>("sort");
                return new Detail(args.Get<int>("id"));
            });

        var result = map.Resolve(new Uri("myapp://host/users/42?sort=name"));

        Assert.True(result.Matched);
        Assert.Equal("name", capturedSort);
    }

    // ════════════════════════════════════════════════════════════════
    //  9. DeepLinkMap optional parameters
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DeepLink_Optional_Parameter_Present()
    {
        var map = new DeepLinkMap<Route>()
            .Map("/users/{id:int?}", args =>
                new Detail(args.GetOrDefault<int>("id", 0)));

        var result = map.Resolve("/users/42");

        Assert.True(result.Matched);
        Assert.Equal(new Detail(42), result.Routes[0]);
    }

    [Fact]
    public void DeepLink_Optional_Parameter_Absent()
    {
        var map = new DeepLinkMap<Route>()
            .Map("/users/{id:int?}", args =>
                new Detail(args.GetOrDefault<int>("id", 0)));

        var result = map.Resolve("/users");

        Assert.True(result.Matched);
        Assert.Equal(new Detail(0), result.Routes[0]);
    }

    [Fact]
    public void DeepLink_Optional_String_Parameter()
    {
        var map = new DeepLinkMap<Route>()
            .Map("/profile/{name?}", args =>
                new Profile(args.GetOrDefault<string>("name", "anonymous")));

        var withName = map.Resolve("/profile/alice");
        Assert.True(withName.Matched);
        Assert.Equal(new Profile("alice"), withName.Routes[0]);

        var withoutName = map.Resolve("/profile");
        Assert.True(withoutName.Matched);
        Assert.Equal(new Profile("anonymous"), withoutName.Routes[0]);
    }

    // ════════════════════════════════════════════════════════════════
    //  10. DeepLinkMap wildcard routes
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DeepLink_Wildcard_Matches_Nested_Path()
    {
        string? capturedWildcard = null;

        var map = new DeepLinkMap<Route>()
            .Map("/docs/**", args =>
            {
                capturedWildcard = args.GetWildcard();
                return new Settings();
            });

        var result = map.Resolve("/docs/getting-started/installation");

        Assert.True(result.Matched);
        Assert.IsType<Settings>(result.Routes[0]);
        Assert.Equal("getting-started/installation", capturedWildcard);
    }

    [Fact]
    public void DeepLink_Wildcard_Matches_Single_Segment()
    {
        string? capturedWildcard = null;

        var map = new DeepLinkMap<Route>()
            .Map("/docs/**", args =>
            {
                capturedWildcard = args.GetWildcard();
                return new Settings();
            });

        var result = map.Resolve("/docs/overview");

        Assert.True(result.Matched);
        Assert.Equal("overview", capturedWildcard);
    }

    [Fact]
    public void DeepLink_Wildcard_Does_Not_Match_Base_Path_Alone()
    {
        var map = new DeepLinkMap<Route>()
            .Map("/docs/**", _ => new Settings());

        var result = map.Resolve("/docs");

        // The wildcard pattern /docs/** requires at least one segment after /docs/
        Assert.False(result.Matched);
    }

    [Fact]
    public void DeepLink_Wildcard_With_Prefix_Parameters()
    {
        string? capturedWildcard = null;
        int capturedVersion = 0;

        var map = new DeepLinkMap<Route>()
            .Map("/api/v{version:int}/**", args =>
            {
                capturedVersion = args.Get<int>("version");
                capturedWildcard = args.GetWildcard();
                return new Detail(capturedVersion);
            });

        var result = map.Resolve("/api/v2/users/list");

        Assert.True(result.Matched);
        Assert.Equal(2, capturedVersion);
        Assert.Equal("users/list", capturedWildcard);
    }

    // ════════════════════════════════════════════════════════════════
    //  11. NavigatingToContext
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void NavigatingToContext_Exposes_Route_PreviousRoute_Mode()
    {
        var ctx = new NavigatingToContext(new Detail(1), new Home(), NavigationMode.Push);

        Assert.Equal(new Detail(1), ctx.Route);
        Assert.Equal(new Home(), ctx.PreviousRoute);
        Assert.Equal(NavigationMode.Push, ctx.Mode);
        Assert.False(ctx.IsCancelled);
    }

    [Fact]
    public void NavigatingToContext_Cancel_Sets_IsCancelled()
    {
        var ctx = new NavigatingToContext(new Settings(), new Home(), NavigationMode.Push);

        ctx.Cancel();

        Assert.True(ctx.IsCancelled);
    }

    [Fact]
    public void NavigatingToContext_Allows_Null_PreviousRoute()
    {
        var ctx = new NavigatingToContext(new Home(), null, NavigationMode.Reset);

        Assert.Equal(new Home(), ctx.Route);
        Assert.Null(ctx.PreviousRoute);
        Assert.Equal(NavigationMode.Reset, ctx.Mode);
    }

    [Fact]
    public void NavigatingToContext_All_Modes()
    {
        foreach (var mode in Enum.GetValues<NavigationMode>())
        {
            var ctx = new NavigatingToContext(new Home(), new Settings(), mode);
            Assert.Equal(mode, ctx.Mode);
            Assert.False(ctx.IsCancelled);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  12. NavigationCache thread safety
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Cache_Thread_Safety_Under_Heavy_Contention()
    {
        var cache = new NavigationCache(5);
        int totalOps = 0;

        var tasks = Enumerable.Range(0, 8).Select(threadId => Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                var route = new Detail(threadId * 100 + i);
                cache.Add(route, MakePage());
                cache.TryGet(route, out _);
                cache.Remove(route);
                Interlocked.Add(ref totalOps, 3);
            }
        })).ToArray();

        var ex = await Record.ExceptionAsync(() => Task.WhenAll(tasks));

        Assert.Null(ex);
        Assert.True(totalOps == 2400); // 8 threads * 100 iterations * 3 ops
        Assert.True(cache.Count >= 0);
        Assert.True(cache.Count <= 5);
    }

    [Fact]
    public async Task Cache_Concurrent_Clear_Does_Not_Throw()
    {
        var cache = new NavigationCache(10);

        var tasks = new List<Task>();

        // Continuously add while clearing
        tasks.Add(Task.Run(() =>
        {
            for (int i = 0; i < 200; i++)
                cache.Add(new Detail(i), MakePage());
        }));

        tasks.Add(Task.Run(() =>
        {
            for (int i = 0; i < 20; i++)
                cache.Clear();
        }));

        tasks.Add(Task.Run(() =>
        {
            for (int i = 0; i < 200; i++)
                cache.TryGet(new Detail(i), out _);
        }));

        var ex = await Record.ExceptionAsync(() => Task.WhenAll(tasks));
        Assert.Null(ex);
    }
}
