using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.SelfTests;

/// <summary>
/// Runs all in-process self-test fixtures in a single Host app launch and reports
/// one [TestMethod] per fixture. The Host mounts each fixture, runs assertions via
/// VisualTreeHelper, and emits TAP to stdout. We parse the TAP stream, split it by
/// `# Running: <fixture>` boundaries, and pair each fixture with pass/fail.
///
/// Fixture names are discovered at test-discovery time by launching the Host with
/// `--list-fixtures` (a fast-path that prints names and exits without starting WinUI).
/// </summary>
[TestClass]
public class SelfTestBatch
{
    /// <summary>
    /// Whole-suite process budget for the <c>--self-test</c> run. This is a <b>backstop, not the
    /// hang detector</b>, and that distinction is what sets its size.
    ///
    /// <para>The Host owns two watchdogs that attribute <i>causally</i>: a per-fixture graceful
    /// timeout (<c>SelfTestFixtureBase.FixtureTimeout</c>, which emits
    /// <c>not ok &lt;n&gt; &lt;fixture&gt;_TIMEOUT</c>) and an off-dispatcher watchdog that
    /// declares a hang after 60 s of no fixture progress (emitting <c>HANG_DETECTED:</c> and
    /// fast-failing). Both name a culprit. This cap only fires when <i>both</i> were unable to —
    /// they are disabled under a debugger and via
    /// <c>REACTOR_SELFTEST_HANG_TIMEOUT_SECONDS</c> — and its attribution is merely
    /// <b>positional</b>: whichever fixture happened to be in flight.</para>
    ///
    /// <para><b>So it must be sized as "the suite could not legitimately take this long", not as
    /// "the suite normally takes this long".</b> It was 300 s, against measured runs of 262–346 s
    /// locally and up to 97.6 % of cap on CI — i.e. <c>main</c> breached it with no PR
    /// contribution, and every breach manufactured a spurious single-fixture failure on an
    /// arbitrary victim (issue #988). Raising it does not delay real hang detection, because the
    /// 60 s off-dispatcher watchdog fires first and names the offender.</para>
    /// </summary>
    private const int DefaultSelfTestTimeoutSeconds = 900;   // 15 min

    /// <summary>
    /// Overrides <see cref="DefaultSelfTestTimeoutSeconds"/> for slow or heavily contended
    /// machines and for stress shards, mirroring the Host's own
    /// <c>REACTOR_SELFTEST_HANG_TIMEOUT_SECONDS</c> knob.
    /// </summary>
    internal const string TimeoutEnvVar = "REACTOR_SELFTEST_TIMEOUT_SECONDS";

    /// <summary>
    /// Soft threshold at which <see cref="SuiteDuration_WithinBudget"/> warns. Deliberately its
    /// <b>own constant</b> rather than a fraction of <see cref="DefaultSelfTestTimeoutSeconds"/>:
    /// 80 % of a deliberately-generous 900 s cap is 720 s, which would only warn long after the
    /// margin had actually eroded. 420 s is ≈1.2× the slowest run measured when this was written,
    /// so it fires while there is still headroom to act.
    /// </summary>
    internal const int SuiteDurationWarnSeconds = 420;

    private static readonly int SelfTestTimeoutMs =
        ResolveTimeoutMilliseconds(Environment.GetEnvironmentVariable(TimeoutEnvVar));

    private const int ListFixturesTimeoutMs = 30_000;

