using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #1204 — <c>ReactorApp.TakeOwnershipOfDispatcherLifetime</c> must move
/// this thread's <see cref="Application.DispatcherShutdownMode"/> to
/// <see cref="DispatcherShutdownMode.OnExplicitShutdown"/>, and must do it
/// regardless of the current <see cref="ShutdownPolicy"/>.
/// </summary>
/// <remarks>
/// <para>WinUI quits the thread's <c>DispatcherQueue</c> event loop when the
/// last XAML window closes unless that property is
/// <see cref="DispatcherShutdownMode.OnExplicitShutdown"/>, and
/// <c>Application.Start</c> resets it to
/// <see cref="DispatcherShutdownMode.OnLastWindowClose"/>. Leaving it there was
/// the bug: the platform cannot see <c>ShutdownPolicy.Explicit</c>, a surviving
/// tray icon, or a window that opted out of the policy, so it ended processes
/// that spec 036 §6.2 says should keep running.</para>
/// <para>The method is driven <b>directly</b> rather than by launching. This
/// host constructs <see cref="ReactorApplication"/> itself instead of going
/// through <c>ReactorApp.Run</c>, and that launch shape deliberately does not
/// take ownership — the host owns its own windows — so asserting the ambient
/// mode here would assert the opposite of what the product does. Calling the
/// method is also what makes the check non-vacuous: the mode is seeded to
/// <see cref="DispatcherShutdownMode.OnLastWindowClose"/> first, so a no-op
/// implementation is visible rather than masked by a value that was already
/// correct.</para>
/// <para>The policy-independence arm guards the regression this PR's review
/// surfaced. An earlier revision derived the mode from the policy, which left
/// the platform free to end an app whose last window was an auxiliary docking
/// tear-off under the default policy (issue #647), and opened a window in which
/// the policy and the mode could disagree.</para>
/// <para>Whether the process then actually survives — and whether a non-Reactor
/// <see cref="Application"/> is left alone — needs a real process and lives in
/// <c>ShutdownPolicyProcessLifetimeTests</c>.</para>
/// </remarks>
internal static class ShutdownPolicyDispatcherModeFixtures
{
    internal class ShutdownPolicyOwnsDispatcherLifetime(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            if (ReactorApp.UIDispatcher is null)
                ReactorApp.UIDispatcher = DispatcherQueue.GetForCurrentThread();

            // Ownership is gated on Reactor owning the Application, so a host
            // failing this precondition would fail every check below for an
            // unrelated reason. Name it separately.
            H.Check("ShutdownPolicy_Host_Owns_ReactorApplication",
                Application.Current is ReactorApplication);

            var priorPolicy = ReactorApp.ShutdownPolicy;
            var priorMode = Application.Current.DispatcherShutdownMode;
            try
            {
                // 1. Ownership. Seeded to the opposite value first, so a no-op
                //    implementation is visible rather than masked by a mode that
                //    was already correct.
                Application.Current.DispatcherShutdownMode = DispatcherShutdownMode.OnLastWindowClose;
                H.Check("ShutdownPolicy_Seed_Took",
                    Application.Current.DispatcherShutdownMode == DispatcherShutdownMode.OnLastWindowClose);

                ReactorApp.TakeOwnershipOfDispatcherLifetime();
                await Harness.Render();

                H.Check("ShutdownPolicy_Takes_Ownership",
                    Application.Current.DispatcherShutdownMode == DispatcherShutdownMode.OnExplicitShutdown);

                // 2. Policy independence, checked WITHOUT re-seeding: a setter
                //    that wrote to the platform would show up as a mode that
                //    moved off OnExplicitShutdown. Re-seeding between arms would
                //    overwrite exactly that evidence, which is what made an
                //    earlier version of this fixture unable to see it.
                foreach (var policy in Enum.GetValues<ShutdownPolicy>())
                {
                    ReactorApp.ShutdownPolicy = policy;
                    await Harness.Render();

                    H.Check($"ShutdownPolicy_{policy}_Does_Not_Move_The_Mode",
                        Application.Current.DispatcherShutdownMode == DispatcherShutdownMode.OnExplicitShutdown);
                }
            }
            finally
            {
                ReactorApp.ShutdownPolicy = priorPolicy;
                Application.Current.DispatcherShutdownMode = priorMode;
            }
        }
    }
}