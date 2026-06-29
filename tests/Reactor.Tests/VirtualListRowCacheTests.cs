using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #327 — opt-in cross-container row memoization on <see cref="VirtualListElement.CacheRowsBy"/>.
///
/// <para>These are deterministic, headless fixtures: they drive the real
/// <see cref="VirtualListComponent.Render"/> to obtain the wrapped view builder
/// (<see cref="LazyVStackElement{T}.ViewBuilder"/>) and then replay
/// realize/recycle cycles against it, counting how many times the author's
/// <c>RenderItem</c> is actually invoked (the "rebuild" cost the issue measures at
/// ~a third of scroll wall-clock) and asserting reference stability (which is what
/// lets the reconciler short-circuit on <see cref="Element.CanSkipUpdate"/> and skip
/// the per-row diff descent).</para>
/// </summary>
public class VirtualListRowCacheTests
{
    // ── Harness ─────────────────────────────────────────────────────

    /// <summary>
    /// Mount a VirtualListComponent with <paramref name="props"/> and return the
    /// wrapped view builder the reconciler would call on every container realize.
    /// Mirrors the reconciler's component flow: SetProps → BeginRender → Render →
    /// FlushEffects (same pattern as MemoizationSelfHostTests).
    /// </summary>
    private static Func<int, int, Element> RenderViewBuilder(
        VirtualListComponent comp, VirtualListElement props)
    {
        ((IPropsReceiver)comp).SetProps(props);
        var scope = new ContextScope();
        comp.Context.BeginRender(() => { }, scope);
        var element = comp.Render();
        comp.Context.FlushEffects();
        return ((LazyVStackElement<int>)element).ViewBuilder;
    }

    /// <summary>Replay <paramref name="passes"/> sweeps over a rolling window of
    /// <paramref name="window"/> rows — the same index is realized once per pass,
    /// modelling the framework recycling and re-realizing a container repeatedly
    /// during a fast scroll.</summary>
    private static void DriveRecycleCycles(Func<int, int, Element> vb, int window, int passes)
    {
        for (int p = 0; p < passes; p++)
            for (int i = 0; i < window; i++)
                _ = vb(i, i); // for LazyVStack<int>, item == index
    }

    // A small multi-node row, so a real reconcile would have to descend.
    private static Element Row(int i) =>
        Border(VStack(4, TextBlock($"Item {i}"), TextBlock($"Description {i}")));

    private static string TextOf(Element e) => ((TextBlockElement)e).Content;

    // ── Effectiveness: rebuild count before vs. after ───────────────

    [Fact]
    public void CacheRowsBy_CollapsesRowRebuilds_AcrossRecycleCycles()
    {
        const int window = 24;
        const int passes = 20; // 24 * 20 = 480 realize events

        // BEFORE — default behavior (no opt-in): every realize rebuilds the row.
        int baselineBuilds = 0;
        var baselineVb = RenderViewBuilder(new VirtualListComponent(), new VirtualListElement
        {
            ItemCount = 1000,
            RenderItem = i => { baselineBuilds++; return Row(i); },
        });
        DriveRecycleCycles(baselineVb, window, passes);

        // AFTER — opt-in: each row is built once per unique key, the rest are hits.
        int cachedBuilds = 0;
        var cachedVb = RenderViewBuilder(new VirtualListComponent(), new VirtualListElement
        {
            ItemCount = 1000,
            RenderItem = i => { cachedBuilds++; return Row(i); },
            CacheRowsBy = i => i.ToString(),
        });
        DriveRecycleCycles(cachedVb, window, passes);

        Assert.Equal(window * passes, baselineBuilds); // 480 — rebuilt on every realize
        Assert.Equal(window, cachedBuilds);            // 24  — one rebuild per unique key
        Assert.True(cachedBuilds * 10 < baselineBuilds,
            $"expected >10x rebuild reduction; before={baselineBuilds} after={cachedBuilds}");
    }

