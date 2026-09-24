using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.UI.Reactor.SearchIndex;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Tooling;

/// <summary>
/// A throwaway on-disk gallery (ControlRegistry.cs + PageRouter.cs + ControlPages) that only
/// needs to *parse*, not compile — it lets the generator's editorial-merge, keyword-normalize,
/// override, and skip/throw paths be exercised in isolation without depending on the real
/// 93-control gallery. Deletes itself on Dispose.
/// </summary>
sealed class MiniGallery : IDisposable
{
    public string Root { get; }
    public string GalleryDir { get; }

    public MiniGallery(bool betaRouted)
    {
        Root = Path.Join(Path.GetTempPath(), "reactor-si-" + Guid.NewGuid().ToString("N"));
        GalleryDir = Path.Join(Root, "gallery");
        Directory.CreateDirectory(Path.Join(GalleryDir, "ControlPages"));
        File.WriteAllText(Path.Join(GalleryDir, "ControlRegistry.cs"), Registry);
        File.WriteAllText(Path.Join(GalleryDir, "PageRouter.cs"), betaRouted ? RouterBoth : RouterAlphaOnly);
        File.WriteAllText(Path.Join(GalleryDir, "ControlPages", "AlphaPage.cs"), AlphaPage);
        File.WriteAllText(Path.Join(GalleryDir, "ControlPages", "BetaPage.cs"), BetaPage);
    }

    public string WriteEditorial(string json)
    {
        var path = Path.Join(Root, "editorial.json");
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>
    /// Writes a throwaway agent kit (<c>skills/topic.md</c>) and returns the root to pass as
    /// <c>agentKitRoot</c>, so the <c>&lt;!-- index:id --&gt;</c> scanner can be driven without
    /// touching the real <c>plugins/</c> tree.
    /// </summary>
    public string WriteAgentKit(string markdown, string fileName = "topic.md")
    {
        var skills = Path.Join(Root, "kit", "skills");
        Directory.CreateDirectory(skills);
        File.WriteAllText(Path.Join(skills, fileName), markdown);
        return Path.Join(Root, "kit");
    }

    /// <summary>Replaces AlphaPage so a test can control how many SampleCards it declares.</summary>
    public void OverwriteAlphaPage(string cs) => File.WriteAllText(Path.Join(GalleryDir, "ControlPages", "AlphaPage.cs"), cs);

    public void OverwriteRegistry(string cs) => File.WriteAllText(Path.Join(GalleryDir, "ControlRegistry.cs"), cs);

    public void Dispose()
    {
        // best-effort temp cleanup — trace (never throw) if the temp dir cannot be removed.
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException ex)
        {
            global::System.Diagnostics.Debug.WriteLine($"MiniGallery: temp cleanup failed for '{Root}': {ex}");
        }
        catch (UnauthorizedAccessException ex)
        {
            global::System.Diagnostics.Debug.WriteLine($"MiniGallery: temp cleanup failed for '{Root}': {ex}");
        }
    }

    const string Registry = @"namespace WinUIGalleryReactor;
public record ControlInfo(string Title, string Description, string Category, string IconGlyph, string Tag, string ImageFile = ""p.png"");
public static class ControlRegistry
{
    public static ControlInfo[] All { get; } = new ControlInfo[]
    {
        new(""Alpha"", ""The alpha control."", ""Basic Input"", ""\uE001"", ""alpha""),
        new(""Beta"", ""The beta control."", ""Basic Input"", ""\uE002"", ""beta""),
    };
}";

    const string RouterBoth = @"namespace WinUIGalleryReactor;
static class PageRouter
{
    public static object Route(string tag) => tag switch
    {
        ""alpha"" => Component<AlphaPage>(),
        ""beta"" => Component<BetaPage>(),
        _ => null,
    };
}";

    const string RouterAlphaOnly = @"namespace WinUIGalleryReactor;
static class PageRouter
{
    public static object Route(string tag) => tag switch
    {
        ""alpha"" => Component<AlphaPage>(),
        _ => null,
    };
}";

    const string AlphaPage = @"namespace WinUIGalleryReactor;
class AlphaPage
{
    object Render() => SampleCard(""Alpha basic"", null, ""Alpha();"");
}";

