using System;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec047.V1Protocol;

/// <summary>
/// Spec 047 §13 Q18 / §14 Phase 1 (1.5) — <see cref="PoolPolicy{TControl}"/>
/// + <see cref="Reconciler.RentControl{T}"/> / <see cref="Reconciler.ReturnControl{T}"/>
/// contract tests.
///
/// The WinUI-control pool integration (rent FrameworkElement, observe
/// CleanElement contract) requires the STA dispatcher and is covered by
/// the AppTests.Host fixture; here we exercise the pure-C# parts of the
/// API contract via a constructible non-WinUI fake type.
///
/// The real FrameworkElement rent/return cycle that observes the engine reset
/// contract is covered by the AppTests.Host self-test fixtures
/// (Spec047EventStateSplitFixtures, spec 047 §4.3): ModifierEventHandlerState
/// Current* user delegates cleared, ControlEventState PRESERVED across
/// rent/return (issue #114), ReactorAttached.StateProperty Tag cleared.
/// </summary>
public class PoolPolicyTests
{
    /// <summary>Constructible non-WinUI fake — exercises the
    /// non-FrameworkElement branch of RentControl/ReturnControl.</summary>
    public sealed class FakeControl { public int ResetCount; }

    [Fact]
    public void RentControl_NonFrameworkElement_Uses_New_When_No_Factory()
    {
        var rec = new Reconciler();
        var ctrl = rec.RentControl<FakeControl>();
        Assert.NotNull(ctrl);
        Assert.IsType<FakeControl>(ctrl);
    }

    [Fact]
    public void RentControl_NonFrameworkElement_Uses_Factory_When_Supplied()
    {
        var rec = new Reconciler();
        var sentinel = new FakeControl { ResetCount = 42 };
        var ctrl = rec.RentControl(policy: null, factory: () => sentinel);
        Assert.Same(sentinel, ctrl);
    }

    [Fact]
    public void RentControl_With_IsPoolable_False_Allocates_Fresh()
    {
        var rec = new Reconciler();
        var policy = new PoolPolicy<FakeControl> { IsPoolable = false };
        var a = rec.RentControl(policy);
        var b = rec.RentControl(policy);
        // No pool path runs — two fresh instances.
        Assert.NotSame(a, b);
    }

    [Fact]
    public void ReturnControl_With_IsPoolable_False_Is_NoOp()
    {
        var rec = new Reconciler();
        bool resetRan = false;
        var policy = new PoolPolicy<FakeControl>
        {
            IsPoolable = false,
            Reset = _ => { resetRan = true; }
        };
        var ctrl = new FakeControl();

        rec.ReturnControl(ctrl, policy);

        // Per Q18: IsPoolable=false means ReturnControl is a no-op,
        // including skipping policy.Reset.
        Assert.False(resetRan);
    }

    [Fact]
    public void ReturnControl_Policy_Reset_Runs_Last()
    {
        var rec = new Reconciler();
        var ctrl = new FakeControl();
        int resetInvocations = 0;
        var policy = new PoolPolicy<FakeControl>
        {
            Reset = c => { c.ResetCount++; resetInvocations++; }
        };

        rec.ReturnControl(ctrl, policy);

        Assert.Equal(1, resetInvocations);
        Assert.Equal(1, ctrl.ResetCount);
    }

    [Fact]
    public void ReturnControl_Throws_On_Null_Control()
    {
        var rec = new Reconciler();
        Assert.Throws<ArgumentNullException>(() => rec.ReturnControl<FakeControl>(null!));
    }

    [Fact]
    public void ReturnControl_Twice_Is_Safe()
    {
        // Spec 047 §13 Q18 — dual-RCW idempotency: ReturnControl twice
        // must not throw. For non-FrameworkElement targets the engine
        // simply runs the policy reset (no pool involvement).
        var rec = new Reconciler();
        var ctrl = new FakeControl();
        var policy = new PoolPolicy<FakeControl> { Reset = c => c.ResetCount++ };

        rec.ReturnControl(ctrl, policy);
        rec.ReturnControl(ctrl, policy);

        // Both invocations ran their reset; no exception, no double-clear hazard
        // (FakeControl tracks invocation count for observation only).
        Assert.Equal(2, ctrl.ResetCount);
    }

    [Fact]
    public void PoolPolicy_Defaults()
    {
        var policy = new PoolPolicy<FakeControl>();
        Assert.True(policy.IsPoolable);
        Assert.Null(policy.Reset);
    }

    // TODO(1.11/1.16): the FrameworkElement rent/return cycle (pool
    // integration, observing the engine reset contract on a real
    // ToggleSwitch / TextBox) requires the WinUI dispatcher and lands
    // in the AppTests.Host fixture site when ToggleSwitch is ported.
    [Fact(Skip = "Requires WinUI dispatcher; covered in AppTests.Host fixture site (1.11+)")]
    public void ReturnControl_FrameworkElement_Reset_Contract_Clears_State()
    {
        // Spec 047 §13 Q18 — rent a control, set element tag / handlers,
        // ReturnControl, rent again. Assert GetElementTag is null,
        // EchoSuppressCount/EchoSuppressScopeDepth are 0,
        // ControlEventState is null. Will live alongside the
        // InputControlsFireEvents fixture in 1.11.
    }
}
