using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Consumer-side tests for the PR-C positional structural-skip fast path
/// (<see cref="ChildReconciler"/>, spec 034 §C). These run headless: cells are
/// callback-bearing <c>Button</c> elements that are <c>CanSkipUpdate</c>-equal
/// across renders, so reconciliation only ever takes the cheap skip arm — which
/// calls <c>children.Get(i)</c> to refresh the callback Tag. A tracking child
/// collection records exactly which indices are visited, making the difference
/// between the O(count) full walk and the O(changed) fast path directly
/// observable without a live WinUI control.
/// </summary>
public class ChildReconcilerStructuralSkipTests
{
    private static readonly global::System.Action NoOp = () => { };

    /// <summary>
    /// Records the indices passed to <see cref="IChildCollection.Get"/> (the
    /// per-visit COM read) plus any structural mutation. Returns <c>null</c> from
    /// Get — the skip arm's <c>is FrameworkElement</c> guard tolerates it, so no
    /// live control is needed.
    /// </summary>
    private sealed class TrackingChildCollection : IChildCollection
    {
        private int _count;
        public List<int> GetCalls { get; } = new();
        public List<string> Structural { get; } = new();

        public TrackingChildCollection(int count) => _count = count;

        public int Count => _count;
        public UIElement Get(int index) { GetCalls.Add(index); return null!; }
        public void Insert(int index, UIElement element) { Structural.Add($"Insert({index})"); _count++; }
        public void RemoveAt(int index) { Structural.Add($"RemoveAt({index})"); if (_count > 0) _count--; }
        public void Move(int oldIndex, int newIndex) => Structural.Add($"Move({oldIndex},{newIndex})");
        public void Replace(int index, UIElement element) => Structural.Add($"Replace({index})");
    }

    // Callback-bearing button cells are CanSkipUpdate-equal across renders when
    // their labels match (OnClick identity is intentionally ignored), so the
    // reconciler always takes the skip arm.
    private static Element[] ButtonCells(int n)
    {
        var arr = new Element[n];
        for (int i = 0; i < n; i++)
            arr[i] = Button($"cell-{i}", NoOp);
        return arr;
    }

    private static Element[] FreshCopyWithSameLabels(int n)
    {
        // New instances, same labels => ShallowEquals true => skip arm.
        var arr = new Element[n];
        for (int i = 0; i < n; i++)
            arr[i] = Button($"cell-{i}", NoOp);
        return arr;
    }