    [Fact]
    public void CacheRowsBy_ReturnsSameInstance_SoReconcilerCanSkip()
    {
        var vb = RenderViewBuilder(new VirtualListComponent(), new VirtualListElement
        {
            ItemCount = 100,
            RenderItem = Row,
            CacheRowsBy = i => i.ToString(),
        });

        var first = vb(7, 7);
        var second = vb(7, 7);

        Assert.Same(first, second); // reference-stable across recycles
        // ReferenceEquals(a, b) is exactly the fast path the reconciler short-circuits
        // on — zero per-row reconcile descent on a cache hit.
        Assert.True(Element.CanSkipUpdate(first, second));
    }

    // ── Correctness guard: no stale content when the keyed input changes ─

    [Fact]
    public void CacheRowsBy_DoesNotServeStale_WhenKeyedInputChanges()
    {
        // Author folds the value RenderItem reads into the key — the documented
        // purity contract. Mutating it must invalidate that row.
        var data = new[] { "alpha", "beta", "gamma" };
        var vb = RenderViewBuilder(new VirtualListComponent(), new VirtualListElement
        {
            ItemCount = 3,
            RenderItem = i => TextBlock(data[i]),
            CacheRowsBy = i => $"{i}:{data[i]}",
        });

        var before = vb(1, 1);
        Assert.Equal("beta", TextOf(before));

        data[1] = "beta-v2";    // the keyed input changed
        var after = vb(1, 1);   // key changed → miss → fresh render

        Assert.NotSame(before, after);
        Assert.Equal("beta-v2", TextOf(after)); // never serves the stale instance
    }

    // ── Bounded LRU: never grows past capacity; evicts least-recently-used ─

    [Fact]
    public void CacheRowsBy_IsBounded_EvictsLeastRecentlyUsed()
    {
        int builds = 0;
        var vb = RenderViewBuilder(new VirtualListComponent(), new VirtualListElement
        {
            ItemCount = 1000,
            RenderItem = i => { builds++; return Row(i); },
            CacheRowsBy = i => i.ToString(),
            RowCacheCapacity = 8,
        });

        // Fill the cache with keys 0..7 (LRU order: 0 oldest … 7 newest).
        for (int i = 0; i < 8; i++) _ = vb(i, i);
        Assert.Equal(8, builds);

        // Re-touch 0..7 — all hits, no rebuilds, but order becomes 0 oldest … 7 newest.
        for (int i = 0; i < 8; i++) _ = vb(i, i);
        Assert.Equal(8, builds);

        // A 9th key evicts the LRU entry (key 0).
        _ = vb(8, 8);
        Assert.Equal(9, builds);

        // Key 0 was evicted → rebuilds; key 8 is still hot → hit, no rebuild.
        _ = vb(0, 0);
        Assert.Equal(10, builds);
        var hot1 = vb(8, 8);
        var hot2 = vb(8, 8);
        Assert.Same(hot1, hot2);
        Assert.Equal(10, builds);
    }

    // ── Invalidation: a re-render with new count/closure drops the cache ─

    [Fact]
    public void CacheRowsBy_Cache_ResetsWhenItemCountOrRenderItemChanges()
    {
        var comp = new VirtualListComponent();

        int builds1 = 0;
        var vb1 = RenderViewBuilder(comp, new VirtualListElement
        {
            ItemCount = 100,
            RenderItem = i => { builds1++; return Row(i); },
            CacheRowsBy = i => i.ToString(),
        });
        _ = vb1(3, 3);
        _ = vb1(3, 3);
        Assert.Equal(1, builds1); // cached within this render

        // Re-render the SAME component instance with a different count + closure.
        // UseMemo's deps changed → a brand new cache, so the new builder cannot
        // serve the previous (possibly stale) instance.
        int builds2 = 0;
        var vb2 = RenderViewBuilder(comp, new VirtualListElement
        {
            ItemCount = 200,
            RenderItem = i => { builds2++; return Row(i); },
            CacheRowsBy = i => i.ToString(),
        });
        _ = vb2(3, 3);
        Assert.Equal(1, builds2); // freshly rebuilt against the new closure, not reused
    }

    // ── Default path is untouched when the flag is not used ─────────

    [Fact]
    public void CacheRowsBy_Null_RebuildsEveryAccess_DefaultBehavior()
    {
        int builds = 0;
        var vb = RenderViewBuilder(new VirtualListComponent(), new VirtualListElement
        {
            ItemCount = 100,
            RenderItem = i => { builds++; return Row(i); },
        });

        var a = vb(2, 2);
        var b = vb(2, 2);

        Assert.Equal(2, builds);  // no cache → rebuilt on each realize (today's behavior)
        Assert.NotSame(a, b);     // a fresh tree every time
    }

