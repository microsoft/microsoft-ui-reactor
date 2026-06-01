using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for Reconciler mount, update, and reconcile logic.
/// Uses the RegisterType API with custom elements to test mount/update dispatch
/// without requiring a XAML Application context.
/// Grid definition parsing tests are pure logic (no UI thread needed).
/// </summary>
public class ReconcilerMountUpdateTests
{
    private static readonly Action NoOp = () => { };

    // ── Custom element for testing mount/update dispatch ────────

    private record TestWidgetElement(string Label, int Value = 0) : Element;

    // ════════════════════════════════════════════════════════════════
    //  Mount dispatch — registered type handler is invoked
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Mount_Custom_Type_Invokes_Handler_With_Correct_Element()
    {
        var reconciler = new Reconciler();
        TestWidgetElement? receivedElement = null;

        reconciler.RegisterType<TestWidgetElement, UIElement>(
            mount: (r, el, rerender) =>
            {
                receivedElement = el;
                throw new InvalidOperationException("Mounted");
            },
            update: (r, oldEl, newEl, ctrl, rerender) => null);

        var element = new TestWidgetElement("Hello", 42);
        Assert.Throws<InvalidOperationException>(() => reconciler.Mount(element, NoOp));
        Assert.NotNull(receivedElement);
        Assert.Equal("Hello", receivedElement!.Label);
        Assert.Equal(42, receivedElement.Value);
    }

    [Fact]
    public void Mount_Custom_Type_Receives_Rerender_Callback()
    {
        var reconciler = new Reconciler();
        Action? receivedRerender = null;

        reconciler.RegisterType<TestWidgetElement, UIElement>(
            mount: (r, el, rerender) =>
            {
                receivedRerender = rerender;
                throw new InvalidOperationException("Mounted");
            },
            update: (r, oldEl, newEl, ctrl, rerender) => null);

        Action myRerender = () => { };
        Assert.Throws<InvalidOperationException>(() => reconciler.Mount(new TestWidgetElement("X"), myRerender));
        Assert.Same(myRerender, receivedRerender);
    }

    // ════════════════════════════════════════════════════════════════
    //  Reconcile — CanUpdate, type changes, null handling
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void CanUpdate_Same_Type_Same_Key_Returns_True()
    {
        var reconciler = new Reconciler();
        Assert.True(reconciler.CanUpdate(new TextBlockElement("a"), new TextBlockElement("b")));
    }

    [Fact]
    public void CanUpdate_Same_Type_Different_Key_Returns_False()
    {
        var reconciler = new Reconciler();
        var a = new TextBlockElement("a") { Key = "1" };
        var b = new TextBlockElement("b") { Key = "2" };
        Assert.False(reconciler.CanUpdate(a, b));
    }

    [Fact]
    public void CanUpdate_Different_Types_Returns_False()
    {
        var reconciler = new Reconciler();
        Assert.False(reconciler.CanUpdate(new TextBlockElement("a"), new ButtonElement("b")));
    }

    [Fact]
    public void Reconcile_Null_NewElement_Returns_Null()
    {
        var reconciler = new Reconciler();
        // Reconcile with no existing control and null new element
        var result = reconciler.Reconcile(null, null, null, NoOp);
        Assert.Null(result);
    }

    [Fact]
    public void Reconcile_EmptyElement_Returns_Null()
    {
        var reconciler = new Reconciler();
        var result = reconciler.Reconcile(null, new EmptyElement(), null, NoOp);
        Assert.Null(result);
    }

    // Note: ParseColumnDef/ParseRowDef create ColumnDefinition/RowDefinition objects
    // which require a XAML Application context. Those are tested via Reactor.TestApp.

    // ════════════════════════════════════════════════════════════════
    //  ParseSymbol
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ParseSymbol_Known_Name()
    {
        var symbol = IconResolver.ParseSymbol("Accept");
        Assert.Equal(Symbol.Accept, symbol);
    }

    [Fact]
    public void ParseSymbol_CaseInsensitive()
    {
        var symbol = IconResolver.ParseSymbol("accept");
        Assert.Equal(Symbol.Accept, symbol);
    }

    [Fact]
    public void ParseSymbol_Unknown_Returns_Placeholder()
    {
        var symbol = IconResolver.ParseSymbol("NonExistentSymbol");
        Assert.Equal(Symbol.Placeholder, symbol);
    }
}
