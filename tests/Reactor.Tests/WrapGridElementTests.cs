using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for WrapGridElement — DSL factory, record properties, defaults,
/// child handling, reconciler dispatch, and Set() extension.
/// </summary>
public class WrapGridElementTests
{
    // ════════════════════════════════════════════════════════════════
    //  DSL factory and record defaults
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void WrapGrid_Creates_With_Children()
    {
        var el = WrapGrid(TextBlock("A"), TextBlock("B"), TextBlock("C"));
        Assert.IsType<WrapGridElement>(el);
        Assert.Equal(3, el.Children.Length);
    }

    [Fact]
    public void WrapGrid_Filters_Null_Children()
    {
        var el = WrapGrid(TextBlock("A"), null, TextBlock("B"));
        Assert.Equal(2, el.Children.Length);
    }

    [Fact]
    public void WrapGrid_Default_Properties()
    {
        var el = WrapGrid(TextBlock("A"));
        Assert.Equal(-1, el.MaximumRowsOrColumns);
        Assert.Equal(Orientation.Horizontal, el.Orientation);
        Assert.True(double.IsNaN(el.ItemWidth));
        Assert.True(double.IsNaN(el.ItemHeight));
    }

    [Fact]
    public void WrapGrid_With_MaxRowsOrColumns()
    {
        var el = WrapGrid(3, TextBlock("A"), TextBlock("B"), TextBlock("C"));
        Assert.Equal(3, el.MaximumRowsOrColumns);
        Assert.Equal(3, el.Children.Length);
    }

    [Fact]
    public void WrapGrid_Orientation_Via_Init()
    {
        var el = WrapGrid(TextBlock("A")) with { Orientation = Orientation.Vertical };
        Assert.Equal(Orientation.Vertical, el.Orientation);
    }

    [Fact]
    public void WrapGrid_ItemSize_Via_Init()
    {
        var el = WrapGrid(TextBlock("A")) with { ItemWidth = 50, ItemHeight = 50 };
        Assert.Equal(50, el.ItemWidth);
        Assert.Equal(50, el.ItemHeight);
    }

    [Fact]
    public void WrapGrid_Is_Element()
    {
        Element el = WrapGrid(TextBlock("A"));
        Assert.IsAssignableFrom<Element>(el);
    }

    [Fact]
    public void WrapGrid_Record_Equality_Same_Array()
    {
        var children = new Element[] { TextBlock("A") };
        var a = new WrapGridElement(children) { MaximumRowsOrColumns = 3 };
        var b = new WrapGridElement(children) { MaximumRowsOrColumns = 3 };
        Assert.Equal(a, b);
    }

    [Fact]
    public void WrapGrid_Record_Inequality_Different_MaxRows()
    {
        var children = new Element[] { TextBlock("A") };
        var a = new WrapGridElement(children) { MaximumRowsOrColumns = 3 };
        var b = new WrapGridElement(children) { MaximumRowsOrColumns = 4 };
        Assert.NotEqual(a, b);
    }

    // ════════════════════════════════════════════════════════════════
    //  Set() extension
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Set_Adds_Setter_To_WrapGridElement()
    {
        var el = WrapGrid(TextBlock("A"))
            .Set(wg => wg.HorizontalChildrenAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center);
        Assert.NotEqual(WrapGrid(TextBlock("A")), el);
    }

    // ════════════════════════════════════════════════════════════════
    //  Modifiers work on WrapGridElement
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Modifiers_Work_On_WrapGrid()
    {
        var el = WrapGrid(TextBlock("A")).Margin(8).Width(400);
        Assert.NotNull(el.Modifiers);
        Assert.Equal(400, el.Modifiers!.Width);
    }

    // ════════════════════════════════════════════════════════════════
    //  Reconciler dispatch
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void CanUpdate_Same_WrapGrid_Elements()
    {
        var reconciler = new Reconciler();
        var a = WrapGrid(TextBlock("A"));
        var b = WrapGrid(TextBlock("B"));
        Assert.True(reconciler.CanUpdate(a, b));
    }

    [Fact]
    public void CanUpdate_WrapGrid_Vs_Stack_Returns_False()
    {
        var reconciler = new Reconciler();
        Assert.False(reconciler.CanUpdate(WrapGrid(TextBlock("A")), VStack(TextBlock("A"))));
    }

    [Fact]
    public void Mount_Dispatches_WrapGridElement()
    {
        var reconciler = new Reconciler();
        try
        {
            var ctrl = reconciler.Mount(WrapGrid(3, TextBlock("A"), TextBlock("B")), () => { });
            Assert.NotNull(ctrl);
            Assert.IsType<VariableSizedWrapGrid>(ctrl);
        }
        catch (global::System.Runtime.InteropServices.COMException)
        {
            // Expected on CI/non-WinUI thread (legacy raw form).
        }
        catch (global::System.Reflection.TargetInvocationException tie)
            when (tie.InnerException is global::System.Runtime.InteropServices.COMException)
        {
            // Expected on CI/non-WinUI thread: the V1 descriptor path constructs
            // VariableSizedWrapGrid via the generic new() constraint, which wraps
            // the off-thread ctor COMException in a TargetInvocationException.
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Empty WrapGrid
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void WrapGrid_Empty_Has_No_Children()
    {
        var el = WrapGrid();
        Assert.Empty(el.Children);
    }

    [Fact]
    public void WrapGrid_WithKey_Sets_Key()
    {
        var el = WrapGrid(TextBlock("A")).WithKey("grid-1");
        Assert.Equal("grid-1", el.Key);
    }
}
