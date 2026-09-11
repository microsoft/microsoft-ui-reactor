using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host;

/// <summary>
/// Issue #1204 — end-to-end probe for whether
/// <see cref="ReactorApp.ShutdownPolicy"/> actually governs process lifetime.
/// </summary>
/// <remarks>
/// <para>The selftest tier can assert that Reactor writes
/// <c>Application.DispatcherShutdownMode</c>, but it cannot assert the thing the
/// bug was about: that the process is <i>still running</i> after the last window
/// closes. Only a real process can answer that, so this mode opens one window,
/// closes it, and reports what happened via the exit code.</para>
/// <para>Exit codes are the oracle:</para>
/// <list type="bullet">
/// <item><description><see cref="AliveExitCode"/> + <see cref="AliveMarker"/> — the
/// event loop outlived its last window and <see cref="ReactorApp.Exit(int)"/> was
/// the thing that ended the process.</description></item>
/// <item><description><see cref="LoopExitedExitCode"/> + <see cref="LoopExitedMarker"/>
/// — <c>Application.Start</c> returned on its own, i.e. WinUI quit the loop on
/// last-window-close.</description></item>
/// </list>
/// <para>Both outcomes are reachable by design: run this with
/// <c>OnPrimaryWindowClosed</c> and the loop-exited branch is the <i>correct</i>
/// result. That arm is the positive control — it proves the probe can distinguish
/// the two states, so an <c>Explicit</c> run reporting "alive" is a measurement
/// rather than a probe that can only ever print one thing.</para>
/// <para><c>OnLastSurfaceClosed</c> needs <see cref="TrayFlag"/> to say anything:
/// without a surviving surface that policy is <i>supposed</i> to end the process,
/// so a tray-less run exits for the right reason and would look identical to the
/// bug.</para>
/// <para>No UI input is involved — the window closes itself from the dispatcher —
/// so unlike the <c>winapp ui</c> tier this runs without an interactive desktop.
/// See the comment on the close call for why it is an app-initiated close and how
/// the user-initiated one was verified.</para>
/// </remarks>
internal static class ShutdownPolicyProbe
{
    internal const string Flag = "--shutdown-policy-probe";

    /// <summary>
    /// Opens a tray icon alongside the window. Required to exercise
    /// <see cref="ShutdownPolicy.OnLastSurfaceClosed"/> meaningfully: with no
    /// tray icon, "last window closed" and "last surface closed" coincide, so
    /// that policy correctly ends the process and the run is indistinguishable
    /// from the bug.
    /// </summary>
    internal const string TrayFlag = "--with-tray";

    /// <summary>
    /// Opens no window at all, exercising the zero-surface startup branch in
    /// <c>OnLaunched</c> (spec 036 §6.2 allows a startup callback to open zero
    /// surfaces). That branch decides process lifetime before any window has
    /// ever existed, so it is reachable only without <see cref="TrayFlag"/>'s
    /// sibling window.
    /// </summary>
    internal const string NoWindowFlag = "--no-window";

    /// <summary>
    /// After the process has outlived its last window, open another one. The
    /// tray-app shape in spec 036 §13.6 depends on this: the tray icon reopens
    /// the window on demand, so surviving the close is only half the promise.
    /// </summary>
    internal const string ReopenFlag = "--reopen";

    /// <summary>
    /// Opens the window with <c>ExcludeFromShutdownPolicy</c>, the way a docking
    /// tear-off does (issue #647), so no <c>PrimaryWindow</c> is ever elected.
    /// Closing it under the DEFAULT policy must leave the process running — the
    /// guarantee the windows guide already made and the platform used to break,
    /// because <c>OnLastWindowClose</c> unwound the loop regardless of what
    /// <c>EvaluateShutdownPolicy</c> decided.
    /// </summary>
    internal const string ExcludedWindowFlag = "--excluded-window";

    /// <summary>
    /// After the window has closed and the process has been confirmed alive,
    /// close the tray icon too and look again. Covers the last-tray-icon exit
    /// path, which <c>UnregisterTrayIcon</c> routes through
    /// <c>EvaluateShutdownPolicy</c> rather than deciding for itself.
    /// </summary>
    internal const string CloseTrayFlag = "--close-tray";

    /// <summary>
    /// Launch through the legacy <c>ReactorApp.Run&lt;TRoot&gt;</c> bridge rather
    /// than the <c>Run(startup)</c> callback. The two entry points reach
    /// <c>OnLaunched</c> by different branches and take dispatcher ownership at
    /// different call sites, so an arm that only ever exercises one of them
    /// leaves the other unobserved.
    /// </summary>
    internal const string LegacyRunFlag = "--legacy-run";

