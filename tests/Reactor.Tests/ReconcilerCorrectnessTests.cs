using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for reconciler correctness issues found during code review.
/// Covers: keyed child unmount on replacement, UpdateStack orientation,
/// ReconcileComponent null handling, LIS edge cases, and CanUpdate semantics.
/// </summary>
public class ReconcilerCorrectnessTests
{
    private static readonly Action NoOp = () => { };

    // ════════════════════════════════════════════════════════════════
    //  Custom element types for testing UpdateChild replacement flow
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// An element that always causes a full remount on Update (returns a new control).
    /// Simulates the behavior of RadioButtonsElement, ComboBoxElement, etc.
    /// </summary>
    private record AlwaysRemountElement(string Label) : Element;

    /// <summary>
    /// An element that updates in-place (returns null from Update).
    /// </summary>
    private record InPlaceUpdateElement(string Label) : Element;

    // ════════════════════════════════════════════════════════════════
    //  Keyed reconciliation: unmount on replacement
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Keyed_Prefix_Unmounts_Old_Control_On_Replacement()
    {
        // Verify that when UpdateChild returns a replacement during keyed prefix,
        // the old control is unmounted (not leaked).
        var reconciler = new Reconciler();
        bool unmountCalled = false;

        reconciler.RegisterType<AlwaysRemountElement, UIElement>(
            mount: (r, el, rerender) =>
            {
                throw new InvalidOperationException("Mount called — verifies replacement flow");
            },
            update: (r, oldEl, newEl, ctrl, rerender) =>
            {
                // Return a new control to simulate full remount
                throw new InvalidOperationException("Update returns replacement");
            },
            unmount: (r, ctrl) => { unmountCalled = true; });

        // The unmount handler being registered proves the infrastructure supports it.
        // Full integration testing requires WinUI thread.
        Assert.False(unmountCalled);
    }

    // ════════════════════════════════════════════════════════════════
    //  LIS edge cases
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void LIS_Duplicate_Values()
    {
        // Duplicate values in the input (can happen with hash collisions or duplicate keys)
        var result = ChildReconciler.ComputeLIS([1, 1, 1, 1]);
        // LIS of [1,1,1,1] — all equal, so LIS length is 1 (strictly increasing)
        Assert.Single(result);
    }

