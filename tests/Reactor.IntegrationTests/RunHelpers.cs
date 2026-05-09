using System.Diagnostics;
using Xunit.Sdk;

namespace Microsoft.UI.Reactor.IntegrationTests
{
    internal static class RunHelpers
    {
        internal static void RunDotnet(
            string arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string?> environmentVariables,
            int timeoutMs)
        {
            _ = RunProcess("dotnet", arguments, workingDirectory, environmentVariables, timeoutMs, throwOnFailure: true);
        }

        internal readonly record struct ProcessResult(int ExitCode, string Stdout, string Stderr);

        internal static ProcessResult RunProcess(
            string fileName,
            string arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string?> environmentVariables,
            int timeoutMs,
            bool throwOnFailure)
        {
            using var process = CreateProcess(fileName, arguments, workingDirectory, environmentVariables);
            if (!process.Start())
            {
                throw new XunitException($"Failed to start: {fileName} {arguments}");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync();
            var completedTask = Task.WhenAny(exitTask, Task.Delay(timeoutMs)).GetAwaiter().GetResult();
            if (completedTask != exitTask)
            {
                TryKillProcessTree(process);
                process.WaitForExit();

                var timeoutMessage =
                    $"Timed out: {fileName} {arguments}{Environment.NewLine}" +
                    $"Working directory: {workingDirectory}{Environment.NewLine}" +
                    FormatCommandOutput(stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
                throw new XunitException(timeoutMessage);
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0 && throwOnFailure)
            {
                throw new XunitException(
                    $"Command failed: {fileName} {arguments}{Environment.NewLine}" +
                    $"Exit code: {process.ExitCode}{Environment.NewLine}" +
                    $"Working directory: {workingDirectory}{Environment.NewLine}" +
                    FormatCommandOutput(stdout, stderr));
            }

            return new ProcessResult(process.ExitCode, stdout, stderr);
        }

        internal static Process CreateProcess(
            string fileName,
            string arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string?> environmentVariables)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };

            foreach (var (key, value) in environmentVariables)
            {
                process.StartInfo.Environment[key] = value ?? string.Empty;
            }

            return process;
        }

        internal static string FormatCommandOutput(string stdout, string stderr)
        {
            return
                $"--- stdout ---{Environment.NewLine}{stdout}{Environment.NewLine}" +
                $"--- stderr ---{Environment.NewLine}{stderr}";
        }

        internal static void TryKillProcessTree(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5_000);
                }
            }
            catch
            {
                // Best-effort cleanup for failed child processes.
            }
        }
    }
}
