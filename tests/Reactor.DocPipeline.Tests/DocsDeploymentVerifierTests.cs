using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Runs the regression suite for <c>.github/scripts/verify-docs-deployment.mjs</c>,
/// the gate that decides whether the live docs site is serving the artifact a
/// <c>Publish docs</c> run produced (issue #1268).
///
/// That script is the only thing in the pipeline that looks at the live site.
/// Everything else — the strict build, the mike push, the Pages deployment —
/// stayed green while a release was stranded, so a mistake in this one file is
/// a mistake in the only signal that can catch the bug it exists for.
///
/// The cases live in JavaScript next to the script so a release engineer can
/// run them with plain <c>node</c>; this test exists so CI runs them too. It is
/// wired into the <c>docs-build</c> job, which is armed whenever a non-Markdown
/// file changes — and both the script and its test script qualify.
/// </summary>
public sealed class DocsDeploymentVerifierTests
{
    [Fact]
    public async Task Deployment_verifier_cases_pass()
    {
        var repoRoot = FindRepoRoot();
        var testScript = Path.Join(repoRoot, ".github", "scripts", "verify-docs-deployment.test.mjs");

        Assert.True(
            File.Exists(testScript),
            $"Missing {testScript}. The docs-deployment gate is unverified without it.");

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "node",
            // TAP explicitly: node's default reporter varies with the node
            // version and whether stdout is a TTY, and the completeness check
            // below reads the summary counters it emits.
            ArgumentList = { "--test-reporter=tap", testScript },
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;

        var cancellationToken = TestContext.Current.CancellationToken;

        // Start both reads before awaiting exit: a child that fills one pipe
        // buffer blocks on the write while the parent waits for it to exit.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var output = string.Join(
            Environment.NewLine,
            new[] { stdout, await stderrTask }.Where(s => !string.IsNullOrWhiteSpace(s)));

        // Fails rather than skips when node is absent, for the same reason
        // SiteRootRedirectTests does: every GitHub-hosted runner ships node, so
        // a missing interpreter is a broken environment — and a test that
        // quietly opts out when its fixture fails is a test that cannot fail.
        Assert.True(
            process.ExitCode == 0,
            $".github/scripts/verify-docs-deployment.test.mjs failed (exit {process.ExitCode}):{Environment.NewLine}{output}");

        // An exit code of zero is also what `node` returns for a file that
        // registered no tests at all, so assert the suite actually ran. Without
        // this, renaming the cases out from under the runner would read green.
        Assert.Contains("# pass ", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("# pass 0", stdout, StringComparison.Ordinal);
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
