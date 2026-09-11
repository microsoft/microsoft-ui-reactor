using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #1204 — <see cref="ReactorApp.ShutdownPolicy"/> has to reach the
/// platform, not just Reactor's own bookkeeping.
/// </summary>
/// <remarks>
/// <para>WinUI quits the thread's <c>DispatcherQueue</c> event loop when the last
/// XAML window closes unless <see cref="Application.DispatcherShutdownMode"/> is
/// <see cref="DispatcherShutdownMode.OnExplicitShutdown"/>, and
/// <c>Application.Start</c> resets that property to
/// <see cref="DispatcherShutdownMode.OnLastWindowClose"/>. So both non-default
/// policies are inert unless Reactor writes the property — which is exactly the
/// bug: the process exited on last-window-close as if the policy were
/// <see cref="ShutdownPolicy.OnPrimaryWindowClosed"/>.</para>
/// <para><b>Every arm forces the property to the opposite value first.</b> Without
/// that, the check is a tautology: the windowing fixture families pin
/// <see cref="ShutdownPolicy.Explicit"/> in their <c>EnsureUIDispatcher</c>
/// helpers, so by the time this runs the property may already hold the value the
/// arm expects, and a completely unwired build would still read green. The forced
/// write doubles as a positive control — <c>_Precondition</c> proves the property
/// is readable and writable on this thread, so a later no-change reading is a
/// measurement rather than a broken probe.</para>
/// <para>Expected modes are spelled out literally rather than obtained from
/// <c>ReactorApp.DispatcherShutdownModeFor</c>: an oracle that asks the code under
/// test what it should have done cannot catch a wrong mapping.</para>
/// <para>This tier can observe the property but not the loop. That the process
/// genuinely survives last-window-close is covered by
/// <c>ShutdownPolicyProcessLifetimeTests</c> in <c>Reactor.SelfTests</c>, which
/// drives a real <c>--shutdown-policy-probe</c> host to exit.</para>
/// </remarks>
internal static class ShutdownPolicyDispatcherModeFixtures
{
    private static void EnsureUIDispatcher()
    {
        // The setter only projects onto the platform when it can resolve a UI
        // dispatcher. OnLaunched captures one for this host, but the windowing
        // fixtures guard the same way and so does this.
        if (ReactorApp.UIDispatcher is null)
            ReactorApp.UIDispatcher = DispatcherQueue.GetForCurrentThread();
    }

    private static DispatcherShutdownMode Opposite(DispatcherShutdownMode mode) =>
        mode == DispatcherShutdownMode.OnExplicitShutdown
            ? DispatcherShutdownMode.OnLastWindowClose
            : DispatcherShutdownMode.OnExplicitShutdown;

    /// <summary>
    /// Drains one full turn of the UI dispatcher so a <c>TryEnqueue</c> posted
    /// from another thread has demonstrably run before we read the property.
    /// </summary>
    private static Task DrainDispatcherAsync()
    {
        var dispatcher = ReactorApp.UIDispatcher!;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() => tcs.TrySetResult()))
            tcs.TrySetResult();
        return tcs.Task;
    }

    internal class ShutdownPolicyProjectsToDispatcherMode(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            // The projection is gated on Reactor owning the Application, so a
            // host that failed this precondition would make every check below
            // fail for an unrelated reason. Name it explicitly.
            H.Check("ShutdownPolicy_Host_Owns_ReactorApplication",
                Application.Current is ReactorApplication);

            var priorPolicy = ReactorApp.ShutdownPolicy;
            var priorMode = Application.Current.DispatcherShutdownMode;
            try
            {
                (ShutdownPolicy Policy, DispatcherShutdownMode Expected, string Label)[] arms =
                [
                    (ShutdownPolicy.OnPrimaryWindowClosed, DispatcherShutdownMode.OnLastWindowClose, "OnPrimaryWindowClosed"),
                    (ShutdownPolicy.OnLastSurfaceClosed, DispatcherShutdownMode.OnExplicitShutdown, "OnLastSurfaceClosed"),
                    (ShutdownPolicy.Explicit, DispatcherShutdownMode.OnExplicitShutdown, "Explicit"),
                ];

                foreach (var (policy, expected, label) in arms)
                {
                    var seed = Opposite(expected);
                    Application.Current.DispatcherShutdownMode = seed;
                    H.Check($"ShutdownPolicy_{label}_Precondition",
                        Application.Current.DispatcherShutdownMode == seed);

                    ReactorApp.ShutdownPolicy = policy;
                    await Harness.Render();

                    H.Check($"ShutdownPolicy_{label}_Projects",
                        Application.Current.DispatcherShutdownMode == expected);
                }
            }
            finally
            {
                ReactorApp.ShutdownPolicy = priorPolicy;
                Application.Current.DispatcherShutdownMode = priorMode;
            }
        }
    }

    internal class ShutdownPolicyOffThreadSetMarshals(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            var priorPolicy = ReactorApp.ShutdownPolicy;
            var priorMode = Application.Current.DispatcherShutdownMode;
            try
            {
                Application.Current.DispatcherShutdownMode = DispatcherShutdownMode.OnLastWindowClose;
                H.Check("ShutdownPolicy_OffThread_Precondition",
                    Application.Current.DispatcherShutdownMode == DispatcherShutdownMode.OnLastWindowClose);

                // DispatcherShutdownMode is a per-thread property, so a policy set
                // from a background thread (a sync worker, a tray command handler
                // posted off-thread) has to be marshalled rather than written where
                // it was set.
                bool hadThreadAccess = true;
                await Task.Run(() =>
                {
                    hadThreadAccess = ReactorApp.UIDispatcher!.HasThreadAccess;
                    ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
                });

                // Guards the arm itself: if this ran on the UI thread after all,
                // it would exercise the inline branch and prove nothing about
                // marshalling.
                H.Check("ShutdownPolicy_OffThread_Really_Off_Thread", !hadThreadAccess);

                await DrainDispatcherAsync();

                H.Check("ShutdownPolicy_OffThread_Projects",
                    Application.Current.DispatcherShutdownMode == DispatcherShutdownMode.OnExplicitShutdown);
            }
            finally
            {
                ReactorApp.ShutdownPolicy = priorPolicy;
                Application.Current.DispatcherShutdownMode = priorMode;
            }
        }
    }
}
