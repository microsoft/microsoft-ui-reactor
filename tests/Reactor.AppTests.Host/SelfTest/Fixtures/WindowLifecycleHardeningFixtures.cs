using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 036/045 — multi-window teardown hardening regression fixtures for
/// issue #647. They lock in the three invariants the framework fix established:
/// <list type="bullet">
/// <item>An auxiliary window (e.g. a docking tear-off floating window) excluded
/// via <c>ExcludeFromShutdownPolicy</c> can never become <em>nor remain</em>
/// (via unregister re-election) the application's <c>PrimaryWindow</c>.</item>
/// <item><c>BackdropApplier</c> never writes <c>SystemBackdrop</c> on a window
/// whose native surface has been torn down — the process-wide closed-window
/// registry gates both <c>Apply</c> and <c>Reset</c>.</item>
/// <item><c>ReactorWindow.Close()</c> is idempotent: a redundant or
/// owner-cascade close performs the native close exactly once.</item>
/// </list>
/// </summary>
internal static class WindowLifecycleHardeningFixtures
{
    private static void EnsureUIDispatcher()
    {
        if (ReactorApp.UIDispatcher is null)
            ReactorApp.UIDispatcher = DispatcherQueue.GetForCurrentThread();
        // These fixtures intentionally close windows that may be the elected
        // PrimaryWindow; Explicit keeps OnPrimaryWindowClosed from exiting the
        // shared self-test host process mid-batch.
        ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
    }

    private sealed class StubComponent : Component
    {
        public override Element Render() => TextBlock("ok");
    }

    private static async Task<ReactorWindow> OpenAndSettle(WindowSpec spec)
    {
        var win = ReactorApp.OpenWindow(spec, () => new StubComponent());
        await win.Host.WaitForIdleAsync();
        await Harness.Render(50);
        return win;
    }

    private static async Task<ReactorWindow> OpenExcludedAndSettle(WindowSpec spec)
    {
        // Mirrors how a docking tear-off floating window registers: opened through
        // the core entry point with excludeFromShutdownPolicy: true so it opts out
        // of primary election. (DockFloatingWindow.cs)
        var win = ReactorApp.OpenWindowCore(
            spec,
            rootFactory: () => new StubComponent(),
            renderFunc: null,
            configure: null,
            excludeFromShutdownPolicy: true);
        await win.Host.WaitForIdleAsync();
        await Harness.Render(50);
        return win;
    }

