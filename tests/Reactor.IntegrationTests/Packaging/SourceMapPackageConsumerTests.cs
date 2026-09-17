using Xunit;

namespace Microsoft.UI.Reactor.IntegrationTests.Packaging;

/// <summary>
/// Spec 010 — proves source mapping survives the PACKAGE delivery path.
///
/// <para>Every other source-map test loads the generator through a direct
/// <c>ProjectReference</c>, which is not how a consumer receives it. A consumer gets it
/// from <c>buildTransitive/sourcemap/</c> inside the nupkg, added as an <c>&lt;Analyzer&gt;</c> by
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
[Collection(LocalPackageFeedCollection.Name)]
public sealed class SourceMapPackageConsumerTests : IDisposable
{
    private readonly TemplatePackageTestFixture _fixture;
    private readonly string _tempRoot = Path.Join(Path.GetTempPath(), $"reactor-sourcemap-pkg-{Guid.NewGuid():N}");

    public SourceMapPackageConsumerTests(TemplatePackageTestFixture fixture)
    {
        _fixture = fixture;
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void PackageConsumerGetsStampedCallSitesInDebugButNotRelease()
    {
        var appDir = Path.Join(_tempRoot, "consumer");
        Directory.CreateDirectory(appDir);

        WriteConsumerProject(appDir);
        WriteConsumerProgram(appDir);
        CreateNuGetConfig(appDir);

        // Debug: the package's targets default ReactorSourceMap to true, add the
        // generator from buildTransitive/sourcemap, and the interceptor stamps the call site.
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
    /// The TRANSITIVE delivery path: an app that never names Microsoft.UI.Reactor,
    /// receiving it only through an intermediate library package.
    ///
    /// <para>NuGet flows <c>build/</c> to a direct <c>PackageReference</c> only, so this
    /// case is served by <c>buildTransitive/</c> and nothing else. It needs its own test
    /// because the targets guard the analyzer with <c>Exists(...)</c>: a wrong or missing
    /// transitive path does not fail the build, it silently produces null locations
    /// everywhere — which the direct-consumer test above cannot detect.</para>
    /// </summary>
    [Fact]
    public void TransitiveConsumerAlsoGetsStampedCallSites()
    {
        // 1. An intermediate library that references Reactor directly, packed to the
        //    same local feed the fixture already set up.
        var libDir = Path.Join(_tempRoot, "intermediate");
        Directory.CreateDirectory(libDir);
        WriteIntermediateLibrary(libDir);
        CreateNuGetConfig(libDir);
        RunHelpers.RunDotnet(
            $"pack -c Release -o \"{_fixture.PackageSourceDir}\"",
            libDir,
            _fixture.CommandEnvironment,
            timeoutMs: 420_000);

        // 2. A downstream app that references ONLY that library. Reactor arrives
        //    transitively, so build/ never applies to it.
        var appDir = Path.Join(_tempRoot, "downstream");
        Directory.CreateDirectory(appDir);
        WriteDownstreamProject(appDir);
        WriteConsumerProgram(appDir);
        CreateNuGetConfig(appDir);

        var debug = RunConsumer(appDir, "Debug");
        Assert.Contains($"CALLSITE=Program.cs:{TextBlockCallLine}", debug, StringComparison.Ordinal);

        var release = RunConsumer(appDir, "Release");
        Assert.Contains("CALLSITE=<null>", release, StringComparison.Ordinal);
    }

    private void WriteIntermediateLibrary(string libDir)
    {
        var csproj = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
                <Nullable>enable</Nullable>
                <UseWinUI>true</UseWinUI>
                <!-- A library must not own self-contained WindowsAppSDK packaging. -->
                <WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>
                <PackageId>Reactor.SourceMap.IntermediateLib</PackageId>
                <Version>{_fixture.PackageVersion}</Version>
                <Platforms>{_fixture.RunArchitecture}</Platforms>
                <Platform>{_fixture.RunArchitecture}</Platform>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.UI.Reactor" Version="{_fixture.PackageVersion}" />
              </ItemGroup>
            </Project>
            """;

        File.WriteAllText(Path.Join(libDir, "IntermediateLib.csproj"), csproj);

        // Uses the DSL so the reference is real rather than declarative.
        File.WriteAllText(Path.Join(libDir, "Widget.cs"), """
            using Microsoft.UI.Reactor.Core;

            namespace Reactor.SourceMap.IntermediateLib;

            public static class Widget
            {
                public static Element Build() => Microsoft.UI.Reactor.Factories.TextBlock("from-lib");
            }
            """);
    }

    private void WriteDownstreamProject(string appDir)
    {
        // Note: NO PackageReference to Microsoft.UI.Reactor. That is the whole point.
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
                <PackageReference Include="Reactor.SourceMap.IntermediateLib" Version="{_fixture.PackageVersion}" />
              </ItemGroup>
            </Project>
            """;

        File.WriteAllText(Path.Join(appDir, "Downstream.csproj"), csproj);
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

        File.WriteAllText(Path.Join(appDir, "Program.cs"), program);
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

        File.WriteAllText(Path.Join(appDir, "Consumer.csproj"), csproj);
    }

    private string RunConsumer(string appDir, string configuration, string? extraProperty = null)
        => RunHelpers.RunProcess(
            "dotnet",
            $"run -c {configuration} -a {_fixture.RunArchitecture}{(extraProperty is null ? "" : " " + extraProperty)}",
            appDir,
            _fixture.CommandEnvironment,
            timeoutMs: 420_000,
            throwOnFailure: true).Stdout;

    /// <summary>
    /// The documented overrides, in both directions.
    ///
    /// <para>The Debug/Release rows are defaults, not a configuration lock: the targets
    /// gate on <c>ReactorSourceMap</c> alone. The Release opt-in is the safety-sensitive
    /// one — it is what embeds mapped source paths into a shipped binary — so if it
    /// silently stopped loading the packaged analyzer, the guide would be promising
    /// something that no longer happens while the default-path tests stayed green.</para>
    /// </summary>
    [Fact]
    public void ExplicitOverridesWinOverTheConfigurationDefault()
    {
        var appDir = Path.Join(_tempRoot, "overrides");
        Directory.CreateDirectory(appDir);

        WriteConsumerProject(appDir);
        WriteConsumerProgram(appDir);
        CreateNuGetConfig(appDir);

        // Release + explicit true: generates interceptors despite the Release default.
        var releaseOptIn = RunConsumer(appDir, "Release", "-p:ReactorSourceMap=true");
        Assert.Contains($"CALLSITE=Program.cs:{TextBlockCallLine}", releaseOptIn, StringComparison.Ordinal);

        // Debug + explicit false: the symmetric opt-out, so this is not just asserting
        // that the property is read in one direction.
        var debugOptOut = RunConsumer(appDir, "Debug", "-p:ReactorSourceMap=false");
        Assert.Contains("CALLSITE=<null>", debugOptOut, StringComparison.Ordinal);
    }

    /// <summary>
    /// A consumer pinned to an older C# language version still builds, and still gets
    /// stamped call sites.
    ///
    /// <para>The concern this answers: the package turns the interceptor generator on by
    /// default in Debug, so if <c>[InterceptsLocation]</c> required the latest language
    /// version, simply upgrading Reactor would break any consumer pinned to an older
    /// <c>LangVersion</c> — a silent breaking change for a supported project shape.</para>
    ///
    /// <para>Measured, not assumed: interceptors are gated by the
    /// <c>InterceptorsNamespaces</c> opt-in the targets already supply, not by
    /// <c>LangVersion</c>, so this passes today. It is here so that if a future Roslyn
    /// ever adds a language-version gate, CI reports it as the consumer-breaking change
    /// it would be, rather than a user discovering it on upgrade.</para>
    /// </summary>
    [Fact]
    public void ConsumerPinnedToAnOlderLangVersionStillBuildsAndGetsStampedCallSites()
    {
        var appDir = Path.Join(_tempRoot, "langversion");
        Directory.CreateDirectory(appDir);

        WriteConsumerProject(appDir);
        WriteConsumerProgram(appDir);
        CreateNuGetConfig(appDir);

        // Debug, so the generator is on by DEFAULT — the upgrade path a pinned consumer
        // would actually hit, rather than one they opted into.
        var pinned = RunConsumer(appDir, "Debug", "-p:LangVersion=13");

        Assert.Contains($"CALLSITE=Program.cs:{TextBlockCallLine}", pinned, StringComparison.Ordinal);
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
        catch (IOException)
        {
            // Best-effort cleanup for temporary smoke-test artifacts. A locked file
            // (a build server still releasing handles) must not fail a passing test.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: a read-only artifact left by the SDK is not a test failure.
        }
    }
}
