using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Xunit;

#pragma warning disable xUnit1031 // The concurrency stress test deliberately blocks (Task.WaitAll).

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Guards the hook-bookkeeping perf work (cache hook delegates + drop per-render
/// hook allocations). These are behavioral proxies for "we stopped allocating on
/// the steady-state path": a cached delegate is observable as reference identity
/// across renders, and a fully-reused memo-cells array is observable as the same
/// array instance. The thread-safe path (where the lock IS needed) must keep
/// working, and deps short-circuits must still skip exactly as before.
/// </summary>
public class HookAllocationPerfTests
{
    private static RenderContext NewCtx()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        return ctx;
    }

    private static void Rerender(RenderContext ctx) => ctx.BeginRender(() => { });

    private record DivElement(string Content) : Element;

    private static Element MakeCell(int v) => new DivElement($"v={v}");

    // ════════════════════════════════════════════════════════════════
    //  #43/#44/#46/#53 — cached delegate identity across renders
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseState_Setter_Is_Same_Instance_Across_Renders()
    {
        var ctx = NewCtx();
        var (_, set1) = ctx.UseState(0);
        Rerender(ctx);
        var (_, set2) = ctx.UseState(0);
        Rerender(ctx);
        var (_, set3) = ctx.UseState(0);

        Assert.Same(set1, set2);
        Assert.Same(set2, set3);
    }

    [Fact]
    public void UseState_Cached_Setter_Still_Updates_And_Rerenders()
    {
        int rerenders = 0;
        var ctx = new RenderContext();
        ctx.BeginRender(() => rerenders++);
        var (v1, set) = ctx.UseState(1);
        Assert.Equal(1, v1);

        set(42);
        Assert.Equal(1, rerenders);

        ctx.BeginRender(() => rerenders++);
        var (v2, set2) = ctx.UseState(1);
        Assert.Equal(42, v2);
        Assert.Same(set, set2); // identity survived the state mutation
    }

    [Fact]
    public void UseReducer_Updater_Is_Same_Instance_Across_Renders()
    {
        var ctx = NewCtx();
        var (_, up1) = ctx.UseReducer(0);
        Rerender(ctx);
        var (_, up2) = ctx.UseReducer(0);

        Assert.Same(up1, up2);
    }

    [Fact]
    public void UseReducer_Redux_Dispatch_Is_Same_Instance_Across_Renders()
    {
        static int Reducer(int s, string a) => a == "inc" ? s + 1 : s;
        var ctx = NewCtx();
        var (_, d1) = ctx.UseReducer<int, string>(Reducer, 0);
        Rerender(ctx);
        var (_, d2) = ctx.UseReducer<int, string>(Reducer, 0);

        Assert.Same(d1, d2);
    }

    [Fact]
    public void UseReducer_Redux_Cached_Dispatch_Runs_Latest_Reducer()
    {
        // The dispatch delegate is cached, but each render refreshes the reducer it
        // runs — so a re-render that swaps the reducer takes effect (matches React /
        // the prior per-render-allocated dispatch).
        var ctx = NewCtx();
        var (_, dispatch) = ctx.UseReducer<int, string>((s, a) => s + 1, 0);

        Rerender(ctx);
        var (_, dispatch2) = ctx.UseReducer<int, string>((s, a) => s + 10, 0);
        Assert.Same(dispatch, dispatch2);

        dispatch("go"); // held from render 1, but must run render 2's reducer (+10)

        Rerender(ctx);
        var (value, _) = ctx.UseReducer<int, string>((s, a) => s, 0);
        Assert.Equal(10, value);
    }

    [Fact]
    public void UsePersisted_Setter_Is_Same_Instance_Across_Renders()
    {
        var ctx = NewCtx();
        var (_, set1) = ctx.UsePersisted("k", 0, PersistedScope.Application);
        Rerender(ctx);
        var (_, set2) = ctx.UsePersisted("k", 0, PersistedScope.Application);

        Assert.Same(set1, set2);
    }

    [Fact]
    public void UseCallback_Returns_Same_Instance_When_Deps_Unchanged()
    {
        var ctx = NewCtx();
        Action cb = () => { };
        var r1 = ctx.UseCallback(cb, "dep");
        Rerender(ctx);
        var r2 = ctx.UseCallback(cb, "dep");

        Assert.Same(cb, r1);
        Assert.Same(r1, r2);
    }

    [Fact]
    public void UseCallback_Returns_New_Instance_When_Deps_Change()
    {
        var ctx = NewCtx();
        Action cbA = () => { };
        Action cbB = () => { };
        var r1 = ctx.UseCallback(cbA, "a");
        Rerender(ctx);
        var r2 = ctx.UseCallback(cbB, "b");

        Assert.Same(cbA, r1);
        Assert.Same(cbB, r2);
        Assert.NotSame(r1, r2);
    }

    [Fact]
    public void UseCommand_Wrapped_Execute_Is_Same_Instance_When_Command_Unchanged()
    {
        var ctx = NewCtx();
        Func<Task> asyncAction = () => Task.CompletedTask;
        var cmd = new Command { Label = "Save", ExecuteAsync = asyncAction };

        var r1 = ctx.UseCommand(cmd);
        Rerender(ctx);
        var r2 = ctx.UseCommand(cmd);

        Assert.NotNull(r1.Execute);
        Assert.Same(r1.Execute, r2.Execute);
    }

    // ════════════════════════════════════════════════════════════════
    //  #42 — thread-safe lock retained; default path leaves it null
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void NonThreadSafe_State_Does_Not_Allocate_Lock()
    {
        var ctx = NewCtx();
        ctx.UseState(0); // default threadSafe: false

        Assert.Null(GetHookLock(ctx, 0));
    }

    [Fact]
    public void ThreadSafe_State_Allocates_Lock()
    {
        var ctx = NewCtx();
        ctx.UseState(0, threadSafe: true);

        Assert.NotNull(GetHookLock(ctx, 0));
    }

    [Fact]
    public void ThreadSafe_State_Is_Correct_Under_Concurrent_Writes()
    {
        // If the lock were dropped on the threadSafe path, the increments would race
        // (lost updates) or NRE on `lock(hook.Lock!)`. A correct final total proves
        // the lock is both allocated and doing its job.
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var (_, update) = ctx.UseReducer(0, threadSafe: true);

        const int threads = 8, perThread = 1000;
        var tasks = new Task[threads];
        for (int t = 0; t < threads; t++)
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < perThread; i++)
                    update(prev => prev + 1);
            }, TestContext.Current.CancellationToken);
        Task.WaitAll(tasks, TestContext.Current.CancellationToken);

        ctx.BeginRender(() => { });
        var (value, _) = ctx.UseReducer(0, threadSafe: true);
        Assert.Equal(threads * perThread, value);
    }

    private static object? GetHookLock(RenderContext ctx, int index)
    {
        var hooksField = typeof(RenderContext).GetField("_hooks", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(hooksField);
        var hooks = (IList)hooksField!.GetValue(ctx)!;
        var hook = hooks[index]!;
        var lockField = hook.GetType().GetField("Lock", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(lockField);
        return lockField!.GetValue(hook);
    }

    // ════════════════════════════════════════════════════════════════
    //  #45/#46 — deps short-circuit still skips (params + arity overloads)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseMemo_Params_Skips_Factory_When_Deps_Unchanged()
    {
        // Four deps → the params overload (arity overloads cover 1-3).
        var ctx = NewCtx();
        int calls = 0;
        var v1 = ctx.UseMemo(() => { calls++; return 7; }, "a", 1, "b", 2);
        Rerender(ctx);
        var v2 = ctx.UseMemo(() => { calls++; return 7; }, "a", 1, "b", 2);
        Rerender(ctx);
        var v3 = ctx.UseMemo(() => { calls++; return 99; }, "a", 1, "b", 3); // changed

        Assert.Equal(7, v1);
        Assert.Equal(7, v2);
        Assert.Equal(99, v3);
        Assert.Equal(2, calls); // first render + the changed render
    }

    [Fact]
    public void UseMemo_Arity1_Skips_Factory_When_Dep_Unchanged()
    {
        var ctx = NewCtx();
        int calls = 0;
        var v1 = ctx.UseMemo(() => { calls++; return calls; }, 5);
        Rerender(ctx);
        var v2 = ctx.UseMemo(() => { calls++; return calls; }, 5);
        Rerender(ctx);
        var v3 = ctx.UseMemo(() => { calls++; return calls; }, 6); // changed

        Assert.Equal(1, v1);
        Assert.Equal(1, v2); // reused
        Assert.Equal(2, v3); // recomputed
        Assert.Equal(2, calls);
    }

    [Fact]
    public void UseMemo_Arity2_And_Arity3_Skip_Factory_When_Deps_Unchanged()
    {
        var ctx = NewCtx();
        int calls2 = 0, calls3 = 0;

        for (int r = 0; r < 3; r++)
        {
            ctx.BeginRender(() => { });
            ctx.UseMemo(() => { calls2++; return 0; }, 1, "x");
            ctx.UseMemo(() => { calls3++; return 0; }, 1, "x", 2.5);
        }

        Assert.Equal(1, calls2);
        Assert.Equal(1, calls3);
    }

    [Fact]
    public void UseEffect_Params_Fires_Once_While_Deps_Unchanged()
    {
        var ctx = NewCtx();
        int runs = 0;
        ctx.UseEffect(() => { runs++; }, "a", 1);
        ctx.FlushEffects();
        Assert.Equal(1, runs);

        Rerender(ctx);
        ctx.UseEffect(() => { runs++; }, "a", 1);
        ctx.FlushEffects();
        Assert.Equal(1, runs); // skipped

        Rerender(ctx);
        ctx.UseEffect(() => { runs++; }, "a", 2); // changed
        ctx.FlushEffects();
        Assert.Equal(2, runs);
    }

    [Fact]
    public void UseEffect_Arity1_Fires_Once_While_Dep_Unchanged()
    {
        var ctx = NewCtx();
        int runs = 0;
        ctx.UseEffect(() => { runs++; }, 10);
        ctx.FlushEffects();
        Rerender(ctx);
        ctx.UseEffect(() => { runs++; }, 10);
        ctx.FlushEffects();
        Assert.Equal(1, runs);

        Rerender(ctx);
        ctx.UseEffect(() => { runs++; }, 11);
        ctx.FlushEffects();
        Assert.Equal(2, runs);
    }

    [Fact]
    public void UseEffect_Arity_Empty_Deps_Runs_Once_On_Mount()
    {
        var ctx = NewCtx();
        int runs = 0, cleanups = 0;
        ctx.UseEffect(() => { runs++; return () => cleanups++; }, Array.Empty<object>());
        ctx.FlushEffects();
        Rerender(ctx);
        ctx.UseEffect(() => { runs++; return () => cleanups++; }, Array.Empty<object>());
        ctx.FlushEffects();

        Assert.Equal(1, runs);
        Assert.Equal(0, cleanups);
    }

    // ════════════════════════════════════════════════════════════════
    //  #47 — UseMemoCells full-reuse returns the SAME array (zero alloc)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseMemoCells_FullReuse_Returns_Same_Array_Instance()
    {
        var ctx = NewCtx();
        var items = new[] { 1, 2, 3 };
        int builds = 0;
        var first = ctx.UseMemoCells<int>(items, (item, i) => { builds++; return MakeCell(item); }, "d");
        Rerender(ctx);
        var second = ctx.UseMemoCells<int>(items, (item, i) => { builds++; return MakeCell(item); }, "d");

        Assert.Equal(3, builds);     // nothing rebuilt on render 2
        Assert.Same(first, second);  // #47: whole array reused, no new allocation
    }

    [Fact]
    public void UseMemoCells_Rebuilds_New_Array_When_An_Item_Changes()
    {
        var ctx = NewCtx();
        int builds = 0;
        var first = ctx.UseMemoCells<int>(new[] { 1, 2, 3 }, (item, i) => { builds++; return MakeCell(item); }, "d");
        Rerender(ctx);
        var second = ctx.UseMemoCells<int>(new[] { 1, 99, 3 }, (item, i) => { builds++; return MakeCell(item); }, "d");

        Assert.NotSame(first, second);
        Assert.Same(first[0], second[0]); // unchanged cell reused
        Assert.NotSame(first[1], second[1]); // changed cell rebuilt
        Assert.Equal(4, builds); // 3 + 1
    }

    [Fact]
    public void UseMemoCellsByKey_FullReuse_Returns_Same_Array_Instance()
    {
        var ctx = NewCtx();
        var items = new[] { 1, 2, 3 };
        int builds = 0;
        var first = ctx.UseMemoCellsByKey<int, int>(items, x => x, (item, i) => { builds++; return MakeCell(item); }, "d");
        Rerender(ctx);
        var second = ctx.UseMemoCellsByKey<int, int>(items, x => x, (item, i) => { builds++; return MakeCell(item); }, "d");

        Assert.Equal(3, builds);
        Assert.Same(first, second);
    }

    [Fact]
    public void UseMemoCellsByKey_Invokes_KeySelector_Once_Per_Item_Per_Render()
    {
        var ctx = NewCtx();
        int keyCalls = 0;
        var items = new[] { 1, 2, 3 };
        // First render builds the snapshot map; #59 means one key call per item.
        ctx.UseMemoCellsByKey<int, int>(items, x => { keyCalls++; return x; }, (item, i) => MakeCell(item), "d");

        Assert.Equal(3, keyCalls);
    }

    [Fact]
    public void UseMemoCellsByIndex_FullReuse_Returns_Same_Array_Instance()
    {
        var ctx = NewCtx();
        var items = new[] { 1, 2, 3 };
        int builds = 0;
        var first = ctx.UseMemoCellsByIndex<int>(items, Array.Empty<int>(), (item, i) => { builds++; return MakeCell(item); }, "d");
        Rerender(ctx);
        var second = ctx.UseMemoCellsByIndex<int>(items, Array.Empty<int>(), (item, i) => { builds++; return MakeCell(item); }, "d");

        Assert.Equal(3, builds);
        Assert.Same(first, second);
    }

    [Fact]
    public void UseMemoCellsByIndex_Rebuilds_Only_Named_Indices()
    {
        var ctx = NewCtx();
        int builds = 0;
        var first = ctx.UseMemoCellsByIndex<int>(new[] { 1, 2, 3 }, Array.Empty<int>(), (item, i) => { builds++; return MakeCell(item); }, "d");
        Rerender(ctx);
        var second = ctx.UseMemoCellsByIndex<int>(new[] { 1, 2, 3 }, new[] { 1 }, (item, i) => { builds++; return MakeCell(item); }, "d");

        Assert.NotSame(first, second);
        Assert.Same(first[0], second[0]);
        Assert.NotSame(first[1], second[1]);
        Assert.Equal(4, builds);
    }

    // ════════════════════════════════════════════════════════════════
    //  #55 — UseElementFocus caches ref + requestFocus across renders
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseElementFocus_RequestFocus_Is_Same_Instance_Across_Renders()
    {
        var ctx = NewCtx();
        var (ref1, requestFocus1) = ctx.UseElementFocus();
        Rerender(ctx);
        var (ref2, requestFocus2) = ctx.UseElementFocus();

        Assert.Same(ref1, ref2);
        Assert.Same(requestFocus1, requestFocus2);
    }
}
