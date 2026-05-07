using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Phase 1 tests: Hook type correctness and effect cleanup ordering.
/// All unit tests exercising RenderContext directly — no reconciler, no WinUI controls.
/// </summary>
public class HookStateRefactorTests
{
    // ════════════════════════════════════════════════════════════════
    //  Helper: get the internal hook state type name via reflection
    // ════════════════════════════════════════════════════════════════

    private static string GetHookTypeName(RenderContext ctx, int index)
    {
        var hooksField = typeof(RenderContext).GetField("_hooks",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance)!;
        var hooks = (global::System.Collections.IList)hooksField.GetValue(ctx)!;
        return hooks[index]!.GetType().Name;
    }

    private static object GetHookState(RenderContext ctx, int index)
    {
        var hooksField = typeof(RenderContext).GetField("_hooks",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance)!;
        var hooks = (global::System.Collections.IList)hooksField.GetValue(ctx)!;
        return hooks[index]!;
    }

    // ════════════════════════════════════════════════════════════════
    //  UseState<T> type correctness
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseState_Int_Uses_ValueHookState_Int_No_Boxing()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        var (value, _) = ctx.UseState(42);

        Assert.Equal(42, value);
        // Verify internal type is ValueHookState`1[Int32] — not boxed object
        var typeName = GetHookTypeName(ctx, 0);
        Assert.StartsWith("ValueHookState", typeName);
        Assert.Contains("Int32", GetHookState(ctx, 0).GetType().ToString());
    }

    [Fact]
    public void UseState_String_Works_With_Generic_HookState()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        var (value, set) = ctx.UseState("hello");
        Assert.Equal("hello", value);

        // Second render after setter
        set("world");
        ctx.BeginRender(() => { });
        var (value2, _) = ctx.UseState("hello"); // initial ignored on re-render
        Assert.Equal("world", value2);
    }

    [Fact]
    public void UseState_Bool_Setter_Correctly_Compares_And_Triggers_Rerender()
    {
        var ctx = new RenderContext();
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        var (value, set) = ctx.UseState(false);
        Assert.False(value);

        // Setting to same value should NOT trigger re-render
        set(false);
        Assert.Equal(0, rerenderCount);

        // Setting to different value should trigger re-render
        set(true);
        Assert.Equal(1, rerenderCount);
    }

    // ════════════════════════════════════════════════════════════════
    //  UseReducer<T> type correctness
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseReducer_Int_Functional_Updater_Works_With_Typed_HookState()
    {
        var ctx = new RenderContext();
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        var (value, update) = ctx.UseReducer(10);
        Assert.Equal(10, value);

        // Apply functional update
        update(prev => prev + 5);
        Assert.Equal(1, rerenderCount);

        // Re-render and verify updated value
        ctx.BeginRender(() => rerenderCount++);
        var (value2, _) = ctx.UseReducer(10);
        Assert.Equal(15, value2);

        // Verify internal type
        Assert.Contains("Int32", GetHookState(ctx, 0).GetType().ToString());
    }

    [Fact]
    public void UseReducer_TState_TAction_Dispatch_Works_With_Typed_HookState()
    {
        var ctx = new RenderContext();
        int rerenderCount = 0;

        static int reducer(int state, string action) => action switch
        {
            "increment" => state + 1,
            "decrement" => state - 1,
            _ => state
        };

        ctx.BeginRender(() => rerenderCount++);
        var (value, dispatch) = ctx.UseReducer<int, string>(reducer, 0);
        Assert.Equal(0, value);

        dispatch("increment");
        Assert.Equal(1, rerenderCount);

        ctx.BeginRender(() => rerenderCount++);
        var (value2, dispatch2) = ctx.UseReducer<int, string>(reducer, 0);
        Assert.Equal(1, value2);

        dispatch2("increment");
        dispatch2("increment");

        ctx.BeginRender(() => { });
        var (value3, _) = ctx.UseReducer<int, string>(reducer, 0);
        Assert.Equal(3, value3);

        // Verify internal type
        Assert.Contains("Int32", GetHookState(ctx, 0).GetType().ToString());
    }

    // ════════════════════════════════════════════════════════════════
    //  UseMemo<T> type correctness
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseMemo_Int_Returns_Cached_Value_When_Deps_Unchanged()
    {
        var ctx = new RenderContext();
        int computeCount = 0;

        ctx.BeginRender(() => { });
        var val1 = ctx.UseMemo(() => { computeCount++; return 42; }, "dep1");
        Assert.Equal(42, val1);
        Assert.Equal(1, computeCount);

        // Re-render with same deps — should NOT recompute
        ctx.BeginRender(() => { });
        var val2 = ctx.UseMemo(() => { computeCount++; return 99; }, "dep1");
        Assert.Equal(42, val2); // cached value
        Assert.Equal(1, computeCount);
    }

    [Fact]
    public void UseMemo_Int_Recomputes_When_Deps_Change()
    {
        var ctx = new RenderContext();
        int computeCount = 0;

        ctx.BeginRender(() => { });
        var val1 = ctx.UseMemo(() => { computeCount++; return 42; }, "dep1");
        Assert.Equal(42, val1);
        Assert.Equal(1, computeCount);

        // Re-render with different deps — should recompute
        ctx.BeginRender(() => { });
        var val2 = ctx.UseMemo(() => { computeCount++; return 99; }, "dep2");
        Assert.Equal(99, val2);
        Assert.Equal(2, computeCount);
    }

    [Fact]
    public void UseCallback_Returns_Stable_Reference_When_Deps_Unchanged()
    {
        var ctx = new RenderContext();

        ctx.BeginRender(() => { });
        var cb1 = ctx.UseCallback(() => { }, "dep1");

        ctx.BeginRender(() => { });
        var cb2 = ctx.UseCallback(() => { }, "dep1");

        Assert.Same(cb1, cb2); // same delegate reference
    }

    // ════════════════════════════════════════════════════════════════
    //  UseRef<T> persistence
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseRef_Int_Persists_Across_Renders()
    {
        var ctx = new RenderContext();

        ctx.BeginRender(() => { });
        var ref1 = ctx.UseRef(0);
        Assert.Equal(0, ref1.Current);

        ref1.Current = 42;

        // Re-render — same Ref object, mutated value
        ctx.BeginRender(() => { });
        var ref2 = ctx.UseRef(0);
        Assert.Same(ref1, ref2);
        Assert.Equal(42, ref2.Current);
    }

    // ════════════════════════════════════════════════════════════════
    //  Hook order violation
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Hook_Order_Violation_Throws_Descriptive_Error_With_New_Type_Names()
    {
        var ctx = new RenderContext();

        // First render: UseState then UseEffect
        ctx.BeginRender(() => { });
        ctx.UseState(0);
        ctx.UseEffect(() => { }, "dep");
        ctx.FlushEffects();

        // Second render: try to call UseEffect where UseState was — should throw
        ctx.BeginRender(() => { });
        var ex = Assert.Throws<HookOrderException>(() => ctx.UseEffect(() => { }, "dep"));
        Assert.Contains("ValueHookState", ex.Message);
    }

    [Fact]
    public void Hook_Order_Violation_UseState_At_Effect_Position_Throws()
    {
        var ctx = new RenderContext();

        // First render: UseEffect then UseState
        ctx.BeginRender(() => { });
        ctx.UseEffect(() => { }, "dep");
        ctx.UseState(0);
        ctx.FlushEffects();

        // Second render: UseState where UseEffect was — should throw
        ctx.BeginRender(() => { });
        var ex = Assert.Throws<HookOrderException>(() => ctx.UseState(0));
        Assert.Contains("EffectHookState", ex.Message);
        Assert.Contains("ValueHookState", ex.Message);
    }

    // ════════════════════════════════════════════════════════════════
    //  Hot Reload recovery: ResetForHotReload clears state + cleanups
    //  so a render after a hook-order break can succeed
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ResetForHotReload_RunsCleanups_And_ClearsHooks_So_Next_Render_Sees_Fresh_Sequence()
    {
        var ctx = new RenderContext();
        bool cleanupRan = false;

        // Render 1 (pre-edit): UseState + UseEffect with cleanup.
        ctx.BeginRender(() => { });
        ctx.UseState(42);
        ctx.UseEffect(() => () => cleanupRan = true, "dep");
        ctx.FlushEffects();

        // Render 2 (post-edit): the developer reordered hooks. Calling
        // UseEffect at index 0 (where UseState lived) throws.
        ctx.BeginRender(() => { });
        Assert.Throws<HookOrderException>(() => ctx.UseEffect(() => { }, "dep"));

        // Hot reload recovery: cleanups run, hook list reset.
        ctx.ResetForHotReload();
        Assert.True(cleanupRan, "Effect cleanup should run during ResetForHotReload");

        // Render 3 (recovery): the new hook order is accepted as if first
        // mount — UseEffect-then-UseState now works because the hook list
        // starts empty. State is lost (the 42 from render 1), which is the
        // documented trade-off.
        ctx.BeginRender(() => { });
        ctx.UseEffect(() => { }, "dep");
        var (value, _) = ctx.UseState(0);
        Assert.Equal(0, value);
    }

    [Fact]
    public void ResetForHotReload_Allows_Different_Hook_Type_At_Same_Index()
    {
        var ctx = new RenderContext();

        // Render 1: UseState<int>.
        ctx.BeginRender(() => { });
        ctx.UseState(7);

        // Render 2: developer changed UseState<int> to UseState<string>.
        // Same hook *kind* but different generic — different ValueHookState<T>.
        ctx.BeginRender(() => { });
        Assert.Throws<HookOrderException>(() => ctx.UseState("hello"));

        // After recovery the new shape works.
        ctx.ResetForHotReload();
        ctx.BeginRender(() => { });
        var (s, _) = ctx.UseState("hello");
        Assert.Equal("hello", s);
    }

    // ════════════════════════════════════════════════════════════════
    //  Effect cleanup ordering (post-render)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Effect_Cleanup_Runs_After_Render_Commits_Not_During_UseEffect_Call()
    {
        var ctx = new RenderContext();
        var events = new List<string>();

        // First render: install effect with cleanup
        ctx.BeginRender(() => { });
        ctx.UseEffect(() =>
        {
            events.Add("effect1");
            return () => events.Add("cleanup1");
        }, "dep1");
        ctx.FlushEffects();

        Assert.Equal(new[] { "effect1" }, events);
        events.Clear();

        // Second render: deps change — cleanup should NOT run during UseEffect
        ctx.BeginRender(() => { });
        ctx.UseEffect(() =>
        {
            events.Add("effect2");
            return () => events.Add("cleanup2");
        }, "dep2");

        // At this point, cleanup should NOT have run yet
        Assert.Empty(events);

        // FlushEffects should run cleanup THEN new effect
        ctx.FlushEffects();
        Assert.Equal(new[] { "cleanup1", "effect2" }, events);
    }

    [Fact]
    public void Effect_Cleanup_Runs_Before_New_Effect_In_Same_FlushEffects()
    {
        var ctx = new RenderContext();
        var events = new List<string>();

        // First render
        ctx.BeginRender(() => { });
        ctx.UseEffect(() =>
        {
            events.Add("effectA");
            return () => events.Add("cleanupA");
        }, "dep1");
        ctx.FlushEffects();
        events.Clear();

        // Second render: change deps
        ctx.BeginRender(() => { });
        ctx.UseEffect(() =>
        {
            events.Add("effectA_v2");
            return () => events.Add("cleanupA_v2");
        }, "dep2");

        ctx.FlushEffects();

        // Cleanup must come before the new effect
        Assert.Equal(new[] { "cleanupA", "effectA_v2" }, events);
    }

    [Fact]
    public void Multiple_Effects_Pending_Cleanups_All_Run_Before_Any_New_Effects()
    {
        var ctx = new RenderContext();
        var events = new List<string>();

        // First render: two effects
        ctx.BeginRender(() => { });
        ctx.UseEffect(() =>
        {
            events.Add("effect1");
            return () => events.Add("cleanup1");
        }, "dep1");
        ctx.UseEffect(() =>
        {
            events.Add("effect2");
            return () => events.Add("cleanup2");
        }, "depA");
        ctx.FlushEffects();
        events.Clear();

        // Second render: both effects change deps
        ctx.BeginRender(() => { });
        ctx.UseEffect(() =>
        {
            events.Add("effect1_v2");
            return () => events.Add("cleanup1_v2");
        }, "dep2");
        ctx.UseEffect(() =>
        {
            events.Add("effect2_v2");
            return () => events.Add("cleanup2_v2");
        }, "depB");

        ctx.FlushEffects();

        // All cleanups first, then all new effects
        Assert.Equal(new[] { "cleanup1", "cleanup2", "effect1_v2", "effect2_v2" }, events);
    }

    [Fact]
    public void Unmount_Cleanup_RunCleanups_Runs_Immediately_Not_Deferred()
    {
        var ctx = new RenderContext();
        var events = new List<string>();

        ctx.BeginRender(() => { });
        ctx.UseEffect(() =>
        {
            events.Add("effect");
            return () => events.Add("cleanup");
        }, "dep");
        ctx.FlushEffects();
        events.Clear();

        // RunCleanups (unmount) should run cleanup immediately
        ctx.RunCleanups();
        Assert.Equal(new[] { "cleanup" }, events);
    }

    [Fact]
    public void Effect_Without_Cleanup_No_PendingCleanup_On_Deps_Change()
    {
        // UseEffect(Action, deps) — no cleanup function
        var ctx = new RenderContext();
        var events = new List<string>();

        ctx.BeginRender(() => { });
        ctx.UseEffect(() => events.Add("effect1"), "dep1");
        ctx.FlushEffects();
        events.Clear();

        // Change deps — no cleanup to run
        ctx.BeginRender(() => { });
        ctx.UseEffect(() => events.Add("effect2"), "dep2");
        ctx.FlushEffects();

        Assert.Equal(new[] { "effect2" }, events);
    }

    // ════════════════════════════════════════════════════════════════
    //  UseMemo type verification
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseMemo_Uses_Generic_MemoHookState()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        ctx.UseMemo(() => 42, "dep");

        var typeName = GetHookTypeName(ctx, 0);
        Assert.StartsWith("MemoHookState", typeName);
        Assert.Contains("Int32", GetHookState(ctx, 0).GetType().ToString());
    }

    // ════════════════════════════════════════════════════════════════
    //  UseRef type verification
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseRef_Uses_ValueHookState_Ref()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        ctx.UseRef(0);

        var hookType = GetHookState(ctx, 0).GetType().ToString();
        Assert.Contains("ValueHookState", hookType);
        Assert.Contains("Ref", hookType);
    }
}
