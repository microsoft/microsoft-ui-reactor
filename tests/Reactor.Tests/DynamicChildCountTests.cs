using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Regression tests for dynamic child count changes during reconciliation.
/// Reproduces the crash scenario where a docking window manager adds/removes
/// splits, causing the virtual tree child count to diverge from the live
/// control tree. The original bug was in UpdateGrid which did inline
/// positional add/remove instead of delegating to ChildReconciler.
///
/// These unit tests verify element-level preconditions and algorithm
/// correctness. Full integration tests with real WinUI controls live in
/// the SelfTestBatch (Grid_DynamicChildCount fixture in Reactor.AppTests).
/// </summary>
public class DynamicChildCountTests
{
    private static readonly Action NoOp = () => { };

    // ════════════════════════════════════════════════════════════════
    //  1. CanUpdate — containers with different child counts must be
    //     update-compatible so the reconciler walks into Update instead
    //     of doing a full remount.
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void CanUpdate_Grid_DifferentChildCounts_Returns_True()
    {
        var reconciler = new Reconciler();
        var oldEl = Grid([GridSize.Star()], [GridSize.Star()], TextBlock("A"));
        var newEl = Grid([GridSize.Star(), GridSize.Star()], [GridSize.Star()], TextBlock("A"), TextBlock("B"));
        Assert.True(reconciler.CanUpdate(oldEl, newEl));
    }

    [Fact]
    public void CanUpdate_Grid_GrowFromEmpty_Returns_True()
    {
        var reconciler = new Reconciler();
        var oldEl = Grid([GridSize.Star()], [GridSize.Star()]);
        var newEl = Grid([GridSize.Star(), GridSize.Star()], [GridSize.Star()], TextBlock("A"), TextBlock("B"));
        Assert.True(reconciler.CanUpdate(oldEl, newEl));
    }

    [Fact]
    public void CanUpdate_Grid_ShrinkToEmpty_Returns_True()
    {
        var reconciler = new Reconciler();
        var oldEl = Grid([GridSize.Star(), GridSize.Star()], [GridSize.Star()], TextBlock("A"), TextBlock("B"));
        var newEl = Grid([GridSize.Star()], [GridSize.Star()]);
        Assert.True(reconciler.CanUpdate(oldEl, newEl));
    }

    [Fact]
    public void CanUpdate_Stack_DifferentChildCounts_Returns_True()
    {
        var reconciler = new Reconciler();
        var oldEl = VStack(TextBlock("A"));
        var newEl = VStack(TextBlock("A"), TextBlock("B"));
        Assert.True(reconciler.CanUpdate(oldEl, newEl));
    }

    [Fact]
    public void CanUpdate_Stack_GrowFromEmpty_Returns_True()
    {
        var reconciler = new Reconciler();
        var oldEl = VStack();
        var newEl = VStack(TextBlock("A"), TextBlock("B"));
        Assert.True(reconciler.CanUpdate(oldEl, newEl));
    }

    [Fact]
    public void CanUpdate_Flex_DifferentChildCounts_Returns_True()
    {
        var reconciler = new Reconciler();
        var oldEl = FlexRow(TextBlock("A"));
        var newEl = FlexRow(TextBlock("A"), TextBlock("B"));
        Assert.True(reconciler.CanUpdate(oldEl, newEl));
    }

    [Fact]
    public void CanUpdate_WrapGrid_DifferentChildCounts_Returns_True()
    {
        var reconciler = new Reconciler();
        var oldEl = WrapGrid(TextBlock("A"));
        var newEl = WrapGrid(TextBlock("A"), TextBlock("B"));
        Assert.True(reconciler.CanUpdate(oldEl, newEl));
    }

    [Fact]
    public void CanUpdate_Canvas_DifferentChildCounts_Returns_True()
    {
        var reconciler = new Reconciler();
        var oldEl = Canvas(TextBlock("A"));
        var newEl = Canvas(TextBlock("A"), TextBlock("B"));
        Assert.True(reconciler.CanUpdate(oldEl, newEl));
    }