    const string BetaPage = @"namespace WinUIGalleryReactor;
class BetaPage
{
    object Render() => SampleCard(""Beta basic"", null, ""Beta();"");
}";
}

/// <summary>
/// Edge-case coverage for the generator's editorial merge, keyword normalization, sample
/// override, and skip/throw guarantees — driven by <see cref="MiniGallery"/> so each behavior
/// is forced rather than inferred from the committed 93-control index.
/// </summary>
public sealed class SearchIndexGeneratorEdgeTests
{
    static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only: reflection-based System.Text.Json deserialization of the generated search index into the concrete IndexRoot record (no source-gen context). Standard `dotnet test` is JIT (never trimmed). Behaviour-neutral.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test-only: reflection-based System.Text.Json deserialization into IndexRoot (see the IL2026 note). Run on JIT, not AOT-compiled. Behaviour-neutral.")]
    static IndexRoot Parse(string json) => JsonSerializer.Deserialize<IndexRoot>(json, ReadOptions)!;
    static ControlEntry Find(IndexRoot root, string id) =>
        root.Controls.FirstOrDefault(c => c.Id == id) ?? throw new Xunit.Sdk.XunitException($"'{id}' missing");

    const string BetaKeywords = @"""beta"": { ""keywords"": [""x"", ""y"", ""z""] }";

    [Fact]
    public void SampleOverride_HeaderOnly_KeepsExtractedCode()
    {
        using var g = new MiniGallery(betaRouted: true);
        var ed = g.WriteEditorial(
            @"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""], ""sampleOverride"": { ""header"": ""Custom alpha header"" } }, " + BetaKeywords + " }");

        var alpha = Find(Parse(SearchIndexGenerator.Generate(g.GalleryDir, ed).Json), "alpha");
        Assert.Equal("Custom alpha header", alpha.Samples[0].Header); // overridden
        Assert.Equal("Alpha();", alpha.Samples[0].Code);              // extracted code preserved
    }

    [Fact]
    public void SampleOverride_CodeOnly_KeepsExtractedHeader()
    {
        using var g = new MiniGallery(betaRouted: true);
        var ed = g.WriteEditorial(
            @"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""], ""sampleOverride"": { ""code"": ""CustomCode();"" } }, " + BetaKeywords + " }");

        var alpha = Find(Parse(SearchIndexGenerator.Generate(g.GalleryDir, ed).Json), "alpha");
        Assert.Equal("Alpha basic", alpha.Samples[0].Header);  // extracted header preserved
        Assert.Equal("CustomCode();", alpha.Samples[0].Code);  // overridden
    }

    [Fact]
    public void MissingKeywords_OnIncludedControl_FailsGeneration()
    {
        using var g = new MiniGallery(betaRouted: true);
        // beta is routed + has a sample but no keywords → included yet keyword-less → throw.
        var ed = g.WriteEditorial(@"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""] }, ""beta"": { } }");

        var ex = Assert.Throws<InvalidOperationException>(() => SearchIndexGenerator.Generate(g.GalleryDir, ed));
        Assert.Contains("beta", ex.Message);
        Assert.Contains("keywords", ex.Message);
    }

    [Fact]
    public void Keywords_AreTrimmedLowercasedCollapsedAndDeduped()
    {
        using var g = new MiniGallery(betaRouted: true);
        var ed = g.WriteEditorial(
            @"{ ""alpha"": { ""keywords"": ["" Click "", ""CLICK"", null, """", ""multi   word"", ""click""] }, " + BetaKeywords + " }");

        var alpha = Find(Parse(SearchIndexGenerator.Generate(g.GalleryDir, ed).Json), "alpha");
        // " Click "→"click"; "CLICK"/"click" dedupe; null + "" dropped; "multi   word"→"multi word".
        Assert.Equal(new[] { "click", "multi word" }, alpha.Keywords!.ToArray());
    }

    [Fact]
    public void UnroutedControl_FailsGeneration()
    {
        using var g = new MiniGallery(betaRouted: false); // beta has no router arm
        var ed = g.WriteEditorial(@"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""] }, " + BetaKeywords + " }");

        var ex = Assert.Throws<InvalidOperationException>(() => SearchIndexGenerator.Generate(g.GalleryDir, ed));
        Assert.Contains("beta", ex.Message);
    }

    [Fact]
    public void ExcludedControl_IsSkippedWithoutThrowing()
    {
        using var g = new MiniGallery(betaRouted: false); // beta unrouted...
        var ed = g.WriteEditorial(@"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""] }, ""beta"": { ""exclude"": true } }"); // ...but excluded

        var result = SearchIndexGenerator.Generate(g.GalleryDir, ed);
        Assert.Contains(result.Skipped, s => s.Id == "beta" && s.Reason == "editorial-exclude");
        Assert.DoesNotContain(Parse(result.Json).Controls, c => c.Id == "beta");
        Assert.Single(Parse(result.Json).Controls); // only alpha survives
    }

    [Fact]
    public void MalformedRegistryEntry_FailsGeneration()
    {
        using var g = new MiniGallery(betaRouted: true);
        g.OverwriteRegistry(@"namespace WinUIGalleryReactor;
public record ControlInfo(string Title, string Description, string Category, string IconGlyph, string Tag);
public static class ControlRegistry
{
    public static ControlInfo[] All { get; } = new ControlInfo[]
    {
        new(""Alpha"", ""ok"", ""Basic Input"", ""\uE001"", ""alpha""),
        new(""Bad"", ""too few args""),
    };
}");
        var ed = g.WriteEditorial(@"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""] } }");

        var ex = Assert.Throws<InvalidOperationException>(() => SearchIndexGenerator.Generate(g.GalleryDir, ed));
        Assert.Contains("ControlRegistry entry", ex.Message);
    }

    [Fact]
    public void MisspelledEditorialField_FailsGeneration()
    {
        using var g = new MiniGallery(betaRouted: true);
        // "keyword" (singular) is not a member of EditorialEntry → UnmappedMemberHandling.Disallow.
        var ed = g.WriteEditorial(@"{ ""alpha"": { ""keyword"": [""a"",""b"",""c""] }, " + BetaKeywords + " }");

        var ex = Assert.Throws<InvalidOperationException>(() => SearchIndexGenerator.Generate(g.GalleryDir, ed));
        Assert.Contains("editorial.json is invalid", ex.Message);
    }

    // ══ Issue #1275 — multi-sample emission, curated keywords, docs, prose markers ══

    const string AlphaThreeCards = @"namespace WinUIGalleryReactor;
class AlphaPage
{
    object Render() => VStack(
        SampleCard(""Alpha basic"", null, ""Alpha();""),
        SampleCard(""Alpha elided"", null, ""Alpha(a, ...);""),
        SampleCard(""Alpha advanced"", null, ""Alpha().Tuned();""),
        SampleCard(""Alpha basic"", null, ""AlphaDuplicateHeader();""));
}";

    [Fact]
    public void EveryCleanCard_IsEmitted_PlaceholdersAndDuplicateHeadersAreNot()
    {
        using var g = new MiniGallery(betaRouted: true);
        g.OverwriteAlphaPage(AlphaThreeCards);
        var ed = g.WriteEditorial(@"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""] }, " + BetaKeywords + " }");

        var alpha = Find(Parse(SearchIndexGenerator.Generate(g.GalleryDir, ed).Json), "alpha");

        // Both clean cards travel; the elided one and the header-duplicate do not.
        Assert.Equal(new[] { "Alpha basic", "Alpha advanced" }, alpha.Samples.Select(s => s.Header).ToArray());
        Assert.DoesNotContain(alpha.Samples, s => s.Code.Contains("...", StringComparison.Ordinal));
    }

    [Fact]
    public void SampleOverride_DefinesTheFirstSample_AndLaterCardsStillFollow()
    {
        using var g = new MiniGallery(betaRouted: true);
        g.OverwriteAlphaPage(AlphaThreeCards);
        var ed = g.WriteEditorial(
            @"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""], ""sampleOverride"": { ""header"": ""Curated first"" } }, " + BetaKeywords + " }");

        var alpha = Find(Parse(SearchIndexGenerator.Generate(g.GalleryDir, ed).Json), "alpha");

        Assert.Equal("Curated first", alpha.Samples[0].Header);
        Assert.Equal("Alpha();", alpha.Samples[0].Code); // override was header-only
        Assert.Equal("Alpha advanced", alpha.Samples[1].Header);
    }

    [Fact]
    public void SampleOverride_HeaderCollidingWithALaterCard_DropsTheDuplicate()
    {
        using var g = new MiniGallery(betaRouted: true);
        g.OverwriteAlphaPage(AlphaThreeCards);
        // Differs from the later card's "Alpha advanced" only by case — the page-level dedupe is
        // case-insensitive, so this guard must be too or the override reintroduces the duplicate.
        var ed = g.WriteEditorial(
            @"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""], ""sampleOverride"": { ""header"": ""alpha ADVANCED"" } }, " + BetaKeywords + " }");

        var alpha = Find(Parse(SearchIndexGenerator.Generate(g.GalleryDir, ed).Json), "alpha");

        Assert.Equal("alpha ADVANCED", alpha.Samples[0].Header);
        Assert.Single(alpha.Samples);
    }

    [Fact]
    public void CuratedKeywords_AreCanonicalizedLikeKeywords()
    {
        using var g = new MiniGallery(betaRouted: true);
        var ed = g.WriteEditorial(
            @"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""], ""curatedKeywords"": ["" State   Management "", ""STATE MANAGEMENT"", null] }, " + BetaKeywords + " }");

        var alpha = Find(Parse(SearchIndexGenerator.Generate(g.GalleryDir, ed).Json), "alpha");
        Assert.Equal(new[] { "state management" }, alpha.CuratedKeywords!.ToArray());
        Assert.Null(Find(Parse(SearchIndexGenerator.Generate(g.GalleryDir, ed).Json), "beta").CuratedKeywords);
    }

    [Fact]
    public void DocLink_MissingTitleOrUri_FailsGeneration()
    {
        using var g = new MiniGallery(betaRouted: true);
        var ed = g.WriteEditorial(
            @"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""], ""docs"": [ { ""title"": ""No uri"" } ] }, " + BetaKeywords + " }");

        var ex = Assert.Throws<InvalidOperationException>(() => SearchIndexGenerator.Generate(g.GalleryDir, ed));
        Assert.Contains("alpha", ex.Message);
        Assert.Contains("docs", ex.Message);
    }

    /// <summary>
    /// A hand-edited editorial file can contain `"docs": [null]`. That must surface as the
    /// documented generation error, not as an unhandled NullReferenceException — the CLI only
    /// catches the former, so an NRE would crash the tool instead of reporting a bad sidecar.
    /// </summary>
    [Fact]
    public void DocLink_NullEntry_FailsGenerationWithoutDereferencing()
    {
        using var g = new MiniGallery(betaRouted: true);
        var ed = g.WriteEditorial(
            @"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""], ""docs"": [ null ] }, " + BetaKeywords + " }");

        var ex = Assert.Throws<InvalidOperationException>(() => SearchIndexGenerator.Generate(g.GalleryDir, ed));
        Assert.Contains("alpha", ex.Message);
        Assert.Contains("docs", ex.Message);

        // The CLI's catch list covers InvalidOperationException but not NullReferenceException,
        // so the exception TYPE is the contract here, not just the message.
        using var log = new StringWriter();
        Assert.Equal(2, SearchIndexCli.Run(new[] { g.GalleryDir, ed, Path.Join(g.Root, "out.json") }, log));
        Assert.Contains("[search-index] ERROR:", log.ToString());
    }

    [Fact]
    public void MarkedBlock_BecomesDetails_AndOtherControlsGetNone()
    {
        using var g = new MiniGallery(betaRouted: true);
        var ed = g.WriteEditorial(@"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""] }, " + BetaKeywords + " }");
        var kit = g.WriteAgentKit("# Topic\n\n<!-- index:alpha -->\nAlpha is the first letter.\n<!-- /index:alpha -->\n");

        var root = Parse(SearchIndexGenerator.Generate(g.GalleryDir, ed, kit).Json);
        Assert.Equal("Alpha is the first letter.", Find(root, "alpha").Details);
        Assert.Null(Find(root, "beta").Details);

        // Omitting the agent-kit root is the documented "no markers" mode, not an error.
        Assert.Null(Find(Parse(SearchIndexGenerator.Generate(g.GalleryDir, ed).Json), "alpha").Details);
    }

    [Theory]
    [InlineData("<!-- index:alpha -->\nunclosed\n", "never closed")]
    [InlineData("<!-- index:alpha -->\n<!-- index:beta -->\nnested\n<!-- /index:beta -->\n<!-- /index:alpha -->", "cannot nest")]
    [InlineData("<!-- /index:alpha -->\n", "never opened")]
    [InlineData("<!-- index:alpha -->\nmismatched\n<!-- /index:beta -->", "closes the wrong block")]
    [InlineData("<!-- index:alpha -->\n\n   \n<!-- /index:alpha -->", "wraps no prose")]
    public void MalformedMarker_FailsGeneration(string markdown, string expected)
    {
        using var g = new MiniGallery(betaRouted: true);
        var ed = g.WriteEditorial(@"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""] }, " + BetaKeywords + " }");
        var kit = g.WriteAgentKit(markdown);

        var ex = Assert.Throws<InvalidOperationException>(() => SearchIndexGenerator.Generate(g.GalleryDir, ed, kit));
        Assert.Contains(expected, ex.Message);
        // Diagnostics must name the file and line, not just the file name — the scanner walks
        // two whole trees and "SKILL.md" is not unique within them.
        Assert.Contains("skills/topic.md(", ex.Message);
    }

    [Fact]
    public void MarkerNamingNoControl_FailsGeneration()
    {
        using var g = new MiniGallery(betaRouted: true);
        var ed = g.WriteEditorial(@"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""] }, " + BetaKeywords + " }");
        var kit = g.WriteAgentKit("<!-- index:gamma -->\nProse for a control that does not exist.\n<!-- /index:gamma -->");

        var ex = Assert.Throws<InvalidOperationException>(() => SearchIndexGenerator.Generate(g.GalleryDir, ed, kit));
        Assert.Contains("gamma", ex.Message);
        Assert.Contains("match no control id", ex.Message);
    }

    [Fact]
    public void RelativeLinksInMarkedProse_AreRewrittenAbsolute()
    {
        using var g = new MiniGallery(betaRouted: true);
        var ed = g.WriteEditorial(@"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""] }, " + BetaKeywords + " }");
        var kit = g.WriteAgentKit("<!-- index:alpha -->\nSee [the sibling](sibling.md) and [an anchor](sibling.md#part).\n<!-- /index:alpha -->");
        File.WriteAllText(Path.Join(kit, "skills", "sibling.md"), "# Sibling\n");

        var details = Find(Parse(SearchIndexGenerator.Generate(g.GalleryDir, ed, kit).Json), "alpha").Details!;

        Assert.Contains("(https://github.com/microsoft/microsoft-ui-reactor/blob/main/skills/sibling.md)", details);
        Assert.Contains("(https://github.com/microsoft/microsoft-ui-reactor/blob/main/skills/sibling.md#part)", details);

        // Guards the capture-group semantics of MarkdownLinkRegex: if the rewriter ever read the
        // `]` group instead of the target, it would resolve a file literally named "]" and throw
        // "points at nothing" rather than producing these URLs. The file name in the assertions
        // above is what makes that distinguishable.
        Assert.DoesNotContain("blob/main/skills/]", details);
    }

    [Fact]
    public void RelativeLinkToNothing_FailsGeneration()
    {
        using var g = new MiniGallery(betaRouted: true);
        var ed = g.WriteEditorial(@"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""] }, " + BetaKeywords + " }");
        var kit = g.WriteAgentKit("<!-- index:alpha -->\nSee [the missing one](nope.md).\n<!-- /index:alpha -->");

        var ex = Assert.Throws<InvalidOperationException>(() => SearchIndexGenerator.Generate(g.GalleryDir, ed, kit));
        Assert.Contains("points at nothing", ex.Message);
    }

    [Fact]
    public void RootedLinkInMarkedProse_FailsGeneration()
    {
        using var g = new MiniGallery(betaRouted: true);
        var ed = g.WriteEditorial(@"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""] }, " + BetaKeywords + " }");
        // Site-absolute markdown links are the common form of this mistake; without an explicit
        // guard Path.Combine drops the file's directory and resolves against the drive root.
        var kit = g.WriteAgentKit("<!-- index:alpha -->\nSee [site absolute](/docs/guide/hooks.md).\n<!-- /index:alpha -->");

        var ex = Assert.Throws<InvalidOperationException>(() => SearchIndexGenerator.Generate(g.GalleryDir, ed, kit));
        Assert.Contains("is rooted", ex.Message);
    }

    [Fact]
    public void AbsoluteAndAnchorLinks_AreLeftAlone()
    {
        using var g = new MiniGallery(betaRouted: true);
        var ed = g.WriteEditorial(@"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""] }, " + BetaKeywords + " }");
        var kit = g.WriteAgentKit("<!-- index:alpha -->\n[ext](https://example.com/x) and [local](#heading).\n<!-- /index:alpha -->");

        var details = Find(Parse(SearchIndexGenerator.Generate(g.GalleryDir, ed, kit).Json), "alpha").Details!;
        Assert.Contains("(https://example.com/x)", details);
        Assert.Contains("(#heading)", details);
    }
}