    internal const int AliveExitCode = 42;
    internal const int LoopExitedExitCode = 1;
    internal const int UsageExitCode = 64;
    internal const int ReopenFailedExitCode = 45;

    internal const string AliveMarker = "STILL-ALIVE";
    internal const string LoopExitedMarker = "LOOP-EXITED";
    internal const string ClosingMarker = "CLOSING";
    internal const string ReopenedMarker = "REOPENED";
    internal const string TrayClosedMarker = "TRAY-CLOSED";

    /// <summary>Settle time before the window is closed.</summary>
    private const int SettleMs = 400;

    /// <summary>
    /// How long after the close we wait before declaring the process alive.
    /// WinUI posts its quit message during the close itself — the reported
    /// symptom was a process exit ~90 ms later — so this only has to be
    /// comfortably longer than that, not long enough to "wait out" a race.
    /// </summary>
    private const int AliveCheckMs = 1000;

    public static int Run(string[] args)
    {
        var index = Array.IndexOf(args, Flag);
        if (index < 0 || index + 1 >= args.Length ||
            !Enum.TryParse<ShutdownPolicy>(args[index + 1], ignoreCase: true, out var policy))
        {
            Console.Error.WriteLine(
                $"usage: {Flag} <{string.Join('|', Enum.GetNames<ShutdownPolicy>())}> " +
                $"[{TrayFlag}] [{NoWindowFlag}] [{ReopenFlag}] [{ExcludedWindowFlag}] [{CloseTrayFlag}] [{LegacyRunFlag}]");
            return UsageExitCode;
        }

        var withTray = args.Contains(TrayFlag);
        var noWindow = args.Contains(NoWindowFlag);
        var reopen = args.Contains(ReopenFlag);
        var excluded = args.Contains(ExcludedWindowFlag);
        var closeTray = args.Contains(CloseTrayFlag);
        var legacyRun = args.Contains(LegacyRunFlag);

        ReactorApp.ShutdownPolicy = policy;
        Console.WriteLine(
            $"POLICY {policy} tray={withTray} noWindow={noWindow} reopen={reopen} excluded={excluded} legacy={legacyRun}");

        if (legacyRun)
        {
            // Run<TRoot> has no startup callback, so the pre-mount `configure`
            // hook is where the close gets scheduled. It runs on the UI thread
            // before RegisterWindow, hence the timer: by the time it ticks,
            // ReactorApp.PrimaryWindow is set.
            ReactorApp.Run<ProbeContent>(
                "Shutdown Policy Probe (legacy)",
                width: 320,
                height: 200,
                configure: _ =>
                {
                    var dq = ReactorApp.UIDispatcher!;
                    var timer = dq.CreateTimer();
                    timer.Interval = TimeSpan.FromMilliseconds(SettleMs);
                    timer.IsRepeating = false;
                    timer.Tick += (_, _) =>
                    {
                        Console.WriteLine(
                            $"OPENED primaryElected={ReactorApp.PrimaryWindow is not null} windows={ReactorApp.Windows.Count}");
                        Console.WriteLine(ClosingMarker);
                        Console.Out.Flush();
                        ReactorApp.PrimaryWindow?.Close();
                        StartAliveTimer(dq, reopen, closeTray);
                    };
                    timer.Start();
                });

            Console.WriteLine(LoopExitedMarker);
            Console.Out.Flush();
            return LoopExitedExitCode;
        }

        ReactorApp.Run(_ =>
        {
            if (withTray)
            {
                // 16x16 opaque magenta, built inline so the probe needs no asset.
                var pixels = new byte[16 * 16 * 4];
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255;
                    pixels[i + 2] = 255;
                    pixels[i + 3] = 255;
                }

                ReactorApp.OpenTrayIcon(new TrayIconSpec(
                    Icon: WindowIcon.FromRgba(pixels, 16, 16),
                    Tooltip: "Shutdown Policy Probe",
                    Key: WindowKey.Of("shutdown-policy-probe")));
            }

            var dispatcher = ReactorApp.UIDispatcher!;

            // DispatcherQueueTimer rather than Task.Delay: a timer does not keep
            // the loop alive, so in the broken case it simply never ticks and the
            // process falls through to the LOOP-EXITED branch below.
            if (noWindow)
            {
                // Nothing to close — the zero-surface startup decision has
                // already been made by the time OnLaunched returns, so go
                // straight to asking whether the loop is still running.
                StartAliveTimer(dispatcher, reopen, closeTray);
                return;
            }

            var window = OpenProbeWindow("Shutdown Policy Probe", excluded);
            Console.WriteLine(
                $"OPENED primaryElected={ReactorApp.PrimaryWindow is not null} windows={ReactorApp.Windows.Count}");
            Console.Out.Flush();

            var closeTimer = dispatcher.CreateTimer();
            closeTimer.Interval = TimeSpan.FromMilliseconds(SettleMs);
            closeTimer.IsRepeating = false;
            closeTimer.Tick += (_, _) =>
            {
                Console.WriteLine(ClosingMarker);
                Console.Out.Flush();

                // App-initiated close. The issue describes a user close (Alt+F4 /
                // the caption X, both of which post WM_CLOSE), and posting that
                // message here would be the more faithful shape — but neither
                // variant survives contact with this host: posting it inline
                // re-enters native teardown from inside a dispatcher callback and
                // faults the XAML runtime (0xC000027B), and posting it from a
                // pool thread never reaches the window at all, leaving it open so
                // the process trivially "survives". The second failure is the
                // dangerous one: it turns every arm green, control included, for
                // a reason that has nothing to do with the fix.
                //
                // So this asserts the path it can assert reliably. The
                // user-initiated path was verified out-of-band against a
                // standalone unpackaged app driven by a cross-process WM_CLOSE,
                // with the fix reverted as the control; both close paths behaved
                // identically. Revisit only with evidence that they can diverge.
                window.Close();

                StartAliveTimer(dispatcher, reopen, closeTray);
            };
            closeTimer.Start();
        });

