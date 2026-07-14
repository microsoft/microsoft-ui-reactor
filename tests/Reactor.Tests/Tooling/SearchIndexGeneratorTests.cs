using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Reactor.SearchIndex;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Tooling;

/// <summary>
/// Staleness gate for samples/ReactorGallery/reactor-search-index.json — the file the
/// external winui-search CLI fetches. Drives SearchIndexGenerator.Generate(...) in-process
/// against the gallery source + editorial sidecar and asserts byte-equality with the
/// committed file, so adding/removing/renaming a control or changing its first sample
/// snippet fails CI until the index is regenerated. The UPDATE_SEARCH_INDEX=1 arm rewrites
/// the committed file (same ergonomics as ApiIndexGeneratorTests' UPDATE_API_INDEX).
///
/// The structural/differential facts are written so each one fails if the code it targets
/// is deleted or no-op'd (extraction, editorial merge, sort, or the header contract).
/// </summary>
public sealed class SearchIndexGeneratorTests
{
    static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Join(dir, "Reactor.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Could not locate repo root (Reactor.slnx) from " + AppContext.BaseDirectory);
    }

    static string GalleryDir() => Path.Join(RepoRoot(), "samples", "ReactorGallery");
    static string EditorialPath() => Path.Join(RepoRoot(), "tools", "Reactor.SearchIndex", "editorial.json");
    static string CommittedPath() => Path.Join(GalleryDir(), "reactor-search-index.json");

    static SearchIndexResult Generate() => SearchIndexGenerator.Generate(GalleryDir(), EditorialPath());

    static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    static IndexRoot Parse(string json) => JsonSerializer.Deserialize<IndexRoot>(json, ReadOptions)!;
    static ControlEntry Find(IndexRoot root, string id) =>
        root.Controls.FirstOrDefault(c => c.Id == id) ?? throw new Xunit.Sdk.XunitException($"control '{id}' missing from index");

    // ── The gate ────────────────────────────────────────────────────────────

    [Fact]
    public void Index_IsUpToDate()
    {
        var generated = Generate().Json;
        var generatedBytes = Encoding.UTF8.GetBytes(generated); // UTF-8, no BOM (GetBytes emits no preamble)

        if (Environment.GetEnvironmentVariable("UPDATE_SEARCH_INDEX") == "1")
        {
            File.WriteAllBytes(CommittedPath(), generatedBytes);
            return;
        }

        // Byte comparison (not File.ReadAllText, which would silently strip a stray BOM) — the
        // committed file is fetched raw by the winui-search CLI, so a BOM is a real diff.
        var committedBytes = File.ReadAllBytes(CommittedPath());
        if (!generatedBytes.AsSpan().SequenceEqual(committedBytes))
        {
            throw new Xunit.Sdk.XunitException(
                "samples/ReactorGallery/reactor-search-index.json is stale (content or BOM/encoding). Regenerate by running:\n" +
                "  dotnet run --project tools/Reactor.SearchIndex\n" +
                "  (or: $env:UPDATE_SEARCH_INDEX=1; dotnet test tests/Reactor.Tests " +
                "--filter \"FullyQualifiedName~Tooling.SearchIndexGeneratorTests.Index_IsUpToDate\")\n" +
                "First diff: " + FirstDiffPreview(File.ReadAllText(CommittedPath()), generated));
        }
    }

    // ── Determinism / formatting invariants ──────────────────────────────────

    [Fact]
    public void Generation_IsDeterministic()
    {
        Assert.Equal(Generate().Json, Generate().Json);
    }

    [Fact]
    public void Output_IsLfOnly_WithSingleTrailingNewline()
    {
        var json = Generate().Json;
        Assert.DoesNotContain('\r', json);
        Assert.EndsWith("\n", json);
        Assert.False(json.EndsWith("\n\n"), "should end with exactly one trailing newline");
    }

    // ── Header contract ──────────────────────────────────────────────────────

    [Fact]
    public void Header_MatchesAgreedContract()
    {
        var root = Parse(Generate().Json);
        Assert.Equal(1, root.SchemaVersion);
        Assert.Equal("reactor", root.Source);
        // Must be the literal repo slug — never a volatile sha/timestamp (invariant #1).
        Assert.Equal("microsoft/microsoft-ui-reactor", root.GeneratedFrom);
    }

    // ── Required fields + real-code samples (fails if extraction returns default) ─

    [Fact]
    public void EveryControl_HasRequiredFields_AndRealCodeSample()
    {
        var root = Parse(Generate().Json);
        Assert.NotEmpty(root.Controls);

        foreach (var c in root.Controls)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Id), "id");
            Assert.False(string.IsNullOrWhiteSpace(c.Name), $"name for {c.Id}");
            Assert.False(string.IsNullOrWhiteSpace(c.Category), $"category for {c.Id}");
            Assert.False(string.IsNullOrWhiteSpace(c.Description), $"description for {c.Id}");
            Assert.Equal(c.Id, c.GalleryRoute);

            Assert.NotEmpty(c.Samples);
            var s = c.Samples[0];
            Assert.Equal("csharp", s.Language);
            Assert.False(string.IsNullOrWhiteSpace(s.Header), $"header for {c.Id}");
            Assert.False(string.IsNullOrWhiteSpace(s.Code), $"code for {c.Id}");
            // At least one line of real code (not blank / not a pure // comment).
            Assert.Contains(s.Code.Split('\n'), line =>
            {
                var t = line.Trim();
                return t.Length > 0 && !t.StartsWith("//", StringComparison.Ordinal);
            });
            // No unresolved template tokens (invariant #2, REAL CODE ONLY).
            Assert.DoesNotContain("{{", s.Code);
        }
    }

    // ── Sort (fails if the sort is removed: registry source order is by category) ─

    [Fact]
    public void Controls_AreStrictlySortedById()
    {
        var ids = Parse(Generate().Json).Controls.Select(c => c.Id).ToList();
        Assert.NotEmpty(ids);
        for (var i = 1; i < ids.Count; i++)
        {
            Assert.True(
                string.CompareOrdinal(ids[i - 1], ids[i]) < 0,
                $"controls must be strictly ascending by id (ordinal); '{ids[i - 1]}' !< '{ids[i]}'");
        }
    }

    // ── Editorial merge (fails if the merge is dropped) ───────────────────────

    [Fact]
    public void EditorialMerge_AppliesKeywords_Related_Usings_AndDefaults()
    {
        var root = Parse(Generate().Json);

        var button = Find(root, "button");
        Assert.NotNull(button.Keywords);
        Assert.Contains("click", button.Keywords!);
        Assert.NotNull(button.RelatedControls);
        Assert.Contains("RepeatButton", button.RelatedControls!);
        Assert.Equal("Microsoft.UI.Reactor", button.ApiNamespace);
        Assert.Equal("Microsoft.UI.Reactor", button.NugetPackage);

        // Non-obvious import surfaced only where a sample needs it...
        Assert.Contains("Microsoft.UI.Reactor.Data", Find(root, "data-grid").Usings!);
        Assert.Contains("Microsoft.UI.Reactor.Docking", Find(root, "docking").Usings!);
        // ...and omitted (null, not empty) where the sample needs no extra import.
        Assert.Null(Find(root, "acrylic").Usings);
    }

    // ── Sample extraction handles both named and positional sourceCode args ──

    [Fact]
    public void SampleExtraction_HandlesNamedAndPositionalSourceCode()
    {
        var root = Parse(Generate().Json);

        // info-bar passes sourceCode as the 3rd positional argument.
        Assert.Contains("InfoBar(", Find(root, "info-bar").Samples[0].Code);
        // data-grid passes it as the named `sourceCode:` argument.
        Assert.Contains("DataGrid(", Find(root, "data-grid").Samples[0].Code);
        // button's representative snippet is the real .Click chain, verbatim from source.
        Assert.Contains(".Click(", Find(root, "button").Samples[0].Code);
    }

    // ── Independent oracle: a curated set of controls must always be present ──

    [Fact]
    public void ExpectedControls_ArePresent()
    {
        var ids = Parse(Generate().Json).Controls.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var expected in new[]
        {
            "button", "combo-box", "list-view", "data-grid", "property-grid", "docking",
            "map-control", "split-view", "navigation-view", "tree-view", "grid", "geometry", "type-ramp",
        })
        {
            Assert.Contains(expected, ids);
        }
        Assert.True(ids.Count >= 90, $"expected >=90 controls, got {ids.Count}");
    }

    // ── REAL CODE ONLY: no placeholder/abbreviation tokens survive (invariant #2) ─

    [Fact]
    public void Samples_ContainNoPlaceholderTokens()
    {
        var placeholder = new Regex(@",\s*\.\.\.|\.\.\.\s*\)|\(\s*\.\.\.|^\s*\.\.\.\s*$|\.\.\./|<your", RegexOptions.Multiline);
        foreach (var c in Parse(Generate().Json).Controls)
        {
            var code = c.Samples[0].Code;
            Assert.False(placeholder.IsMatch(code), $"{c.Id} sample has a placeholder token:\n{code}");
            Assert.DoesNotContain("{{", code);
        }
    }

    // ── keywords are winui-search's weighted BM25 field: every control must carry a
    //    populated, high-signal set that does not restate the name/category. ──

    [Fact]
    public void EveryControl_HasHighSignalKeywords()
    {
        foreach (var c in Parse(Generate().Json).Controls)
        {
            Assert.NotNull(c.Keywords);
            Assert.InRange(c.Keywords!.Count, 3, 10);
            Assert.Equal(c.Keywords.Count, c.Keywords.Distinct(StringComparer.Ordinal).Count()); // no dups
            foreach (var k in c.Keywords)
            {
                Assert.False(string.IsNullOrWhiteSpace(k), $"{c.Id} has a blank keyword");
                Assert.Equal(k.ToLowerInvariant(), k);           // lowercase
                Assert.Equal(k.Trim(), k);                       // trimmed
                Assert.DoesNotContain("  ", k);                  // internal whitespace collapsed
                Assert.False(k.EndsWith('.'), $"{c.Id}: keyword '{k}' looks like a sentence");
                Assert.InRange(k.Split(' ').Length, 1, 6);       // single token or short phrase
                Assert.NotEqual(c.Name, k, StringComparer.OrdinalIgnoreCase);     // not the control name
                Assert.NotEqual(c.Id, k, StringComparer.OrdinalIgnoreCase);
                Assert.NotEqual(c.Category, k, StringComparer.OrdinalIgnoreCase); // not the category
            }
        }
    }

    // ── each sample header is the primary per-scenario BM25 field: descriptive,
    //    non-empty, and never a bare repeat of the control name/id. ──

    [Fact]
    public void EveryHeader_IsDescriptive_NotABareControlNameRepeat()
    {
        foreach (var c in Parse(Generate().Json).Controls)
        {
            var h = c.Samples[0].Header;
            Assert.False(string.IsNullOrWhiteSpace(h));
            Assert.NotEqual(c.Name, h, StringComparer.OrdinalIgnoreCase);
            Assert.NotEqual(c.Id, h, StringComparer.OrdinalIgnoreCase);
        }
    }

    // ── headers within one control's samples[] must be distinct (near-dupes collapse
    //    into redundant scenarios). Moot at 1 sample/control today; a cheap future guard. ──

    [Fact]
    public void Samples_HaveDistinctHeadersWithinEachControl()
    {
        foreach (var c in Parse(Generate().Json).Controls)
        {
            // v1 contract: exactly one sample per control — non-vacuous, fails if the
            // generator ever emits multiple samples without this guard being revisited...
            Assert.Single(c.Samples);
            // ...and whenever it does, their headers must stay distinct (no redundant scenarios).
            var headers = c.Samples.Select(s => s.Header).ToList();
            Assert.Equal(headers.Count, headers.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    // ── sampleOverride escape hatch replaces a control whose only card is a
    //    placeholder (map-control's sole SampleCard used <your-key>). ──

    [Fact]
    public void SampleOverride_ReplacesRejectedPlaceholderCard()
    {
        var mc = Find(Parse(Generate().Json), "map-control");
        Assert.Equal("Interactive map", mc.Samples[0].Header);
        Assert.Contains("MapControl(zoomLevel", mc.Samples[0].Code);
        Assert.DoesNotContain("<your", mc.Samples[0].Code);
    }

    // ── a mistyped editorial key must fail generation, not silently drop curation ─

    [Fact]
    public void OrphanEditorialKey_FailsGeneration()
    {
        var tmp = Path.Join(Path.GetTempPath(), $"reactor-editorial-orphan-{Guid.NewGuid():N}.json");
        File.WriteAllText(tmp, "{ \"button\": { \"keywords\": [\"click\",\"submit\",\"accent\"] }, \"buton\": { \"keywords\": [\"x\",\"y\",\"z\"] } }");
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => SearchIndexGenerator.Generate(GalleryDir(), tmp));
            Assert.Contains("buton", ex.Message);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // ── coverage: no control is silently dropped (Generate throws on non-exclude skips) ─

    [Fact]
    public void Generate_DropsNoControls()
    {
        // With no editorial excludes today, the skip list must be empty. Fails if a control
        // loses its route/sample (Generate would throw) or an exclude is added — either way
        // forcing an intentional review of index coverage.
        Assert.Empty(Generate().Skipped);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    static string FirstDiffPreview(string expected, string actual)
    {
        var min = Math.Min(expected.Length, actual.Length);
        var i = 0;
        while (i < min && expected[i] == actual[i]) i++;
        if (i == min && expected.Length == actual.Length) return "(no diff)";

        var start = Math.Max(0, i - 40);
        string Slice(string s) =>
            s.Substring(start, Math.Min(200, s.Length - start)).Replace("\r", "\\r").Replace("\n", "\\n");
        return $"at offset {i}\n  expected: …{Slice(expected)}…\n  actual:   …{Slice(actual)}…";
    }
}
