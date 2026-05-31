using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec048.V1Protocol;

/// <summary>
/// Spec 048 §3.4 — contract tests for the decorator sibling shim
/// <c>RegDecorator&lt;TElement, THandler&gt;</c>. Mirrors
/// <see cref="RegTests"/>; same one-shot cctor invariant, same
/// per-test unique closed-generic instantiation discipline (every test
/// uses a fresh nested element + handler pair so the cctor under test
/// fires fresh), same delta-style registry assertions (no absolute
/// counts — the global registry is intentionally monotonic).
///
/// <para>Joins the same <c>ControlRegistryTestCollection</c> as
/// <see cref="ControlRegistryTests"/> and <see cref="RegTests"/> so the
/// per-process global registry is not raced by parallel test
/// execution.</para>
/// </summary>
[Collection(nameof(ControlRegistryTestCollection))]
public class RegDecoratorTests
{
    // ─────────────────────────────────────────────────────────────────
    // §3.4 — first touch registers the decorator handler exactly once
    // for the closed-generic instantiation.
    // ─────────────────────────────────────────────────────────────────

    public record FirstTouchElement(string Tag) : Element;
    public sealed class FirstTouchHandler : IDecoratorElementHandler<FirstTouchElement>
    {
        public static int CtorCalls;
        public FirstTouchHandler() => Interlocked.Increment(ref CtorCalls);
        public UIElement Mount(MountContext ctx, FirstTouchElement element) => null!;
        public UIElement Update(UpdateContext ctx, FirstTouchElement oldEl, FirstTouchElement newEl, UIElement control) => control;
        public V1UnmountDisposition Unmount(UnmountContext ctx, FirstTouchElement? element, UIElement control)
            => V1UnmountDisposition.CollectSelf;
    }

    [Fact]
    public void First_Touch_Registers_Decorator_Element_Type_Exactly_Once()
    {
        Assert.False(ControlRegistry.Contains(typeof(FirstTouchElement)));

        _ = RegDecorator<FirstTouchElement, FirstTouchHandler>.Done;

        Assert.True(ControlRegistry.Contains(typeof(FirstTouchElement)));
        Assert.True(ControlRegistry.TryResolve(typeof(FirstTouchElement), out _));

        // Registry stores the factory; does not invoke it. Handler ctor
        // must not have run yet (lazy until first dispatch hit).
        Assert.Equal(0, FirstTouchHandler.CtorCalls);
    }

    // ─────────────────────────────────────────────────────────────────
    // §3.4 — repeated Done reads do NOT cause additional Register calls
    // (CLR runs the cctor at most once per closed generic per process).
    // ─────────────────────────────────────────────────────────────────

    public record RepeatTouchElement(string Tag) : Element;
    public sealed class RepeatTouchHandler : IDecoratorElementHandler<RepeatTouchElement>
    {
        public static int CtorCalls;
        public RepeatTouchHandler() => Interlocked.Increment(ref CtorCalls);
        public UIElement Mount(MountContext ctx, RepeatTouchElement element) => null!;
        public UIElement Update(UpdateContext ctx, RepeatTouchElement oldEl, RepeatTouchElement newEl, UIElement control) => control;
        public V1UnmountDisposition Unmount(UnmountContext ctx, RepeatTouchElement? element, UIElement control)
            => V1UnmountDisposition.CollectSelf;
    }

    [Fact]
    public void Repeated_Reads_Of_Done_Do_Not_Re_Register()
    {
        Assert.False(ControlRegistry.Contains(typeof(RepeatTouchElement)));

        for (var i = 0; i < 100; i++)
        {
            _ = RegDecorator<RepeatTouchElement, RepeatTouchHandler>.Done;
        }

        Assert.True(ControlRegistry.Contains(typeof(RepeatTouchElement)));
        Assert.True(ControlRegistry.TryResolve(typeof(RepeatTouchElement), out var factoryA));
        Assert.True(ControlRegistry.TryResolve(typeof(RepeatTouchElement), out var factoryB));
        Assert.Same(factoryA, factoryB);
    }

    // ─────────────────────────────────────────────────────────────────
    // §3.4 — Done field reads non-zero (Init sentinel; a future
    // contributor cannot quietly default it to 0).
    // ─────────────────────────────────────────────────────────────────

    public record SentinelElement(string Tag) : Element;
    public sealed class SentinelHandler : IDecoratorElementHandler<SentinelElement>
    {
        public UIElement Mount(MountContext ctx, SentinelElement element) => null!;
        public UIElement Update(UpdateContext ctx, SentinelElement oldEl, SentinelElement newEl, UIElement control) => control;
        public V1UnmountDisposition Unmount(UnmountContext ctx, SentinelElement? element, UIElement control)
            => V1UnmountDisposition.CollectSelf;
    }

