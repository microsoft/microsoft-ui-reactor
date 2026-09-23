using System.IO.Compression;
using Xunit;

namespace Microsoft.UI.Reactor.IntegrationTests.Packaging;

/// <summary>
/// Issue #1271 — the Reactor packages must not ship a <c>.pri</c>, and the compiled
/// <c>ReactorApplication.xbf</c> that replaced it must actually be in the package.
///
/// <para>Why a <c>.pri</c> beside the assembly is harmful: <c>ResolveAssemblyReference</c>
/// relates same-base-name sidecars to a reference (<c>AllowedReferenceRelatedFileExtensions</c>
/// defaults to <c>.pdb;.xml;.pri;…</c>), so a <c>lib/&lt;tfm&gt;/Reactor.pri</c> lands in
/// <c>_ReferenceRelatedPaths</c> / <c>ReferenceCopyLocalPaths</c>. The Windows App SDK's
/// <c>AddPriPayloadFilesToCopyToOutputDirectoryItems</c> then runs <c>makepri.exe Dump</c> on
/// every one of them, writing into <c>$(IntermediateOutputPath)</c>. Those output paths break
/// past MAX_PATH, so consumers failed to build at a depth where an equivalent XAML app built
/// fine — and <c>Microsoft.UI.Reactor.Devtools.pri.xml</c>, the longest of the three names, is
/// what tipped a borderline project over.</para>
///
/// <para>Measured when this was fixed: <c>Reactor.Advanced.pri</c> and
/// <c>Microsoft.UI.Reactor.Devtools.pri</c> were <em>entirely empty</em> (a resource map with no
/// <c>NamedResource</c>), and <c>Reactor.pri</c> held exactly one entry — an <c>EmbeddedData</c>
/// copy of <c>ReactorApplication.xbf</c>, which also ships loose. So nothing was lost.</para>
///
/// <para>Like its siblings in this collection, this needs network access to restore the
/// Windows App SDK, so it only runs where the feeds are reachable (CI's "Integration Tests" job).</para>
/// </summary>
[Collection(LocalPackageFeedCollection.Name)]
public sealed class PriPackagingTests : IDisposable
{
    private readonly TemplatePackageTestFixture _fixture;
    private readonly string _tempRoot = Path.Join(Path.GetTempPath(), $"reactor-pri-pkg-{Guid.NewGuid():N}");

    private static readonly string[] PackageIds =
    [
        "Microsoft.UI.Reactor",
        "Microsoft.UI.Reactor.Advanced",
        "Microsoft.UI.Reactor.Devtools",
    ];

    public PriPackagingTests(TemplatePackageTestFixture fixture)
    {
        _fixture = fixture;
        Directory.CreateDirectory(_tempRoot);
    }

