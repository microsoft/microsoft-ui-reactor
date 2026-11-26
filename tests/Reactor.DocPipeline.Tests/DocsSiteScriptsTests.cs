using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Runs the regression suites for the docs site's own scripts:
/// <c>docs/_website/scripts/sync.mjs</c>, which turns <c>docs/guide</c> into the
/// Blume source tree, and <c>docs/_website/scripts/publish.mjs</c>, which writes
/// each version into <c>gh-pages</c> for <c>.github/workflows/docs.yml</c>.
///
/// The publisher runs only after a merge, so a mistake in it surfaces as a
/// broken or half-written <c>gh-pages</c> rather than as a red PR. The cases
/// live in JavaScript next to the scripts so a docs author can run them with
/// <c>npm run smoke</c>; this test exists so CI runs them on every PR too. They
/// inject a fake build, so they need <c>node</c> and <c>git</c> but not the
/// Blume install.
/// </summary>
public sealed class DocsSiteScriptsTests
{
    /// <summary>
    /// Floor for the number of cases the node suites must run, so they cannot be
    /// quietly reduced while the xUnit gate stays green. Raising it as cases are
    /// added is optional; lowering it is a decision.
    /// </summary>
    private const int MinimumCases = 50;

    /// <summary>
    /// Ceiling for the node subprocess. The suites run in a few seconds, so this
    /// only ever fires on a hang.
    /// </summary>
    private static readonly TimeSpan SubprocessTimeout = TimeSpan.FromMinutes(3);

    [Fact]
    public async Task Docs_site_script_cases_pass()
    {
        var repoRoot = FindRepoRoot();
        var testScripts = new[] { "sync.test.mjs", "publish.test.mjs", "redirect-pages.test.mjs" }
            .Select(name => Path.Join(repoRoot, "docs", "_website", "scripts", name))
            .ToArray();

        foreach (var script in testScripts)
        {
            Assert.True(File.Exists(script), $"Missing {script}. The docs publisher is unverified without it.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // TAP explicitly, because node's default reporter varies with the node
        // version and whether stdout is a TTY, and the floor below reads the
        // summary counters it emits.
        startInfo.ArgumentList.Add("--test");
        startInfo.ArgumentList.Add("--test-reporter=tap");
        foreach (var script in testScripts)
        {
            startInfo.ArgumentList.Add(script);
        }

        using var process = Process.Start(startInfo)!;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(SubprocessTimeout);
        var cancellationToken = timeout.Token;

        // Start both reads before awaiting exit: a child that fills one pipe
        // buffer blocks on the write while the parent waits for it to exit.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited between the timeout firing and the kill.
            }

            Assert.Fail(
                $"node did not finish within {SubprocessTimeout.TotalSeconds:0} seconds running the "
                    + "docs/_website script suites. A case is hanging.");
        }

        var stdout = await stdoutTask;
        var output = string.Join(
            Environment.NewLine,
            new[] { stdout, await stderrTask }.Where(s => !string.IsNullOrWhiteSpace(s)));

        // Fails rather than skips when node is absent: every GitHub-hosted
        // runner ships node, so a missing interpreter is a broken environment.
        Assert.True(
            process.ExitCode == 0,
            $"docs/_website/scripts tests failed (exit {process.ExitCode}):{Environment.NewLine}{output}");

        // Exit code zero is also what `node` returns for files that register no
        // tests, so assert a floor: growth is fine, silent shrinkage is not.
        var match = Regex.Match(stdout, @"^# pass (\d+)$", RegexOptions.Multiline);
        Assert.True(match.Success, $"No TAP pass count in the runner output:{Environment.NewLine}{output}");

        var passed = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.True(
            passed >= MinimumCases,
            $"Only {passed} cases ran, below the {MinimumCases} these suites are expected to carry. "
                + "If cases were deliberately removed or merged, lower "
                + $"{nameof(MinimumCases)} in this file with the reason in the commit message.");
    }

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            if (File.Exists(Path.Join(dir, "Reactor.slnx")) || Directory.Exists(Path.Join(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Reactor repo root not found from test cwd.");
    }
}
