using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Microsoft.UI.Reactor.SelfTests;

/// <summary>
/// Locates and runs the selftest Host app as a child process. Shared by
/// <see cref="SelfTestBatch"/> (which drives <c>--self-test</c> and
/// <c>--list-fixtures</c>) and <see cref="ShutdownPolicyProcessLifetimeTests"/>
/// (which drives <c>--shutdown-policy-probe</c>), so both agree on where the Host
/// lives and how its streams are drained.
/// </summary>
internal static class HostProcess
{
    /// <summary>
    /// Runs the Host to completion, or kills it once <paramref name="timeoutMs"/>
    /// elapses. Both streams are read concurrently so neither pipe can block the
    /// child by filling its OS buffer.
    /// </summary>
    internal static (string Stdout, string Stderr, int ExitCode, bool TimedOut) Run(
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

    internal static string FindHostExe()
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

        // Configuration and TFM are stamped into this assembly by the csproj
        // rather than hardcoded: the Host is built by our ProjectReference under
        // whatever configuration the test run used, so a literal "Debug" here
        // silently runs a stale Debug host under `dotnet test -c Release` — or
        // fails outright when one was never built. Same fix, same reason, as
        // Reactor.PackagedTests. The fallbacks keep a hand-run assembly working.
        var configuration = MetadataOr("ReactorSelfTestsConfiguration", "Debug");
        var tfm = MetadataOr("ReactorSelfTestsTargetFramework", "net10.0-windows10.0.22621.0");

        var exe = Path.Combine(dir, "tests", "Reactor.AppTests.Host", "bin", platform,
            configuration, tfm, "Reactor.AppTests.Host.exe");

        if (!File.Exists(exe))
            throw new FileNotFoundException($"Host app not built. Expected: {exe}");

        return exe;
    }

    private static string MetadataOr(string key, string fallback) =>
        typeof(HostProcess).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(a => string.Equals(a.Key, key, StringComparison.Ordinal) &&
                        !string.IsNullOrWhiteSpace(a.Value))
            .Select(a => a.Value!)
            .FirstOrDefault() ?? fallback;
}
