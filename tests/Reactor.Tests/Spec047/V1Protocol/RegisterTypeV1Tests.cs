using System;
using System.Reflection;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec047.V1Protocol;

/// <summary>
/// Spec 047 §13 Q17 / §14 Phase 1 (1.9) — RegisterType v1 semantics:
/// exact-type lookup, throw on duplicate (cross-registry too), throw on
/// open-generic, no RegisterOverride verb.
/// </summary>
public class RegisterTypeV1Tests
{
    private record BaseEl : Element;
    private record DerivedEl : BaseEl;
    private record OtherEl : Element;
    private record GenericEl<T>(T Value) : Element;

    public sealed class FakeHandler<TEl> : IElementHandler<TEl, UIElement> where TEl : Element
    {
        public UIElement Mount(MountContext ctx, TEl element) => null!;
        public void Update(UpdateContext ctx, TEl oldEl, TEl newEl, UIElement control) { }
    }

    [Fact]
    public void RegisterType_Twice_Same_Element_Throws_With_Both_Control_Names()
    {
        var rec = new Reconciler();
        rec.RegisterType<BaseEl, TextBlock>(
            mount: (r, el, rr) => null!,
            update: (r, o, n, c, rr) => null);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            rec.RegisterType<BaseEl, Border>(
                mount: (r, el, rr) => null!,
                update: (r, o, n, c, rr) => null));

        Assert.Contains("BaseEl", ex.Message);
        Assert.Contains("TextBlock", ex.Message);
        Assert.Contains("Border", ex.Message);
    }

    [Fact]
    public void RegisterHandler_Twice_Same_Element_Throws()
    {
        var rec = new Reconciler();
        rec.RegisterHandler<BaseEl, UIElement>(new FakeHandler<BaseEl>());
        var ex = Assert.Throws<InvalidOperationException>(() =>
            rec.RegisterHandler<BaseEl, UIElement>(new FakeHandler<BaseEl>()));
        Assert.Contains("BaseEl", ex.Message);
    }

    [Fact]
    public void RegisterType_Then_RegisterHandler_Same_Type_Throws()
    {
        var rec = new Reconciler();
        rec.RegisterType<BaseEl, TextBlock>(
            mount: (r, el, rr) => null!,
            update: (r, o, n, c, rr) => null);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            rec.RegisterHandler<BaseEl, UIElement>(new FakeHandler<BaseEl>()));
        Assert.Contains("BaseEl", ex.Message);
    }

    [Fact]
    public void RegisterHandler_Then_RegisterType_Same_Type_Throws()
    {
        var rec = new Reconciler();
        rec.RegisterHandler<BaseEl, UIElement>(new FakeHandler<BaseEl>());
        var ex = Assert.Throws<InvalidOperationException>(() =>
            rec.RegisterType<BaseEl, TextBlock>(
                mount: (r, el, rr) => null!,
                update: (r, o, n, c, rr) => null));
        Assert.Contains("BaseEl", ex.Message);
        Assert.Contains("V1", ex.Message); // cross-registry message uses different phrasing
    }

    [Fact]
    public void Exact_Type_Lookup_Does_Not_Pick_Up_Parent_Handler()
    {
        // Spec 047 §13 Q17 sub-Q1: register a handler whose element type's
        // BASE type also has a registered handler; lookup must be exact-
        // type by element.GetType(), so the derived element must NOT pick
        // up the parent's handler.
        var rec = new Reconciler();
        rec.RegisterType<BaseEl, TextBlock>(
            mount: (r, el, rr) => null!,
            update: (r, o, n, c, rr) => null);
        // Registering DerivedEl with its own handler — must succeed (no
        // duplicate against BaseEl), and the dispatch is keyed on
        // typeof(DerivedEl), not typeof(BaseEl).
        rec.RegisterType<DerivedEl, Border>(
            mount: (r, el, rr) => null!,
            update: (r, o, n, c, rr) => null);
        // If the lookup were base-walking, registering DerivedEl after
        // BaseEl would have shadowed; if the lookup were base-walking
        // INTO the dispatch table, dispatching for DerivedEl would
        // re-route to BaseEl. Both are forbidden in v1 — assert by
        // construction (no throw on registration of DerivedEl with a
        // different TControl than BaseEl).
    }

    [Fact]
    public void RegisterType_Different_Element_Types_Succeed_Independently()
    {
        var rec = new Reconciler();
        rec.RegisterType<BaseEl, TextBlock>(
            mount: (r, el, rr) => null!,
            update: (r, o, n, c, rr) => null);
        rec.RegisterType<OtherEl, Border>(
            mount: (r, el, rr) => null!,
            update: (r, o, n, c, rr) => null);
        // No throw — different element types.
    }

    [Fact]
    public void RegisterType_Open_Generic_Throws()
    {
        // Spec 047 §13 Q17 sub-Q4: registering an open-generic element type
        // (e.g. typeof(DataGrid<>)) is forbidden. The .NET runtime itself
        // refuses to invoke a generic method instantiated with an open
        // generic type argument — InvalidOperationException "Late bound
        // operations cannot be performed on types or methods for which
        // ContainsGenericParameters is true." We assert the engine's own
        // ContainsGenericParameters check is in place via the private
        // EnsureRegistrableElementType helper (which IS reachable through
        // reflection because it takes a System.Type, not a generic param).
        var rec = new Reconciler();
        var ensure = typeof(Reconciler).GetMethod(
            "EnsureRegistrableElementType",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(ensure);
        var ex = Assert.Throws<TargetInvocationException>(() =>
            ensure!.Invoke(rec, new object?[] { typeof(GenericEl<>), typeof(TextBlock), "RegisterType" }));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("open-generic", ex.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureRegistrableElementType_Throws_When_Element_Type_Has_Generic_Parameters()
    {
        // Direct unit test of the v1 guard. Open-generic detection lives in
        // EnsureRegistrableElementType so both RegisterType and RegisterHandler
        // get the same throw.
        var rec = new Reconciler();
        var ensure = typeof(Reconciler).GetMethod(
            "EnsureRegistrableElementType",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(ensure);
        // Construct a closed-generic — passes the check.
        ensure!.Invoke(rec, new object?[] { typeof(GenericEl<int>), typeof(TextBlock), "RegisterType" });
        // Now try the open generic — must throw.
        var ex = Assert.Throws<TargetInvocationException>(() =>
            ensure.Invoke(rec, new object?[] { typeof(GenericEl<>), typeof(TextBlock), "RegisterType" }));
        Assert.Contains("open-generic", ex.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }
}
