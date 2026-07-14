using System.Collections.Generic;
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
}
