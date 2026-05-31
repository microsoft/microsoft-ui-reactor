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
/// <para>Each test resets the global registry first so case ordering is
/// irrelevant; nothing in Phase 1 of spec 048 registers into the global
/// registry yet (built-ins still go through <c>RegisterV1BuiltInHandlers</c>),
/// so the reset is safe for the rest of the suite.</para>
/// </summary>
[Collection(nameof(ControlRegistryTestCollection))]
public class ControlRegistryTests : IDisposable
{
    public ControlRegistryTests() => ControlRegistry.ResetForTesting();
    public void Dispose() => ControlRegistry.ResetForTesting();

    // ── Test fixtures — pure-data elements, simple counting handlers ──

    public record ProbeElement(string Tag) : Element;
    public record OtherProbeElement(string Tag) : Element;

    public sealed class ProbeHandler : IElementHandler<ProbeElement, UIElement>
    {
        public string? Identity { get; set; }
        public UIElement Mount(MountContext ctx, ProbeElement element) => null!;
        public void Update(UpdateContext ctx, ProbeElement oldEl, ProbeElement newEl, UIElement control) { }
    }

    public sealed class OtherProbeHandler : IElementHandler<OtherProbeElement, UIElement>
    {
        public UIElement Mount(MountContext ctx, OtherProbeElement element) => null!;
        public void Update(UpdateContext ctx, OtherProbeElement oldEl, OtherProbeElement newEl, UIElement control) { }
    }

    // ─────────────────────────────────────────────────────────────────
    // 1.3 — bullet 1: idempotent Register (second call is a silent no-op).
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Register_Same_Element_Type_Twice_Is_Silent_NoOp()
    {
        var firstFactoryCalls = 0;
        var secondFactoryCalls = 0;

        ControlRegistry.Register<ProbeElement, UIElement>(() =>
        {
            Interlocked.Increment(ref firstFactoryCalls);
            return new ProbeHandler { Identity = "first" };
        });

        // Second registration must NOT throw and must NOT replace the first.
        ControlRegistry.Register<ProbeElement, UIElement>(() =>
        {
            Interlocked.Increment(ref secondFactoryCalls);
            return new ProbeHandler { Identity = "second" };
        });

        Assert.Equal(1, ControlRegistry.Count);

        // The first registration's factory wins on resolution.
        var rec = new Reconciler();
        Assert.True(rec.TryResolveFromControlRegistry(typeof(ProbeElement), out _));

        Assert.Equal(1, firstFactoryCalls);
        Assert.Equal(0, secondFactoryCalls);
    }

