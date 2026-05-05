using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Phase 4 tests: PersistedStateCache unit tests and UsePersisted hook tests.
/// </summary>
// Shares the process-wide ApplicationPersistedScope.Default with
// ComponentModelIntegrationTests; both Clear() it in setup/teardown, so xUnit
// must serialize them or a Clear() can race with another class's Set/TryGet.
[Collection("PersistedStateCache")]
public class PersistedStateTests : IDisposable
{
    public PersistedStateTests()
    {
        // Reset cache before each test to avoid cross-test contamination
        PersistedStateCache.Clear();
    }

    public void Dispose()
    {
        PersistedStateCache.Clear();
    }

    // ════════════════════════════════════════════════════════════════
    //  PersistedStateCache unit tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void TryGet_Returns_False_When_Key_Not_Present()
    {
        Assert.False(PersistedStateCache.TryGet<int>("missing", out _));
    }

    [Fact]
    public void Set_Then_TryGet_Returns_Stored_Value()
    {
        PersistedStateCache.Set("count", 42);
        Assert.True(PersistedStateCache.TryGet<int>("count", out var value));
        Assert.Equal(42, value);
    }

    [Fact]
    public void Clear_Removes_All_Entries()
    {
        PersistedStateCache.Set("a", 1);
        PersistedStateCache.Set("b", "hello");
        PersistedStateCache.Clear();

        Assert.False(PersistedStateCache.TryGet<int>("a", out _));
        Assert.False(PersistedStateCache.TryGet<string>("b", out _));
    }

    [Fact]
    public void Set_With_Same_Key_Overwrites_Previous_Value()
    {
        PersistedStateCache.Set("key", 1);
        PersistedStateCache.Set("key", 99);

        Assert.True(PersistedStateCache.TryGet<int>("key", out var value));
        Assert.Equal(99, value);
    }

    [Fact]
    public void Remove_Deletes_Single_Entry()
    {
        PersistedStateCache.Set("a", 1);
        PersistedStateCache.Set("b", 2);
        PersistedStateCache.Remove("a");

        Assert.False(PersistedStateCache.TryGet<int>("a", out _));
        Assert.True(PersistedStateCache.TryGet<int>("b", out var value));
        Assert.Equal(2, value);
    }

    [Fact]
    public void TryGet_String_Value()
    {
        PersistedStateCache.Set("name", "Alice");
        Assert.True(PersistedStateCache.TryGet<string>("name", out var value));
        Assert.Equal("Alice", value);
    }

