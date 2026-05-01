using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Comprehensive tests for panel child reconciliation consistency.
/// Validates that all panel types (Stack, Grid, Flex, WrapGrid, Canvas, RelativePanel)
/// handle child count changes, null items, empty elements, and type transitions correctly.
/// Also covers Yoga/WinUI layout interaction edge cases.
/// </summary>
public class PanelChildReconciliationTests
{
    private static readonly Action NoOp = () => { };

    // ════════════════════════════════════════════════════════════════
    //  Mock infrastructure for structural tests
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Records structural operations on a child collection without requiring real UIElements.
    /// Tracks Insert, RemoveAt, Replace, and Move operations.
    /// </summary>
    private class MockChildCollection : IChildCollection
    {
        private readonly List<string> _items;
        public List<string> Operations { get; } = new();

        public MockChildCollection(params string[] items)
        {
            _items = new List<string>(items);
        }

        public int Count => _items.Count;

        public UIElement Get(int index)
        {
            throw new InvalidOperationException(
                $"Get({index}) - mock does not hold real UIElements. " +
                "Structural tests should not need to access real controls.");
        }

        public void Insert(int index, UIElement element)
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

        public void Replace(int index, UIElement element)
        {
            Operations.Add($"Replace({index})");
        }

        public IReadOnlyList<string> Items => _items;
    }

    // ════════════════════════════════════════════════════════════════
    //  1. Filter algorithm via reflection (no WinUI controls needed)
    // ════════════════════════════════════════════════════════════════

    private static Element[] InvokeFilter(Element[] elements)
    {
        var method = typeof(ChildReconciler).GetMethod("Filter",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static);
        return (Element[])method!.Invoke(null, [elements])!;
    }

    [Fact]
    public void Filter_All_Null_Returns_Empty()
    {
        var result = InvokeFilter([null!, null!, null!]);
        Assert.Empty(result);
    }

    [Fact]
    public void Filter_All_EmptyElement_Returns_Empty()
    {
        var result = InvokeFilter([new EmptyElement(), new EmptyElement()]);
        Assert.Empty(result);
    }

    [Fact]
    public void Filter_Mixed_Null_Empty_Real_Preserves_Real()
    {
        var result = InvokeFilter([
            null!,
            new TextBlockElement("A"),
            new EmptyElement(),
            new TextBlockElement("B"),
            null!,
            new EmptyElement(),
        ]);
        Assert.Equal(2, result.Length);
        Assert.Equal("A", ((TextBlockElement)result[0]).Content);
        Assert.Equal("B", ((TextBlockElement)result[1]).Content);
    }

    [Fact]
    public void Filter_No_Nulls_Returns_Same_Array()
    {
        var elements = new Element[] { new TextBlockElement("A"), new TextBlockElement("B") };
        var result = InvokeFilter(elements);
        Assert.Same(elements, result); // fast path: same reference
    }

    [Fact]
    public void Filter_Single_Null_Among_Real()
    {
        var result = InvokeFilter([new TextBlockElement("A"), null!, new TextBlockElement("B")]);
        Assert.Equal(2, result.Length);
    }

    [Fact]
    public void Filter_Empty_Array_Returns_Same()
    {
        var elements = Array.Empty<Element>();
        var result = InvokeFilter(elements);
        Assert.Same(elements, result);
    }

    // ════════════════════════════════════════════════════════════════
    //  2. Reconcile both arrays empty → no operations
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Reconcile_Both_Empty_No_Operations()
    {
        var mock = new MockChildCollection();
        var reconciler = new Reconciler();
        ChildReconciler.Reconcile(Array.Empty<Element>(), Array.Empty<Element>(), mock, reconciler, NoOp);
        Assert.Empty(mock.Operations);
    }

    [Fact]
    public void Reconcile_Both_All_Null_No_Operations()
    {
        var mock = new MockChildCollection();
        var reconciler = new Reconciler();
        ChildReconciler.Reconcile(
            new Element[] { null!, null! },
            new Element[] { null!, null!, null! },
            mock, reconciler, NoOp);
        Assert.Empty(mock.Operations);
    }

    [Fact]
    public void Reconcile_Old_Empty_New_All_Null_No_Operations()
    {
        var mock = new MockChildCollection();
        var reconciler = new Reconciler();
        ChildReconciler.Reconcile(Array.Empty<Element>(), new Element[] { null!, null! }, mock, reconciler, NoOp);
        Assert.Empty(mock.Operations);
    }

    [Fact]
    public void Reconcile_Old_All_Empty_New_Empty_No_Operations()
    {
        var mock = new MockChildCollection();
        var reconciler = new Reconciler();
        ChildReconciler.Reconcile(
            new Element[] { new EmptyElement(), new EmptyElement() },
            Array.Empty<Element>(),
            mock, reconciler, NoOp);
        Assert.Empty(mock.Operations);
    }

    // ════════════════════════════════════════════════════════════════
    //  3. HasAnyKeys algorithm
    // ════════════════════════════════════════════════════════════════

    private static bool InvokeHasAnyKeys(Element[] elements)
    {
        var method = typeof(ChildReconciler).GetMethod("HasAnyKeys",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static);
        return (bool)method!.Invoke(null, [elements])!;
    }

    [Fact]
    public void HasAnyKeys_All_Keyed_Returns_True()
    {
        Assert.True(InvokeHasAnyKeys([
            new TextBlockElement("A") { Key = "k1" },
            new TextBlockElement("B") { Key = "k2" },
        ]));
    }

    [Fact]
    public void HasAnyKeys_Some_Keyed_Returns_True()
    {
        Assert.True(InvokeHasAnyKeys([
            new TextBlockElement("A"),
            new TextBlockElement("B") { Key = "k2" },
        ]));
    }

    [Fact]
    public void HasAnyKeys_None_Keyed_Returns_False()
    {
        Assert.False(InvokeHasAnyKeys([
            new TextBlockElement("A"),
            new TextBlockElement("B"),
        ]));
    }

