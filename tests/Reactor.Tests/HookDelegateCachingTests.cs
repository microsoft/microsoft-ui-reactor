using System;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #659: correctness guards for the cached hook delegates and the
/// UseMemoCells full-reuse early-out. The optimization caches the
/// setter/updater/dispatch delegate on the (identity-stable) hook cell and
/// returns the same instance every render. These tests pin:
///  - the cached delegate is reference-stable across renders,
///  - it still mutates the live cell value and triggers re-render,
///  - the two-arg reducer dispatch honors the LATEST render's reducer,
///  - threadSafe state still serializes (locked path retained),
///  - UseMemoCells full cache-hit returns the prior children array (zero new
///    allocations) and never re-invokes the builder.
/// </summary>
public class HookDelegateCachingTests
{
    private record Cell(string Content) : Element;

    // ── Cached delegate identity ─────────────────────────────────────────

    [Fact]
    public void UseState_Setter_Is_Reference_Stable_Across_Renders()
    {
        var ctx = new RenderContext();

        ctx.BeginRender(() => { });
        var (_, set1) = ctx.UseState(0);

        ctx.BeginRender(() => { });
        var (_, set2) = ctx.UseState(0);

        Assert.Same((object)set1, set2);
    }

    [Fact]
    public void UseReducer_Updater_Is_Reference_Stable_Across_Renders()
    {
        var ctx = new RenderContext();

        ctx.BeginRender(() => { });
        var (_, up1) = ctx.UseReducer(0);

        ctx.BeginRender(() => { });
        var (_, up2) = ctx.UseReducer(0);

        Assert.Same((object)up1, up2);
    }

    [Fact]
    public void UseReducer_TwoArg_Dispatch_Is_Reference_Stable_Across_Renders()
    {
        var ctx = new RenderContext();

        ctx.BeginRender(() => { });
        var (_, d1) = ctx.UseReducer<int, int>((s, a) => s + a, 0);

        ctx.BeginRender(() => { });
        var (_, d2) = ctx.UseReducer<int, int>((s, a) => s + a, 0);

        Assert.Same((object)d1, d2);
    }

    [Fact]
    public void UsePersisted_Setter_Is_Reference_Stable_Across_Renders()
    {
        var ctx = new RenderContext();

        ctx.BeginRender(() => { });
        var (_, set1) = ctx.UsePersisted("k659-stable", 0);

        ctx.BeginRender(() => { });
        var (_, set2) = ctx.UsePersisted("k659-stable", 0);

        Assert.Same((object)set1, set2);
    }

    // ── Cached delegate still observes live state + re-renders ────────────

    [Fact]
    public void Cached_UseState_Setter_Mutates_Value_And_Rerenders()
    {
        var ctx = new RenderContext();
        int renders = 0;

        ctx.BeginRender(() => renders++);
        var (v0, set) = ctx.UseState(0);
        Assert.Equal(0, v0);

        set(5);
        Assert.Equal(1, renders);

        // A setter captured on an earlier render keeps working after later renders.
        ctx.BeginRender(() => renders++);
        var (v1, set2) = ctx.UseState(0);
        Assert.Equal(5, v1);
        Assert.Same((object)set, set2);

        set(9); // the OLD reference still drives the current re-render callback.
        Assert.Equal(2, renders);

        ctx.BeginRender(() => renders++);
        var (v2, _) = ctx.UseState(0);
        Assert.Equal(9, v2);
    }

    [Fact]
    public void TwoArg_Reducer_Dispatch_Uses_Latest_Render_Reducer()
    {
        var ctx = new RenderContext();
        int renders = 0;

        // First render: reducer adds.
        ctx.BeginRender(() => renders++);
        var (_, dispatchAdd) = ctx.UseReducer<int, int>((s, a) => s + a, 10);

        // Second render: reducer subtracts. The cached dispatch must honor the
        // newest reducer, matching React's "reducer from the latest render".
        ctx.BeginRender(() => renders++);
        var (val, dispatchSub) = ctx.UseReducer<int, int>((s, a) => s - a, 10);
        Assert.Same((object)dispatchAdd, dispatchSub);

        dispatchSub(3);

        ctx.BeginRender(() => renders++);
        var (after, _) = ctx.UseReducer<int, int>((s, a) => s - a, 10);
        Assert.Equal(7, after); // 10 - 3, NOT 10 + 3
    }

    // ── threadSafe path retained ─────────────────────────────────────────

    [Fact]
    public async global::System.Threading.Tasks.Task ThreadSafe_State_Still_Serializes_Concurrent_Setters()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var (_, set) = ctx.UseReducer<int>(0, threadSafe: true);