    /// <summary>
    /// Resolves the suite budget in seconds from an environment value, falling back to
    /// <see cref="DefaultSelfTestTimeoutSeconds"/> for absent, unparseable, non-positive, or
    /// overflowing input. There is deliberately no upper sanity cap, so a large-but-representable
    /// override (say 2,000,000s) is accepted: the workflow's own <c>timeout-minutes</c> already
    /// bounds the run, and a second ceiling here would only be a different arbitrary number to
    /// keep in sync. A malformed override must not silently produce a tiny (or zero)
    /// budget — that would recreate the exact failure this constant exists to prevent, only
    /// faster — nor a value that overflows when converted to milliseconds, which lands as a
    /// *negative* timeout and fails initialization outright.
    /// </summary>
    internal static int ResolveTimeoutSeconds(string? envValue)
    {
        if (!string.IsNullOrWhiteSpace(envValue)
            && int.TryParse(envValue.Trim(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0
            && seconds <= int.MaxValue / 1000)
        {
            return seconds;
        }

        return DefaultSelfTestTimeoutSeconds;
    }

    /// <summary>
    /// The same resolution in milliseconds — the unit the process watchdog actually takes. Exists
    /// as its own seam so the environment-to-budget wiring is reachable from a test: the static
    /// field below runs once at type load, so a test cannot re-enter it with a different value.
    /// </summary>
    internal static int ResolveTimeoutMilliseconds(string? envValue) =>
        ResolveTimeoutSeconds(envValue) * 1000;

    // Diagnostic numbers are formatted invariantly and parsed invariantly (see
    // ExtractSuiteElapsedSeconds). A machine with a comma decimal separator would otherwise emit
    // "901,2s" into a message whose documented triage snippets — and this suite's own assertions —
    // read a '.', so the run would fail on locale rather than on anything the suite measured.
    private static string Fixed1(double value) =>
        value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

    private static string Fixed0(double value) =>
        value.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The budget actually in force this run, after the environment override.</summary>
    internal static int EffectiveTimeoutSeconds => SelfTestTimeoutMs / 1000;

    // Per-fixture aggregated outcome, populated by ClassInitialize.
    // Key = fixture name; Value = the three-state verdict plus its detail.
    private static readonly ConcurrentDictionary<string, FixtureOutcome> _byFixture = new();
    private static string _fullOutput = "";
    private static bool _initialized;
    private static string? _initError;
    // Captured process outcome for the teardown-exit-code guard (issue #680).
    private static int _exitCode;
    private static bool _timedOut;
    // When the Host run aborts (hang/timeout) we attribute the failure to a
    // single fixture, but every later fixture still has no entry in
    // _byFixture. _abortedReason marks the run as not-fully-executed so the
    // Fixture test method can report missing entries as Inconclusive rather
    // than cascading "was not reported by the Host" failures across every
    // fixture downstream of the hang.
    private static string? _abortedReason;
    // Suite duration, for the duration gate and for the budget-overrun message.
    // _hostElapsedSeconds is the Host's own figure (excludes process start and
    // pipe-drain overhead); _wrapperElapsedSeconds is what this process measured.
    private static double _wrapperElapsedSeconds;
    private static double? _hostElapsedSeconds;

    private static double ElapsedSeconds => _hostElapsedSeconds ?? _wrapperElapsedSeconds;

    [ClassInitialize]
    public static void RunSelfTests(TestContext context)
    {
        var exe = FindHostExe();
        var stopwatch = Stopwatch.StartNew();
        var (stdout, stderr, exitCode, timedOut) = RunProcess(exe, "--self-test", SelfTestTimeoutMs);
        stopwatch.Stop();
        _wrapperElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
        _exitCode = exitCode;
        _timedOut = timedOut;

        _fullOutput = stdout;
        if (!string.IsNullOrEmpty(stderr))
            _fullOutput += "\n--- stderr ---\n" + stderr;

        _hostElapsedSeconds = ExtractSuiteElapsedSeconds(stdout);

        var tap = ParseTap(stdout, _byFixture);

        // Off-dispatcher watchdog in the Host emits a structured signal on
        // dispatcher-starvation hangs. Parse it from stdout *and* stderr (the
        // Host writes to both before FailFast so the signal survives buffered
        // pipes), then attribute the failure to the named fixture so the dev
        // sees a clear pointer to the offender instead of an opaque "process
        // timed out" affecting every fixture.
        var hangFixture = ExtractHangSignal(stdout) ?? ExtractHangSignal(stderr);

        var outcome = ClassifyAbort(
            timedOut, hangFixture, ExtractLastRunningFixture(stdout),
            SelfTestTimeoutMs, _wrapperElapsedSeconds, _byFixture.Count, TryGetFixtureCount(),
            Tail(_fullOutput, 4000));

        var disposition = ApplyAbortOutcome(
            outcome, timedOut, SelfTestTimeoutMs, _fullOutput, _byFixture);
        _abortedReason = disposition.AbortReason;
        _initError = disposition.InitError;

        if (!disposition.RunEarlyAbortCheck)
        {
            _initialized = true;
            return;
        }

        MarkEarlyAbortIfNeeded(exitCode, tap);

        _initialized = true;

        if (exitCode != 0 && _byFixture.IsEmpty)
            _initError = $"Self-test process exited with code {exitCode} but produced no parsable TAP output.\n{_fullOutput}";
    }

    /// <summary>The fixture an aborted run is attributed to, and how that attribution is worded.</summary>
    internal sealed record AbortOutcome(string Fixture, string Detail, string AbortReason);

    /// <summary>
    /// What a fixture's TAP output actually established. Three states, not two, because
    /// <c>Harness.Skip</c> emits a line beginning <c>ok </c> and the parser used to let that
    /// satisfy its own "did you assert anything?" guard — so a fixture whose <i>only</i> output
    /// was a skip reported PASSED, with zero <c>not ok</c> lines and a <c># Total failures: 0</c>
    /// trailer (issue #1061).
    ///
    /// <para><b>Why a third state rather than just not counting the skip.</b> Treating a skip line
    /// as "no checks" makes <c>passed</c> false, which turns
    /// <c>CenterOnCurrent_UsesCursorMonitor</c> and <c>CornerStyle_Apply</c> red on exactly the
    /// machines their skip was introduced to accommodate — a non-interactive desktop where
    /// <c>GetCursorPos</c> cannot answer, and Windows 10 where the DWM corner attribute is not
    /// round-trippable. Those are undeterminable preconditions, not product defects. The verdict
    /// has to carry "could not tell" as its own value; anything else re-creates one of the two
    /// bugs.</para>
    /// </summary>
    internal enum FixtureStatus
    {
        /// <summary>At least one real assertion ran, and none failed.</summary>
        Passed,

        /// <summary>An assertion failed, the fixture crashed/timed out, or it emitted nothing at all.</summary>
        Failed,

        /// <summary>The fixture ran no assertions at all; everything it owns was skipped.</summary>
        Skipped,
    }

    /// <summary>
    /// A fixture's verdict, the text explaining it, and the checks it skipped.
    ///
    /// <para><paramref name="SkippedChecks"/> is carried for <b>passing</b> fixtures too, not just
    /// fully-skipped ones: a fixture that asserted 26 of its 27 checks and skipped the 27th is
    /// legitimately green, but the skipped leg is still information the TAP stream carried and the
    /// consumer used to discard. It is reported rather than acted on.</para>
    /// </summary>
    internal sealed record FixtureOutcome(
        FixtureStatus Status, string Detail, IReadOnlyList<string> SkippedChecks)
    {
        internal FixtureOutcome(FixtureStatus status, string detail)
            : this(status, detail, Array.Empty<string>()) { }
    }

    /// <summary>
    /// What the runner does with a classified abort: the reason to stamp on every unexecuted
    /// fixture, an initialization error when a timeout could not be attributed at all, and whether
    /// the early-abort scan still runs.
    /// </summary>
    internal sealed record RunDisposition(string? AbortReason, string? InitError, bool RunEarlyAbortCheck);

    /// <summary>
    /// Applies a classified abort to the per-fixture map and decides what happens next.
    ///
    /// <para>Split out from <see cref="RunSelfTests"/> so the <i>wiring</i> is testable, not just
    /// the classifier. <see cref="ClassifyAbort"/> being correct in isolation says nothing about
    /// whether production still routes through it — reverting the caller to the old inline branches
    /// would leave a classifier test suite entirely green while restoring the misleading
    /// attribution of issue #988. The map is passed in rather than read from the static field for
    /// the same reason: a test can watch the attribution actually land.</para>
    ///
    /// <para><paramref name="timedOut"/> suppresses the early-abort scan because a killed process
    /// is truncated by definition — every downstream fixture is missing, so the scan would
    /// "discover" an early abort on every single run and overwrite the budget attribution.</para>
    /// </summary>
    internal static RunDisposition ApplyAbortOutcome(
        AbortOutcome? outcome,
        bool timedOut,
        int budgetMs,
        string fullOutput,
        IDictionary<string, FixtureOutcome> byFixture)
    {
        if (outcome is not null)
        {
            byFixture[outcome.Fixture] = new FixtureOutcome(FixtureStatus.Failed, outcome.Detail);
            return new RunDisposition(outcome.AbortReason, null, RunEarlyAbortCheck: !timedOut);
        }

        if (timedOut)
        {
            return new RunDisposition(
                null,
                $"Self-test process timed out after {budgetMs}ms with no fixture attribution.\n{fullOutput}",
                RunEarlyAbortCheck: false);
        }

        return new RunDisposition(null, null, RunEarlyAbortCheck: true);
    }

    /// <summary>
    /// Decides which fixture (if any) an aborted run is attributed to, and with what wording.
    /// Returns null when the run was not aborted, or when nothing can be attributed.
    ///
    /// <para>This is the whole point of issue #988, so it is a <b>pure function that production
    /// actually calls</b> rather than three inline branches. Three cases reach here and they used
    /// to be two:</para>
    /// <list type="bullet">
    /// <item><b>Hang, process died.</b> The Host's watchdog printed <c>HANG_DETECTED</c> and
    /// <c>FailFast</c>ed, so the process exited on its own. Causal — this is the ordinary hang
    /// path, and the common one.</item>
    /// <item><b>Hang, process would not die.</b> Same signal, but <c>FailFast</c> did not take the
    /// process down before the wrapper's budget expired. Still causal.</item>
    /// <item><b>Budget expired with no signal.</b> Positional: the named fixture was merely in
    /// flight. This is the case that manufactured seven spurious failures across six PRs.</item>
    /// </list>
    /// </summary>
    internal static AbortOutcome? ClassifyAbort(
        bool timedOut, string? hangFixture, string? lastRunningFixture,
        int budgetMs, double elapsedSeconds, int fixturesReported, int? fixturesTotal, string tail)
    {
        if (hangFixture is not null)
        {
            return new AbortOutcome(
                hangFixture,
                DescribeStarvationHang(hangFixture, timedOut, budgetMs, tail),
                StarvationHangAbortReason(hangFixture, timedOut));
        }

        if (!timedOut || lastRunningFixture is null) return null;

        return new AbortOutcome(
            lastRunningFixture,
            DescribeBudgetOverrun(lastRunningFixture, budgetMs, elapsedSeconds, fixturesReported, fixturesTotal, tail),
            BudgetOverrunAbortReason(lastRunningFixture, budgetMs));
    }

    /// <summary>
    /// Renders a Host exit code with its NTSTATUS interpretation, for the truncated-TAP paths
    /// where the only question a triager has is "did the host fault, or did something kill it?".
    ///
    /// <para>This is a <b>strong prior, not a guarantee</b>, and the wording keeps that honest.
    /// What is measured: .NET's <c>Process.Kill()</c> — and <c>Stop-Process -Force</c>, which goes
    /// through the same path — produce <c>-1</c> (<c>0xFFFFFFFF</c>); <c>taskkill /F</c> produces
    /// <c>1</c>. What is NOT established: that an external killer *cannot* produce an
    /// NTSTATUS-shaped code. <c>TerminateProcess</c> takes <c>uExitCode</c> as an arbitrary
    /// <c>UINT</c>, so the caller picks it; nothing structurally prevents a killer choosing
    /// <c>0xC0000005</c>. It just isn't what any killer in this environment does.</para>
    ///
    /// <para><b>Scope of the prior, and when it expires.</b> The inference is not over
    /// <c>TerminateProcess</c> — it is over <i>the population of things that kill this process</i>.
    /// It holds because every killer present today (this harness's own watchdog, an external
    /// <c>Process.Kill</c>/<c>Stop-Process</c>, <c>taskkill /F</c>) lands on <c>-1</c> or
    /// <c>1</c>. It weakens the moment that population changes — a new harness, a CI job-object
    /// teardown, a container runtime, or any watchdog that deliberately propagates the child's
    /// status. <b>If you add something that can kill the Host, check what exit code it produces
    /// and revisit this method.</b> A reader with no cue that the prior is environment-scoped
    /// would keep trusting it after it stopped being true.</para>
    ///
    /// <para>The raw value is always printed alongside the interpretation so nobody has to trust
    /// the mapping. A triager who reads "external kill" as certain stops looking, and the cost of
    /// a false certainty here is chasing the wrong cause.</para>
    /// </summary>
    internal static string DescribeExitCode(int exitCode)
    {
        // Compared as uint: these are NTSTATUS values that arrive as negative Int32.
        var known = (uint)exitCode switch
        {
            0xC0000005 => "STATUS_ACCESS_VIOLATION",
            0xC000027B => "STATUS_STOWED_EXCEPTION (WinUI/WinRT — the most likely one here)",
            0xC0000409 => "STATUS_STACK_BUFFER_OVERRUN / fast-fail",
            0xC00000FD => "STATUS_STACK_OVERFLOW",
            0xE0434352 => "CLR managed exception (unhandled .NET exception)",
            _ => null,
        };

        // The CLR's managed-exception tag is NOT NTSTATUS-shaped — 0xE0434352 & 0xF0000000 is
        // 0xE0000000, so the mask below does not catch it and it would otherwise fall through
        // with no verdict at all. That is the likeliest crash mode for a .NET host, so it gets
        // its own branch. Same tag the Devtools stress runner already keys on
        // (DevtoolsStressE2ERunner.cs) and MxcSandbox documents.
        bool clrManaged = (uint)exitCode == 0xE0434352u;

        // NTSTATUS failure codes are 0xC0000000-shaped. Treat that whole space as
        // "the host faulted", not just the four named above.
        bool ntStatusShaped = ((uint)exitCode & 0xF0000000u) == 0xC0000000u;

        var raw = $"Exit code: {exitCode} (0x{(uint)exitCode:X8}{(known is null ? "" : " " + known)})";

        if (clrManaged)
        {
            return raw + "\n  -> Unhandled MANAGED exception: the host almost certainly crashed " +
                   "on its own, via the CLR's unhandled-exception path rather than a native " +
                   "fault. The exception type and stack trace are in the Host's stderr / the " +
                   "output tail below — read those first. As with the native-fault codes, a " +
                   "terminator can pass any value to TerminateProcess, so this is a strong prior " +
                   "rather than proof.";
        }

        if (ntStatusShaped)
        {
            return raw + "\n  -> NTSTATUS-shaped: the host almost certainly crashed on its own. " +
                   "Look for the faulting fixture, not for an external killer. " +
                   "(Known killers in this environment exit -1 or 1, but TerminateProcess lets a " +
                   "caller choose any code, so this is a strong prior rather than proof.)";
        }

        if (exitCode is -1 or 1)
        {
            return raw + "\n  -> Category is decidable, cause is NOT. This says the host did not fault; " +
                   "it does not say who ended it. Beware: `RunProcess` SYNTHESIZES -1 for this " +
                   "harness's own watchdog kill (it discards the real code), and an external " +
                   "`Process.Kill` / `Stop-Process`, a parent reap and a CI job-object teardown all " +
                   "land on -1 too — so -1 alone cannot name the agent. `taskkill /F` and a genuine " +
                   "fixture failure both exit 1; use the TAP trailer to separate those two: present " +
                   "trailer = real failure, truncated = killed.";
        }

        return raw;
    }

    /// <summary>
    /// Message for a run where the Host emitted a <c>HANG_DETECTED:</c> signal, i.e. the named
    /// fixture starved the dispatcher. <b>Causal</b> attribution: this fixture is the culprit, and
    /// a per-fixture repro genuinely reproduces it — which is why this message keeps the
    /// <c>--filter</c> line and <see cref="DescribeBudgetOverrun"/> deliberately does not.
    /// </summary>
    /// <param name="alsoTimedOut">
    /// False on the ordinary path (the watchdog's <c>FailFast</c> took the process down, so the
    /// wrapper never had to). True when even <c>FailFast</c> did not land before the suite budget
    /// expired — worth saying out loud, because it means the process was wedged below the CLR.
    /// </param>
    internal static string DescribeStarvationHang(string fixture, bool alsoTimedOut, int budgetMs, string tail)
    {
        var lead = alsoTimedOut
            ? $"DISPATCHER-STARVATION HANG in '{fixture}' — this fixture IS the cause.\n" +
              $"The Host's off-dispatcher watchdog named it via a HANG_DETECTED signal, and the " +
              $"process then failed to exit within the {budgetMs / 1000}s suite budget, so the " +
              $"wrapper killed it. FailFast not landing points below the CLR — a native lock or " +
              $"a wedged UI thread.\n"
            : $"DISPATCHER-STARVATION HANG in '{fixture}' — this fixture IS the cause.\n" +
              $"The Host's off-dispatcher watchdog named it via a HANG_DETECTED signal and " +
              $"fast-failed the process.\n";

        return lead +
               $"Repro: build the Host (AOT publish if needed) and run " +
               $"`Reactor.AppTests.Host.exe --self-test --no-aot-skip --filter {fixture}`. " +
               $"Set DOTNET_DbgEnableMiniDump=1 (and COMPlus_DbgEnableMiniDump=1) to capture a dump.\n" +
               $"--- tail of full output ---\n{tail}";
    }

    /// <summary>
    /// Message for a wrapper timeout with <b>no</b> hang signal: the suite ran out of its shared
    /// process budget, and the harness blamed whichever fixture happened to be in flight.
    ///
    /// <para><b>This message's job is to stop the reader debugging that fixture.</b> Issue #988
    /// records seven distinct victims across six PRs, all innocent, because three things in the
    /// old message pointed the wrong way: the fixture name looked causal, MSTest printed the
    /// fixture's own elapsed time (<c>[16 ms]</c>) next to a 300-second process kill so it read as
    /// a fast assertion failure, and the <c>Repro:</c> line suggested <c>--filter &lt;fixture&gt;</c>
    /// — which removes the other fixtures sharing the budget, i.e. removes the cause, so the
    /// suggested reproduction essentially always passes and argues the fixture is fine.</para>
    ///
    /// <para>It must not overcorrect into the opposite false claim. The absence of a
    /// <c>HANG_DETECTED</c> signal does <b>not</b> prove the fixture innocent — the watchdog can be
    /// disabled by env or by an attached debugger, and a fixture can be pathologically slow, or
    /// order-dependent, while still pumping the dispatcher often enough never to trip it.
    /// Positional attribution means <i>unproven</i>, not <i>exonerated</i>, and the wording says
    /// so.</para>
    /// </summary>
    internal static string DescribeBudgetOverrun(
        string inFlight, int budgetMs, double elapsedSeconds, int fixturesReported, int? fixturesTotal, string tail)
    {
        var budgetSeconds = budgetMs / 1000.0;
        var progress = fixturesTotal is int total
            ? $"{fixturesReported} of {total}"
            : $"{fixturesReported}";

        // Say that the denominator is missing rather than just omitting it. A bare count reads as
        // if it were the total, and the reason it is missing — fixture discovery did not complete —
        // is itself a finding this message is the only place anyone would see it.
        var totalNote = fixturesTotal is null
            ? " — the suite total is UNKNOWN here because fixture discovery did not complete, " +
              "which is worth investigating on its own"
            : "";

        return $"SUITE BUDGET EXCEEDED — '{inFlight}' is NOT PROVEN to be the cause.\n" +
               $"The whole selftest suite shares ONE process budget. It expired while '{inFlight}' " +
               $"happened to be running, so the harness killed the Host and attributed the kill to " +
               $"it. That attribution is POSITIONAL: it records where the run was, not what went " +
               $"wrong, and the fixture named here differs from run to run.\n" +
               $"  elapsed   : {Fixed1(elapsedSeconds)}s against a {Fixed0(budgetSeconds)}s budget " +
               $"(these are necessarily close — the kill IS the budget expiring)\n" +
               $"  reported  : {progress} fixtures had TAP output parsed before the kill " +
               $"(includes '{inFlight}', which was still running){totalNote}\n" +
               $"  remaining : reported Skipped (Assert.Inconclusive) — never RUN, so their " +
               $"results say nothing\n" +
               $"Do NOT start by debugging '{inFlight}', and do NOT re-run it under `--filter`: " +
               $"that removes the other fixtures that shared the budget — i.e. removes the cause — " +
               $"so it passes whether or not the fixture is healthy. It is not a valid " +
               $"reproduction of a suite-budget kill in either direction.\n" +
               $"Start with total suite duration instead. If the suite has simply grown into its " +
               $"cap (issue #988), raise it with {TimeoutEnvVar} or trim suite time; the " +
               $"`# Fixture time:` TAP comments rank the offenders. If duration looks normal, " +
               $"look for a fixture that wedged WITHOUT tripping the Host's per-fixture timeout " +
               $"or its 60s off-dispatcher watchdog — which is the one scenario where '{inFlight}' " +
               $"could still turn out to be at fault.\n" +
               $"The exit code is not evidence on this path: RunProcess synthesizes -1 for its own " +
               $"watchdog kill and discards the real one.\n" +
               $"--- tail of full output ---\n{tail}";
    }

    // Abort reasons are stamped verbatim onto every unexecuted fixture (see Fixture below), so
    // their prefixes are the cheapest triage signal there is: they are readable off ANY skipped
    // fixture with no re-run and no raw job log. Keeping the causal and positional kinds distinct
    // here is the whole point — they used to share one string.
    internal static string StarvationHangAbortReason(string fixture, bool alsoTimedOut) =>
        alsoTimedOut
            ? $"Run aborted by dispatcher-starvation hang on fixture '{fixture}' (FailFast did not " +
              $"land; the wrapper's budget killed the process)"
            : $"Run aborted by dispatcher-starvation hang on fixture '{fixture}'";

    internal static string BudgetOverrunAbortReason(string inFlight, int budgetMs) =>
        $"Run aborted: suite exceeded its {budgetMs / 1000}s budget with fixture '{inFlight}' in " +
        $"flight (POSITIONAL attribution — that fixture is not proven to be at fault)";

    /// <summary>
    /// Renders the suite's wall clock against the soft warn threshold and the hard budget, and
    /// says whether it warrants a warning. Pure, so the thresholds are testable without a run.
    /// </summary>
    internal static (bool Warn, string Text) DescribeSuiteDuration(
        double elapsedSeconds, int warnSeconds, int budgetSeconds)
    {
        var percentOfBudget = budgetSeconds > 0 ? elapsedSeconds / budgetSeconds * 100.0 : 0.0;
        var warn = elapsedSeconds > warnSeconds;

        var text =
            $"Selftest suite duration: {Fixed1(elapsedSeconds)}s " +
            $"({Fixed1(percentOfBudget)}% of the {budgetSeconds}s hard budget, warn above {warnSeconds}s).";

        if (warn)
        {
            text += $"\nThe suite is approaching the budget that kills it. When it crosses, the " +
                    $"harness reports ONE arbitrary fixture as failed and skips the rest — a " +
                    $"misleading signal that has cost multiple investigations (issue #988). " +
                    $"Trim suite time, or raise the budget deliberately rather than discovering " +
                    $"it in a red PR.";
        }

        return (warn, text);
    }

    /// <summary>
    /// Reads the Host's own <c># Suite elapsed: &lt;seconds&gt;</c> trailer. Preferred over this
    /// process's stopwatch because it excludes process start and pipe-drain overhead (measured at
    /// ≈2.5 s in issue #988, enough to make a 300 s kill report as 302.5 s and confuse the margin).
    /// Returns null when the marker is absent — an older Host, or a run killed before the trailer.
    /// </summary>
    /// <remarks>Marker literal is duplicated from <c>SelfTestRunner.SuiteElapsedMarker</c>; the
    /// Host assembly is referenced with <c>ReferenceOutputAssembly=false</c> so it cannot be shared.
    /// <para>The whole-buffer <c>Split</c> is deliberate, not an oversight — review has suggested a
    /// reverse scan to avoid allocating the line array. This runs <b>once</b> per suite in
    /// <c>[ClassInitialize]</c>, not per fixture, on a buffer this process has just finished draining
    /// from a pipe: the split is dominated by the I/O that produced it and is unmeasurable against a
    /// budget counted in minutes. A hand-rolled reverse scan would trade a declarative form for two
    /// behaviours that are easy to lose; <c>SuiteElapsed_LastParseableMarkerWins</c> is the test that
    /// decides whether a rewrite kept them, failing on first-wins and, separately, on letting a
    /// malformed trailing marker discard a value already parsed.</para></remarks>
    internal static double? ExtractSuiteElapsedSeconds(string stdout)
    {
        if (string.IsNullOrEmpty(stdout)) return null;
        const string marker = "# Suite elapsed: ";

        // Last wins: a resumed or re-entered Host can emit the trailer more than once, and the
        // final one is the run being reported on. Unparseable markers are skipped rather than
        // treated as zero, so a malformed line cannot masquerade as an instantaneous suite.
        return stdout.Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith(marker, StringComparison.Ordinal))
            .Select(line => double.TryParse(line[marker.Length..].Trim(),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var seconds)
                ? seconds
                : (double?)null)
            .Where(seconds => seconds.HasValue)
            .LastOrDefault();
    }

    /// <summary>
    /// The fixture count for a diagnostic message, or <see langword="null"/> when it is not already
    /// known.
    /// </summary>
    /// <remarks>
    /// Deliberately reads <see cref="Lazy{T}.IsValueCreated"/> rather than <c>Value</c>. Two
    /// reasons, both about the caller: this runs while reporting a run that already failed, and
    /// (a) forcing the value there would launch a Host subprocess (<c>--list-fixtures</c>) from a
    /// failure path, while (b) a factory that already threw leaves <c>IsValueCreated</c> false, so
    /// the guard makes the read total without swallowing exceptions to achieve it. Discovery
    /// normally forces the value long before any of this — <c>DynamicData</c> drives
    /// <see cref="AllFixtures"/> — so the count is present in practice.
    /// </remarks>
    internal static int? FixtureCountIfKnown(Lazy<string[]> names)
        => names.IsValueCreated ? names.Value.Length : null;

    private static int? TryGetFixtureCount() => FixtureCountIfKnown(FixtureNames);

    private static string? ExtractHangSignal(string output)
    {
        if (string.IsNullOrEmpty(output)) return null;
        const string marker = "HANG_DETECTED: ";
        foreach (var raw in output.Split('\n'))
        {
            var idx = raw.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) continue;
            var rest = raw[(idx + marker.Length)..].TrimStart();
            var space = rest.IndexOf(' ');
            var name = (space > 0 ? rest[..space] : rest).Trim();
            if (name.Length > 0) return name;
        }
        return null;
    }

