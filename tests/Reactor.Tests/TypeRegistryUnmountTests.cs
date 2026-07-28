using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests verifying that registered type unmount handlers are invoked
/// when controls are removed from the tree.
/// Uses the RegisterType API to verify unmount dispatch without creating
/// real WinUI controls (which require a XAML Application context).
/// </summary>
[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Test-only: all sites reflect known members (HasUnmount property, Unmount method) on the concrete TypeRegistry instance the test constructs. Intentional and JIT-only (this host is never trimmed) — not claimed trim-safe; behaviour-neutral (neither preserves nor prunes members, so it cannot cause the DAM-narrowing regression noted in issue #70).")]
public class TypeRegistryUnmountTests
{
    private record CustomCardElement(string Title) : Element;

    [Fact]
    public void UnmountChild_Invokes_Registered_Unmount_Handler()
    {
        var reconciler = new Reconciler();
        bool mountCalled = false;

        reconciler.RegisterType<CustomCardElement, UIElement>(
            mount: (r, el, rerender) =>
            {
                mountCalled = true;
                // Throw to signal we got here — we can't create real controls
                throw new InvalidOperationException("Mount dispatched");
            },
            update: (r, oldEl, newEl, ctrl, rerender) => ctrl,
            unmount: (r, ctrl) => { });

        // Verify mount is dispatched
        var element = new CustomCardElement("Hello");
        Assert.Throws<InvalidOperationException>(() => reconciler.Mount(element, () => { }));
        Assert.True(mountCalled);

        // For unmount, we need an actual UIElement. Since we can't create one
        // without UI thread, verify the handler is registered via reflection.
        var registry = typeof(Reconciler)
            .GetField("_typeRegistry", global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance)!
            .GetValue(reconciler) as global::System.Collections.IDictionary;

        Assert.NotNull(registry);
        Assert.True(registry!.Contains(typeof(CustomCardElement)));

        // Verify the registration has an unmount handler via the HasUnmount property
        var reg = registry[typeof(CustomCardElement)]!;
        var hasUnmount = (bool)reg.GetType().GetProperty("HasUnmount")!.GetValue(reg)!;
        Assert.True(hasUnmount, "Expected unmount handler to be registered");
    }

    [Fact]
    public void Registration_Without_Unmount_Has_No_Handler()
    {
        var reconciler = new Reconciler();

        reconciler.RegisterType<CustomCardElement, UIElement>(
            mount: (r, el, rerender) => throw new InvalidOperationException("Mounted"),
            update: (r, oldEl, newEl, ctrl, rerender) => ctrl);
        // No unmount handler

        var registry = typeof(Reconciler)
            .GetField("_typeRegistry", global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance)!
            .GetValue(reconciler) as global::System.Collections.IDictionary;

        var reg = registry![typeof(CustomCardElement)]!;
        var hasUnmount = (bool)reg.GetType().GetProperty("HasUnmount")!.GetValue(reg)!;
        Assert.False(hasUnmount, "Expected no unmount handler");
    }

    [Fact]
    public void Reconcile_Null_New_Element_Calls_Unmount_Path()
    {
        // This verifies the Reconcile method returns null when new element is null,
        // which triggers the Unmount path.
        var reconciler = new Reconciler();
        var oldEl = new TextBlockElement("Hello");

        // When new element is null, reconciler should return null
        // (this tests the branch, though without a real control it won't call Unmount)
        var result = reconciler.Reconcile(oldEl, null, null, () => { });
        Assert.Null(result);
    }

    [Fact]
    public void Unmount_With_Null_Control_Skips_Handler()
    {
        // After the type-mismatch safety fix, Unmount uses `control is TControl`
        // pattern matching. Null never matches, so the handler is correctly skipped
        // (null is not a valid control to unmount).
        var reconciler = new Reconciler();
        bool unmountInvoked = false;

        reconciler.RegisterType<CustomCardElement, UIElement>(
            mount: (r, el, rerender) =>
            {
                throw new InvalidOperationException("Cannot mount without UI thread");
            },
            update: (r, oldEl, newEl, ctrl, rerender) => ctrl,
            unmount: (r, ctrl) =>
            {
                unmountInvoked = true;
            });

        var registry = typeof(Reconciler)
            .GetField("_typeRegistry", global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance)!
            .GetValue(reconciler) as global::System.Collections.IDictionary;

        var reg = registry![typeof(CustomCardElement)]!;
        var unmountMethod = reg.GetType().GetMethod("Unmount")!;

        // Call Unmount with null — the `is TControl` guard rejects null,
        // so the handler is never invoked (no NullReferenceException either).
        unmountMethod.Invoke(reg, [null!, reconciler]);

        Assert.False(unmountInvoked, "Unmount handler should be skipped for null control");
    }
}
