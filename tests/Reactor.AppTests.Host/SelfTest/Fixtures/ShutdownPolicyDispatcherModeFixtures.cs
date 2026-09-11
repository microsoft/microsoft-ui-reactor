using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #1204 — Reactor must own this thread's event loop, so that
/// <c>ReactorApp.EvaluateShutdownPolicy</c> is the only thing that ends the
/// process.
/// </summary>
/// <remarks>
/// <para>WinUI quits the thread's <c>DispatcherQueue</c> event loop when the
/// last XAML window closes unless
/// <see cref="Application.DispatcherShutdownMode"/> is
/// <see cref="DispatcherShutdownMode.OnExplicitShutdown"/>, and
/// <c>Application.Start</c> resets that property to
/// <see cref="DispatcherShutdownMode.OnLastWindowClose"/>. Leaving it there was
/// the bug: the platform cannot see <c>ShutdownPolicy.Explicit</c>, a surviving
/// tray icon, or a window that opted out of the policy, so it ended processes
/// spec 036 §6.2 says should keep running.</para>
/// <para><c>ReactorApplication.OnLaunched</c> takes ownership once, before any
/// window exists. This fixture asserts the state that leaves behind on the live
/// host — a fixture cannot re-run <c>OnLaunched</c>, so the assertion is of the
/// standing effect rather than of a re-application. It is not vacuous: deleting
/// the <c>TakeOwnershipOfDispatcherLifetime</c> call reddens it, because
/// <c>Application.Start</c>'s reset is then what remains.</para>
/// <para>The policy-independence check is what guards the regression this PR's
/// review surfaced. An earlier revision derived the mode from the policy, which
/// (a) left the platform free to end an app whose last window was an auxiliary
/// docking tear-off under the default policy, and (b) opened a window in which
/// the policy and the mode could disagree. Ownership is now unconditional, so
/// flipping the policy must not move the property at all.</para>
/// <para>Process-lifetime consequences — surviving a close, zero-surface
/// startup, reopening afterwards, and leaving a non-Reactor <c>Application</c>
/// alone — need a real process and live in
/// <c>ShutdownPolicyProcessLifetimeTests</c>.</para>
/// </remarks>
internal static class ShutdownPolicyDispatcherModeFixtures
{
    private static void EnsureUIDispatcher()
    {
        if (ReactorApp.UIDispatcher is null)
            ReactorApp.UIDispatcher = DispatcherQueue.GetForCurrentThread();
    }

    internal class ShutdownPolicyOwnsDispatcherLifetime(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            // Ownership is gated on Reactor owning the Application, so a host
            // that failed this precondition would fail the real check below for
            // an unrelated reason. Name it separately.
            H.Check("ShutdownPolicy_Host_Owns_ReactorApplication",
                Application.Current is ReactorApplication);

            H.Check("ShutdownPolicy_Launch_Took_Dispatcher_Ownership",
                Application.Current.DispatcherShutdownMode == DispatcherShutdownMode.OnExplicitShutdown);

            var priorPolicy = ReactorApp.ShutdownPolicy;
            try
            {
                // Every policy, including the default, must leave the property
                // alone. The default one is the load-bearing case: deriving the
                // mode from it is exactly what let the platform end an app whose
                // last window was an auxiliary one (issue #647).
                foreach (var policy in Enum.GetValues<ShutdownPolicy>())
                {
                    ReactorApp.ShutdownPolicy = policy;
                    await Harness.Render();

                    H.Check($"ShutdownPolicy_{policy}_Leaves_Ownership_Intact",
                        Application.Current.DispatcherShutdownMode == DispatcherShutdownMode.OnExplicitShutdown);
                }
            }
            finally
            {
                ReactorApp.ShutdownPolicy = priorPolicy;
            }
        }
    }
}