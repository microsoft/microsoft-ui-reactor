using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #327 (Option A) — typed keyed memo, ElementFactory-scoped.
///
/// <para>These are HEADLESS tests: they drive <see cref="ElementFactory{T}.BuildOrCache"/> and the
/// underlying <see cref="KeyedMemoCache"/> directly — no WinUI controls are mounted — so they run in
/// the fast xUnit tier. <see cref="ElementFactory{T}.BuildOrCache"/> is the exact per-recycle
/// resolution step that both <see cref="ElementFactory{T}.GetElement"/> (realize) and
/// <see cref="ElementFactory{T}.RefreshRealizedItems"/> (in-place refresh) funnel through, so calling
/// it in a loop faithfully simulates fast-scroll recycle/realize cycles.</para>
///
/// <para>The effectiveness metric is the <b>rebuild count</b> — the number of times the row's inner
/// <c>Factory</c> delegate actually runs. Without the memo, the int-index VirtualList path rebuilds
/// every row on every recycle (the <c>_viewBuilderCache</c> guard never hits because each int access
/// re-boxes). With the memo, a rebuild happens only on a genuine cache miss (first sight / after
/// eviction / after invalidation). Returning the SAME inner instance on a hit is what lets
/// <see cref="Element.ShallowEquals"/> short-circuit on <c>ReferenceEquals</c> and collapse the
/// per-row reconcile descent to a sub-µs skip — asserted here via <see cref="Element.CanSkipUpdate"/>.</para>
/// </summary>
public class KeyedMemoTests
{
    // ── Helpers ─────────────────────────────────────────────────────

    private static ElementFactory<int> MakeIntFactory(
        IReadOnlyList<int> items, global::System.Func<int, int, Element> viewBuilder)
        => new(items, viewBuilder, new Reconciler(), requestRerender: static () => { }, pool: null);

    // A deliberately non-trivial row body so the "same instance → skip descent" win is meaningful:
    // ShallowEquals short-circuits at the root on ReferenceEquals and never walks these children.
    private static Element Row(int i, string suffix = "")
        => new BorderElement(new TextBlockElement($"row {i}{suffix}"));

    // Legacy int-index path: key is the index string, item is the boxed index, keyed=false.
    private static Element Realize(ElementFactory<int> factory, int index)
        => factory.BuildOrCache(index.ToString(), index, index, keyed: false);

    // ── Overload resolution (required) ──────────────────────────────

    [Fact]
    public void Memo_KeyedOverload_BindsForValueKey()
    {
        // Memo(key, () => …) must bind to the NEW typed keyed overload.
        var el = Memo(5, () => new TextBlockElement("x"));
        var keyed = Assert.IsType<KeyedMemoElement>(el);
        Assert.Equal(5, keyed.MemoKey);
    }

    [Fact]
    public void Memo_ClassicOverload_BindsForRenderContextLambda()
    {
        // Memo(ctx => …) and Memo(ctx => …, deps) must still bind to the EXISTING overload.
        var noDeps = Memo(ctx => new TextBlockElement("y"));
        Assert.IsType<MemoElement>(noDeps);

        var withDeps = Memo(ctx => new TextBlockElement("z"), 1, "a");
        var memo = Assert.IsType<MemoElement>(withDeps);
        Assert.NotNull(memo.Dependencies);
        Assert.Equal(2, memo.Dependencies!.Length);
    }

    [Fact]
    public void Memo_KeyedOverload_AcceptsTupleKeyForWidenedInputs()
    {
        // The documented escape from staleness: fold extra inputs into the key.
        var el = Memo((id: 7, selected: true), () => new TextBlockElement("t"));
        var keyed = Assert.IsType<KeyedMemoElement>(el);
        Assert.Equal((7, true), keyed.MemoKey);
    }

    // ── Argument validation (issue #327 review) ─────────────────────

    [Fact]
    public void Memo_KeyedOverload_NullKey_ThrowsArgumentNullException()
    {
        // A null reference key is rejected up front with the argument name, instead of being
        // deferred to an opaque throw from the KeyedMemoCache dictionary lookup at realize time.
        var ex = Assert.Throws<global::System.ArgumentNullException>(
            () => { Memo((string)null!, () => new TextBlockElement("x")); });
        Assert.Equal("key", ex.ParamName);
    }

