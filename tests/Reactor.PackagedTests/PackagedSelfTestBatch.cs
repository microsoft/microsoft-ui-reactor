using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.PackagedTests;

/// <summary>
/// Runs the packaged selftest host under real MSIX package identity and reports one
/// <c>[TestMethod]</c> per fixture (issue #1148).
/// </summary>
/// <remarks>
/// <para>The unpackaged shim (<c>Reactor.SelfTests.SelfTestBatch</c>) and this one differ
/// in exactly one step: how the host process is started. There, <c>Process.Start</c> on
/// the build output; here, register the MSIX layout and <c>Process.Start</c> the
/// execution-alias stub. Both then read TAP off a redirected stdout pipe, so the whole
/// contract — <c>--self-test</c>, <c>--list-fixtures</c>, <c>--filter</c>, the
/// <c># Running:</c> markers, the exit code — is reused unchanged.</para>
/// <para><b>Fail, never skip.</b> A tier that quietly skips is indistinguishable from a
/// tier that passes, which is the failure mode issue #1148 exists to remove. Registration
/// failures, an unresolvable alias, missing TAP, a truncated stream, and an identity guard
/// that did not actually assert are all hard failures here.</para>
/// </remarks>
[TestClass]
public class PackagedSelfTestBatch
{
    /// <summary>Fixture whose failure means every other reading this run is meaningless.</summary>
    private const string IdentityGuardFixture = "Packaged_IdentityGuard";

    /// <summary>
    /// Naming contract for identity-dependent fixtures: it is what
    /// <see cref="IdentityDependentFixtures_Actually_Asserted"/> uses to decide which
    /// fixtures must never skip. A fixture gated on
    /// <c>PackagedIdentityFixtures.RequirePackagedTier</c> must be registered with this
    /// prefix.
    /// </summary>
    internal const string IdentityFixturePrefix = "Packaged_";

    private const int DefaultTimeoutSeconds = 900;

    private static readonly Dictionary<string, FixtureOutcome> _byFixture = new(StringComparer.Ordinal);
    private static IPackagedHostDeployment? _deployment;
    private static string _fullOutput = "";
    private static string? _initError;
    private static int _exitCode;

    /// <summary>Resolved once so discovery and execution can never disagree on the set.</summary>
    private static readonly Lazy<string[]> _fixtureNames = new(DiscoverFixtures);

    public static IEnumerable<object[]> AllFixtures =>
        _fixtureNames.Value.Select(n => new object[] { n });

    internal enum FixtureStatus { Passed, Failed, Skipped }

    internal sealed record FixtureOutcome(FixtureStatus Status, string Detail);

    // ────────────────────────────────────────────────────────────────────
    //  Run
    // ────────────────────────────────────────────────────────────────────