    [Fact]
    public void Done_Field_Reads_Nonzero_To_Confirm_Init_Ran()
    {
        var value = RegDecorator<SentinelElement, SentinelHandler>.Done;
        Assert.NotEqual((byte)0, value);
        Assert.True(ControlRegistry.Contains(typeof(SentinelElement)));
    }

    // ─────────────────────────────────────────────────────────────────
    // §3.4 — two factories sharing the SAME closed-generic still
    // register exactly once. Mirrors Reg<>'s aliasing invariant
    // (Heading/Subheading sharing TextBlockElement).
    // ─────────────────────────────────────────────────────────────────

    public record AliasedElement(string Tag) : Element;
    public sealed class AliasedHandler : IDecoratorElementHandler<AliasedElement>
    {
        public UIElement Mount(MountContext ctx, AliasedElement element) => null!;
        public UIElement Update(UpdateContext ctx, AliasedElement oldEl, AliasedElement newEl, UIElement control) => control;
        public V1UnmountDisposition Unmount(UnmountContext ctx, AliasedElement? element, UIElement control)
            => V1UnmountDisposition.CollectSelf;
    }

    private static AliasedElement AliasedFactoryOne(string tag)
    {
        _ = RegDecorator<AliasedElement, AliasedHandler>.Done;
        return new AliasedElement(tag);
    }

    private static AliasedElement AliasedFactoryTwo(string tag)
    {
        _ = RegDecorator<AliasedElement, AliasedHandler>.Done;
        return new AliasedElement(tag);
    }

    [Fact]
    public void Two_Factories_Sharing_A_Closed_Generic_Register_Exactly_Once()
    {
        _ = AliasedFactoryOne("a");
        _ = AliasedFactoryTwo("b");
        _ = AliasedFactoryOne("c");

        Assert.True(ControlRegistry.Contains(typeof(AliasedElement)));
        Assert.True(ControlRegistry.TryResolve(typeof(AliasedElement), out var factoryA));
        Assert.True(ControlRegistry.TryResolve(typeof(AliasedElement), out var factoryB));
        Assert.Same(factoryA, factoryB);
    }

    // ─────────────────────────────────────────────────────────────────
    // §3.4 — concurrent first-touch from many threads still registers
    // exactly once. CLR cctor thread-safe before-first-use + lock-free
    // first-wins TryAdd.
    // ─────────────────────────────────────────────────────────────────

    public record ConcurrentElement(string Tag) : Element;
    public sealed class ConcurrentHandler : IDecoratorElementHandler<ConcurrentElement>
    {
        public UIElement Mount(MountContext ctx, ConcurrentElement element) => null!;
        public UIElement Update(UpdateContext ctx, ConcurrentElement oldEl, ConcurrentElement newEl, UIElement control) => control;
        public V1UnmountDisposition Unmount(UnmountContext ctx, ConcurrentElement? element, UIElement control)
            => V1UnmountDisposition.CollectSelf;
    }

