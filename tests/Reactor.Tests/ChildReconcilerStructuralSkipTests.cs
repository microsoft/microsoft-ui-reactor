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

        ChildDiffHints.Publish(newChildren, new ChildDiffHint(new[] { 1, 4 }, themeSensitiveCount: 0));
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
        var newChildren = FreshCopyWithSameLabels(n);
        var collFast = new TrackingChildCollection(n);
        var rFast = new Reconciler();
        ChildDiffHints.Publish(newChildren, new ChildDiffHint(new[] { 2 }, themeSensitiveCount: 0));
        ChildReconciler.Reconcile(ButtonCells(n), newChildren, collFast, rFast, NoOp);

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

        ChildDiffHints.Publish(newChildren, new ChildDiffHint(new[] { 1 }, themeSensitiveCount: 1));
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

        ChildDiffHints.Publish(newChildren, new ChildDiffHint(new[] { 1 }, themeSensitiveCount: 0));
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

        ChildDiffHints.Publish(newChildren, new ChildDiffHint(new[] { 2, 99 }, themeSensitiveCount: 0));
        var ex = Record.Exception(() =>
            ChildReconciler.Reconcile(oldChildren, newChildren, coll, reconciler, NoOp));

        Assert.Null(ex);
        Assert.Equal(new[] { 2 }, coll.GetCalls); // 99 ignored, 2 visited
        Assert.Empty(coll.Structural);
    }

    [Fact]
    public void Empty_Changed_Hint_Skips_Entire_Range()
    {
        const int n = 5;
        var oldChildren = ButtonCells(n);
        var newChildren = FreshCopyWithSameLabels(n);
        var coll = new TrackingChildCollection(n);
        var reconciler = new Reconciler();

        ChildDiffHints.Publish(newChildren, new ChildDiffHint(global::System.Array.Empty<int>(), themeSensitiveCount: 0));
        ChildReconciler.Reconcile(oldChildren, newChildren, coll, reconciler, NoOp);

        Assert.Empty(coll.GetCalls);       // nothing visited
        Assert.Empty(coll.Structural);     // nothing mutated
        Assert.Equal(n, reconciler.DebugElementsSkipped);
    }
}