    /// <summary>
    /// Structural half: no <c>.pri</c> anywhere in any of the three packages.
    ///
    /// <para>The mechanism that keeps it out is a single property
    /// (<c>DefaultAllowedOutputExtensionsInPackageBuildOutputFolder</c> minus <c>.pri</c>) in
    /// each csproj. Nothing fails if that property stops being honoured — NuGet would simply
    /// resume packing the file and consumers would silently regress — so this assertion is the
    /// only thing standing between a rename upstream and a re-broken package.</para>
    /// </summary>
    [Fact]
    public void PackagesShipNoPriFile()
    {
        foreach (var packageId in PackageIds)
        {
            var entries = ReadPackageEntries(packageId);

            // Positive control: the package was really opened and really has content. Without
            // this, an empty or unreadable archive would satisfy the "no .pri" assertion below.
            Assert.Contains(entries, e => e.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

            var pris = entries.Where(e => e.EndsWith(".pri", StringComparison.OrdinalIgnoreCase)).ToArray();
            Assert.True(
                pris.Length == 0,
                $"{packageId} ships {pris.Length} .pri file(s): {string.Join(", ", pris)}. " +
                "A .pri in lib/ is picked up as a reference-related path and fed to the Windows App SDK's " +
                "makepri.exe Dump expansion, whose output path breaks past MAX_PATH (issue #1271).");
        }
    }

    /// <summary>
    /// The other half, and the reason the first half is safe: the loose sidecars are still there.
    ///
    /// <para><c>ReactorApplication.xbf</c> is what <c>InitializeComponent()</c> resolves now that
    /// the <c>.pri</c>'s embedded copy is gone, and it reaches the package through a pack-time
    /// target rather than a <c>&lt;None&gt;</c> item — because a <c>&lt;None&gt;</c> guarded by
    /// <c>Exists()</c> is evaluated BEFORE the build produces the file, which silently shipped a
    /// package without it on a clean tree. Asserting "no .pri" without also asserting this would
    /// happily pass for a package that delivers neither copy.</para>
    /// </summary>
    [Fact]
    public void CorePackageStillShipsTheApplicationXamlSidecars()
    {
        var entries = ReadPackageEntries("Microsoft.UI.Reactor");

        Assert.Contains(entries, e => e.EndsWith("Reactor/Hosting/ReactorApplication.xbf", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, e => e.EndsWith("Reactor/Hosting/ReactorApplication.xaml", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every package must expose exactly ONE <c>lib/&lt;tfm&gt;</c> folder.
    ///
    /// <para>Packing a file with a literal <c>lib\$(TargetFramework)\…</c> path is the easy way to
    /// get this wrong: <c>$(TargetFramework)</c> is <c>net10.0-windows10.0.22621.0</c>, but NuGet
    /// shortens the build-output folder to <c>net10.0-windows10.0.22621</c>. Both parse to the
    /// same framework, so the package ends up with two sibling <c>lib</c> groups for one target —
    /// one holding the assemblies, one holding only a stray sidecar. It resolved correctly by
    /// luck of ordering rather than by rule, which is not a property worth relying on.</para>
    ///
    /// <para>Asserting the count (rather than a specific folder name) keeps this from breaking
    /// the next time the TFM or NuGet's shortening changes.</para>
    /// </summary>
    [Fact]
    public void PackagesExposeExactlyOneLibFolder()
    {
        foreach (var packageId in PackageIds)
        {
            var libFolders = ReadPackageEntries(packageId)
                .Where(e => e.StartsWith("lib/", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Split('/')[1])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // Positive control: a package with no lib/ at all would otherwise satisfy "not > 1".
            Assert.True(libFolders.Length == 1, $"{packageId} has {libFolders.Length} lib/ folder(s): {string.Join(", ", libFolders)}.");
        }
    }

    /// <summary>
    /// Behavioural half: a real consumer build no longer invokes <c>makepri.exe Dump</c> for any
    /// Reactor package.
    ///
    /// <para>This is what the structural test cannot show — that the expansion step is actually
    /// gone from the consumer's build rather than merely that one file moved. Before the fix this
    /// build logged six Reactor dumps (three packages, reached through both the reference path and
    /// the payload path); after it, zero.</para>
    ///
    /// <para>The assertion carries its own positive control: the Windows App SDK's own framework
    /// PRIs are still dumped and the app still runs <c>makepri.exe New</c> for its own resources.
    /// If the probe were simply failing to match — a changed log format, a quieter verbosity, a
    /// build that died early — those counts would be zero too, and the test fails instead of
    /// reporting a false success.</para>
    /// </summary>
    [Fact]
    public void ConsumerBuildRunsNoMakepriDumpForReactorPackages()
    {
        var appDir = Path.Join(_tempRoot, "consumer");
        Directory.CreateDirectory(appDir);

        WriteConsumerProject(appDir);
        WriteConsumerProgram(appDir);
        CreateNuGetConfig(appDir);

        var build = RunHelpers.RunProcess(
            "dotnet",
            $"build -c Debug -a {_fixture.RunArchitecture} -v:normal",
            appDir,
            _fixture.CommandEnvironment,
            timeoutMs: 600_000,
            throwOnFailure: true).Stdout;

        var dumpLines = build
            .Split('\n')
            .Where(l => l.Contains("makepri.exe Dump", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // Positive controls FIRST: prove the probe can match in this log before trusting a zero.
        Assert.True(
            dumpLines.Length > 0,
            "No 'makepri.exe Dump' line at all. The Windows App SDK dumps its own framework PRIs, " +
            "so zero means the probe stopped matching (log format or verbosity changed) rather " +
            "than that Reactor stopped contributing PRIs.");
        Assert.Contains("makepri.exe New", build, StringComparison.OrdinalIgnoreCase);

        var reactorDumps = dumpLines
            .Where(l => l.Contains("microsoft.ui.reactor", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(
            reactorDumps.Length == 0,
            $"Consumer build ran makepri.exe Dump on {reactorDumps.Length} Reactor package PRI(s) " +
            $"(issue #1271):{Environment.NewLine}{string.Join(Environment.NewLine, reactorDumps)}");
    }

    /// <summary>
    /// WindowsAppSDK#6394 — the package's targets must put the CONSUMING app's own `.pri`
    /// (and the `.xbf` sidecars) into its publish output.
    ///
    /// <para>The Windows App SDK generates the app's resource index into the build output but
    /// never copies it to publish for an unpackaged app, and the published app then dies at
    /// startup with <c>0xC000027B</c> inside native XAML. Reactor ships
    /// <c>_ReactorCopyWinUIResourcesToPublish</c> to close that gap for every consumer; this
    /// test is what keeps it working, since nothing else fails if the target stops running —
    /// the build stays green and only the published app breaks.</para>
    ///
    /// <para>The opt-out property doubles as the control: it must reproduce the missing file.
    /// Without that half, a publish that happened to include the <c>.pri</c> for some unrelated
    /// reason would let this pass while the target did nothing.</para>
    /// </summary>
    [Fact]
    public void ConsumerPublishIncludesTheAppPriAndXbfSidecars()
    {
        var appDir = Path.Join(_tempRoot, "publish-consumer");
        Directory.CreateDirectory(appDir);

        WriteConsumerProject(appDir);
        WriteConsumerProgram(appDir);
        CreateNuGetConfig(appDir);

        var publishDir = Path.Join(appDir, "out-default");
        RunHelpers.RunDotnet(
            $"publish -c Release -a {_fixture.RunArchitecture} -o \"{publishDir}\"",
            appDir,
            _fixture.CommandEnvironment,
            timeoutMs: 600_000);

        Assert.True(
            File.Exists(Path.Join(publishDir, "Consumer.pri")),
            "The app's own .pri is missing from the publish output, so the published app would " +
            "start with an empty resource dictionary and crash in native XAML (WindowsAppSDK#6394). " +
            "Reactor's _ReactorCopyWinUIResourcesToPublish target is supposed to copy it.");

        Assert.True(
            File.Exists(Path.Join(publishDir, "Reactor", "Hosting", "ReactorApplication.xbf")),
            "ReactorApplication.xbf is missing from the publish output, and its relative " +
            "Reactor/Hosting/ path must be preserved for ms-appx:/// to resolve it.");

        // Control: with the target disabled the file must disappear again. If it does not,
        // something else is supplying it and the assertions above prove nothing about the target.
        var optOutDir = Path.Join(appDir, "out-optout");
        RunHelpers.RunDotnet(
            $"publish -c Release -a {_fixture.RunArchitecture} -o \"{optOutDir}\" -p:ReactorCopyWinUIResourcesToPublish=false",
            appDir,
            _fixture.CommandEnvironment,
            timeoutMs: 600_000);

        Assert.False(
            File.Exists(Path.Join(optOutDir, "Consumer.pri")),
            "Opting out of _ReactorCopyWinUIResourcesToPublish still produced the app .pri, so the " +
            "passing assertion above does not actually attribute the copy to Reactor's target.");
    }

    private string[] ReadPackageEntries(string packageId)
    {
        var packagePath = Path.Join(_fixture.PackageSourceDir, $"{packageId}.{_fixture.PackageVersion}.nupkg");
        Assert.True(File.Exists(packagePath), $"Expected package '{packagePath}' to exist.");

        using var archive = ZipFile.OpenRead(packagePath);
        return archive.Entries.Select(e => e.FullName).ToArray();
    }

    /// <summary>
    /// References all three packages explicitly. Before the fix each contributed its own
    /// <c>makepri.exe Dump</c>, so covering only the core package would leave the two whose PRIs
    /// were empty — including the Devtools one with the longest, build-breaking file name —
    /// untested.
    /// </summary>
    private void WriteConsumerProject(string appDir)
    {
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
                <PackageReference Include="Microsoft.UI.Reactor.Advanced" Version="{_fixture.PackageVersion}" />
                <PackageReference Include="Microsoft.UI.Reactor.Devtools" Version="{_fixture.PackageVersion}" />
              </ItemGroup>
            </Project>
            """;

        File.WriteAllText(Path.Join(appDir, "Consumer.csproj"), csproj);
    }

    private static void WriteConsumerProgram(string appDir)
    {
        // OutputType=Exe is what makes the Windows App SDK compute input PRIs at all
        // (ShouldComputeInputPris), so the app must be an executable. It is never run here —
        // this test is about the build, and CreateTemplateTests already covers running.
        File.WriteAllText(Path.Join(appDir, "Program.cs"), """
            using static Microsoft.UI.Reactor.Factories;

            System.Console.WriteLine(TextBlock("hello") is not null);

            """);
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
        catch
        {
            // Best-effort cleanup for temporary test artifacts.
        }
    }
}
