using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;
using Xunit.Sdk;

namespace Microsoft.UI.Reactor.IntegrationTests.Packaging;

public sealed class CreateTemplateTests : IClassFixture<TemplatePackageTestFixture>, IDisposable
{
    private readonly TemplatePackageTestFixture _fixture;
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"reactor-template-smoke-{Guid.NewGuid():N}");

    public CreateTemplateTests(TemplatePackageTestFixture fixture)
    {
        _fixture = fixture;
        Directory.CreateDirectory(_tempRoot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateAndRunReactorappTemplate(bool useProgramMain)
    {
        var scenarioName = useProgramMain ? "program-main" : "top-level";
        var appDir = CreateDirectory($"generated-app-{scenarioName}");
        var projectName = CreateProjectName(useProgramMain);

        CreateNuGetConfig(appDir, _fixture.PackageSourceDir, _fixture.NugetPackagesDir, _fixture.CommandEnvironment);

        RunHelpers.RunDotnet(
            $"new reactorapp --debug:custom-hive \"{_fixture.TemplateHiveDir}\" --use-program-main {useProgramMain.ToString().ToLowerInvariant()} --name {projectName} --output \"{appDir}\" --force",
            _fixture.RepoRoot,
            _fixture.CommandEnvironment,
            timeoutMs: 180_000);

        var projectPath = Path.Combine(appDir, $"{projectName}.csproj");
        Assert.True(File.Exists(projectPath), $"Expected generated project at '{projectPath}'.");

        RunHelpers.RunDotnet(
            $"build -a {_fixture.RunArchitecture}",
            appDir,
            _fixture.CommandEnvironment,
            timeoutMs: 300_000);

        RunDotnetRun(
            appDir,
            projectName,
            _fixture.RunArchitecture,
            _fixture.CommandEnvironment,
            timeoutMs: 120_000);
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

    private static void CreateNuGetConfig(
        string workingDirectory,
        string packageSourceDir,
        string nugetPackagesDir,
        IReadOnlyDictionary<string, string?> environmentVariables)
    {
        RunHelpers.RunDotnet(
            $"new nugetconfig --output \"{workingDirectory}\" --force",
            workingDirectory,
            environmentVariables,
            timeoutMs: 30_000);

        var configPath = Path.Combine(workingDirectory, "nuget.config");

        RunHelpers.RunDotnet(
            $"nuget config set globalPackagesFolder \"{nugetPackagesDir}\" --configfile \"{configPath}\"",
            workingDirectory,
            environmentVariables,
            timeoutMs: 30_000);

        RunHelpers.RunDotnet(
            $"nuget add source \"{packageSourceDir}\" --name reactor-local --configfile \"{configPath}\"",
            workingDirectory,
            environmentVariables,
            timeoutMs: 30_000);
    }

    private static string CreateProjectName(bool useProgramMain)
    {
        var prefix = useProgramMain ? "ReactorProgMain" : "ReactorTopLevel";
        return $"{prefix}{Guid.NewGuid():N}".Substring(0, 28);
    }

    private static void RunDotnetRun(
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

        using var process = RunHelpers.CreateProcess("dotnet", $"run -a {architecture}", workingDirectory, environmentVariables);
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
                    var uiState = UiaHelpers.FindUIALement(launchedProcess, "NameInput", out var uiDetails);
                    lastUiDetails = uiDetails;
                    launchedProcess.Dispose();

                    if (uiState == UiaHelpers.UIAFindResult.Found)
                    {
                        return;
                    }

                    if (uiState == UiaHelpers.UIAFindResult.NotFound)
                    {
                        throw new XunitException(
                            $"Generated app showed Reactor's render-error fallback instead of the expected template UI.{Environment.NewLine}" +
                            $"Working directory: {workingDirectory}{Environment.NewLine}" +
                            $"UI Automation details: {uiDetails}{Environment.NewLine}" +
                            RunHelpers.FormatCommandOutput(stdout.ToString(), stderr.ToString()));
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
                    RunHelpers.FormatCommandOutput(stdout.ToString(), stderr.ToString()));
            }

            throw new XunitException(
                $"Timed out waiting for '{projectName}.exe' to start from 'dotnet run -a {architecture}'. " +
                $"Child process observed: {sawChildProcess}.{Environment.NewLine}" +
                $"UI Automation details: {lastUiDetails ?? "None captured."}{Environment.NewLine}" +
                $"Working directory: {workingDirectory}{Environment.NewLine}" +
                RunHelpers.FormatCommandOutput(stdout.ToString(), stderr.ToString()));
        }
        finally
        {
            RunHelpers.TryKillProcessTree(process);
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
}

public sealed class TemplatePackageTestFixture : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"reactor-template-packages-{Guid.NewGuid():N}");

    public TemplatePackageTestFixture()
    {
        Directory.CreateDirectory(_tempRoot);

        RepoRoot = FindRepoRoot();
        PackageVersion = $"0.0.0-template-smoke-{Guid.NewGuid():N}";
        PackageSourceDir = CreateDirectory("packages");
        NugetPackagesDir = CreateDirectory("nuget-global-packages");
        var nugetHttpCacheDir = CreateDirectory("nuget-http-cache");
        var dotnetCliHomeDir = CreateDirectory("dotnet-home");
        TemplateHiveDir = CreateDirectory("template-hive");
        RunArchitecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        CommandEnvironment = CreateCommandEnvironment(dotnetCliHomeDir, nugetHttpCacheDir);

        RunHelpers.RunDotnet(
            $"pack \"{Path.Combine(RepoRoot, "src", "Reactor", "Reactor.csproj")}\" --no-restore --configuration Release -o \"{PackageSourceDir}\" -p:Version={PackageVersion}",
            RepoRoot,
            CommandEnvironment,
            timeoutMs: 300_000);
        RunHelpers.RunDotnet(
            $"pack \"{Path.Combine(RepoRoot, "tools", "Templates", "Microsoft.UI.Reactor.Templates.csproj")}\" --no-restore --configuration Release -o \"{PackageSourceDir}\" -p:Version={PackageVersion} -p:MicrosoftUIReactorVersion={PackageVersion} -p:Platform=AnyCPU",
            RepoRoot,
            CommandEnvironment,
            timeoutMs: 180_000);

        _ = FindPackage(PackageSourceDir, "Microsoft.UI.Reactor", PackageVersion);
        var templatePackage = FindPackage(PackageSourceDir, "Microsoft.UI.Reactor.ProjectTemplates", PackageVersion);

        RunHelpers.RunDotnet(
            $"new install --debug:custom-hive \"{TemplateHiveDir}\" \"{templatePackage}\"",
            RepoRoot,
            CommandEnvironment,
            timeoutMs: 120_000);
    }

    private static string FindPackage(string packageSourceDir, string packageId, string version)
    {
        var packagePath = Path.Combine(packageSourceDir, $"{packageId}.{version}.nupkg");
        Assert.True(File.Exists(packagePath), $"Expected package '{packagePath}' to exist.");
        return packagePath;
    }

    private static Dictionary<string, string?> CreateCommandEnvironment(string dotnetCliHomeDir, string nugetHttpCacheDir)
    {
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "false",
            ["DOTNET_CLI_HOME"] = dotnetCliHomeDir,
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["NUGET_HTTP_CACHE_PATH"] = nugetHttpCacheDir,
        };
    }

    public string RepoRoot { get; }

    public string PackageVersion { get; }

    public string PackageSourceDir { get; }

    public string NugetPackagesDir { get; }

    public string TemplateHiveDir { get; }

    public string RunArchitecture { get; }

    public IReadOnlyDictionary<string, string?> CommandEnvironment { get; }

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
            // Best-effort cleanup for shared package-setup artifacts.
        }
    }

    private string CreateDirectory(string name)
    {
        var path = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(path);
        return path;
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
}
