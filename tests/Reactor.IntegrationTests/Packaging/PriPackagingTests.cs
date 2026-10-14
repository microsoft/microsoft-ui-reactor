using System.IO.Compression;
using System.Text.Json;
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
    private readonly LocalPackageFeedFixture _fixture;
    private readonly string _tempRoot = Path.Join(Path.GetTempPath(), $"rpri-{Guid.NewGuid():N}");

    private static readonly string[] PackageIds =
    [
        "Microsoft.UI.Reactor",
        "Microsoft.UI.Reactor.Advanced",
        "Microsoft.UI.Reactor.Devtools",
    ];

    public PriPackagingTests(LocalPackageFeedFixture fixture)
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
    /// WindowsAppSDK#6394 — the CONSUMING app's own <c>.pri</c> must reach its publish output.
    ///
    /// <para>An unpackaged app builds a <c>.pri</c> that embeds its compiled XAML and, when
    /// self-contained, WinUI's own <c>generic.xbf</c> / <c>themeresources.xbf</c>. Build tools
    /// before <c>Microsoft.Windows.SDK.BuildTools.MSIX</c> 1.7.260930100 never copy it to
    /// publish, and the published self-contained app dies at startup with <c>0xC000027B</c>.
    /// The Windows App SDK still resolves an older version on its own, so Reactor's package
    /// declares the fixed one as a dependency floor. Nothing else fails if that floor is lost —
    /// the build stays green and only the published app breaks — so this test is what keeps it.</para>
    ///
    /// <para>The control pins the same consumer back to the version the Windows App SDK resolves
    /// alone (suppressing the <c>NU1605</c> downgrade warning that a direct reference below a
    /// dependency's minimum raises), so the build-tools version is the only variable between the
    /// two publishes. It must reproduce the missing file. Without that half, a publish that
    /// produced the <c>.pri</c> for an unrelated reason would let this pass while the floor did
    /// nothing.</para>
    ///
    /// <para>The two publishes deliberately run from SEPARATE project directories. Publishing the
    /// same project twice with a different global property forces a full re-evaluation that reuses
    /// the same <c>obj/</c>, and the second XAML compile then failed on CI with
    /// <c>WMC1006: Cannot resolve Assembly or Windows Metadata file</c> — the reference path under
    /// the fixture's package folder is ~258 characters, and the XAML compiler is not long-path
    /// aware. One publish per directory stays clear of both.</para>
    /// </summary>
    [Fact]
    public void ConsumerPublishIncludesTheAppPri()
    {
        // Short directory names on purpose: these paths feed the XAML compiler, which fails
        // around MAX_PATH, and the fixture's package folder already consumes most of the budget.
        var floorDir = Path.Join(_tempRoot, "on");
        var downgradedDir = Path.Join(_tempRoot, "off");
        Directory.CreateDirectory(floorDir);
        Directory.CreateDirectory(downgradedDir);

        // Core package only. The publish path is the one that runs the XAML compiler against
        // the full reference closure, and dragging in Advanced (Win2D) and Devtools adds
        // references without adding coverage — the floor under test is in the core package.
        WriteConsumerProject(floorDir, coreOnly: true);
        WriteConsumerProject(downgradedDir, coreOnly: true, buildToolsMsixVersion: BuildToolsMsixWithoutPublishFix);
        foreach (var dir in new[] { floorDir, downgradedDir })
        {
            WriteConsumerProgram(dir);
            CreateNuGetConfig(dir);
        }

        var publishDir = Path.Join(floorDir, "pub");
        RunHelpers.RunDotnet(
            $"publish -c Release -a {_fixture.RunArchitecture} -o \"{publishDir}\"",
            floorDir,
            _fixture.CommandEnvironment,
            timeoutMs: 600_000);

        Assert.True(
            File.Exists(Path.Join(publishDir, "Consumer.pri")),
            "The app's own .pri is missing from the publish output, so the published app would " +
            "crash in native XAML at startup (WindowsAppSDK#6394). Reactor's dependency on " +
            $"{BuildToolsMsixId} is supposed to bring the fix; the consumer resolved " +
            $"{ResolvedBuildToolsMsix(floorDir)}.");

        var downgradedPublishDir = Path.Join(downgradedDir, "pub");
        RunHelpers.RunDotnet(
            $"publish -c Release -a {_fixture.RunArchitecture} -o \"{downgradedPublishDir}\"",
            downgradedDir,
            _fixture.CommandEnvironment,
            timeoutMs: 600_000);

        Assert.False(
            File.Exists(Path.Join(downgradedPublishDir, "Consumer.pri")),
            $"Pinned back to {BuildToolsMsixId} {BuildToolsMsixWithoutPublishFix} (resolved " +
            $"{ResolvedBuildToolsMsix(downgradedDir)}), the publish still produced the app .pri, so " +
            "the passing assertion above does not attribute it to Reactor's dependency floor.");
    }

    private const string BuildToolsMsixId = "Microsoft.Windows.SDK.BuildTools.MSIX";

    // What Microsoft.WindowsAppSDK.Base still resolves on its own, and the last version
    // without the WindowsAppSDK#6394 publish fix.
    private const string BuildToolsMsixWithoutPublishFix = "1.7.251221100";

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

    private string[] ReadPackageEntries(string packageId)
    {
        var packagePath = Path.Join(_fixture.PackageSourceDir, $"{packageId}.{_fixture.PackageVersion}.nupkg");
        Assert.True(File.Exists(packagePath), $"Expected package '{packagePath}' to exist.");

        using var archive = ZipFile.OpenRead(packagePath);
        return archive.Entries.Select(e => e.FullName).ToArray();
    }

    /// <summary>
    /// References all three packages by default. Before the fix each contributed its own
    /// <c>makepri.exe Dump</c>, so covering only the core package would leave the two whose PRIs
    /// were empty — including the Devtools one with the longest, build-breaking file name —
    /// untested. <paramref name="coreOnly"/> trims that down for the publish test, which does not
    /// need them and is sensitive to how many references the XAML compiler has to resolve.
    /// <paramref name="buildToolsMsixVersion"/> pins the build tools below Reactor's floor for
    /// the publish test's control.
    ///
    /// <para><c>WindowsAppSDKSelfContained=true</c> is deliberate, not incidental, for two
    /// reasons. It is the deployment in which a missing app <c>.pri</c> crashes the published app
    /// (<c>0xC000027B</c>), because the <c>.pri</c> also carries WinUI's own XAML; a
    /// framework-dependent app loads that from the installed runtime and loses only its own XAML.
    /// And it is what makes the Windows App SDK dump its framework PRIs at all, which is the
    /// positive control in <see cref="ConsumerBuildRunsNoMakepriDumpForReactorPackages"/>:
    /// measured, a framework-dependent consumer dumps none, so that control would have nothing to
    /// match. Do not "simplify" this to the framework-dependent default.</para>
    /// </summary>
    private void WriteConsumerProject(string appDir, bool coreOnly = false, string? buildToolsMsixVersion = null)
    {
        var extraPackages = coreOnly
            ? string.Empty
            : $"""

                    <PackageReference Include="Microsoft.UI.Reactor.Advanced" Version="{_fixture.PackageVersion}" />
                    <PackageReference Include="Microsoft.UI.Reactor.Devtools" Version="{_fixture.PackageVersion}" />
              """;

        // A direct reference below a dependency's minimum is an NU1605 downgrade, which fails the
        // restore by default; the control wants exactly that downgrade.
        var buildToolsPin = buildToolsMsixVersion is null
            ? string.Empty
            : $"""

                    <PackageReference Include="{BuildToolsMsixId}" Version="{buildToolsMsixVersion}" />
              """;
        var noWarn = buildToolsMsixVersion is null
            ? string.Empty
            : """

                    <NoWarn>$(NoWarn);NU1605</NoWarn>
              """;

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
                <RuntimeIdentifier>win-{_fixture.RunArchitecture}</RuntimeIdentifier>{noWarn}
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.UI.Reactor" Version="{_fixture.PackageVersion}" />{extraPackages}{buildToolsPin}
              </ItemGroup>
            </Project>
            """;

        File.WriteAllText(Path.Join(appDir, "Consumer.csproj"), csproj);
    }

    private static void WriteConsumerProgram(string appDir)
    {
        // OutputType=Exe is what makes the Windows App SDK compute input PRIs at all
        // (ShouldComputeInputPris), so the app must be an executable. It is never run here:
        // these tests assert on the build and publish layout.
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup for temporary test artifacts. These two cover what
            // Directory.Delete realistically throws here (a file still held by a just-exited
            // build process, or a read-only artifact); anything else is a real bug and
            // should not be swallowed.
        }
    }
}