    [ClassInitialize]
    public static void RunPackagedSelfTests(TestContext context)
    {
        try
        {
            _deployment = new AppxLooseLayoutDeployment(AppxLooseLayoutDeployment.ResolveLayoutDirectory());
            var alias = _deployment.Register();

            var filter = ResolveFilter();
            var (stdout, stderr, exitCode, timedOut) = RunHost(alias, filter);
            _exitCode = exitCode;
            _fullOutput = CombineStreams(stdout, stderr);

            if (timedOut)
            {
                _initError =
                    $"The packaged host did not exit within {TimeoutMs / 1000}s.\n{Tail(_fullOutput, 4000)}";
                return;
            }

            ParseTap(stdout, _byFixture, _fixtureNames.Value);

            // Completeness, not mere presence. The host ends its run with a teardown-free
            // TerminateProcess (issue #680), so a truncated stream is a realistic failure
            // and a partial parse would report a subset of fixtures as green while
            // silently dropping the rest.
            if (!stdout.Contains("# Total failures:", StringComparison.Ordinal))
            {
                _initError =
                    "The packaged host produced no '# Total failures:' trailer, so it did not reach " +
                    $"the end of its run (exit {exitCode}). The TAP stream is truncated and cannot be " +
                    $"trusted.\n{Tail(_fullOutput, 4000)}";
                return;
            }

            if (_byFixture.Count == 0)
            {
                _initError =
                    $"The packaged host exited with code {exitCode} but produced no parsable TAP " +
                    $"output.\n{Tail(_fullOutput, 4000)}";
                return;
            }

            // A custom REACTOR_PACKAGED_FILTER can select a subset that excludes the identity
            // guard — `REACTOR_PACKAGED_FILTER=SettingsStore` is a reasonable thing to type.
            // The guard is what establishes that this run had package identity at all, so
            // rather than let the subset run unguarded (or fail for the guard's absence, which
            // would make the documented knob unusable) it is fetched in a second, cheap pass.
            if (!FilterSelects(filter, IdentityGuardFixture))
            {
                var guard = RunHost(alias, IdentityGuardFixture);
                var guardOutput = CombineStreams(guard.Stdout, guard.Stderr);
                _fullOutput += $"\n--- identity guard pass ---\n{guardOutput}";

                if (guard.TimedOut || !guard.Stdout.Contains("# Total failures:", StringComparison.Ordinal))
                {
                    _initError =
                        "The identity-guard pass did not complete, so nothing establishes that this " +
                        $"run had package identity.\n{Tail(guardOutput, 2000)}";
                    return;
                }

                ParseTap(guard.Stdout, _byFixture, new[] { IdentityGuardFixture });
                if (guard.ExitCode != 0) _exitCode = guard.ExitCode;
            }
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
               or COMException
               or IOException
               or UnauthorizedAccessException
               or System.ComponentModel.Win32Exception)
        {
            // Registration, alias-resolution and process-start failures land here. They are
            // recorded rather than thrown so every fixture test reports the same actionable
            // reason instead of MSTest collapsing the whole class into one opaque
            // initialization error. Scoped to the failures this path actually produces: an
            // unexpected type still propagates, so a defect in the harness cannot disguise
            // itself as "the packaged host would not start".
            _initError = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    [ClassCleanup]
    public static void RemovePackage() => _deployment?.Unregister();

    // ────────────────────────────────────────────────────────────────────
    //  Report
    // ────────────────────────────────────────────────────────────────────

    [TestMethod]
    [DynamicData(nameof(AllFixtures))]
    public void Fixture(string fixtureName)
    {
        FailIfNotInitialized();

        if (!_byFixture.TryGetValue(fixtureName, out var outcome))
            Assert.Fail($"'{fixtureName}' was not reported by the packaged host.\n{Tail(_fullOutput, 3000)}");

        switch (outcome.Status)
        {
            case FixtureStatus.Passed:
                return;
            case FixtureStatus.Skipped:
                Assert.Inconclusive($"{fixtureName} skipped: {outcome.Detail}");
                return;
            default:
                Assert.Fail($"{fixtureName} failed:\n{outcome.Detail}");
                return;
        }
    }

    /// <summary>
    /// The tier's own anti-vacuity gate.
    /// </summary>
    /// <remarks>
    /// Every identity-dependent fixture self-skips when it is not running in the packaged
    /// host, which is what lets the corpus stay shared between the two tiers. That skip is
    /// correct in the unpackaged tier and catastrophic here: if this tier ever launched the
    /// host without identity, the fixtures would skip, <c>Fixture</c> would report
    /// Inconclusive rather than failed, and the suite would look fine while measuring
    /// nothing. This asserts they actually ran assertions, so "the packaged tier did not run
    /// packaged" can only ever surface as a failure.
    /// <para>Checked across <b>every</b> <c>Packaged_</c> fixture rather than just the guard:
    /// those fixtures are structurally identity-dependent (they all gate on
    /// <c>RequirePackagedTier</c>), so a skip in the packaged host is always a bug, and
    /// pinning the assertion to one hardcoded name left every fixture added later
    /// unprotected.</para>
    /// </remarks>
    [TestMethod]
    public void IdentityDependentFixtures_Actually_Asserted()
    {
        FailIfNotInitialized();

        Assert.IsTrue(
            _byFixture.TryGetValue(IdentityGuardFixture, out var guard),
            $"'{IdentityGuardFixture}' did not run. Without it, nothing establishes that this " +
            "tier had package identity at all.");

        Assert.AreEqual(
            FixtureStatus.Passed, guard!.Status,
            $"'{IdentityGuardFixture}' did not pass ({guard.Status}), so nothing establishes that " +
            $"this run had MSIX package identity — every identity-dependent check in it is " +
            $"therefore vacuous. Most likely the host was started from its build output instead " +
            $"of through the '{AppxLooseLayoutDeployment.AliasExeName}' execution alias.\n" +
            guard.Detail);

        var skipped = _byFixture
            .Where(e => e.Key.StartsWith(IdentityFixturePrefix, StringComparison.Ordinal)
                     && e.Value.Status == FixtureStatus.Skipped)
            .Select(e => $"{e.Key} ({e.Value.Detail})")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.AreEqual(
            0, skipped.Length,
            $"These identity-dependent fixtures skipped inside the packaged tier, so they " +
            $"asserted nothing:\n  {string.Join("\n  ", skipped)}\n" +
            "A fixture gated on RequirePackagedTier must always run here — a skip means the " +
            "host lacked package identity or the gate itself is broken.");
    }

    /// <summary>
    /// Surfaces any fixture whose result was parsed but which has no <c>[DynamicData]</c> test
    /// case to report it.
    /// </summary>
    /// <remarks>
    /// Discovery builds <see cref="AllFixtures"/> from the filtered set, so a fixture fetched
    /// outside that set — today the identity guard's second pass under a narrow
    /// <c>REACTOR_PACKAGED_FILTER</c> — has no <c>Fixture</c> case of its own. Its failure
    /// would then be recorded in the map and never asserted on:
    /// <c>Host_Exit_Code_Agrees_With_Reported_Failures</c> returns early as soon as *any*
    /// parsed failure exists, so the run would go green on the strength of the very failure
    /// that should have sunk it. This is the backstop for that whole class, not just for the
    /// guard.
    /// </remarks>
    [TestMethod]
    public void Fixtures_Without_A_Test_Case_Must_Have_Passed()
    {
        FailIfNotInitialized();

        var covered = new HashSet<string>(_fixtureNames.Value, StringComparer.Ordinal);

        var unreported = _byFixture
            .Where(e => !covered.Contains(e.Key) && e.Value.Status != FixtureStatus.Passed)
            .Select(e => $"{e.Key} [{e.Value.Status}] {e.Value.Detail}")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.AreEqual(
            0, unreported.Length,
            "These fixtures ran but have no test case reporting them, and did not pass:\n  " +
            string.Join("\n  ", unreported));
    }

    /// <summary>Guards the exit-code contract, so a crash after the last fixture cannot pass.</summary>
    [TestMethod]
    public void Host_Exit_Code_Agrees_With_Reported_Failures()
    {
        FailIfNotInitialized();

        // Only stand down when the failures are ones a Fixture test case will actually report.
        // A failure recorded for a fixture outside the discovered set has no such case, so
        // returning early on it would let the exit code go unexamined too.
        var covered = new HashSet<string>(_fixtureNames.Value, StringComparer.Ordinal);
        var reportedFailure = _byFixture.Any(
            e => e.Value.Status == FixtureStatus.Failed && covered.Contains(e.Key));

        if (reportedFailure) return; // individual Fixture tests already report the detail.

        Assert.AreEqual(
            0, _exitCode,
            $"The packaged host reported no failing fixture but exited with code {_exitCode}, so it " +
            $"failed after its last fixture.\n{Tail(_fullOutput, 3000)}");
    }

    private static void FailIfNotInitialized()
    {
        if (_initError is not null) Assert.Fail(_initError);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Plumbing
    // ────────────────────────────────────────────────────────────────────

    private static string? ResolveFilter() =>
        ResolveFilter(Environment.GetEnvironmentVariable("REACTOR_PACKAGED_FILTER"));

    /// <summary>
    /// Resolves the fixture filter. <c>null</c> — the default — runs the whole corpus under
    /// package identity.
    /// </summary>
    /// <remarks>
    /// Running everything is the point of sharing the corpus with the unpackaged tier: a
    /// fixture only proves something about package identity if it actually runs there.
    /// Restricting the default to identity-named fixtures would have left the tier
    /// exercising three of them, which is indistinguishable from a tier that isn't pulling
    /// its weight. Measured cost of the full run is ~5 minutes, and it runs on its own
    /// runner in parallel with the unpackaged suite.
    /// <para>Takes the value explicitly rather than reading the environment so it is
    /// testable without mutating process state.</para>
    /// </remarks>
    internal static string? ResolveFilter(string? custom) =>
        string.IsNullOrWhiteSpace(custom) ? null : custom.Trim();

    private static int TimeoutMs =>
        ResolveTimeoutSeconds(Environment.GetEnvironmentVariable("REACTOR_PACKAGED_TIMEOUT_SECONDS")) * 1000;

    /// <summary>
    /// Resolves the process budget in seconds, falling back to the default for absent,
    /// unparseable, non-positive, or overflowing input.
    /// </summary>
    /// <remarks>
    /// The guards are not decorative: a malformed override that resolved to 0 or a negative
    /// value would make the run either kill the host instantly or throw
    /// <see cref="ArgumentOutOfRangeException"/> from the delay — recreating the class of
    /// failure issue #988 documents for the unpackaged tier, only faster. Mirrors
    /// <c>Reactor.SelfTests.SelfTestBatch.ResolveTimeoutSeconds</c>.
    /// </remarks>
    internal static int ResolveTimeoutSeconds(string? envValue) =>
        !string.IsNullOrWhiteSpace(envValue)
        && int.TryParse(envValue.Trim(), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var seconds)
        && seconds > 0
        && seconds <= int.MaxValue / 1000
            ? seconds
            : DefaultTimeoutSeconds;

    /// <summary>
    /// Enumerates fixtures by running the host's <c>--list-fixtures</c> fast path against
    /// the plain build-output executable.
    /// </summary>
    /// <remarks>
    /// Deliberately unpackaged: listing names neither needs nor consults package identity,
    /// and doing it here would otherwise force a package registration during MSTest's
    /// discovery phase. The filter is applied with the host's own semantics
    /// (case-insensitive substring, see <c>SelfTestRunner</c>) so the set enumerated here
    /// and the set the host runs cannot drift apart.
    /// </remarks>
    private static string[] DiscoverFixtures()
    {
        var layout = AppxLooseLayoutDeployment.ResolveLayoutDirectory();
        var exe = Path.Join(layout, "Reactor.PackagedTests.Host.exe");
        if (!File.Exists(exe))
        {
            throw new FileNotFoundException(
                $"Packaged host not built. Expected: {exe}\n{AppxLooseLayoutDeployment.BuildHint}");
        }

        var (stdout, stderr, exitCode, timedOut) = RunProcess(exe, "--list-fixtures", 60_000);

        // Discovery failures must be loud. A --list-fixtures run that printed some names and
        // then died would otherwise silently narrow the tier to whatever it managed to emit,
        // which is the "reports green while measuring nothing" outcome this tier exists to
        // prevent — reached before the empty-set guard below ever runs.
        if (timedOut)
            throw new InvalidOperationException($"--list-fixtures did not exit within 60s.\n{stderr}");

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"--list-fixtures exited with code {exitCode}, so the fixture list may be " +
                $"truncated.\nstderr: {stderr}");
        }

        var names = ParseFixtureList(stdout, ResolveFilter());

        if (names.Length == 0)
        {
            throw new InvalidOperationException(
                $"--list-fixtures returned no fixtures matching filter '{ResolveFilter()}'. A tier " +
                "with no tests would report green while measuring nothing.");
        }

        return names;
    }

    /// <summary>
    /// Filters a <c>--list-fixtures</c> stdout dump with the host's own matching semantics
    /// (case-insensitive substring, see <c>SelfTestRunner</c>).
    /// </summary>
    internal static string[] ParseFixtureList(string stdout, string? filter)
    {
        var names = stdout
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToArray();

        return filter is null
            ? names
            : names.Where(n => n.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    /// <summary>
    /// Splits the TAP stream on <c># Running: &lt;fixture&gt;</c> and reduces each fixture's
    /// checks to a verdict.
    /// </summary>
    /// <remarks>
    /// <para>Three states, not two: <c>Harness.Skip</c> emits a line beginning <c>ok </c>, so
    /// counting skips as passes would let a fixture whose every check was skipped report
    /// green having asserted nothing. That is the same trap issue #1061 documents for the
    /// unpackaged parser.</para>
    /// <para><paramref name="knownFixtures"/> enables runner-level attribution. The runner
    /// emits some lines <i>about</i> a fixture without a preceding <c># Running:</c> for it —
    /// a fixture it declined to start, or one it could not find. Charging those to whichever
    /// fixture happened to be current would blame an innocent neighbour and let the real one
    /// look untouched, so a line naming a known fixture is attributed to that fixture.</para>
    /// </remarks>
    internal static void ParseTap(
        string stdout,
        IDictionary<string, FixtureOutcome> byFixture,
        IReadOnlyCollection<string>? knownFixtures = null)
    {
        const string marker = "# Running: ";
        var known = knownFixtures is null
            ? null
            : new HashSet<string>(knownFixtures, StringComparer.Ordinal);

        string? current = null;
        var failures = new List<string>();
        var asserted = 0;
        var skipped = new List<string>();

        void Flush()
        {
            if (current is null) return;
            byFixture[current] = failures.Count > 0
                ? new FixtureOutcome(FixtureStatus.Failed, string.Join("\n", failures))
                : asserted > 0
                    ? new FixtureOutcome(FixtureStatus.Passed, "")
                    : new FixtureOutcome(
                        FixtureStatus.Skipped,
                        skipped.Count > 0 ? string.Join("; ", skipped) : "no checks ran");
        }

        foreach (var line in stdout.Split('\n').Select(static raw => raw.TrimEnd('\r')))
        {
            if (line.StartsWith(marker, StringComparison.Ordinal))
            {
                Flush();
                current = line[marker.Length..].Trim();
                failures = new List<string>();
                skipped = new List<string>();
                asserted = 0;
                continue;
            }

            var isFailure = line.StartsWith("not ok", StringComparison.Ordinal);
            var isOk = !isFailure && line.StartsWith("ok ", StringComparison.Ordinal);
            if (!isFailure && !isOk) continue;

            // Runner-level line naming a fixture other than the one in flight.
            if (known is not null &&
                TryAttributeToNamedFixture(line, current, known, isFailure, byFixture))
            {
                continue;
            }

            if (current is null) continue;

            if (isFailure)
            {
                failures.Add(line);
                asserted++;
            }
            else
            {
                // A SKIP directive is a non-measurement, so it must not count as an
                // assertion — otherwise a fully-skipped fixture reports Passed.
                if (line.Contains("# SKIP", StringComparison.Ordinal)) skipped.Add(line);
                else asserted++;
            }
        }

        Flush();
    }

    /// <summary>Fixture-level abort suffixes the runner appends to a TAP fixture name.</summary>
    private static readonly string[] AbortSuffixes = ["_CRASH", "_TIMEOUT"];

    /// <summary>
    /// Combines a host invocation's streams into the text every diagnostic quotes, so both
    /// invocations report identically.
    /// </summary>
    /// <remarks>
    /// stderr matters most exactly when stdout is useless: an activation or CLR failure
    /// produces no TAP at all, so stderr is the only actionable output there is.
    /// </remarks>
    internal static string CombineStreams(string stdout, string stderr) =>
        string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}\n--- stderr ---\n{stderr}";

    /// <summary>
    /// Recognises a runner-level TAP line that names a known fixture which is not the one
    /// currently in flight, and records that fixture's verdict directly.
    /// </summary>
    /// <returns><c>true</c> when the line was consumed.</returns>
    private static bool TryAttributeToNamedFixture(
        string line,
        string? current,
        HashSet<string> known,
        bool isFailure,
        IDictionary<string, FixtureOutcome> byFixture)
    {
        // Shapes: "ok <n> <Fixture> # SKIP <reason>" / "not ok <n> <Fixture>_CRASH - <detail>"
        // The leading token is "ok" or "not ok", optionally followed by a numeric index.
        var rest = line[(isFailure ? "not ok".Length : "ok".Length)..].TrimStart();
        var space = rest.IndexOf(' ');
        if (space > 0 && rest[..space].All(char.IsDigit)) rest = rest[(space + 1)..].TrimStart();

        var end = rest.IndexOf(' ');
        var token = (end < 0 ? rest : rest[..end]).Trim();
        if (token.Length == 0) return false;

        // The runner suffixes a fixture-level abort with _CRASH / _TIMEOUT.
        var abortSuffix = AbortSuffixes.FirstOrDefault(s => token.EndsWith(s, StringComparison.Ordinal));
        var name = abortSuffix is null ? token : token[..^abortSuffix.Length];

        if (!known.Contains(name) || string.Equals(name, current, StringComparison.Ordinal))
            return false;

        // Never downgrade a verdict already established by the fixture's own checks.
        if (byFixture.TryGetValue(name, out var existing) && existing.Status == FixtureStatus.Failed)
            return true;

        byFixture[name] = isFailure
            ? new FixtureOutcome(FixtureStatus.Failed, line)
            : new FixtureOutcome(FixtureStatus.Skipped, line);

        return true;
    }

    private static (string Stdout, string Stderr, int ExitCode, bool TimedOut) RunHost(
        string alias, string? filter)
    {
        var args = "--self-test";
        if (filter is not null) args += $" --filter {filter}";
        return RunProcess(alias, args, TimeoutMs);
    }

    /// <summary>
    /// Whether <paramref name="filter"/> selects <paramref name="fixtureName"/>, using the
    /// host's own matching semantics. A <c>null</c> filter runs the whole corpus.
    /// </summary>
    internal static bool FilterSelects(string? filter, string fixtureName) =>
        filter is null || fixtureName.Contains(filter, StringComparison.OrdinalIgnoreCase);

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

        // Drain both pipes concurrently so neither can block the child by filling its OS
        // buffer.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var exitTask = process.WaitForExitAsync();

        var completed = Task.WhenAny(exitTask, Task.Delay(timeoutMs)).GetAwaiter().GetResult();
        var timedOut = completed != exitTask;

        if (timedOut)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* already exited */ }
            process.WaitForExit();
        }

        return (stdoutTask.GetAwaiter().GetResult(),
                stderrTask.GetAwaiter().GetResult(),
                timedOut ? -1 : process.ExitCode,
                timedOut);
    }

    private static string Tail(string text, int max) =>
        text.Length <= max ? text : text[^max..];
}