/// <summary>
/// Exercises the <see cref="SearchIndexCli"/> exit-code / arg-validation contract with a
/// captured <see cref="StringWriter"/> (no real console), against a <see cref="MiniGallery"/>.
/// </summary>
public sealed class SearchIndexCliTests
{
    const string ValidEditorial = @"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""] }, ""beta"": { ""keywords"": [""x"",""y"",""z""] } }";

    [Fact]
    public void Run_Write_Then_Check_ReportsStaleThenUpToDate()
    {
        using var g = new MiniGallery(betaRouted: true);
        var ed = g.WriteEditorial(ValidEditorial);
        var outPath = Path.Join(g.Root, "out.json");
        using var log = new StringWriter();

        // --check before the file exists → stale (1).
        Assert.Equal(1, SearchIndexCli.Run(new[] { "--check", g.GalleryDir, ed, outPath }, log));
        // write → 0, file created.
        Assert.Equal(0, SearchIndexCli.Run(new[] { g.GalleryDir, ed, outPath }, log));
        Assert.True(File.Exists(outPath));
        // --check now → up to date (0).
        Assert.Equal(0, SearchIndexCli.Run(new[] { "--check", g.GalleryDir, ed, outPath }, log));
        // corrupt the committed file → stale (1) again.
        File.WriteAllText(outPath, "{}\n");
        Assert.Equal(1, SearchIndexCli.Run(new[] { "--check", g.GalleryDir, ed, outPath }, log));
    }

