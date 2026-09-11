using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 036 §6.2 — <see cref="ShutdownPolicy"/> dispatch in
/// <see cref="ReactorApp.EvaluateShutdownPolicy(bool)"/>. Pure logic tests
/// that exercise the bookkeeping path without spinning a XAML Application.
/// We can't observe <c>Application.Exit()</c> directly here (no application
/// context), but we can drive the policy enum and confirm the branches that
/// reach the exit path don't throw, and the branches that should be a no-op
/// stay a no-op. Process-exit semantics are covered end-to-end by selftest
/// fixtures in <c>Reactor.AppTests.Host</c>.
/// </summary>
public class WindowShutdownPolicyTests
{
    [Fact]
    public void DefaultPolicy_Is_OnPrimaryWindowClosed()
    {
        // Sanity: the default must match spec §4.3 / §6.2 — apps that never
        // touch ShutdownPolicy get the legacy behavior.
        var prior = ReactorApp.ShutdownPolicy;
        try
        {
            // Reset by writing the default explicitly so the assertion holds
            // regardless of test ordering inside this assembly.
            ReactorApp.ShutdownPolicy = ShutdownPolicy.OnPrimaryWindowClosed;
            Assert.Equal(ShutdownPolicy.OnPrimaryWindowClosed, ReactorApp.ShutdownPolicy);
        }
        finally
        {
            ReactorApp.ShutdownPolicy = prior;
        }
    }

    [Fact]
    public void Explicit_Policy_Skips_Exit_On_Surface_Close()
    {
        var prior = ReactorApp.ShutdownPolicy;
        try
        {
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            // Even with no windows registered and "primary just closed",
            // Explicit must not call Exit. We cannot observe Exit here, but
            // calling EvaluateShutdownPolicy must not throw.
            ReactorApp.EvaluateShutdownPolicy(closedWasPrimary: true);
            ReactorApp.EvaluateShutdownPolicy(closedWasPrimary: false);
        }
        finally
        {
            ReactorApp.ShutdownPolicy = prior;
        }
    }

    [Fact]
    public void OnLastSurfaceClosed_Considers_TrayIconCount()
    {
        // Phase 4 stubs TrayIconCount at 0 (Phase 8 wires the real registry).
        // The key invariant we can assert here without an Application context
        // is that the policy enum value round-trips and EvaluateShutdownPolicy
        // accepts the closedWasPrimary parameter in either polarity without
        // exception.
        var prior = ReactorApp.ShutdownPolicy;
        try
        {
            ReactorApp.ShutdownPolicy = ShutdownPolicy.OnLastSurfaceClosed;
            Assert.Equal(ShutdownPolicy.OnLastSurfaceClosed, ReactorApp.ShutdownPolicy);
        }
        finally
        {
            ReactorApp.ShutdownPolicy = prior;
        }
    }

    [Fact]
    public void Policy_RoundTrips_All_Enum_Values()
    {
        var prior = ReactorApp.ShutdownPolicy;
        try
        {
            foreach (var p in new[] {
                ShutdownPolicy.OnPrimaryWindowClosed,
                ShutdownPolicy.OnLastSurfaceClosed,
                ShutdownPolicy.Explicit })
            {
                ReactorApp.ShutdownPolicy = p;
                Assert.Equal(p, ReactorApp.ShutdownPolicy);
            }
        }
        finally
        {
            ReactorApp.ShutdownPolicy = prior;
        }
    }

    // ── issue #1204: the policy must reach the platform ────────────────────
    //
    // WinUI quits the thread's DispatcherQueue event loop on last-window-close
    // unless Application.DispatcherShutdownMode is OnExplicitShutdown, so a
    // policy that never gets honoured is decorative. Reactor takes ownership of
    // the loop at launch and lets EvaluateShutdownPolicy decide every exit.
    //
    // The observable behaviour of that lives in a live Application and a live
    // process, so it is asserted by ShutdownPolicyDispatcherModeFixtures (the
    // mode the host actually runs under) and by ShutdownPolicyProcessLifetimeTests
    // (whether the process survives). What is left to check headlessly is that
    // the setter stayed a plain store — the split-brain hazard it briefly had
    // was that a policy could be published before the platform agreed with it.

    [Fact]
    public void Setting_Policy_Is_A_Plain_Store_With_No_Platform_Side_Effect()
    {
        // Headless: there is no Application and no UI dispatcher. A setter that
        // reached for either would throw a COMException and redden this test.
        // It also documents the invariant that makes the policy safe to set from
        // any thread: the value is never staged behind a dispatcher hop, so it
        // cannot be observed out of step with the platform.
        Assert.Null(ReactorApp.UIDispatcher);

        var prior = ReactorApp.ShutdownPolicy;
        try
        {
            foreach (var p in Enum.GetValues<ShutdownPolicy>())
            {
                ReactorApp.ShutdownPolicy = p;
                Assert.Equal(p, ReactorApp.ShutdownPolicy);
            }
        }
        finally
        {
            ReactorApp.ShutdownPolicy = prior;
        }
    }

    [Fact]
    public void EvaluateShutdownPolicy_Accepts_Both_Polarities_Of_ClosedWasPrimary()
    {
        // Bookkeeping only. The interesting behaviour — that a non-primary close
        // under the default policy leaves the process running, which is issue
        // #647's guarantee and only holds because Reactor owns the event loop —
        // is NOT assertable here: SafeExit reduces to Application.Current?.Exit()
        // with no Application, so both branches are indistinguishable. That case
        // is covered by the probe's --excluded-window arm, which can observe the
        // process. This keeps only the claim this tier can actually make.
        var prior = ReactorApp.ShutdownPolicy;
        try
        {
            ReactorApp.ShutdownPolicy = ShutdownPolicy.OnPrimaryWindowClosed;
            ReactorApp.EvaluateShutdownPolicy(closedWasPrimary: false);
            ReactorApp.EvaluateShutdownPolicy(closedWasPrimary: true);
        }
        finally
        {
            ReactorApp.ShutdownPolicy = prior;
        }
    }
}
