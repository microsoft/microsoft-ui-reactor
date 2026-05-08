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
    private const int SelfTestTimeoutMs = 300_000;   // 5 min
    private const int ListFixturesTimeoutMs = 30_000;

    // Per-fixture aggregated outcome, populated by ClassInitialize.
    // Key = fixture name; Value = (passed, joined failure reasons).
    private static readonly ConcurrentDictionary<string, (bool Passed, string Detail)> _byFixture = new();
    private static string _fullOutput = "";
    private static bool _initialized;
    private static string? _initError;

    [ClassInitialize]
    public static void RunSelfTests(TestContext context)
    {
        var exe = FindHostExe();
        var (stdout, stderr, exitCode, timedOut) = RunProcess(exe, "--self-test", SelfTestTimeoutMs);

        _fullOutput = stdout;
        if (!string.IsNullOrEmpty(stderr))
            _fullOutput += "\n--- stderr ---\n" + stderr;

        if (timedOut)
        {
            _initError = $"Self-test process timed out after {SelfTestTimeoutMs}ms.\n{_fullOutput}";
            _initialized = true;
            return;
        }

        ParseTap(stdout);
        _initialized = true;

        if (exitCode != 0 && _byFixture.IsEmpty)
            _initError = $"Self-test process exited with code {exitCode} but produced no parsable TAP output.\n{_fullOutput}";
    }

    private static void ParseTap(string stdout)
    {
        // Two TAP emitter sources:
        //   Harness check:   "ok <checkName>"  /  "not ok <checkName> - <reason>"
        //   SelfTestRunner:  "# Running: <fixtureName>"
        //                    "not ok <index> <fixtureName> - fixture not found"     (before any marker)
        //                    "not ok <index> <fixtureName>_CRASH - <type>: <msg>"   (after marker if RunAsync threw)
        //
        // Runner-level "not ok" lines start with a numeric test index; check-level lines do not.
        // Runner-level failures attribute to their own fixture name regardless of `current`.

        string? current = null;
        var failuresForCurrent = new List<string>();
        var sawChecksForCurrent = false;

        void Flush()
        {
            if (current is null) return;
            var passed = failuresForCurrent.Count == 0 && sawChecksForCurrent;
            var detail = failuresForCurrent.Count == 0
                ? (sawChecksForCurrent ? "" : "fixture emitted no TAP checks")
                : string.Join("\n", failuresForCurrent);
            _byFixture[current] = (passed, detail);
        }

        foreach (var raw in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.StartsWith("# Running: "))
            {
                Flush();
                current = line["# Running: ".Length..].Trim();
                failuresForCurrent = new List<string>();
                sawChecksForCurrent = false;
            }
            else if (line.StartsWith("ok "))
            {
                // Harness-level pass; ignore payload, just note that current saw checks.
                sawChecksForCurrent = true;
            }
            else if (line.StartsWith("not ok "))
            {
                var rest = line[7..].Trim();
                if (TryParseRunnerLevelFailure(rest, out var fixtureName, out var detail))
                {
                    // Runner-level failure — attribute directly to the fixture name,
                    // overriding any in-progress `current` bucket.
                    _byFixture[fixtureName] = (false, detail);
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
        fixtureName = namePart.EndsWith("_CRASH", StringComparison.Ordinal)
            ? namePart[..^"_CRASH".Length]
            : namePart;
        return true;
    }

    public static IEnumerable<object[]> AllFixtures => FixtureNames.Value.Select(n => new object[] { n });

    [DataTestMethod]
    [DynamicData(nameof(AllFixtures))]
    public void Fixture(string name)
    {
        Assert.IsTrue(_initialized, "Self-test batch did not run.");
        if (_initError is not null)
            Assert.Fail(_initError);

        if (!_byFixture.TryGetValue(name, out var result))
            Assert.Fail($"Fixture '{name}' was not reported by the Host. Full output:\n{_fullOutput}");

        if (!result.Passed)
            Assert.Fail(result.Detail);
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