    private static async Task CloseAndSettle(params ReactorWindow?[] windows)
    {
        foreach (var win in windows.Where(static w => w is not null))
        {
            try { win!.Close(); }
            catch (global::System.Exception ex)
                when (ex is global::System.InvalidOperationException
                    or global::System.Runtime.InteropServices.COMException)
            {
                global::System.Diagnostics.Debug.WriteLine($"[selftest] CloseAndSettle best-effort close failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
        await Task.Delay(80);
        await Harness.Render(30);
    }

    private static WindowSpec Spec(string title) => new()
    {
        Title = title,
        Width = 220,
        Height = 150,
        StartPosition = WindowStartPosition.Manual,
        ManualPosition = (120, 120),
    };

    /// <summary>
    /// H1 — an excluded auxiliary window can neither become nor remain the
    /// PrimaryWindow. Drives the unregister re-election path specifically (M1's
    /// regression guard): with [primary1, aux(excluded), primary2] registered,
    /// closing primary1 must re-elect primary2 (skipping aux, i.e. NOT next[0]),
    /// and closing primary2 must leave PrimaryWindow null — never the aux.
    /// </summary>
    internal class PrimaryElectionExcludesAuxiliary(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            // Clean slate so a closed window left primary by an earlier fixture
            // doesn't mask the election under test.
            ReactorApp.PrimaryWindow = null;

            ReactorWindow? primary1 = null, aux = null, primary2 = null;
            try
            {
                primary1 = await OpenAndSettle(Spec("Election Primary 1"));
                H.Check("Election_PrimaryElectedOnOpen",
                    ReferenceEquals(ReactorApp.PrimaryWindow, primary1));

                aux = await OpenExcludedAndSettle(Spec("Election Aux (excluded)"));
                H.Check("Election_AuxIsExcluded", aux.ExcludeFromShutdownPolicy);
                // Opening the excluded window must not displace the real primary.
                H.Check("Election_AuxDoesNotBecomePrimary",
                    ReferenceEquals(ReactorApp.PrimaryWindow, primary1));

                // Registered AFTER aux, so aux sits before primary2 in the window
                // array — re-election picking next[0] would (wrongly) pick aux.
                primary2 = await OpenAndSettle(Spec("Election Primary 2"));

                // Close the real primary: re-election must skip the excluded aux
                // and land on primary2, not the array head (aux).
                await CloseAndSettle(primary1);
                bool reelected = await Harness.WaitFor(
                    () => ReferenceEquals(ReactorApp.PrimaryWindow, primary2),
                    maxPasses: 25, perPassMs: 20);
                H.Check("Election_ReelectsNonExcludedNotAux", reelected);
                H.Check("Election_ExcludedNeverReelectedPrimary",
                    !ReferenceEquals(ReactorApp.PrimaryWindow, aux));
                primary1 = null;

                // Close the remaining real primary: only the excluded aux is left,
                // so PrimaryWindow must go null — never the aux.
                await CloseAndSettle(primary2);
                bool wentNull = await Harness.WaitFor(
                    () => ReactorApp.PrimaryWindow is null,
                    maxPasses: 25, perPassMs: 20);
                H.Check("Election_NullWhenOnlyExcludedRemains", wentNull);
                H.Check("Election_ExcludedNeverPromotedOnLastClose",
                    !ReferenceEquals(ReactorApp.PrimaryWindow, aux));
                primary2 = null;
            }
            finally
            {
                await CloseAndSettle(primary1, aux, primary2);
                ReactorApp.PrimaryWindow = null;
            }
        }
    }

    /// <summary>
    /// M2 — the BackdropApplier closed-window registry suppresses every
    /// SystemBackdrop write once a window is marked closed, on both Apply and
    /// Reset, and across a freshly constructed applier (proving it is the
    /// process-wide registry, not per-instance last-applied state, doing the
    /// skip — and not an exception swallow, since no setter is reached).
    /// </summary>
    internal class BackdropSkipsClosedWindow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            var window = new Microsoft.UI.Xaml.Window();
            try
            {
                var applier = new BackdropApplier(window);

                // Live window: Apply writes and reports a change.
                bool liveApplied = applier.Apply(BackdropChoice.Of(BackdropKind.Mica));
                H.Check("Backdrop_LiveApplyWrites",
                    liveApplied && window.SystemBackdrop is not null);
                var backdropWhileLive = window.SystemBackdrop;

                // Surface torn down — record it in the process-wide registry.
                BackdropApplier.MarkWindowClosed(window);

                // A genuinely different kind WOULD write, so a skip here can only be
                // the registry guard (it short-circuits before the no-change bail).
                bool appliedAfterClose = applier.Apply(BackdropChoice.Of(BackdropKind.DesktopAcrylic));
                H.Check("Backdrop_ApplySkippedAfterClose", !appliedAfterClose);
                H.Check("Backdrop_SurfaceUnchangedAfterClose",
                    ReferenceEquals(window.SystemBackdrop, backdropWhileLive));

                // A fresh applier (mirrors a later host on the same reused, already
                // closed window) is gated too — proves it's the registry, not the
                // first applier's instance state.
                var freshApplier = new BackdropApplier(window);
                bool freshApplied = freshApplier.Apply(BackdropChoice.Of(BackdropKind.Mica));
                H.Check("Backdrop_FreshApplierAlsoSkips", !freshApplied);

                // Reset (windowClosed:false) must still skip the SystemBackdrop=null
                // clear via the registry guard — that write is the one that AVs.
                freshApplier.Reset();
                H.Check("Backdrop_ResetSkipsClearAfterClose",
                    window.SystemBackdrop is not null);
            }
            finally
            {
                try { window.Close(); }
                catch (global::System.Exception ex)
                    when (ex is global::System.InvalidOperationException
                        or global::System.Runtime.InteropServices.COMException)
                {
                    global::System.Diagnostics.Debug.WriteLine($"[selftest] BackdropSkipsClosedWindow cleanup close failed: {ex.GetType().Name}: {ex.Message}");
                }
                await Harness.Render(20);
            }
        }
    }

    /// <summary>
    /// M3 — ReactorWindow.Close() is idempotent. Two converging close calls on
    /// one window fire the native Window.Closed exactly once; a second native
    /// close would re-enter teardown and AV (#647). The process stays healthy:
    /// a window opened afterward mounts content normally.
    /// </summary>
    internal class NativeCloseIsIdempotent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            ReactorWindow? win2 = null;
            try
            {
                var win = await OpenAndSettle(Spec("CloseOnce Target"));

                int closedCount = 0;
                win.NativeWindow.Closed += (_, _) => closedCount++;

                // Two converging programmatic close paths against the same window.
                // The second must be a guarded no-op.
                win.Close();
                win.Close();

                await Harness.WaitFor(() => closedCount >= 1, maxPasses: 25, perPassMs: 20);
                await Task.Delay(60);
                H.Check("CloseOnce_NativeClosedFiredExactlyOnce", closedCount == 1);

                // Process is still healthy after the double close: a new window
                // opens and mounts its content.
                win2 = await OpenAndSettle(Spec("CloseOnce HealthCheck"));
                H.Check("CloseOnce_SubsequentWindowOpens", win2.Host is not null);
                H.Check("CloseOnce_SubsequentWindowRendersContent",
                    win2.NativeWindow.Content is not null);
            }
            finally
            {
                await CloseAndSettle(win2);
                ReactorApp.PrimaryWindow = null;
            }
        }
    }
}
