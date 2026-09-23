using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

/// <summary>
/// Bounds of a UIA element in physical screen pixels (winapp's BoundingRectangle).
/// </summary>
public readonly record struct UiRect(int X, int Y, int Width, int Height)
{
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;
}

/// <summary>One element returned by <c>winapp ui search</c>.</summary>
public sealed record UiMatch(
    string? Type,
    string? Name,
    string? AutomationId,
    string? ClassName,
    bool IsEnabled,
    bool IsOffscreen,
    int X, int Y, int Width, int Height,
    string Selector,
    bool IsInvokable,
    string? InvokableAncestorType = null,
    string? InvokableAncestorSelector = null);

/// <summary>A visible top-level window returned by <c>winapp ui list-windows</c>.</summary>
public sealed record UiWindow(long Hwnd, int ProcessId, string? Title, string? ClassName, bool IsForeground);

/// <summary>
/// Thin wrapper over the <c>winapp ui</c> CLI (UI Automation). Each method spawns a
/// short-lived <c>winapp.exe</c> process targeting the Host app's window (by HWND for
/// stability — survives the multi-window state docking tear-off creates) and parses the
/// <c>--json</c> envelope with System.Text.Json.
///
/// This replaces the persistent Appium <c>WindowsDriver</c> session. winapp has no
/// persistent session (process-per-call), so polling helpers map onto winapp's own
/// internal <c>wait-for</c> (single process, 100ms internal poll) to avoid spawning a
/// process per poll tick.
/// </summary>
public sealed class WinAppUi
{
    private static readonly string WinAppExe = ResolveWinAppExe();

    /// <summary>
    /// The winapp binary this process resolved, for diagnostics.
    /// </summary>
    /// <remarks>
    /// Worth recording rather than inferring. <see cref="ResolveWinAppExe"/> prefers an explicit
    /// override, then <c>%LOCALAPPDATA%\Microsoft\WindowsApps</c>, and only then <c>PATH</c> — so
    /// a caller that installs a specific winapp and puts it on <c>PATH</c> can still be running a
    /// different one, and a bare <c>winapp</c> on the command line is not necessarily the binary
    /// these tests use. Printing the path is what makes those two facts comparable.
    /// </remarks>
    internal static string ResolvedWinAppExe => WinAppExe;

    private readonly int _pid;

    /// <summary>HWND of the primary Host window, captured at session start.</summary>
    public long HostHwnd { get; }

    public WinAppUi(int pid, long hostHwnd)
    {
        _pid = pid;
        HostHwnd = hostHwnd;
    }

    private static string ResolveWinAppExe()
    {
        // Explicit override — an absolute path to a winapp.exe, honored first.
        // Needed when the machine's `winapp.exe` app-execution-alias resolves to an
        // older build that lacks the `ui` UIA verb (alias drift from a sideloaded
        // winapp-dev package), while a newer ui-capable winapp is installed elsewhere
        // (e.g. %LOCALAPPDATA%\Microsoft\WindowsApps\winapp_8wekyb3d8bbwe\winapp.exe).
        // Also lets CI pin an exact winapp build. No effect unless the var is set.
        var overridePath = Environment.GetEnvironmentVariable("REACTOR_WINAPP_EXE");
        if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
            return Path.GetFullPath(overridePath);

        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(local))
        {
            var candidate = Path.Combine(local, "Microsoft", "WindowsApps", "winapp.exe");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                         .Where(Path.IsPathFullyQualified))
            {
                var candidate = Path.Combine(entry, "winapp.exe");
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
        }

