using System.Diagnostics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.AppTests.Tests;

[TestClass]
public sealed class Spec051DevtoolsPackageE2ETests
{
    private const string DevtoolsSwitchSnippet = "RuntimeHostConfigurationOption Include=\"Reactor.DevtoolsSupport\"";

    [TestMethod]
    [TestCategory("Spec051")]
    public async Task Spec051_DevtoolsCliFallback_EmitsActionableError()
    {
        var exe = FindPublishedExe(
            "REACTOR_SPEC051_SWITCH_OFF_EXE",
            Path.Combine("samples", "apps", "hello-world-aot"),
            "hello-world-aot.exe");

        using var process = StartProcess(exe, ["--devtools", "run"]);
        var stderrTask = process.StandardError.ReadToEndAsync();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();

        if (!process.WaitForExit(10_000))
        {
            KillProcess(process);
            Assert.Fail("switch-off published app did not exit after --devtools run.");
        }

        var stderr = await stderrTask;
        _ = await stdoutTask;

        Assert.AreNotEqual(0, process.ExitCode, "--devtools run on a switch-off binary should exit non-zero.");
        StringAssert.Contains(stderr, DevtoolsSwitchSnippet);
    }

    [TestMethod]
    [TestCategory("Spec051")]
    public async Task Spec051_DevtoolsEndToEnd_SwitchOn_PreviewCaptureServerStarts()
    {
        // The MCP server is gone; what the package still serves is the preview capture
        // server the VS Code panel and the Visual Studio embedded preview launch with
        // --vscode / --embed. Prove it starts in a NativeAOT binary and enforces its token.
        var exe = FindPublishedExe(
            "REACTOR_SPEC051_SWITCH_ON_EXE",
            Path.Combine("samples", "apps", "hello-world-aot-devtools-on"),
            "hello-world-aot-devtools-on.exe");

        using var process = StartProcess(exe, ["--devtools", "run", "--vscode"]);
        try
        {
            var (port, token) = await WaitForCaptureEndpointAsync(process, TimeSpan.FromSeconds(30));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var status = $"http://127.0.0.1:{port}/status";

            using (var anonymous = await client.GetAsync(status))
                Assert.AreEqual(401, (int)anonymous.StatusCode, "/status without the bearer token must be rejected.");

            using var request = new HttpRequestMessage(HttpMethod.Get, status);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            Assert.IsTrue(response.IsSuccessStatusCode, $"/status HTTP status {(int)response.StatusCode}: {body}");

            using var json = JsonDocument.Parse(body);
            Assert.AreEqual(port, json.RootElement.GetProperty("port").GetInt32(), body);
        }
        finally
        {
            KillProcess(process);
        }
    }

    private static Process StartProcess(string exe, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        return Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {exe}.");
    }

    private static string FindPublishedExe(string envVar, string sampleRelativePath, string exeName)
    {
        var fromEnv = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            if (File.Exists(fromEnv)) return fromEnv;
            Assert.Fail($"{envVar} points to a missing executable: {fromEnv}");
        }

        if (string.Equals(Environment.GetEnvironmentVariable("REACTOR_SPEC051_DISCOVER_PUBLISHED"), "1", StringComparison.Ordinal))
        {
            var repo = FindRepoRoot();
            var sampleDir = Path.Combine(repo, sampleRelativePath);
            var candidates = Directory.Exists(sampleDir)
                ? Directory.EnumerateFiles(sampleDir, exeName, SearchOption.AllDirectories)
                    .Where(p => p.Contains($"{Path.DirectorySeparatorChar}publish{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList()
                : [];

            if (candidates.Count > 0) return candidates[0].FullName;
        }

        Assert.Inconclusive(
            $"Set {envVar} to the published {exeName} path (or set REACTOR_SPEC051_DISCOVER_PUBLISHED=1 after publishing the sample). Sample: {sampleRelativePath}");
        throw new UnreachableException();
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Reactor.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new DirectoryNotFoundException("Could not find repo root (Reactor.slnx).");
    }

    private static async Task<(int Port, string Token)> WaitForCaptureEndpointAsync(Process process, TimeSpan timeout)
    {
        int? port = null;
        string? token = null;
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested && (port is null || token is null))
        {
            string? line;
            try { line = await process.StandardOutput.ReadLineAsync(cts.Token); }
            catch (OperationCanceledException) { break; }

            if (line is null)
            {
                var stderr = await process.StandardError.ReadToEndAsync();
                Assert.Fail($"devtools process exited before announcing the capture endpoint. Exit={process.ExitCode}; stderr={stderr}");
            }

            if (line.StartsWith("CAPTURE_PORT=", StringComparison.Ordinal) && int.TryParse(line["CAPTURE_PORT=".Length..], out var p))
                port = p;
            else if (line.StartsWith("CAPTURE_TOKEN=", StringComparison.Ordinal))
                token = line["CAPTURE_TOKEN=".Length..];
        }

        if (port is null || string.IsNullOrEmpty(token))
            Assert.Fail("Timed out waiting for CAPTURE_PORT / CAPTURE_TOKEN.");
        return (port!.Value, token!);
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
        }
        catch { }
    }
}
