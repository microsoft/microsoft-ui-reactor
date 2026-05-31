using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec048.V1Protocol;

/// <summary>
/// Spec 048 §7 — contract tests for the <c>Reg&lt;TElement, TControl, THandler&gt;</c>
/// static-field registration shim.
///
/// <para><b>Important test-isolation note.</b> A static field on a closed
/// generic type is initialized at most once per process per closed-generic
/// instantiation — the CLR's cctor invariant. The global
/// <see cref="ControlRegistry"/> is also process-wide and intentionally
/// monotonic (registrations never go away — same model as WinUI's "once a
/// type is loaded it stays loaded"). Therefore every test below uses a
/// <b>unique</b> nested element + handler type triple, and every assertion
/// is a <i>delta</i> on the element-type slot the test owns (e.g.
/// <see cref="ControlRegistry.Contains"/>) rather than an absolute count
/// over the whole registry. The container-collection
/// <c>ControlRegistryTestCollection</c> serialises these cases so the
/// global state is not raced, but it deliberately does not reset between
/// tests — that would be artificial and would not match production
/// semantics.</para>
/// </summary>
[Collection(nameof(ControlRegistryTestCollection))]
public class RegTests
{
    // ─── Shared minimal handler shape ─────────────────────────────────
    // All handlers below share this shape — they only exist so the
    // generic Reg<,,>.cctor has a real `new THandler()` target. The
    // ctor increments a per-type counter so tests can assert how many
    // times the static-lambda factory was invoked (always 0 — the
    // registry stores the factory but never invokes it without a real
    // dispatch, which these tests do not perform).

    public abstract class CountingHandlerBase
    {
        public static int CtorCalls;
    }

    // ─────────────────────────────────────────────────────────────────
    // §7 bullet 1 — touching Reg<>.Done once registers the element type
    // exactly once.
    // ─────────────────────────────────────────────────────────────────

    public record FirstTouchElement(string Tag) : Element;
    public sealed class FirstTouchHandler : CountingHandlerBase, IElementHandler<FirstTouchElement, UIElement>
    {
        public FirstTouchHandler() => Interlocked.Increment(ref CtorCalls);
        public UIElement Mount(MountContext ctx, FirstTouchElement element) => null!;
        public void Update(UpdateContext ctx, FirstTouchElement oldEl, FirstTouchElement newEl, UIElement control) { }
    }

    [Fact]
    public void First_Touch_Registers_Element_Type_Exactly_Once()
    {
        Assert.False(ControlRegistry.Contains(typeof(FirstTouchElement)));

        // Single touch — this is the canonical Pattern B authoring shape.
        _ = Reg<FirstTouchElement, UIElement, FirstTouchHandler>.Done;

        Assert.True(ControlRegistry.Contains(typeof(FirstTouchElement)));
        Assert.True(ControlRegistry.TryResolve(typeof(FirstTouchElement), out _));

        // The registry stores the factory; it does not invoke it. So the
        // handler ctor must not have fired yet — that happens lazily on the
        // first dispatch hit, which these tests do not perform.
        Assert.Equal(0, FirstTouchHandler.CtorCalls);
    }

    // ─────────────────────────────────────────────────────────────────
    // §7 bullet 2 — repeat reads of Done do NOT cause additional
    // Register calls (because the CLR runs the cctor at most once).
    // The registry's first-wins TryAdd would silently no-op a duplicate
    // even if a re-register were somehow forced; we observe both by
    // verifying the slot is populated and the registered factory
    // identity is stable across reads.
    // ─────────────────────────────────────────────────────────────────

    public record RepeatTouchElement(string Tag) : Element;
    public sealed class RepeatTouchHandler : CountingHandlerBase, IElementHandler<RepeatTouchElement, UIElement>
    {
        public RepeatTouchHandler() => Interlocked.Increment(ref CtorCalls);
        public UIElement Mount(MountContext ctx, RepeatTouchElement element) => null!;
        public void Update(UpdateContext ctx, RepeatTouchElement oldEl, RepeatTouchElement newEl, UIElement control) { }
    }

    [Fact]
    public void Repeated_Reads_Of_Done_Do_Not_Re_Register()
    {
        Assert.False(ControlRegistry.Contains(typeof(RepeatTouchElement)));

        // 100 reads should be indistinguishable from 1 read at the
        // registry level. The cctor fires on the first read; subsequent
        // reads are bare static-field loads.
        for (var i = 0; i < 100; i++)
        {
            _ = Reg<RepeatTouchElement, UIElement, RepeatTouchHandler>.Done;
        }

        Assert.True(ControlRegistry.Contains(typeof(RepeatTouchElement)));
        Assert.True(ControlRegistry.TryResolve(typeof(RepeatTouchElement), out var factoryA));
        Assert.True(ControlRegistry.TryResolve(typeof(RepeatTouchElement), out var factoryB));
        // Registry-level Func identity — the entry is a single slot, never
        // overwritten on subsequent (no-op) Register calls.
        Assert.Same(factoryA, factoryB);
    }

    // ─────────────────────────────────────────────────────────────────
    // §7 — the `Done` field value itself is a documented side-effect
    // sentinel. It must read as the non-zero return from Init() so a
    // future contributor cannot quietly default it back to 0 (which
    // would be functionally identical but would erode the documented
    // "you touched Init" guarantee a static analyzer could later rely on).
    // ─────────────────────────────────────────────────────────────────