    // ════════════════════════════════════════════════════════════════
    //  2. Type transitions must NOT be update-compatible — forces
    //     unmount/remount when a pane type changes (e.g., Text → Grid
    //     when splitting, or Grid → Text when collapsing).
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void CanUpdate_TextToGrid_Returns_False()
    {
        var reconciler = new Reconciler();
        Assert.False(reconciler.CanUpdate(TextBlock("A"), Grid([GridSize.Star()], [GridSize.Star()], TextBlock("A"))));
    }

    [Fact]
    public void CanUpdate_GridToText_Returns_False()
    {
        var reconciler = new Reconciler();
        Assert.False(reconciler.CanUpdate(Grid([GridSize.Star()], [GridSize.Star()], TextBlock("A")), TextBlock("A")));
    }

    [Fact]
    public void CanUpdate_StackToFlex_Returns_False()
    {
        var reconciler = new Reconciler();
        Assert.False(reconciler.CanUpdate(VStack(TextBlock("A")), FlexRow(TextBlock("A"))));
    }

    [Fact]
    public void CanUpdate_GridToStack_Returns_False()
    {
        var reconciler = new Reconciler();
        Assert.False(reconciler.CanUpdate(
            Grid([GridSize.Star()], [GridSize.Star()], TextBlock("A")),
            VStack(TextBlock("A"))));
    }

    [Fact]
    public void CanUpdate_CanvasToGrid_Returns_False()
    {
        var reconciler = new Reconciler();
        Assert.False(reconciler.CanUpdate(
            Canvas(TextBlock("A")),
            Grid([GridSize.Star()], [GridSize.Star()], TextBlock("A"))));
    }

    // ════════════════════════════════════════════════════════════════
    //  3. Grid element tree structure — child count divergence
    //     produces structurally distinct elements, ensuring the
    //     reconciler detects the change and enters the delegation path.
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Grid_DifferentChildCounts_Are_Not_Equal()
    {
        var el1 = Grid([GridSize.Star()], [GridSize.Star()], TextBlock("A"));
        var el2 = Grid([GridSize.Star(), GridSize.Star()], [GridSize.Star()], TextBlock("A"), TextBlock("B"));
        Assert.NotEqual(el1, el2);
    }

    [Fact]
    public void Grid_SameChildCount_DifferentContent_Are_Not_Equal()
    {
        var el1 = Grid([GridSize.Star(), GridSize.Star()], [GridSize.Star()], TextBlock("A"), TextBlock("B"));
        var el2 = Grid([GridSize.Star(), GridSize.Star()], [GridSize.Star()], TextBlock("A"), TextBlock("C"));
        Assert.NotEqual(el1, el2);
    }

    [Fact]
    public void Grid_Same_Reference_Is_Equal()
    {
        var el = Grid([GridSize.Star(), GridSize.Star()], [GridSize.Star()], TextBlock("A"), TextBlock("B"));
        Assert.Equal(el, el);
    }

    [Fact]
    public void Stack_DifferentChildCounts_Are_Not_Equal()
    {
        var el1 = VStack(TextBlock("A"));
        var el2 = VStack(TextBlock("A"), TextBlock("B"));
        Assert.NotEqual(el1, el2);
    }

    [Fact]
    public void Flex_DifferentChildCounts_Are_Not_Equal()
    {
        var el1 = FlexRow(TextBlock("A"));
        var el2 = FlexRow(TextBlock("A"), TextBlock("B"));
        Assert.NotEqual(el1, el2);
    }

    // ════════════════════════════════════════════════════════════════
    //  4. ChildReconciler structural operations — verify the algorithm
    //     produces correct Insert/RemoveAt/Move for child count changes.
    //     Uses MockChildCollection (no WinUI controls needed).
    //     Limited to scenarios that don't call reconciler.Mount or
    //     children.Get (empty→empty, null filtering, etc.)
    // ════════════════════════════════════════════════════════════════

    private class MockChildCollection : IChildCollection
    {
        private readonly List<string> _items;
        public List<string> Operations { get; } = new();

        public MockChildCollection(params string[] items) => _items = new List<string>(items);