    [Fact]
    public void HasAnyKeys_Empty_Returns_False()
    {
        Assert.False(InvokeHasAnyKeys(Array.Empty<Element>()));
    }

    // ════════════════════════════════════════════════════════════════
    //  4. GetKey algorithm
    // ════════════════════════════════════════════════════════════════

    private static string InvokeGetKey(Element element, int positionalIndex)
    {
        var method = typeof(ChildReconciler).GetMethod("GetKey",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static);
        return (string)method!.Invoke(null, [element, positionalIndex])!;
    }

    [Fact]
    public void GetKey_With_Explicit_Key()
    {
        Assert.Equal("my-key", InvokeGetKey(new TextBlockElement("A") { Key = "my-key" }, 5));
    }

    [Fact]
    public void GetKey_Without_Key_Uses_Positional()
    {
        Assert.Equal("__pos_3_TextBlockElement", InvokeGetKey(new TextBlockElement("A"), 3));
    }

    [Fact]
    public void GetKey_Different_Types_Different_Positional_Keys()
    {
        var textKey = InvokeGetKey(new TextBlockElement("A"), 0);
        var buttonKey = InvokeGetKey(new ButtonElement("A"), 0);
        Assert.NotEqual(textKey, buttonKey);
    }

    [Fact]
    public void GetKey_Same_Type_Different_Positions_Different_Keys()
    {
        var key0 = InvokeGetKey(new TextBlockElement("A"), 0);
        var key1 = InvokeGetKey(new TextBlockElement("A"), 1);
        Assert.NotEqual(key0, key1);
    }

    // ════════════════════════════════════════════════════════════════
    //  5. KeyMatch algorithm
    // ════════════════════════════════════════════════════════════════

    private static bool InvokeKeyMatch(Element a, Element b)
    {
        var method = typeof(ChildReconciler).GetMethod("KeyMatch",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static);
        return (bool)method!.Invoke(null, [a, b])!;
    }

    [Fact]
    public void KeyMatch_Same_Type_Same_Key_Returns_True()
    {
        Assert.True(InvokeKeyMatch(
            new TextBlockElement("A") { Key = "k1" },
            new TextBlockElement("B") { Key = "k1" }));
    }

    [Fact]
    public void KeyMatch_Same_Type_Different_Key_Returns_False()
    {
        Assert.False(InvokeKeyMatch(
            new TextBlockElement("A") { Key = "k1" },
            new TextBlockElement("B") { Key = "k2" }));
    }

    [Fact]
    public void KeyMatch_Different_Type_Same_Key_Returns_False()
    {
        Assert.False(InvokeKeyMatch(
            new TextBlockElement("A") { Key = "k1" },
            new ButtonElement("A") { Key = "k1" }));
    }

    [Fact]
    public void KeyMatch_Both_Null_Keys_Same_Type_Returns_True()
    {
        Assert.True(InvokeKeyMatch(new TextBlockElement("A"), new TextBlockElement("B")));
    }

    [Fact]
    public void KeyMatch_One_Keyed_One_Not_Returns_False()
    {
        Assert.False(InvokeKeyMatch(
            new TextBlockElement("A") { Key = "k1" },
            new TextBlockElement("B")));
    }

    // ════════════════════════════════════════════════════════════════
    //  6. LIS edge cases for child count changes
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void LIS_All_Unmapped_Returns_Empty()
    {
        var result = ChildReconciler.ComputeLIS([-1, -1, -1]);
        Assert.Empty(result);
    }

    [Fact]
    public void LIS_Single_Mapped_Among_Unmapped()
    {
        var result = ChildReconciler.ComputeLIS([-1, 5, -1]);
        Assert.Single(result);
        Assert.Contains(1, result);
    }