    [Fact]
    public void Register_Throws_On_Null_Factory()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ControlRegistry.Register<ProbeElement, UIElement>(null!));
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

    [Fact]
    public void Registry_Factory_Invoked_Exactly_Once_Across_Many_Sequential_Dispatches_On_Same_Host()
    {
        var factoryCalls = 0;
        ControlRegistry.Register<ProbeElement, UIElement>(() =>
        {
            Interlocked.Increment(ref factoryCalls);
            return new ProbeHandler();
        });

        var rec = new Reconciler();

        // Mimic the Mount dispatch path: try per-host first, fall through
        // to the registry arm on miss. This loop simulates many sequential
        // mounts of the same element type on the same host.
        for (var i = 0; i < 256; i++)
        {
            if (rec._v1Handlers.TryGet(typeof(ProbeElement), out _))
                continue;
            rec.TryResolveFromControlRegistry(typeof(ProbeElement), out _);
        }

        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task Register_Concurrent_Distinct_Types_Idempotent_No_Throws()
    {
        // Hammer Register from many threads with two distinct element
        // types; ConcurrentDictionary.TryAdd must accept the first
        // registration for each type and silently no-op the rest, without
        // throwing or deadlocking.
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
                    ControlRegistry.Register<ProbeElement, UIElement>(static () => new ProbeHandler());
                else
                    ControlRegistry.Register<OtherProbeElement, UIElement>(static () => new OtherProbeHandler());
            });
        }
        await Task.WhenAll(tasks);

        Assert.Equal(2, ControlRegistry.Count);
        Assert.True(ControlRegistry.Contains(typeof(ProbeElement)));
        Assert.True(ControlRegistry.Contains(typeof(OtherProbeElement)));
    }

    // ─────────────────────────────────────────────────────────────────
    // 1.3 — bullet 2 / §12.2 — dispatch precedence: per-host
    // RegisterHandler shadows a globally-registered handler for the same
    // element type when wired up before the first dispatch on that host.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void PerHost_RegisterHandler_Shadows_Global_ControlRegistry()
    {
        var globalFactoryCalls = 0;
        ControlRegistry.Register<ProbeElement, UIElement>(() =>
        {
            Interlocked.Increment(ref globalFactoryCalls);
            return new ProbeHandler { Identity = "global" };
        });

        var rec = new Reconciler();
        var perHostHandler = new ProbeHandler { Identity = "per-host" };
        rec.RegisterHandler<ProbeElement, UIElement>(perHostHandler);

        // Arm 1 (per-host) hits first; the global registry factory never
        // runs on this host because the Mount/Update dispatch never falls
        // through to arm 3.
        Assert.True(rec._v1Handlers.TryGet(typeof(ProbeElement), out _));
        Assert.Equal(0, globalFactoryCalls);
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
        ControlRegistry.Register<ProbeElement, UIElement>(static () => new ProbeHandler());

        var rec = new Reconciler();
        Assert.True(rec.TryResolveFromControlRegistry(typeof(ProbeElement), out _));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            rec.RegisterHandler<ProbeElement, UIElement>(new ProbeHandler()));
        Assert.Contains("ProbeElement", ex.Message);
    }

    // ─────────────────────────────────────────────────────────────────
    // 1.3 — bullet 3: cache test — after the first global-table hit on a
    // given host, the registry's factory delegate is not invoked on
    // subsequent mounts of the same element type on that host.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Registry_Factory_Result_Cached_Into_PerHost_V1Handlers()
    {
        var factoryCalls = 0;
        ControlRegistry.Register<ProbeElement, UIElement>(() =>
        {
            Interlocked.Increment(ref factoryCalls);
            return new ProbeHandler();
        });

        var rec = new Reconciler();

        // First arm-3 dispatch ⇒ factory runs once, result cached into
        // _v1Handlers.
        Assert.True(rec.TryResolveFromControlRegistry(typeof(ProbeElement), out var first));
        Assert.Equal(1, factoryCalls);
        Assert.NotNull(first);

        // Subsequent dispatches simulate the Mount fast path: arm 1
        // (_v1Handlers.TryGet) hits, the registry resolution helper is
        // never re-entered. Verify the same adapter instance is returned
        // on every subsequent lookup (handler identity preserved).
        for (var i = 0; i < 10; i++)
        {
            Assert.True(rec._v1Handlers.TryGet(typeof(ProbeElement), out var cached));
            Assert.Same(first, cached);
        }

        Assert.Equal(1, factoryCalls);
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
        ControlRegistry.Register<ProbeElement, UIElement>(() =>
        {
            Interlocked.Increment(ref factoryCalls);
            return new ProbeHandler();
        });

        var rec1 = new Reconciler();
        var rec2 = new Reconciler();

        Assert.True(rec1.TryResolveFromControlRegistry(typeof(ProbeElement), out var a1));
        Assert.True(rec2.TryResolveFromControlRegistry(typeof(ProbeElement), out var a2));

        Assert.NotSame(a1, a2);
        Assert.Equal(2, factoryCalls);
    }

    // ─────────────────────────────────────────────────────────────────
    // Resolution semantics — TryResolve returns false for unregistered
    // element types, and the per-host cache is *not* polluted on a miss.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TryResolveFromControlRegistry_Returns_False_On_Miss_Without_Polluting_Cache()
    {
        var rec = new Reconciler();
        Assert.False(rec.TryResolveFromControlRegistry(typeof(ProbeElement), out _));
        Assert.False(rec._v1Handlers.TryGet(typeof(ProbeElement), out _));
    }

    [Fact]
    public void Register_Allows_Distinct_Element_Types_Independently()
    {
        ControlRegistry.Register<ProbeElement, UIElement>(static () => new ProbeHandler());
        ControlRegistry.Register<OtherProbeElement, UIElement>(static () => new OtherProbeHandler());

        Assert.Equal(2, ControlRegistry.Count);
        Assert.True(ControlRegistry.Contains(typeof(ProbeElement)));
        Assert.True(ControlRegistry.Contains(typeof(OtherProbeElement)));
    }

    // ═════════════════════════════════════════════════════════════════
    // Spec 048 §3.4 — RegisterDecorator contract. Decorator handlers
    // implement the SEPARATE IDecoratorElementHandler<TElement>
    // interface and need a parallel registration entry point that
    // bridges to V1DecoratorHandlerAdapter<TElement>.
    // ═════════════════════════════════════════════════════════════════

    public record DecoratorProbeElement(string Tag) : Element;

    public sealed class DecoratorProbeHandler : IDecoratorElementHandler<DecoratorProbeElement>
    {
        public string? Identity { get; set; }
        public UIElement Mount(MountContext ctx, DecoratorProbeElement element) => null!;
        public UIElement Update(UpdateContext ctx, DecoratorProbeElement oldEl, DecoratorProbeElement newEl, UIElement control) => control;
        public V1UnmountDisposition Unmount(UnmountContext ctx, DecoratorProbeElement? element, UIElement control)
            => V1UnmountDisposition.CollectSelf;
    }

    [Fact]
    public void RegisterDecorator_Adds_Element_Type_To_Registry()
    {
        Assert.Equal(0, ControlRegistry.Count);

        ControlRegistry.RegisterDecorator<DecoratorProbeElement>(static () => new DecoratorProbeHandler());

        Assert.Equal(1, ControlRegistry.Count);
        Assert.True(ControlRegistry.Contains(typeof(DecoratorProbeElement)));
        Assert.True(ControlRegistry.TryResolve(typeof(DecoratorProbeElement), out _));
    }

    [Fact]
    public void RegisterDecorator_Throws_On_Null_Factory()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ControlRegistry.RegisterDecorator<DecoratorProbeElement>(null!));
    }

    [Fact]
    public void RegisterDecorator_Twice_Is_Silent_NoOp_First_Wins()
    {
        var firstFactoryCalls = 0;
        var secondFactoryCalls = 0;

        ControlRegistry.RegisterDecorator<DecoratorProbeElement>(() =>
        {
            Interlocked.Increment(ref firstFactoryCalls);
            return new DecoratorProbeHandler { Identity = "first" };
        });

        ControlRegistry.RegisterDecorator<DecoratorProbeElement>(() =>
        {
            Interlocked.Increment(ref secondFactoryCalls);
            return new DecoratorProbeHandler { Identity = "second" };
        });

        Assert.Equal(1, ControlRegistry.Count);

        // Resolve to invoke the winning factory and confirm it's the first.
        var rec = new Reconciler();
        Assert.True(rec.TryResolveFromControlRegistry(typeof(DecoratorProbeElement), out _));

        Assert.Equal(1, firstFactoryCalls);
        Assert.Equal(0, secondFactoryCalls);
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

        // Reusing DecoratorProbeElement as a fake "value-path" element
        // type just for this collision test; the value-path handler is a
        // throwaway no-op shape.
        ControlRegistry.Register<DecoratorProbeElement, UIElement>(() =>
        {
            Interlocked.Increment(ref valueFactoryCalls);
            return new ValueAdapterShim();
        });

        ControlRegistry.RegisterDecorator<DecoratorProbeElement>(() =>
        {
            Interlocked.Increment(ref decoratorFactoryCalls);
            return new DecoratorProbeHandler();
        });

        Assert.Equal(1, ControlRegistry.Count);

        var rec = new Reconciler();
        Assert.True(rec.TryResolveFromControlRegistry(typeof(DecoratorProbeElement), out _));

        // Value path registered first → its factory ran on resolve.
        // Decorator factory was silently dropped.
        Assert.Equal(1, valueFactoryCalls);
        Assert.Equal(0, decoratorFactoryCalls);
    }

    private sealed class ValueAdapterShim : IElementHandler<DecoratorProbeElement, UIElement>
    {
        public UIElement Mount(MountContext ctx, DecoratorProbeElement element) => null!;
        public void Update(UpdateContext ctx, DecoratorProbeElement oldEl, DecoratorProbeElement newEl, UIElement control) { }
    }

    [Fact]
    public void RegisterDecorator_Resolved_Entry_Is_Cached_Into_Host_V1Handlers()
    {
        ControlRegistry.RegisterDecorator<DecoratorProbeElement>(static () => new DecoratorProbeHandler());

        var rec = new Reconciler();
        Assert.False(rec._v1Handlers.TryGet(typeof(DecoratorProbeElement), out _));

        Assert.True(rec.TryResolveFromControlRegistry(typeof(DecoratorProbeElement), out var entry1));

        // After a registry hit the per-host cache holds the adapter, so a
        // second TryGet on _v1Handlers short-circuits without re-walking
        // the registry. This is the documented arm 3 → arm 1 cache hop.
        Assert.True(rec._v1Handlers.TryGet(typeof(DecoratorProbeElement), out var entry2));
        Assert.Same(entry1, entry2);
    }
}

[CollectionDefinition(nameof(ControlRegistryTestCollection), DisableParallelization = true)]
public sealed class ControlRegistryTestCollection { }