    [Fact]
    public void NoHint_Full_Walk_Visits_Every_Common_Index()
    {
        const int n = 5;
        var oldChildren = ButtonCells(n);
        var newChildren = FreshCopyWithSameLabels(n);
        var coll = new TrackingChildCollection(n);
        var reconciler = new Reconciler();

        ChildReconciler.Reconcile(oldChildren, newChildren, coll, reconciler, NoOp);

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, coll.GetCalls);
        Assert.Empty(coll.Structural);
        Assert.Equal(n, reconciler.DebugElementsSkipped);
    }

    [Fact]
    public void Hint_Fast_Path_Visits_Only_Changed_Indices()
    {
        const int n = 6;
        var oldChildren = ButtonCells(n);
        var newChildren = FreshCopyWithSameLabels(n);
        var coll = new TrackingChildCollection(n);
        var reconciler = new Reconciler();

        ChildDiffHints.Publish(newChildren, new ChildDiffHint(new[] { 1, 4 }, themeSensitiveCount: 0, previousChildren: oldChildren));
        ChildReconciler.Reconcile(oldChildren, newChildren, coll, reconciler, NoOp);

        // Only the changed indices are visited; the untouched range is skipped.
        Assert.Equal(new[] { 1, 4 }, coll.GetCalls);
        Assert.Empty(coll.Structural);
        // The skipped-element diagnostic still accounts for every cell.
        Assert.Equal(n, reconciler.DebugElementsSkipped);
    }

    [Fact]
    public void Hint_And_Full_Walk_Produce_Identical_Skip_Accounting()
    {
        const int n = 5;
        // Full walk (no hint).
        var collFull = new TrackingChildCollection(n);
        var rFull = new Reconciler();
        ChildReconciler.Reconcile(ButtonCells(n), FreshCopyWithSameLabels(n), collFull, rFull, NoOp);

        // Fast path (hint present).
        var oldFast = ButtonCells(n);
        var newChildren = FreshCopyWithSameLabels(n);
        var collFast = new TrackingChildCollection(n);
        var rFast = new Reconciler();
        ChildDiffHints.Publish(newChildren, new ChildDiffHint(new[] { 2 }, themeSensitiveCount: 0, previousChildren: oldFast));
        ChildReconciler.Reconcile(oldFast, newChildren, collFast, rFast, NoOp);

        // Both paths skip the same number of elements and mutate nothing.
        Assert.Equal(rFull.DebugElementsSkipped, rFast.DebugElementsSkipped);
        Assert.Empty(collFull.Structural);
        Assert.Empty(collFast.Structural);
        // The fast path is strictly a subset of the full walk's visits.
        Assert.Equal(new[] { 2 }, collFast.GetCalls);
    }

    [Fact]
    public void ThemeSensitive_Hint_Forces_Full_Walk()
    {
        // The consumer reads only hint.AnyThemeSensitive; the cells themselves
        // are plain so the full walk stays on the safe skip arm. A theme-sensitive
        // hint must defeat the fast path and visit every index.
        const int n = 5;
        var oldChildren = ButtonCells(n);
        var newChildren = FreshCopyWithSameLabels(n);
        var coll = new TrackingChildCollection(n);
        var reconciler = new Reconciler();

        ChildDiffHints.Publish(newChildren, new ChildDiffHint(new[] { 1 }, themeSensitiveCount: 1, previousChildren: oldChildren));
        ChildReconciler.Reconcile(oldChildren, newChildren, coll, reconciler, NoOp);

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, coll.GetCalls);
        Assert.Equal(n, reconciler.DebugElementsSkipped);
    }

    [Fact]
    public void Count_Mismatch_Defeats_Fast_Path()
    {
        // Live collection count != element count (e.g. an in-flight animation
        // inflated/deflated it) — gate (2) must fall back to the full walk.
        const int n = 5;
        var oldChildren = ButtonCells(n);
        var newChildren = FreshCopyWithSameLabels(n);
        var coll = new TrackingChildCollection(n - 1); // mismatched
        var reconciler = new Reconciler();

        ChildDiffHints.Publish(newChildren, new ChildDiffHint(new[] { 1 }, themeSensitiveCount: 0, previousChildren: oldChildren));
        ChildReconciler.Reconcile(oldChildren, newChildren, coll, reconciler, NoOp);

        // Full walk truncated at the live count; NOT the single hinted index.
        Assert.Equal(new[] { 0, 1, 2, 3 }, coll.GetCalls);
    }

    [Fact]
    public void Out_Of_Range_Hint_Index_Is_Skipped_Safely()
    {
        // Defense-in-depth: the producer validates indices before publishing, but
        // a directly-supplied bad hint must not throw — out-of-range indices are
        // ignored and the in-range ones still update.
        const int n = 4;
        var oldChildren = ButtonCells(n);
        var newChildren = FreshCopyWithSameLabels(n);
        var coll = new TrackingChildCollection(n);
        var reconciler = new Reconciler();

        ChildDiffHints.Publish(newChildren, new ChildDiffHint(new[] { 2, 99 }, themeSensitiveCount: 0, previousChildren: oldChildren));
        var ex = Record.Exception(() =>
            ChildReconciler.Reconcile(oldChildren, newChildren, coll, reconciler, NoOp));

        Assert.Null(ex);
        Assert.Equal(new[] { 2 }, coll.GetCalls); // 99 ignored, 2 visited
        Assert.Empty(coll.Structural);
        // All 4 in-range elements end up skipped (nothing mutated): indices 0,1,3
        // via the structural skip + index 2 via CanSkipUpdate (its fresh copy is
        // value-equal). The fix bases the structural part on indices ACTUALLY
        // visited — common(4) - visited(1) = 3 — so the total (3 + 1) matches the
        // full walk. The old `common - changed.Length` = 2 undercounted the
        // untouched range (and with more out-of-range indices could go negative).
        Assert.Equal(4, reconciler.DebugElementsSkipped);
    }

    [Fact]
    public void Empty_Changed_Hint_Skips_Entire_Range()
    {
        const int n = 5;
        var oldChildren = ButtonCells(n);
        var newChildren = FreshCopyWithSameLabels(n);
        var coll = new TrackingChildCollection(n);
        var reconciler = new Reconciler();

        ChildDiffHints.Publish(newChildren, new ChildDiffHint(global::System.Array.Empty<int>(), themeSensitiveCount: 0, previousChildren: oldChildren));
        ChildReconciler.Reconcile(oldChildren, newChildren, coll, reconciler, NoOp);

        Assert.Empty(coll.GetCalls);       // nothing visited
        Assert.Empty(coll.Structural);     // nothing mutated
        Assert.Equal(n, reconciler.DebugElementsSkipped);
    }

    [Fact]
    public void Stale_Old_Array_Defeats_Fast_Path()
    {
        // The hint's ChangedIndices were diffed against a SPECIFIC prior array.
        // If the reconciler's old array is a different instance (e.g. an upstream
        // defensive copy), the indices can't be trusted, so the fast path must
        // fall back to the full walk rather than skip a possibly-stale cell.
        const int n = 5;
        var oldChildren = ButtonCells(n);
        var unrelatedPrev = ButtonCells(n); // NOT the array passed to Reconcile
        var newChildren = FreshCopyWithSameLabels(n);
        var coll = new TrackingChildCollection(n);
        var reconciler = new Reconciler();

        ChildDiffHints.Publish(newChildren,
            new ChildDiffHint(new[] { 1 }, themeSensitiveCount: 0, previousChildren: unrelatedPrev));
        ChildReconciler.Reconcile(oldChildren, newChildren, coll, reconciler, NoOp);

        // Old-array identity mismatch => full walk visits every common index.
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, coll.GetCalls);
        Assert.Empty(coll.Structural);
    }

    [Fact]
    public void Fast_Path_Output_Matches_Full_Walk_With_Reference_Equal_Reuse()
    {
        // Differential A/B mirroring the REAL producer: untouched indices REUSE the
        // previous render's element instances (reference-equal, as
        // UseMemoCellsByIndex does via children[i] = prevChildren[i]); only the
        // changed index is a fresh instance. The fast path (hint present) and the
        // full walk (no hint) must produce identical observable output. This is the
        // invariant the structural skip relies on — that an untouched cell is
        // reference-equal old<->new, so skipping its update is provably a no-op.
        const int n = 5;
        const int changedIdx = 2;

        static Element[] BuildNewReusing(Element[] prev)
        {
            // Reuse prev references everywhere except the one changed index, which is
            // rebuilt as a fresh (value-equal) instance — exactly the producer's shape.
            var arr = new Element[prev.Length];
            for (int i = 0; i < prev.Length; i++)
                arr[i] = i == changedIdx ? Button($"cell-{i}", NoOp) : prev[i];
            return arr;
        }

        // Full walk (no hint).
        var oldFull = ButtonCells(n);
        var newFull = BuildNewReusing(oldFull);
        var collFull = new TrackingChildCollection(n);
        var rFull = new Reconciler();
        ChildReconciler.Reconcile(oldFull, newFull, collFull, rFull, NoOp);

        // Fast path (hint present), identical inputs.
        var oldFast = ButtonCells(n);
        var newFast = BuildNewReusing(oldFast);
        var collFast = new TrackingChildCollection(n);
        var rFast = new Reconciler();
        ChildDiffHints.Publish(newFast,
            new ChildDiffHint(new[] { changedIdx }, themeSensitiveCount: 0, previousChildren: oldFast));
        ChildReconciler.Reconcile(oldFast, newFast, collFast, rFast, NoOp);

        // The reference-equality invariant the fast path trusts actually holds here.
        for (int i = 0; i < n; i++)
            if (i != changedIdx)
                Assert.Same(oldFast[i], newFast[i]);

        // Identical observable output: same total skip accounting, no structural
        // mutation on either path.
        Assert.Equal(rFull.DebugElementsSkipped, rFast.DebugElementsSkipped);
        Assert.Equal(n, rFull.DebugElementsSkipped);
        Assert.Empty(collFull.Structural);
        Assert.Empty(collFast.Structural);

        // The full walk visits every common index; the fast path visits only the
        // changed one and skips the reference-equal remainder wholesale. Because the
        // skipped cells are reference-equal, the full walk's per-cell work on them
        // (the callback Tag refresh) is a provable no-op, so the two paths converge.
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, collFull.GetCalls);
        Assert.Equal(new[] { changedIdx }, collFast.GetCalls);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Allocation-budget teeth for the structural-skip win (spec 034 §C, #699).
    //
    //  The behavioural tests above pin the VISIT COUNT (which indices the fast
    //  path reads). This pins the SAME elision as a GC-bytes budget — the shape
    //  the #692 / #665 regression guards use — so the measured StocksGrid
    //  allocation win cannot be silently reverted with the visit-count tests
    //  still green.
    //
    //  What the win is: for every UNTOUCHED cell the full positional walk issues
    //  a children.Get(i) read — a COM IVector.GetAt round-trip that marshals /
    //  projects a wrapper per call on a live control. The structural skip elides
    //  that read for the reference-equal untouched range. Headless there is no
    //  XAML core, so the real COM/marshaling cost cannot be incurred (Get returns
    //  null and allocates nothing). To make the elision measurable as managed
    //  bytes, the measuring collection below charges a fixed allocation per read,
    //  modeling the per-cell marshaling the skip avoids. The fast path then
    //  allocates O(changed); the full walk O(count); a reverted skip makes the
    //  hinted path walk every cell too, collapsing the two and failing the budget.

    /// <summary>
    /// An <see cref="IChildCollection"/> that charges a fixed managed allocation
    /// per <see cref="Get"/>, modeling the per-cell COM read / marshaling the
    /// structural skip elides for untouched cells (unmeasurable headless, where
    /// no live control exists). Returns null so no WinUI control is needed — the
    /// skip arm's <c>is FrameworkElement</c> guard tolerates it.
    /// </summary>
    private sealed class MeasuringChildCollection : IChildCollection
    {
        private readonly int _count;
        // Public field so the per-read allocation provably escapes: the JIT may
        // not elide a heap write to a reachable field, so each read is counted by
        // GetAllocatedBytesForCurrentThread.
        public object? LastRead;
        public int Reads { get; private set; }

        public MeasuringChildCollection(int count) => _count = count;

        public int Count => _count;
        public UIElement Get(int index)
        {
            Reads++;
            LastRead = new byte[48]; // models the elided per-cell COM read / marshaling
            return null!;
        }
        public void Insert(int index, UIElement element) { }
        public void RemoveAt(int index) { }
        public void Move(int oldIndex, int newIndex) { }
        public void Replace(int index, UIElement element) { }
    }

    [Fact]
    public void Structural_Skip_Pins_PerCell_Read_Elision_As_Allocation_Budget()
    {
        const int n = 500;            // StocksGrid cell count
        const int changedCount = 5;   // steady-state moderate churn
        const int warmup = 200;
        const int iterations = 1_000;

        // Steady-state producer shape: the new array REUSES the previous render's
        // element instances at untouched indices (reference-equal) and a fresh
        // value-equal copy at each changed index — exactly UseMemoCellsByIndex.
        int[] changedIdx = new int[changedCount];
        for (int k = 0; k < changedCount; k++)
            changedIdx[k] = (k + 1) * (n / (changedCount + 1));

        var oldFast = ButtonCells(n);
        var newFast = (Element[])oldFast.Clone();
        foreach (int idx in changedIdx)
            newFast[idx] = Button($"cell-{idx}", NoOp); // fresh, value-equal copy
        ChildDiffHints.Publish(newFast,
            new ChildDiffHint(changedIdx, themeSensitiveCount: 0, previousChildren: oldFast));

        // FAST PATH — hint present, so the positional walk visits only changedIdx.
        var fastColl = new MeasuringChildCollection(n);
        var rFast = new Reconciler();
        for (int i = 0; i < warmup; i++)
            ChildReconciler.Reconcile(oldFast, newFast, fastColl, rFast, NoOp);
        long fb = global::System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
            ChildReconciler.Reconcile(oldFast, newFast, fastColl, rFast, NoOp);
        long fastAlloc = global::System.GC.GetAllocatedBytesForCurrentThread() - fb;
        int fastReadsPerIter = fastColl.Reads / (warmup + iterations);

        // FULL WALK — identical inputs but NO published hint, so the fast path
        // cannot engage and every common index is read.
        var oldFull = ButtonCells(n);
        var newFull = (Element[])oldFull.Clone();
        foreach (int idx in changedIdx)
            newFull[idx] = Button($"cell-{idx}", NoOp);
        // (deliberately no ChildDiffHints.Publish for newFull — defeats the skip)
        var fullColl = new MeasuringChildCollection(n);
        var rFull = new Reconciler();
        for (int i = 0; i < warmup; i++)
            ChildReconciler.Reconcile(oldFull, newFull, fullColl, rFull, NoOp);
        long ub = global::System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
            ChildReconciler.Reconcile(oldFull, newFull, fullColl, rFull, NoOp);
        long fullAlloc = global::System.GC.GetAllocatedBytesForCurrentThread() - ub;
        int fullReadsPerIter = fullColl.Reads / (warmup + iterations);

        // Mechanism: the fast path reads ONLY the changed cells; the full walk
        // reads EVERY cell. This read-elision is exactly what the budget pins.
        Assert.Equal(changedCount, fastReadsPerIter);
        Assert.Equal(n, fullReadsPerIter);

        // Budget: the full walk allocates ~n/changedCount× (≈100×) the fast path.
        // Require a wide ≥8× margin — robust to measurement noise, yet the test
        // FAILS if the structural skip is disabled/reverted, because the hinted
        // path then walks every cell too and fastAlloc collapses onto fullAlloc.
        Assert.True(fullAlloc > fastAlloc * 8,
            $"Structural skip no longer cuts the per-cell read allocation: " +
            $"fast={fastAlloc}B ({fastReadsPerIter} reads/iter), " +
            $"full={fullAlloc}B ({fullReadsPerIter} reads/iter) over {iterations} iters. " +
            $"Expected full > 8x fast; a reverted skip makes them equal.");
    }
}