    private static string? ExtractLastRunningFixture(string stdout)
    {
        if (string.IsNullOrEmpty(stdout)) return null;
        string? last = null;
        const string marker = "# Running: ";
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith(marker, StringComparison.Ordinal))
                last = line[marker.Length..].Trim();
        }
        return string.IsNullOrEmpty(last) ? null : last;
    }

    private static string Tail(string s, int maxChars)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= maxChars) return s;
        return "..." + s[^maxChars..];
    }

    internal sealed record TapParseResult(string? LastRunningFixture, bool SawTotalFailures);

    /// <summary>
    /// Folds the Host's TAP stream into a per-fixture verdict.
    ///
    /// <para>Takes <paramref name="byFixture"/> as a parameter rather than reading the static
    /// field, for the same reason <see cref="ApplyAbortOutcome"/> does: it makes the <i>wiring</i>
    /// testable, not just the classification. A parser that is correct in isolation says nothing
    /// about whether production still routes through it, and issue #1061 was precisely a case of
    /// the stream carrying a distinction the consumer discarded.</para>
    /// </summary>
    internal static TapParseResult ParseTap(string stdout, IDictionary<string, FixtureOutcome> byFixture)
    {
        // Three TAP emitter shapes:
        //   Harness check:   "ok <checkName>"  /  "not ok <checkName> - <reason>"
        //   Harness skip:    "ok <checkName> # SKIP <reason>"
        //   SelfTestRunner:  "# Running: <fixtureName>"
        //                    "ok <index> <fixtureName> # SKIP <reason>"              (AOT skip list)
        //                    "not ok <index> <fixtureName> - fixture not found"     (before any marker)
        //                    "not ok <index> <fixtureName>_CRASH - <type>: <msg>"   (after marker if RunAsync threw)
        //
        // Runner-level lines start with a numeric test index; check-level lines do not.
        // Runner-level results attribute to their own fixture name regardless of `current`.

        string? current = null;
        var failuresForCurrent = new List<string>();
        var skipsForCurrent = new List<string>();
        var sawChecksForCurrent = false;
        string? lastRunningFixture = null;
        var sawTotalFailures = false;

        void Flush()
        {
            if (current is null) return;
            if (byFixture.TryGetValue(current, out var existing)
                && existing.Status == FixtureStatus.Failed
                && failuresForCurrent.Count == 0)
                return;

            var skipped = skipsForCurrent.ToArray();

            // Order matters, and each arm is a distinct claim about what the run established:
            //   a failure          -> the product (or the fixture) is broken
            //   a real assertion   -> the product was exercised and held
            //   only skips         -> nothing was established; say so instead of showing green
            //   nothing at all     -> the fixture is broken; this stays a FAILURE, because a
            //                         silent fixture has no documented reason where a skip does
            var outcome = failuresForCurrent.Count > 0
                ? new FixtureOutcome(
                    FixtureStatus.Failed, string.Join("\n", failuresForCurrent), skipped)
                : sawChecksForCurrent
                    ? new FixtureOutcome(FixtureStatus.Passed, "", skipped)
                    : skipped.Length > 0
                        ? new FixtureOutcome(
                            FixtureStatus.Skipped, DescribeFullySkipped(current, skipped), skipped)
                        : new FixtureOutcome(FixtureStatus.Failed, "fixture emitted no TAP checks");

            byFixture[current] = outcome;
        }

        foreach (var raw in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.StartsWith("# Running: "))
            {
                Flush();
                current = line["# Running: ".Length..].Trim();
                lastRunningFixture = current;
                failuresForCurrent = new List<string>();
                skipsForCurrent = new List<string>();
                sawChecksForCurrent = false;
            }
            else if (line.StartsWith("# Total failures:", StringComparison.Ordinal))
            {
                sawTotalFailures = true;
            }
            else if (line.StartsWith("ok "))
            {
                var rest = line[3..].Trim();
                if (TryParseSkipDirective(rest, out var skipName, out var skipReason))
                {
                    // A skip is NOT a check. Letting it set sawChecksForCurrent is the whole of
                    // issue #1061: the flag exists to catch a fixture that asserted nothing, and a
                    // skip is a fixture saying exactly that.
                    if (TryParseRunnerLevelName(skipName, out var skippedFixture))
                    {
                        // Fixture-level skip (the AOT pattern list). It arrives with no
                        // `# Running:` marker, so attributing it to `current` would credit an
                        // unrelated, already-finished fixture with a skip it never emitted.
                        //
                        // The entry is labelled rather than named, because there is no check name
                        // to give: the fixture never ran, so no `H.Skip` was reached and the TAP
                        // name here is the FIXTURE's. Reusing it would render as
                        // "`X` — X — <reason>" in the inventory, which both repeats the fixture
                        // and asserts a check called X was skipped. No such check exists.
                        byFixture[skippedFixture] = new FixtureOutcome(
                            FixtureStatus.Skipped,
                            $"Fixture '{skippedFixture}' was skipped by the runner before it ran: {skipReason}",
                            [$"{RunnerLevelSkipLabel} — {skipReason}"]);
                    }
                    else
                    {
                        skipsForCurrent.Add($"{skipName} — {skipReason}");
                    }
                }
                else
                {
                    // Harness-level pass; ignore payload, just note that current saw checks.
                    sawChecksForCurrent = true;
                }
            }
            else if (line.StartsWith("not ok "))
            {
                var rest = line[7..].Trim();
                if (TryParseRunnerLevelFailure(rest, out var fixtureName, out var detail))
                {
                    if (string.Equals(fixtureName, current, StringComparison.Ordinal))
                    {
                        sawChecksForCurrent = true;
                        failuresForCurrent.Add(detail);
                    }
                    else
                    {
                        // Runner-level failure — attribute directly to the fixture name,
                        // overriding any in-progress `current` bucket.
                        byFixture[fixtureName] = new FixtureOutcome(FixtureStatus.Failed, detail);
                    }
                }
                else
                {
                    sawChecksForCurrent = true;
                    if (current is not null)
                        failuresForCurrent.Add(rest);
                    // A check-level failure with no `# Running:` context is malformed TAP;
                    // drop it into the output blob (already captured in _fullOutput).
                }
            }
        }
        Flush();
        return new TapParseResult(lastRunningFixture, sawTotalFailures);
    }

    /// <summary>
    /// The message a fully-skipped fixture reports. Written for someone reading a Skipped result
    /// with no other context: it has to say that the fixture is <i>not</i> known-good, and that the
    /// skip is a deliberate design choice rather than an oversight, or the next reader will either
    /// trust it as a pass or "fix" it into a failure.
    /// </summary>
    private static string DescribeFullySkipped(string fixture, IReadOnlyList<string> skipped) =>
        $"Fixture '{fixture}' ran NO assertions — all {skipped.Count} of its checks were skipped, " +
        $"so this run establishes nothing about it either way.\n" +
        $"  {string.Join("\n  ", skipped)}\n" +
        $"This is reported as skipped rather than passed because a `# SKIP` directive is a fixture " +
        $"saying it did not make its assertion (issue #1061). It is deliberately NOT a failure, " +
        $"because a skip is usually a question this machine cannot answer — a non-interactive " +
        $"desktop where GetCursorPos returns ACCESS_DENIED, a Windows 10 box where the DWM corner " +
        $"attribute is not round-trippable — and failing those would redden the suite on exactly " +
        $"the machines the skips accommodate. Read the reasons above before concluding anything: " +
        $"some skips instead defer to the E2E tier, and some mark a tracked product gap with an " +
        $"issue number, where the product really is broken. If you need coverage here, give the " +
        $"fixture an observable precondition to assert before it skips (see " +
        $"NativeDockingA11yFixture), rather than turning the skip into a red.";

    /// <summary>
    /// Stands in for the check name on a runner-level skip, where the fixture never ran and so
    /// reached no <c>H.Skip</c> call. Reusing the fixture name there would render as
    /// <c>`X` — X — reason</c> and claim a check named <c>X</c> was skipped.
    /// </summary>
    internal const string RunnerLevelSkipLabel = "(whole fixture)";

    /// <summary>
    /// Splits a TAP <c>SKIP</c> directive off the payload of an <c>ok </c> line, returning the
    /// name and the reason. The directive is matched case-insensitively because TAP 14 specifies it
    /// that way; the Host emits upper case today, and a parser that only accepts what the current
    /// emitter happens to produce is one rename away from silently reporting every skip as a pass
    /// again.
    ///
    /// <para><b>Every</b> <c>#</c> is a directive candidate, not just the first. Check names embed
    /// a tracking number by convention — <c>ContentDialogLive_Rerender_TextAdvanced_#1069</c>,
    /// <c>..._#948</c>, <c>..._#246</c> — so on those lines the first <c>#</c> belongs to the
    /// <i>name</i>. Anchoring on it leaves <c>1069 # SKIP …</c> as the candidate directive, which
    /// fails the match and drops the line into the ordinary-pass arm: issue #1061 restored in full,
    /// and precisely for the checks most likely to carry an issue number, because those are the
    /// ones already known to be problematic. The scan makes the name's own hashes inert.</para>
    /// </summary>
    internal static bool TryParseSkipDirective(string afterOk, out string name, out string reason)
    {
        name = "";
        reason = "";

        for (var hash = afterOk.IndexOf('#'); hash >= 0; hash = afterOk.IndexOf('#', hash + 1))
        {
            var directive = afterOk[(hash + 1)..].TrimStart();
            if (!directive.StartsWith("SKIP", StringComparison.OrdinalIgnoreCase)) continue;

            // Guard against a name that merely starts with "skip..." — the directive is the bare
            // word. `continue` rather than `return false`: a later hash may still be the real
            // directive, e.g. `Foo_#SKIPPABLE # SKIP reason`.
            var afterWord = directive[4..];
            if (afterWord.Length > 0 && !char.IsWhiteSpace(afterWord[0]) && afterWord[0] != ':')
                continue;

            name = afterOk[..hash].Trim();
            if (name.Length == 0) continue;

            reason = afterWord.TrimStart(' ', ':', '\t');
            if (reason.Length == 0) reason = "(no reason given)";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Recognises the runner's <c>&lt;index&gt; &lt;fixtureName&gt;</c> head and returns the
    /// fixture name. Same numeric-index discriminator <see cref="TryParseRunnerLevelFailure"/>
    /// uses: <c>Harness</c> check names are C# identifiers, so a leading all-digits token can only
    /// have come from the runner.
    /// </summary>
    internal static bool TryParseRunnerLevelName(string head, out string fixtureName)
    {
        fixtureName = "";
        var firstSpace = head.IndexOf(' ');
        if (firstSpace <= 0) return false;
        if (!head[..firstSpace].All(char.IsDigit)) return false;

        var name = head[(firstSpace + 1)..].Trim();
        if (name.Length == 0) return false;

        fixtureName = StripRunnerFailureSuffix(name);
        return true;
    }

    private static bool TryParseRunnerLevelFailure(string rest, out string fixtureName, out string detail)
    {
        // Runner-level format: "<digits> <fixtureName>[_CRASH] - <detail>"
        fixtureName = "";
        detail = "";
        var firstSpace = rest.IndexOf(' ');
        if (firstSpace <= 0) return false;
        var head = rest[..firstSpace];
        if (!head.All(char.IsDigit)) return false;

        var tail = rest[(firstSpace + 1)..].TrimStart();
        var dashIdx = tail.IndexOf(" - ");
        string namePart;
        if (dashIdx >= 0)
        {
            namePart = tail[..dashIdx].Trim();
            detail = tail[(dashIdx + 3)..].Trim();
        }
        else
        {
            namePart = tail.Trim();
            detail = "(no detail)";
        }

        if (namePart.Length == 0) return false;
        fixtureName = StripRunnerFailureSuffix(namePart);
        return true;
    }

    private static string StripRunnerFailureSuffix(string namePart)
    {
        string[] suffixes = ["_CRASH", "_TIMEOUT"];
        foreach (var suffix in suffixes)
        {
            if (namePart.EndsWith(suffix, StringComparison.Ordinal))
                return namePart[..^suffix.Length];
        }

        return namePart;
    }

    private static void MarkEarlyAbortIfNeeded(int exitCode, TapParseResult tap)
    {
        if (_abortedReason is not null || exitCode == 0 && tap.SawTotalFailures)
            return;

        var fixtureNames = FixtureNames.Value;
        var firstMissingIndex = Array.FindIndex(fixtureNames, name => !_byFixture.ContainsKey(name));
        if (firstMissingIndex < 0)
            return;

        var hasReportedAfterMissing = fixtureNames
            .Skip(firstMissingIndex + 1)
            .Any(name => _byFixture.ContainsKey(name));
        if (hasReportedAfterMissing)
            return;

        var attributed = tap.LastRunningFixture;
        if (attributed is not null)
        {
            // Overwrite anything that is not already a Failed verdict. A Skipped fixture reached
            // here means the run died right after a fixture that established nothing — the silent
            // death is the more important fact and must not be masked by the skip.
            if (!_byFixture.TryGetValue(attributed, out var existing)
                || existing.Status != FixtureStatus.Failed)
            {
                _byFixture[attributed] = new FixtureOutcome(FixtureStatus.Failed, DescribeSilentDeath(
                    attributed, exitCode, tap.SawTotalFailures, ElapsedSeconds,
                    SelfTestTimeoutMs, Tail(_fullOutput, 4000)));
            }

            _abortedReason = SilentDeathAbortReason(attributed, tap.SawTotalFailures);
        }
        else
        {
            _abortedReason = EarlyAbortReason(fixtureNames[firstMissingIndex], tap.SawTotalFailures);
        }
    }

    // Appended to the early-abort reasons, which are stamped verbatim onto every unexecuted
    // fixture. The two prefixes ("Run aborted after/before fixture '<name>'") are unchanged, so
    // greps anchored to THOSE keep working — and both are pinned by tests, so this stays true.
    //
    // That is deliberately not a general compatibility promise, because one string did not
    // survive: main emitted "timed out after 300000ms with this fixture in flight", and a saved
    // grep for it returns zero here. Zero reads as "no budget kill happened" — the reassuring
    // direction, and therefore the dangerous one. The wording had to go (it asserts the positional
    // attribution this change exists to retire, and the budget is no longer a fixed 300000), so
    // anchor triage to TrailerDiscriminator, which reports what the Host got to do rather than how
    // the harness worded it. Bounded, though: it says what the Host got to do, not WHICH BINARY
    // did it — a stale binary run under --no-build after a failed build emits an identical
    // trailer. See TESTING.md for the fail-closed build step.
    private static string TrailerDiscriminator(bool sawTotalFailures) =>
        sawTotalFailures
            ? " (Host finished its run, then exited abnormally)"
            : " (Host died mid-run with no '# Total failures:' trailer — NOT a budget kill; issue #978)";

    internal static string SilentDeathAbortReason(string fixture, bool sawTotalFailures) =>
        $"Run aborted after fixture '{fixture}'{TrailerDiscriminator(sawTotalFailures)}";

    internal static string EarlyAbortReason(string fixture, bool sawTotalFailures) =>
        $"Run aborted before fixture '{fixture}'{TrailerDiscriminator(sawTotalFailures)}";

    /// <summary>
    /// Wording for a Host that stopped without the wrapper's budget killing it.
    ///
    /// <para>From the outside this is nearly indistinguishable from the issue #988 budget kill:
    /// one arbitrary fixture blamed, everything downstream missing, and the victim moving between
    /// runs. That is exactly why the two get conflated — issue #978 is this failure, and it was
    /// initially read as #988. Raising the budget does nothing for it.</para>
    ///
    /// <para>The discriminator is the <c># Total failures:</c> trailer, and it is the reliable one
    /// because it states what the Host <i>got to do</i> rather than how long it took: a budget kill
    /// interrupts a Host that would have printed it, whereas a Host that dies mid-fixture never
    /// reaches it. Elapsed time corroborates but cannot decide on its own — a slow machine and a
    /// fast crash produce overlapping durations — so the timing sentence below is hedged when the
    /// run did land near the cap.</para>
    ///
    /// <para>The trailer decides the <i>whole</i> message, not one line of it. A Host that printed
    /// the trailer did reach the end of its run, so a headline saying it stopped mid-run — or a
    /// remaining-fixtures line saying they were never run — contradicts the diagnosis directly
    /// underneath it and points triage at a mid-run crash that did not happen.</para>
    /// </summary>
    internal static string DescribeSilentDeath(
        string fixture, int exitCode, bool sawTotalFailures, double elapsedSeconds, int budgetMs, string tail)
    {
        var budgetSeconds = budgetMs / 1000.0;
        var fractionOfBudget = budgetSeconds > 0 ? elapsedSeconds / budgetSeconds : 1.0;

        var timing = fractionOfBudget < 0.75
            ? "— the run ended well short of the cap, so nothing was killed for running long"
            : "— close enough to the cap that timing alone cannot rule out a budget interaction; " +
              "the trailer line above is the reliable signal";

        var (headline, trailer, remaining, advice) = sawTotalFailures
            ? ("SELFTEST HOST FINISHED ITS RUN, THEN EXITED ABNORMALLY — NOT a suite-budget kill.",

               "The Host DID print its `# Total failures:` trailer, so it reached the end of its " +
               "run. Whatever went wrong happened after the fixtures — or the names below were " +
               "never scheduled at all.",

               "reported Skipped (Assert.Inconclusive). The Host ran to completion without ever " +
               "naming them, so the likely cause is two-place fixture registration: a name that " +
               "`--list-fixtures` reports but the run's `Create()` switch does not produce.",

               $"Start at teardown and at the fixture registry, not at '{fixture}' — it is named " +
               "here only because it started last.")

            : ("SELFTEST HOST STOPPED MID-RUN — this is NOT a suite-budget kill.",

               "The Host never printed its `# Total failures:` trailer, so it did not reach the " +
               "end of its run: it stopped mid-fixture. That is the discriminator against issue " +
               "#988, whose kills always leave one.",

               "reported Skipped (Assert.Inconclusive) — never RUN",

               $"Because the victim is positional here too, do not start by debugging '{fixture}'. " +
               "A native crash in the Host is the usual cause and the exit-code line above is the " +
               "first thing to read; issue #978 tracks this failure.");

        return $"{headline}\n" +
               $"'{fixture}' was the last fixture to start. {DescribeExitCode(exitCode)}\n" +
               $"  trailer   : {trailer}\n" +
               $"  elapsed   : {Fixed1(elapsedSeconds)}s against a {Fixed0(budgetSeconds)}s budget " +
               $"{timing}\n" +
               $"  remaining : {remaining}\n" +
               $"{advice}\n" +
               $"--- tail of full output ---\n{tail}";
    }

    public static IEnumerable<object[]> AllFixtures => FixtureNames.Value.Select(n => new object[] { n });

    [TestMethod]
    [DynamicData(nameof(AllFixtures))]
    public void Fixture(string name)
    {
        Assert.IsTrue(_initialized, "Self-test batch did not run.");
        if (_initError is not null)
            Assert.Fail(_initError);

        if (!_byFixture.TryGetValue(name, out var result))
        {
            if (_abortedReason is not null)
                Assert.Inconclusive(
                    $"{_abortedReason}; fixture '{name}' was not executed — this result carries NO " +
                    $"information about '{name}' itself. Read the abort reason above: it names " +
                    $"which of the four abort paths fired, and whether the fixture blamed by the " +
                    $"run was the cause or merely in flight.");
            Assert.Fail($"Fixture '{name}' was not reported by the Host. Full output:\n{_fullOutput}");
        }

        // Reported as an MSTest skip, not a pass. `Assert.Inconclusive` is the only verdict that
        // is visible without being red: `dotnet test` prints `Skipped <FixtureName>` and the run
        // stays green, so a machine that legitimately cannot observe the precondition does not
        // manufacture a failure — while a reader can no longer mistake it for a fixture that ran.
        if (result.Status == FixtureStatus.Skipped)
            Assert.Inconclusive(result.Detail);

        if (result.Status == FixtureStatus.Failed)
            Assert.Fail(result.Detail);
    }

    /// <summary>
    /// What the run's skip directives added up to: how many fixtures established nothing, how many
    /// were merely missing a leg, and the inventory behind both numbers.
    /// </summary>
    internal sealed record SkipInventory(
        IReadOnlyList<string> FullySkippedFixtures,
        IReadOnlyList<string> PartiallySkippedFixtures,
        int TotalSkippedChecks,
        string Text);

    /// <summary>Entries listed in full before the report elides the tail.</summary>
    private const int MaxListedSkips = 25;

    /// <summary>
    /// Folds the per-fixture verdicts into the run-level skip inventory.
    ///
    /// <para>Both halves are reported, and they mean different things. A <b>fully</b> skipped
    /// fixture ran no assertions at all, so the run establishes nothing about it — that is issue
    /// #1061's subject and it changes the fixture's verdict. A <b>partially</b> skipped fixture is
    /// legitimately green; listing it changes no verdict, but the skipped leg is a real gap the
    /// TAP stream carried and the consumer used to throw away, and a gap nobody can see is a gap
    /// nobody closes.</para>
    /// </summary>
    internal static SkipInventory BuildSkipInventory(IReadOnlyDictionary<string, FixtureOutcome> byFixture)
    {
        var fully = new List<string>();
        var partial = new List<string>();
        var totalChecks = 0;

        foreach (var (fixture, outcome) in byFixture.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (outcome.SkippedChecks.Count == 0) continue;
            totalChecks += outcome.SkippedChecks.Count;

            var entry = $"`{fixture}` — {string.Join("; ", outcome.SkippedChecks)}";
            if (outcome.Status == FixtureStatus.Skipped) fully.Add(entry);
            else partial.Add(entry);
        }

        var text = fully.Count == 0 && partial.Count == 0
            ? "No checks were skipped in this run."
            : $"{fully.Count} fixture(s) ran NO assertions (every check they own was skipped); " +
              $"{partial.Count} more skipped at least one check. " +
              $"{totalChecks} skipped check(s) in total.";

        return new SkipInventory(fully, partial, totalChecks, text);
    }

    /// <summary>The skip inventory's markdown, and whether it landed on the job summary.</summary>
    internal sealed record SkipReport(SkipInventory Inventory, string Markdown, bool Delivered);

    /// <summary>
    /// Builds the job-summary block for the run's skips and delivers it.
    ///
    /// <para>Composition and delivery live together for the same reason
    /// <see cref="PublishSuiteDuration"/> does: delivery is the load-bearing half. An
    /// <c>Assert.Inconclusive</c> message never reaches the Actions log — the tests run in a child
    /// <c>testhost</c> whose stdout the runner does not forward, so all CI shows is the bare line
    /// <c>Skipped &lt;FixtureName&gt;</c> with no reason attached. The job summary is plain file
    /// I/O performed by this process, so it is the only channel that actually renders the
    /// <i>why</i>.</para>
    /// </summary>
    internal static SkipReport PublishSkipReport(
        IReadOnlyDictionary<string, FixtureOutcome> byFixture, string? summaryPath)
    {
        var inventory = BuildSkipInventory(byFixture);

        var icon = inventory.FullySkippedFixtures.Count > 0 ? "⚠️" : "ℹ️";
        var body = new global::System.Text.StringBuilder()
            .Append($"### {icon} Selftest skipped checks\n\n")
            .Append(inventory.Text)
            .Append("\n\n");

        AppendList(body, "Fully skipped — these establish nothing either way",
            inventory.FullySkippedFixtures);
        AppendList(body, "Partially skipped — passed on their remaining checks",
            inventory.PartiallySkippedFixtures);

        body.Append("<sub>A `# SKIP` directive is a fixture reporting that it could not observe " +
                    "its precondition. Background: issue #1061.</sub>");

        var markdown = body.ToString();
        return new SkipReport(inventory, markdown, TryAppendSummary(summaryPath, markdown));

        static void AppendList(global::System.Text.StringBuilder sb, string heading, IReadOnlyList<string> entries)
        {
            if (entries.Count == 0) return;
            sb.Append($"**{heading}:**\n\n");
            foreach (var entry in entries.Take(MaxListedSkips))
                sb.Append($"- {entry}\n");
            if (entries.Count > MaxListedSkips)
                sb.Append($"- …and {entries.Count - MaxListedSkips} more\n");
            sb.Append('\n');
        }
    }

    /// <summary>
    /// Publishes the run's skip inventory, and reports a fixture that established nothing as a
    /// skip rather than letting it pass unremarked.
    ///
    /// <para>Deliberately <c>Inconclusive</c> and not <c>Fail</c>, because most skips mark a
    /// question this machine cannot answer — <c>GetCursorPos</c> returning <c>ACCESS_DENIED</c> on
    /// a non-interactive desktop, or a Windows 10 box where the DWM corner attribute is not
    /// round-trippable. Failing those would make the suite red on exactly the machines the skips
    /// were introduced to accommodate, which is the regression the skips fixed.</para>
    ///
    /// <para>But <b>not every skip is benign</b>: some mark a tracked product gap (see
    /// <c>Harness.Skip</c> — e.g. <c>"issue #942 - decorator retags the target"</c>), where the
    /// product really is broken and the skip is a referenced deferral. That is why the reasons are
    /// reproduced verbatim below rather than summarised into a count: a reader has to be able to
    /// tell the two apart, and only the reason string can tell them.</para>
    /// </summary>
    [TestMethod]
    public void SkippedFixtures_AreReported()
    {
        Assert.IsTrue(_initialized, "Self-test batch did not run.");
        if (_initError is not null)
            Assert.Fail(_initError);

        if (_timedOut || _abortedReason is not null)
        {
            Assert.Inconclusive(
                $"{_abortedReason ?? "The suite was killed by its process budget"}; the skip " +
                $"inventory is not meaningful for a run that did not complete, because every " +
                $"fixture downstream of the abort is missing rather than skipped.");
        }

        var report = PublishSkipReport(_byFixture, Environment.GetEnvironmentVariable(StepSummaryEnvVar));

        // Kept for local vstest/IDE runs, where testhost stdout does show up.
        Console.WriteLine(report.Inventory.Text);
        foreach (var entry in report.Inventory.FullySkippedFixtures)
            Console.WriteLine($"  fully skipped: {entry}");

        // Same reasoning as the delivery check in SuiteDuration_WithinBudget, and the same failure
        // mode: under `dotnet test` the Inconclusive message below never reaches the log, because
        // the testhost's stdout is not forwarded. The step summary is therefore the ONLY channel
        // that renders this inventory, so a dead channel leaves the skip report silently
        // undelivered while everything else stays green — the suite passes, the report is composed,
        // and nobody learns which fixtures asserted nothing.
        var summaryPath = Environment.GetEnvironmentVariable(StepSummaryEnvVar);
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(summaryPath),
                $"{StepSummaryEnvVar} is unset on a GitHub Actions runner, so the skip inventory " +
                $"had nowhere to go and the delivery check below silently skipped. Without this " +
                $"the check cannot come out the other way, and a check that cannot fail is the " +
                $"instrument bug issue #1061 was.");
        }

        if (!string.IsNullOrWhiteSpace(summaryPath))
        {
            Assert.IsTrue(report.Delivered,
                $"The skip inventory could not be written to {StepSummaryEnvVar} " +
                $"('{summaryPath}'). This is a fault in the reporting channel, not in the suite: " +
                $"the run itself is fine, but the only surface that names the skipped fixtures is " +
                $"now silent.");
        }

        // The positive control. Everything else in this file feeds ParseTap a fabricated string,
        // which proves the parser but not the Host: the decision "this fixture asserted nothing"
        // is made in SelfTestRunner, in a project no test can reference
        // (ReferenceOutputAssembly=false), so a fabricated stream cannot reach it. This fixture
        // exists to be fully skipped, so its presence here is the one end-to-end proof that the
        // Host still classifies, the `# SKIP` literal still matches across the duplicated
        // boundary, and the wrapper still reports SKIPPED instead of green.
        //
        // It fails rather than warns because the regression it guards is silent by construction:
        // if the chain breaks, this fixture goes GREEN, the suite stays green, and issue #1061 is
        // back with nothing to show for it. Without this assertion the whole skip pipeline could
        // rot while every test above it stayed passing — a check that cannot come out the other
        // way, which is the exact instrument bug this work is about.
        var controlSeen = report.Inventory.FullySkippedFixtures
            .Any(e => e.Contains(SkipVerdictControlFixture, StringComparison.Ordinal));

        Assert.IsTrue(controlSeen,
            $"The positive control '{SkipVerdictControlFixture}' was not reported as a fully " +
            $"skipped fixture. It asserts nothing on purpose, so it must land here on every run. " +
            $"Something in the chain is broken, and the failure is silent everywhere else:\n" +
            $"  (a) The fixture was deleted, renamed, or given a real H.Check — restore it; its " +
            $"amber IS the healthy result (see SkipVerdictPositiveControlFixture.cs).\n" +
            $"  (b) SelfTestRunner stopped classifying a zero-assertion fixture as skipped, so " +
            $"fully-skipped fixtures are reported PASSED again — that is issue #1061 exactly.\n" +
            $"  (c) The '# SKIP' literal drifted between Harness.Skip and TryParseSkipDirective. " +
            $"The Host is referenced with ReferenceOutputAssembly=false, so it is duplicated, not " +
            $"shared, and nothing but this assertion compares the two.\n" +
            $"Fixtures reported fully skipped this run: " +
            $"{(report.Inventory.FullySkippedFixtures.Count == 0 ? "(none)" : string.Join(", ", report.Inventory.FullySkippedFixtures))}");

        // Report only the *unexpected* skips. The control is always here, so folding it in would
        // make this permanently Inconclusive and drown the signal it exists to protect.
        var unexpected = report.Inventory.FullySkippedFixtures
            .Where(e => !e.Contains(SkipVerdictControlFixture, StringComparison.Ordinal))
            .ToArray();

        if (unexpected.Length > 0)
        {
            Assert.Inconclusive(
                $"{report.Inventory.Text}\n" +
                $"Fully skipped (excluding the '{SkipVerdictControlFixture}' control):\n  " +
                $"{string.Join("\n  ", unexpected)}\n" +
                $"Each of those is reported Skipped individually too. They are NOT automatically " +
                $"failures — but they are not passes either: read the per-fixture reason, because " +
                $"a skip can mean an undeterminable precondition, coverage owned by the E2E tier, " +
                $"or a tracked product gap where the product really is broken.");
        }
    }

    /// <summary>
    /// Name of the fixture that exists purely to be fully skipped, so the SKIPPED verdict has an
    /// end-to-end positive control. Duplicated from
    /// <c>SkipVerdictPositiveControl.FixtureName</c> in the Host, which cannot be referenced from
    /// here; <see cref="SkippedFixtures_AreReported"/> is what catches the two drifting apart.
    /// </summary>
    internal const string SkipVerdictControlFixture = "SelfTestVerdict_OnlySkips_PositiveControl";

    /// <summary>
    /// The one assertion that observes the <b>real</b> Host's real skip output, and the only thing
    /// that can catch the two projects drifting apart.
    ///
    /// <para>Every other test of the skip pipeline feeds <see cref="ParseTap"/> a fabricated
    /// string, so if <c>Harness.Skip</c> stopped emitting the <c># SKIP</c> directive — or the
    /// literal changed on one side only, which it can, because the Host is referenced with
    /// <c>ReferenceOutputAssembly=false</c> and the token is duplicated rather than shared — every
    /// one of those tests would stay green while the wrapper quietly went back to reporting
    /// fully-skipped fixtures as PASSED. That is issue #1061 restored in full, with a completely
    /// green suite. The same reasoning, and the same shape, as the
    /// <c>'# Suite elapsed: '</c> guard in <see cref="SuiteDuration_WithinBudget"/>.</para>
    ///
    /// <para>Non-vacuous by design, not by luck: <c>SelfTestVerdict_OnlySkips_PositiveControl</c>
    /// exists to be fully skipped, so every full run emits at least one directive regardless of
    /// the machine. It previously leaned on <c>Spec047EventStateSplitFixtures</c> skipping
    /// unconditionally, which was true but incidental — that fixture's skip could be closed at any
    /// time by work that had no idea this guard depended on it.</para>
    /// </summary>
    [TestMethod]
    public void SkipDirectives_SurviveIntoTheReport()
    {
        Assert.IsTrue(_initialized, "Self-test batch did not run.");
        if (_initError is not null)
            Assert.Fail(_initError);

        // An aborted run is truncated by definition, so "no skips seen" would mean "the run
        // stopped before reaching one" rather than "the channel is broken". Reporting it would
        // manufacture a second failure on top of the abort, blaming the wrong thing.
        if (_timedOut || _abortedReason is not null)
        {
            Assert.Inconclusive(
                $"{_abortedReason ?? "The suite was killed by its process budget"}; a truncated " +
                $"run cannot establish whether skip directives still reach the parser.");
        }

        var inventory = BuildSkipInventory(_byFixture);

        Assert.IsTrue(inventory.TotalSkippedChecks > 0,
            "The Host completed its run but the parser saw ZERO '# SKIP' directives. That should " +
            "be impossible: " + SkipVerdictControlFixture + " exists to emit one on every run. " +
            "Two explanations, and they need different fixes:\n" +
            "  (a) DRIFT — Harness.Skip stopped emitting 'ok <name> # SKIP <reason>', or " +
            "SelfTestBatch.TryParseSkipDirective stopped recognising it. Every fully-skipped " +
            "fixture is now silently reported PASSED again, which is issue #1061 exactly. Check " +
            "the literal on BOTH sides: the Host is referenced with ReferenceOutputAssembly=false, " +
            "so it is duplicated, not shared.\n" +
            "  (b) The positive-control fixture was deleted or de-registered. Restore it rather " +
            "than deleting this guard — it is the only thing keeping the check falsifiable.");
    }

    /// <summary>
    /// Reports the suite's wall clock every run, and warns — without failing — when it climbs past
    /// <see cref="SuiteDurationWarnSeconds"/>.
    ///
    /// <para>This exists because the failure it guards against is silent. Nothing measured suite
    /// duration before, so the margin against the process budget eroded run by run as fixtures
    /// were added (check counts moved 6090 → 6146 in a single day) until <c>main</c> itself
    /// breached the cap. The first visible symptom was an unrelated fixture failing on an
    /// unrelated PR. A number in the log every run makes that erosion observable while there is
    /// still headroom to act.</para>
    ///
    /// <para>Deliberately <c>Inconclusive</c> rather than <c>Fail</c>: duration depends on runner
    /// speed and contention, so a hard gate here would itself be a flake. A Skipped result cannot
    /// turn a slow runner into a red build, but it is conspicuous in the run summary.</para>
    ///
    /// <para><b>Why the number goes to a file and not just the console.</b> Measured, not assumed:
    /// under <c>dotnet test</c> the tests execute in a child <c>testhost</c> process whose stdout
    /// the runner does not forward. A probe writing to <c>Console.WriteLine</c> <i>and</i> straight
    /// to the process's standard-output handle produced neither marker in the run output at
    /// <c>console;verbosity=normal</c>, and the <c>Assert.Inconclusive</c> message did not appear
    /// either — only the bare line <c>Skipped SuiteDuration_WithinBudget</c>. So a
    /// <c>::warning::</c> workflow command emitted from here can never reach the Actions runner,
    /// and a gate whose report is invisible is the same silent erosion in a new costume.
    /// <c>GITHUB_STEP_SUMMARY</c> is plain file I/O performed by this process, so it is immune to
    /// whatever the runner does with stdout, and it renders on the run page. The console lines are
    /// kept for local <c>vstest</c>/IDE runs, where they do show up.</para>
    /// </summary>
    [TestMethod]
    public void SuiteDuration_WithinBudget()
    {
        Assert.IsTrue(_initialized, "Self-test batch did not run.");
        if (_initError is not null)
            Assert.Fail(_initError);

        var summaryPath = Environment.GetEnvironmentVariable(StepSummaryEnvVar);

        if (_timedOut)
        {
            // Elapsed == budget by construction here; the overrun is already reported against the
            // in-flight fixture, and repeating it as a duration warning would add nothing.
            PublishBestEffort(
                () => PublishBudgetKill(SelfTestTimeoutMs / 1000, summaryPath),
                "the budget-kill summary");

            Assert.Inconclusive(
                $"Suite hit its {SelfTestTimeoutMs / 1000}s hard budget and was killed; see the " +
                $"budget-overrun failure for the attribution caveat.");
        }

        if (_abortedReason is not null)
        {
            Assert.Inconclusive(
                $"{_abortedReason}; suite duration is not meaningful for a run that did not " +
                $"complete.");
        }

        // Non-vacuity guard for the Host's own instrumentation. Every test that exercises
        // ExtractSuiteElapsedSeconds feeds it a fabricated string, so if SelfTestRunner stopped
        // emitting the trailer — or the marker literals in the two projects drifted apart, which
        // they can, because the Host is referenced with ReferenceOutputAssembly=false and the
        // constant is duplicated rather than shared — the parser tests would all stay green while
        // this report silently began measuring wrapper overhead instead of the suite. This is the
        // only assertion in the codebase that observes the real Host's real output, so it is the
        // only place that drift can be caught.
        Assert.IsNotNull(_hostElapsedSeconds,
            "The Host completed its run but emitted no '# Suite elapsed: ' trailer, so suite " +
            "duration fell back to the wrapper's clock (which includes process start and pipe " +
            "drain). Check SelfTestRunner still writes SuiteElapsedMarker and that the duplicated " +
            "marker literals in the two projects still match.");

        var report = PublishSuiteDuration(
            ElapsedSeconds, SuiteDurationWarnSeconds, SelfTestTimeoutMs / 1000,
            // Unconditional, and deliberately so: ElapsedSeconds falls back to the wrapper clock
            // only when _hostElapsedSeconds is null, which the assertion directly above has just
            // ruled out. A ternary here reads as though this path can report wrapper timing, which
            // it cannot. If that assertion is ever relaxed, this label has to become conditional
            // again — the two are one decision, not two.
            "Host-reported",
            summaryPath);

        Console.WriteLine($"{report.Text} [{report.Source}]");

        // The only end-to-end proof that the reporting channel works. Everything else about it is
        // exercised against a temp file, which establishes that TryAppendSummary can write — not
        // that the runner's GITHUB_STEP_SUMMARY is reachable from inside the testhost. That is a
        // property of CI, so only CI can settle it, and this assertion is inert locally by
        // construction (the variable is unset, and a report with nowhere to go is correct there).
        //
        // It fails rather than warns because the failure it describes is silence. A dead channel
        // leaves every other signal green — the suite passes, the report is composed, nothing is
        // skipped — while the gate that exists to stop the budget eroding quietly stops reporting.
        // That is not a hypothetical failure mode; it is what #988 was.
        //
        // The env-var guard is what makes it inert locally, and it is also the one thing that could
        // make a green CI run meaningless: if the variable were ever unset on the runner, this would
        // pass by skipping rather than by succeeding, and the pass would look identical. So on CI
        // the premise is asserted too. Without this the check cannot come out the other way, and a
        // check that cannot fail is the instrument bug this whole PR is about.
        //
        // What this does not prove: that GITHUB_ACTIONS itself is set. Verification has to bottom
        // out somewhere, and this is a deliberate choice of where — one documented platform
        // invariant, rather than the two silently-skippable unknowns it replaced. Note the summary
        // file cannot be checked from a later step instead: GITHUB_STEP_SUMMARY is unique per step,
        // so a following step reads its own empty file, not this one's.
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(summaryPath),
                $"{StepSummaryEnvVar} is unset on a GitHub Actions runner, so the duration report " +
                $"had nowhere to go and the delivery check below silently skipped. The suite may " +
                $"be fine; the gate is not.");
        }

        if (!string.IsNullOrWhiteSpace(summaryPath))
        {
            Assert.IsTrue(report.Delivered,
                $"The suite-duration report could not be written to {StepSummaryEnvVar} " +
                $"('{summaryPath}'). This is a fault in the reporting channel, not in the suite: " +
                $"the run itself is fine, but the duration gate is now silent — which is the " +
                $"failure mode issue #988 was.");
        }

        if (report.Warn)
        {
            // Kept for local runs; under `dotnet test` this is swallowed with the rest of the
            // testhost's stdout, which is why the step summary above is the load-bearing channel.
            Console.WriteLine($"::warning title=Selftest suite duration::{report.Text.Replace("\n", " ")}");
            Assert.Inconclusive(report.Text);
        }
    }

    /// <summary>Environment variable naming the GitHub Actions job-summary file.</summary>
    internal const string StepSummaryEnvVar = "GITHUB_STEP_SUMMARY";

    /// <summary>The duration verdict, the markdown built from it, and whether that markdown landed.</summary>
    internal sealed record SuiteDurationReport(
        bool Warn, string Text, string Source, string Markdown, bool Delivered);

    /// <summary>
    /// Builds the job-summary block for a completed run and delivers it, reporting both the text
    /// and whether the write landed.
    ///
    /// <para>Composition and delivery live together, and production calls exactly this, because the
    /// delivery is the load-bearing half: this report is the mechanism that stops the budget
    /// eroding back to the state issue #988 describes, and a report nobody receives does not
    /// perform that job. Testing the formatter and the file-append separately would leave the join
    /// between them — the only part that can silently stop reporting — uncovered.</para>
    /// </summary>
    internal static SuiteDurationReport PublishSuiteDuration(
        double elapsedSeconds, int warnSeconds, int budgetSeconds, string source, string? summaryPath)
    {
        var (warn, text) = DescribeSuiteDuration(elapsedSeconds, warnSeconds, budgetSeconds);

        var markdown =
            $"### {(warn ? "⚠️" : "✅")} Selftest suite duration\n\n" +
            $"{text.Replace("\n", " ")}\n\n<sub>Source: {source}. Budget knob: " +
            $"`{TimeoutEnvVar}`. Background: issue #988.</sub>";

        return new SuiteDurationReport(warn, text, source, markdown, TryAppendSummary(summaryPath, markdown));
    }

    /// <summary>
    /// The job-summary block for a run the budget killed. Separate from the duration report because
    /// there is no meaningful duration to report — elapsed equals the budget by construction.
    /// </summary>
    internal static (string Markdown, bool Delivered) PublishBudgetKill(int budgetSeconds, string? summaryPath)
    {
        var markdown =
            $"### ❌ Selftest suite exceeded its {budgetSeconds}s hard budget\n\n" +
            $"The Host was killed. One fixture is reported failed, but that attribution is " +
            $"positional — see its message, and issue #988.";

        return (markdown, TryAppendSummary(summaryPath, markdown));
    }

    /// <summary>
    /// Appends to the job-summary file, reporting whether it landed. It does not throw for any
    /// failure <c>File.AppendAllText</c> documents, and reports success through its return value
    /// rather than an exception, so a caller on a failure path cannot be derailed by the
    /// diagnostics it is trying to emit. The catch is an explicit list rather than a blanket
    /// <c>Exception</c> on purpose: an exception type this method has no reason to see is a bug,
    /// and swallowing it here would hide it behind the very silence this gate exists to remove.
    /// Returns false (rather than throwing) when there is no summary file, which is the normal
    /// case locally.
    /// <para>
    /// Whether non-delivery is <em>fatal</em> is the caller's policy, not this method's, and the
    /// two callers deliberately differ:
    /// </para>
    /// <list type="bullet">
    /// <item><description><see cref="PublishSuiteDuration"/> — <b>asserted</b> by
    /// <see cref="SuiteDuration_WithinBudget"/> when the variable is set. Delivery *is* the gate;
    /// an undelivered duration report is a gate that has silently stopped gating, which is the
    /// failure mode issue #988 was.</description></item>
    /// <item><description><see cref="PublishBudgetKill"/> — <b>not asserted</b>; its result is
    /// deliberately discarded. That run is already failing for a reason the reader needs, and
    /// replacing it with a complaint about the summary file would bury the finding behind its
    /// own diagnostics.</description></item>
    /// </list>
    /// </summary>
    internal static bool TryAppendSummary(string? path, string markdown)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            File.AppendAllText(path, markdown + "\n\n");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or NotSupportedException or ArgumentException
                                      or System.Security.SecurityException)
        {
            Console.WriteLine($"Could not write the job summary ({ex.GetType().Name}: {ex.Message}).");
            return false;
        }
    }

    /// <summary>
    /// Runs a diagnostics publish whose failure must not displace the finding it accompanies.
    /// Returns whether the publish ran to completion.
    ///
    /// <para><see cref="TryAppendSummary"/> deliberately lets an undocumented exception type escape,
    /// so a genuine bug is not hidden behind the silence this gate exists to remove. That default is
    /// right for <see cref="PublishSuiteDuration"/>, where delivery <em>is</em> the gate. It is wrong
    /// for exactly one caller: the budget-kill path publishes <em>while already failing</em>, and its
    /// next statement is the <c>Assert.Inconclusive</c> carrying the attribution caveat. An exception
    /// escaping the publish would skip that statement and replace the finding with a complaint about
    /// the summary file — the outcome <see cref="PublishBudgetKill"/>'s contract explicitly rejects.
    /// Since non-delivery policy is the caller's, the widening lives here and not in
    /// <see cref="TryAppendSummary"/>, which keeps its narrow catch.</para>
    ///
    /// <para>The broad catch is the point, not an oversight. Narrowing it to
    /// <see cref="TryAppendSummary"/>'s list would make this method dead code: those types are
    /// already handled there and never reach here, so the filter would catch nothing that can
    /// occur, while the types that <em>can</em> escape — the ones this guard exists to stop —
    /// would still skip the caller's assertion. A filtered catch here reads safer and does
    /// strictly less.</para>
    /// </summary>
    internal static bool PublishBestEffort(Action publish, string label)
    {
        try
        {
            publish();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not publish {label} ({ex.GetType().Name}: {ex.Message}). The " +
                              "finding this accompanies is reported separately and is unaffected.");
            return false;
        }
    }

    /// <summary>
    /// Regression guard for issue #680. The full self-test suite used to fault
    /// with 0xC0000005 at *final process teardown*: <c>SelfTestRunner</c> called
    /// <see cref="System.Environment.Exit(int)"/> from inside the live WinUI
    /// desktop message loop, which jumps straight to <c>ExitProcess</c> and lets
    /// the Windows loader run Microsoft.UI.Xaml's TLS destructors while the
    /// suite's accumulated XAML object graph is still mounted — a
    /// <c>DependencyObject</c> destructor then dereferences the XAML core's
    /// already-freed tear-off bookkeeping map and access-violates.
    /// <para>
    /// The per-fixture <see cref="Fixture"/> tests can't catch this: every
    /// fixture has already emitted its TAP result before the teardown crash, so
    /// the run looks green even though the process exited with a crash code.
    /// This guard asserts the Host exited with one of the only two codes the
    /// runner legitimately produces — 0 (all passed) or 1 (fixture failures) —
    /// so a teardown access violation (a large negative exit code) fails CI.
    /// </para>
    /// </summary>
    [TestMethod]
    public void HostProcessExitsCleanly_NoTeardownCrash()
    {
        Assert.IsTrue(_initialized, "Self-test batch did not run.");
        if (_initError is not null)
            Assert.Fail(_initError);

        // Hang/timeout is surfaced per-fixture by the watchdog path and the
        // process is killed (exit code is not meaningful), so this teardown
        // guard only applies to a run that completed on its own.
        if (_timedOut || _abortedReason is not null)
            Assert.Inconclusive(_abortedReason ?? "Self-test process timed out; teardown exit code is not meaningful.");

        Assert.IsTrue(_exitCode is 0 or 1,
            $"Self-test Host exited ABNORMALLY — the runner only ever returns 0 (all passed) or " +
            $"1 (fixture failures), so any other code means the process did not exit through the " +
            $"runner. Do not assume teardown: the guard is reached whenever the run was not " +
            $"already attributed to a hang/timeout, which does not by itself distinguish a " +
            $"teardown fault from an earlier crash or an external termination. The line below " +
            $"classifies the code; the issue #680 final-exit access violation is one known cause, " +
            $"not the only one.\n" +
            $"{DescribeExitCode(_exitCode)}\n" +
            $"--- tail of full output ---\n{Tail(_fullOutput, 4000)}");
    }

    // -- Discovery: one-shot Host launch to list fixture names -----------------

    private static readonly Lazy<string[]> FixtureNames = new(LoadFixtureNames);

    private static string[] LoadFixtureNames()
    {
        var exe = FindHostExe();
        var (stdout, stderr, exitCode, timedOut) = RunProcess(exe, "--list-fixtures", ListFixturesTimeoutMs);

        if (timedOut)
            throw new TimeoutException($"`--list-fixtures` timed out after {ListFixturesTimeoutMs}ms. Host: {exe}");

        if (exitCode != 0)
            throw new InvalidOperationException(
                $"`--list-fixtures` failed with exit code {exitCode}.\nstdout:\n{stdout}\nstderr:\n{stderr}");

        var names = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();

        if (names.Length == 0)
            throw new InvalidOperationException(
                $"`--list-fixtures` returned no fixture names.\nstdout:\n{stdout}\nstderr:\n{stderr}");

        return names;
    }

    // -- Process runner: async reads + timeout race with kill ------------------

    private static (string Stdout, string Stderr, int ExitCode, bool TimedOut) RunProcess(
        string exe, string args, int timeoutMs)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {exe} {args}");

        // Read both streams concurrently so neither pipe can block the child by
        // filling its OS buffer.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var exitTask = process.WaitForExitAsync();
        var timeoutTask = Task.Delay(timeoutMs);

        var completed = Task.WhenAny(exitTask, timeoutTask).GetAwaiter().GetResult();
        var timedOut = completed != exitTask;

        if (timedOut)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* already exited */ }
            process.WaitForExit();
        }

        // At this point the process has exited; the stream tasks will complete.
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        return (stdout, stderr, timedOut ? -1 : process.ExitCode, timedOut);
    }

    private static string FindHostExe()
    {
        // Allow callers to point the harness at an AOT-published Host (which
        // lives under a `publish` directory, not the standard build output)
        // or any other custom build. This lets the same MSTest harness validate
        // the AOT binary that the developer is actually trying to ship.
        var overrideExe = Environment.GetEnvironmentVariable("REACTOR_SELFTEST_HOST_EXE");
        if (!string.IsNullOrWhiteSpace(overrideExe))
        {
            if (!File.Exists(overrideExe))
                throw new FileNotFoundException(
                    $"REACTOR_SELFTEST_HOST_EXE points at a path that does not exist: {overrideExe}");
            return overrideExe;
        }

        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Reactor.slnx")))
            dir = Path.GetDirectoryName(dir);

        if (dir == null)
            throw new DirectoryNotFoundException("Could not find repo root (Reactor.slnx)");

        var platform = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "ARM64",
            _ => "x64"
        };

        var exe = Path.Combine(dir, "tests", "Reactor.AppTests.Host", "bin", platform,
            "Debug", "net10.0-windows10.0.22621.0", "Reactor.AppTests.Host.exe");

        if (!File.Exists(exe))
            throw new FileNotFoundException($"Host app not built. Expected: {exe}");

        return exe;
    }
}
