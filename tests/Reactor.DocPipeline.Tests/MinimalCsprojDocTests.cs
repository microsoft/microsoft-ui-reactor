using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Guards the "Minimal Project Setup" csproj published on the docset landing
/// page (<c>docs/_pipeline/templates/index.md.dt</c>). That block is the very
/// first thing a new user copies, and nothing else in the pipeline validates
/// it: it is a fenced <c>xml</c> block, so no compiler ever sees it, and it
/// lives inside <c>ai:lock</c> markers, which authoring passes skip by design.
/// <para>
/// It shipped broken for exactly that reason — it told readers to consume
/// Reactor via <c>&lt;ProjectReference Include="..\Reactor\Reactor.csproj" /&gt;</c>,
/// a path that does not exist outside this repository, so the documented
/// first-run could not work for anyone. These assertions encode the shape that
/// was verified by actually restoring and building the extracted block.
/// </para>
/// <para>
/// Every assertion below fails if its specific defect returns. A build fixture
/// was considered instead and rejected: it would couple CI to a
/// <c>Microsoft.UI.Reactor</c> version that is not yet published at release
/// time. This runs offline with no feed and no release-ordering hazard.
/// </para>
/// </summary>
public class MinimalCsprojDocTests
{
    private const string PackageId = "Microsoft.UI.Reactor";

    [Fact]
    public void Documented_csproj_block_is_present_and_well_formed_xml()
    {
        // Guards the extractor itself. If the heading is renamed or the block
        // deleted, every other test here would vacuously pass on an empty
        // string — so failing loudly here is what makes the rest meaningful.
        var xml = ExtractMinimalSetupCsproj();

        Assert.False(string.IsNullOrWhiteSpace(xml));
        var doc = global::System.Xml.Linq.XDocument.Parse(xml);
        Assert.Equal("Project", doc.Root!.Name.LocalName);
    }

    [Fact]
    public void Documented_csproj_references_the_published_package_not_a_repo_path()
    {
        // THE original defect. A ProjectReference to ..\Reactor\Reactor.csproj
        // resolves only inside this repository; an external reader's build fails
        // outright. Consumption must be via the published package.
        var doc = global::System.Xml.Linq.XDocument.Parse(ExtractMinimalSetupCsproj());

        var projectRefs = doc.Descendants("ProjectReference").ToList();
        Assert.True(
            projectRefs.Count == 0,
            "docs/_pipeline/templates/index.md.dt 'Minimal Project Setup' must not use ProjectReference — " +
            "that path does not exist for a reader outside this repo. Use " +
            $"<PackageReference Include=\"{PackageId}\" Version=\"{DocAssembler.VersionToken}\" />. " +
            "Found: " + string.Join(", ", projectRefs.Select(r => r.Attribute("Include")?.Value)));

        var packageIds = doc.Descendants("PackageReference")
            .Select(p => p.Attribute("Include")?.Value)
            .ToList();
        Assert.Contains(PackageId, packageIds);
    }

    [Fact]
    public void Documented_csproj_pins_the_version_token_not_a_hardcoded_literal()
    {
        // A literal version silently goes stale at the next release. The
        // {{reactorVersion}} token is substituted from <ReactorPublicVersion>,
        // which is what a release bumps.
        var doc = global::System.Xml.Linq.XDocument.Parse(ExtractMinimalSetupCsproj());

        var version = doc.Descendants("PackageReference")
            .Single(p => p.Attribute("Include")?.Value == PackageId)
            .Attribute("Version")?.Value;

        Assert.Equal(DocAssembler.VersionToken, version);
    }

    [Fact]
    public void Documented_csproj_keeps_the_runtime_identifier_line()
    {
        // Load-bearing and non-obvious: without this, a bare `dotnet build` or
        // `dotnet run` — which the very next sentence of the page tells the
        // reader to run — fails with "WindowsAppSDKSelfContained requires a
        // supported Windows architecture". Adding <Platforms> instead does NOT
        // fix it; that was measured against a real build before this shipped.
        var doc = global::System.Xml.Linq.XDocument.Parse(ExtractMinimalSetupCsproj());

        Assert.True(
            doc.Descendants("RuntimeIdentifier").Any(),
            "The documented minimal csproj must keep its RuntimeIdentifier auto-resolve line, or a bare " +
            "`dotnet run` fails with 'WindowsAppSDKSelfContained requires a supported Windows architecture'.");
    }

