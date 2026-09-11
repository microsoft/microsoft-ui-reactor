using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.SelfTests;

/// <summary>
/// Issue #1204 — <see cref="Microsoft.UI.Reactor.ShutdownPolicy"/> has to govern
/// process lifetime, not just Reactor's own bookkeeping.
/// </summary>
/// <remarks>
/// <para>WinUI quits the thread's event loop when the last XAML window closes
/// unless <c>Application.DispatcherShutdownMode</c> is <c>OnExplicitShutdown</c>,
/// and <c>Application.Start</c> resets that property. The reported symptom was an
/// app with <c>ShutdownPolicy.Explicit</c> and a live tray icon exiting with code
/// 0 the moment its window closed.</para>
/// <para>No other tier can see this. Headless tests cannot construct an
/// <c>Application</c>; the in-process selftest fixtures can read the property but
/// cannot observe the loop, because the selftest Host exits by terminating itself.
/// Only a separate process can answer "is it still running?", so each test drives
/// the Host's <c>--shutdown-policy-probe</c> mode, which opens a window, closes it
/// from the dispatcher, and encodes the answer in its exit code.</para>
/// <para><see cref="OnPrimaryWindowClosed_Still_Exits_When_Its_Window_Closes"/> is
/// the positive control: it asserts the opposite outcome through the same probe,
/// so a "still alive" result elsewhere is a measurement rather than a probe that
/// can only ever report one thing. It also guards the default policy against
/// regressing into a process that never exits.</para>
/// <para>The probe needs no UI input — the window closes itself — so unlike the
/// <c>winapp ui</c> tier this runs without an interactive desktop.</para>
/// </remarks>
[TestClass]
public class ShutdownPolicyProcessLifetimeTests
{
    // Mirrors ShutdownPolicyProbe in Reactor.AppTests.Host. SelfTests references
    // the Host for build ordering only (ReferenceOutputAssembly="false"), so the
    // contract is duplicated rather than shared. Asserting the marker text as
    // well as the exit code means a drift in either shows up as a failure here
    // instead of a test that quietly stops proving anything.
    private const string ProbeFlag = "--shutdown-policy-probe";
    private const string TrayFlag = "--with-tray";
    private const string NoWindowFlag = "--no-window";
    private const string ReopenFlag = "--reopen";
    private const string ExcludedWindowFlag = "--excluded-window";
    private const string CloseTrayFlag = "--close-tray";
    private const string LegacyRunFlag = "--legacy-run";
    private const string ClosingMarker = "CLOSING";
    private const string GateProbeFlag = "--shutdown-policy-gate-probe";
    private const int AliveExitCode = 42;
    private const int LoopExitedExitCode = 1;
    private const int GateHeldExitCode = 43;
    private const string AliveMarker = "STILL-ALIVE";
    private const string LoopExitedMarker = "LOOP-EXITED";
    private const string ReopenedMarker = "REOPENED";
    private const string GateHeldMarker = "GATE-HELD";

    /// <summary>
    /// The probe itself spends ~1.4 s; the rest is WinUI process startup. Sized
    /// as "could not legitimately take this long" rather than "usually takes this
    /// long", matching <see cref="SelfTestBatch"/>'s backstop reasoning.
    /// </summary>
    private const int ProbeTimeoutMs = 60_000;

    private static (int ExitCode, string Stdout, string Stderr) RunProbe(string policy, params string[] flags)
    {
        var exe = HostProcess.FindHostExe();
        var args = $"{ProbeFlag} {policy}" + string.Concat(flags.Select(f => " " + f));
        var result = RunHost(exe, args);

        // Unless the arm is deliberately zero-surface, every one of these is a
        // last-window-close probe, and that only means something if a window was
        // opened and then closed. Without this, deleting OpenProbeWindow or the
        // close timer would silently route several arms through the zero-surface
        // startup path, where they would still report STILL-ALIVE and pass.
        //
        // The zero-surface arms need the mirror image. They assert windows=0,
        // which is also what a window that opened and closed leaves behind, so
        // bypassing the zero-surface branch would leave them green too. Requiring
        // the markers to be ABSENT is what pins them to the startup path.
        var detail = Detail(policy, args, result);
        if (!flags.Contains(NoWindowFlag))
        {
            StringAssert.Contains(result.Stdout, "OPENED",
                $"The probe never opened a window, so this is not a last-window-close run.\n{detail}");
            StringAssert.Contains(result.Stdout, ClosingMarker,
                $"The probe never reached the close step, so this is not a last-window-close run.\n{detail}");
        }
        else
        {
            StringAssert.DoesNotMatch(result.Stdout, new Regex("OPENED"),
                $"A window was opened, so this run exercised last-window-close rather than zero-surface startup.\n{detail}");
            StringAssert.DoesNotMatch(result.Stdout, new Regex(ClosingMarker),
                $"A window was closed, so this run exercised last-window-close rather than zero-surface startup.\n{detail}");
        }

        return result;
    }