        throw new WinAppException(
            "Could not find winapp.exe. Install the winapp CLI (winget install Microsoft.WinAppCli) " +
            "or ensure an absolute PATH entry contains winapp.exe, or set REACTOR_WINAPP_EXE to an " +
            "absolute path to a ui-capable winapp.exe.");
    }

    /// <summary>
    /// Total number of <c>winapp.exe</c> processes spawned since process start. winapp has no
    /// persistent session — every verb is a fresh process — so this is the headline
    /// process-spawn-overhead metric vs the old single long-lived WinAppDriver session.
    /// <see cref="AppTestBase"/> snapshots this around each test to report a per-test count.
    /// </summary>
    public static long InvocationCount;

    // ─── UI turn continuity ──────────────────────────────────────────────────

    /// <summary>
    /// winapp's logical-UI-workflow variable. Every <c>winapp ui</c> mutation takes a turn on the
    /// interactive desktop whether or not this is set — collision arbitration is unconditional.
    /// What the variable buys is <em>continuity</em>: a workflow that names itself keeps a
    /// post-command idle grace, so a second agent driving the same desktop cannot interleave
    /// between our click and the assertion that reads the result. An anonymous command releases
    /// the desktop the instant it exits, which is exactly the window a concurrent run slips into.
    /// </summary>
    internal const string WorkflowIdEnvVar = "WINAPP_UI_WORKFLOW_ID";

    /// <summary>Mirrors winapp's own cap; a longer value is rejected with InvalidWorkflowId.</summary>
    internal const int MaxWorkflowIdLength = 256;

    /// <summary>
    /// UTF-8 that refuses to encode ill-formed UTF-16 instead of substituting U+FFFD — the same
    /// encoder configuration winapp hashes the workflow id with.
    /// </summary>
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// The workflow id stamped onto every <c>winapp ui</c> child process, resolved once per test
    /// process. Public so a failure report can name the workflow that held the desktop.
    /// </summary>
    public static string WorkflowId { get; } = ResolveWorkflowId(
        Environment.GetEnvironmentVariable(WorkflowIdEnvVar),
        Environment.ProcessId,
        Guid.NewGuid());

    /// <summary>
    /// Picks the workflow id for this test process. An ambient value wins, so an agent harness (or
    /// a developer) can group a whole test run with surrounding manual <c>winapp ui</c> calls into
    /// one workflow. Only a *usable* ambient value wins: winapp hard-fails an unusable id on every
    /// single command, so inheriting one would break the entire suite rather than degrade it —
    /// falling back to a synthesized id is strictly better than propagating a guaranteed error.
    /// </summary>
    /// <remarks>
    /// The three rejection cases are winapp's, not ours: empty/whitespace, longer than
    /// <see cref="MaxWorkflowIdLength"/>, and text that is not well-formed UTF-16. The last one is
    /// easy to miss — winapp hashes the id with a strict UTF-8 encoder specifically so that
    /// distinct ill-formed ids cannot collide onto one owner key, which makes an unpaired
    /// surrogate a hard error rather than a mangled-but-accepted value.
    /// </remarks>
    internal static string ResolveWorkflowId(string? ambient, int pid, Guid unique)
    {
        if (!string.IsNullOrWhiteSpace(ambient)
            && ambient.Length <= MaxWorkflowIdLength
            && IsWellFormedUtf16(ambient))
        {
            return ambient;
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"reactor-apptests-{pid}-{unique:N}");
    }

    /// <summary>
    /// Whether a string survives strict UTF-8 encoding — i.e. contains no unpaired surrogate.
    /// </summary>
    /// <remarks>
    /// Deliberately asks the same encoder winapp uses rather than scanning for surrogate code
    /// units by hand, so this cannot drift from the rule it is predicting.
    /// </remarks>
    private static bool IsWellFormedUtf16(string value)
    {
        try
        {
            StrictUtf8.GetByteCount(value);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the resolved winapp implements <c>ui yield</c> at all, probed once per test
    /// process and cached.
    /// </summary>
    /// <remarks>
    /// <para>Cooperative UI turns landed whole in winappCli#767, so a build without the verb has
    /// no turn arbitration to release and every yield is a spawn that can only fail. Asking once
    /// turns a per-test cost into a per-process one; the probe is forced lazily from
    /// <see cref="ReleaseUiTurn"/>, so a run that never drives the UI never pays even that.</para>
    /// <para><see cref="Lazy{T}"/> rather than a plain field because MSTest may run test classes
    /// in parallel: the default publication mode runs the probe exactly once no matter how many
    /// threads reach it together, which is the difference between one extra <c>winapp.exe</c> and
    /// one per worker.</para>
    /// </remarks>
    internal static bool SupportsUiYield => YieldVerb.Value == UiVerbSupport.Present;

    /// <summary>
    /// The raw tri-state behind <see cref="SupportsUiYield"/>, so a caller that must not proceed
    /// on a guess can tell "winapp says no" from "winapp did not answer".
    /// </summary>
    internal static UiVerbSupport UiYieldSupport => YieldVerb.Value;

    private static readonly Lazy<UiVerbSupport> YieldVerb = new(ProbeYieldVerb);

    /// <summary>What a capability probe was able to establish about one <c>winapp ui</c> verb.</summary>
    internal enum UiVerbSupport
    {
        /// <summary>The verb is listed by <c>winapp ui --help</c>.</summary>
        Present,

        /// <summary>The command list was read, and the verb is not in it.</summary>
        Absent,

        /// <summary>
        /// No usable command list came back, so nothing was established either way. Distinct from
        /// <see cref="Absent"/> on purpose: absence is a measurement, this is its failure.
        /// </summary>
        Unreadable,
    }

    /// <summary>
    /// Asks the resolved winapp whether it understands <c>ui yield</c>, by reading the command
    /// list out of <c>winapp ui --help</c>.
    /// </summary>
    /// <remarks>
    /// <para>This deliberately does not run <c>ui yield --help</c> and check the exit code, which
    /// is what it used to do. Measured against winapp 0.6.3-prerelease.92, an unrecognized verb
    /// does not error: <c>ui bogusverbxyz --help</c> exits <c>0</c> and prints output
    /// byte-identical to <c>ui --help</c>, never mentioning the unrecognized token. The old probe
    /// therefore answered "yes" for every verb, including ones that do not exist. It happened to
    /// return the right answer — the published builds that lack <c>yield</c> are old enough to
    /// still reject unmatched tokens — but it was no longer measuring anything, which would have
    /// made <c>REACTOR_E2E_REQUIRE_UI_YIELD</c> a gate that cannot fail.</para>
    /// <para>Grepping the output of <c>ui yield --help</c> for the word "yield" does not fix it
    /// either, and is the trap worth naming: the fallback help lists every subcommand with its
    /// description, so the word is present whether or not the verb is. Only the parent command
    /// list distinguishes them.</para>
    /// </remarks>
    private static UiVerbSupport ProbeYieldVerb()
    {
        try
        {
            var psi = CreateStartInfo("--help");

            using var proc = Process.Start(psi);
            if (proc is null) return UiVerbSupport.Unreadable;

            // Read before waiting: winapp's command list is larger than a pipe buffer, and a
            // child that fills stdout while we wait for exit deadlocks against its own output.
            var stdout = proc.StandardOutput.ReadToEnd();

            if (!proc.WaitForExit(YieldTimeoutMs))
            {
                TryKill(proc);
                return UiVerbSupport.Unreadable;
            }

            // The exit code is not the signal here, but a non-zero one means the text that came
            // back is not a command list worth parsing.
            return proc.ExitCode == 0
                ? ParseUiVerbSupport(stdout, "yield")
                : UiVerbSupport.Unreadable;
        }
        // Same narrow set as ReleaseUiTurn: these mean "winapp could not be run here". That is
        // not the verb being absent, and saying so would be claiming a measurement never taken.
        catch (System.ComponentModel.Win32Exception) { return UiVerbSupport.Unreadable; }
        catch (InvalidOperationException) { return UiVerbSupport.Unreadable; }
        catch (NotSupportedException) { return UiVerbSupport.Unreadable; }
        catch (TypeInitializationException) { return UiVerbSupport.Unreadable; }
    }

    /// <summary>
    /// Verbs that have existed for as long as <c>winapp ui</c> has, used to prove the command
    /// list was actually parsed before concluding anything from a verb's absence.
    /// </summary>
    /// <remarks>
    /// Several rather than one so a single rename does not turn every run
    /// <see cref="UiVerbSupport.Unreadable"/>; any one of them is enough to establish that the
    /// section was found and understood.
    /// </remarks>
    private static readonly string[] SentinelUiVerbs = ["status", "inspect", "invoke"];

    /// <summary>
    /// Extracts the <c>Commands:</c> section from <c>winapp ui --help</c> and reports whether
    /// <paramref name="verb"/> is listed in it.
    /// </summary>
    /// <remarks>
    /// Pure, so the discriminator can be tested against captured help text without a winapp on
    /// the machine. The sentinel check is what stops a format change from being read as "the
    /// verb was removed": if not one long-standing verb can be found either, the parse failed
    /// and the honest answer is <see cref="UiVerbSupport.Unreadable"/>.
    /// </remarks>
    internal static UiVerbSupport ParseUiVerbSupport(string uiHelp, string verb)
    {
        if (string.IsNullOrWhiteSpace(uiHelp)) return UiVerbSupport.Unreadable;

        var lines = uiHelp.Replace("\r\n", "\n").Split('\n');

        var header = Array.FindIndex(
            lines, l => l.Trim().Equals("Commands:", StringComparison.Ordinal));
        if (header < 0) return UiVerbSupport.Unreadable;

        var listed = new HashSet<string>(StringComparer.Ordinal);
        for (var i = header + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || string.IsNullOrWhiteSpace(line)) continue;

            // The section ends at the next unindented line: entries are indented, headers are not.
            if (!char.IsWhiteSpace(line[0])) break;

            var name = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            listed.Add(name);
        }

        if (!SentinelUiVerbs.Any(listed.Contains)) return UiVerbSupport.Unreadable;

        return listed.Contains(verb) ? UiVerbSupport.Present : UiVerbSupport.Absent;
    }

    /// <summary>
    /// Releases this workflow's UI turn instead of waiting out the idle grace. Best-effort by
    /// construction: yielding is an optimization that hands the desktop to a waiting agent sooner,
    /// so a failure here must never redden a test that already passed. The turn is released by the
    /// idle grace regardless, making the worst case a short delay rather than a stranded desktop.
    /// </summary>
    /// <param name="verbPresent">
    /// Overrides the cached <see cref="SupportsUiYield"/> probe. Injected so the decision to skip
    /// the spawn is testable without depending on which winapp the machine happens to resolve —
    /// the regression worth catching is this gate going missing, and an environment-derived oracle
    /// could not catch it on a machine whose winapp has the verb.
    /// </param>
    /// <returns>
    /// winapp's exit code, or <see langword="null"/> if the process could not be run at all —
    /// including when the resolved winapp has no such verb, which is the same "nothing was
    /// released" outcome from the caller's point of view. Surfaced rather than discarded because
    /// it is the one externally observable proof that a workflow id reached the child:
    /// <c>winapp ui yield</c> exits non-zero when the variable is absent and zero when it is
    /// present.
    /// </returns>
    internal static int? ReleaseUiTurn(bool? verbPresent = null)
    {
        // Ahead of RecordInvocation, so a winapp that cannot yield costs neither a process nor a
        // phantom entry in the per-test spawn metric.
        if (!(verbPresent ?? SupportsUiYield)) return null;

        try
        {
            RecordInvocation();

            var psi = CreateStartInfo("yield", "--json");

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            if (!proc.WaitForExit(YieldTimeoutMs))
            {
                TryKill(proc);
                return null;
            }

            return proc.ExitCode;
        }
        // Narrow rather than bare: the contract is that yielding cannot redden a passing test, and
        // these are the failures that actually mean "winapp could not be run here" (missing or
        // unlaunchable executable, a process that died between calls, an unresolvable winapp.exe
        // surfacing through the static initializer). Something outside this set is not a yield
        // problem and should surface rather than be silently turned into a null.
        catch (System.ComponentModel.Win32Exception ex) { return YieldUnavailable(ex); }
        catch (InvalidOperationException ex) { return YieldUnavailable(ex); }
        catch (NotSupportedException ex) { return YieldUnavailable(ex); }
        catch (TypeInitializationException ex) { return YieldUnavailable(ex); }
    }

    private static int? YieldUnavailable(Exception ex)
    {
        Console.WriteLine($"Could not release the winapp UI turn ({ex.GetType().Name}: {ex.Message}). " +
                          "Falling back to the idle grace.");
        return null;
    }

    /// <summary>
    /// Best-effort termination of a winapp child that overran its timeout, waiting a bounded
    /// time for it to actually go.
    /// </summary>
    /// <remarks>
    /// The caller is already failing or returning null, so a kill failure changes no outcome —
    /// but it is reported rather than swallowed, because repeated failures leave orphaned winapp
    /// processes holding the UI turn, which looks like an unrelated hang in the *next* test.
    /// <para><see cref="Process.Kill(bool)"/> only <em>requests</em> termination and returns
    /// immediately, so reporting the kill is not the same as the process being gone. Every
    /// caller returns or throws the moment this comes back, which put the next test's first
    /// `winapp ui` call in a race with a child that still held the turn — the same orphan
    /// symptom the warning above exists to make legible, arrived at through success rather than
    /// failure. Waiting closes that window; a process still alive after the grace period is
    /// reported, since at that point it is stuck in the kernel and no further kill will help.</para>
    /// <para><b>Deliberately untested, because the available oracle was vacuous.</b> A test was
    /// written asserting the postcondition — exited by the time this returns — and then mutation
    /// checked by deleting the wait. It stayed green across three consecutive runs: a
    /// <c>ping.exe</c> probe is already reaped by the time <c>Kill</c> returns, so the healthy
    /// and broken builds are indistinguishable to it. Shipping it would have implied coverage
    /// that does not exist, so it was dropped rather than kept green. The guarantee here rests on
    /// <see cref="Process.Kill(bool)"/> being documented as asynchronous; a real oracle needs a
    /// process that is slow to die on demand, which nothing in this tier currently provides.</para>
    /// </remarks>
    internal static void TryKill(Process proc)
    {
        var requested = false;

        try
        {
            proc.Kill(entireProcessTree: true);
            requested = true;
        }
        catch (InvalidOperationException ex) { WarnKillFailed(ex); }
        catch (NotSupportedException ex) { WarnKillFailed(ex); }
        catch (System.ComponentModel.Win32Exception ex) { WarnKillFailed(ex); }
        catch (AggregateException ex) { WarnKillFailed(ex); }

        if (!requested) return;

        try
        {
            if (!proc.WaitForExit(KillGraceMs))
            {
                Console.WriteLine(
                    $"Timed-out winapp child did not exit within {KillGraceMs}ms of being killed; " +
                    "it may still hold the UI turn as the next test starts.");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process is gone and its handle no longer answers, which is the outcome the
            // wait was after. Nothing to report.
        }

        static void WarnKillFailed(Exception ex) =>
            Console.WriteLine($"Could not terminate the timed-out winapp child " +
                              $"({ex.GetType().Name}: {ex.Message}).");
    }

    /// <summary>
    /// How long to wait for a killed winapp child to actually exit. Short on purpose: this runs
    /// only after a timeout has already been paid, and a process that has not gone this long
    /// after <c>TerminateProcess</c> is blocked in kernel mode, where waiting longer changes
    /// nothing.
    /// </summary>
    private const int KillGraceMs = 5_000;

    private const int YieldTimeoutMs = 10_000;

    // ─── Process plumbing ────────────────────────────────────────────────────

    /// <summary>
    /// Builds the <see cref="ProcessStartInfo"/> for one <c>winapp ui</c> child. The single place
    /// the workflow id is stamped: every spawn in this harness routes through here, so the turn
    /// continuity cannot be wired on one path and silently missing on another.
    /// </summary>
    internal static ProcessStartInfo CreateStartInfo(params string[] args)
    {
        var psi = new ProcessStartInfo(WinAppExe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("ui");
        foreach (var a in args) psi.ArgumentList.Add(a);

        // Stamp every child with this run's workflow id so the whole suite reads as one logical
        // UI workflow to winapp's turn arbitration, rather than a few thousand anonymous one-shots
        // that each drop the desktop the moment they exit.
        psi.Environment[WorkflowIdEnvVar] = WorkflowId;

        return psi;
    }

    private readonly record struct RunResult(int ExitCode, string StdOut, string StdErr);

    // Bumps the process-global spawn counter from a static scope, keeping the mutation off the
    // instance Run path while still aggregating across the many short-lived WinAppUi instances.
    private static void RecordInvocation() => Interlocked.Increment(ref InvocationCount);

    private RunResult Run(int processTimeoutMs, params string[] args)
    {
        RecordInvocation();

        var psi = CreateStartInfo(args);

        using var proc = new Process { StartInfo = psi };
        var sbOut = new StringBuilder();
        var sbErr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };

        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            throw new WinAppException(
                $"Failed to launch winapp ({WinAppExe}). Ensure winapp CLI is installed and on PATH.", ex);
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        if (!proc.WaitForExit(processTimeoutMs))
        {
            TryKill(proc);
            throw new WinAppTimeoutException(
                $"winapp ui {string.Join(' ', args)} did not exit within {processTimeoutMs}ms.");
        }
        // Ensure async buffers are flushed.
        proc.WaitForExit();

        return new RunResult(proc.ExitCode, sbOut.ToString(), sbErr.ToString());
    }

    /// <summary>Append the window target + --json to a verb's args.</summary>
    private string[] Args(string verb, long hwnd, params string[] rest)
        => BuildArgs(verb, hwnd, rest);

    internal static string[] BuildArgs(string verb, long hwnd, params string[] rest)
    {
        var list = new List<string>(rest.Length + 5) { verb };
        list.AddRange(rest);
        list.Add("-w");
        list.Add(hwnd.ToString(CultureInfo.InvariantCulture));
        list.Add("--json");
        return list.ToArray();
    }

    private static JsonDocument Parse(RunResult r)
        => ParseJson(r.ExitCode, r.StdOut, r.StdErr);

    internal static JsonDocument ParseJson(int exitCode, string stdOut, string stdErr)
    {
        var text = stdOut.Trim();
        if (text.Length == 0)
            throw new WinAppException($"winapp returned empty output (exit {exitCode}). stderr: {stdErr.Trim()}");
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new WinAppException($"Could not parse winapp JSON (exit {exitCode}): {text}", ex);
        }
    }

    // ─── Connection ──────────────────────────────────────────────────────────

    /// <summary>Resolve the primary Host window HWND for a process by title.</summary>
    public static long FindWindowHwnd(int pid, string title, int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var ui = new WinAppUi(pid, 0);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                foreach (var w in ui.ListWindowsForPid()
                             .Where(w => w.ProcessId == pid &&
                                         (w.Title?.Contains(title, StringComparison.OrdinalIgnoreCase) ?? false)))
                    return w.Hwnd;
            }
            catch (Exception ex) { last = ex; }
            Thread.Sleep(200);
        }
        throw new WinAppTimeoutException(
            $"Host window '{title}' (pid {pid}) did not appear within {timeoutMs}ms." +
            (last is null ? "" : $" Last error: {last.Message}"));
    }

    private IEnumerable<UiWindow> ListWindowsForPid()
    {
        var r = Run(15000, "list-windows", "-a", _pid.ToString(CultureInfo.InvariantCulture), "--json");
        using var doc = Parse(r);
        foreach (var w in EnumerateWindows(doc.RootElement)) yield return w;
    }

    /// <summary>All visible windows belonging to the Host process (host + floating tear-off windows).</summary>
    public IReadOnlyList<UiWindow> ListWindows()
    {
        var result = new List<UiWindow>();
        var r = Run(15000, "list-windows", "-a", _pid.ToString(CultureInfo.InvariantCulture), "--json");
        using var doc = Parse(r);
        result.AddRange(EnumerateWindows(doc.RootElement));
        return result;
    }

    private static IEnumerable<UiWindow> EnumerateWindows(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array) yield break;
        foreach (var w in root.EnumerateArray())
        {
            yield return new UiWindow(
                GetLong(w, "hwnd"),
                (int)GetLong(w, "processId"),
                GetString(w, "title"),
                GetString(w, "className"),
                GetBool(w, "isForeground"));
        }
    }

    // ─── Search / existence ──────────────────────────────────────────────────

    /// <summary>Run <c>winapp ui search</c> against the given window. Empty list on miss.</summary>
    public IReadOnlyList<UiMatch> Search(string selector, long? hwnd = null)
    {
        var r = Run(15000, Args("search", hwnd ?? HostHwnd, selector));
        var matches = new List<UiMatch>();
        if (r.StdOut.Trim().Length == 0) return matches;
        using var doc = Parse(r);
        if (!doc.RootElement.TryGetProperty("matches", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return matches;
        foreach (var m in arr.EnumerateArray())
        {
            // winapp reports, in the SAME search call, the nearest invokable ancestor of a
            // non-invokable match (e.g. a tab caption TextBlock → its owning TabItem). Capturing
            // it here lets callers resolve the tab without a second `inspect` call — which would
            // open a re-render race window where the caption's hash slug can go stale (exactly
            // what broke SelectTab on an inactive pinnable docking tab).
            string? ancType = null, ancSel = null;
            if (m.TryGetProperty("invokableAncestor", out var anc) && anc.ValueKind == JsonValueKind.Object)
            {
                ancType = GetString(anc, "type");
                ancSel = GetString(anc, "selector");
            }
            matches.Add(new UiMatch(
                GetString(m, "type"), GetString(m, "name"), GetString(m, "automationId"),
                GetString(m, "className"), GetBool(m, "isEnabled"), GetBool(m, "isOffscreen"),
                (int)GetLong(m, "x"), (int)GetLong(m, "y"), (int)GetLong(m, "width"), (int)GetLong(m, "height"),
                GetString(m, "selector") ?? selector, GetBool(m, "isInvokable"),
                ancType, ancSel));
        }
        return matches;
    }

    /// <summary>True if any element matches the selector in the target window.</summary>
    public bool Exists(string selector, long? hwnd = null) => Search(selector, hwnd).Count > 0;

    // ─── Reads ───────────────────────────────────────────────────────────────

    /// <summary>Smart value read (TextPattern → ValuePattern → Name). Null if absent.</summary>
    public string? GetValue(string selector, long? hwnd = null)
    {
        var r = Run(15000, Args("get-value", hwnd ?? HostHwnd, selector));
        if (r.ExitCode != 0 || r.StdOut.Trim().Length == 0) return null;
        using var doc = Parse(r);
        return GetString(doc.RootElement, "text");
    }

    /// <summary>Read a single UIA property. Null if winapp can't surface it (caller may fall back to UIA).</summary>
    public string? GetProperty(string selector, string property, long? hwnd = null)
    {
        var r = Run(15000, Args("get-property", hwnd ?? HostHwnd, selector, "-p", property));
        if (r.ExitCode != 0 || r.StdOut.Trim().Length == 0) return null;
        using var doc = Parse(r);
        if (!doc.RootElement.TryGetProperty("properties", out var props)) return null;
        if (!props.TryGetProperty(property, out var val)) return null;
        return val.ValueKind == JsonValueKind.Null ? null : val.GetString();
    }

    /// <summary>Bounds of the first element matching the selector. Null if absent.</summary>
    public UiRect? GetBounds(string selector, long? hwnd = null)
    {
        var matches = Search(selector, hwnd);
        if (matches.Count == 0) return null;
        var m = matches[0];
        return new UiRect(m.X, m.Y, m.Width, m.Height);
    }

    /// <summary>
    /// Walks up the UIA tree from <paramref name="childSelector"/> and returns the slug of the
    /// nearest ancestor whose control type is <c>TabItem</c> (a WinUI <c>TabViewItem</c>), or
    /// null if none is found. Used to resolve a tab from its caption: a docking tab with a pin
    /// affordance renders a composite (StackPanel) header, so the <c>TabViewItem</c> itself has
    /// no Name and is not returned by a text search for the caption — but its caption TextBlock
    /// child is, and the TabItem is its ancestor. The returned slug invokes the tab's
    /// SelectionItemPattern (selects the tab) without colliding with the pin button that a plain
    /// caption-text Invoke would hit.
    /// </summary>
    public string? ResolveAncestorTab(string childSelector, long? hwnd = null)
    {
        var r = Run(15000, Args("inspect", hwnd ?? HostHwnd, childSelector, "--ancestors"));
        if (r.ExitCode != 0 || r.StdOut.Trim().Length == 0) return null;
        using var doc = Parse(r);
        if (!doc.RootElement.TryGetProperty("windows", out var windows) ||
            windows.ValueKind != JsonValueKind.Array)
            return null;
        // --ancestors emits the root→target lineage as a nested children chain; the deepest
        // TabItem on the path is the tab that owns this caption.
        string? found = null;
        foreach (var win in windows.EnumerateArray())
            if (win.TryGetProperty("elements", out var els))
                WalkForTab(els, ref found);
        return found;
    }

    private static void WalkForTab(JsonElement nodes, ref string? found)
    {
        if (nodes.ValueKind != JsonValueKind.Array) return;
        foreach (var n in nodes.EnumerateArray())
        {
            if (string.Equals(GetString(n, "type"), "TabItem", StringComparison.OrdinalIgnoreCase) &&
                GetString(n, "selector") is { } sel)
                found = sel; // keep the deepest TabItem on the lineage
            if (n.TryGetProperty("children", out var kids))
                WalkForTab(kids, ref found);
        }
    }

    // ─── Actions ─────────────────────────────────────────────────────────────

    /// <summary>Activate via UIA patterns (Invoke → Toggle → SelectionItem → ExpandCollapse).</summary>
    public void Invoke(string selector, long? hwnd = null)
    {
        var r = Run(15000, Args("invoke", hwnd ?? HostHwnd, selector));
        if (r.ExitCode == 0)
            return;

        if (CanFallbackToClick(r))
            Click(selector, hwnd: hwnd);
        else
            throw new WinAppException($"winapp ui invoke '{selector}' failed: {r.StdErr.Trim()} {r.StdOut.Trim()}");
    }

    private static bool CanFallbackToClick(RunResult r)
    {
        var text = $"{r.StdErr}\n{r.StdOut}";
        return text.Contains("invoke pattern", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("invokable pattern", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("not invokable", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("not invokeable", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("does not support invoke", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("no supported pattern", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("no supported action", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Mouse-simulation click (for elements without InvokePattern).</summary>
    public void Click(string selector, bool doubleClick = false, bool rightClick = false, long? hwnd = null)
    {
        // winapp's click verb is real SendInput under the hood — it fails with
        // ACCESS_DENIED off the interactive input desktop (non-uiAccess / disconnected
        // session). Surface that as Inconclusive (not Failed) via the interactivity guard.
        SessionInteractivityGuard.EnsureInputInjectable($"click '{selector}'");

        var extra = new List<string> { selector };
        if (doubleClick) extra.Add("--double");
        if (rightClick) extra.Add("--right");
        var r = Run(15000, Args("click", hwnd ?? HostHwnd, extra.ToArray()));
        if (r.ExitCode != 0)
            throw new WinAppException($"winapp ui click '{selector}' failed: {r.StdErr.Trim()} {r.StdOut.Trim()}");
    }

    /// <summary>
    /// Synthesize keyboard input through the native <c>winapp ui send-keys</c> verb. <paramref name="keys"/>
    /// uses winapp's token grammar: named keys (<c>tab</c>, <c>enter</c>, <c>home</c>, <c>f1</c>), modifier
    /// combos (<c>ctrl+a</c>, <c>shift+tab</c>), raw virtual keys (<c>vk=0x6B</c>), and <c>text=</c> literals.
    /// With <paramref name="viaSendInput"/> the OS-wide send-input transport raises a real per-character
    /// KeyDown (required by keystroke-observing handlers); the default post-message transport only raises
    /// TextChanged. <paramref name="target"/>, when set, is focused (UIA SetFocus) before the keys are sent.
    /// </summary>
    public void SendKeys(string keys, bool viaSendInput = false, string? target = null, long? hwnd = null)
    {
        RejectUntranslatedKeyConstants(keys);

        // send-input routes OS-wide and fails with ACCESS_DENIED off the interactive input desktop, just
        // like the click verb — surface that as Inconclusive (not Failed). post-message posts
        // straight to the window's message queue and needs no interactive desktop, so only guard send-input.
        if (viaSendInput)
            SessionInteractivityGuard.EnsureInputInjectable($"send-keys '{keys}'");

        var extra = new List<string> { keys };
        if (viaSendInput) { extra.Add("--via"); extra.Add("send-input"); }
        if (!string.IsNullOrEmpty(target)) { extra.Add("--target"); extra.Add(target); }
        var r = Run(15000, Args("send-keys", hwnd ?? HostHwnd, extra.ToArray()));
        if (r.ExitCode != 0)
            throw new WinAppException($"winapp ui send-keys '{keys}' failed: {r.StdErr.Trim()} {r.StdOut.Trim()}");
    }

    /// <summary>
    /// The <see cref="Keys"/> private-use code points, mapped to the constant that declares each one.
    /// Read from <see cref="Keys"/> by reflection rather than re-listed here: a hand-copied set is a
    /// two-place registration, and the place it would go stale is precisely the case this guard exists
    /// to catch — a tenth constant added with no arm in <c>TryNamedKey</c>/<c>TryModifierName</c>.
    /// Grouped rather than keyed directly so an alias (two names, one code point) cannot throw at
    /// static-init.
    /// </summary>
    private static readonly Dictionary<char, string> KeyConstantNames = typeof(Keys)
        .GetFields(global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (f.Name, Value: f.GetRawConstantValue() as string))
        .Where(x => x.Value is { Length: 1 })
        .GroupBy(x => x.Value![0])
        .ToDictionary(g => g.Key, g => string.Join("/", g.Select(x => x.Name)));

    /// <summary>
    /// Rejects a <see cref="Keys"/> constant that reached the winapp token grammar untranslated.
    /// </summary>
    /// <remarks>
    /// <para>This verb takes the winapp TOKEN grammar ("tab", "enter", "ctrl+a delete", "text=..."),
    /// not the private-use constants in <see cref="Keys"/>. Those are a <c>UiElement.SendKeys</c>
    /// convenience — <c>UiElement.ToSendKeysTokens</c> translates them; this path does not. Passing one
    /// here used to forward the raw glyph as literal text: the CLI typed an unmapped character, exited
    /// 0, and nothing moved. A no-op that reports success is the worst possible failure mode for an
    /// input primitive — an E2E built on it fails much later, somewhere else, claiming the product did
    /// not respond.</para>
    /// <para>Scoped to the <see cref="Keys"/> code points rather than the whole private-use area
    /// (U+E000..U+F8FF), because <c>text=</c> literals are part of the documented grammar and carry
    /// arbitrary user payload — a private-use character that is not a <see cref="Keys"/> constant is
    /// ordinary text, and rejecting it would contradict the contract on <see cref="SendKeys"/>.
    /// Scoping by code point rather than by token position keeps the second failure mode covered:
    /// a <see cref="Keys"/> constant with no translation arm is emitted by
    /// <c>ToSendKeysTokens</c> INSIDE a <c>text=</c> token, so a position-based check would let
    /// exactly that case through.</para>
    /// </remarks>
    internal static void RejectUntranslatedKeyConstants(string keys)
    {
        foreach (var ch in keys.Where(KeyConstantNames.ContainsKey))
        {
            throw new global::System.ArgumentException(
                $"SendKeys received the private-use key constant U+{(int)ch:X4} (Keys.{KeyConstantNames[ch]}). " +
                "WinAppUi.SendKeys takes winapp token syntax — pass \"tab\"/\"enter\"/\"esc\" directly, or run " +
                "the string through UiElement.ToSendKeysTokens first. If it already came from " +
                "ToSendKeysTokens, that constant has no arm in TryNamedKey/TryModifierName and was emitted " +
                "as literal text — add one there.",
                nameof(keys));
        }
    }

    /// <summary>
    /// Drag from one point to another through the native <c>winapp ui drag</c> verb. <paramref name="from"/>
    /// and <paramref name="to"/> are each an element selector (drags from/to the element's center) or
    /// <c>"x,y"</c> screen coordinates in the same space <see cref="GetBounds"/> / <c>UiElement.Rect</c>
    /// report. The CLI interpolates the motion internally (crossing WinUI's 4-DIP drag threshold) and
    /// re-resolves element endpoints after foregrounding. <paramref name="holdMs"/> presses and holds the
    /// button before moving — with <c>from == to</c> (no movement) this is a press-and-hold / long-press;
    /// <paramref name="dwellMs"/> settles on the destination before releasing so hover-armed drop targets /
    /// merge overlays can latch.
    /// </summary>
    public void Drag(string from, string to, int holdMs = 0, int dwellMs = 0, bool rightButton = false,
        long? hwnd = null)
    {
        // Native drag is real SendInput mouse input — ACCESS_DENIED off the interactive desktop, as click.
        SessionInteractivityGuard.EnsureInputInjectable($"drag '{from}' -> '{to}'");

        var extra = new List<string> { from, to };
        if (rightButton) extra.Add("--right");
        if (holdMs > 0) { extra.Add("--hold-ms"); extra.Add(holdMs.ToString(CultureInfo.InvariantCulture)); }
        if (dwellMs > 0) { extra.Add("--dwell-ms"); extra.Add(dwellMs.ToString(CultureInfo.InvariantCulture)); }
        // Give the process budget for the hold + dwell it will spend inside the drag on top of the base
        // stabilize/interpolate work, so a long-press/merge dwell can't trip the process timeout.
        var r = Run(15000 + holdMs + dwellMs, Args("drag", hwnd ?? HostHwnd, extra.ToArray()));
        if (r.ExitCode != 0)
            throw new WinAppException($"winapp ui drag '{from}' -> '{to}' failed: {r.StdErr.Trim()} {r.StdOut.Trim()}");
    }

    /// <summary>Set a value via UIA ValuePattern (TextBox/ComboBox/Slider).</summary>
    public void SetValue(string selector, string value, long? hwnd = null)
    {
        var r = Run(15000, Args("set-value", hwnd ?? HostHwnd, selector, value));
        if (r.ExitCode != 0)
            throw new WinAppException($"winapp ui set-value '{selector}' failed: {r.StdErr.Trim()} {r.StdOut.Trim()}");
    }

    /// <summary>Move keyboard focus to the element via UIA SetFocus.</summary>
    public void Focus(string selector, long? hwnd = null)
    {
        var r = Run(15000, Args("focus", hwnd ?? HostHwnd, selector));
        if (r.ExitCode != 0)
            throw new WinAppException($"winapp ui focus '{selector}' failed: {r.StdErr.Trim()} {r.StdOut.Trim()}");
    }

    /// <summary>
    /// Selector (volatile slug) of the first editable text control — UIA type <c>Edit</c> — in the
    /// window. Used to locate the DataGrid inline editor, which renders without an AutomationId and so
    /// can only be addressed by winapp's semantic slug. The slug must be consumed immediately (focus /
    /// type) before the next re-render, since it is a display hint, not a stable handle.
    /// </summary>
    public string? FindFirstEditableSelector(long? hwnd = null)
    {
        var r = Run(15000, Args("inspect", hwnd ?? HostHwnd, "-i"));
        if (r.ExitCode != 0 || r.StdOut.Trim().Length == 0) return null;
        using var doc = Parse(r);
        if (!doc.RootElement.TryGetProperty("windows", out var wins) || wins.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var w in wins.EnumerateArray())
        {
            if (!w.TryGetProperty("elements", out var els) || els.ValueKind != JsonValueKind.Array)
                continue;
            if (FindFirstEditableSelector(els) is { } selector)
                return selector;
        }
        return null;
    }

    internal static string? FindFirstEditableSelector(JsonElement nodes)
    {
        if (nodes.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var node in nodes.EnumerateArray())
        {
            if (string.Equals(GetString(node, "type"), "Edit", StringComparison.OrdinalIgnoreCase) &&
                GetString(node, "selector") is { } selector)
                return selector;

            if (node.TryGetProperty("children", out var children) &&
                FindFirstEditableSelector(children) is { } childSelector)
                return childSelector;
        }

        return null;
    }

    // ─── Waits (winapp-internal polling) ─────────────────────────────────────

    /// <summary>Wait for the element to exist. Returns false on timeout.</summary>
    public bool WaitForExists(string selector, int timeoutMs = 5000, long? hwnd = null)
    {
        var r = Run(timeoutMs + 30000,
            Args("wait-for", hwnd ?? HostHwnd, selector, "--timeout", timeoutMs.ToString(CultureInfo.InvariantCulture)));
        return r.ExitCode == 0;
    }

    /// <summary>Wait for the element to disappear. Returns false on timeout.</summary>
    public bool WaitForGone(string selector, int timeoutMs = 5000, long? hwnd = null)
    {
        var r = Run(timeoutMs + 30000,
            Args("wait-for", hwnd ?? HostHwnd, selector, "--gone",
                "--timeout", timeoutMs.ToString(CultureInfo.InvariantCulture)));
        return r.ExitCode == 0;
    }

    /// <summary>Wait until the element's value equals (or contains) the target. False on timeout.</summary>
    public bool WaitForValue(string selector, string value, bool contains = false,
        int timeoutMs = 5000, long? hwnd = null)
    {
        var args = new List<string> { selector, "--value", value, "--timeout",
            timeoutMs.ToString(CultureInfo.InvariantCulture) };
        if (contains) args.Add("--contains");
        var r = Run(timeoutMs + 30000, Args("wait-for", hwnd ?? HostHwnd, args.ToArray()));
        return r.ExitCode == 0;
    }

    // ─── JSON helpers ────────────────────────────────────────────────────────

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long GetLong(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt64(out var l) ? l : 0,
            JsonValueKind.String => long.TryParse(v.GetString(), out var l) ? l : 0,
            _ => 0,
        };
    }

    private static bool GetBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False && v.GetBoolean();
}