        public int Count => _items.Count;
        public Microsoft.UI.Xaml.UIElement Get(int index) =>
            throw new InvalidOperationException($"Get({index}) - mock");
        public void Insert(int index, Microsoft.UI.Xaml.UIElement element)
        {
            Operations.Add($"Insert({index})");
            _items.Insert(index, $"new-{_items.Count}");
        }
        public void RemoveAt(int index)
        {
            Operations.Add($"RemoveAt({index})");
            if (index < _items.Count) _items.RemoveAt(index);
        }
        public void Move(int oldIndex, int newIndex)
        {
            Operations.Add($"Move({oldIndex},{newIndex})");
            if (oldIndex < _items.Count)
            {
                var item = _items[oldIndex];
                _items.RemoveAt(oldIndex);
                _items.Insert(Math.Min(newIndex, _items.Count), item);
            }
        }
        public void Replace(int index, Microsoft.UI.Xaml.UIElement element) =>
            Operations.Add($"Replace({index})");
        public IReadOnlyList<string> Items => _items;
    }

    [Fact]
    public void Reconcile_NullsFiltered_GrowFromEmpty_Produces_No_Ops()
    {
        // New children are all null → filters to empty → no operations
        var mock = new MockChildCollection();
        var reconciler = new Reconciler();
        ChildReconciler.Reconcile(
            Array.Empty<Element>(),
            new Element[] { null!, null!, null! },
            mock, reconciler, NoOp);
        Assert.Empty(mock.Operations);
    }

    [Fact]
    public void Reconcile_EmptyElementsFiltered_ShrinkProducesNoOps()
    {
        // Old and new both filter to empty → no operations
        var mock = new MockChildCollection();
        var reconciler = new Reconciler();
        ChildReconciler.Reconcile(
            new Element[] { new EmptyElement(), new EmptyElement() },
            new Element[] { new EmptyElement() },
            mock, reconciler, NoOp);
        Assert.Empty(mock.Operations);
    }

    [Fact]
    public void Reconcile_OldNullsToNewNulls_NoOps()
    {
        // Both sides all null after filtering → nothing to do
        var mock = new MockChildCollection();
        var reconciler = new Reconciler();
        ChildReconciler.Reconcile(
            new Element[] { null!, null! },
            new Element[] { null!, null!, null! },
            mock, reconciler, NoOp);
        Assert.Empty(mock.Operations);
    }

    [Fact]
    public void Reconcile_OldEmpty_NewEmpty_NoOps()
    {
        var mock = new MockChildCollection();
        var reconciler = new Reconciler();
        ChildReconciler.Reconcile(
            Array.Empty<Element>(),
            Array.Empty<Element>(),
            mock, reconciler, NoOp);
        Assert.Empty(mock.Operations);
    }

    // ════════════════════════════════════════════════════════════════
    //  5. LIS algorithm — verify correctness for docking-like
    //     reorder patterns (pane rearrangement).
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void LIS_DockingReorder_ThreePanes()
    {
        // [A, B, C] reordered to [C, A, B]
        // Old indices in new order: [2, 0, 1]
        // LIS of [2, 0, 1] = [0, 1] at indices [1, 2] → length 2
        // Only pane C needs to move, A and B stay in relative order.
        var result = ChildReconciler.ComputeLIS([2, 0, 1]);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void LIS_DockingReorder_FourPanes_ReverseMiddle()
    {
        // [A, B, C, D] reordered to [A, D, C, B]
        // Old indices in new order: [0, 3, 2, 1]
        // LIS of [0, 3, 2, 1] = [0, 3] or [0, 2] → length 2
        var result = ChildReconciler.ComputeLIS([0, 3, 2, 1]);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void LIS_DockingNoReorder_AllInPlace()
    {
        // [A, B, C, D] → [A, B, C, D] (no change)
        // Old indices: [0, 1, 2, 3] — fully increasing
        var result = ChildReconciler.ComputeLIS([0, 1, 2, 3]);
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void LIS_DockingWithNewPanes()
    {
        // [A, B, C] → [A, X, B, Y, C] where X, Y are new
        // Old indices: [0, -1, 1, -1, 2] — -1 means new/unmapped
        // LIS of mapped values: [0, 1, 2] → all in place, no moves needed
        var result = ChildReconciler.ComputeLIS([-1, 0, -1, 1, -1, 2]);
        Assert.Equal(3, result.Count);
    }
}
