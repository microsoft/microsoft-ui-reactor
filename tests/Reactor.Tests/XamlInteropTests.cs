using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for Feature 7: Reverse Embedding (XAML inside Reactor).
/// Validates XamlPageElement, XamlHostElement records and XamlInterop registration.
/// Pure logic tests — no WinUI control creation (no XAML thread needed).
/// </summary>
public class XamlInteropTests
{
    // ── XamlPageElement record tests ──────────────────────────────

    [Fact]
    public void XamlPageElement_Is_Element()
    {
        Element el = new XamlPageElement(typeof(Page));
        Assert.IsAssignableFrom<Element>(el);
    }

    [Fact]
    public void XamlPageElement_Stores_PageType()
    {
        var el = new XamlPageElement(typeof(Page));
        Assert.Equal(typeof(Page), el.PageType);
    }

    [Fact]
    public void XamlPageElement_Stores_Parameter()
    {
        var el = new XamlPageElement(typeof(Page), "item-42");
        Assert.Equal("item-42", el.Parameter);
    }

    [Fact]
    public void XamlPageElement_Default_Parameter_Is_Null()
    {
        var el = new XamlPageElement(typeof(Page));
        Assert.Null(el.Parameter);
    }

    [Fact]
    public void XamlPageElement_Record_Equality()
    {
        var a = new XamlPageElement(typeof(Page), "param");
        var b = new XamlPageElement(typeof(Page), "param");
        Assert.Equal(a, b);
    }

