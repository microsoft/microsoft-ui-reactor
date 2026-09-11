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
        return RunHost(exe, args);
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
            $"The process exited when its last window closed — DispatcherShutdownMode was never raised to OnExplicitShutdown.\n{detail}");
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
        StringAssert.Contains(result.Stdout, "trayIcons=1",
            $"The probe reported no tray icon, so this run did not exercise the surviving-surface case.\n{detail}");
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

        StringAssert.Contains(result.Stdout, ReopenedMarker,
            $"The probe never reached the reopen step.\n{detail}");
        StringAssert.Contains(result.Stdout, "before=0 after=1",
            $"Reopening did not produce a registered window.\n{detail}");
        Assert.AreEqual(AliveExitCode, result.ExitCode,
            $"Exit code 45 means the reopened window never registered.\n{detail}");
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
            $"ReactorHostControl would start exiting when its last window closes.\n{detail}");
        Assert.AreEqual(GateHeldExitCode, result.ExitCode,
            $"Exit 44 = gate violated, 46 = inconclusive (the probe could not establish its preconditions).\n{detail}");
    }
}