    [Fact]
    public void Run_UnknownOption_ReturnsUsageError()
    {
        using var log = new StringWriter();
        Assert.Equal(2, SearchIndexCli.Run(new[] { "--chek" }, log));
        Assert.Contains("unknown option", log.ToString());
    }

    /// <summary>
    /// The agent-kit root is inferred from the GALLERY, not from the argument count, so passing
    /// the real paths explicitly must produce byte-identical output to passing none. Before this
    /// was keyed on `positional.Count == 0`, which silently wrote a details-free index whenever a
    /// contributor spelled the paths out.
    /// </summary>
    [Fact]
    public void Run_ExplicitRealPaths_MatchTheDefaultInvocationByte4Byte()
    {
        var repoRoot = RepoRoot();
        var galleryDir = Path.Join(repoRoot, "samples", "ReactorGallery");
        var editorial = Path.Join(repoRoot, "tools", "Reactor.SearchIndex", "editorial.json");
        var committed = Path.Join(galleryDir, "reactor-search-index.json");
        var outPath = Path.Join(Path.GetTempPath(), $"reactor-si-cli-{Guid.NewGuid():N}.json");
        using var log = new StringWriter();

        try
        {
            Assert.Equal(0, SearchIndexCli.Run(new[] { galleryDir, editorial, outPath }, log));
            Assert.Equal(File.ReadAllBytes(committed), File.ReadAllBytes(outPath));

            // Positive control: the comparison above is only meaningful if details are present.
            Assert.Contains("\"details\":", File.ReadAllText(outPath), StringComparison.Ordinal);

            // ...and --no-agent-kit is the explicit opt-out, which must differ.
            Assert.Equal(0, SearchIndexCli.Run(new[] { "--no-agent-kit", galleryDir, editorial, outPath }, log));
            Assert.DoesNotContain("\"details\":", File.ReadAllText(outPath), StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    [Fact]
    public void Run_NoAgentKitCombinedWithAgentKit_ReturnsUsageError()
    {
        using var log = new StringWriter();
        Assert.Equal(2, SearchIndexCli.Run(new[] { "--no-agent-kit", "--agent-kit=." }, log));
        Assert.Contains("mutually exclusive", log.ToString());
    }

    /// <summary>
    /// A mistyped explicit override must fail loudly. An INFERRED root is allowed to be absent —
    /// that is how a synthetic gallery opts out — but if a typo were treated the same way the
    /// tool would happily write a details-free index and report success.
    /// </summary>
    [Fact]
    public void Run_MistypedAgentKitDirectory_ReturnsUsageError()
    {
        using var g = new MiniGallery(betaRouted: true);
        var ed = g.WriteEditorial(ValidEditorial);
        var outPath = Path.Join(g.Root, "out.json");
        var missing = Path.Join(g.Root, "no-such-kit");
        using var log = new StringWriter();

        Assert.Equal(2, SearchIndexCli.Run(new[] { $"--agent-kit={missing}", g.GalleryDir, ed, outPath }, log));
        Assert.Contains("does not exist", log.ToString());
        Assert.False(File.Exists(outPath), "a rejected run must not write an index");

        // A path that exists but is a FILE is the other half of the same mistake, and gets its
        // own message rather than the misleading "does not exist".
        using var fileLog = new StringWriter();
        Assert.Equal(2, SearchIndexCli.Run(new[] { $"--agent-kit={ed}", g.GalleryDir, ed, outPath }, fileLog));
        Assert.Contains("must be a directory, not a file", fileLog.ToString());
        Assert.False(File.Exists(outPath), "a rejected run must not write an index");

        // Positive control: the same invocation against a real directory succeeds, so the
        // assertion above is about the missing path and not about the argument shape.
        var kit = g.WriteAgentKit("<!-- index:alpha -->\nAlpha prose.\n<!-- /index:alpha -->");
        Assert.Equal(0, SearchIndexCli.Run(new[] { $"--agent-kit={kit}", g.GalleryDir, ed, outPath }, log));
        Assert.Contains("\"details\":", File.ReadAllText(outPath), StringComparison.Ordinal);
    }

    static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Join(dir, "Reactor.slnx"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Could not locate repo root from " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Run_TooManyPositionalArgs_ReturnsUsageError()
    {
        using var log = new StringWriter();
        Assert.Equal(2, SearchIndexCli.Run(new[] { "a", "b", "c", "d" }, log));
        Assert.Contains("too many arguments", log.ToString());
    }

    [Fact]
    public void Run_GenerationError_ReturnsTwo()
    {
        using var g = new MiniGallery(betaRouted: true);
        // orphan editorial key "ghost" → InvalidOperationException → caught → exit 2.
        var ed = g.WriteEditorial(
            @"{ ""alpha"": { ""keywords"": [""a"",""b"",""c""] }, ""beta"": { ""keywords"": [""x"",""y"",""z""] }, ""ghost"": { ""keywords"": [""p"",""q"",""r""] } }");
        using var log = new StringWriter();

        Assert.Equal(2, SearchIndexCli.Run(new[] { g.GalleryDir, ed, Path.Join(g.Root, "out.json") }, log));
        Assert.Contains("ERROR", log.ToString());
    }

    [Fact]
    public void Run_MalformedPath_ReturnsErrorExitCode_NotCrash()
    {
        using var log = new StringWriter();
        // "a:b:c" makes Path.GetFullPath throw NotSupportedException on Windows; elsewhere it
        // resolves and the subsequent read fails. Either way the CLI must return 2, not crash.
        Assert.Equal(2, SearchIndexCli.Run(new[] { "a:b:c" }, log));
        Assert.Contains("ERROR", log.ToString());
    }
}
