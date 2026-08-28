using Xunit;

namespace Microsoft.UI.Reactor.IntegrationTests.Packaging;

/// <summary>
/// Spec 010 — proves source mapping survives the PACKAGE delivery path.
///
/// <para>Every other source-map test loads the generator through a direct
/// <c>ProjectReference</c>, which is not how a consumer receives it. A consumer gets it
/// from <c>build/sourcemap/</c> inside the nupkg, added as an <c>&lt;Analyzer&gt;</c> by
/// <c>build/Microsoft.UI.Reactor.targets</c>. That wiring is guarded by
/// <c>Exists(...)</c>, so a packing or path regression does not fail — it silently
/// disables mapping, and every ProjectReference-based test stays green. This is the only
/// test that can catch that class of break.</para>
///
/// <para>Both directions are asserted in one build pair, because either alone is weak: a
/// Debug-only check passes if the generator is unconditionally on, and a Release-only
/// check passes if it is unconditionally off.</para>
///
/// <para>Like its sibling <see cref="CreateTemplateTests"/> this needs network access to
/// restore the Windows App SDK, so it only runs where NuGet.org is reachable (CI's
/// "Integration Tests" job). On a network-restricted machine both fail identically with
/// NU1301 during restore.</para>
/// </summary>
public sealed class SourceMapPackageConsumerTests : IClassFixture<TemplatePackageTestFixture>, IDisposable
{
    private readonly TemplatePackageTestFixture _fixture;
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"reactor-sourcemap-pkg-{Guid.NewGuid():N}");

    public SourceMapPackageConsumerTests(TemplatePackageTestFixture fixture)
    {
        _fixture = fixture;
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void PackageConsumerGetsStampedCallSitesInDebugButNotRelease()
    {
        var appDir = Path.Combine(_tempRoot, "consumer");
        Directory.CreateDirectory(appDir);

        WriteConsumerProject(appDir);
        WriteConsumerProgram(appDir);
        CreateNuGetConfig(appDir);

        // Debug: the package's targets default ReactorSourceMap to true, add the
        // generator from build/sourcemap, and the interceptor stamps the call site.
        var debug = RunConsumer(appDir, "Debug");
        Assert.Contains("CALLSITE=Program.cs:", debug, StringComparison.Ordinal);

        // The stamped line must be the DSL call site, not merely non-empty. The marker
        // in Program.cs pins it, so a generator that stamped a constant fails here.
        Assert.Contains($"CALLSITE=Program.cs:{TextBlockCallLine}", debug, StringComparison.Ordinal);

        // Release: no generator is added, so nothing stamps and no source path is
        // embedded in the shipped binary.
        var release = RunConsumer(appDir, "Release");
        Assert.Contains("CALLSITE=<null>", release, StringComparison.Ordinal);
    }

    /// <summary>
    /// 1-based line of the <c>TextBlock("hello")</c> call in the generated Program.cs.
    /// Kept adjacent to the writer below so the two cannot drift apart unnoticed.
    /// </summary>
    private const int TextBlockCallLine = 7;

    private static void WriteConsumerProgram(string appDir)
    {
        // NOTE: TextBlock("hello") must stay on line 7 — see TextBlockCallLine.
        var program = """
            using Microsoft.UI.Reactor.Core;
            using Microsoft.UI.Reactor.Diagnostics;
            using static Microsoft.UI.Reactor.Factories;

            ReactorSourceMap.Enabled = true;

            Element el = TextBlock("hello");

            var cs = el.CallSite;
            System.Console.WriteLine(cs is null
                ? "CALLSITE=<null>"
                : $"CALLSITE={System.IO.Path.GetFileName(cs.Value.FilePath)}:{cs.Value.LineNumber}");

            """;

        File.WriteAllText(Path.Combine(appDir, "Program.cs"), program);
    }

    private void WriteConsumerProject(string appDir)
    {
        // A plain console app, not the WinUI template: this test is about the package's
        // build wiring, and the assertion only needs an Element record, which does not
        // require a UI thread. Keeping it headless also keeps it runnable in CI.
        var csproj = $"""
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
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.UI.Reactor" Version="{_fixture.PackageVersion}" />
              </ItemGroup>
            </Project>
            """;

        File.WriteAllText(Path.Combine(appDir, "Consumer.csproj"), csproj);
    }

    private string RunConsumer(string appDir, string configuration)
        => RunHelpers.RunProcess(
            "dotnet",
            $"run -c {configuration} -a {_fixture.RunArchitecture}",
            appDir,
            _fixture.CommandEnvironment,
            timeoutMs: 420_000,
            throwOnFailure: true).Stdout;

    private void CreateNuGetConfig(string appDir)
    {
        RunHelpers.RunDotnet(
            $"new nugetconfig --output \"{appDir}\" --force",
            appDir,
            _fixture.CommandEnvironment,
            timeoutMs: 30_000);

        var configPath = Path.Combine(appDir, "nuget.config");

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
        catch
        {
            // Best-effort cleanup for temporary smoke-test artifacts.
        }
    }
}
