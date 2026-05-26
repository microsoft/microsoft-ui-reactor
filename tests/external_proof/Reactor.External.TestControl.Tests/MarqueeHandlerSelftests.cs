using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Reactor.External.TestControl.Tests;

/// <summary>
/// Spec 047 §14 Phase 1 (1.16) — external-assembly proof selftests.
///
/// Tests that require a real WinUI dispatcher (value-bearing prop write,
/// custom-event subscription, modifier chain, setter chain) are gated
/// with <c>Skip</c> here; their live equivalents live in the
/// <c>Reactor.AppTests.Host</c> harness — see
/// <c>SelfTest/Fixtures/Spec047ExternalProofFixtures.cs</c>.
///
/// The hermetic tests below cover the engine-side invariants that DON'T
/// need a dispatcher: registration semantics, pool API surface
/// observability, and the no-<c>InternalsVisibleTo</c> proof (free — if
/// this assembly compiled, it didn't touch internals).
/// </summary>
public class MarqueeHandlerSelftests
{
    // ────────────────────────────────────────────────────────────────────
    //  Registration semantics — pure engine, no dispatcher needed.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterHandler_Succeeds_ForExternalElementType()
    {
        var reconciler = new Reconciler(logger: null, useV1Protocol: true);
        // Public RegisterHandler API — author surface only. Compiles
        // because Reactor.dll exposes IElementHandler, MountContext,
        // ReactorBinding<T>, and ChildrenStrategy as public types.
        reconciler.RegisterHandler<MarqueeElement, MarqueeControl>(new MarqueeHandler());
        // No exception means the registry accepted the external handler
        // alongside the five built-in V1 ports.
    }

    [Fact]
    public void RegisterHandler_Twice_Throws()
    {
        var reconciler = new Reconciler(logger: null, useV1Protocol: true);
        reconciler.RegisterHandler<MarqueeElement, MarqueeControl>(new MarqueeHandler());

        // V1 semantics per spec §13 Q17 — throw on duplicate. The exception
        // type is the documented InvalidOperationException.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            reconciler.RegisterHandler<MarqueeElement, MarqueeControl>(new MarqueeHandler()));

        Assert.Contains("MarqueeElement", ex.Message);
    }

    [Fact]
    public void RegisterHandler_DoesNotCollide_WithUnrelatedV1Handler()
    {
        var reconciler = new Reconciler(logger: null, useV1Protocol: true);
        // The five built-in V1 ports are already in the registry via the
        // Reconciler ctor. A fresh element type (MarqueeElement) must be
        // accepted alongside them.
        reconciler.RegisterHandler<MarqueeElement, MarqueeControl>(new MarqueeHandler());
    }

    // ────────────────────────────────────────────────────────────────────
    //  Pool API surface observability — pure engine, no dispatcher needed
    //  for the API call itself, but WinUI control construction (Border,
    //  UserControl) requires a WinUI activation context. So the rent/return
    //  exercises live in the AppTests.Host fixture; only the Skip stubs
    //  stay here as test-discovery markers.
    // ────────────────────────────────────────────────────────────────────

    [Fact(Skip = "WinUI control construction (Border, MarqueeControl) requires an activation context; lives in Spec047ExternalProof_Marquee_PoolRent")]
    public void RentControl_NonPoolableType_ReturnsFreshInstance_NoException()
    {
        // See Spec047ExternalProofFixtures.MarqueePoolRentReturn.
    }

    [Fact(Skip = "WinUI control construction (MarqueeControl) requires an activation context; lives in Spec047ExternalProof_Marquee_PoolReturn")]
    public void ReturnControl_NonPoolableType_IsSafeNoOp()
    {
        // See Spec047ExternalProofFixtures.MarqueePoolRentReturn.
    }

    [Fact(Skip = "WinUI control construction (Border) requires an activation context; lives in Spec047ExternalProof_Marquee_PoolResetContract")]
    public void PoolResetContract_Observable_ThroughPublicSurface_ForPoolableType()
    {
        // See Spec047ExternalProofFixtures.MarqueePoolResetContract.
    }

    // ────────────────────────────────────────────────────────────────────
    //  WinUI-dispatcher-bound selftests are gated; the live versions live
    //  in Reactor.AppTests.Host as Spec047ExternalProofFixtures (registered
    //  in SelfTestFixtureRegistry.cs). These stub methods document the
    //  fixture coverage and ensure the test-discovery surface is visible
    //  at a glance from the test project.
    // ────────────────────────────────────────────────────────────────────

    [Fact(Skip = "Requires WinUI dispatcher; lives in AppTests.Host fixture Spec047ExternalProof_Marquee_MountUpdate")]
    public void Marquee_MountUpdate_LiveControl()
    {
        // See Spec047ExternalProofFixtures.MarqueeMountUpdate.
    }

    [Fact(Skip = "Requires WinUI dispatcher; lives in AppTests.Host fixture Spec047ExternalProof_Marquee_CustomEvent")]
    public void Marquee_CustomEventSubscription()
    {
        // See Spec047ExternalProofFixtures.MarqueeCaptionChangedTrampoline.
    }

    [Fact(Skip = "Requires WinUI dispatcher; lives in AppTests.Host fixture Spec047ExternalProof_Marquee_ModifierChain")]
    public void Marquee_ModifierChain()
    {
        // See Spec047ExternalProofFixtures.MarqueeModifierChain.
    }

    [Fact(Skip = "Requires WinUI dispatcher; lives in AppTests.Host fixture Spec047ExternalProof_Marquee_SetterChain")]
    public void Marquee_SetterChain()
    {
        // See Spec047ExternalProofFixtures.MarqueeSetterChain.
    }

    [Fact(Skip = "Requires WinUI dispatcher; lives in AppTests.Host fixture Spec047ExternalProof_Marquee_WriteSuppressed")]
    public void Marquee_WriteSuppressed_DoesNotEcho()
    {
        // See Spec047ExternalProofFixtures.MarqueeWriteSuppressedEcho.
    }

    // ────────────────────────────────────────────────────────────────────
    //  No-InternalsVisibleTo audit — compile-time assertion.
    //
    //  The Reactor.csproj <ItemGroup> with InternalsVisibleTo includes
    //  exactly three test assemblies: Reactor.Tests, Reactor.AppTests.Host,
    //  Reactor.Fuzz. This assembly is NOT on that list and references
    //  Reactor as a regular project reference. The very fact that all
    //  files in Reactor.External.TestControl compiled is proof that
    //  Reactor's public surface is sufficient for an external author.
    //
    //  This test is a no-op; the compile-time invariant is the real
    //  assertion. It exists so the audit is callable by name in a CI
    //  inventory pass.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoInternalsVisibleTo_AuditMarker()
    {
        // If this test assembly was on Reactor.csproj's InternalsVisibleTo
        // list, the external-assembly proof would be invalidated. The check
        // itself is at csproj-edit time (PR review). This test is a
        // permanent marker so the invariant has a name CI can search.
        Assert.True(true);
    }
}