    [Fact]
    public void Memo_KeyedOverload_NullFactory_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<global::System.ArgumentNullException>(
            () => { Memo(1, (global::System.Func<Element>)null!); });
        Assert.Equal("factory", ex.ParamName);
    }

    // ── Direct (non-virtualized) reconcile: keyed update-vs-replace ──

    [Fact]
    public void KeyedMemo_DirectReconcile_IsKeyed_SameKeyUpdates_DifferentKeyReplaces()
    {
        // When a KeyedMemoElement is reconciled directly (outside a virtualized ElementFactory),
        // the reconciler must NOT reconstruct the "old" inner tree by re-invoking the old factory
        // (which would diff against the wrong "old" if the factory reads mutable state by ref).
        // Instead CanUpdate is key-driven: SAME key ⇒ update in place (the Update arm then skips,
        // since the factory output is identical by contract — regardless of factory identity);
        // DIFFERENT key ⇒ replace (unmount + fresh mount of the new factory output). This proves
        // the old factory is never re-run at update time.
        var rec = new Reconciler();

        // Different factory closures, SAME key → update in place (key, not closure, decides).
        var sameKeyA = Memo(1, () => new TextBlockElement("a"));
        var sameKeyB = Memo(1, () => new TextBlockElement("b"));
        Assert.True(rec.CanUpdate(sameKeyA, sameKeyB));

        // Changed key → replace path (no old-factory re-invocation).
        var otherKey = Memo(2, () => new TextBlockElement("a"));
        Assert.False(rec.CanUpdate(sameKeyA, otherKey));
    }

    // ── Effectiveness: rebuilds collapse across recycle cycles ───────

    [Fact]
    public void KeyedMemo_CollapsesRebuilds_AcrossRecycleCycles()
    {
        const int Window = 20;   // realized window of rows
        const int Cycles = 50;   // scroll passes that recycle/realize the whole window
        var items = Enumerable.Range(0, Window).ToList();

        // BEFORE — plain viewBuilder, no memo. Every recycle rebuilds the row and yields a
        // fresh instance, so the reconciler would descend the whole subtree every time.
        int beforeBuilds = 0;
        var before = MakeIntFactory(items, (i, _) => { beforeBuilds++; return Row(i); });
        var beforeLast = new Element[Window];
        int beforeSameInstanceHits = 0;
        for (int c = 0; c < Cycles; c++)
            for (int i = 0; i < Window; i++)
            {
                var el = Realize(before, i);
                if (c > 0 && ReferenceEquals(beforeLast[i], el)) beforeSameInstanceHits++;
                beforeLast[i] = el;
            }

        // AFTER — same rows wrapped in Memo(i, …). Rebuild only on the first miss per key.
        int afterBuilds = 0;
        var after = MakeIntFactory(items, (i, _) => Memo(i, () => { afterBuilds++; return Row(i); }));
        var afterLast = new Element[Window];
        int afterSameInstanceHits = 0;
        int afterSkippableHits = 0;
        for (int c = 0; c < Cycles; c++)
            for (int i = 0; i < Window; i++)
            {
                var el = Realize(after, i);
                if (c > 0)
                {
                    if (ReferenceEquals(afterLast[i], el)) afterSameInstanceHits++;
                    // The reconcile-descent skip precondition: identical instance ⇒ CanSkipUpdate.
                    if (Element.CanSkipUpdate(afterLast[i], el)) afterSkippableHits++;
                }
                afterLast[i] = el;
            }

        int recycleHits = (Cycles - 1) * Window; // realizations after the first cycle

        // Rebuild counts: BEFORE rebuilds every realization; AFTER rebuilds once per distinct key.
        Assert.Equal(Window * Cycles, beforeBuilds);            // 1000
        Assert.Equal(Window, afterBuilds);                     // 20
        Assert.Equal(Window, (int)after.DebugKeyedMemoFactoryInvocations);

        // BEFORE never returns the same instance across recycles (→ full reconcile descent).
        Assert.Equal(0, beforeSameInstanceHits);
        // AFTER returns the SAME instance on every recycle hit (→ ShallowEquals ReferenceEquals
        // short-circuit → reconcile descent skipped). All hits are skippable.
        Assert.Equal(recycleHits, afterSameInstanceHits);
        Assert.Equal(recycleHits, afterSkippableHits);

        // The headline win: rebuilds-per-recycle drop from 100% to ~0% on the cache-hit cycles.
        Assert.True(afterBuilds < beforeBuilds / 10,
            $"expected ≥10× fewer rebuilds; before={beforeBuilds} after={afterBuilds}");
    }

    // ── Correctness guard: a changed keyed input is never served stale ──

    [Fact]
    public void KeyedMemo_KeyChange_DoesNotServeStaleContent()
    {
        // Author widens the key to a tuple (id, value). When `value` changes the KEY changes,
        // so the cache must build fresh content — never return the previous value's element.
        var items = Enumerable.Range(0, 1).ToList();
        string version = "a";
        var factory = MakeIntFactory(items,
            (i, _) => Memo((id: i, v: version), () => new TextBlockElement($"item {i} = {version}")));

        var first = Assert.IsType<TextBlockElement>(Realize(factory, 0));
        Assert.Equal("item 0 = a", first.Content);

        // Same realization while the keyed input is unchanged → cached SAME instance (no rebuild).
        var firstAgain = Realize(factory, 0);
        Assert.Same(first, firstAgain);

        // Keyed input changes → new key → fresh content (NOT the stale "= a").
        version = "b";
        var second = Assert.IsType<TextBlockElement>(Realize(factory, 0));
        Assert.Equal("item 0 = b", second.Content);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void KeyedMemo_UnkeyedExternalState_IsAuthorsResponsibility()
    {
        // Documents the purity contract: closing over state NOT folded into the key returns the
        // memoized instance (stale) — by design. This is the behavior the XML doc/guide warn about.
        var items = Enumerable.Range(0, 1).ToList();
        string external = "a";
        var factory = MakeIntFactory(items,
            // BUG-on-purpose: `external` is read but not in the key.
            (i, _) => Memo(i, () => new TextBlockElement($"v={external}")));

        var first = Assert.IsType<TextBlockElement>(Realize(factory, 0));
        Assert.Equal("v=a", first.Content);

        external = "b";
        var second = Assert.IsType<TextBlockElement>(Realize(factory, 0));
        // Stale by design — key didn't change, so the cached instance is reused.
        Assert.Same(first, second);
        Assert.Equal("v=a", ((TextBlockElement)second).Content);
    }

    // ── Invalidation: UpdateInPlace clears the cache (new closure boundary) ──

    [Fact]
    public void KeyedMemo_UpdateInPlace_InvalidatesCache()
    {
        var items = Enumerable.Range(0, 3).ToList();
        int buildsV1 = 0;
        var factory = MakeIntFactory(items,
            (i, _) => Memo(i, () => { buildsV1++; return Row(i, ":v1"); }));

        for (int c = 0; c < 5; c++)
            for (int i = 0; i < 3; i++)
                Realize(factory, i);

        Assert.Equal(3, buildsV1);                       // one build per key despite 15 realizations
        Assert.Equal(3, factory.DebugKeyedMemoCacheCount);

        // New items/viewBuilder closure (e.g. a component re-render). The cache must drop every
        // entry so the new closure's content is never shadowed by a stale instance.
        int buildsV2 = 0;
        factory.UpdateInPlace(items, (i, _) => Memo(i, () => { buildsV2++; return Row(i, ":v2"); }));
        Assert.Equal(0, factory.DebugKeyedMemoCacheCount);

        var rebuilt = Realize(factory, 0);
        var inner = Assert.IsType<TextBlockElement>(((BorderElement)rebuilt).Child);
        Assert.Equal("row 0:v2", inner.Content);
        Assert.Equal(1, buildsV2);
    }

    // ── Bound: LRU never grows past capacity; eviction forces a rebuild ──

    [Fact]
    public void KeyedMemoCache_IsBounded_AndEvictsLeastRecentlyUsed()
    {
        var cache = new KeyedMemoCache(capacity: 3);

        int Build(int k, global::System.Func<Element> body)
        {
            int before = (int)cache.FactoryInvocations;
            cache.Resolve(new KeyedMemoElement(k, body), identityKey: null);
            return (int)cache.FactoryInvocations - before; // 1 = rebuilt (miss), 0 = cache hit
        }

        Assert.Equal(1, Build(0, () => Row(0))); // miss
        Assert.Equal(1, Build(1, () => Row(1))); // miss
        Assert.Equal(1, Build(2, () => Row(2))); // miss   → LRU = [2,1,0]
        Assert.Equal(0, Build(0, () => Row(0))); // hit, promotes 0 → LRU = [0,2,1]
        Assert.Equal(3, cache.Count);            // never exceeds capacity

        // Insert a 4th distinct key → evicts the LRU tail (key 1, the least-recently-used).
        Assert.Equal(1, Build(3, () => Row(3))); // miss   → evicts 1 → LRU = [3,0,2]
        Assert.Equal(3, cache.Count);

        Assert.Equal(1, Build(1, () => Row(1))); // 1 was evicted → rebuild
        Assert.Equal(0, Build(0, () => Row(0))); // 0 still cached → hit
    }

    [Fact]
    public void KeyedMemoCache_Hit_ReturnsReferenceEqualInstance()
    {
        var cache = new KeyedMemoCache();
        var memo = new KeyedMemoElement(42, () => Row(42));

        var a = cache.Resolve(memo, identityKey: null);
        var b = cache.Resolve(memo, identityKey: null);

        Assert.Same(a, b);                       // identical instance across resolves
        Assert.True(Element.CanSkipUpdate(a, b)); // ⇒ reconcile descent is skipped
        Assert.Equal(1, (int)cache.FactoryInvocations);
    }

    [Fact]
    public void KeyedMemoCache_StampsIdentityKey_OnlyWhenInnerKeyIsNull()
    {
        var cache = new KeyedMemoCache();

        // keyed path (ReactorRow): inner has no Key → stamp the per-item identity key.
        var stamped = cache.Resolve(new KeyedMemoElement("k1", () => Row(1)), identityKey: "row-1");
        Assert.Equal("row-1", stamped.Key);

        // explicit author key inside the factory always wins.
        var explicitKey = cache.Resolve(
            new KeyedMemoElement("k2", () => Row(2) with { Key = "author" }), identityKey: "row-2");
        Assert.Equal("author", explicitKey.Key);

        // int-index path passes identityKey=null → never stamps.
        var unstamped = cache.Resolve(new KeyedMemoElement("k3", () => Row(3)), identityKey: null);
        Assert.Null(unstamped.Key);
    }
}
