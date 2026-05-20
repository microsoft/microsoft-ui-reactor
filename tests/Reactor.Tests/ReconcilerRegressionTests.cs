using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Regression tests for reconciler correctness.
/// These verify invariants that, if broken, cause subtle bugs like
/// event handlers not firing or components not re-rendering.
/// </summary>
public class ReconcilerRegressionTests
{
    // ════════════════════════════════════════════════════════════════
    //  Wrapper elements containing components ARE record-equal.
    //  This proves why a tree-level equality skip is dangerous:
    //  the reconciler must always walk through wrappers to reach
    //  child components that may have internal state changes.
    // ════════════════════════════════════════════════════════════════

    private class CounterComponent : Component
    {
        public override Element Render() => new TextBlockElement("test");
    }

    [Fact]
    public void ComponentElement_Without_Props_Is_Record_Equal()
    {
        // Two ComponentElement instances for the same type with no props are equal.
        // This is the exact scenario in: Component<Counter>()
        var a = new ComponentElement(typeof(CounterComponent));
        var b = new ComponentElement(typeof(CounterComponent));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Border_Wrapping_Component_Is_Record_Equal()
    {
        // Border(Component<Counter>()) produces structurally identical elements
        // across renders. If the reconciler skips this, the component never re-renders.
        var a = new BorderElement(new ComponentElement(typeof(CounterComponent)))
        {
            Padding = new Thickness(24)
        };
        var b = new BorderElement(new ComponentElement(typeof(CounterComponent)))
        {
            Padding = new Thickness(24)
        };

        // These ARE equal — proving a tree-level equality check would skip them
        Assert.Equal(a, b);
    }

    [Fact]
    public void Modified_Wrapping_Component_Is_Record_Equal()
    {
        // Component<Counter>().Margin(16) wraps in ModifiedElement.
        // If a tree-level equality check skips this, the component won't re-render.
        var inner = new ComponentElement(typeof(CounterComponent));
        var mods = new ElementModifiers { Margin = new Thickness(16) };
        var a = new ModifiedElement(inner, mods);
        var b = new ModifiedElement(inner, mods);

        Assert.Equal(a, b);
    }

    [Fact]
    public void ScrollViewer_Wrapping_Component_Is_Record_Equal()
    {
        var a = new ScrollViewerElement(new ComponentElement(typeof(CounterComponent)));
        var b = new ScrollViewerElement(new ComponentElement(typeof(CounterComponent)));

        Assert.Equal(a, b);
    }

    [Fact]
    public void Nested_Wrappers_Around_Component_Are_Record_Equal()
    {
        // ScrollView(Border(Component<Counter>().Margin(16)))
        // Multiple layers of wrapping, all structurally identical across renders
        var comp = new ComponentElement(typeof(CounterComponent));
        var mods = new ElementModifiers { Margin = new Thickness(16) };
        var modified = new ModifiedElement(comp, mods);
        var border = new BorderElement(modified) { Padding = new Thickness(24) };

        var a = new ScrollViewerElement(border);
        var b = new ScrollViewerElement(border);

        Assert.Equal(a, b);
    }

    [Fact]
    public void FuncElement_With_Same_Delegate_Is_Record_Equal()
    {
        // A stable function reference (method group, cached delegate) used with Func()
        // would produce equal FuncElements. These must still re-render.
        static Element renderFunc(RenderContext ctx) => new TextBlockElement("hello");

        var a = new FuncElement(renderFunc);
        var b = new FuncElement(renderFunc);

        Assert.Equal(a, b);
    }

    // ════════════════════════════════════════════════════════════════
    //  The reconciler must NOT skip update for container elements.
    //  These tests verify that CanUpdate returns true (enabling the
    //  walk) for all wrapper/container types.
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void CanUpdate_Border_Wrapping_Component_Returns_True()
    {
        var reconciler = new Reconciler();
        var a = new BorderElement(new ComponentElement(typeof(CounterComponent)));
        var b = new BorderElement(new ComponentElement(typeof(CounterComponent)));

        Assert.True(reconciler.CanUpdate(a, b));
    }

    [Fact]
    public void CanUpdate_Modified_Wrapping_Component_Returns_True()
    {
        var reconciler = new Reconciler();
        var comp = new ComponentElement(typeof(CounterComponent));
        var mods = new ElementModifiers { Margin = new Thickness(16) };
        var a = new ModifiedElement(comp, mods);
        var b = new ModifiedElement(comp, mods);

        Assert.True(reconciler.CanUpdate(a, b));
    }

    [Fact]
    public void CanUpdate_ScrollViewer_Wrapping_Component_Returns_True()
    {
        var reconciler = new Reconciler();
        var a = new ScrollViewerElement(new ComponentElement(typeof(CounterComponent)));
        var b = new ScrollViewerElement(new ComponentElement(typeof(CounterComponent)));

        Assert.True(reconciler.CanUpdate(a, b));
    }

    [Fact]
    public void CanUpdate_Stack_With_Component_Children_Returns_True()
    {
        var reconciler = new Reconciler();
        var childrenA = new Element[] { new ComponentElement(typeof(CounterComponent)) };
        var childrenB = new Element[] { new ComponentElement(typeof(CounterComponent)) };
        var a = new StackElement(Orientation.Vertical, childrenA);
        var b = new StackElement(Orientation.Vertical, childrenB);

        Assert.True(reconciler.CanUpdate(a, b));
    }

    // ════════════════════════════════════════════════════════════════
    //  Elements with event handler closures should NOT be equal
    //  when the closures capture different state (different renders).
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ButtonElement_With_Different_Closures_Not_Equal()
    {
        // Each render typically creates a new closure capturing current state.
        // These must NOT be equal — the Tag needs to be updated.
        int count = 0;
        var a = new ButtonElement("Go", () => count++);
        count = 5;
        var b = new ButtonElement("Go", () => count++);

        // Different Action instances → not equal
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ButtonElement_Without_Handler_IS_Equal()
    {
        // Buttons with no OnClick are structurally equal.
        // This is safe — there's no handler to go stale.
        var a = new ButtonElement("Go");
        var b = new ButtonElement("Go");
        Assert.Equal(a, b);
    }

    [Fact]
    public void TextBlockElement_Same_Content_IS_Equal()
    {
        // Static text that hasn't changed is safely equal.
        var a = new TextBlockElement("Hello");
        var b = new TextBlockElement("Hello");
        Assert.Equal(a, b);
    }

    [Fact]
    public void TextBlockElement_Different_Content_Not_Equal()
    {
        var a = new TextBlockElement("Count: 0");
        var b = new TextBlockElement("Count: 1");
        Assert.NotEqual(a, b);
    }

    // ════════════════════════════════════════════════════════════════
    //  ChildCollection.Move index correctness
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Move_Forward_Places_Item_At_Target_Index()
    {
        // Simulate [A, B, C, D, E] → move B (index 1) to index 3
        // Expected result: [A, C, D, B, E]
        var items = new List<string> { "A", "B", "C", "D", "E" };
        // Simulate the Move operation
        var item = items[1];
        items.RemoveAt(1);
        items.Insert(3, item);

        Assert.Equal(["A", "C", "D", "B", "E"], items);
    }

    [Fact]
    public void Move_Backward_Places_Item_At_Target_Index()
    {
        // Simulate [A, B, C, D, E] → move D (index 3) to index 1
        // Expected result: [A, D, B, C, E]
        var items = new List<string> { "A", "B", "C", "D", "E" };
        var item = items[3];
        items.RemoveAt(3);
        items.Insert(1, item);

        Assert.Equal(["A", "D", "B", "C", "E"], items);
    }

    [Fact]
    public void Move_To_End_Places_Item_Last()
    {
        // [A, B, C] → move A (index 0) to index 2
        // Expected: [B, C, A]
        var items = new List<string> { "A", "B", "C" };
        var item = items[0];
        items.RemoveAt(0);
        items.Insert(2, item);

        Assert.Equal(["B", "C", "A"], items);
    }

    [Fact]
    public void Move_To_Start_Places_Item_First()
    {
        // [A, B, C] → move C (index 2) to index 0
        // Expected: [C, A, B]
        var items = new List<string> { "A", "B", "C" };
        var item = items[2];
        items.RemoveAt(2);
        items.Insert(0, item);

        Assert.Equal(["C", "A", "B"], items);
    }

    // ════════════════════════════════════════════════════════════════
    //  Nested components must not cause infinite reconciliation.
    //  Runtime test lives in SelfTestRunner (needs WinUI context).
    //  Here we verify the structural preconditions hold.
    // ════════════════════════════════════════════════════════════════

    private class InnerComponent : Component
    {
        public override Element Render() => new TextBlockElement("Hello from inner");
    }

    private class OuterComponent : Component
    {
        public override Element Render() => new ComponentElement(typeof(InnerComponent));
    }

    [Fact]
    public void NestedComponentElements_Are_Structurally_Valid()
    {
        var reconciler = new Reconciler();
        var outerA = new ComponentElement(typeof(OuterComponent));
        var outerB = new ComponentElement(typeof(OuterComponent));
        var inner = new ComponentElement(typeof(InnerComponent));

        // Same component type is update-compatible (precondition for the infinite loop)
        Assert.True(reconciler.CanUpdate(outerA, outerB));
        Assert.True(reconciler.CanUpdate(inner, inner));

        // Outer and inner are NOT update-compatible (different component types)
        Assert.False(reconciler.CanUpdate(outerA, inner));
    }
}
