using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DemoScriptTool.App.Models;

namespace DemoScriptTool.App.Services;

/// <summary>
/// Encapsulates every <c>dotnet</c> shell-out so the UI never spawns processes
/// directly (spec §Build invocation). The build/run separation matters for the
/// build-and-fix loop: builds capture output and run synchronously; runs
/// launch the GUI app non-blocking and surface only spawn failures.
///
/// Open question §4 resolution: single-file mode uses <c>dotnet run</c>
/// against the file directly (file-based apps, available in net10 SDK and
/// later); if the SDK is older, we wrap each <c>step-NN.cs</c> in a transient
/// project under <c>obj/demo/step-NN/</c>. Multi-file mode runs <c>dotnet build</c>
/// then <c>dotnet run --no-build</c> in the step directory.
/// </summary>
public sealed class DotnetRunner
{
    /// <summary>Outcome of a build attempt.</summary>
    public sealed record BuildResult(bool Succeeded, string CombinedOutput, int ExitCode);

    /// <summary>
    /// Serializes concurrent BuildAsync calls. The WindowsAppSDK markup-
    /// compiler MSBuild task (Microsoft.UI.Xaml.Markup.Compiler.interop) is
    /// not safe to invoke from two `dotnet build` processes at once — it
    /// races on a shared temp directory and one of the two reliably fails
    /// with MSB3073. The generation pipeline fires a build per step from
    /// independent Task.Run continuations so without this gate, anything
    /// past step 1 hits the race.
    /// </summary>
    readonly System.Threading.SemaphoreSlim _buildGate = new(1, 1);

    /// <summary>Run <c>dotnet build</c> for a step. The process is killed if cancelled.</summary>
    public async Task<BuildResult> BuildAsync(StepModel step, string projectRoot, bool multiFile, CancellationToken ct)
    {
        await _buildGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (multiFile)
            {
                var stepDir = Path.Combine(projectRoot, $"step-{step.Number:D2}");
                return await RunCapturedAsync("dotnet", "build --nologo -v:m", stepDir, ct).ConfigureAwait(false);
            }

            var file = Path.Combine(projectRoot, $"step-{step.Number:D2}.cs");
            // `dotnet build <file.cs>` (file-based apps) — when unavailable, fall back to a transient project wrap.
            var direct = await RunCapturedAsync("dotnet", $"build --nologo -v:m \"{file}\"", projectRoot, ct).ConfigureAwait(false);
            if (direct.Succeeded || !LooksLikeFileBasedNotSupported(direct.CombinedOutput))
                return direct;

            var wrap = await WrapAsTransientProjectAsync(step, projectRoot, ct).ConfigureAwait(false);
            return await RunCapturedAsync("dotnet", "build --nologo -v:m", wrap, ct).ConfigureAwait(false);
        }
        finally
        {
            _buildGate.Release();
        }
    }

    /// <summary>Spawn a non-blocking <c>dotnet run</c> for a step. Returns whether spawn succeeded.</summary>
    public async Task<(bool Spawned, string? Error)> RunAsync(StepModel step, string projectRoot, bool multiFile, CancellationToken ct)
    {
        try
        {
            string args, workdir;
            // Forward `--devtools` to the spawned step so any Reactor devtools
            // overlay / debug menu in the generated app is reachable from the
            // demo. Everything after `--` is passed verbatim to the program,
            // not consumed by `dotnet run`.
            if (multiFile)
            {
                workdir = Path.Combine(projectRoot, $"step-{step.Number:D2}");
                args = "run --no-build -- --devtools";
            }
            else
            {
                workdir = projectRoot;
                args = $"run \"{Path.Combine(projectRoot, $"step-{step.Number:D2}.cs")}\" -- --devtools";
            }

            var psi = new ProcessStartInfo("dotnet", args)
            {
                WorkingDirectory = workdir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            var p = Process.Start(psi);
            if (p is null) return (false, "Process.Start returned null.");

            // Capture stderr to a per-step run log per spec §Error Surfacing.
            var logPath = Path.Combine(projectRoot, "obj", "demo", $"step-{step.Number:D2}.runlog");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            _ = Task.Run(async () =>
            {
                try
                {
                    var err = await p.StandardError.ReadToEndAsync().ConfigureAwait(false);
                    await File.WriteAllTextAsync(logPath, err).ConfigureAwait(false);
                }
                catch { /* best-effort logging */ }
            });

            await Task.Yield();
            return (!p.HasExited || p.ExitCode == 0, p.HasExited && p.ExitCode != 0 ? $"exit code {p.ExitCode}" : null);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            return (false, "The .NET SDK ('dotnet') was not found on PATH.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    static async Task<BuildResult> RunCapturedAsync(string fileName, string args, string workdir, CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[DotnetRunner] spawn '{fileName} {args}' cwd='{workdir}'");
        var psi = new ProcessStartInfo(fileName, args)
        {
            WorkingDirectory = workdir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        Process? p;
        try { p = Process.Start(psi); }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            return new BuildResult(false, $"The .NET SDK ('{fileName}') was not found on PATH.", -1);
        }

        if (p is null)
            return new BuildResult(false, "Process.Start returned null.", -1);

        var output = new StringBuilder();
        var ctRegistration = ct.Register(() =>
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
            catch { /* race */ }
        });

        var stdoutTask = ReadAllAsync(p.StandardOutput, output);
        var stderrTask = ReadAllAsync(p.StandardError, output);
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        await p.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        ctRegistration.Dispose();

        var combined = output.ToString();
        return new BuildResult(p.ExitCode == 0 && !ct.IsCancellationRequested, combined, p.ExitCode);
    }

    static async Task ReadAllAsync(StreamReader reader, StringBuilder sink)
    {
        char[] buffer = new char[2048];
        while (true)
        {
            int read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            if (read <= 0) return;
            lock (sink) sink.Append(buffer, 0, read);
        }
    }

    static bool LooksLikeFileBasedNotSupported(string output) =>
        output.Contains("MSBUILD : error MSB1003", StringComparison.Ordinal)
        || output.Contains("Specify a project or solution file", StringComparison.Ordinal)
        || output.Contains("not a recognized command", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Fallback for SDKs without file-based-app support: scaffold an SDK-style
    /// project in <c>obj/demo/step-NN/</c> that links the original file.
    /// </summary>
    static Task<string> WrapAsTransientProjectAsync(StepModel step, string projectRoot, CancellationToken ct)
    {
        var dir = Path.Combine(projectRoot, "obj", "demo", $"step-{step.Number:D2}");
        Directory.CreateDirectory(dir);
        var csproj = Path.Combine(dir, $"step-{step.Number:D2}.csproj");
        var src = Path.Combine(projectRoot, $"step-{step.Number:D2}.cs");
        File.WriteAllText(csproj, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="{src.Replace('\\', '/')}" Link="step-{step.Number:D2}.cs" />
              </ItemGroup>
            </Project>
            """);
        return Task.FromResult(dir);
    }
}