    private static (int ExitCode, string Stdout, string Stderr) RunHost(string exe, string args)
    {
        var (stdout, stderr, exitCode, timedOut) = HostProcess.Run(exe, args, ProbeTimeoutMs);

        Assert.IsFalse(timedOut,
            $"Probe did not exit within {ProbeTimeoutMs}ms.\nHost: {exe} {args}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");

        return (exitCode, stdout, stderr);
    }

    private static string Detail(string policy, string args, (int ExitCode, string Stdout, string Stderr) result) =>
        $"policy={policy} args={args}\nexit={result.ExitCode}\n--- stdout ---\n{result.Stdout}\n--- stderr ---\n{result.Stderr}";

    [TestMethod]
    public void Explicit_Keeps_Process_Alive_After_Last_Window_Closes()
    {
        var result = RunProbe("Explicit");
        var detail = Detail("Explicit", ProbeFlag, result);

        StringAssert.Contains(result.Stdout, AliveMarker,
            $"The process exited when its last window closed — Reactor never took ownership of the event loop.\n{detail}");
        StringAssert.Contains(result.Stdout, "windows=0",
            $"The window was still open, so the process staying alive proves nothing about last-window-close.\n{detail}");
        Assert.AreEqual(AliveExitCode, result.ExitCode,
            $"ReactorApp.Exit() should have been the only thing that ended the process.\n{detail}");
    }

    [TestMethod]
    public void OnLastSurfaceClosed_Keeps_Process_Alive_While_A_Tray_Icon_Remains()
    {
        // The tray icon is what makes this arm meaningful: with no surviving
        // surface, OnLastSurfaceClosed is *supposed* to end the process, and a
        // tray-less run would exit for the right reason while looking exactly
        // like the bug.
        var result = RunProbe("OnLastSurfaceClosed", TrayFlag);
        var detail = Detail("OnLastSurfaceClosed", $"{ProbeFlag} {TrayFlag}", result);

        StringAssert.Contains(result.Stdout, AliveMarker,
            $"A live tray icon should have kept the process running after the last window closed.\n{detail}");
        StringAssert.Contains(result.Stdout, "windows=0 trayIcons=1",
            $"This run did not reach 'last window closed, tray icon surviving', so a live process proves nothing.\n{detail}");
        Assert.AreEqual(AliveExitCode, result.ExitCode, detail);
    }

    [TestMethod]
    public void OnLastSurfaceClosed_Exits_When_The_Last_Window_Closes_With_No_Tray_Icon()
    {
        // Control for the arm above: same policy, no surviving surface, opposite
        // outcome. Without it, a regression that made OnLastSurfaceClosed never
        // exit would leave every other process test green — "stays alive" is the
        // assertion everywhere else.
        var result = RunProbe("OnLastSurfaceClosed");
        var detail = Detail("OnLastSurfaceClosed", ProbeFlag, result);

        StringAssert.Contains(result.Stdout, LoopExitedMarker,
            $"With no window and no tray icon left, this policy must end the process.\n{detail}");
        Assert.AreEqual(LoopExitedExitCode, result.ExitCode, detail);
    }

    [TestMethod]
    public void OnLastSurfaceClosed_Exits_When_The_Last_Tray_Icon_Closes_Too()
    {
        // Covers the other half of "last surface": the window goes first, the
        // tray icon keeps the process alive, and closing the tray icon is what
        // finally ends it. UnregisterTrayIcon routes that through
        // EvaluateShutdownPolicy rather than deciding for itself, so this is the
        // arm that would catch that path diverging from the evaluator.
        var result = RunProbe("OnLastSurfaceClosed", TrayFlag, CloseTrayFlag);
        var detail = Detail("OnLastSurfaceClosed", $"{ProbeFlag} {TrayFlag} {CloseTrayFlag}", result);

        StringAssert.Contains(result.Stdout, "windows=0 trayIcons=1",
            $"The run never reached 'window closed, tray icon surviving'.\n{detail}");
        StringAssert.Contains(result.Stdout, "TRAY-CLOSED trayIcons=0",
            $"The tray icon never closed, so this run says nothing about the last-surface exit.\n{detail}");
        StringAssert.Contains(result.Stdout, LoopExitedMarker,
            $"Closing the last surface under OnLastSurfaceClosed must end the process.\n{detail}");
        Assert.AreEqual(LoopExitedExitCode, result.ExitCode, detail);
    }

