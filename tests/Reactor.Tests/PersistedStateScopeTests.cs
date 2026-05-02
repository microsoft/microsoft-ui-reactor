using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Internal;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 033 §2 — LRU cache primitive, application/window scopes, and the
/// <c>UsePersisted</c> hook. Tests focus on (a) the LRU primitive correctness
/// and (b) the public scope surfaces' bounded-memory + key-validation
/// behavior. Reconciler-level wiring (per-host scope resolution) is a
/// follow-up; covered here are the foundations.
/// </summary>
public class PersistedStateScopeTests
{
    // ════════════════════════════════════════════════════════════════
    //  LruCache primitive (Internal namespace)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Lru_Capacity_Is_Strictly_Enforced()
    {
        var lru = new LruCache<string, int>(3);
        lru.Set("a", 1);
        lru.Set("b", 2);
        lru.Set("c", 3);
        lru.Set("d", 4); // forces eviction of LRU "a"

        Assert.Equal(3, lru.Count);
        Assert.False(lru.TryGet("a", out _));
        Assert.True(lru.TryGet("d", out var d) && d == 4);
    }

    [Fact]
    public void Lru_Touch_On_Access_Promotes_To_MRU()
    {
        var lru = new LruCache<string, int>(3);
        lru.Set("a", 1);
        lru.Set("b", 2);
        lru.Set("c", 3);
        Assert.True(lru.TryGet("a", out _)); // promote a to MRU
        lru.Set("d", 4); // should evict "b" (now LRU), not "a"

        Assert.True(lru.TryGet("a", out _));
        Assert.False(lru.TryGet("b", out _));
    }

    [Fact]
    public void Lru_Set_Existing_Key_Updates_In_Place_And_Promotes()
    {
        var lru = new LruCache<string, int>(2);
        lru.Set("a", 1);
        lru.Set("b", 2);
        lru.Set("a", 99); // update + promote
        lru.Set("c", 3);  // evict LRU = "b"

        Assert.False(lru.TryGet("b", out _));
        Assert.True(lru.TryGet("a", out var a) && a == 99);
    }

    [Fact]
    public void Lru_Trim_To_Target_Removes_LRU_First()
    {
        var lru = new LruCache<string, int>(10);
        for (int i = 0; i < 8; i++) lru.Set($"k{i}", i);
        lru.TryGet("k0", out _); // promote k0
        var removed = lru.Trim(targetCount: 3);

        Assert.Equal(5, removed);
        Assert.Equal(3, lru.Count);
        Assert.True(lru.TryGet("k0", out _));   // promoted = retained
        Assert.True(lru.TryGet("k7", out _));   // recent = retained
        Assert.False(lru.TryGet("k1", out _));  // oldest unread = evicted
    }

