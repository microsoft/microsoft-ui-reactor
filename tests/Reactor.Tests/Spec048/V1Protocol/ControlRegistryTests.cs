using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec048.V1Protocol;

/// <summary>
/// Spec 048 §8 + §12.1 — global <see cref="ControlRegistry"/> contract
/// and dispatch-precedence wiring. Covers idempotent first-wins
/// registration, lock-free <see cref="ControlRegistry.Register{TElement,TControl}"/>
/// semantics under concurrent contention, per-host caching of the
/// registry factory result into <c>_v1Handlers</c>, and the §12.2
/// shadowing rule (explicit per-host <see cref="Reconciler.RegisterHandler{TElement,TControl}"/>
/// wins over a globally-registered handler for the same element type).
///
/// <para>The Mount/Update round-trip through a real WinUI control is
/// covered by selftests (which run on the STA dispatcher); these tests
/// exercise the registry primitive and the internal
/// <see cref="Reconciler.TryResolveFromControlRegistry"/> resolution path
/// directly, the same way <c>RegisterTypeV1Tests</c> does for the legacy
/// per-host registry.</para>
///
/// <para><b>Test-isolation discipline.</b> The global registry is
/// intentionally <i>monotonic</i> (process-wide; registrations never go
/// away — same model as WinUI's "once a type is loaded it stays loaded").
/// So every test below declares its own unique nested probe element +
/// handler types, and every assertion is a <i>delta</i> on the slot the
/// test owns (e.g. <see cref="ControlRegistry.Contains"/>,
/// <see cref="ControlRegistry.ContainsBase"/>) rather than an absolute
/// count over the whole registry. The container-collection
/// <c>ControlRegistryTestCollection</c> serialises these cases so the
/// global state is not raced.</para>
/// </summary>
[Collection(nameof(ControlRegistryTestCollection))]
public class ControlRegistryTests
{
    // ─────────────────────────────────────────────────────────────────
    // 1.3 — bullet 1: idempotent Register (second call is a silent no-op).
    // ─────────────────────────────────────────────────────────────────

    public record IdempotentValueElement(string Tag) : Element;
    public sealed class IdempotentValueHandler : IElementHandler<IdempotentValueElement, UIElement>
    {
        public string? Identity { get; set; }
        public UIElement Mount(MountContext ctx, IdempotentValueElement element) => null!;
        public void Update(UpdateContext ctx, IdempotentValueElement oldEl, IdempotentValueElement newEl, UIElement control) { }
    }

    [Fact]
    public void Register_Same_Element_Type_Twice_Is_Silent_NoOp()
    {
        Assert.False(ControlRegistry.Contains(typeof(IdempotentValueElement)));

        var firstFactoryCalls = 0;
        var secondFactoryCalls = 0;

        ControlRegistry.Register<IdempotentValueElement, UIElement>(() =>
        {
            Interlocked.Increment(ref firstFactoryCalls);
            return new IdempotentValueHandler { Identity = "first" };
        });

        // Second registration must NOT throw and must NOT replace the first.
        ControlRegistry.Register<IdempotentValueElement, UIElement>(() =>
        {
            Interlocked.Increment(ref secondFactoryCalls);
            return new IdempotentValueHandler { Identity = "second" };
        });

        Assert.True(ControlRegistry.Contains(typeof(IdempotentValueElement)));

        // The first registration's factory wins on resolution.
        var rec = new Reconciler();
        Assert.True(rec.TryResolveFromControlRegistry(typeof(IdempotentValueElement), out _));

        Assert.Equal(1, firstFactoryCalls);
        Assert.Equal(0, secondFactoryCalls);
    }

    public record NullValueProbeElement(string Tag) : Element;