        // ReactorApp.Run blocks until Application.Start returns, and it only
        // returns when the event loop unwinds on its own — the alive path exits
        // the process from inside the dispatcher and never reaches this line.
        Console.WriteLine(LoopExitedMarker);
        Console.Out.Flush();
        return LoopExitedExitCode;
    }

    private static ReactorWindow OpenProbeWindow(string title, bool excludeFromShutdownPolicy = false) =>
        ReactorApp.OpenWindowCore(
            new WindowSpec { Title = title, Width = 320, Height = 200 },
            rootFactory: () => new ProbeContent(),
            renderFunc: null,
            configure: null,
            excludeFromShutdownPolicy: excludeFromShutdownPolicy);

    private static void StartAliveTimer(DispatcherQueue dispatcher, bool reopen, bool closeTray = false)
    {
        var aliveTimer = dispatcher.CreateTimer();
        aliveTimer.Interval = TimeSpan.FromMilliseconds(AliveCheckMs);
        aliveTimer.IsRepeating = false;
        aliveTimer.Tick += (_, _) =>
        {
            Console.WriteLine(
                $"{AliveMarker} windows={ReactorApp.Windows.Count} trayIcons={ReactorApp.TrayIcons.Count}");
            Console.Out.Flush();

            if (closeTray)
            {
                // Close the remaining surface and look again instead of exiting,
                // so the caller can tell "the last tray icon ended the process"
                // from "it did not".
                foreach (var tray in ReactorApp.TrayIcons.ToArray())
                {
                    // Narrow rather than generic: a shell/COM fault while
                    // removing the notify-icon is survivable and the trayIcons
                    // count below reports what actually happened, but anything
                    // else is a probe bug and should crash rather than be
                    // mistaken for a clean close.
                    try { tray.Close(); }
                    catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or global::System.Runtime.InteropServices.COMException)
                    {
                        Console.WriteLine($"TRAY-CLOSE-FAILED {ex.GetType().Name}: {ex.Message}");
                    }
                }
                Console.WriteLine($"{TrayClosedMarker} trayIcons={ReactorApp.TrayIcons.Count}");
                Console.Out.Flush();
                StartAliveTimer(dispatcher, reopen);
                return;
            }

            if (reopen)
            {
                // Surviving the close is only useful if the app can put UI back
                // on screen afterwards. WinUI documents that new XAML windows can
                // still be shown under OnExplicitShutdown; this asserts it for a
                // Reactor window created after the process already outlived its
                // last one.
                var before = ReactorApp.Windows.Count;
                var revived = OpenProbeWindow("Shutdown Policy Probe (reopened)");
                var after = ReactorApp.Windows.Count;
                Console.WriteLine($"{ReopenedMarker} before={before} after={after} id={revived.Id}");
                Console.Out.Flush();

                if (after != before + 1)
                {
                    ReactorApp.Exit(ReopenFailedExitCode);
                    return;
                }
            }

            ReactorApp.Exit(AliveExitCode);
        };
        aliveTimer.Start();
    }

    private sealed class ProbeContent : Component
    {
        public override Element Render() => TextBlock("shutdown policy probe").Padding(24);
    }
}
