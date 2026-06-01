using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec048.V1Protocol;

/// <summary>
/// Spec 048 §3.4 — contract tests for the base-derived shims
/// <see cref="RegBase{TBase,TControl,THandler}"/> and
/// <see cref="RegBaseDecorator{TBase,THandler}"/>. Mirrors
/// <see cref="RegDecoratorTests"/>; every test uses a fresh element/handler
/// pair so the closed-generic cctor under test fires fresh (the CLR runs
/// each closed generic's cctor at most once per process, so reusing the
/// same shim instantiation across tests would silently no-op the second
/// touch). Assertions are <i>deltas</i> on the base/derived slot the test
/// owns (<see cref="ControlRegistry.ContainsBase"/> /
/// <see cref="ControlRegistry.ContainsForType"/>) rather than absolute
/// registry counts.
/// </summary>
[Collection(nameof(ControlRegistryTestCollection))]
public class RegBaseTests
{
    // ── Value-handler shim (RegBase) ─────────────────────────────────

    public abstract record FirstTouchBaseElement(string Tag) : Element;
    public record FirstTouchDerivedElement(string Tag) : FirstTouchBaseElement(Tag);

    public sealed class FirstTouchBaseHandler : IElementHandler<FirstTouchBaseElement, UIElement>
    {
        public static int CtorCalls;
        public FirstTouchBaseHandler() => Interlocked.Increment(ref CtorCalls);
        public UIElement Mount(MountContext ctx, FirstTouchBaseElement element) => null!;
        public void Update(UpdateContext ctx, FirstTouchBaseElement oldEl, FirstTouchBaseElement newEl, UIElement control) { }
    }

    [Fact]
    public void RegBase_First_Touch_Registers_Base_Exactly_Once()
    {
        Assert.False(ControlRegistry.ContainsBase(typeof(FirstTouchBaseElement)));

        _ = RegBase<FirstTouchBaseElement, UIElement, FirstTouchBaseHandler>.Done;

        Assert.True(ControlRegistry.ContainsBase(typeof(FirstTouchBaseElement)));
        Assert.True(ControlRegistry.ContainsForType(typeof(FirstTouchDerivedElement)));

        // Registry stores the factory; does not invoke it. Handler ctor
        // must not have run yet (lazy until first dispatch hit).
        Assert.Equal(0, FirstTouchBaseHandler.CtorCalls);
    }

    public abstract record RepeatBaseElement(string Tag) : Element;
    public record RepeatDerivedElement(string Tag) : RepeatBaseElement(Tag);
    public sealed class RepeatBaseHandler : IElementHandler<RepeatBaseElement, UIElement>
    {
        public UIElement Mount(MountContext ctx, RepeatBaseElement element) => null!;
        public void Update(UpdateContext ctx, RepeatBaseElement oldEl, RepeatBaseElement newEl, UIElement control) { }
    }

    [Fact]
    public void RegBase_Repeated_Reads_Of_Done_Do_Not_Re_Register()
    {
        Assert.False(ControlRegistry.ContainsBase(typeof(RepeatBaseElement)));

        for (var i = 0; i < 50; i++)
        {
            _ = RegBase<RepeatBaseElement, UIElement, RepeatBaseHandler>.Done;
        }

        Assert.True(ControlRegistry.ContainsBase(typeof(RepeatBaseElement)));
        Assert.True(ControlRegistry.ContainsForType(typeof(RepeatDerivedElement)));
    }

    public abstract record DispatchBaseElement(string Tag) : Element;
    public record DispatchDerivedElementA(string Tag) : DispatchBaseElement(Tag);
    public record DispatchDerivedElementB(string Tag) : DispatchBaseElement(Tag);
    public sealed class DispatchBaseHandler : IElementHandler<DispatchBaseElement, UIElement>
    {
        public UIElement Mount(MountContext ctx, DispatchBaseElement element) => null!;
        public void Update(UpdateContext ctx, DispatchBaseElement oldEl, DispatchBaseElement newEl, UIElement control) { }
    }

    [Fact]
    public void RegBase_Resolves_Multiple_Derived_Types_To_Same_Base_Entry()
    {
        _ = RegBase<DispatchBaseElement, UIElement, DispatchBaseHandler>.Done;

        var rec = new Reconciler();
        Assert.True(rec.TryResolveFromControlRegistry(typeof(DispatchDerivedElementA), out _));
        Assert.True(rec.TryResolveFromControlRegistry(typeof(DispatchDerivedElementB), out _));
        // Both derived types resolved through the SAME base-registered Func
        // (registry-level identity).
        Assert.True(ControlRegistry.TryResolve(typeof(DispatchDerivedElementA), out var factory1));
        Assert.True(ControlRegistry.TryResolve(typeof(DispatchDerivedElementB), out var factory2));
        Assert.Same(factory1, factory2);
    }