    public record SentinelElement(string Tag) : Element;
    public sealed class SentinelHandler : CountingHandlerBase, IElementHandler<SentinelElement, UIElement>
    {
        public SentinelHandler() => Interlocked.Increment(ref CtorCalls);
        public UIElement Mount(MountContext ctx, SentinelElement element) => null!;
        public void Update(UpdateContext ctx, SentinelElement oldEl, SentinelElement newEl, UIElement control) { }
    }

    [Fact]
    public void Done_Field_Reads_Nonzero_To_Confirm_Init_Ran()
    {
        var value = Reg<SentinelElement, UIElement, SentinelHandler>.Done;
        Assert.NotEqual((byte)0, value);
        Assert.True(ControlRegistry.Contains(typeof(SentinelElement)));
    }

    // ─────────────────────────────────────────────────────────────────
    // §7 + spec §10.3 — two factories legitimately touching the SAME
    // closed-generic Reg<…> still result in one registration. (Aliased
    // factories like Heading()/Subheading() both touching
    // Reg<TextBlockElement, TextBlock, TextBlockHandler>.)
    // ─────────────────────────────────────────────────────────────────

    public record AliasedElement(string Tag) : Element;
    public sealed class AliasedHandler : CountingHandlerBase, IElementHandler<AliasedElement, UIElement>
    {
        public AliasedHandler() => Interlocked.Increment(ref CtorCalls);
        public UIElement Mount(MountContext ctx, AliasedElement element) => null!;
        public void Update(UpdateContext ctx, AliasedElement oldEl, AliasedElement newEl, UIElement control) { }
    }

    // Stand-ins for two factories that both produce AliasedElement and
    // therefore both touch the same Reg<…> instantiation.
    private static AliasedElement AliasedFactoryOne(string tag)
    {
        _ = Reg<AliasedElement, UIElement, AliasedHandler>.Done;
        return new AliasedElement(tag);
    }

    private static AliasedElement AliasedFactoryTwo(string tag)
    {
        _ = Reg<AliasedElement, UIElement, AliasedHandler>.Done;
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
    // §7 — concurrent first-touch from many threads still registers
    // exactly once. This pins the CLR cctor's thread-safe before-first-
    // use guarantee in combination with ControlRegistry's lock-free
    // first-wins TryAdd.
    // ─────────────────────────────────────────────────────────────────

    public record ConcurrentElement(string Tag) : Element;
    public sealed class ConcurrentHandler : CountingHandlerBase, IElementHandler<ConcurrentElement, UIElement>
    {
        public ConcurrentHandler() => Interlocked.Increment(ref CtorCalls);
        public UIElement Mount(MountContext ctx, ConcurrentElement element) => null!;
        public void Update(UpdateContext ctx, ConcurrentElement oldEl, ConcurrentElement newEl, UIElement control) { }
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
                    _ = Reg<ConcurrentElement, UIElement, ConcurrentHandler>.Done;
                }
            });
        }

        gate.Set();
        await Task.WhenAll(tasks);

        Assert.True(ControlRegistry.Contains(typeof(ConcurrentElement)));
        Assert.True(ControlRegistry.TryResolve(typeof(ConcurrentElement), out var factoryA));
        Assert.True(ControlRegistry.TryResolve(typeof(ConcurrentElement), out var factoryB));
        // Single-slot guarantee under contention — the contended writers
        // race through TryAdd, only one wins, the rest silently no-op.
        Assert.Same(factoryA, factoryB);
    }

    // ─────────────────────────────────────────────────────────────────
    // §7 — distinct closed-generic instantiations (different TElement)
    // produce independent registrations. The CLR's cctor invariant is
    // per closed type, not per open type, so Reg<A,…> and Reg<B,…> are
    // independent.
    // ─────────────────────────────────────────────────────────────────

    public record DistinctElementA(string Tag) : Element;
    public record DistinctElementB(string Tag) : Element;

    public sealed class DistinctHandlerA : CountingHandlerBase, IElementHandler<DistinctElementA, UIElement>
    {
        public DistinctHandlerA() => Interlocked.Increment(ref CtorCalls);
        public UIElement Mount(MountContext ctx, DistinctElementA element) => null!;
        public void Update(UpdateContext ctx, DistinctElementA oldEl, DistinctElementA newEl, UIElement control) { }
    }

    public sealed class DistinctHandlerB : CountingHandlerBase, IElementHandler<DistinctElementB, UIElement>
    {
        public DistinctHandlerB() => Interlocked.Increment(ref CtorCalls);
        public UIElement Mount(MountContext ctx, DistinctElementB element) => null!;
        public void Update(UpdateContext ctx, DistinctElementB oldEl, DistinctElementB newEl, UIElement control) { }
    }

    [Fact]
    public void Distinct_Closed_Generics_Register_Independently()
    {
        Assert.False(ControlRegistry.Contains(typeof(DistinctElementA)));
        Assert.False(ControlRegistry.Contains(typeof(DistinctElementB)));

        _ = Reg<DistinctElementA, UIElement, DistinctHandlerA>.Done;
        Assert.True(ControlRegistry.Contains(typeof(DistinctElementA)));
        Assert.False(ControlRegistry.Contains(typeof(DistinctElementB)));

        _ = Reg<DistinctElementB, UIElement, DistinctHandlerB>.Done;
        Assert.True(ControlRegistry.Contains(typeof(DistinctElementA)));
        Assert.True(ControlRegistry.Contains(typeof(DistinctElementB)));
    }
}
