using System.IO.Compression;
using System.Text.Json;
using Xunit;

namespace Microsoft.UI.Reactor.IntegrationTests.Packaging;

/// <summary>
/// The Reactor packages' layout, and the build-tools version <c>Microsoft.UI.Reactor</c>
/// guarantees its consumers (issue #1271, WindowsAppSDK#6394).
///
/// <para>Needs network access to restore the Windows App SDK, so it only runs where the feeds
/// are reachable (CI's "Integration Tests" job).</para>
/// </summary>
[Collection(LocalPackageFeedCollection.Name)]
public sealed class PriPackagingTests : IDisposable
{
    private const string BuildToolsMsixId = "Microsoft.Windows.SDK.BuildTools.MSIX";

    // What Microsoft.WindowsAppSDK.Base resolves on its own; it predates the fixes.
    private const string BuildToolsMsixWithoutFixes = "1.7.251221100";

    private readonly LocalPackageFeedFixture _fixture;
    private readonly string _tempRoot = Path.Join(Path.GetTempPath(), $"rpri-{Guid.NewGuid():N}");

    public PriPackagingTests(LocalPackageFeedFixture fixture)
    {
        _fixture = fixture;
        Directory.CreateDirectory(_tempRoot);
    }

    /// <summary>
    /// WinUI loads <c>ReactorApplication.xaml</c> (or its compiled <c>.xbf</c>) at startup.
    /// The <c>.xbf</c> is packed by a pack-time target, because the <c>&lt;None&gt;</c> item it
    /// replaced was evaluated before the build created the file.
    /// </summary>
    [Fact]
    public void CorePackageShipsTheApplicationXamlSidecars()
    {
        var entries = ReadPackageEntries("Microsoft.UI.Reactor");

        Assert.Contains(entries, e => e.EndsWith("Reactor/Hosting/ReactorApplication.xbf", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, e => e.EndsWith("Reactor/Hosting/ReactorApplication.xaml", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Build tools before 1.7.260930100 leave an unpackaged app's own <c>.pri</c> out of its
    /// publish output, and the published app crashes at startup (WindowsAppSDK#6394). Reactor's
    /// dependency on the fixed version must reach its consumers. The control pins the same
    /// consumer to the version the Windows App SDK resolves alone, and must lose the <c>.pri</c>.
    /// </summary>
    [Fact]
    public void ConsumerPublishIncludesTheAppPri()
    {
        // Short names: the XAML compiler is not long-path aware.
        var withFloorDir = Path.Join(_tempRoot, "on");
        var pinnedDir = Path.Join(_tempRoot, "off");
        WriteConsumer(withFloorDir);
        WriteConsumer(pinnedDir, buildToolsMsixVersion: BuildToolsMsixWithoutFixes);

        var withFloor = Publish(withFloorDir);
        Assert.True(
            File.Exists(Path.Join(withFloor, "Consumer.pri")),
            $"The app's own .pri is missing from the publish output (WindowsAppSDK#6394). " +
            $"The consumer resolved {ResolvedBuildToolsMsix(withFloorDir)}.");

        var pinned = Publish(pinnedDir);
        Assert.False(
            File.Exists(Path.Join(pinned, "Consumer.pri")),
            $"Pinned to {BuildToolsMsixId} {BuildToolsMsixWithoutFixes} (resolved " +
            $"{ResolvedBuildToolsMsix(pinnedDir)}), the publish still produced the app .pri, so the " +
            "assertion above does not show that Reactor's dependency is what puts it there.");
    }

    private string[] ReadPackageEntries(string packageId)
    {
        var packagePath = Path.Join(_fixture.PackageSourceDir, $"{packageId}.{_fixture.PackageVersion}.nupkg");
        Assert.True(File.Exists(packagePath), $"Expected package '{packagePath}' to exist.");

        using var archive = ZipFile.OpenRead(packagePath);
        return archive.Entries.Select(e => e.FullName).ToArray();
    }

    /// <summary>
    /// A self-contained consumer of the core package: the deployment where a missing app
    /// <c>.pri</c> crashes the published app, because it also carries WinUI's own XAML.
    /// <paramref name="buildToolsMsixVersion"/> pins the build tools below Reactor's floor, which
    /// NuGet reports as an <c>NU1605</c> downgrade.
    /// </summary>
    private void WriteConsumer(string appDir, string? buildToolsMsixVersion = null)
    {
        Directory.CreateDirectory(appDir);

        var pin = buildToolsMsixVersion is null
            ? string.Empty
            : $"""

                    <PackageReference Include="{BuildToolsMsixId}" Version="{buildToolsMsixVersion}" />
              """;

        File.WriteAllText(Path.Join(appDir, "Consumer.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
                <Nullable>enable</Nullable>
                <UseWinUI>true</UseWinUI>
                <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
                <Platform>{_fixture.RunArchitecture}</Platform>
                <Platforms>{_fixture.RunArchitecture}</Platforms>
                <RuntimeIdentifier>win-{_fixture.RunArchitecture}</RuntimeIdentifier>
                <NoWarn>$(NoWarn);NU1605</NoWarn>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.UI.Reactor" Version="{_fixture.PackageVersion}" />{pin}
              </ItemGroup>
            </Project>
            """);

        // Never run: the test asserts on the publish output.
        File.WriteAllText(Path.Join(appDir, "Program.cs"), """
            using static Microsoft.UI.Reactor.Factories;

            System.Console.WriteLine(TextBlock("hello") is not null);

            """);

        CreateNuGetConfig(appDir);
    }

    private string Publish(string appDir)
    {
        var publishDir = Path.Join(appDir, "pub");
        RunHelpers.RunDotnet(
            $"publish -c Release -a {_fixture.RunArchitecture} -o \"{publishDir}\"",
            appDir,
            _fixture.CommandEnvironment,
            timeoutMs: 600_000);
        return publishDir;
    }

    private static string ResolvedBuildToolsMsix(string appDir)
    {
        var assetsPath = Path.Join(appDir, "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
        {
            return "nothing (no project.assets.json)";
        }

        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        return assets.RootElement.GetProperty("libraries").EnumerateObject()
            .Select(library => library.Name)
            .FirstOrDefault(name => name.StartsWith(BuildToolsMsixId + "/", StringComparison.OrdinalIgnoreCase))
            ?? $"no {BuildToolsMsixId} at all";
    }

    private void CreateNuGetConfig(string appDir)
    {
        RunHelpers.RunDotnet(
            $"new nugetconfig --output \"{appDir}\" --force",
            appDir,
            _fixture.CommandEnvironment,
            timeoutMs: 30_000);

        var configPath = Path.Join(appDir, "nuget.config");

        RunHelpers.RunDotnet(
            $"nuget config set globalPackagesFolder \"{_fixture.NugetPackagesDir}\" --configfile \"{configPath}\"",
            appDir,
            _fixture.CommandEnvironment,
            timeoutMs: 30_000);

        RunHelpers.RunDotnet(
            $"nuget add source \"{_fixture.PackageSourceDir}\" --name reactor-local --configfile \"{configPath}\"",
            appDir,
            _fixture.CommandEnvironment,
            timeoutMs: 30_000);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a just-exited build can still hold a file.
        }
    }
}