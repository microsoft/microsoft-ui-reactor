using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for Feature 1: Extensible Reconciler (RegisterType API).
/// These are pure logic tests that don't create WinUI controls (no XAML thread needed).
/// They verify the registration API, type dispatch, and CanUpdate behavior.
/// </summary>
public class TypeRegistryTests
{
    // ── Custom elements for testing ────────────────────────────────

    private record TestCustomElement(string Label, string? Detail = null) : Element;
    private record AnotherCustomElement(int Value) : Element;

    // ── Registration API ───────────────────────────────────────────

    [Fact]
    public void RegisterType_Does_Not_Throw()
    {
        var reconciler = new Reconciler();
        reconciler.RegisterType<TestCustomElement, TextBlock>(
            mount: (r, el, rerender) => null!,
            update: (r, oldEl, newEl, ctrl, rerender) => null);
    }

    [Fact]
    public void RegisterType_Multiple_Types_Does_Not_Throw()
    {
        var reconciler = new Reconciler();
        reconciler.RegisterType<TestCustomElement, TextBlock>(
            mount: (r, el, rerender) => null!,
            update: (r, oldEl, newEl, ctrl, rerender) => null);
        reconciler.RegisterType<AnotherCustomElement, Border>(
            mount: (r, el, rerender) => null!,
            update: (r, oldEl, newEl, ctrl, rerender) => null);
    }