    // ── Decorator-handler shim (RegBaseDecorator) ────────────────────

    public abstract record FirstTouchBaseDecoratorElement(string Tag) : Element;
    public record FirstTouchDerivedDecoratorElement(string Tag) : FirstTouchBaseDecoratorElement(Tag);

    public sealed class FirstTouchBaseDecoratorHandler : IDecoratorElementHandler<FirstTouchBaseDecoratorElement>
    {
        public static int CtorCalls;
        public FirstTouchBaseDecoratorHandler() => Interlocked.Increment(ref CtorCalls);
        public UIElement Mount(MountContext ctx, FirstTouchBaseDecoratorElement element) => null!;
        public UIElement Update(UpdateContext ctx, FirstTouchBaseDecoratorElement oldEl, FirstTouchBaseDecoratorElement newEl, UIElement control) => control;
        public V1UnmountDisposition Unmount(UnmountContext ctx, FirstTouchBaseDecoratorElement? element, UIElement control)
            => V1UnmountDisposition.CollectSelf;
    }

    [Fact]
    public void RegBaseDecorator_First_Touch_Registers_Base_Exactly_Once()
    {
        Assert.False(ControlRegistry.ContainsBase(typeof(FirstTouchBaseDecoratorElement)));

        _ = RegBaseDecorator<FirstTouchBaseDecoratorElement, FirstTouchBaseDecoratorHandler>.Done;

        Assert.True(ControlRegistry.ContainsBase(typeof(FirstTouchBaseDecoratorElement)));
        Assert.True(ControlRegistry.ContainsForType(typeof(FirstTouchDerivedDecoratorElement)));
        Assert.Equal(0, FirstTouchBaseDecoratorHandler.CtorCalls);
    }

    public abstract record RepeatBaseDecoratorElement(string Tag) : Element;
    public record RepeatDerivedDecoratorElement(string Tag) : RepeatBaseDecoratorElement(Tag);
    public sealed class RepeatBaseDecoratorHandler : IDecoratorElementHandler<RepeatBaseDecoratorElement>
    {
        public UIElement Mount(MountContext ctx, RepeatBaseDecoratorElement element) => null!;
        public UIElement Update(UpdateContext ctx, RepeatBaseDecoratorElement oldEl, RepeatBaseDecoratorElement newEl, UIElement control) => control;
        public V1UnmountDisposition Unmount(UnmountContext ctx, RepeatBaseDecoratorElement? element, UIElement control)
            => V1UnmountDisposition.CollectSelf;
    }

    [Fact]
    public void RegBaseDecorator_Repeated_Reads_Of_Done_Do_Not_Re_Register()
    {
        Assert.False(ControlRegistry.ContainsBase(typeof(RepeatBaseDecoratorElement)));

        for (var i = 0; i < 50; i++)
        {
            _ = RegBaseDecorator<RepeatBaseDecoratorElement, RepeatBaseDecoratorHandler>.Done;
        }

        Assert.True(ControlRegistry.ContainsBase(typeof(RepeatBaseDecoratorElement)));
        Assert.True(ControlRegistry.ContainsForType(typeof(RepeatDerivedDecoratorElement)));
    }

    public abstract record DispatchBaseDecoratorElement(string Tag) : Element;
    public record DispatchDerivedDecoratorElementA(string Tag) : DispatchBaseDecoratorElement(Tag);
    public record DispatchDerivedDecoratorElementB(string Tag) : DispatchBaseDecoratorElement(Tag);
    public sealed class DispatchBaseDecoratorHandler : IDecoratorElementHandler<DispatchBaseDecoratorElement>
    {
        public UIElement Mount(MountContext ctx, DispatchBaseDecoratorElement element) => null!;
        public UIElement Update(UpdateContext ctx, DispatchBaseDecoratorElement oldEl, DispatchBaseDecoratorElement newEl, UIElement control) => control;
        public V1UnmountDisposition Unmount(UnmountContext ctx, DispatchBaseDecoratorElement? element, UIElement control)
            => V1UnmountDisposition.CollectSelf;
    }

    [Fact]
    public void RegBaseDecorator_Resolves_Multiple_Derived_Types_To_Same_Base_Entry()
    {
        _ = RegBaseDecorator<DispatchBaseDecoratorElement, DispatchBaseDecoratorHandler>.Done;

        var rec = new Reconciler();
        Assert.True(rec.TryResolveFromControlRegistry(typeof(DispatchDerivedDecoratorElementA), out _));
        Assert.True(rec.TryResolveFromControlRegistry(typeof(DispatchDerivedDecoratorElementB), out _));
        Assert.True(ControlRegistry.TryResolve(typeof(DispatchDerivedDecoratorElementA), out var factory1));
        Assert.True(ControlRegistry.TryResolve(typeof(DispatchDerivedDecoratorElementB), out var factory2));
        Assert.Same(factory1, factory2);
    }
}