    [TestMethod]
    public void Explicit_Survives_Even_When_Every_Surface_Is_Gone()
    {
        // Control for the arm above: identical sequence, opposite policy,
        // opposite outcome. Explicit means zero surfaces is a valid running
        // state, so only ReactorApp.Exit ends it.
        var result = RunProbe("Explicit", TrayFlag, CloseTrayFlag);
        var detail = Detail("Explicit", $"{ProbeFlag} {TrayFlag} {CloseTrayFlag}", result);

        StringAssert.Contains(result.Stdout, "TRAY-CLOSED trayIcons=0",
            $"The tray icon never closed, so this run never reached the zero-surface state.\n{detail}");
        StringAssert.Contains(result.Stdout, AliveMarker,
            $"Explicit must keep the process running with no surfaces at all.\n{detail}");
        Assert.AreEqual(AliveExitCode, result.ExitCode, detail);
    }

    [TestMethod]
    public void OnPrimaryWindowClosed_Still_Exits_When_Its_Window_Closes()
    {
        var result = RunProbe("OnPrimaryWindowClosed");
        var detail = Detail("OnPrimaryWindowClosed", ProbeFlag, result);

        StringAssert.Contains(result.Stdout, LoopExitedMarker,
            $"The default policy must still end the process when the primary window closes.\n{detail}");
        Assert.AreEqual(LoopExitedExitCode, result.ExitCode, detail);
    }

    // ── zero-surface startup (spec 036 §6.2) ───────────────────────────────
    //
    // A startup callback is allowed to open nothing at all. That decision is
    // made in OnLaunched before any window has existed, so it is a different
    // branch from the close-driven one above and needs its own arms — including
    // the one that must still exit, which doubles as this pair's control.

    [TestMethod]
    public void Explicit_Keeps_Process_Alive_When_Startup_Opens_No_Surface()
    {
        var result = RunProbe("Explicit", NoWindowFlag);
        var detail = Detail("Explicit", $"{ProbeFlag} {NoWindowFlag}", result);

        StringAssert.Contains(result.Stdout, AliveMarker,
            $"Explicit permits a zero-surface running state; the process should not have exited.\n{detail}");
        StringAssert.Contains(result.Stdout, "windows=0",
            $"The probe opened a window, so this run did not exercise zero-surface startup.\n{detail}");
        Assert.AreEqual(AliveExitCode, result.ExitCode, detail);
    }

    [TestMethod]
    public void OnLastSurfaceClosed_Keeps_Process_Alive_When_Startup_Opens_Only_A_Tray_Icon()
    {
        var result = RunProbe("OnLastSurfaceClosed", NoWindowFlag, TrayFlag);
        var detail = Detail("OnLastSurfaceClosed", $"{ProbeFlag} {NoWindowFlag} {TrayFlag}", result);

        StringAssert.Contains(result.Stdout, AliveMarker,
            $"Tray-only startup is the documented shape for this policy (spec 036 §13.6).\n{detail}");
        StringAssert.Contains(result.Stdout, "windows=0 trayIcons=1",
            $"This run did not reach the tray-only state it is meant to assert.\n{detail}");
        Assert.AreEqual(AliveExitCode, result.ExitCode, detail);
    }

    [TestMethod]
    public void OnPrimaryWindowClosed_Exits_When_Startup_Opens_No_Surface()
    {
        // Control for the two arms above: same zero-surface startup, opposite
        // outcome. "I forgot to OpenWindow" must still exit under the default.
        var result = RunProbe("OnPrimaryWindowClosed", NoWindowFlag);
        var detail = Detail("OnPrimaryWindowClosed", $"{ProbeFlag} {NoWindowFlag}", result);

        StringAssert.Contains(result.Stdout, LoopExitedMarker,
            $"A startup that opens zero windows under the default policy must exit immediately.\n{detail}");
        Assert.AreEqual(LoopExitedExitCode, result.ExitCode, detail);
    }