    [Fact]
    public void Register_Throws_On_Null_Factory()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ControlRegistry.Register<NullValueProbeElement, UIElement>(null!));
    }

    // ─────────────────────────────────────────────────────────────────
    // 1.3 — bullet 1: lock-free TryAdd semantics; factory invoked exactly
    // once across N sequential dispatch hits on the same host (the per-host
    // _v1Handlers cache short-circuits after the first hit).
    //
    // The registry's factory is invoked at most once per (host, element
    // type): every host that hits the registry pays one factory call, but
    // a single host paying N dispatches pays exactly one factory call
    // (subsequent hits short-circuit in the host's _v1Handlers cache).
    // ─────────────────────────────────────────────────────────────────

    public record SeqDispatchElement(string Tag) : Element;
    public sealed class SeqDispatchHandler : IElementHandler<SeqDispatchElement, UIElement>
    {
        public UIElement Mount(MountContext ctx, SeqDispatchElement element) => null!;
        public void Update(UpdateContext ctx, SeqDispatchElement oldEl, SeqDispatchElement newEl, UIElement control) { }
    }

    [Fact]
    public void Registry_Factory_Invoked_Exactly_Once_Across_Many_Sequential_Dispatches_On_Same_Host()
    {
        var factoryCalls = 0;
        ControlRegistry.Register<SeqDispatchElement, UIElement>(() =>
        {
            Interlocked.Increment(ref factoryCalls);
            return new SeqDispatchHandler();
        });

        var rec = new Reconciler();

        // Mimic the Mount dispatch path: try per-host first, fall through
        // to the registry arm on miss. This loop simulates many sequential
        // mounts of the same element type on the same host.
        for (var i = 0; i < 256; i++)
        {
            if (rec._v1Handlers.TryGet(typeof(SeqDispatchElement), out _))
                continue;
            rec.TryResolveFromControlRegistry(typeof(SeqDispatchElement), out _);
        }

        Assert.Equal(1, factoryCalls);
    }

    public record ConcurrentRegisterElementA(string Tag) : Element;
    public record ConcurrentRegisterElementB(string Tag) : Element;
    public sealed class ConcurrentRegisterHandlerA : IElementHandler<ConcurrentRegisterElementA, UIElement>
    {
        public UIElement Mount(MountContext ctx, ConcurrentRegisterElementA element) => null!;
        public void Update(UpdateContext ctx, ConcurrentRegisterElementA oldEl, ConcurrentRegisterElementA newEl, UIElement control) { }
    }
    public sealed class ConcurrentRegisterHandlerB : IElementHandler<ConcurrentRegisterElementB, UIElement>
    {
        public UIElement Mount(MountContext ctx, ConcurrentRegisterElementB element) => null!;
        public void Update(UpdateContext ctx, ConcurrentRegisterElementB oldEl, ConcurrentRegisterElementB newEl, UIElement control) { }
    }

    [Fact]
    public async Task Register_Concurrent_Distinct_Types_Idempotent_No_Throws()
    {
        // Hammer Register from many threads with two distinct element
        // types; ConcurrentDictionary.TryAdd must accept the first
        // registration for each type and silently no-op the rest, without
        // throwing or deadlocking.
        Assert.False(ControlRegistry.Contains(typeof(ConcurrentRegisterElementA)));
        Assert.False(ControlRegistry.Contains(typeof(ConcurrentRegisterElementB)));

        const int threadCount = 32;
        using var barrier = new Barrier(threadCount);
        var tasks = new Task[threadCount];
        for (var t = 0; t < threadCount; t++)
        {
            var idx = t;
            tasks[t] = Task.Run(() =>
            {
                barrier.SignalAndWait();
                if (idx % 2 == 0)
                    ControlRegistry.Register<ConcurrentRegisterElementA, UIElement>(static () => new ConcurrentRegisterHandlerA());
                else
                    ControlRegistry.Register<ConcurrentRegisterElementB, UIElement>(static () => new ConcurrentRegisterHandlerB());
            });
        }
        await Task.WhenAll(tasks);

        Assert.True(ControlRegistry.Contains(typeof(ConcurrentRegisterElementA)));
        Assert.True(ControlRegistry.Contains(typeof(ConcurrentRegisterElementB)));
    }

    // ─────────────────────────────────────────────────────────────────
    // 1.3 — bullet 2 / §12.2 — dispatch precedence: per-host
    // RegisterHandler shadows a globally-registered handler for the same
    // element type when wired up before the first dispatch on that host.
    // ─────────────────────────────────────────────────────────────────

    public record ShadowElement(string Tag) : Element;
    public sealed class ShadowHandler : IElementHandler<ShadowElement, UIElement>
    {
        public string? Identity { get; set; }
        public UIElement Mount(MountContext ctx, ShadowElement element) => null!;
        public void Update(UpdateContext ctx, ShadowElement oldEl, ShadowElement newEl, UIElement control) { }
    }

    [Fact]
    public void PerHost_RegisterHandler_Shadows_Global_ControlRegistry()
    {
        var globalFactoryCalls = 0;
        ControlRegistry.Register<ShadowElement, UIElement>(() =>
        {
            Interlocked.Increment(ref globalFactoryCalls);
            return new ShadowHandler { Identity = "global" };
        });

        var rec = new Reconciler();
        var perHostHandler = new ShadowHandler { Identity = "per-host" };
        rec.RegisterHandler<ShadowElement, UIElement>(perHostHandler);

        // Arm 1 (per-host) hits first; the global registry factory never
        // runs on this host because the Mount/Update dispatch never falls
        // through to arm 3.
        Assert.True(rec._v1Handlers.TryGet(typeof(ShadowElement), out _));
        Assert.Equal(0, globalFactoryCalls);
    }

    public record ShadowAfterCacheElement(string Tag) : Element;
    public sealed class ShadowAfterCacheHandler : IElementHandler<ShadowAfterCacheElement, UIElement>
    {
        public UIElement Mount(MountContext ctx, ShadowAfterCacheElement element) => null!;
        public void Update(UpdateContext ctx, ShadowAfterCacheElement oldEl, ShadowAfterCacheElement newEl, UIElement control) { }
    }

    [Fact]
    public void PerHost_RegisterHandler_After_Global_Cache_Population_Throws()
    {
        // The precedence rule guarantees shadowing only when the per-host
        // registration precedes the first dispatch on that host. If the
        // global registry's factory has already been cached into
        // _v1Handlers (via TryResolveFromControlRegistry), a later
        // RegisterHandler is a duplicate against the cached entry — spec
        // 047 §13 Q17 keeps the strict throw on the explicit per-host path.
        ControlRegistry.Register<ShadowAfterCacheElement, UIElement>(static () => new ShadowAfterCacheHandler());

        var rec = new Reconciler();
        Assert.True(rec.TryResolveFromControlRegistry(typeof(ShadowAfterCacheElement), out _));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            rec.RegisterHandler<ShadowAfterCacheElement, UIElement>(new ShadowAfterCacheHandler()));
        Assert.Contains(nameof(ShadowAfterCacheElement), ex.Message);
    }

    // ─────────────────────────────────────────────────────────────────
    // 1.3 — bullet 3: cache test — after the first global-table hit on a
    // given host, the registry's factory delegate is not invoked on
    // subsequent mounts of the same element type on that host.
    // ─────────────────────────────────────────────────────────────────

    public record CachedFactoryElement(string Tag) : Element;
    public sealed class CachedFactoryHandler : IElementHandler<CachedFactoryElement, UIElement>
    {
        public UIElement Mount(MountContext ctx, CachedFactoryElement element) => null!;
        public void Update(UpdateContext ctx, CachedFactoryElement oldEl, CachedFactoryElement newEl, UIElement control) { }
    }

    [Fact]
    public void Registry_Factory_Result_Cached_Into_PerHost_V1Handlers()
    {
        var factoryCalls = 0;
        ControlRegistry.Register<CachedFactoryElement, UIElement>(() =>
        {
            Interlocked.Increment(ref factoryCalls);
            return new CachedFactoryHandler();
        });

        var rec = new Reconciler();

        // First arm-3 dispatch ⇒ factory runs once, result cached into
        // _v1Handlers.
        Assert.True(rec.TryResolveFromControlRegistry(typeof(CachedFactoryElement), out var first));
        Assert.Equal(1, factoryCalls);
        Assert.NotNull(first);

        // Subsequent dispatches simulate the Mount fast path: arm 1
        // (_v1Handlers.TryGet) hits, the registry resolution helper is
        // never re-entered. Verify the same adapter instance is returned
        // on every subsequent lookup (handler identity preserved).
        for (var i = 0; i < 10; i++)
        {
            Assert.True(rec._v1Handlers.TryGet(typeof(CachedFactoryElement), out var cached));
            Assert.Same(first, cached);
        }

        Assert.Equal(1, factoryCalls);
    }

    public record IndepHostElement(string Tag) : Element;
    public sealed class IndepHostHandler : IElementHandler<IndepHostElement, UIElement>
    {
        public UIElement Mount(MountContext ctx, IndepHostElement element) => null!;
        public void Update(UpdateContext ctx, IndepHostElement oldEl, IndepHostElement newEl, UIElement control) { }
    }

    [Fact]
    public void Registry_Factory_Invoked_Per_Host_For_Independent_Reconcilers()
    {
        // The cache is per-host: two reconcilers each pay one factory
        // call on first dispatch. This is intentional — each host owns
        // its own adapter so per-host state is isolated (mirrors what the
        // legacy RegisterV1BuiltInHandlers does in the ctor of every
        // Reconciler today).
        var factoryCalls = 0;
        ControlRegistry.Register<IndepHostElement, UIElement>(() =>
        {
            Interlocked.Increment(ref factoryCalls);
            return new IndepHostHandler();
        });

        var rec1 = new Reconciler();
        var rec2 = new Reconciler();

        Assert.True(rec1.TryResolveFromControlRegistry(typeof(IndepHostElement), out var a1));
        Assert.True(rec2.TryResolveFromControlRegistry(typeof(IndepHostElement), out var a2));

        Assert.NotSame(a1, a2);
        Assert.Equal(2, factoryCalls);
    }

    // ─────────────────────────────────────────────────────────────────
    // Resolution semantics — TryResolve returns false for unregistered
    // element types, and the per-host cache is *not* polluted on a miss.
    // ─────────────────────────────────────────────────────────────────

    public record MissProbeElement(string Tag) : Element;

    [Fact]
    public void TryResolveFromControlRegistry_Returns_False_On_Miss_Without_Polluting_Cache()
    {
        // MissProbeElement is never registered (unique to this test).
        Assert.False(ControlRegistry.Contains(typeof(MissProbeElement)));

        var rec = new Reconciler();
        Assert.False(rec.TryResolveFromControlRegistry(typeof(MissProbeElement), out _));
        Assert.False(rec._v1Handlers.TryGet(typeof(MissProbeElement), out _));
    }

    public record DistinctRegisterElementA(string Tag) : Element;
    public record DistinctRegisterElementB(string Tag) : Element;
    public sealed class DistinctRegisterHandlerA : IElementHandler<DistinctRegisterElementA, UIElement>
    {
        public UIElement Mount(MountContext ctx, DistinctRegisterElementA element) => null!;
        public void Update(UpdateContext ctx, DistinctRegisterElementA oldEl, DistinctRegisterElementA newEl, UIElement control) { }
    }
    public sealed class DistinctRegisterHandlerB : IElementHandler<DistinctRegisterElementB, UIElement>
    {
        public UIElement Mount(MountContext ctx, DistinctRegisterElementB element) => null!;
        public void Update(UpdateContext ctx, DistinctRegisterElementB oldEl, DistinctRegisterElementB newEl, UIElement control) { }
    }

    [Fact]
    public void Register_Allows_Distinct_Element_Types_Independently()
    {
        Assert.False(ControlRegistry.Contains(typeof(DistinctRegisterElementA)));
        Assert.False(ControlRegistry.Contains(typeof(DistinctRegisterElementB)));

        ControlRegistry.Register<DistinctRegisterElementA, UIElement>(static () => new DistinctRegisterHandlerA());
        ControlRegistry.Register<DistinctRegisterElementB, UIElement>(static () => new DistinctRegisterHandlerB());

        Assert.True(ControlRegistry.Contains(typeof(DistinctRegisterElementA)));
        Assert.True(ControlRegistry.Contains(typeof(DistinctRegisterElementB)));
    }

    // ═════════════════════════════════════════════════════════════════
    // Spec 048 §3.4 — RegisterDecorator contract. Decorator handlers
    // implement the SEPARATE IDecoratorElementHandler<TElement>
    // interface and need a parallel registration entry point that
    // bridges to V1DecoratorHandlerAdapter<TElement>.
    // ═════════════════════════════════════════════════════════════════

    public record RegDecAddElement(string Tag) : Element;
    public sealed class RegDecAddHandler : IDecoratorElementHandler<RegDecAddElement>
    {
        public UIElement Mount(MountContext ctx, RegDecAddElement element) => null!;
        public UIElement Update(UpdateContext ctx, RegDecAddElement oldEl, RegDecAddElement newEl, UIElement control) => control;
        public V1UnmountDisposition Unmount(UnmountContext ctx, RegDecAddElement? element, UIElement control)
            => V1UnmountDisposition.CollectSelf;
    }

    [Fact]
    public void RegisterDecorator_Adds_Element_Type_To_Registry()
    {
        Assert.False(ControlRegistry.Contains(typeof(RegDecAddElement)));

        ControlRegistry.RegisterDecorator<RegDecAddElement>(static () => new RegDecAddHandler());

        Assert.True(ControlRegistry.Contains(typeof(RegDecAddElement)));
        Assert.True(ControlRegistry.TryResolve(typeof(RegDecAddElement), out _));
    }

    public record RegDecNullProbeElement(string Tag) : Element;

    [Fact]
    public void RegisterDecorator_Throws_On_Null_Factory()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ControlRegistry.RegisterDecorator<RegDecNullProbeElement>(null!));
    }

    public record RegDecTwiceElement(string Tag) : Element;
    public sealed class RegDecTwiceHandler : IDecoratorElementHandler<RegDecTwiceElement>
    {
        public string? Identity { get; set; }
        public UIElement Mount(MountContext ctx, RegDecTwiceElement element) => null!;
        public UIElement Update(UpdateContext ctx, RegDecTwiceElement oldEl, RegDecTwiceElement newEl, UIElement control) => control;
        public V1UnmountDisposition Unmount(UnmountContext ctx, RegDecTwiceElement? element, UIElement control)
            => V1UnmountDisposition.CollectSelf;
    }

    [Fact]
    public void RegisterDecorator_Twice_Is_Silent_NoOp_First_Wins()
    {
        Assert.False(ControlRegistry.Contains(typeof(RegDecTwiceElement)));

        var firstFactoryCalls = 0;
        var secondFactoryCalls = 0;

        ControlRegistry.RegisterDecorator<RegDecTwiceElement>(() =>
        {
            Interlocked.Increment(ref firstFactoryCalls);
            return new RegDecTwiceHandler { Identity = "first" };
        });

        ControlRegistry.RegisterDecorator<RegDecTwiceElement>(() =>
        {
            Interlocked.Increment(ref secondFactoryCalls);
            return new RegDecTwiceHandler { Identity = "second" };
        });

        Assert.True(ControlRegistry.Contains(typeof(RegDecTwiceElement)));

        // Resolve to invoke the winning factory and confirm it's the first.
        var rec = new Reconciler();
        Assert.True(rec.TryResolveFromControlRegistry(typeof(RegDecTwiceElement), out _));

        Assert.Equal(1, firstFactoryCalls);
        Assert.Equal(0, secondFactoryCalls);
    }

    public record RegDecCollideElement(string Tag) : Element;
    public sealed class RegDecCollideValueHandler : IElementHandler<RegDecCollideElement, UIElement>
    {
        public UIElement Mount(MountContext ctx, RegDecCollideElement element) => null!;
        public void Update(UpdateContext ctx, RegDecCollideElement oldEl, RegDecCollideElement newEl, UIElement control) { }
    }
    public sealed class RegDecCollideDecoratorHandler : IDecoratorElementHandler<RegDecCollideElement>
    {
        public UIElement Mount(MountContext ctx, RegDecCollideElement element) => null!;
        public UIElement Update(UpdateContext ctx, RegDecCollideElement oldEl, RegDecCollideElement newEl, UIElement control) => control;
        public V1UnmountDisposition Unmount(UnmountContext ctx, RegDecCollideElement? element, UIElement control)
            => V1UnmountDisposition.CollectSelf;
    }

    [Fact]
    public void RegisterDecorator_And_Register_Are_FirstWins_For_Same_Element_Type()
    {
        // Mixing Reg<> (value) and RegDecorator (decorator) for the same
        // TElement is the §3.4 authoring rule violation. The registry
        // itself doesn't reject the mix — it's first-wins TryAdd, same as
        // any other dup. This test pins that semantic so a future
        // contributor sees the silent-drop behavior is intentional and
        // not a bug to "fix" by throwing.

        var valueFactoryCalls = 0;
        var decoratorFactoryCalls = 0;

        ControlRegistry.Register<RegDecCollideElement, UIElement>(() =>
        {
            Interlocked.Increment(ref valueFactoryCalls);
            return new RegDecCollideValueHandler();
        });

        ControlRegistry.RegisterDecorator<RegDecCollideElement>(() =>
        {
            Interlocked.Increment(ref decoratorFactoryCalls);
            return new RegDecCollideDecoratorHandler();
        });

        Assert.True(ControlRegistry.Contains(typeof(RegDecCollideElement)));

        var rec = new Reconciler();
        Assert.True(rec.TryResolveFromControlRegistry(typeof(RegDecCollideElement), out _));

        // Value path registered first → its factory ran on resolve.
        // Decorator factory was silently dropped.
        Assert.Equal(1, valueFactoryCalls);
        Assert.Equal(0, decoratorFactoryCalls);
    }

    public record RegDecCachedElement(string Tag) : Element;
    public sealed class RegDecCachedHandler : IDecoratorElementHandler<RegDecCachedElement>
    {
        public UIElement Mount(MountContext ctx, RegDecCachedElement element) => null!;
        public UIElement Update(UpdateContext ctx, RegDecCachedElement oldEl, RegDecCachedElement newEl, UIElement control) => control;
        public V1UnmountDisposition Unmount(UnmountContext ctx, RegDecCachedElement? element, UIElement control)
            => V1UnmountDisposition.CollectSelf;
    }

    [Fact]
    public void RegisterDecorator_Resolved_Entry_Is_Cached_Into_Host_V1Handlers()
    {
        ControlRegistry.RegisterDecorator<RegDecCachedElement>(static () => new RegDecCachedHandler());

        var rec = new Reconciler();
        Assert.False(rec._v1Handlers.TryGet(typeof(RegDecCachedElement), out _));

        Assert.True(rec.TryResolveFromControlRegistry(typeof(RegDecCachedElement), out var entry1));

        // After a registry hit the per-host cache holds the adapter, so a
        // second TryGet on _v1Handlers short-circuits without re-walking
        // the registry. This is the documented arm 3 → arm 1 cache hop.
        Assert.True(rec._v1Handlers.TryGet(typeof(RegDecCachedElement), out var entry2));
        Assert.Same(entry1, entry2);
    }

    // ═════════════════════════════════════════════════════════════════
    // Spec 048 §3.4 — RegisterForDerivedTypes / RegisterDecoratorForDerivedTypes
    // contract. Mirrors the per-host V1HandlerRegistry._baseEntries +
    // _baseCache pattern: a single base registration catches every concrete
    // element type whose runtime type derives from the base (T-erasure
    // pattern used by TemplatedListElementBase, LazyStackElementBase,
    // ItemsRepeaterElement, ItemsViewElement). Exact-type registrations
    // still win at dispatch.
    // ═════════════════════════════════════════════════════════════════

    public abstract record ResolveDerivedBaseElement(string Tag) : Element;
    public record ResolveDerivedElementA(string Tag) : ResolveDerivedBaseElement(Tag);
    public record ResolveDerivedElementB(string Tag) : ResolveDerivedBaseElement(Tag);
    public sealed class ResolveDerivedBaseHandler : IElementHandler<ResolveDerivedBaseElement, UIElement>
    {
        public UIElement Mount(MountContext ctx, ResolveDerivedBaseElement element) => null!;
        public void Update(UpdateContext ctx, ResolveDerivedBaseElement oldEl, ResolveDerivedBaseElement newEl, UIElement control) { }
    }

    [Fact]
    public void RegisterForDerivedTypes_Resolves_Derived_Element_Type_To_Base_Handler()
    {
        Assert.False(ControlRegistry.ContainsBase(typeof(ResolveDerivedBaseElement)));

        ControlRegistry.RegisterForDerivedTypes<ResolveDerivedBaseElement, UIElement>(
            static () => new ResolveDerivedBaseHandler());

        Assert.False(ControlRegistry.Contains(typeof(ResolveDerivedElementA)));
        Assert.True(ControlRegistry.ContainsBase(typeof(ResolveDerivedBaseElement)));
        Assert.True(ControlRegistry.ContainsForType(typeof(ResolveDerivedElementA)));
        Assert.True(ControlRegistry.ContainsForType(typeof(ResolveDerivedElementB)));

        var rec = new Reconciler();
        Assert.True(rec.TryResolveFromControlRegistry(typeof(ResolveDerivedElementA), out _));
        Assert.True(rec.TryResolveFromControlRegistry(typeof(ResolveDerivedElementB), out _));
    }

    public abstract record ExactWinsBaseElement(string Tag) : Element;
    public record ExactWinsDerivedExact(string Tag) : ExactWinsBaseElement(Tag);
    public record ExactWinsDerivedOther(string Tag) : ExactWinsBaseElement(Tag);
    public sealed class ExactWinsBaseHandler : IElementHandler<ExactWinsBaseElement, UIElement>
    {
        public UIElement Mount(MountContext ctx, ExactWinsBaseElement element) => null!;
        public void Update(UpdateContext ctx, ExactWinsBaseElement oldEl, ExactWinsBaseElement newEl, UIElement control) { }
    }
    public sealed class ExactWinsExactHandler : IElementHandler<ExactWinsDerivedExact, UIElement>
    {
        public UIElement Mount(MountContext ctx, ExactWinsDerivedExact element) => null!;
        public void Update(UpdateContext ctx, ExactWinsDerivedExact oldEl, ExactWinsDerivedExact newEl, UIElement control) { }
    }

    [Fact]
    public void RegisterForDerivedTypes_Exact_Registration_Wins_Over_Base()
    {
        var baseHandlerCalls = 0;
        var exactHandlerCalls = 0;

        ControlRegistry.RegisterForDerivedTypes<ExactWinsBaseElement, UIElement>(() =>
        {
            Interlocked.Increment(ref baseHandlerCalls);
            return new ExactWinsBaseHandler();
        });
        ControlRegistry.Register<ExactWinsDerivedExact, UIElement>(() =>
        {
            Interlocked.Increment(ref exactHandlerCalls);
            return new ExactWinsExactHandler();
        });

        var rec = new Reconciler();
        // ExactWinsDerivedExact → exact hit (one factory call, exact)
        Assert.True(rec.TryResolveFromControlRegistry(typeof(ExactWinsDerivedExact), out _));
        Assert.Equal(0, baseHandlerCalls);
        Assert.Equal(1, exactHandlerCalls);

        // ExactWinsDerivedOther → base hit (the only path)
        var rec2 = new Reconciler();
        Assert.True(rec2.TryResolveFromControlRegistry(typeof(ExactWinsDerivedOther), out _));
        Assert.Equal(1, baseHandlerCalls);
        Assert.Equal(1, exactHandlerCalls);
    }

    public abstract record UnrelTypeBaseElement(string Tag) : Element;
    public record UnrelTypeUnrelatedElement(string Tag) : Element;
    public sealed class UnrelTypeBaseHandler : IElementHandler<UnrelTypeBaseElement, UIElement>
    {
        public UIElement Mount(MountContext ctx, UnrelTypeBaseElement element) => null!;
        public void Update(UpdateContext ctx, UnrelTypeBaseElement oldEl, UnrelTypeBaseElement newEl, UIElement control) { }
    }

    [Fact]
    public void RegisterForDerivedTypes_Returns_False_For_Unrelated_Type()
    {
        ControlRegistry.RegisterForDerivedTypes<UnrelTypeBaseElement, UIElement>(
            static () => new UnrelTypeBaseHandler());

        var rec = new Reconciler();
        Assert.False(rec.TryResolveFromControlRegistry(typeof(UnrelTypeUnrelatedElement), out _));
        Assert.False(ControlRegistry.ContainsForType(typeof(UnrelTypeUnrelatedElement)));
    }

    public abstract record WalkCacheBaseElement(string Tag) : Element;
    public record WalkCacheDerivedElement(string Tag) : WalkCacheBaseElement(Tag);
    public sealed class WalkCacheBaseHandler : IElementHandler<WalkCacheBaseElement, UIElement>
    {
        public UIElement Mount(MountContext ctx, WalkCacheBaseElement element) => null!;
        public void Update(UpdateContext ctx, WalkCacheBaseElement oldEl, WalkCacheBaseElement newEl, UIElement control) { }
    }

    [Fact]
    public void RegisterForDerivedTypes_Walk_Result_Is_Cached_For_Subsequent_Lookups()
    {
        var factoryCalls = 0;
        ControlRegistry.RegisterForDerivedTypes<WalkCacheBaseElement, UIElement>(() =>
        {
            Interlocked.Increment(ref factoryCalls);
            return new WalkCacheBaseHandler();
        });

        // First lookup walks the BaseType chain and caches the resolved
        // adapter factory under the derived key. The second lookup hits
        // the cache and returns the SAME Func instance (registry-level
        // identity — distinct from the per-host adapter cache, which
        // creates one fresh adapter per host).
        Assert.True(ControlRegistry.TryResolve(typeof(WalkCacheDerivedElement), out var factory1));
        Assert.True(ControlRegistry.TryResolve(typeof(WalkCacheDerivedElement), out var factory2));
        Assert.Same(factory1, factory2);

        // No factory invocation occurs during TryResolve — it returns the
        // Func without calling it. Only Reconciler.TryResolveFromControlRegistry
        // invokes the factory (once per host, on cache miss).
        Assert.Equal(0, factoryCalls);

        var rec = new Reconciler();
        Assert.True(rec.TryResolveFromControlRegistry(typeof(WalkCacheDerivedElement), out _));
        var rec2 = new Reconciler();
        Assert.True(rec2.TryResolveFromControlRegistry(typeof(WalkCacheDerivedElement), out _));
        // One factory invocation per host hit; the per-host _v1Handlers
        // cache absorbs all subsequent dispatches on the same host.
        Assert.Equal(2, factoryCalls);
    }

    public abstract record NegCacheBaseElement(string Tag) : Element;
    public record NegCacheDerivedElement(string Tag) : NegCacheBaseElement(Tag);
    public sealed class NegCacheBaseHandler : IElementHandler<NegCacheBaseElement, UIElement>
    {
        public UIElement Mount(MountContext ctx, NegCacheBaseElement element) => null!;
        public void Update(UpdateContext ctx, NegCacheBaseElement oldEl, NegCacheBaseElement newEl, UIElement control) { }
    }

    [Fact]
    public void RegisterForDerivedTypes_Negative_Cache_Is_Invalidated_When_Later_Base_Registered()
    {
        // First lookup misses → null marker cached.
        var rec = new Reconciler();
        Assert.False(rec.TryResolveFromControlRegistry(typeof(NegCacheDerivedElement), out _));

        // Now register a base. The null marker for NegCacheDerivedElement
        // must be invalidated so the next lookup walks again and finds
        // the new base entry.
        ControlRegistry.RegisterForDerivedTypes<NegCacheBaseElement, UIElement>(
            static () => new NegCacheBaseHandler());

        var rec2 = new Reconciler();
        Assert.True(rec2.TryResolveFromControlRegistry(typeof(NegCacheDerivedElement), out _));
    }

    public abstract record BaseTwiceBaseElement(string Tag) : Element;
    public record BaseTwiceDerivedElement(string Tag) : BaseTwiceBaseElement(Tag);
    public sealed class BaseTwiceBaseHandler : IElementHandler<BaseTwiceBaseElement, UIElement>
    {
        public string? Identity { get; set; }
        public UIElement Mount(MountContext ctx, BaseTwiceBaseElement element) => null!;
        public void Update(UpdateContext ctx, BaseTwiceBaseElement oldEl, BaseTwiceBaseElement newEl, UIElement control) { }
    }

    [Fact]
    public void RegisterForDerivedTypes_Twice_For_Same_Base_Is_Silent_NoOp_First_Wins()
    {
        Assert.False(ControlRegistry.ContainsBase(typeof(BaseTwiceBaseElement)));

        var firstCalls = 0;
        var secondCalls = 0;

        ControlRegistry.RegisterForDerivedTypes<BaseTwiceBaseElement, UIElement>(() =>
        {
            Interlocked.Increment(ref firstCalls);
            return new BaseTwiceBaseHandler { Identity = "first" };
        });
        ControlRegistry.RegisterForDerivedTypes<BaseTwiceBaseElement, UIElement>(() =>
        {
            Interlocked.Increment(ref secondCalls);
            return new BaseTwiceBaseHandler { Identity = "second" };
        });

        Assert.True(ControlRegistry.ContainsBase(typeof(BaseTwiceBaseElement)));

        var rec = new Reconciler();
        Assert.True(rec.TryResolveFromControlRegistry(typeof(BaseTwiceDerivedElement), out _));
        Assert.Equal(1, firstCalls);
        Assert.Equal(0, secondCalls);
    }

    public abstract record DecBaseResolveBaseElement(string Tag) : Element;
    public record DecBaseResolveDerivedA(string Tag) : DecBaseResolveBaseElement(Tag);
    public record DecBaseResolveDerivedB(string Tag) : DecBaseResolveBaseElement(Tag);
    public sealed class DecBaseResolveBaseHandler : IDecoratorElementHandler<DecBaseResolveBaseElement>
    {
        public UIElement Mount(MountContext ctx, DecBaseResolveBaseElement element) => null!;
        public UIElement Update(UpdateContext ctx, DecBaseResolveBaseElement oldEl, DecBaseResolveBaseElement newEl, UIElement control) => control;
        public V1UnmountDisposition Unmount(UnmountContext ctx, DecBaseResolveBaseElement? element, UIElement control)
            => V1UnmountDisposition.CollectSelf;
    }

    [Fact]
    public void RegisterDecoratorForDerivedTypes_Resolves_Derived_Element_Type_To_Base_Handler()
    {
        Assert.False(ControlRegistry.ContainsBase(typeof(DecBaseResolveBaseElement)));

        ControlRegistry.RegisterDecoratorForDerivedTypes<DecBaseResolveBaseElement>(
            static () => new DecBaseResolveBaseHandler());

        Assert.False(ControlRegistry.Contains(typeof(DecBaseResolveDerivedA)));
        Assert.True(ControlRegistry.ContainsBase(typeof(DecBaseResolveBaseElement)));
        Assert.True(ControlRegistry.ContainsForType(typeof(DecBaseResolveDerivedA)));
        Assert.True(ControlRegistry.ContainsForType(typeof(DecBaseResolveDerivedB)));

        var rec = new Reconciler();
        Assert.True(rec.TryResolveFromControlRegistry(typeof(DecBaseResolveDerivedA), out _));
        Assert.True(rec.TryResolveFromControlRegistry(typeof(DecBaseResolveDerivedB), out _));
    }

    public abstract record DecBaseFirstWinsBaseElement(string Tag) : Element;
    public record DecBaseFirstWinsDerivedElement(string Tag) : DecBaseFirstWinsBaseElement(Tag);
    public sealed class DecBaseFirstWinsValueHandler : IElementHandler<DecBaseFirstWinsBaseElement, UIElement>
    {
        public UIElement Mount(MountContext ctx, DecBaseFirstWinsBaseElement element) => null!;
        public void Update(UpdateContext ctx, DecBaseFirstWinsBaseElement oldEl, DecBaseFirstWinsBaseElement newEl, UIElement control) { }
    }
    public sealed class DecBaseFirstWinsDecoratorHandler : IDecoratorElementHandler<DecBaseFirstWinsBaseElement>
    {
        public UIElement Mount(MountContext ctx, DecBaseFirstWinsBaseElement element) => null!;
        public UIElement Update(UpdateContext ctx, DecBaseFirstWinsBaseElement oldEl, DecBaseFirstWinsBaseElement newEl, UIElement control) => control;
        public V1UnmountDisposition Unmount(UnmountContext ctx, DecBaseFirstWinsBaseElement? element, UIElement control)
            => V1UnmountDisposition.CollectSelf;
    }

    [Fact]
    public void RegisterDecoratorForDerivedTypes_Value_And_Decorator_FirstWins_For_Same_Base()
    {
        Assert.False(ControlRegistry.ContainsBase(typeof(DecBaseFirstWinsBaseElement)));

        ControlRegistry.RegisterForDerivedTypes<DecBaseFirstWinsBaseElement, UIElement>(
            static () => new DecBaseFirstWinsValueHandler());
        // Second decorator-flavoured registration for the same base must
        // be a silent no-op (single s_baseEntries slot per type, first
        // wins regardless of value vs. decorator shim).
        ControlRegistry.RegisterDecoratorForDerivedTypes<DecBaseFirstWinsBaseElement>(
            static () => new DecBaseFirstWinsDecoratorHandler());

        Assert.True(ControlRegistry.ContainsBase(typeof(DecBaseFirstWinsBaseElement)));
        var rec = new Reconciler();
        Assert.True(rec.TryResolveFromControlRegistry(typeof(DecBaseFirstWinsDerivedElement), out _));
    }

    public abstract record NullValueBaseElement(string Tag) : Element;
    public abstract record NullDecBaseElement(string Tag) : Element;

    [Fact]
    public void RegisterForDerivedTypes_Throws_On_Null_Factory()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ControlRegistry.RegisterForDerivedTypes<NullValueBaseElement, UIElement>(null!));
    }

    [Fact]
    public void RegisterDecoratorForDerivedTypes_Throws_On_Null_Factory()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ControlRegistry.RegisterDecoratorForDerivedTypes<NullDecBaseElement>(null!));
    }
}

[CollectionDefinition(nameof(ControlRegistryTestCollection), DisableParallelization = true)]
public sealed class ControlRegistryTestCollection { }
