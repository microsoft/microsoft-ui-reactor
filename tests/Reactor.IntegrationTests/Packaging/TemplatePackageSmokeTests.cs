using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;
using Xunit.Sdk;

namespace Microsoft.UI.Reactor.IntegrationTests.Packaging;

public sealed class TemplatePackageSmokeTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"reactor-template-smoke-{Guid.NewGuid():N}");

    public TemplatePackageSmokeTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReactorTemplatePackage_CanCreateBuildAndRunApp(bool useProgramMain)
    {
        var repoRoot = FindRepoRoot();
        var packageVersion = $"0.0.0-template-smoke-{Guid.NewGuid():N}";
        var packageSourceDir = CreateDirectory("packages");
        var nugetPackagesDir = CreateDirectory("nuget-global-packages");
        var nugetHttpCacheDir = CreateDirectory("nuget-http-cache");
        var dotnetCliHomeDir = CreateDirectory("dotnet-home");
        var templateHiveDir = CreateDirectory("template-hive");
        var runArchitecture = GetRunArchitecture();
        var packPlatform = GetPackPlatform();

        var commandEnvironment = CreateCommandEnvironment(dotnetCliHomeDir, nugetHttpCacheDir);

        RunDotnet(
            $"pack \"{Path.Combine(repoRoot, "src", "Reactor", "Reactor.csproj")}\" -c Release -o \"{packageSourceDir}\" -p:Version={packageVersion}",
            repoRoot,
            commandEnvironment,
            timeoutMs: 300_000);

        RunDotnet(
            $"pack \"{Path.Combine(repoRoot, "tools", "Templates", "Microsoft.UI.Reactor.Templates.csproj")}\" -c Release -o \"{packageSourceDir}\" -p:Version={packageVersion} -p:MicrosoftUIReactorVersion={packageVersion}",
            repoRoot,
            commandEnvironment,
            timeoutMs: 180_000);

        var frameworkPackage = FindPackage(packageSourceDir, "Microsoft.UI.Reactor", packageVersion);
        var templatePackage = FindPackage(packageSourceDir, "Microsoft.UI.Reactor.ProjectTemplates", packageVersion);

        RunDotnet(
            $"new install --debug:custom-hive \"{templateHiveDir}\" \"{templatePackage}\"",
            repoRoot,
            commandEnvironment,
            timeoutMs: 120_000);

        Assert.True(File.Exists(frameworkPackage), $"Expected packed framework package at '{frameworkPackage}'.");
        RunTemplateScenario(
            repoRoot,
            packageSourceDir,
            nugetPackagesDir,
            templateHiveDir,
            runArchitecture,
            commandEnvironment,
            useProgramMain);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for temporary smoke-test artifacts.
        }
    }

    private string CreateDirectory(string name)
    {
        var path = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static Dictionary<string, string?> CreateCommandEnvironment(string dotnetCliHomeDir, string nugetHttpCacheDir)
    {
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTNET_CLI_HOME"] = dotnetCliHomeDir,
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["NUGET_HTTP_CACHE_PATH"] = nugetHttpCacheDir,
        };
    }

    private static void CreateNuGetConfig(
        string workingDirectory,
        string packageSourceDir,
        string nugetPackagesDir,
        IReadOnlyDictionary<string, string?> environmentVariables)
    {
        RunDotnet(
            $"new nugetconfig --output \"{workingDirectory}\" --force",
            workingDirectory,
            environmentVariables,
            timeoutMs: 30_000);

        var configPath = Path.Combine(workingDirectory, "nuget.config");

        RunDotnet(
            $"nuget config set globalPackagesFolder \"{nugetPackagesDir}\" --configfile \"{configPath}\"",
            workingDirectory,
            environmentVariables,
            timeoutMs: 30_000);

        RunDotnet(
            $"nuget add source \"{packageSourceDir}\" --name reactor-local --configfile \"{configPath}\"",
            workingDirectory,
            environmentVariables,
            timeoutMs: 30_000);
    }

    private static string FindPackage(string packageSourceDir, string packageId, string version)
    {
        var packagePath = Path.Combine(packageSourceDir, $"{packageId}.{version}.nupkg");
        Assert.True(File.Exists(packagePath), $"Expected package '{packagePath}' to exist.");
        return packagePath;
    }

    private void RunTemplateScenario(
        string repoRoot,
        string packageSourceDir,
        string nugetPackagesDir,
        string templateHiveDir,
        string runArchitecture,
        IReadOnlyDictionary<string, string?> commandEnvironment,
        bool useProgramMain)
    {
        var scenarioName = useProgramMain ? "program-main" : "top-level";
        var appDir = CreateDirectory($"generated-app-{scenarioName}");
        var projectName = CreateProjectName(useProgramMain);

        CreateNuGetConfig(appDir, packageSourceDir, nugetPackagesDir, commandEnvironment);

        RunDotnet(
            $"new reactorapp --debug:custom-hive \"{templateHiveDir}\" --use-program-main {useProgramMain.ToString().ToLowerInvariant()} --name {projectName} --output \"{appDir}\" --force",
            repoRoot,
            commandEnvironment,
            timeoutMs: 180_000);

        var projectPath = Path.Combine(appDir, $"{projectName}.csproj");
        Assert.True(File.Exists(projectPath), $"Expected generated project at '{projectPath}'.");

        AssertTemplateProgramMode(appDir, useProgramMain);

        RunDotnet(
            $"build -a {runArchitecture}",
            appDir,
            commandEnvironment,
            timeoutMs: 300_000);

        RunDotnetRunSmoke(
            appDir,
            projectName,
            runArchitecture,
            commandEnvironment,
            timeoutMs: 120_000);
    }

    private static void AssertTemplateProgramMode(string appDir, bool useProgramMain)
    {
        var appCode = File.ReadAllText(Path.Combine(appDir, "App.cs"));

        if (useProgramMain)
        {
            Assert.Contains("class Program", appCode);
            Assert.DoesNotContain("#if (csharpFeature_TopLevelProgram)", appCode);
        }
        else
        {
            Assert.Contains("ReactorApp.Run<App>(", appCode);
            Assert.DoesNotContain("class Program", appCode);
        }
    }

    private static string CreateProjectName(bool useProgramMain)
    {
        var prefix = useProgramMain ? "ReactorProgMain" : "ReactorTopLevel";
        return $"{prefix}{Guid.NewGuid():N}".Substring(0, 28);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Reactor.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new DirectoryNotFoundException("Could not find repo root (Reactor.sln).");
    }

    private static string GetPackPlatform() => RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "ARM64" : "x64";

    private static string GetRunArchitecture() => RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";

    private static void RunDotnetRunSmoke(
        string workingDirectory,
        string projectName,
        string architecture,
        IReadOnlyDictionary<string, string?> environmentVariables,
        int timeoutMs)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var outputLock = new object();
        var sawChildProcess = false;
        string? lastUiDetails = null;

        using var process = CreateProcess("dotnet", $"run -a {architecture}", workingDirectory, environmentVariables);
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                return;
            }

            lock (outputLock)
            {
                stdout.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                return;
            }

            lock (outputLock)
            {
                stderr.AppendLine(args.Data);
            }
        };

        if (!process.Start())
        {
            throw new XunitException("Failed to start 'dotnet run'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    break;
                }

                var launchedProcess = Process.GetProcessesByName(projectName)
                    .FirstOrDefault(candidate => !candidate.HasExited);
                if (launchedProcess != null)
                {
                    sawChildProcess = true;
                    var uiState = ProbeAppUi(launchedProcess, out var uiDetails);
                    lastUiDetails = uiDetails;
                    launchedProcess.Dispose();

                    if (uiState == AppUiState.Healthy)
                    {
                        return;
                    }

                    if (uiState == AppUiState.RenderError)
                    {
                        throw new XunitException(
                            $"Generated app showed Reactor's render-error fallback instead of the expected template UI.{Environment.NewLine}" +
                            $"Working directory: {workingDirectory}{Environment.NewLine}" +
                            $"UI Automation details: {uiDetails}{Environment.NewLine}" +
                            FormatCommandOutput(stdout.ToString(), stderr.ToString()));
                    }
                }

                Thread.Sleep(500);
            }

            if (process.HasExited)
            {
                process.WaitForExit();
                throw new XunitException(
                    $"Command failed: dotnet run -a {architecture}{Environment.NewLine}" +
                    $"Exit code: {process.ExitCode}{Environment.NewLine}" +
                    $"Working directory: {workingDirectory}{Environment.NewLine}" +
                    FormatCommandOutput(stdout.ToString(), stderr.ToString()));
            }

            throw new XunitException(
                $"Timed out waiting for '{projectName}.exe' to start from 'dotnet run -a {architecture}'. " +
                $"Child process observed: {sawChildProcess}.{Environment.NewLine}" +
                $"UI Automation details: {lastUiDetails ?? "None captured."}{Environment.NewLine}" +
                $"Working directory: {workingDirectory}{Environment.NewLine}" +
                FormatCommandOutput(stdout.ToString(), stderr.ToString()));
        }
        finally
        {
            TryKillProcessTree(process);
            foreach (var launchedProcess in Process.GetProcessesByName(projectName))
            {
                try
                {
                    if (!launchedProcess.HasExited)
                    {
                        launchedProcess.Kill(entireProcessTree: true);
                        launchedProcess.WaitForExit(5_000);
                    }
                }
                catch
                {
                    // Best-effort cleanup for unique smoke-test app names.
                }
                finally
                {
                    launchedProcess.Dispose();
                }
            }
        }
    }

    private static void RunDotnet(
        string arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environmentVariables,
        int timeoutMs)
    {
        var result = RunProcess("dotnet", arguments, workingDirectory, environmentVariables, timeoutMs, throwOnFailure: true);
        _ = result;
    }

    private static ProcessResult RunProcess(
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

    private static Process CreateProcess(
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

    private static string FormatCommandOutput(string stdout, string stderr)
    {
        return
            $"--- stdout ---{Environment.NewLine}{stdout}{Environment.NewLine}" +
            $"--- stderr ---{Environment.NewLine}{stderr}";
    }

    private static void TryKillProcessTree(Process process)
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

    private static AppUiState ProbeAppUi(Process launchedProcess, out string? details)
    {
        details = null;

        try
        {
            var probe = RunProcess(
                "powershell.exe",
                BuildUiaProbeArguments(launchedProcess.Id),
                Environment.SystemDirectory,
                environmentVariables: new Dictionary<string, string?>(),
                timeoutMs: 5_000,
                throwOnFailure: false);

            details = string.IsNullOrWhiteSpace(probe.Stdout)
                ? probe.Stderr.Trim()
                : probe.Stdout.Trim();

            if (probe.ExitCode == 0)
            {
                return AppUiState.Healthy;
            }

            if (probe.ExitCode == 2)
            {
                return AppUiState.RenderError;
            }

            return AppUiState.NotReady;
        }
        catch (XunitException ex)
        {
            details = $"UI Automation probe failed: {ex.Message}";
            return AppUiState.NotReady;
        }
        catch (InvalidOperationException ex)
        {
            details = $"UI Automation probe failed: {ex.Message}";
            return AppUiState.NotReady;
        }
    }

    private static string BuildUiaProbeArguments(int processId)
    {
        var script = $$"""
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
$processId = {{processId}}
$root = [System.Windows.Automation.AutomationElement]::RootElement
$condition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
    $processId)
$elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
$names = New-Object 'System.Collections.Generic.List[string]'

for ($i = 0; $i -lt $elements.Count; $i++) {
    $name = [string]$elements.Item($i).GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::NameProperty)
    if (-not [string]::IsNullOrWhiteSpace($name) -and -not $names.Contains($name)) {
        [void]$names.Add($name)
        if ($names.Count -ge 20) {
            break
        }
    }
}

if ($names.Contains('NameInput')) {
    Write-Output 'Found template NameInput automation name.'
    exit 0
}

$renderError = $names | Where-Object { $_ -like '*Render error*' } | Select-Object -First 1
if ($renderError) {
    Write-Output $renderError
    exit 2
}

if ($names.Count -eq 0) {
    Write-Output 'No UI Automation names are visible for the launched process yet.'
    exit 1
}

Write-Output ('Observed names: ' + [string]::Join(', ', $names))
exit 1
""";

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}";
    }

    private enum AppUiState
    {
        NotReady,
        Healthy,
        RenderError,
    }

    private readonly record struct ProcessResult(int ExitCode, string Stdout, string Stderr);
}