    [Fact]
    public void XamlPageElement_Record_Inequality_Different_Type()
    {
        var a = new XamlPageElement(typeof(Page));
        var b = new XamlPageElement(typeof(Frame)); // different type
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void XamlPageElement_Record_Inequality_Different_Param()
    {
        var a = new XamlPageElement(typeof(Page), "a");
        var b = new XamlPageElement(typeof(Page), "b");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void XamlPageElement_Supports_Key()
    {
        var el = new XamlPageElement(typeof(Page)) { Key = "page-1" };
        Assert.Equal("page-1", el.Key);
    }

    [Fact]
    public void XamlPageElement_Can_Be_Wrapped_In_ModifiedElement()
    {
        var inner = new XamlPageElement(typeof(Page));
        var modified = new ModifiedElement(inner, new ElementModifiers { Margin = new Thickness(8) });
        Assert.Equal(inner, modified.Inner);
    }

    // ── XamlHostElement record tests ─────────────────────────────

    [Fact]
    public void XamlHostElement_Is_Element()
    {
        Element el = new XamlHostElement(() => null!);
        Assert.IsAssignableFrom<Element>(el);
    }

    [Fact]
    public void XamlHostElement_Stores_Factory()
    {
        Func<FrameworkElement> factory = () => null!;
        var el = new XamlHostElement(factory);
        Assert.Same(factory, el.Factory);
    }

    [Fact]
    public void XamlHostElement_Stores_Updater()
    {
        Action<FrameworkElement> updater = _ => { };
        var el = new XamlHostElement(() => null!, updater);
        Assert.Same(updater, el.Updater);
    }

    [Fact]
    public void XamlHostElement_Default_Updater_Is_Null()
    {
        var el = new XamlHostElement(() => null!);
        Assert.Null(el.Updater);
    }

    [Fact]
    public void XamlHostElement_Supports_TypeKey()
    {
        var el = new XamlHostElement(() => null!) { TypeKey = "MyControl" };
        Assert.Equal("MyControl", el.TypeKey);
    }

    [Fact]
    public void XamlHostElement_Supports_Key()
    {
        var el = new XamlHostElement(() => null!) { Key = "host-1" };
        Assert.Equal("host-1", el.Key);
    }

    // ── CanUpdate with reverse-embedding elements ─────────────────

    [Fact]
    public void CanUpdate_Same_XamlPageElement_Type_Returns_True()
    {
        var reconciler = new Reconciler();
        var a = new XamlPageElement(typeof(Page), "a");
        var b = new XamlPageElement(typeof(Page), "b");
        Assert.True(reconciler.CanUpdate(a, b));
    }

    [Fact]
    public void CanUpdate_XamlPageElement_Vs_XamlHostElement_Returns_False()
    {
        var reconciler = new Reconciler();
        var page = new XamlPageElement(typeof(Page));
        Element host = new XamlHostElement(() => null!);
        Assert.False(reconciler.CanUpdate(page, host));
    }

    [Fact]
    public void CanUpdate_XamlPageElement_Vs_Builtin_Returns_False()
    {
        var reconciler = new Reconciler();
        var page = new XamlPageElement(typeof(Page));
        var text = new TextBlockElement("hello");
        Assert.False(reconciler.CanUpdate(page, text));
    }

    [Fact]
    public void CanUpdate_Same_XamlHostElement_Type_Returns_True()
    {
        var reconciler = new Reconciler();
        var a = new XamlHostElement(() => null!);
        var b = new XamlHostElement(() => null!);
        Assert.True(reconciler.CanUpdate(a, b));
    }

    // ── XamlInterop.Register ──────────────────────────────────────

    [Fact]
    public void Register_Does_Not_Throw()
    {
        var reconciler = new Reconciler();
        XamlInterop.Register(reconciler);
    }

    [Fact]
    public void Register_Adds_XamlPageElement_To_Registry()
    {
        // §14 Phase 4 (§4.0.5 + §4.1): with V1 ON by default, XamlPage is owned
        // by V1 auto-registration (the V1 handler registry), so Register is an
        // idempotent no-op for it. Under the V1-OFF escape hatch, Register
        // populates the legacy _typeRegistry instead. IsElementTypeRegistered
        // unifies both, so it is the flag-independent assertion.
        var reconciler = new Reconciler();
        XamlInterop.Register(reconciler);

        Assert.True(reconciler.IsElementTypeRegistered(typeof(XamlPageElement)));
    }

    [Fact]
    public void Register_Adds_XamlHostElement_To_Registry()
    {
        // See Register_Adds_XamlPageElement_To_Registry — flag-independent check.
        var reconciler = new Reconciler();
        XamlInterop.Register(reconciler);

        Assert.True(reconciler.IsElementTypeRegistered(typeof(XamlHostElement)));
    }

    [Fact]
    public void Register_XamlPageElement_Mount_Is_Dispatched()
    {
        var reconciler = new Reconciler();
        XamlInterop.Register(reconciler);

        // Mount dispatches to the registered handler which tries to create a Frame.
        // Frame creation throws COMException off the XAML thread — that proves dispatch.
        var el = new XamlPageElement(typeof(Page));
        Assert.ThrowsAny<Exception>(() => reconciler.Mount(el, () => { }));
    }

    [Fact]
    public void Register_XamlHostElement_Mount_Calls_Factory()
    {
        var reconciler = new Reconciler();
        XamlInterop.Register(reconciler);

        bool factoryCalled = false;
        var el = new XamlHostElement(() =>
        {
            factoryCalled = true;
            throw new InvalidOperationException("Factory dispatched");
        });

        Assert.Throws<InvalidOperationException>(() => reconciler.Mount(el, () => { }));
        Assert.True(factoryCalled);
    }

    [Fact]
    public void Register_Twice_Is_Idempotent()
    {
        // Spec 047 §14 Phase 4 (4.0.5): XamlInterop.Register now skips any
        // element type that is already registered (whether by a prior Register
        // call or by V1 auto-registration), so calling it more than once per
        // Reconciler is a safe no-op rather than tripping the §13 Q17
        // duplicate-registration guard.
        var reconciler = new Reconciler();
        XamlInterop.Register(reconciler);
        var ex = Record.Exception(() => XamlInterop.Register(reconciler));
        Assert.Null(ex);
    }

    // ── XamlHostElement record with TypeKey ───────────────────────

    [Fact]
    public void XamlHostElement_TypeKey_Equality()
    {
        // Two XamlHostElements with different factories but same TypeKey
        // should be considered the same "type" by CanUpdate (same record type).
        var reconciler = new Reconciler();
        var a = new XamlHostElement(() => null!) { TypeKey = "Counter" };
        var b = new XamlHostElement(() => null!) { TypeKey = "Counter" };
        Assert.True(reconciler.CanUpdate(a, b));
    }

    [Fact]
    public void XamlHostElement_Record_Equality_Same_TypeKey()
    {
        var factory = new Func<FrameworkElement>(() => null!);
        var a = new XamlHostElement(factory) { TypeKey = "X" };
        var b = new XamlHostElement(factory) { TypeKey = "X" };
        Assert.Equal(a, b);
    }

    [Fact]
    public void XamlHostElement_Record_Inequality_Different_TypeKey()
    {
        var factory = new Func<FrameworkElement>(() => null!);
        var a = new XamlHostElement(factory) { TypeKey = "A" };
        var b = new XamlHostElement(factory) { TypeKey = "B" };
        Assert.NotEqual(a, b);
    }

    // ── ModifiedElement wrapping ──────────────────────────────────

    [Fact]
    public void CanUpdate_Modified_XamlPageElements_Returns_True()
    {
        var reconciler = new Reconciler();
        var a = new ModifiedElement(new XamlPageElement(typeof(Page), "a"), new ElementModifiers());
        var b = new ModifiedElement(new XamlPageElement(typeof(Page), "b"), new ElementModifiers());
        Assert.True(reconciler.CanUpdate(a, b));
    }

    [Fact]
    public void CanUpdate_Modified_XamlHostElements_Returns_True()
    {
        var reconciler = new Reconciler();
        var a = new ModifiedElement(new XamlHostElement(() => null!), new ElementModifiers());
        var b = new ModifiedElement(new XamlHostElement(() => null!), new ElementModifiers());
        Assert.True(reconciler.CanUpdate(a, b));
    }
}