        // 8 threads each increment 1000 times via the functional updater; the
        // retained lock must serialize the read-modify-write so none are lost.
        const int threads = 8, perThread = 1000;
        var tasks = Enumerable.Range(0, threads).Select(_ =>
            global::System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < perThread; i++)
                    set(v => v + 1);
            })).ToArray();
        await global::System.Threading.Tasks.Task.WhenAll(tasks);

        ctx.BeginRender(() => { });
        var (final, _) = ctx.UseReducer<int>(0, threadSafe: true);
        Assert.Equal(threads * perThread, final);
    }

    // ── UseMemoCells full cache-hit early-out ────────────────────────────

    [Fact]
    public void UseMemoCells_Full_CacheHit_Returns_Prior_Array_Without_Building()
    {
        var ctx = new RenderContext();
        var items = new[] { new Cell("a"), new Cell("b"), new Cell("c") };
        int builds = 0;

        ctx.BeginRender(() => { });
        var first = ctx.UseMemoCells<Cell>(items, (it, i) => { builds++; return it; }, "deps");
        Assert.Equal(3, builds);

        ctx.BeginRender(() => { });
        var second = ctx.UseMemoCells<Cell>(items, (it, i) => { builds++; return it; }, "deps");

        // Full cache-hit: same array instance, no extra builds.
        Assert.Same(first, second);
        Assert.Equal(3, builds);
    }

    [Fact]
    public void UseMemoCells_Partial_Change_After_CacheHit_Still_Rebuilds_Changed()
    {
        var ctx = new RenderContext();
        int builds = 0;
        Func<int, int, Element> b = (v, i) => { builds++; return new Cell($"v={v}"); };

        ctx.BeginRender(() => { });
        var first = ctx.UseMemoCells<int>(new[] { 1, 2, 3 }, b, "deps");

        // Full hit — early-out.
        ctx.BeginRender(() => { });
        var second = ctx.UseMemoCells<int>(new[] { 1, 2, 3 }, b, "deps");
        Assert.Same(first, second);

        // Now one item changes — a fresh array, only the changed cell rebuilt.
        ctx.BeginRender(() => { });
        var third = ctx.UseMemoCells<int>(new[] { 1, 99, 3 }, b, "deps");
        Assert.NotSame(first, third);
        Assert.Same(first[0], third[0]);
        Assert.NotSame(first[1], third[1]);
        Assert.Same(first[2], third[2]);
        Assert.Equal(3 + 1, builds); // 3 initial + 1 rebuild
    }

    [Fact]
    public void UseMemoCellsByKey_Full_CacheHit_Reuses_Element_References()
    {
        var ctx = new RenderContext();
        var items = new[] { new Cell("a"), new Cell("b") };
        int builds = 0;

        ctx.BeginRender(() => { });
        var first = ctx.UseMemoCellsByKey<Cell, string>(
            items, x => x.Content, (it, i) => { builds++; return it; }, "deps");

        ctx.BeginRender(() => { });
        var second = ctx.UseMemoCellsByKey<Cell, string>(
            items, x => x.Content, (it, i) => { builds++; return it; }, "deps");

        // ByKey has no array-reuse early-out (dropped on review for key-purity
        // safety): the array is fresh, but the element references are reused and
        // the builder is not re-invoked on a key+value match.
        Assert.Equal(2, builds);
        for (int i = 0; i < first.Length; i++)
            Assert.Same(first[i], second[i]);
    }

    // ── UseCallback inline (no wrapper) keeps its semantics ──────────────

    [Fact]
    public void UseCallback_Returns_Stable_Reference_While_Deps_Unchanged()
    {
        var ctx = new RenderContext();
        Action cb = () => { };

        ctx.BeginRender(() => { });
        var c1 = ctx.UseCallback(cb, "dep");

        ctx.BeginRender(() => { });
        var c2 = ctx.UseCallback(cb, "dep");

        Assert.Same((object)c1, c2);
        Assert.Same((object)cb, c1);
    }

    [Fact]
    public void UseCallback_Updates_When_Deps_Change()
    {
        var ctx = new RenderContext();
        Action a = () => { };
        Action b = () => { };

        ctx.BeginRender(() => { });
        var c1 = ctx.UseCallback(a, "v1");

        ctx.BeginRender(() => { });
        var c2 = ctx.UseCallback(b, "v2");

        Assert.Same((object)a, c1);
        Assert.Same((object)b, c2);
    }
}