    [TestMethod]
    public void Explicit_Can_Reopen_A_Window_After_Outliving_The_Last_One()
    {
        // Surviving the close is only half of spec 036 §13.6 — the tray icon has
        // to be able to put the window back.
        var result = RunProbe("Explicit", TrayFlag, ReopenFlag);
        var detail = Detail("Explicit", $"{ProbeFlag} {TrayFlag} {ReopenFlag}", result);

        StringAssert.Contains(result.Stdout, "STILL-ALIVE windows=0 trayIcons=1",
            $"The process had not actually outlived its last window when the reopen ran.\n{detail}");
        StringAssert.Contains(result.Stdout, ReopenedMarker,
            $"The probe never reached the reopen step.\n{detail}");
        StringAssert.Contains(result.Stdout, "before=0 after=1",
            $"Reopening did not produce a registered window.\n{detail}");
        Assert.AreEqual(AliveExitCode, result.ExitCode,
            $"Exit code 45 means the reopened window never registered.\n{detail}");
    }

    [TestMethod]
    public void OnPrimaryWindowClosed_Stays_Alive_When_The_Last_Window_Opted_Out_Of_The_Policy()
    {
        // Issue #647's guarantee, which the windows guide already states:
        // an auxiliary window (a docking tear-off) is never elected primary, so
        // closing it never exits the app "even when it is the last visible
        // window". The platform used to break that promise — OnLastWindowClose
        // unwound the loop no matter what EvaluateShutdownPolicy decided — and
        // it only holds now because Reactor owns the loop for every policy,
        // including the default.
        var result = RunProbe("OnPrimaryWindowClosed", ExcludedWindowFlag);
        var detail = Detail("OnPrimaryWindowClosed", $"{ProbeFlag} {ExcludedWindowFlag}", result);

        StringAssert.Contains(result.Stdout, "primaryElected=False",
            $"The probe's window was elected primary, so this run never reached the auxiliary-window case.\n{detail}");
        StringAssert.Contains(result.Stdout, AliveMarker,
            $"Closing an auxiliary window ended the process, contradicting the documented issue-#647 behaviour.\n{detail}");
        StringAssert.Contains(result.Stdout, "windows=0",
            $"The auxiliary window never closed, so a live process proves nothing about closing one.\n{detail}");
        Assert.AreEqual(AliveExitCode, result.ExitCode, detail);
    }

    [TestMethod]
    public void Explicit_Keeps_Process_Alive_Through_The_Legacy_Run_Entry_Point()
    {
        // ReactorApp.Run<TRoot> reaches OnLaunched by a different branch than
        // Run(startup) and takes dispatcher ownership at its own call site.
        // Every other arm goes through the callback overload, so without this
        // one that call site could be deleted with the suite still green.
        var result = RunProbe("Explicit", LegacyRunFlag);
        var detail = Detail("Explicit", $"{ProbeFlag} {LegacyRunFlag}", result);

        StringAssert.Contains(result.Stdout, "legacy=True",
            $"This run did not take the legacy entry point.\n{detail}");
        StringAssert.Contains(result.Stdout, AliveMarker,
            $"The legacy Run<TRoot> entry point exited on last-window-close.\n{detail}");
        StringAssert.Contains(result.Stdout, "windows=0",
            $"The window was still open, so a live process proves nothing.\n{detail}");
        Assert.AreEqual(AliveExitCode, result.ExitCode, detail);
    }

    [TestMethod]
    public void OnPrimaryWindowClosed_Exits_Through_The_Legacy_Run_Entry_Point()
    {
        // Control for the arm above: same entry point, default policy, opposite
        // outcome.
        var result = RunProbe("OnPrimaryWindowClosed", LegacyRunFlag);
        var detail = Detail("OnPrimaryWindowClosed", $"{ProbeFlag} {LegacyRunFlag}", result);

        StringAssert.Contains(result.Stdout, LoopExitedMarker,
            $"The legacy entry point must still exit when its primary window closes.\n{detail}");
        Assert.AreEqual(LoopExitedExitCode, result.ExitCode, detail);
    }

    // ── ownership gate (negative case) ─────────────────────────────────────

    [TestMethod]
    public void Setting_The_Policy_Does_Not_Touch_A_Host_Applications_Lifetime()
    {
        // Runs a process whose Application is NOT a ReactorApplication — the one
        // shape no in-process fixture can reach, since inside the selftest host
        // the gate's condition is always true.
        var result = RunHost(HostProcess.FindHostExe(), GateProbeFlag);
        var detail = Detail("Explicit", GateProbeFlag, result);

        StringAssert.Contains(result.Stdout, GateHeldMarker,
            $"Reactor rewrote DispatcherShutdownMode on an Application it does not own. An app embedding " +
            $"ReactorHostControl would stop exiting when its last window closes, and hang instead.\n{detail}");
        Assert.AreEqual(GateHeldExitCode, result.ExitCode,
            $"Exit 44 = gate violated, 46 = inconclusive (the probe could not establish its preconditions).\n{detail}");
    }
}