    [Theory]
    [InlineData("OutputType", "WinExe")]
    [InlineData("UseWinUI", "true")]
    [InlineData("WindowsPackageType", "None")]
    public void Documented_csproj_declares_required_winui_properties(string element, string expected)
    {
        var doc = global::System.Xml.Linq.XDocument.Parse(ExtractMinimalSetupCsproj());

        var value = doc.Descendants(element).FirstOrDefault()?.Value?.Trim();
        Assert.Equal(expected, value);
    }

    [Fact]
    public void Documented_package_id_matches_the_scaffolded_template()
    {
        // Cross-check against the real `dotnet new reactorapp` template, which
        // the page names as the fast path. If the two ever disagree on which
        // package to reference, one of them is lying to the reader.
        var repoRoot = FindRepoRoot();
        var templateCsproj = global::System.IO.Path.Join(
            repoRoot, "tools", "Templates", "templates", "WinUIApp-CSharp", "Company.ReactorApp1.csproj");

        Assert.True(global::System.IO.File.Exists(templateCsproj),
            $"Expected the scaffolded template at {templateCsproj}; if it moved, update this test.");

        var scaffoldIds = global::System.Xml.Linq.XDocument.Load(templateCsproj)
            .Descendants("PackageReference")
            .Select(p => p.Attribute("Include")?.Value)
            .ToList();

        // Positive control: the scaffold really does carry package references,
        // so a miss below is a disagreement and not an empty parse.
        Assert.NotEmpty(scaffoldIds);
        Assert.Contains(PackageId, scaffoldIds);
    }

    [Fact]
    public void Generated_index_substitutes_the_version_and_leaks_no_token()
    {
        // End-to-end: the committed guide page a reader actually lands on must
        // show a concrete version, never the raw token.
        var repoRoot = FindRepoRoot();
        var generated = global::System.IO.File.ReadAllText(
            global::System.IO.Path.Join(repoRoot, "docs", "guide", "index.md"));

        var expectedVersion = VersionSource.ReadPublicVersion(repoRoot);

        Assert.Contains($"<PackageReference Include=\"{PackageId}\" Version=\"{expectedVersion}\" />", generated);
        Assert.DoesNotContain(DocAssembler.VersionToken, generated);
    }

    /// <summary>
    /// Pulls the fenced <c>xml</c> block that follows the "Minimal Project
    /// Setup" heading out of the landing-page template.
    /// </summary>
    private static string ExtractMinimalSetupCsproj()
    {
        var repoRoot = FindRepoRoot();
        var templatePath = global::System.IO.Path.Join(
            repoRoot, "docs", "_pipeline", "templates", "index.md.dt");

        var lines = global::System.IO.File.ReadAllLines(templatePath);

        var headingIndex = global::System.Array.FindIndex(
            lines, l => l.TrimStart('#', ' ').Trim().Equals("Minimal Project Setup", global::System.StringComparison.OrdinalIgnoreCase));

        Assert.True(headingIndex >= 0,
            "Could not find a 'Minimal Project Setup' heading in index.md.dt. If the section was renamed, " +
            "update this test rather than deleting it — the block it guards is the docset's most-copied code.");

        var open = global::System.Array.FindIndex(
            lines, headingIndex, l => l.TrimStart().StartsWith("```xml", global::System.StringComparison.Ordinal));
        Assert.True(open > headingIndex, "No fenced ```xml block found after the 'Minimal Project Setup' heading.");

        var close = global::System.Array.FindIndex(
            lines, open + 1, l => l.TrimStart().StartsWith("```", global::System.StringComparison.Ordinal));
        Assert.True(close > open, "Unterminated ```xml block after the 'Minimal Project Setup' heading.");

        return string.Join("\n", lines[(open + 1)..close]);
    }

    private static string FindRepoRoot()
    {
        var dir = new global::System.IO.DirectoryInfo(global::System.AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (global::System.IO.File.Exists(global::System.IO.Path.Join(dir.FullName, "Reactor.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new global::System.InvalidOperationException("Could not locate repo root (Reactor.slnx) from test base dir.");
    }
}
