using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Runs the redirect regression suite for <c>docs/_site-root/404.html</c>.
///
/// That file is the only part of the published docs site that MkDocs never
/// sees: the publish workflow copies it straight into the Pages artifact, so
/// <c>mkdocs build --strict</c> cannot catch a mistake in it. Without this test
/// the branching inside it — the loop guard that stops a miss under
/// <c>latest/</c> redirecting to itself, and the query/anchor preservation that
/// keeps deep links like the one in README.md intact — would ship unverified.
///
/// The cases live in JavaScript next to the file they cover so a docs author can
/// run them with plain <c>node</c>; this test exists so CI runs them too. It is
/// wired into the <c>docs-build</c> job, which is armed whenever a non-Markdown
/// file changes, and both the HTML and the test script qualify.
/// </summary>
public sealed class SiteRootRedirectTests
{
    [Fact]
    public async Task Site_root_404_redirect_cases_pass()
    {
        var repoRoot = FindRepoRoot();
        var testScript = Path.Join(repoRoot, "docs", "_site-root", "404.redirect.test.js");

        Assert.True(
            File.Exists(testScript),
            $"Missing {testScript}. The published site's 404 redirect is unverified without it.");

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "node",
            ArgumentList = { testScript },
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

        var output = string.Join(
            Environment.NewLine,
            new[] { await stdoutTask, await stderrTask }.Where(s => !string.IsNullOrWhiteSpace(s)));

        // Fails rather than skips when node is absent. Every GitHub-hosted
        // runner ships node, and the repo already has npm projects, so a
        // missing interpreter is a broken environment — and a test that quietly
        // opts out when its fixture fails is a test that cannot fail.
        Assert.True(
            process.ExitCode == 0,
            $"docs/_site-root/404.redirect.test.js failed (exit {process.ExitCode}):{Environment.NewLine}{output}");
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
