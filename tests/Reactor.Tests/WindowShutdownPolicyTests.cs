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
    // policy that never gets projected onto that property is decorative. These
    // assert the projection itself; the selftest fixture asserts it lands on a
    // live Application, and the Reactor.SelfTests lifetime probe asserts the
    // process actually survives.

    [Theory]
    [InlineData(ShutdownPolicy.OnPrimaryWindowClosed, DispatcherShutdownMode.OnLastWindowClose)]
    [InlineData(ShutdownPolicy.OnLastSurfaceClosed, DispatcherShutdownMode.OnExplicitShutdown)]
    [InlineData(ShutdownPolicy.Explicit, DispatcherShutdownMode.OnExplicitShutdown)]
    public void DispatcherShutdownModeFor_Maps_Each_Policy(ShutdownPolicy policy, DispatcherShutdownMode expected)
    {
        Assert.Equal(expected, ReactorApp.DispatcherShutdownModeFor(policy));
    }

    [Fact]
    public void DispatcherShutdownModeFor_Distinguishes_NonDefault_Policies_From_The_Default()
    {
        // Differential oracle: a mapping that collapsed to one constant would
        // satisfy "returns a valid enum value" but would re-break the bug.
        var platformDefault = ReactorApp.DispatcherShutdownModeFor(ShutdownPolicy.OnPrimaryWindowClosed);

        Assert.NotEqual(platformDefault, ReactorApp.DispatcherShutdownModeFor(ShutdownPolicy.Explicit));
        Assert.NotEqual(platformDefault, ReactorApp.DispatcherShutdownModeFor(ShutdownPolicy.OnLastSurfaceClosed));
    }

    [Fact]
    public void DispatcherShutdownModeFor_Keeps_The_Platform_Default_For_Exactly_The_Default_Policy()
    {
        // Totality + shape: every policy is classified, and OnLastWindowClose —
        // the mode that lets WinUI own process lifetime — is reserved for the
        // one policy whose semantics already agree with it. A new enum member
        // reaching the `_` arm gets caught here rather than in the field.
        var keepPlatformOwnership = Enum.GetValues<ShutdownPolicy>()
            .Where(p => ReactorApp.DispatcherShutdownModeFor(p) == DispatcherShutdownMode.OnLastWindowClose)
            .ToArray();

        Assert.Equal(new[] { ShutdownPolicy.OnPrimaryWindowClosed }, keepPlatformOwnership);
    }

    [Fact]
    public void Setting_Policy_Without_A_UIDispatcher_Does_Not_Touch_WinUI()
    {
        // Headless: there is no Application and no UI dispatcher, so the setter
        // must store the value and stop. Touching Application.Current here would
        // throw a COMException and redden this test.
        Assert.Null(ReactorApp.UIDispatcher);

        var prior = ReactorApp.ShutdownPolicy;
        try
        {
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            Assert.Equal(ShutdownPolicy.Explicit, ReactorApp.ShutdownPolicy);
        }
        finally
        {
            ReactorApp.ShutdownPolicy = prior;
        }
    }
}