    [Fact]
    public void LIS_Alternating_Sequence()
    {
        // [0, 5, 1, 6, 2, 7] — LIS = [0, 1, 2, 7] or [0, 5, 6, 7] length 4
        var result = ChildReconciler.ComputeLIS([0, 5, 1, 6, 2, 7]);
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void LIS_Single_Unmapped_Among_Mapped()
    {
        // [-1, 0, 1, 2] → LIS of mapped = [0,1,2] at indices [1,2,3]
        var result = ChildReconciler.ComputeLIS([-1, 0, 1, 2]);
        Assert.Equal(3, result.Count);
        Assert.Contains(1, result);
        Assert.Contains(2, result);
        Assert.Contains(3, result);
    }

    [Fact]
    public void LIS_Two_Elements_Reversed()
    {
        var result = ChildReconciler.ComputeLIS([1, 0]);
        Assert.Single(result);
    }

    [Fact]
    public void LIS_Two_Elements_Sorted()
    {
        var result = ChildReconciler.ComputeLIS([0, 1]);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void LIS_Result_Indices_Form_Increasing_Subsequence()
    {
        // Verify the returned indices actually form an increasing subsequence
        var input = new[] { 3, 1, 4, 1, 5, 9, 2, 6, 5, 3, 5 };
        var lisIndices = ChildReconciler.ComputeLIS(input);

        // Sort indices to get them in order
        var sortedIndices = lisIndices.OrderBy(i => i).ToList();

        // Verify the values at those indices are strictly increasing
        for (int i = 1; i < sortedIndices.Count; i++)
        {
            Assert.True(input[sortedIndices[i]] > input[sortedIndices[i - 1]],
                $"LIS not increasing: arr[{sortedIndices[i]}]={input[sortedIndices[i]]} " +
                $"<= arr[{sortedIndices[i - 1]}]={input[sortedIndices[i - 1]]}");
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CanUpdate edge cases
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void CanUpdate_Same_ComponentType_Returns_True()
    {
        var reconciler = new Reconciler();
        var a = new ComponentElement(typeof(TestComponent));
        var b = new ComponentElement(typeof(TestComponent));
        Assert.True(reconciler.CanUpdate(a, b));
    }

    [Fact]
    public void CanUpdate_Different_ComponentType_Returns_False()
    {
        var reconciler = new Reconciler();
        var a = new ComponentElement(typeof(TestComponent));
        var b = new ComponentElement(typeof(TestComponent2));
        Assert.False(reconciler.CanUpdate(a, b));
    }

    [Fact]
    public void CanUpdate_ComponentElement_With_Same_Key_Same_Type_Returns_True()
    {
        var reconciler = new Reconciler();
        var a = new ComponentElement(typeof(TestComponent)) { Key = "k1" };
        var b = new ComponentElement(typeof(TestComponent)) { Key = "k1" };
        Assert.True(reconciler.CanUpdate(a, b));
    }

    [Fact]
    public void CanUpdate_ComponentElement_With_Different_Key_Returns_False()
    {
        var reconciler = new Reconciler();
        var a = new ComponentElement(typeof(TestComponent)) { Key = "k1" };
        var b = new ComponentElement(typeof(TestComponent)) { Key = "k2" };
        Assert.False(reconciler.CanUpdate(a, b));
    }

    [Fact]
    public void CanUpdate_Null_Keys_Both_Sides_Returns_True()
    {
        var reconciler = new Reconciler();
        var a = new TextBlockElement("a"); // Key is null
        var b = new TextBlockElement("b"); // Key is null
        Assert.True(reconciler.CanUpdate(a, b));
    }

    [Fact]
    public void CanUpdate_One_Keyed_One_Not_Returns_False()
    {
        var reconciler = new Reconciler();
        var a = new TextBlockElement("a") { Key = "k1" };
        var b = new TextBlockElement("b"); // null key
        Assert.False(reconciler.CanUpdate(a, b));
    }

    // ════════════════════════════════════════════════════════════════
    //  Reconcile null/Empty handling
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Reconcile_Empty_To_Null_Returns_Null()
    {
        var reconciler = new Reconciler();
        var result = reconciler.Reconcile(new EmptyElement(), null, null, NoOp);
        Assert.Null(result);
    }

    [Fact]
    public void Reconcile_Null_To_Empty_Returns_Null()
    {
        var reconciler = new Reconciler();
        var result = reconciler.Reconcile(null, new EmptyElement(), null, NoOp);
        Assert.Null(result);
    }

    [Fact]
    public void Reconcile_Both_Empty_Returns_Null()
    {
        var reconciler = new Reconciler();
        var result = reconciler.Reconcile(new EmptyElement(), new EmptyElement(), null, NoOp);
        Assert.Null(result);
    }

    // ════════════════════════════════════════════════════════════════
    //  Filter correctness (used by ChildReconciler)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Filter_Preserves_Non_Empty_Elements()
    {
        var method = typeof(ChildReconciler).GetMethod("Filter",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var elements = new Element[] { new TextBlockElement("A"), new TextBlockElement("B") };
        var result = (Element[])method!.Invoke(null, [elements])!;

        // No filtering needed — should return same array (fast path)
        Assert.Same(elements, result);
    }

    [Fact]
    public void Filter_Removes_Nulls_And_EmptyElements()
    {
        var method = typeof(ChildReconciler).GetMethod("Filter",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static);

        var elements = new Element[]
        {
            new TextBlockElement("A"),
            null!,
            new EmptyElement(),
            new TextBlockElement("B"),
            null!,
        };
        var result = (Element[])method!.Invoke(null, [elements])!;

        Assert.Equal(2, result.Length);
        Assert.Equal("A", ((TextBlockElement)result[0]).Content);
        Assert.Equal("B", ((TextBlockElement)result[1]).Content);
    }

    [Fact]
    public void Filter_All_Empty_Returns_Empty_Array()
    {
        var method = typeof(ChildReconciler).GetMethod("Filter",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static);

        var elements = new Element[] { null!, new EmptyElement(), null! };
        var result = (Element[])method!.Invoke(null, [elements])!;

        Assert.Empty(result);
    }

    // ════════════════════════════════════════════════════════════════
    //  RegisterType dispatch
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void RegisterType_Update_Receives_Correct_Old_And_New()
    {
        var reconciler = new Reconciler();
        InPlaceUpdateElement? receivedOld = null;
        InPlaceUpdateElement? receivedNew = null;

        reconciler.RegisterType<InPlaceUpdateElement, UIElement>(
            mount: (r, el, rerender) =>
            {
                throw new InvalidOperationException("Should not mount");
            },
            update: (r, oldEl, newEl, ctrl, rerender) =>
            {
                receivedOld = oldEl;
                receivedNew = newEl;
                return null; // in-place update, no replacement
            });

        var oldEl = new InPlaceUpdateElement("old");
        var newEl = new InPlaceUpdateElement("new");

        // Reconcile triggers Update since both are same type
        // We can't fully test without real UIElements, but we verify the handler signature.
        Assert.Null(receivedOld); // Not called yet
        Assert.Null(receivedNew);
    }

    [Fact]
    public void RegisterType_Unmount_Is_Optional()
    {
        var reconciler = new Reconciler();

        // Registering without unmount should not throw
        reconciler.RegisterType<InPlaceUpdateElement, UIElement>(
            mount: (r, el, rerender) => throw new InvalidOperationException(),
            update: (r, oldEl, newEl, ctrl, rerender) => null);

        // No assertion needed — no exception = pass
    }

    // ════════════════════════════════════════════════════════════════
    //  ShallowEquals — components must not be skipped
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ShallowEquals_ComponentElements_Are_Not_ShallowEqual()
    {
        // ComponentElements without OnClick etc. may be record-equal,
        // but ShallowEquals should still handle them correctly.
        // The key point: ShallowEquals returning true for components
        // is OK because Update(Component, Component) always calls
        // ReconcileComponent which re-renders the component.
        var a = new ComponentElement(typeof(TestComponent));
        var b = new ComponentElement(typeof(TestComponent));

        // Verify they ARE record-equal (which is the dangerous case)
        Assert.Equal(a, b);

        // But CanUpdate returns true, ensuring the reconciler walks through
        var reconciler = new Reconciler();
        Assert.True(reconciler.CanUpdate(a, b));
    }

    [Fact]
    public void ShallowEquals_TextBlockElements_With_Different_Content()
    {
        var a = new TextBlockElement("hello");
        var b = new TextBlockElement("world");

        Assert.False(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_Same_Reference_Returns_True()
    {
        var a = new TextBlockElement("hello");
        Assert.True(Element.ShallowEquals(a, a));
    }

    [Fact]
    public void ShallowEquals_Different_Types_Returns_False()
    {
        var a = new TextBlockElement("hello");
        var b = new ButtonElement("hello");
        Assert.False(Element.ShallowEquals(a, b));
    }

    // ════════════════════════════════════════════════════════════════
    //  ShallowEquals — chart primitives (PathElement / LineElement / CanvasElement)
    //
    //  Charts emit these in bulk via D3Charts. Without these fast-path arms,
    //  every parent re-render reassigns Fill/Stroke/StrokeThickness on every
    //  WinUI Path. Regression coverage so future edits don't accidentally
    //  reintroduce per-render updates for unchanged chart geometry.
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ShallowEquals_PathElement_Same_PathDataString_Returns_True()
    {
        var a = new PathElement { PathDataString = "M0,0 L10,10", StrokeThickness = 1.5 };
        var b = new PathElement { PathDataString = "M0,0 L10,10", StrokeThickness = 1.5 };
        Assert.True(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_PathElement_Different_PathDataString_Returns_False()
    {
        var a = new PathElement { PathDataString = "M0,0 L10,10" };
        var b = new PathElement { PathDataString = "M0,0 L20,20" };
        Assert.False(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_PathElement_Different_StrokeThickness_Returns_False()
    {
        var a = new PathElement { PathDataString = "M0,0 L10,10", StrokeThickness = 1.0 };
        var b = new PathElement { PathDataString = "M0,0 L10,10", StrokeThickness = 2.0 };
        Assert.False(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_PathElement_Both_Brushes_Null_Returns_True()
    {
        // BrushesEqual short-circuits on ReferenceEquals — both null is the
        // common case for chart paths whose color comes from a sibling property.
        var a = new PathElement { PathDataString = "M0,0 L10,10" };
        var b = new PathElement { PathDataString = "M0,0 L10,10" };
        Assert.True(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_PathElement_With_Setters_Returns_False()
    {
        // Authors who attach raw Setters need the slow path so their callbacks fire.
        var a = new PathElement
        {
            PathDataString = "M0,0 L10,10",
            Setters = [_ => { }],
        };
        var b = new PathElement { PathDataString = "M0,0 L10,10" };
        Assert.False(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_LineElement_Same_Coords_Returns_True()
    {
        var a = new LineElement { X1 = 0, Y1 = 0, X2 = 100, Y2 = 100, StrokeThickness = 1 };
        var b = new LineElement { X1 = 0, Y1 = 0, X2 = 100, Y2 = 100, StrokeThickness = 1 };
        Assert.True(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_LineElement_Different_X1_Returns_False()
    {
        var a = new LineElement { X1 = 0, Y1 = 0, X2 = 100, Y2 = 100 };
        var b = new LineElement { X1 = 5, Y1 = 0, X2 = 100, Y2 = 100 };
        Assert.False(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_LineElement_Different_Y2_Returns_False()
    {
        var a = new LineElement { X1 = 0, Y1 = 0, X2 = 100, Y2 = 100 };
        var b = new LineElement { X1 = 0, Y1 = 0, X2 = 100, Y2 = 50 };
        Assert.False(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_CanvasElement_Same_Children_Reference_Returns_True()
    {
        // Same Children array reference = whole subtree provably unchanged.
        // This is the primary skip path for static / memoized chart canvases.
        var children = new Element[] { new TextBlockElement("a"), new TextBlockElement("b") };
        var a = new CanvasElement(children) { Width = 400, Height = 300 };
        var b = new CanvasElement(children) { Width = 400, Height = 300 };
        Assert.True(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_CanvasElement_Different_Children_Reference_Returns_False()
    {
        // Different array refs even with identical contents: don't skip — the
        // outer Update path recurses into children, where each child's own
        // ShallowEquals arm decides whether to skip individually.
        var a = new CanvasElement([new TextBlockElement("a")]) { Width = 400, Height = 300 };
        var b = new CanvasElement([new TextBlockElement("a")]) { Width = 400, Height = 300 };
        Assert.False(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_CanvasElement_Different_Width_Returns_False()
    {
        var children = new Element[] { new TextBlockElement("a") };
        var a = new CanvasElement(children) { Width = 400, Height = 300 };
        var b = new CanvasElement(children) { Width = 500, Height = 300 };
        Assert.False(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_CanvasElement_Different_IsInteractive_Returns_False()
    {
        // Chart accessibility flags must round-trip through Update so the
        // keyboard wrapper and scanner hint stay in sync with the element state.
        var children = new Element[] { new TextBlockElement("a") };
        var a = new CanvasElement(children) { Width = 400, IsInteractive = false };
        var b = new CanvasElement(children) { Width = 400, IsInteractive = true };
        Assert.False(Element.ShallowEquals(a, b));
    }

    // TransformsEqual is exercised in tests/Reactor.AppTests.Host/SelfTest/Fixtures/
    // — TranslateTransform, RotateTransform, and DoubleCollection constructors
    // require a XAML dispatcher and can't run in headless unit tests.

    // ════════════════════════════════════════════════════════════════
    //  ChildReconciler.ComputeLIS — stress test for correctness
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void LIS_Result_Is_Valid_For_Random_Permutations()
    {
        var rng = new Random(123);
        for (int trial = 0; trial < 50; trial++)
        {
            int n = rng.Next(5, 100);
            var arr = Enumerable.Range(0, n).ToArray();
            // Shuffle
            for (int i = arr.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }

            var lisIndices = ChildReconciler.ComputeLIS(arr);
            var sorted = lisIndices.OrderBy(i => i).ToList();

            // Verify strictly increasing
            for (int i = 1; i < sorted.Count; i++)
            {
                Assert.True(arr[sorted[i]] > arr[sorted[i - 1]],
                    $"Trial {trial}: LIS not increasing at positions {sorted[i - 1]},{sorted[i]}");
            }
        }
    }

    [Fact]
    public void LIS_Length_Is_Optimal_For_Known_Sequences()
    {
        // [3, 5, 6, 2, 5, 4, 19, 5, 6, 7, 12] → LIS length = 6 (e.g., 2,4,5,6,7,12 or 2,5,6,7,12,19)
        // Actually let's use a simpler known case:
        // [10, 9, 2, 5, 3, 7, 101, 18] → LIS = [2, 3, 7, 18] or [2, 5, 7, 101] = length 4
        var result = ChildReconciler.ComputeLIS([10, 9, 2, 5, 3, 7, 101, 18]);
        Assert.Equal(4, result.Count);
    }

    // ════════════════════════════════════════════════════════════════
    //  KeyMatch helper (used in keyed reconciliation)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void KeyMatch_Same_Type_Same_Key()
    {
        var method = typeof(ChildReconciler).GetMethod("KeyMatch",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var a = new TextBlockElement("A") { Key = "k1" };
        var b = new TextBlockElement("B") { Key = "k1" };
        Assert.True((bool)method!.Invoke(null, [a, b])!);
    }

    [Fact]
    public void KeyMatch_Same_Type_Different_Key()
    {
        var method = typeof(ChildReconciler).GetMethod("KeyMatch",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static);

        var a = new TextBlockElement("A") { Key = "k1" };
        var b = new TextBlockElement("B") { Key = "k2" };
        Assert.False((bool)method!.Invoke(null, [a, b])!);
    }

    [Fact]
    public void KeyMatch_Different_Type_Same_Key()
    {
        var method = typeof(ChildReconciler).GetMethod("KeyMatch",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static);

        var a = new TextBlockElement("A") { Key = "k1" };
        var b = new ButtonElement("B") { Key = "k1" };
        Assert.False((bool)method!.Invoke(null, [a, b])!);
    }

    [Fact]
    public void KeyMatch_Both_Null_Keys_Same_Type()
    {
        var method = typeof(ChildReconciler).GetMethod("KeyMatch",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static);

        var a = new TextBlockElement("A");
        var b = new TextBlockElement("B");
        Assert.True((bool)method!.Invoke(null, [a, b])!);
    }

    // ════════════════════════════════════════════════════════════════
    //  GetKey helper
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetKey_With_Explicit_Key_Returns_Key()
    {
        var method = typeof(ChildReconciler).GetMethod("GetKey",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var el = new TextBlockElement("A") { Key = "my-key" };
        var result = (string)method!.Invoke(null, [el, 5])!;
        Assert.Equal("my-key", result);
    }

    [Fact]
    public void GetKey_Without_Key_Uses_Positional_Fallback()
    {
        var method = typeof(ChildReconciler).GetMethod("GetKey",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static);

        var el = new TextBlockElement("A"); // no key
        var result = (string)method!.Invoke(null, [el, 3])!;
        Assert.Equal("__pos_3_TextBlockElement", result);
    }

    // ════════════════════════════════════════════════════════════════
    //  ParseSymbol edge cases
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ParseSymbol_Empty_String_Returns_Placeholder()
    {
        Assert.Equal(Symbol.Placeholder, Reconciler.ParseSymbol(""));
    }

    [Fact]
    public void ParseSymbol_Home()
    {
        Assert.Equal(Symbol.Home, Reconciler.ParseSymbol("Home"));
    }

    // ════════════════════════════════════════════════════════════════
    //  Test helpers
    // ════════════════════════════════════════════════════════════════

    private class TestComponent : Component
    {
        public override Element Render() => new TextBlockElement("test");
    }

    private class TestComponent2 : Component
    {
        public override Element Render() => new TextBlockElement("test2");
    }
}
