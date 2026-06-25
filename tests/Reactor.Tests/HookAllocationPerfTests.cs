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

    [Fact]
    public void Hook_Delegates_Stay_Reference_Stable_Across_Many_Renders_DiffSkipPath()
    {
        // The Element-layer DIFF skip-path (PR #665) skips a cell's update only when
        // its event-handler delegates are REFERENCE-STABLE across renders (presence-only
        // comparison was unsafe). That makes UseState/UseReducer setters and UseCallback
        // (deps unchanged) load-bearing for DIFF, not merely an allocation win: a
        // component passing these as handlers must receive the SAME delegate instance on
        // every steady-state render so the diff can prove the handler is unchanged. This
        // guards that contract across many renders, with fresh reducer/callback lambdas
        // supplied each render (as a real component would).
        var ctx = NewCtx();
        Action handler = () => { };

        var (_, set0) = ctx.UseState(0);
        var (_, update0) = ctx.UseReducer(0);
        var (_, dispatch0) = ctx.UseReducer<int, string>((s, a) => s, 0);
        var cb0 = ctx.UseCallback(handler, "stable-dep");

        for (int i = 0; i < 50; i++)
        {
            Rerender(ctx);
            var (_, set) = ctx.UseState(0);
            var (_, update) = ctx.UseReducer(0);
            var (_, dispatch) = ctx.UseReducer<int, string>((s, a) => s, 0);
            // Pass a FRESH, distinct handler lambda each render (capturing i) — exactly
            // what a real component does. UseCallback must ignore it and return the
            // render-0 instance while the dep is unchanged, proving it truly caches
            // rather than echoing back whatever was passed in.
            int captured = i;
            var cb = ctx.UseCallback(() => { _ = captured; }, "stable-dep");

            Assert.Same(set0, set);
            Assert.Same(update0, update);
            Assert.Same(dispatch0, dispatch);
            Assert.Same(cb0, cb);
        }

        // Precision: when a callback dep actually changes, the skip-path must NOT skip —
        // UseCallback hands back the fresh instance so the diff re-applies the handler.
        // Hook order/count is preserved (all four hooks, in order) so only the dep varies,
        // and a distinct handler instance is supplied so the change is observable.
        Rerender(ctx);
        ctx.UseState(0);
        ctx.UseReducer(0);
        ctx.UseReducer<int, string>((s, a) => s, 0);
        int sentinel = -1;
        Action newHandler = () => { _ = sentinel; };
        var cbChanged = ctx.UseCallback(newHandler, "new-dep");
        Assert.NotSame(cb0, cbChanged);
        Assert.Same(newHandler, cbChanged);
    }

    [Fact]
    public void UseCallback_And_Setter_Satisfy_ReferenceEquals_Contract_DiffSkipPath()
    {
        // Literal ReferenceEquals form of the DIFF skip-path contract (PR #665), the
        // exact predicate the Element-layer skip path evaluates on handler delegates:
        //   deps unchanged  ⇒ ReferenceEquals(prev, next) == true  (cell can be skipped)
        //   deps changed     ⇒ ReferenceEquals(prev, next) == false (cell must re-apply)
        // The UseState setter identity is dep-independent (cached once on the slot), so it
        // is stable regardless.
        var ctx = NewCtx();
        Action handler = () => { };
        var (_, set1) = ctx.UseState(0);
        var cb1 = ctx.UseCallback(handler, "k");

        Rerender(ctx);
        var (_, set2) = ctx.UseState(0);
        var cb2 = ctx.UseCallback(handler, "k"); // deps unchanged
        Assert.True(ReferenceEquals(set1, set2));
        Assert.True(ReferenceEquals(cb1, cb2));

        Rerender(ctx);
        var (_, set3) = ctx.UseState(0);
        int s = 1;
        var cb3 = ctx.UseCallback(() => { _ = s; }, "k2"); // deps changed
        Assert.True(ReferenceEquals(set1, set3));
        Assert.False(ReferenceEquals(cb1, cb3));
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
        // A start barrier maximizes real contention: every worker blocks until all
        // threads are ready, so the increments actually race on the lock.
        using var barrier = new Barrier(threads);
        var tasks = new Task[threads];
        for (int t = 0; t < threads; t++)
            tasks[t] = Task.Run(() =>
            {
                barrier.SignalAndWait(TestContext.Current.CancellationToken);
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
    //  Review (test-coverage) — arity-2/3 cleanup-flavor UseEffect overloads:
    //  the prior cleanup runs exactly once when a dependency changes, and the
    //  effect+cleanup are skipped entirely while every dependency is unchanged.
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseEffect_Arity2_Cleanup_Skips_While_Unchanged_And_Cleans_Up_Once_On_Change()
    {
        var ctx = NewCtx();
        int runs = 0, cleanups = 0;
        ctx.UseEffect(() => { runs++; return () => cleanups++; }, "a", 1);
        ctx.FlushEffects();
        Assert.Equal(1, runs);
        Assert.Equal(0, cleanups);

        Rerender(ctx);
        ctx.UseEffect(() => { runs++; return () => cleanups++; }, "a", 1); // unchanged
        ctx.FlushEffects();
        Assert.Equal(1, runs);     // skipped
        Assert.Equal(0, cleanups); // no cleanup while skipped

        Rerender(ctx);
        ctx.UseEffect(() => { runs++; return () => cleanups++; }, "a", 2); // d2 changed
        ctx.FlushEffects();
        Assert.Equal(2, runs);
        Assert.Equal(1, cleanups); // prior cleanup ran exactly once
    }

    [Fact]
    public void UseEffect_Arity3_Cleanup_Skips_While_Unchanged_And_Cleans_Up_Once_On_Change()
    {
        var ctx = NewCtx();
        int runs = 0, cleanups = 0;
        ctx.UseEffect(() => { runs++; return () => cleanups++; }, "a", 1, true);
        ctx.FlushEffects();
        Assert.Equal(1, runs);
        Assert.Equal(0, cleanups);

        Rerender(ctx);
        ctx.UseEffect(() => { runs++; return () => cleanups++; }, "a", 1, true); // unchanged
        ctx.FlushEffects();
        Assert.Equal(1, runs);
        Assert.Equal(0, cleanups);

        Rerender(ctx);
        ctx.UseEffect(() => { runs++; return () => cleanups++; }, "a", 1, false); // d3 changed
        ctx.FlushEffects();
        Assert.Equal(2, runs);
        Assert.Equal(1, cleanups);
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
    public void UseMemoCellsByKey_FullReuse_Does_Not_Allocate_Per_Render()
    {
        // Guards the steady-state full-reuse fast path against per-render allocation
        // (the key buffer is carried on the hook state and reused). Reference-type
        // keys avoid boxing in the equality scan; Array.Empty deps avoids the params
        // call-site allocation; the delegates are hoisted so no delegate is allocated
        // per call. Only the hook call is bracketed — render passes are excluded.
        var ctx = NewCtx();
        var items = new[] { "a", "b", "c" };
        var deps = Array.Empty<object>();
        Func<string, string> keySel = x => x;
        Func<string, int, Element> build = (item, i) => new DivElement($"v={item}");

        for (int w = 0; w < 3; w++) // prime + JIT the fast-path return
        {
            ctx.UseMemoCellsByKey(items, keySel, build, deps);
            Rerender(ctx);
        }

        long worst = 0;
        for (int i = 0; i < 64; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var children = ctx.UseMemoCellsByKey(items, keySel, build, deps);
            long after = GC.GetAllocatedBytesForCurrentThread();
            worst = Math.Max(worst, after - before);
            Assert.Equal(3, children.Length); // full-reuse path actually taken
            Rerender(ctx);
        }

        Assert.Equal(0, worst);
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

    [Fact]
    public void UseElementFocus_RequestFocus_Invokes_Safely_When_Unmounted()
    {
        // In headless tests no UI dispatcher is registered, so the cached requestFocus
        // takes its synchronous fallback branch and calls FocusManager.Focus directly.
        // With an unmounted ref (no target) Focus is a no-op, so invoking must not throw —
        // this exercises the invoke/fallback path, not just delegate identity.
        var ctx = NewCtx();
        var (_, requestFocus) = ctx.UseElementFocus();
        Assert.Null(Record.Exception(() => requestFocus()));

        Rerender(ctx);
        var (_, requestFocus2) = ctx.UseElementFocus();
        Assert.Same(requestFocus, requestFocus2);             // still the cached delegate
        Assert.Null(Record.Exception(() => requestFocus2())); // still invocable after re-render
    }

    // ════════════════════════════════════════════════════════════════
    //  H1 — a lone object[]-typed dep keeps element-wise (params) semantics
    //  (covariant ref-type arrays must NOT be reference-compared as one dep)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseEffect_Arity1_ObjectArray_Dep_Uses_ElementWise_Comparison()
    {
        // A fresh string[] of equal contents each render must NOT re-run the effect.
        // (If the array were wrapped as one reference-compared dep it would re-run.)
        var ctx = NewCtx();
        int runs = 0;
        ctx.UseEffect(() => { runs++; }, new[] { "a", "b" });
        ctx.FlushEffects();
        Assert.Equal(1, runs);

        Rerender(ctx);
        ctx.UseEffect(() => { runs++; }, new[] { "a", "b" }); // new array, equal contents
        ctx.FlushEffects();
        Assert.Equal(1, runs); // element-wise equal → skipped

        Rerender(ctx);
        ctx.UseEffect(() => { runs++; }, new[] { "a", "c" }); // contents changed
        ctx.FlushEffects();
        Assert.Equal(2, runs);
    }

    // ════════════════════════════════════════════════════════════════
    //  CORR-1 (correctness review) — an arity-1 dep whose STATIC type is NOT an
    //  array (here object) but whose RUNTIME value is object[] must be compared as
    //  ONE value, exactly as the legacy params overload did (it wrapped such a value
    //  as new object[]{ dep } and reference-compared it). Only UseMemo is affected:
    //  its two overloads are both generic, so the arity-1 generic wins overload
    //  resolution (UseEffect/UseCallback bind their non-generic params overload here).
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseMemo_Arity1_ObjectTyped_ArrayValue_Is_Compared_By_Reference_Like_Params()
    {
        var ctx = NewCtx();
        int factoryRuns = 0;
        object dep = new object[] { "a" };          // static type object, runtime object[]
        ctx.UseMemo(() => { factoryRuns++; return factoryRuns; }, dep);
        Assert.Equal(1, factoryRuns);

        Rerender(ctx);
        ctx.UseMemo(() => { factoryRuns++; return factoryRuns; }, dep); // same ref → cached
        Assert.Equal(1, factoryRuns);

        Rerender(ctx);
        object depNewEqual = new object[] { "a" };  // NEW reference, equal contents
        ctx.UseMemo(() => { factoryRuns++; return factoryRuns; }, depNewEqual);
        Assert.Equal(2, factoryRuns); // reference changed → recompute (NOT element-wise)
    }

    // ════════════════════════════════════════════════════════════════
    //  Cast-safety (Copilot review) — a hot-reload edit or dynamic call site can
    //  change a dependency's runtime type at the same hook slot across renders. The
    //  typed comparer must treat a type mismatch as "changed" (re-run) rather than
    //  throwing InvalidCastException while comparing deps.
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Arity1_Deps_Comparer_Treats_Runtime_Type_Change_As_Changed_Without_Throwing()
    {
        var ctx = NewCtx();
        int runs = 0;
        ctx.UseEffect(() => { runs++; }, 42);    // T1=int → stores boxed int at slot 0
        ctx.FlushEffects();
        Assert.Equal(1, runs);

        Rerender(ctx);
        // Same slot, but the dep is now a string. Comparing (string)prev[0] over a
        // boxed int must NOT throw; the mismatch is treated as "changed".
        Exception? ex = Record.Exception(() =>
        {
            ctx.UseEffect(() => { runs++; }, "now-a-string"); // T1=string at the same slot
            ctx.FlushEffects();
        });
        Assert.Null(ex);
        Assert.Equal(2, runs); // type changed → effect re-ran (no crash)
    }

    // ════════════════════════════════════════════════════════════════
    //  Nullable-dep semantics (Copilot review — verified FALSE POSITIVE). It was
    //  suggested that DepEquals<T>'s `stored is T t` always fails for T = Nullable<U>
    //  (because boxing a non-null nullable stores a boxed U), making an unchanged
    //  nullable dep re-run every render. That is NOT how the generic `is T` pattern
    //  behaves: the CLR special-cases nullable type-tests, so a boxed U satisfies
    //  `o is U?` and binds the nullable-wrapped value (the symmetric counterpart of
    //  `(U?)(object)u` unboxing). This locks in that a nullable value-type dep is
    //  SKIPPED while unchanged (incl. null↔null) and re-runs on every change
    //  (incl. value↔null), matching the legacy `(T)prev[0]` semantics.
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseEffect_Arity1_Nullable_ValueType_Dep_Skips_While_Unchanged_And_Reruns_On_Change()
    {
        var ctx = NewCtx();
        int runs = 0;

        ctx.UseEffect(() => { runs++; }, (int?)5);    // T1=int? → boxed int stored at slot 0
        ctx.FlushEffects();
        Assert.Equal(1, runs);

        Rerender(ctx);
        ctx.UseEffect(() => { runs++; }, (int?)5);    // unchanged → skipped
        ctx.FlushEffects();
        Assert.Equal(1, runs);

        Rerender(ctx);
        ctx.UseEffect(() => { runs++; }, (int?)6);    // value changed → re-run
        ctx.FlushEffects();
        Assert.Equal(2, runs);

        Rerender(ctx);
        ctx.UseEffect(() => { runs++; }, (int?)null); // value → null → re-run
        ctx.FlushEffects();
        Assert.Equal(3, runs);

        Rerender(ctx);
        ctx.UseEffect(() => { runs++; }, (int?)null); // null unchanged → skipped
        ctx.FlushEffects();
        Assert.Equal(3, runs);

        Rerender(ctx);
        ctx.UseEffect(() => { runs++; }, (int?)7);    // null → value → re-run
        ctx.FlushEffects();
        Assert.Equal(4, runs);
    }

    // ════════════════════════════════════════════════════════════════
    //  Aliasing guard (Copilot review) — a caller that reuses AND mutates the
    //  SAME deps array instance across renders must still observe the change.
    //  Stored deps are snapshotted (SnapshotDeps) so prev/next can never alias
    //  the caller's live array.
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseEffect_Reused_Mutated_Deps_Array_Still_Detects_Change()
    {
        var ctx = NewCtx();
        var deps = new object[] { "a" };
        int runs = 0;

        ctx.UseEffect(() => { runs++; }, deps);
        ctx.FlushEffects();
        Assert.Equal(1, runs);

        deps[0] = "b"; // mutate in place, reuse the SAME instance
        Rerender(ctx);
        ctx.UseEffect(() => { runs++; }, deps);
        ctx.FlushEffects();
        Assert.Equal(2, runs); // change detected despite array aliasing

        Rerender(ctx);
        ctx.UseEffect(() => { runs++; }, deps); // same instance, same contents
        ctx.FlushEffects();
        Assert.Equal(2, runs); // unchanged → skipped
    }

    [Fact]
    public void UseMemo_Reused_Mutated_Deps_Array_Still_Recomputes()
    {
        var ctx = NewCtx();
        var deps = new object[] { 1 };
        int factoryCalls = 0;

        int Render() => ctx.UseMemo(() => { factoryCalls++; return (int)deps[0]; }, deps);

        Assert.Equal(1, Render());
        Assert.Equal(1, factoryCalls);

        deps[0] = 2; // mutate in place
        Rerender(ctx);
        Assert.Equal(2, Render());
        Assert.Equal(2, factoryCalls); // recomputed despite aliasing

        Rerender(ctx);
        Assert.Equal(2, Render()); // unchanged contents
        Assert.Equal(2, factoryCalls); // cached, not recomputed
    }

    [Fact]
    public void UseCallback_Reused_Mutated_Deps_Array_Returns_Updated_Callback()
    {
        var ctx = NewCtx();
        var deps = new object[] { 1 };
        // Capture a distinct value so each callback is a unique delegate instance
        // (a non-capturing lambda would be compiler-cached and defeat NotSame).
        static Action Cb(int tag) => () => { _ = tag; };

        Action cb1 = ctx.UseCallback(Cb(1), deps);

        deps[0] = 2; // mutate the same instance in place
        Rerender(ctx);
        Action cb2 = ctx.UseCallback(Cb(2), deps);
        Assert.NotSame(cb1, cb2); // deps change detected despite aliasing → new callback

        Rerender(ctx);
        Action cb3 = ctx.UseCallback(Cb(3), deps); // unchanged contents
        Assert.Same(cb2, cb3); // cached
    }

    [Fact]
    public void UseMemo_Arity1_ObjectArray_Dep_Uses_ElementWise_Comparison()
    {
        var ctx = NewCtx();
        int calls = 0;
        var v1 = ctx.UseMemo(() => { calls++; return calls; }, new[] { "a", "b" });
        Rerender(ctx);
        var v2 = ctx.UseMemo(() => { calls++; return calls; }, new[] { "a", "b" }); // equal contents
        Rerender(ctx);
        var v3 = ctx.UseMemo(() => { calls++; return calls; }, new[] { "a", "c" }); // changed

        Assert.Equal(1, v1);
        Assert.Equal(1, v2); // reused
        Assert.Equal(2, v3); // recomputed
        Assert.Equal(2, calls);
    }

    [Fact]
    public void UseCallback_Arity1_ObjectArray_Dep_Uses_ElementWise_Comparison()
    {
        var ctx = NewCtx();
        Action cb1 = () => { };
        Action cb2 = () => { };
        Action cb3 = () => { };
        var r1 = ctx.UseCallback(cb1, new[] { "a", "b" });
        Rerender(ctx);
        var r2 = ctx.UseCallback(cb2, new[] { "a", "b" }); // equal contents → retain cb1
        Rerender(ctx);
        var r3 = ctx.UseCallback(cb3, new[] { "a", "c" }); // changed → adopt cb3

        Assert.Same(cb1, r1);
        Assert.Same(cb1, r2); // deps equal element-wise → cached callback retained
        Assert.Same(cb3, r3); // deps changed → new callback
    }

    // ════════════════════════════════════════════════════════════════
    //  #45/#46 — remaining deps-overload matrix (arity 2/3 + cleanup flavor)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseEffect_Arity2_And_Arity3_Fire_Once_While_Deps_Unchanged()
    {
        var ctx = NewCtx();
        int runs2 = 0, runs3 = 0;
        for (int r = 0; r < 3; r++)
        {
            ctx.BeginRender(() => { });
            ctx.UseEffect(() => { runs2++; }, 1, "x");
            ctx.UseEffect(() => { runs3++; }, 1, "x", 2.5);
            ctx.FlushEffects();
        }

        Assert.Equal(1, runs2);
        Assert.Equal(1, runs3);
    }

    [Fact]
    public void UseEffect_Cleanup_Arity1_Reruns_And_Cleans_Up_On_Dep_Change()
    {
        var ctx = NewCtx();
        int runs = 0, cleanups = 0;
        ctx.UseEffect(() => { runs++; return () => cleanups++; }, 1);
        ctx.FlushEffects();
        Assert.Equal(1, runs);
        Assert.Equal(0, cleanups);

        Rerender(ctx);
        ctx.UseEffect(() => { runs++; return () => cleanups++; }, 1); // unchanged
        ctx.FlushEffects();
        Assert.Equal(1, runs);
        Assert.Equal(0, cleanups);

        Rerender(ctx);
        ctx.UseEffect(() => { runs++; return () => cleanups++; }, 2); // changed → cleanup + rerun
        ctx.FlushEffects();
        Assert.Equal(2, runs);
        Assert.Equal(1, cleanups);
    }

    [Fact]
    public void UseCallback_Arity2_And_Arity3_Recompute_On_Change()
    {
        var ctx = NewCtx();
        Action a1 = () => { };
        Action a2 = () => { };
        ctx.UseCallback(a1, 1, "x");
        ctx.UseCallback(a1, 1, "x", 2.5);
        Rerender(ctx);
        var c2 = ctx.UseCallback(a2, 1, "x");      // unchanged deps → keep a1
        var c3 = ctx.UseCallback(a2, 1, "x", 9.9); // changed dep → adopt a2

        Assert.Same(a1, c2);
        Assert.Same(a2, c3);
    }

    // ════════════════════════════════════════════════════════════════
    //  #56 — UseDisposableEffect: dispose exactly once on unmount
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseDisposableEffect_Disposes_Resource_Once_On_Unmount()
    {
        var ctx = NewCtx();
        var probe = new DisposeProbe();
        ctx.UseDisposableEffect(probe);
        ctx.FlushEffects();

        for (int i = 0; i < 3; i++) // re-render: mount-once effect must not re-fire
        {
            Rerender(ctx);
            ctx.UseDisposableEffect(probe);
            ctx.FlushEffects();
        }
        Assert.Equal(0, probe.Disposals); // still mounted

        ctx.RunCleanups();
        Assert.Equal(1, probe.Disposals); // disposed exactly once on unmount
    }

    // ════════════════════════════════════════════════════════════════
    //  #52 — UseCommand<T> caches its wrapped Execute across renders
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseCommand_Generic_Wrapped_Execute_Is_Same_Instance_When_Command_Unchanged()
    {
        var ctx = NewCtx();
        var cmd = new Command<string> { Label = "Delete", ExecuteAsync = _ => Task.CompletedTask };

        var r1 = ctx.UseCommand(cmd);
        Rerender(ctx);
        var r2 = ctx.UseCommand(cmd);

        Assert.NotNull(r1.Execute);
        Assert.Same(r1.Execute, r2.Execute);
    }

    // ════════════════════════════════════════════════════════════════
    //  M1 — UseMemoCellsByKey full-reuse requires KEY stability, not just value
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseMemoCellsByKey_Rebuilds_When_Key_Changes_Despite_Equal_Items()
    {
        // The full-reuse fast path must also require key stability. With equal items
        // but changed keys, cells are rebuilt (keyed identity changed) rather than
        // wrongly reused — matching the slow path's key-identity reuse.
        var ctx = NewCtx();
        int delta = 0;
        var items = new[] { 1, 2, 3 };
        int builds = 0;
        var first = ctx.UseMemoCellsByKey<int, int>(items, x => x + delta, (item, i) => { builds++; return MakeCell(item); }, "d");
        Rerender(ctx);
        delta = 100; // same items, but every key now differs
        var second = ctx.UseMemoCellsByKey<int, int>(items, x => x + delta, (item, i) => { builds++; return MakeCell(item); }, "d");

        Assert.NotSame(first, second);       // fast path skipped (keys changed)
        Assert.NotSame(first[0], second[0]); // cell rebuilt under its new key
        Assert.Equal(6, builds);             // 3 first render + 3 rebuilt
    }

    // ════════════════════════════════════════════════════════════════
    //  #57 / M2 — UseResource cache-key recompute correctness
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseResource_Stable_Deps_Does_Not_Refetch_But_Changed_Deps_Does()
    {
        // #57: identical deps + no explicit key reuses the deps-hash key (no spurious
        // refetch); changed deps recompute the key and fetch again.
        var cache = NewCache();
        var dispatcher = new InlineDispatcher();
        int calls = 0;
        Func<CancellationToken, Task<int>> fetcher = _ => { calls++; return Task.FromResult(calls); };
        var opts = new ResourceOptions(StaleTime: TimeSpan.FromMinutes(5));

        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        ctx.UseResource(fetcher, cache, new object[] { 1 }, opts, dispatcher);
        ctx.FlushEffects();
        Assert.Equal(1, calls);

        ctx.BeginRender(() => { });
        ctx.UseResource(fetcher, cache, new object[] { 1 }, opts, dispatcher); // same deps
        ctx.FlushEffects();
        Assert.Equal(1, calls); // reused key → no refetch

        ctx.BeginRender(() => { });
        ctx.UseResource(fetcher, cache, new object[] { 2 }, opts, dispatcher); // changed deps
        ctx.FlushEffects();
        Assert.Equal(2, calls); // recomputed key → refetch

        ctx.RunCleanups();
    }

    [Fact]
    public void UseResource_Explicit_To_Null_CacheKey_With_Equal_Deps_Recomputes_Key()
    {
        // M2 regression: when options.CacheKey transitions non-null -> null while deps
        // are unchanged, the #57 rehash-skip must NOT reuse the stale explicit key. It
        // must fall back to the deps-hash key (a key change → a fresh fetch), matching
        // the pre-#57 behaviour of always recomputing when no explicit key is supplied.
        var cache = NewCache();
        var dispatcher = new InlineDispatcher();
        int calls = 0;
        Func<CancellationToken, Task<int>> fetcher = _ => { calls++; return Task.FromResult(calls); };

        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        ctx.UseResource(fetcher, cache, new object[] { 1 }, new ResourceOptions(CacheKey: "explicit"), dispatcher);
        ctx.FlushEffects();
        Assert.Equal(1, calls);

        ctx.BeginRender(() => { });
        ctx.UseResource(fetcher, cache, new object[] { 1 }, null, dispatcher); // CacheKey now null, deps equal
        ctx.FlushEffects();
        Assert.Equal(2, calls); // key recomputed → new fetch (would stay 1 if stale key reused)

        ctx.RunCleanups();
    }

    // ════════════════════════════════════════════════════════════════
    //  Test helpers
    // ════════════════════════════════════════════════════════════════

    private sealed class DisposeProbe : IDisposable
    {
        public int Disposals;
        public void Dispose() => Disposals++;
    }

    private sealed class InlineDispatcher : IHookDispatcher
    {
        public void Post(Action action) => action();
    }

    private static QueryCache NewCache()
    {
        var cache = new QueryCache();
        var t = DateTime.UtcNow;
        cache.UtcNow = () => t; // frozen clock so cached entries stay fresh within a test
        return cache;
    }
}