    [Fact]
    public void Lru_Capacity_Validation_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(-1));
    }

    [Fact]
    public async Task Lru_Concurrent_Mutations_Stay_Consistent()
    {
        var lru = new LruCache<int, int>(100);
        var tasks = new List<Task>();
        for (int t = 0; t < 4; t++)
        {
            int tid = t;
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 5_000; i++)
                {
                    lru.Set(tid * 10_000 + i, i);
                    lru.TryGet(tid * 10_000 + (i / 2), out _);
                }
            }));
        }
        await Task.WhenAll(tasks);
        Assert.True(lru.Count <= 100);
        Assert.True(lru.Count > 0);
    }

    // ════════════════════════════════════════════════════════════════
    //  ApplicationPersistedScope public surface
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ApplicationScope_Capacity_Is_Configurable()
    {
        using var scope = new ApplicationPersistedScope(8);
        Assert.Equal(8, scope.Capacity);
    }

    [Fact]
    public void ApplicationScope_Set_TryGet_Roundtrip()
    {
        using var scope = new ApplicationPersistedScope(8);
        scope.Set("k1", 42);
        Assert.True(scope.TryGet<int>("k1", out var v) && v == 42);
    }

    [Fact]
    public void ApplicationScope_TryGet_Returns_False_For_Wrong_Type()
    {
        using var scope = new ApplicationPersistedScope(8);
        scope.Set("k1", "hello");
        Assert.False(scope.TryGet<int>("k1", out _));
    }

    [Fact]
    public void ApplicationScope_Eviction_Plateaus_At_Capacity()
    {
        using var scope = new ApplicationPersistedScope(4);
        for (int i = 0; i < 20; i++) scope.Set($"k{i}", i);
        Assert.Equal(4, scope.Count);
        // The newest 4 keys win.
        Assert.True(scope.TryGet<int>("k19", out _));
        Assert.False(scope.TryGet<int>("k0", out _));
    }

    [Fact]
    public void ApplicationScope_ApplyMemoryPressureTrim_Shrinks_To_25_Percent()
    {
        using var scope = new ApplicationPersistedScope(40);
        for (int i = 0; i < 40; i++) scope.Set($"k{i}", i);
        Assert.Equal(40, scope.Count);

        var removed = scope.ApplyMemoryPressureTrim();
        // 25% of 40 = 10
        Assert.Equal(10, scope.Count);
        Assert.Equal(30, removed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplicationScope_Invalid_Keys_Throw(string? key)
    {
        using var scope = new ApplicationPersistedScope(4);
        if (key is null)
            Assert.Throws<ArgumentNullException>(() => scope.Set(key!, 1));
        else
            Assert.Throws<ArgumentException>(() => scope.Set(key, 1));
    }

    [Fact]
    public void ApplicationScope_Oversized_Key_Throws()
    {
        using var scope = new ApplicationPersistedScope(4);
        var longKey = new string('a', 257);
        Assert.Throws<ArgumentException>(() => scope.Set(longKey, 1));
    }

    [Fact]
    public void ApplicationScope_Default_Singleton_Is_Stable_Across_Calls()
    {
        Assert.Same(ApplicationPersistedScope.Default, ApplicationPersistedScope.Default);
        Assert.Equal(ApplicationPersistedScope.DefaultCapacity, ApplicationPersistedScope.Default.Capacity);
    }

    [Fact]
    public void ApplicationScope_Dispose_Clears_State()
    {
        var scope = new ApplicationPersistedScope(4);
        scope.Set("k", 1);
        Assert.Equal(1, scope.Count);
        scope.Dispose();
        Assert.Equal(0, scope.Count);
    }

    [Fact]
    public void ApplicationScope_Disposes_Becomes_Inert()
    {
        // Matches WindowPersistedScope's "becomes inert" behavior: post-dispose
        // mutations no-op and TryGet returns false. Prevents stale state from
        // leaking back into the cache via a lingering reference.
        var scope = new ApplicationPersistedScope(4);
        scope.Set("k", 42);
        scope.Dispose();
        Assert.Equal(0, scope.Count);
        scope.Set("k", 99);
        Assert.False(scope.TryGet<int>("k", out _));
        scope.Remove("k"); // also no-op (would otherwise hit the LRU after dispose)
        // ApplyMemoryPressureTrim is also safe on a disposed scope: the
        // underlying _cache.Trim handles a count <= target as a no-op.
        var trimmed = scope.ApplyMemoryPressureTrim();
        Assert.Equal(0, trimmed);
    }

    // ════════════════════════════════════════════════════════════════
    //  WindowPersistedScope public surface
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void WindowScope_Default_Capacity_Is_1024()
    {
        using var scope = new WindowPersistedScope();
        Assert.Equal(1024, scope.Capacity);
    }

    [Fact]
    public void WindowScope_Disposes_Clears_And_Becomes_Inert()
    {
        var scope = new WindowPersistedScope(4);
        scope.Set("k", 42);
        scope.Dispose();
        Assert.Equal(0, scope.Count);
        // After dispose, Set is a no-op and TryGet returns false.
        scope.Set("k", 99);
        Assert.False(scope.TryGet<int>("k", out _));
    }

    [Fact]
    public void WindowScope_Eviction_Plateaus_At_Capacity()
    {
        using var scope = new WindowPersistedScope(4);
        for (int i = 0; i < 20; i++) scope.Set($"k{i}", i);
        Assert.Equal(4, scope.Count);
    }

    [Fact]
    public void WindowScope_Distinct_Instances_Do_Not_Share_State()
    {
        using var a = new WindowPersistedScope(8);
        using var b = new WindowPersistedScope(8);
        a.Set("shared-key", 1);
        Assert.False(b.TryGet<int>("shared-key", out _));
    }

    // ════════════════════════════════════════════════════════════════
    //  PersistedScope enum + UsePersisted overload surface
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void PersistedScope_Enum_Has_Window_And_Application_Members()
    {
        Assert.Equal(0, (int)PersistedScope.Window);
        Assert.Equal(1, (int)PersistedScope.Application);
    }

    [Fact]
    public void UsePersisted_ThreeArg_Form_Compiles_And_Runs()
    {
        // Surface check: the new overload can be called and stores the
        // initial value on first read. (Cross-render persistence is
        // covered by the existing PersistedStateTests.)
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var (v, _) = ctx.UsePersisted<int>(key: "phase7-test-key-" + Guid.NewGuid(), initialValue: 7, scope: PersistedScope.Window);
        Assert.Equal(7, v);
    }
}