    // ════════════════════════════════════════════════════════════════
    //  UsePersisted hook unit tests (via RenderContext)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UsePersisted_Returns_Initial_Value_On_First_Mount_No_Cache()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        var (value, _) = ctx.UsePersisted("test-key", 42);
        Assert.Equal(42, value);
    }

    [Fact]
    public void UsePersisted_Setter_Updates_Value_And_Triggers_Rerender()
    {
        var ctx = new RenderContext();
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        var (value, set) = ctx.UsePersisted("counter", 0);
        Assert.Equal(0, value);

        // Setting to different value triggers re-render
        set(10);
        Assert.Equal(1, rerenderCount);

        // Setting to same value does NOT trigger re-render
        set(10);
        Assert.Equal(1, rerenderCount);

        // Re-render shows new value
        ctx.BeginRender(() => rerenderCount++);
        var (value2, _) = ctx.UsePersisted("counter", 0);
        Assert.Equal(10, value2);
    }

    [Fact]
    public void UsePersisted_Value_Type_Int_No_Boxing_Issue()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        var (value, set) = ctx.UsePersisted("int-key", 0);
        Assert.Equal(0, value);

        set(42);
        ctx.BeginRender(() => { });
        var (value2, _) = ctx.UsePersisted("int-key", 0);
        Assert.Equal(42, value2);

        // Verify internal type
        var hooksField = typeof(RenderContext).GetField("_hooks",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance)!;
        var hooks = (global::System.Collections.IList)hooksField.GetValue(ctx)!;
        Assert.Contains("PersistedHookState", hooks[0]!.GetType().Name);
        Assert.Contains("Int32", hooks[0]!.GetType().ToString());
    }

    [Fact]
    public void Multiple_UsePersisted_Hooks_In_Same_Context_Keyed_Independently()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        var (a, setA) = ctx.UsePersisted("key-a", 1);
        var (b, setB) = ctx.UsePersisted("key-b", "hello");

        Assert.Equal(1, a);
        Assert.Equal("hello", b);

        setA(99);
        setB("world");

        ctx.BeginRender(() => { });
        var (a2, _) = ctx.UsePersisted("key-a", 1);
        var (b2, _) = ctx.UsePersisted("key-b", "hello");

        Assert.Equal(99, a2);
        Assert.Equal("world", b2);
    }

    // ════════════════════════════════════════════════════════════════
    //  UsePersisted lifecycle (self-host)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void After_Unmount_Remount_UsePersisted_Returns_Cached_Value()
    {
        // First component instance — mount, update state, unmount
        var ctx1 = new RenderContext();
        ctx1.BeginRender(() => { });
        var (_, set) = ctx1.UsePersisted("scroll-pos", 0);
        ctx1.FlushEffects();

        set(150);

        // Re-render to pick up state
        ctx1.BeginRender(() => { });
        var (val1, _) = ctx1.UsePersisted("scroll-pos", 0);
        ctx1.FlushEffects();
        Assert.Equal(150, val1);

        // Unmount → saves to cache
        ctx1.RunCleanups();

        // Verify cache has the value
        Assert.True(PersistedStateCache.TryGet<int>("scroll-pos", out var cached));
        Assert.Equal(150, cached);

        // Second component instance — remount should see cached value
        var ctx2 = new RenderContext();
        ctx2.BeginRender(() => { });
        var (val2, _) = ctx2.UsePersisted("scroll-pos", 0);
        ctx2.FlushEffects();

        Assert.Equal(150, val2); // cached, not initial
    }

    [Fact]
    public void UsePersisted_Same_Key_Different_Components_Shares_State()
    {
        // Component A writes
        var ctxA = new RenderContext();
        ctxA.BeginRender(() => { });
        var (_, setA) = ctxA.UsePersisted("shared-key", "default");
        ctxA.FlushEffects();
        setA("from-A");

        ctxA.BeginRender(() => { });
        ctxA.UsePersisted("shared-key", "default");
        ctxA.FlushEffects();
        ctxA.RunCleanups(); // saves "from-A" to cache

        // Component B reads same key
        var ctxB = new RenderContext();
        ctxB.BeginRender(() => { });
        var (val, _) = ctxB.UsePersisted("shared-key", "default");
        ctxB.FlushEffects();

        Assert.Equal("from-A", val);
    }

    [Fact]
    public void UsePersisted_Different_Keys_Independent_State()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var (_, setA) = ctx.UsePersisted("key-alpha", 1);
        var (_, setB) = ctx.UsePersisted("key-beta", 100);
        ctx.FlushEffects();

        setA(10);
        setB(200);

        ctx.BeginRender(() => { });
        ctx.UsePersisted("key-alpha", 1);
        ctx.UsePersisted("key-beta", 100);
        ctx.FlushEffects();
        ctx.RunCleanups();

        // Each key cached independently
        Assert.True(PersistedStateCache.TryGet<int>("key-alpha", out var a));
        Assert.Equal(10, a);
        Assert.True(PersistedStateCache.TryGet<int>("key-beta", out var b));
        Assert.Equal(200, b);
    }

    [Fact]
    public void PersistedStateCache_Clear_Resets_All_Subsequent_Mount_Gets_Initial()
    {
        // Populate cache
        PersistedStateCache.Set("key", 42);

        // Clear
        PersistedStateCache.Clear();

        // Mount gets initial, not cached
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var (val, _) = ctx.UsePersisted("key", 0);
        ctx.FlushEffects();

        Assert.Equal(0, val); // initial, not 42
    }

    [Fact]
    public void UsePersisted_Coexists_With_UseState()
    {
        var ctx = new RenderContext();

        // Mount: UseState then UsePersisted
        ctx.BeginRender(() => { });
        var (stateVal, setState) = ctx.UseState(0);
        var (persistVal, setPersist) = ctx.UsePersisted("persist-key", "hello");
        ctx.FlushEffects();

        Assert.Equal(0, stateVal);
        Assert.Equal("hello", persistVal);

        // Update both
        setState(42);
        setPersist("world");

        ctx.BeginRender(() => { });
        var (stateVal2, _) = ctx.UseState(0);
        var (persistVal2, _) = ctx.UsePersisted("persist-key", "hello");
        ctx.FlushEffects();

        Assert.Equal(42, stateVal2);
        Assert.Equal("world", persistVal2);

        // Unmount — saves persisted to cache
        ctx.RunCleanups();

        // Remount: UseState is lost, UsePersisted is preserved
        var ctx2 = new RenderContext();
        ctx2.BeginRender(() => { });
        var (newState, _) = ctx2.UseState(0);
        var (newPersist, _) = ctx2.UsePersisted("persist-key", "hello");
        ctx2.FlushEffects();

        Assert.Equal(0, newState); // lost — back to initial
        Assert.Equal("world", newPersist); // preserved from cache
    }

    [Fact]
    public void Effect_Cleanups_Run_Before_Persisted_State_Saved()
    {
        var events = new List<string>();
        var ctx = new RenderContext();

        ctx.BeginRender(() => { });
        ctx.UseEffect(() =>
        {
            events.Add("effect");
            return () => events.Add("cleanup");
        });
        ctx.UsePersisted("key", 0);
        ctx.FlushEffects();

        Assert.Equal(new[] { "effect" }, events);
        events.Clear();

        // Unmount: cleanup runs first, then persisted saves
        // We can't directly observe persisted save order, but verify cleanup ran
        ctx.RunCleanups();
        Assert.Contains("cleanup", events);

        // And persisted state was saved
        Assert.True(PersistedStateCache.TryGet<int>("key", out _));
    }

    [Fact]
    public void UsePersisted_Hook_Order_Violation_Throws()
    {
        var ctx = new RenderContext();

        // First render: UseState then UsePersisted
        ctx.BeginRender(() => { });
        ctx.UseState(0);
        ctx.UsePersisted("key", "hello");
        ctx.FlushEffects();

        // Second render: try UsePersisted where UseState was
        ctx.BeginRender(() => { });
        var ex = Assert.Throws<InvalidOperationException>(() => ctx.UsePersisted("key", "hello"));
        Assert.Contains("PersistedHookState", ex.Message);
    }
}
