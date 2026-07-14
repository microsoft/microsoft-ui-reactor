using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetCreator.Services;

/// <summary>Result of building a widget project.</summary>
public sealed record BuildResult(bool Success, string? PublishDir, string? ExePath, string Output);

/// <summary>
/// Publishes a scaffolded widget project. We publish framework-dependent (the
/// host's <c>C:\Program Files\dotnet</c> is reachable from the sandbox) with
/// <c>WindowsAppSDKSelfContained=true</c> so every other dependency lands in the
/// publish dir. The sandbox is granted read+execute on exactly this directory by
/// MXC itself (see <see cref="MxcSandbox"/>) — nothing here touches ACLs.
/// </summary>
public sealed class WidgetBuilder
{
    public async Task<BuildResult> BuildAsync(WidgetProject project, Action<string>? onLine, CancellationToken ct)
    {
        SessionLog.Write($"[Builder] build start {project.Dir} rid={project.Rid} platform={project.Platform}");

        var buildArgs =
            $"build \"{project.CsprojFile}\" -c Debug -p:Platform={project.Platform} --nologo";

        var (code, output) = await RunAsync("dotnet", buildArgs, project.Dir, onLine, ct).ConfigureAwait(false);
        if (code != 0)
        {
            SessionLog.Write($"[Builder] build FAILED exit={code}");
            SessionLog.Write($"[Builder] compiler diagnostics:{Environment.NewLine}{GenerationPipeline.ExtractErrors(output)}");
            return new BuildResult(false, null, null, output);
        }

        var publishDir = Path.Combine(
            project.Dir, "bin", project.Platform, "Debug", "net10.0-windows10.0.22621.0");

        var exe = Path.Combine(publishDir, "widget.exe");
        if (!File.Exists(exe))
        {
            SessionLog.Write($"[Builder] widget.exe missing under {publishDir}");
            return new BuildResult(false, publishDir, null, output + $"\nwidget.exe not found under {publishDir}");
        }

        SessionLog.Write($"[Builder] build OK exe={exe}");
        return new BuildResult(true, publishDir, exe, output);
    }

    static async Task<(int Code, string Output)> RunAsync(
        string file, string args, string cwd, Action<string>? onLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var sb = new StringBuilder();
        void Sink(string? s)
        {
            if (s is null) return;
            lock (sb) sb.AppendLine(s);
            onLine?.Invoke(s);
        }
        proc.OutputDataReceived += (_, e) => Sink(e.Data);
        proc.ErrorDataReceived += (_, e) => Sink(e.Data);

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return (proc.ExitCode, sb.ToString());
    }
}
