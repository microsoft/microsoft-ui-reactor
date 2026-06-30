using System;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #688: behavioral guards for the typed-arity <c>UseEffect</c>/<c>UseMemo</c>/
/// <c>UseCallback</c> overloads (1–3 positional deps). These are the ergonomic,
/// allocation-free counterparts to the <c>params object[]</c> overloads — they must
/// be observationally identical to <c>params</c> on the deps short-circuit:
///  - factory/effect/callback is skipped while every dep compares equal,
///  - it re-runs (and the prior effect cleanup fires exactly once) on any change,
///  - a lone arity-1 dep whose <em>compile-time</em> type is an array stays
///    element-wise compared (matching the old <c>params</c> path), while an
///    <c>object</c>-typed array value is reference-compared,
///  - the comparer never throws when a dep's runtime type changes at a slot,
///  - nullable value-type deps skip/re-run correctly (incl. null transitions),
///  - stored deps are snapshotted, so a caller that reuses+mutates one array still
///    observes the change.
/// Ported from the (closed) PR #668 allocation suite, scoped to the arity overloads.
/// </summary>
public class HookArityOverloadTests
{
    private static RenderContext NewCtx()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        return ctx;
    }

    private static void Rerender(RenderContext ctx) => ctx.BeginRender(() => { });

    // ── UseMemo: deps short-circuit (params baseline + arity 1/2/3) ──────────

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

    // ── UseEffect: deps short-circuit (params baseline + arity 1/2/3) ────────

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

    // ── UseEffect cleanup flavor: prior cleanup fires exactly once on change ──

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

    // ── UseCallback: deps short-circuit (arity 2/3) ──────────────────────────

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

    // ── Arity-1 array-dep semantics (AsParamsArrayDep) ───────────────────────

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

    [Fact]
    public void UseMemo_Arity1_ObjectTyped_ArrayValue_Is_Compared_By_Reference_Like_Params()
    {
        // An arity-1 dep whose STATIC type is object (not an array) but whose RUNTIME
        // value is object[] must be compared as ONE value, exactly as the legacy params
        // overload did (reference comparison). Only UseMemo binds the arity-1 generic
        // here — both its overloads are generic, so the normal-form arity-1 wins over
        // the expanded-form params overload (UseEffect/UseCallback bind their
        // non-generic params overload for an object-typed single dep).
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

    // ── Comparer robustness: runtime type change must not throw ───────────────

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

    // ── Nullable value-type dep semantics ────────────────────────────────────

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

    // ── Aliasing guard: stored deps are snapshotted (SnapshotDeps) ───────────

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

    // ── UseCallback arity-1 scalar (issue #688 follow-up coverage) ───────────

    [Fact]
    public void UseCallback_Arity1_Scalar_Dep_Keeps_Identity_While_Unchanged_And_Adopts_On_Change()
    {
        var ctx = NewCtx();
        Action a1 = () => { };
        Action a2 = () => { };
        Action a3 = () => { };
        var r1 = ctx.UseCallback(a1, 5);
        Rerender(ctx);
        var r2 = ctx.UseCallback(a2, 5); // dep unchanged → keep a1
        Rerender(ctx);
        var r3 = ctx.UseCallback(a3, 6); // dep changed → adopt a3

        Assert.Same(a1, r1);
        Assert.Same(a1, r2);
        Assert.Same(a3, r3);
    }

    // ── Arity-2/3 reruns on change (not just the unchanged/skip path) ────────

    [Fact]
    public void UseEffect_Arity2_And_Arity3_Rerun_When_A_Dep_Changes()
    {
        var ctx = NewCtx();
        int runs2 = 0, runs3 = 0;

        ctx.UseEffect(() => { runs2++; }, 1, "x");
        ctx.UseEffect(() => { runs3++; }, 1, "x", 2.5);
        ctx.FlushEffects();
        Assert.Equal(1, runs2);
        Assert.Equal(1, runs3);

        Rerender(ctx);
        ctx.UseEffect(() => { runs2++; }, 2, "x");      // d1 changed
        ctx.UseEffect(() => { runs3++; }, 1, "x", 9.9); // d3 changed
        ctx.FlushEffects();
        Assert.Equal(2, runs2);
        Assert.Equal(2, runs3);

        Rerender(ctx);
        ctx.UseEffect(() => { runs2++; }, 2, "x");      // unchanged → skip
        ctx.UseEffect(() => { runs3++; }, 1, "x", 9.9); // unchanged → skip
        ctx.FlushEffects();
        Assert.Equal(2, runs2);
        Assert.Equal(2, runs3);
    }

    [Fact]
    public void UseMemo_Arity2_And_Arity3_Recompute_When_A_Dep_Changes()
    {
        var ctx = NewCtx();
        int calls2 = 0, calls3 = 0;

        ctx.UseMemo(() => { calls2++; return calls2; }, 1, "x");
        ctx.UseMemo(() => { calls3++; return calls3; }, 1, "x", true);
        Assert.Equal(1, calls2);
        Assert.Equal(1, calls3);

        Rerender(ctx);
        ctx.UseMemo(() => { calls2++; return calls2; }, 1, "x");       // unchanged → cached
        ctx.UseMemo(() => { calls3++; return calls3; }, 1, "x", true); // unchanged → cached
        Assert.Equal(1, calls2);
        Assert.Equal(1, calls3);

        Rerender(ctx);
        ctx.UseMemo(() => { calls2++; return calls2; }, 9, "x");        // d1 changed
        ctx.UseMemo(() => { calls3++; return calls3; }, 1, "x", false); // d3 changed
        Assert.Equal(2, calls2);
        Assert.Equal(2, calls3);
    }

    // ── Cleanup runs on unmount (RunCleanups), not just on dep change ────────

    [Fact]
    public void UseEffect_Cleanup_Arity_Runs_Cleanup_Once_On_Unmount()
    {
        var ctx = NewCtx();
        int runs = 0, cleanups = 0;
        ctx.UseEffect(() => { runs++; return () => cleanups++; }, 1, "x", 2.5);
        ctx.FlushEffects();
        Assert.Equal(1, runs);
        Assert.Equal(0, cleanups);

        ctx.RunCleanups(); // unmount → live cleanup fires exactly once
        Assert.Equal(1, cleanups);
    }

    // ── Reference-type / struct value equality (DepEquals) ───────────────────

    [Fact]
    public void UseEffect_Arity1_ReferenceType_Dep_Uses_Value_Equality()
    {
        // A reference type with value equality (record) skips while equal-by-value across
        // distinct instances and re-runs when the value differs — matching the params
        // path's object.Equals.
        var ctx = NewCtx();
        int runs = 0;
        ctx.UseEffect(() => { runs++; }, new Point(1, 2));
        ctx.FlushEffects();
        Assert.Equal(1, runs);

        Rerender(ctx);
        ctx.UseEffect(() => { runs++; }, new Point(1, 2)); // distinct instance, equal value → skip
        ctx.FlushEffects();
        Assert.Equal(1, runs);

        Rerender(ctx);
        ctx.UseEffect(() => { runs++; }, new Point(1, 3)); // different value → re-run
        ctx.FlushEffects();
        Assert.Equal(2, runs);
    }

    [Fact]
    public void UseMemo_Arity1_Struct_Dep_Skips_While_Equal_And_Recomputes_On_Change()
    {
        // A struct dep compares by value through EqualityComparer<T>.Default with no
        // params object[] allocation and no boxing on the unchanged path.
        var ctx = NewCtx();
        int calls = 0;
        var v1 = ctx.UseMemo(() => { calls++; return calls; }, new PointStruct(1, 2));
        Rerender(ctx);
        var v2 = ctx.UseMemo(() => { calls++; return calls; }, new PointStruct(1, 2)); // equal → cached
        Rerender(ctx);
        var v3 = ctx.UseMemo(() => { calls++; return calls; }, new PointStruct(1, 9)); // changed → recompute

        Assert.Equal(1, v1);
        Assert.Equal(1, v2);
        Assert.Equal(2, v3);
        Assert.Equal(2, calls);
    }

    // ── Params-vs-arity equivalence (same skip/re-run decision) ──────────────

    [Fact]
    public void Arity_And_Params_Make_The_Same_Skip_Or_Rerun_Decision()
    {
        var ctx = NewCtx();
        int arityRuns = 0, paramsRuns = 0;
        var seq = new (int a, string b)[] { (1, "x"), (1, "x"), (2, "x"), (2, "y"), (2, "y") };
        int[] expected = { 1, 1, 2, 3, 3 }; // cumulative run count after each render

        for (int i = 0; i < seq.Length; i++)
        {
            if (i > 0) Rerender(ctx);
            var (a, b) = seq[i];
            ctx.UseEffect(() => { arityRuns++; }, a, b);                   // arity-2
            ctx.UseEffect(() => { paramsRuns++; }, new object[] { a, b }); // params
            ctx.FlushEffects();
            Assert.Equal(expected[i], arityRuns);
            Assert.Equal(expected[i], paramsRuns);
            Assert.Equal(arityRuns, paramsRuns); // never diverge
        }
    }

    private sealed record Point(int X, int Y);
    private readonly record struct PointStruct(int X, int Y);
}