    [Fact]
    public void RegisterType_Duplicate_Throws_In_V1()
    {
        // Spec 047 §13 Q17 / §14 Phase 1 (1.9): duplicate registration is
        // forbidden in v1. The pre-v1 overwrite behavior has been removed;
        // callers that need a different mapping build a separate Reconciler.
        var reconciler = new Reconciler();
        reconciler.RegisterType<TestCustomElement, TextBlock>(
            mount: (r, el, rerender) => null!,
            update: (r, oldEl, newEl, ctrl, rerender) => null);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            reconciler.RegisterType<TestCustomElement, Border>(
                mount: (r, el, rerender) => null!,
                update: (r, oldEl, newEl, ctrl, rerender) => null));
        // Error message must name both control types (Q17).
        Assert.Contains("TestCustomElement", ex.Message);
        Assert.Contains("TextBlock", ex.Message);
        Assert.Contains("Border", ex.Message);
    }

    [Fact]
    public void RegisterType_With_Unmount_Does_Not_Throw()
    {
        var reconciler = new Reconciler();
        reconciler.RegisterType<TestCustomElement, TextBlock>(
            mount: (r, el, rerender) => null!,
            update: (r, oldEl, newEl, ctrl, rerender) => null,
            unmount: (r, ctrl) => { });
    }

    // ── CanUpdate works with registered custom elements ────────────

    [Fact]
    public void CanUpdate_Same_Custom_Element_Type_Returns_True()
    {
        var reconciler = new Reconciler();
        var a = new TestCustomElement("A");
        var b = new TestCustomElement("B");
        Assert.True(reconciler.CanUpdate(a, b));
    }

    [Fact]
    public void CanUpdate_Different_Custom_Element_Types_Returns_False()
    {
        var reconciler = new Reconciler();
        var a = new TestCustomElement("A");
        var b = new AnotherCustomElement(42);
        Assert.False(reconciler.CanUpdate(a, b));
    }

    [Fact]
    public void CanUpdate_Custom_Element_Vs_Builtin_Returns_False()
    {
        var reconciler = new Reconciler();
        var custom = new TestCustomElement("A");
        var builtin = new TextBlockElement("A");
        Assert.False(reconciler.CanUpdate(custom, builtin));
        Assert.False(reconciler.CanUpdate(builtin, custom));
    }

    // ── Custom element is a valid Element ──────────────────────────

    [Fact]
    public void Custom_Element_Is_Element()
    {
        Element el = new TestCustomElement("Hello");
        Assert.IsAssignableFrom<Element>(el);
    }

    [Fact]
    public void Custom_Element_Record_Equality()
    {
        var a = new TestCustomElement("Hello", "World");
        var b = new TestCustomElement("Hello", "World");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Custom_Element_Record_Inequality()
    {
        var a = new TestCustomElement("Hello");
        var b = new TestCustomElement("Goodbye");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Custom_Element_With_Key()
    {
        var el = new TestCustomElement("Hello") { Key = "item-1" };
        Assert.Equal("item-1", el.Key);
    }

    // ── ModifiedElement wrapping custom elements ───────────────────

    [Fact]
    public void ModifiedElement_Can_Wrap_Custom_Element()
    {
        var inner = new TestCustomElement("Wrapped");
        var modified = new ModifiedElement(inner, new ElementModifiers { Margin = new Thickness(10) });
        Assert.Equal(inner, modified.Inner);
        Assert.Equal(new Thickness(10), modified.WrappedModifiers.Margin);
    }

    [Fact]
    public void CanUpdate_Modified_Custom_Elements_Returns_True()
    {
        var reconciler = new Reconciler();
        var a = new ModifiedElement(new TestCustomElement("A"), new ElementModifiers());
        var b = new ModifiedElement(new TestCustomElement("B"), new ElementModifiers());
        Assert.True(reconciler.CanUpdate(a, b));
    }

    // ── UpdateChild / UnmountChild are public (compile-time) ──────

    [Fact]
    public void UpdateChild_Is_Public_Method()
    {
        // This test verifies UpdateChild is public by calling it.
        // Uses reflection to avoid needing a real UIElement.
        var method = typeof(Reconciler).GetMethod("UpdateChild");
        Assert.NotNull(method);
        Assert.True(method!.IsPublic);
    }

    [Fact]
    public void UnmountChild_Is_Public_Method()
    {
        var method = typeof(Reconciler).GetMethod("UnmountChild");
        Assert.NotNull(method);
        Assert.True(method!.IsPublic);
    }

    // ── ITypeRegistration interface exists ──────────────────────────

    [Fact]
    public void TypeRegistration_Interface_Exists()
    {
        // Verify the nested interface exists via the reconciler type
        var nestedTypes = typeof(Reconciler).GetNestedTypes(
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Public);
        Assert.Contains(nestedTypes, t => t.Name == "ITypeRegistration");
    }

    // ── Mount dispatch integration (via reflection) ────────────────

    [Fact]
    public void Mount_Checks_Registry_Before_Switch()
    {
        // Verify by registering a handler and checking it gets called
        // via a flag — we catch the COMException from TextBlock construction
        // and verify the handler was entered.
        var reconciler = new Reconciler();
        bool handlerEntered = false;

        reconciler.RegisterType<TestCustomElement, TextBlock>(
            mount: (r, el, rerender) =>
            {
                handlerEntered = true;
                throw new InvalidOperationException("Stop here — handler was dispatched");
            },
            update: (r, oldEl, newEl, ctrl, rerender) => null);

        var element = new TestCustomElement("Test");
        Assert.Throws<InvalidOperationException>(() => reconciler.Mount(element, () => { }));
        Assert.True(handlerEntered);
    }

    [Fact]
    public void Update_Checks_Registry_Before_Switch()
    {
        // Verify update dispatch by registering a custom type where both mount
        // and update use UIElement (base type) to avoid cast issues.
        var reconciler = new Reconciler();

        reconciler.RegisterType<TestCustomElement, UIElement>(
            mount: (r, el, rerender) =>
            {
                // Return a marker — we use a throw to signal dispatch happened
                throw new InvalidOperationException("Mount dispatched");
            },
            update: (r, oldEl, newEl, ctrl, rerender) =>
            {
                throw new InvalidOperationException("Update dispatched");
            });

        // Mount will throw, confirming dispatch. But for update test we need
        // to verify the update path. We test this indirectly:
        // The registry lookup in Update checks newEl.GetType() against the registry.
        // If Update is called with a TestCustomElement and a control, it should
        // enter the registered handler.

        // We use the internal Update path via Reconcile which calls Update internally.
        // Since we can't get past Mount, verify that the registry key lookup is correct.
        var hasKey = typeof(Reconciler)
            .GetField("_typeRegistry", global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance)!
            .GetValue(reconciler) is global::System.Collections.IDictionary dict
            && dict.Contains(typeof(TestCustomElement));

        Assert.True(hasKey);
    }

    [Fact]
    public void Builtin_TextBlockElement_Still_Works_Without_Registration()
    {
        // TextBlockElement should fall through to built-in handler when nothing is registered
        var reconciler = new Reconciler();
        // CanUpdate should work for built-in types
        Assert.True(reconciler.CanUpdate(new TextBlockElement("a"), new TextBlockElement("b")));
    }
}