    [Fact]
    public async Task Concurrent_First_Touches_Register_Exactly_Once()
    {
        Assert.False(ControlRegistry.Contains(typeof(ConcurrentElement)));

        const int threadCount = 32;
        const int touchesPerThread = 50;

        using var gate = new ManualResetEventSlim(false);
        var tasks = new Task[threadCount];
        for (var t = 0; t < threadCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                gate.Wait();
                for (var i = 0; i < touchesPerThread; i++)
                {
                    _ = RegDecorator<ConcurrentElement, ConcurrentHandler>.Done;
                }
            });
        }

        gate.Set();
        await Task.WhenAll(tasks);

        Assert.True(ControlRegistry.Contains(typeof(ConcurrentElement)));
        Assert.True(ControlRegistry.TryResolve(typeof(ConcurrentElement), out var factoryA));
        Assert.True(ControlRegistry.TryResolve(typeof(ConcurrentElement), out var factoryB));
        Assert.Same(factoryA, factoryB);
    }

    // ─────────────────────────────────────────────────────────────────
    // §3.4 — distinct closed generics (different TElement) produce
    // independent registrations.
    // ─────────────────────────────────────────────────────────────────

    public record DistinctElementA(string Tag) : Element;
    public record DistinctElementB(string Tag) : Element;

    public sealed class DistinctHandlerA : IDecoratorElementHandler<DistinctElementA>
    {
        public UIElement Mount(MountContext ctx, DistinctElementA element) => null!;
        public UIElement Update(UpdateContext ctx, DistinctElementA oldEl, DistinctElementA newEl, UIElement control) => control;
        public V1UnmountDisposition Unmount(UnmountContext ctx, DistinctElementA? element, UIElement control)
            => V1UnmountDisposition.CollectSelf;
    }

    public sealed class DistinctHandlerB : IDecoratorElementHandler<DistinctElementB>
    {
        public UIElement Mount(MountContext ctx, DistinctElementB element) => null!;
        public UIElement Update(UpdateContext ctx, DistinctElementB oldEl, DistinctElementB newEl, UIElement control) => control;
        public V1UnmountDisposition Unmount(UnmountContext ctx, DistinctElementB? element, UIElement control)
            => V1UnmountDisposition.CollectSelf;
    }

    [Fact]
    public void Distinct_Closed_Generics_Register_Independently()
    {
        Assert.False(ControlRegistry.Contains(typeof(DistinctElementA)));
        Assert.False(ControlRegistry.Contains(typeof(DistinctElementB)));

        _ = RegDecorator<DistinctElementA, DistinctHandlerA>.Done;
        Assert.True(ControlRegistry.Contains(typeof(DistinctElementA)));
        Assert.False(ControlRegistry.Contains(typeof(DistinctElementB)));

        _ = RegDecorator<DistinctElementB, DistinctHandlerB>.Done;
        Assert.True(ControlRegistry.Contains(typeof(DistinctElementA)));
        Assert.True(ControlRegistry.Contains(typeof(DistinctElementB)));
    }

    // ─────────────────────────────────────────────────────────────────
    // §3.4 dispatch wiring — the entry resolved through arm 3
    // (TryResolveFromControlRegistry) is a V1DecoratorHandlerAdapter,
    // and the adapter routes Mount/Update/Unmount through the
    // registered IDecoratorElementHandler<TElement>. This is the
    // end-to-end smoke test the rubber-duck critique flagged as
    // necessary: registration alone is not proof of dispatch.
    // ─────────────────────────────────────────────────────────────────

    public record DispatchElement(string Tag) : Element;
    public sealed class DispatchHandler : IDecoratorElementHandler<DispatchElement>
    {
        public static int MountCalls;
        public static int UpdateCalls;
        public static int UnmountCalls;

        public UIElement Mount(MountContext ctx, DispatchElement element)
        {
            Interlocked.Increment(ref MountCalls);
            return null!;
        }

        public UIElement Update(UpdateContext ctx, DispatchElement oldEl, DispatchElement newEl, UIElement control)
        {
            Interlocked.Increment(ref UpdateCalls);
            return control;
        }

        public V1UnmountDisposition Unmount(UnmountContext ctx, DispatchElement? element, UIElement control)
        {
            Interlocked.Increment(ref UnmountCalls);
            return V1UnmountDisposition.CollectSelf;
        }
    }

    [Fact]
    public void Resolved_Entry_Dispatches_Through_Decorator_Handler()
    {
        // Per-test counter snapshot — DispatchHandler is unique to this
        // [Fact] so the static fields are only touched here. The
        // ControlRegistryTestCollection serializes tests so no other
        // case can race against these counters.
        var mountBaseline = DispatchHandler.MountCalls;
        var updateBaseline = DispatchHandler.UpdateCalls;
        var unmountBaseline = DispatchHandler.UnmountCalls;

        _ = RegDecorator<DispatchElement, DispatchHandler>.Done;

        var rec = new Reconciler();
        Assert.True(rec.TryResolveFromControlRegistry(typeof(DispatchElement), out var entry));

        // The adapter is a V1DecoratorHandlerAdapter<DispatchElement>; we
        // assert via behavior rather than internal type identity (the
        // adapter type is internal and not part of the contract).
        var el = new DispatchElement("probe");
        _ = entry.Mount(el, requestRerender: static () => { }, reconciler: rec);
        Assert.Equal(mountBaseline + 1, DispatchHandler.MountCalls);

        var el2 = new DispatchElement("probe2");
        _ = entry.Update(el, el2, control: null!, requestRerender: static () => { }, reconciler: rec);
        Assert.Equal(updateBaseline + 1, DispatchHandler.UpdateCalls);

        _ = entry.Unmount(control: null!, reconciler: rec);
        Assert.Equal(unmountBaseline + 1, DispatchHandler.UnmountCalls);
    }
}
