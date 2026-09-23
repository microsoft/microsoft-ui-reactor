// Shared fixture that packs the Reactor framework packages into a throwaway
// local NuGet feed, so consumer-facing integration tests can restore against a
// real .nupkg instead of a project reference.
//
// It previously also packed and installed the in-repo
// `Microsoft.UI.Reactor.ProjectTemplates` package and scaffolded from it. That
// package was removed in favour of the Windows App SDK `dotnet new reactor`
// templates, so the template-pack and template-hive plumbing is gone; what
// remains is the local package feed itself.

using System.Runtime.InteropServices;
using Xunit;

namespace Microsoft.UI.Reactor.IntegrationTests.Packaging;

public sealed class LocalPackageFeedFixture : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"reactor-local-feed-{Guid.NewGuid():N}");

    public LocalPackageFeedFixture()
    {
        Directory.CreateDirectory(_tempRoot);

        RepoRoot = FindRepoRoot();
        var packageSuffix = Guid.NewGuid().ToString("N")[..12];
        PackageVersion = $"0.0.0-feed-smoke-{packageSuffix}";
        PackageSourceDir = CreateDirectory("packages");
        NugetPackagesDir = CreateDirectory("nuget-global-packages");
        var nugetHttpCacheDir = CreateDirectory("nuget-http-cache");
        var dotnetCliHomeDir = CreateDirectory("dotnet-home");
        RunArchitecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        CommandEnvironment = CreateCommandEnvironment(dotnetCliHomeDir, nugetHttpCacheDir);

        RunHelpers.RunDotnet(
            $"pack \"{Path.Combine(RepoRoot, "src", "Reactor", "Reactor.csproj")}\" --no-restore --configuration Release -o \"{PackageSourceDir}\" -p:Version={PackageVersion}",
            RepoRoot,
            CommandEnvironment,
            timeoutMs: 300_000);
        // Devtools depends on Advanced (spec-062), and consumers can pull both
        // transitively, so pack all three at the same version to keep a restore
        // from falling through to NuGet.org and hitting an NU1605 downgrade.
        RunHelpers.RunDotnet(
            $"pack \"{Path.Combine(RepoRoot, "src", "Reactor.Devtools", "Reactor.Devtools.csproj")}\" --no-restore --configuration Release -o \"{PackageSourceDir}\" -p:Version={PackageVersion}",
            RepoRoot,
            CommandEnvironment,
            timeoutMs: 300_000);
        RunHelpers.RunDotnet(
            $"pack \"{Path.Combine(RepoRoot, "src", "Reactor.Advanced", "Reactor.Advanced.csproj")}\" --no-restore --configuration Release -o \"{PackageSourceDir}\" -p:Version={PackageVersion}",
            RepoRoot,
            CommandEnvironment,
            timeoutMs: 300_000);

        _ = FindPackage(PackageSourceDir, "Microsoft.UI.Reactor", PackageVersion);
        _ = FindPackage(PackageSourceDir, "Microsoft.UI.Reactor.Devtools", PackageVersion);
        _ = FindPackage(PackageSourceDir, "Microsoft.UI.Reactor.Advanced", PackageVersion);
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

        return dir ?? throw new DirectoryNotFoundException("Could not find repo root (Reactor.slnx).");
    }
}