    [Fact]
    public void CacheRowsBy_WithFixedHeight_CachesPostHeightInstance()
    {
        // Wrapping-order correctness: .Height(...) returns a NEW element, so the
        // cached instance must be the post-Height one for hits to stay reference-stable.
        int builds = 0;
        var vb = RenderViewBuilder(new VirtualListComponent(), new VirtualListElement
        {
            ItemCount = 100,
            RenderItem = i => { builds++; return Row(i); },
            ItemHeight = 48,
            CacheRowsBy = i => i.ToString(),
        });

        var a = vb(5, 5);
        var b = vb(5, 5);

        Assert.Same(a, b);
        Assert.Equal(1, builds);
        Assert.True(Element.CanSkipUpdate(a, b));
    }

    // ── RowMemoCache unit semantics ─────────────────────────────────

    [Fact]
    public void RowMemoCache_Get_Miss_Then_Hit()
    {
        var cache = new RowMemoCache(4);
        Assert.False(cache.TryGet("a", out _));

        var e = TextBlock("a");
        cache.Set("a", e);

        Assert.True(cache.TryGet("a", out var got));
        Assert.Same(e, got);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void RowMemoCache_Evicts_LeastRecentlyUsed_At_Capacity()
    {
        var cache = new RowMemoCache(2);
        var a = TextBlock("a");
        var b = TextBlock("b");
        var c = TextBlock("c");

        cache.Set("a", a);
        cache.Set("b", b);   // {a,b}, a is LRU
        cache.Set("c", c);   // exceeds cap → evict a → {b,c}

        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryGet("a", out _)); // evicted
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void RowMemoCache_Get_Promotes_To_MostRecentlyUsed()
    {
        var cache = new RowMemoCache(2);
        cache.Set("a", TextBlock("a"));
        cache.Set("b", TextBlock("b")); // {a(LRU), b}

        Assert.True(cache.TryGet("a", out _)); // promote a → b is now LRU
        cache.Set("c", TextBlock("c"));        // evict b (now LRU), keep a

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void RowMemoCache_ReSet_Existing_Key_Updates_Value_And_Promotes()
    {
        var cache = new RowMemoCache(2);
        var a1 = TextBlock("a1");
        var a2 = TextBlock("a2");
        cache.Set("a", a1);
        cache.Set("b", TextBlock("b"));
        cache.Set("a", a2);             // update a's value + promote → b is LRU
        cache.Set("c", TextBlock("c")); // evict b

        Assert.Equal(2, cache.Count);
        Assert.True(cache.TryGet("a", out var got));
        Assert.Same(a2, got);          // latest value retained
        Assert.False(cache.TryGet("b", out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void RowMemoCache_Capacity_Is_Clamped_To_At_Least_One(int requested)
    {
        var cache = new RowMemoCache(requested);
        Assert.Equal(1, cache.Capacity);

        cache.Set("a", TextBlock("a"));
        cache.Set("b", TextBlock("b")); // evicts a
        Assert.Equal(1, cache.Count);
        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
    }

    [Fact]
    public void VirtualList_Factory_Threads_CacheRowsBy_Onto_Props()
    {
        Func<int, string> key = i => $"k{i}";
        var element = VirtualList(
            itemCount: 10,
            renderItem: i => TextBlock($"{i}"),
            cacheRowsBy: key,
            rowCacheCapacity: 32);

        var props = ((ComponentElement<VirtualListElement>)element).Props;
        Assert.Same(key, props.CacheRowsBy);
        Assert.Equal(32, props.RowCacheCapacity);
    }

    [Fact]
    public void VirtualListElement_CacheRowsBy_Defaults_Null_And_Capacity_128()
    {
        var el = new VirtualListElement { ItemCount = 1, RenderItem = i => TextBlock($"{i}") };
        Assert.Null(el.CacheRowsBy);
        Assert.Equal(128, el.RowCacheCapacity);
        Assert.Equal(128, VirtualListElement.DefaultRowCacheCapacity);
    }
}
