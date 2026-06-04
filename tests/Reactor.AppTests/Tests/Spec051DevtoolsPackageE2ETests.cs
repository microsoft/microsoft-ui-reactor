using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
    public async Task Spec051_DevtoolsEndToEnd_SwitchOn_McpServerStarts()
    {
        var exe = FindPublishedExe(
            "REACTOR_SPEC051_SWITCH_ON_EXE",
            Path.Combine("samples", "apps", "hello-world-aot-devtools-on"),
            "hello-world-aot-devtools-on.exe");
        var projectId = Path.Combine(Path.GetTempPath(), $"reactor-spec051-{Guid.NewGuid():N}.csproj");
        var lockfile = LockfilePathFor(projectId);

        using var process = StartProcess(exe, ["--devtools", "run", "--mcp-port", "0", "--devtools-project", projectId]);
        try
        {
            var ready = await WaitForReadyAsync(process, TimeSpan.FromSeconds(30));
            Assert.AreEqual("devtools-ready", ready.GetProperty("event").GetString());
            var endpoint = ready.GetProperty("endpoint").GetString();
            Assert.IsFalse(string.IsNullOrWhiteSpace(endpoint), "ready payload should include endpoint.");

            var token = await WaitForLockfileTokenAsync(lockfile, TimeSpan.FromSeconds(10));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}",
                    Encoding.UTF8,
                    "application/json"),
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            Assert.IsTrue(response.IsSuccessStatusCode, $"tools/list HTTP status {(int)response.StatusCode}: {body}");

            using var rpc = JsonDocument.Parse(body);
            Assert.IsFalse(rpc.RootElement.TryGetProperty("error", out var error), error.ToString());
            var tools = rpc.RootElement.GetProperty("result").GetProperty("tools");
            Assert.IsTrue(tools.GetArrayLength() > 0, "tools/list should return at least one tool.");
        }
        finally
        {
            KillProcess(process);
            try { if (File.Exists(lockfile)) File.Delete(lockfile); } catch { }
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
            Assert.Inconclusive($"{envVar} points to a missing executable: {fromEnv}");
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

    private static async Task<JsonElement> WaitForReadyAsync(Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            var lineTask = process.StandardOutput.ReadLineAsync(cts.Token).AsTask();
            string? line;
            try { line = await lineTask; }
            catch (OperationCanceledException) { break; }

            if (line is null)
            {
                var stderr = await process.StandardError.ReadToEndAsync();
                Assert.Fail($"devtools process exited before ready. Exit={process.ExitCode}; stderr={stderr}");
            }

            if (line.Contains("devtools-ready", StringComparison.Ordinal))
            {
                using var document = JsonDocument.Parse(line);
                return document.RootElement.Clone();
            }
        }

        Assert.Fail("Timed out waiting for devtools-ready announcement.");
        throw new UnreachableException();
    }

    private static async Task<string> WaitForLockfileTokenAsync(string lockfile, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(lockfile))
            {
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(lockfile));
                if (document.RootElement.TryGetProperty("token", out var token) && token.GetString() is { Length: > 0 } value)
                    return value;
            }

            await Task.Delay(100);
        }

        Assert.Fail($"Timed out waiting for devtools lockfile: {lockfile}");
        throw new UnreachableException();
    }

    private static string LockfilePathFor(string projectIdentifier)
    {
        var canonical = Path.GetFullPath(projectIdentifier).Replace('/', '\\').ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        var builder = new StringBuilder(16);
        for (var i = 0; i < 8; i++) builder.Append(bytes[i].ToString("x2"));
        return Path.Combine(Path.GetTempPath(), "reactor-devtools", builder + ".json");
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