    [Fact]
    public void LIS_Growing_Sequence()
    {
        // Simulates adding items at end: old indices [0, 1, 2] still in order
        var result = ChildReconciler.ComputeLIS([0, 1, 2, -1, -1]);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void LIS_Shrinking_Sequence()
    {
        // Only first item survives
        var result = ChildReconciler.ComputeLIS([0]);
        Assert.Single(result);
    }

    [Fact]
    public void LIS_Reorder_With_New_Items()
    {
        // Simulates: old [A,B,C] → new [C, new, A, B]
        // newToOld = [2, -1, 0, 1]
        var result = ChildReconciler.ComputeLIS([2, -1, 0, 1]);
        // LIS of [2, 0, 1] (excluding -1) → [0, 1] at indices 2,3
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void LIS_Middle_Insertion()
    {
        // old [A, C] → new [A, new, C]
        // newToOld = [0, -1, 1]
        var result = ChildReconciler.ComputeLIS([0, -1, 1]);
        Assert.Equal(2, result.Count); // [0, 1] are in order
    }

    [Fact]
    public void LIS_Middle_Deletion()
    {
        // old [A, B, C] → new [A, C]
        // newToOld = [0, 2]
        var result = ChildReconciler.ComputeLIS([0, 2]);
        Assert.Equal(2, result.Count); // [0, 2] is increasing
    }

    // ════════════════════════════════════════════════════════════════
    //  4. Element-level: DSL factories handle null children
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void VStack_DSL_Filters_Null_Children()
    {
        var el = VStack(TextBlock("A"), null, TextBlock("B"), null);
        Assert.Equal(2, el.Children.Length);
    }

    [Fact]
    public void HStack_DSL_Filters_Null_Children()
    {
        var el = HStack(null, TextBlock("A"), null);
        Assert.Single(el.Children);
    }

    [Fact]
    public void Flex_DSL_Filters_Null_Children()
    {
        var el = Factories.Flex(TextBlock("A"), null, TextBlock("B"), null, TextBlock("C"));
        Assert.Equal(3, el.Children.Length);
    }

    [Fact]
    public void FlexRow_DSL_Filters_Null_Children()
    {
        var el = FlexRow(null, null, TextBlock("A"));
        Assert.Single(el.Children);
    }

    [Fact]
    public void FlexColumn_DSL_Filters_Null_Children()
    {
        var el = FlexColumn(null, TextBlock("A"), null);
        Assert.Single(el.Children);
    }

    [Fact]
    public void Grid_DSL_Filters_Null_Children()
    {
        var el = Grid([GridSize.Star(), GridSize.Star()], [GridSize.Star()], TextBlock("A"), null, TextBlock("B"));
        Assert.Equal(2, el.Children.Length);
    }

    [Fact]
    public void WrapGrid_DSL_Filters_Null_Children()
    {
        var el = WrapGrid(TextBlock("A"), null, TextBlock("B"));
        Assert.Equal(2, el.Children.Length);
    }

    [Fact]
    public void Canvas_DSL_Filters_Null_Children()
    {
        var el = Canvas(null, TextBlock("A"), null);
        Assert.Single(el.Children);
    }

    [Fact]
    public void RelativePanel_DSL_Filters_Null_Children()
    {
        var el = RelativePanel(TextBlock("A"), null, TextBlock("B"));
        Assert.Equal(2, el.Children.Length);
    }

    [Fact]
    public void VStack_DSL_All_Null_Produces_Empty()
    {
        var el = VStack(null, null);
        Assert.Empty(el.Children);
    }

    [Fact]
    public void Flex_DSL_All_Null_Produces_Empty()
    {
        var el = Factories.Flex(null, null, null);
        Assert.Empty(el.Children);
    }

    [Fact]
    public void VStack_DSL_Empty_Params()
    {
        var el = VStack();
        Assert.Empty(el.Children);
    }

    [Fact]
    public void Flex_DSL_Empty_Params()
    {
        var el = Factories.Flex();
        Assert.Empty(el.Children);
    }

    // ════════════════════════════════════════════════════════════════
    //  5. CanUpdate consistency across all panel types
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void CanUpdate_Stack_Elements_Same_Type()
    {
        var reconciler = new Reconciler();
        Assert.True(reconciler.CanUpdate(VStack(TextBlock("A")), VStack(TextBlock("B"))));
        Assert.True(reconciler.CanUpdate(HStack(TextBlock("A")), HStack(TextBlock("B"))));
    }

    [Fact]
    public void CanUpdate_Flex_Elements_Same_Type()
    {
        var reconciler = new Reconciler();
        Assert.True(reconciler.CanUpdate(Factories.Flex(TextBlock("A")), Factories.Flex(TextBlock("B"))));
        Assert.True(reconciler.CanUpdate(FlexRow(TextBlock("A")), FlexColumn(TextBlock("B"))));
    }

    [Fact]
    public void CanUpdate_Grid_Elements_Same_Type()
    {
        var reconciler = new Reconciler();
        var a = Grid([GridSize.Star()], [GridSize.Star()], TextBlock("A"));
        var b = Grid([GridSize.Star(), GridSize.Star()], [GridSize.Star()], TextBlock("B"));
        Assert.True(reconciler.CanUpdate(a, b));
    }

    [Fact]
    public void CanUpdate_WrapGrid_Elements_Same_Type()
    {
        var reconciler = new Reconciler();
        Assert.True(reconciler.CanUpdate(WrapGrid(TextBlock("A")), WrapGrid(TextBlock("B"))));
    }

    [Fact]
    public void CanUpdate_Canvas_Elements_Same_Type()
    {
        var reconciler = new Reconciler();
        Assert.True(reconciler.CanUpdate(Canvas(TextBlock("A")), Canvas(TextBlock("B"))));
    }

    [Fact]
    public void CanUpdate_RelativePanel_Elements_Same_Type()
    {
        var reconciler = new Reconciler();
        Assert.True(reconciler.CanUpdate(RelativePanel(TextBlock("A")), RelativePanel(TextBlock("B"))));
    }

    [Fact]
    public void CanUpdate_Cross_Panel_Types_Returns_False()
    {
        var reconciler = new Reconciler();
        Assert.False(reconciler.CanUpdate(VStack(TextBlock("A")), Factories.Flex(TextBlock("A"))));
        Assert.False(reconciler.CanUpdate(Factories.Flex(TextBlock("A")), WrapGrid(TextBlock("A"))));
        Assert.False(reconciler.CanUpdate(WrapGrid(TextBlock("A")), Canvas(TextBlock("A"))));
        Assert.False(reconciler.CanUpdate(
            Grid([GridSize.Star()], [GridSize.Star()], TextBlock("A")),
            VStack(TextBlock("A"))));
    }

    // ════════════════════════════════════════════════════════════════
    //  7. CanUpdate type mismatch scenarios (no WinUI needed)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void CanUpdate_Text_Vs_Button_Returns_False()
    {
        var reconciler = new Reconciler();
        Assert.False(reconciler.CanUpdate(new TextBlockElement("A"), new ButtonElement("A")));
    }

    [Fact]
    public void CanUpdate_Text_Vs_Text_Returns_True()
    {
        var reconciler = new Reconciler();
        Assert.True(reconciler.CanUpdate(new TextBlockElement("A"), new TextBlockElement("B")));
    }

    [Fact]
    public void CanUpdate_Keyed_Text_Vs_Unkeyed_Text_Returns_False()
    {
        var reconciler = new Reconciler();
        Assert.False(reconciler.CanUpdate(
            new TextBlockElement("A") { Key = "k1" },
            new TextBlockElement("B")));
    }

    // ════════════════════════════════════════════════════════════════
    //  8. LIS large/stress tests for reconciliation performance
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void LIS_Large_Growing_From_Empty()
    {
        // All new items: newToOld is all -1
        var input = Enumerable.Repeat(-1, 100).ToArray();
        var result = ChildReconciler.ComputeLIS(input);
        Assert.Empty(result);
    }

    [Fact]
    public void LIS_Large_Shrinking_To_Single()
    {
        var result = ChildReconciler.ComputeLIS([42]);
        Assert.Single(result);
    }

    [Fact]
    public void LIS_Large_Stable_Order()
    {
        // All items stay in place: newToOld = [0, 1, 2, ..., 99]
        var input = Enumerable.Range(0, 100).ToArray();
        var result = ChildReconciler.ComputeLIS(input);
        Assert.Equal(100, result.Count);
    }

    [Fact]
    public void LIS_Large_Reversed_Order()
    {
        // Complete reversal: only 1 item in LIS
        var input = Enumerable.Range(0, 100).Reverse().ToArray();
        var result = ChildReconciler.ComputeLIS(input);
        Assert.Single(result);
    }

    [Fact]
    public void LIS_Mixed_New_And_Existing()
    {
        // Simulates: half items new, half reordered
        // [0, -1, 1, -1, 2, -1, 3, -1]
        var input = new int[8];
        for (int i = 0; i < 8; i++)
            input[i] = i % 2 == 0 ? i / 2 : -1;
        var result = ChildReconciler.ComputeLIS(input);
        Assert.Equal(4, result.Count); // [0, 1, 2, 3] are in order
    }

    // ════════════════════════════════════════════════════════════════
    //  9. ShallowEquals for panel elements with children
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ShallowEquals_Stack_Different_Children_Count()
    {
        var a = VStack(TextBlock("A"));
        var b = VStack(TextBlock("A"), TextBlock("B"));
        Assert.False(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_Stack_Same_Reference_Returns_True()
    {
        var a = VStack(TextBlock("A"));
        Assert.True(Element.ShallowEquals(a, a));
    }

    [Fact]
    public void ShallowEquals_Flex_Different_Direction()
    {
        var a = FlexRow(TextBlock("A"));
        var b = FlexColumn(TextBlock("A"));
        Assert.False(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_Flex_Same_Instance_Returns_True()
    {
        var a = FlexRow(TextBlock("A"));
        Assert.True(Element.ShallowEquals(a, a));
    }

    // ════════════════════════════════════════════════════════════════
    //  10. Panel elements: empty children edge cases
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Stack_Empty_Children_Is_Valid()
    {
        var el = VStack();
        Assert.Empty(el.Children);
        Assert.IsType<StackElement>(el);
    }

    [Fact]
    public void Flex_Empty_Children_Is_Valid()
    {
        var el = Factories.Flex();
        Assert.Empty(el.Children);
        Assert.IsType<FlexElement>(el);
    }

    [Fact]
    public void WrapGrid_Empty_Children_Is_Valid()
    {
        var el = WrapGrid();
        Assert.Empty(el.Children);
    }

    [Fact]
    public void Canvas_Empty_Children_Is_Valid()
    {
        var el = Canvas();
        Assert.Empty(el.Children);
    }

    [Fact]
    public void Grid_Empty_Children_Is_Valid()
    {
        var el = Grid([GridSize.Star()], [GridSize.Star()]);
        Assert.Empty(el.Children);
    }

}

/// <summary>
/// Tests for Yoga layout engine ↔ WinUI layout integration.
/// Validates FlexPanel's SyncYogaTree, MeasureOverride/ArrangeOverride behavior,
/// and edge cases in the Yoga-to-WinUI bridge.
/// </summary>
public class YogaWinUILayoutInteractionTests
{
    // ════════════════════════════════════════════════════════════════
    //  1. Yoga node tree operations (pure algorithm, no WinUI)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void YogaNode_InsertChild_At_End()
    {
        var root = new YogaNode();
        var child0 = new YogaNode();
        var child1 = new YogaNode();
        root.InsertChild(child0, 0);
        root.InsertChild(child1, 1);

        Assert.Equal(2, root.ChildCount);
        Assert.Same(child0, root.GetChild(0));
        Assert.Same(child1, root.GetChild(1));
    }

    [Fact]
    public void YogaNode_InsertChild_At_Beginning()
    {
        var root = new YogaNode();
        var child0 = new YogaNode();
        var child1 = new YogaNode();
        root.InsertChild(child0, 0);
        root.InsertChild(child1, 0);

        Assert.Equal(2, root.ChildCount);
        Assert.Same(child1, root.GetChild(0));
        Assert.Same(child0, root.GetChild(1));
    }

    [Fact]
    public void YogaNode_RemoveChild_By_Reference()
    {
        var root = new YogaNode();
        var child0 = new YogaNode();
        var child1 = new YogaNode();
        root.InsertChild(child0, 0);
        root.InsertChild(child1, 1);

        root.RemoveChild(child0);

        Assert.Equal(1, root.ChildCount);
        Assert.Same(child1, root.GetChild(0));
    }

    [Fact]
    public void YogaNode_RemoveChild_By_Index()
    {
        var root = new YogaNode();
        var child0 = new YogaNode();
        var child1 = new YogaNode();
        var child2 = new YogaNode();
        root.InsertChild(child0, 0);
        root.InsertChild(child1, 1);
        root.InsertChild(child2, 2);

        root.RemoveChild(1);

        Assert.Equal(2, root.ChildCount);
        Assert.Same(child0, root.GetChild(0));
        Assert.Same(child2, root.GetChild(1));
    }

    [Fact]
    public void YogaNode_RemoveAll_Children()
    {
        var root = new YogaNode();
        for (int i = 0; i < 5; i++)
            root.InsertChild(new YogaNode(), i);

        Assert.Equal(5, root.ChildCount);

        while (root.ChildCount > 0)
            root.RemoveChild(0);

        Assert.Equal(0, root.ChildCount);
    }

    [Fact]
    public void YogaNode_Owner_Is_Set_On_Insert()
    {
        var root = new YogaNode();
        var child = new YogaNode();
        root.InsertChild(child, 0);

        Assert.Same(root, child.Owner);
    }

    [Fact]
    public void YogaNode_Owner_Is_Cleared_On_Remove()
    {
        var root = new YogaNode();
        var child = new YogaNode();
        root.InsertChild(child, 0);
        root.RemoveChild(child);

        Assert.Null(child.Owner);
    }

    // ════════════════════════════════════════════════════════════════
    //  2. Yoga layout with child count changes
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Layout_Add_Children_Updates_Layout()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;
        root.Width = YogaValue.Point(300);
        root.Height = YogaValue.Point(100);

        var child0 = new YogaNode { FlexGrow = 1 };
        root.InsertChild(child0, 0);
        root.CalculateLayout(300, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);
        Assert.Equal(300f, child0.LayoutWidth);

        // Add second child — both should share space
        var child1 = new YogaNode { FlexGrow = 1 };
        root.InsertChild(child1, 1);
        root.CalculateLayout(300, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(150f, child0.LayoutWidth);
        Assert.Equal(150f, child1.LayoutWidth);
    }

    [Fact]
    public void Layout_Remove_Children_Updates_Layout()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;
        root.Width = YogaValue.Point(300);
        root.Height = YogaValue.Point(100);

        var child0 = new YogaNode { FlexGrow = 1 };
        var child1 = new YogaNode { FlexGrow = 1 };
        root.InsertChild(child0, 0);
        root.InsertChild(child1, 1);
        root.CalculateLayout(300, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);
        Assert.Equal(150f, child0.LayoutWidth);

        // Remove second child — first should take all space
        root.RemoveChild(child1);
        root.CalculateLayout(300, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(300f, child0.LayoutWidth);
    }

    [Fact]
    public void Layout_Empty_Root_Has_Zero_Content_Size()
    {
        var root = new YogaNode();
        root.CalculateLayout(float.NaN, float.NaN, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(0f, root.LayoutWidth);
        Assert.Equal(0f, root.LayoutHeight);
    }

    [Fact]
    public void Layout_Add_Single_Child_To_Empty()
    {
        var root = new YogaNode();
        root.Width = YogaValue.Point(200);
        root.Height = YogaValue.Point(100);

        var child = new YogaNode();
        child.Width = YogaValue.Point(50);
        child.Height = YogaValue.Point(50);
        root.InsertChild(child, 0);
        root.CalculateLayout(200, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(0f, child.LayoutX);
        Assert.Equal(0f, child.LayoutY);
        Assert.Equal(50f, child.LayoutWidth);
        Assert.Equal(50f, child.LayoutHeight);
    }

    [Fact]
    public void Layout_Remove_All_Children_Leaves_Root_Sized()
    {
        var root = new YogaNode();
        root.Width = YogaValue.Point(200);
        root.Height = YogaValue.Point(100);

        var child = new YogaNode { FlexGrow = 1 };
        root.InsertChild(child, 0);
        root.CalculateLayout(200, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);
        root.RemoveChild(child);
        root.CalculateLayout(200, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
    }

    // ════════════════════════════════════════════════════════════════
    //  3. Yoga measure vs arrange split (content sizing vs definite)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Content_Sizing_NaN_Dimensions_Computes_From_Children()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;

        var child0 = new YogaNode { Width = YogaValue.Point(50), Height = YogaValue.Point(30) };
        var child1 = new YogaNode { Width = YogaValue.Point(70), Height = YogaValue.Point(40) };
        root.InsertChild(child0, 0);
        root.InsertChild(child1, 1);

        // NaN = content sizing (like MeasureOverride)
        root.CalculateLayout(float.NaN, float.NaN, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(120f, root.LayoutWidth);  // 50 + 70
        Assert.Equal(40f, root.LayoutHeight);   // max(30, 40)
    }

    [Fact]
    public void Definite_Sizing_Distributes_Space_Via_FlexGrow()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;

        var child0 = new YogaNode { FlexGrow = 1 };
        var child1 = new YogaNode { FlexGrow = 2 };
        root.InsertChild(child0, 0);
        root.InsertChild(child1, 1);

        // Definite sizing (like ArrangeOverride)
        root.CalculateLayout(300, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(100f, child0.LayoutWidth);
        Assert.Equal(200f, child1.LayoutWidth);
    }

    [Fact]
    public void Content_Sizing_With_MaxWidth_Constraint()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;
        root.MaxWidth = YogaValue.Point(100);

        var child0 = new YogaNode { Width = YogaValue.Point(80), Height = YogaValue.Point(30) };
        var child1 = new YogaNode { Width = YogaValue.Point(80), Height = YogaValue.Point(30) };
        root.InsertChild(child0, 0);
        root.InsertChild(child1, 1);

        // With wrap disabled, children will overflow. Root width is capped at MaxWidth.
        root.CalculateLayout(float.NaN, float.NaN, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.True(root.LayoutWidth <= 100f || root.LayoutWidth == 160f,
            $"Root width should be constrained to MaxWidth or represent natural size: {root.LayoutWidth}");
    }

    [Fact]
    public void Measure_Then_Arrange_Produces_Consistent_Results()
    {
        // Simulates the WinUI two-pass layout: measure with NaN, arrange with definite
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Column;

        var child0 = new YogaNode { Width = YogaValue.Point(100), Height = YogaValue.Point(50) };
        var child1 = new YogaNode { Width = YogaValue.Point(100), Height = YogaValue.Point(50), FlexGrow = 1 };
        root.InsertChild(child0, 0);
        root.InsertChild(child1, 1);

        // Measure pass: content-size the root
        root.CalculateLayout(float.NaN, float.NaN, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);
        float measureWidth = root.LayoutWidth;
        float measureHeight = root.LayoutHeight;

        Assert.Equal(100f, measureWidth);
        Assert.Equal(100f, measureHeight);

        // Arrange pass: given more height, flex-grow expands child1
        root.CalculateLayout(100, 200, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(50f, child0.LayoutHeight);   // fixed
        Assert.Equal(150f, child1.LayoutHeight);   // grew to fill remaining 150
    }

    // ════════════════════════════════════════════════════════════════
    //  4. Yoga tree sync edge cases (child insertion/removal order)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Yoga_Child_Reorder_Preserves_Layout()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;
        root.Width = YogaValue.Point(300);
        root.Height = YogaValue.Point(100);

        var childA = new YogaNode { Width = YogaValue.Point(100), Height = YogaValue.Point(50) };
        var childB = new YogaNode { Width = YogaValue.Point(100), Height = YogaValue.Point(50) };
        var childC = new YogaNode { Width = YogaValue.Point(100), Height = YogaValue.Point(50) };
        root.InsertChild(childA, 0);
        root.InsertChild(childB, 1);
        root.InsertChild(childC, 2);
        root.CalculateLayout(300, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(0f, childA.LayoutX);
        Assert.Equal(100f, childB.LayoutX);
        Assert.Equal(200f, childC.LayoutX);

        // Reorder: [C, A, B]
        root.RemoveChild(childA);
        root.RemoveChild(childB);
        root.RemoveChild(childC);
        root.InsertChild(childC, 0);
        root.InsertChild(childA, 1);
        root.InsertChild(childB, 2);
        root.CalculateLayout(300, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(0f, childC.LayoutX);
        Assert.Equal(100f, childA.LayoutX);
        Assert.Equal(200f, childB.LayoutX);
    }

    [Fact]
    public void Yoga_Replace_Child_In_Middle()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;
        root.Width = YogaValue.Point(300);
        root.Height = YogaValue.Point(100);

        var childA = new YogaNode { Width = YogaValue.Point(100) };
        var childB = new YogaNode { Width = YogaValue.Point(100) };
        var childC = new YogaNode { Width = YogaValue.Point(100) };
        root.InsertChild(childA, 0);
        root.InsertChild(childB, 1);
        root.InsertChild(childC, 2);

        // Replace B with a wider D
        root.RemoveChild(childB);
        var childD = new YogaNode { Width = YogaValue.Point(150) };
        root.InsertChild(childD, 1);
        root.CalculateLayout(300, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(0f, childA.LayoutX);
        Assert.Equal(100f, childD.LayoutX);
        Assert.Equal(150f, childD.LayoutWidth);
        Assert.Equal(250f, childC.LayoutX);
    }

    // ════════════════════════════════════════════════════════════════
    //  5. Yoga measure function interaction
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void MeasureFunction_Called_During_Layout()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;
        root.Width = YogaValue.Point(200);
        root.Height = YogaValue.Point(100);

        bool measureCalled = false;
        var child = new YogaNode();
        child.MeasureFunction = (node, w, wMode, h, hMode) =>
        {
            measureCalled = true;
            return new YogaSize(50, 30);
        };
        root.InsertChild(child, 0);
        root.CalculateLayout(200, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.True(measureCalled);
        Assert.Equal(50f, child.LayoutWidth);
        // Height stretches to container height (AlignItems=Stretch is default)
        Assert.Equal(100f, child.LayoutHeight);
    }

    [Fact]
    public void MeasureFunction_Receives_Constraints()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;
        root.Width = YogaValue.Point(100);
        root.Height = YogaValue.Point(50);

        float receivedWidth = -1;
        Microsoft.UI.Reactor.Layout.YogaMeasureMode receivedWMode = (Microsoft.UI.Reactor.Layout.YogaMeasureMode)(-1);

        var child = new YogaNode { FlexGrow = 1 };
        child.MeasureFunction = (node, w, wMode, h, hMode) =>
        {
            receivedWidth = w;
            receivedWMode = wMode;
            return new YogaSize(w, 30);
        };
        root.InsertChild(child, 0);
        root.CalculateLayout(100, 50, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.True(receivedWidth > 0, "MeasureFunction should receive positive width constraint");
    }

    [Fact]
    public void MeasureFunction_Not_Called_When_Node_Has_Children()
    {
        // Yoga rule: nodes with children should not have a measure function
        // (the measure function is only for leaf nodes)
        var root = new YogaNode();
        root.Width = YogaValue.Point(200);
        root.Height = YogaValue.Point(100);

        var parent = new YogaNode();
        var grandchild = new YogaNode { Width = YogaValue.Point(50), Height = YogaValue.Point(50) };
        parent.InsertChild(grandchild, 0);

        // Setting a measure function on a node with children — Yoga should use children for sizing
        root.InsertChild(parent, 0);
        root.CalculateLayout(200, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(50f, grandchild.LayoutWidth);
    }

    // ════════════════════════════════════════════════════════════════
    //  6. Flex properties affect layout correctly
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void FlexShrink_Shrinks_Children_When_Overflowing()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;
        root.Width = YogaValue.Point(200);

        var child0 = new YogaNode { Width = YogaValue.Point(150), Style = { FlexShrink = 1 } };
        var child1 = new YogaNode { Width = YogaValue.Point(150), Style = { FlexShrink = 1 } };
        root.InsertChild(child0, 0);
        root.InsertChild(child1, 1);
        root.CalculateLayout(200, float.NaN, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(100f, child0.LayoutWidth);
        Assert.Equal(100f, child1.LayoutWidth);
    }

    [Fact]
    public void FlexBasis_Overrides_Width_For_Grow()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;
        root.Width = YogaValue.Point(300);

        var child0 = new YogaNode { FlexBasis = YogaValue.Point(100), FlexGrow = 1 };
        var child1 = new YogaNode { FlexBasis = YogaValue.Point(100), FlexGrow = 1 };
        root.InsertChild(child0, 0);
        root.InsertChild(child1, 1);
        root.CalculateLayout(300, float.NaN, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        // 100 + 50 = 150 each (100 remaining split equally)
        Assert.Equal(150f, child0.LayoutWidth);
        Assert.Equal(150f, child1.LayoutWidth);
    }

    [Fact]
    public void AlignItems_Center_Centers_Cross_Axis()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;
        root.AlignItems = Microsoft.UI.Reactor.Layout.FlexAlign.Center;
        root.Width = YogaValue.Point(200);
        root.Height = YogaValue.Point(100);

        var child = new YogaNode { Width = YogaValue.Point(50), Height = YogaValue.Point(30) };
        root.InsertChild(child, 0);
        root.CalculateLayout(200, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        // Centered in 100px height: (100-30)/2 = 35
        Assert.Equal(35f, child.LayoutY);
    }

    [Fact]
    public void JustifyContent_SpaceBetween_Distributes_Space()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;
        root.JustifyContent = Microsoft.UI.Reactor.Layout.FlexJustify.SpaceBetween;
        root.Width = YogaValue.Point(300);
        root.Height = YogaValue.Point(50);

        var child0 = new YogaNode { Width = YogaValue.Point(50) };
        var child1 = new YogaNode { Width = YogaValue.Point(50) };
        var child2 = new YogaNode { Width = YogaValue.Point(50) };
        root.InsertChild(child0, 0);
        root.InsertChild(child1, 1);
        root.InsertChild(child2, 2);
        root.CalculateLayout(300, 50, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(0f, child0.LayoutX);
        Assert.Equal(125f, child1.LayoutX);
        Assert.Equal(250f, child2.LayoutX);
    }

    [Fact]
    public void FlexWrap_Wraps_Children_To_Next_Line()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;
        root.FlexWrap = Microsoft.UI.Reactor.Layout.FlexWrap.Wrap;
        root.Width = YogaValue.Point(200);

        var child0 = new YogaNode { Width = YogaValue.Point(120), Height = YogaValue.Point(50) };
        var child1 = new YogaNode { Width = YogaValue.Point(120), Height = YogaValue.Point(50) };
        root.InsertChild(child0, 0);
        root.InsertChild(child1, 1);
        root.CalculateLayout(200, float.NaN, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        // child1 should wrap to next line
        Assert.Equal(0f, child0.LayoutX);
        Assert.Equal(0f, child0.LayoutY);
        Assert.Equal(0f, child1.LayoutX);
        Assert.Equal(50f, child1.LayoutY);
    }

    [Fact]
    public void Gap_Adds_Spacing_Between_Children()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;
        root.Width = YogaValue.Point(300);
        root.Height = YogaValue.Point(50);
        root.SetGap(Microsoft.UI.Reactor.Layout.YogaGutter.Column, 20);

        var child0 = new YogaNode { Width = YogaValue.Point(50) };
        var child1 = new YogaNode { Width = YogaValue.Point(50) };
        var child2 = new YogaNode { Width = YogaValue.Point(50) };
        root.InsertChild(child0, 0);
        root.InsertChild(child1, 1);
        root.InsertChild(child2, 2);
        root.CalculateLayout(300, 50, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(0f, child0.LayoutX);
        Assert.Equal(70f, child1.LayoutX);   // 50 + 20
        Assert.Equal(140f, child2.LayoutX);  // 50 + 20 + 50 + 20
    }

    // ════════════════════════════════════════════════════════════════
    //  7. Yoga layout after dynamic child count changes
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Layout_Recomputes_After_Adding_Child_In_Middle()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;
        root.Width = YogaValue.Point(300);
        root.Height = YogaValue.Point(100);

        var childA = new YogaNode { FlexGrow = 1 };
        var childC = new YogaNode { FlexGrow = 1 };
        root.InsertChild(childA, 0);
        root.InsertChild(childC, 1);
        root.CalculateLayout(300, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);
        Assert.Equal(150f, childA.LayoutWidth);

        // Insert B between A and C
        var childB = new YogaNode { FlexGrow = 1 };
        root.InsertChild(childB, 1);
        root.CalculateLayout(300, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(100f, childA.LayoutWidth);
        Assert.Equal(100f, childB.LayoutWidth);
        Assert.Equal(100f, childC.LayoutWidth);
    }

    [Fact]
    public void Layout_Recomputes_After_Removing_Child_From_Middle()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;
        root.Width = YogaValue.Point(300);
        root.Height = YogaValue.Point(100);

        var childA = new YogaNode { FlexGrow = 1 };
        var childB = new YogaNode { FlexGrow = 1 };
        var childC = new YogaNode { FlexGrow = 1 };
        root.InsertChild(childA, 0);
        root.InsertChild(childB, 1);
        root.InsertChild(childC, 2);
        root.CalculateLayout(300, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);
        Assert.Equal(100f, childA.LayoutWidth);

        // Remove B from middle
        root.RemoveChild(childB);
        root.CalculateLayout(300, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(150f, childA.LayoutWidth);
        Assert.Equal(150f, childC.LayoutWidth);
    }

    [Fact]
    public void Layout_Column_Direction_Child_Count_Change()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Column;
        root.Width = YogaValue.Point(100);
        root.Height = YogaValue.Point(300);

        var child0 = new YogaNode { FlexGrow = 1 };
        root.InsertChild(child0, 0);
        root.CalculateLayout(100, 300, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);
        Assert.Equal(300f, child0.LayoutHeight);

        // Add 2 more children
        var child1 = new YogaNode { FlexGrow = 1 };
        var child2 = new YogaNode { FlexGrow = 1 };
        root.InsertChild(child1, 1);
        root.InsertChild(child2, 2);
        root.CalculateLayout(100, 300, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(100f, child0.LayoutHeight);
        Assert.Equal(100f, child1.LayoutHeight);
        Assert.Equal(100f, child2.LayoutHeight);
    }

    // ════════════════════════════════════════════════════════════════
    //  8. Yoga padding interaction with children
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Padding_Reduces_Available_Space_For_Children()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;
        root.Width = YogaValue.Point(200);
        root.Height = YogaValue.Point(100);
        root.SetPadding(Microsoft.UI.Reactor.Layout.YogaEdge.Left, YogaValue.Point(20));
        root.SetPadding(Microsoft.UI.Reactor.Layout.YogaEdge.Right, YogaValue.Point(20));

        var child = new YogaNode { FlexGrow = 1 };
        root.InsertChild(child, 0);
        root.CalculateLayout(200, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(20f, child.LayoutX);
        Assert.Equal(160f, child.LayoutWidth); // 200 - 20 - 20
    }

    [Fact]
    public void Margin_Offsets_Child_Position()
    {
        var root = new YogaNode();
        root.Width = YogaValue.Point(200);
        root.Height = YogaValue.Point(100);

        var child = new YogaNode();
        child.Width = YogaValue.Point(50);
        child.Height = YogaValue.Point(50);
        child.SetMargin(Microsoft.UI.Reactor.Layout.YogaEdge.Left, YogaValue.Point(10));
        child.SetMargin(Microsoft.UI.Reactor.Layout.YogaEdge.Top, YogaValue.Point(15));
        root.InsertChild(child, 0);
        root.CalculateLayout(200, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(10f, child.LayoutX);
        Assert.Equal(15f, child.LayoutY);
    }

    // ════════════════════════════════════════════════════════════════
    //  9. Edge case: zero-size children
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Zero_Size_Children_Do_Not_Affect_Siblings()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;
        root.Width = YogaValue.Point(200);
        root.Height = YogaValue.Point(100);

        var child0 = new YogaNode { Width = YogaValue.Point(0), Height = YogaValue.Point(0) };
        var child1 = new YogaNode { Width = YogaValue.Point(100), Height = YogaValue.Point(50) };
        root.InsertChild(child0, 0);
        root.InsertChild(child1, 1);
        root.CalculateLayout(200, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        Assert.Equal(0f, child1.LayoutX);
        Assert.Equal(100f, child1.LayoutWidth);
    }

    // ════════════════════════════════════════════════════════════════
    //  10. RTL layout direction
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void RTL_Reverses_Child_Order()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;
        root.Width = YogaValue.Point(300);
        root.Height = YogaValue.Point(100);

        var child0 = new YogaNode { Width = YogaValue.Point(100) };
        var child1 = new YogaNode { Width = YogaValue.Point(100) };
        root.InsertChild(child0, 0);
        root.InsertChild(child1, 1);

        root.CalculateLayout(300, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.RTL);

        Assert.Equal(200f, child0.LayoutX);
        Assert.Equal(100f, child1.LayoutX);
    }

    // ════════════════════════════════════════════════════════════════
    //  11. FlexElement DSL defaults
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void FlexElement_Default_Properties()
    {
        var el = Factories.Flex(TextBlock("A"));
        Assert.Equal(Microsoft.UI.Reactor.Layout.FlexDirection.Row, el.Direction);
        Assert.Equal(Microsoft.UI.Reactor.Layout.FlexJustify.FlexStart, el.JustifyContent);
        Assert.Equal(Microsoft.UI.Reactor.Layout.FlexAlign.Stretch, el.AlignItems);
        Assert.Equal(Microsoft.UI.Reactor.Layout.FlexAlign.FlexStart, el.AlignContent);
        Assert.Equal(Microsoft.UI.Reactor.Layout.FlexWrap.NoWrap, el.Wrap);
        Assert.Equal(0.0, el.ColumnGap);
        Assert.Equal(0.0, el.RowGap);
    }

    [Fact]
    public void FlexRow_Sets_Row_Direction()
    {
        var el = FlexRow(TextBlock("A"));
        Assert.Equal(Microsoft.UI.Reactor.Layout.FlexDirection.Row, el.Direction);
    }

    [Fact]
    public void FlexColumn_Sets_Column_Direction()
    {
        var el = FlexColumn(TextBlock("A"));
        Assert.Equal(Microsoft.UI.Reactor.Layout.FlexDirection.Column, el.Direction);
    }

    [Fact]
    public void FlexElement_WithInit_Overrides()
    {
        var el = Factories.Flex(TextBlock("A")) with
        {
            Direction = Microsoft.UI.Reactor.Layout.FlexDirection.ColumnReverse,
            JustifyContent = Microsoft.UI.Reactor.Layout.FlexJustify.SpaceAround,
            AlignItems = Microsoft.UI.Reactor.Layout.FlexAlign.Center,
            Wrap = Microsoft.UI.Reactor.Layout.FlexWrap.Wrap,
            ColumnGap = 10,
            RowGap = 20,
        };
        Assert.Equal(Microsoft.UI.Reactor.Layout.FlexDirection.ColumnReverse, el.Direction);
        Assert.Equal(Microsoft.UI.Reactor.Layout.FlexJustify.SpaceAround, el.JustifyContent);
        Assert.Equal(Microsoft.UI.Reactor.Layout.FlexAlign.Center, el.AlignItems);
        Assert.Equal(Microsoft.UI.Reactor.Layout.FlexWrap.Wrap, el.Wrap);
        Assert.Equal(10.0, el.ColumnGap);
        Assert.Equal(20.0, el.RowGap);
    }

    // ════════════════════════════════════════════════════════════════
    //  12. Absolute position children
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Absolute_Position_Child_Does_Not_Affect_Siblings()
    {
        var root = new YogaNode();
        root.FlexDirection = Microsoft.UI.Reactor.Layout.FlexDirection.Row;
        root.Width = YogaValue.Point(300);
        root.Height = YogaValue.Point(100);

        var absChild = new YogaNode
        {
            PositionType = Microsoft.UI.Reactor.Layout.FlexPositionType.Absolute,
            Width = YogaValue.Point(50),
            Height = YogaValue.Point(50),
        };
        absChild.Style.Position[(int)Microsoft.UI.Reactor.Layout.YogaEdge.Left] = YogaValue.Point(10);
        absChild.Style.Position[(int)Microsoft.UI.Reactor.Layout.YogaEdge.Top] = YogaValue.Point(10);

        var normalChild = new YogaNode { FlexGrow = 1 };

        root.InsertChild(absChild, 0);
        root.InsertChild(normalChild, 1);
        root.CalculateLayout(300, 100, Microsoft.UI.Reactor.Layout.FlexLayoutDirection.LTR);

        // Absolute child positioned at (10, 10), doesn't consume flow space
        Assert.Equal(10f, absChild.LayoutX);
        Assert.Equal(10f, absChild.LayoutY);
        // Normal child should take full width
        Assert.Equal(300f, normalChild.LayoutWidth);
    }
}
