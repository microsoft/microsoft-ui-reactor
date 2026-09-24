using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.UI.Reactor.SearchIndex;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Tooling;

/// <summary>
/// Issue #1275 — the search index and the shipped agent-kit skills must teach the same thing.
///
/// <para>Prose parity is structural rather than asserted: an entry's <c>details</c> is lifted
/// verbatim out of the owning SKILL.md by its <c>&lt;!-- index:id --&gt;</c> marker, so comparing
/// the two would be a tautology and would pass no matter how wrong either side was.</para>
///
/// <para><b>What this file actually measures</b> is the half that CAN drift: the marked block's
/// C# fences name APIs, and the gallery page is what proves those APIs exist and compile. The
/// gate is therefore skill-fence → emitted sample. Delete a gallery card and it reddens; add an
/// API to a skill without demonstrating it and it reddens. <see cref="TheGate_CanFail"/> is the
/// instrument check — a broken extractor would otherwise report "no gaps" forever.</para>
/// </summary>
public sealed class SearchIndexSkillParityTests
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

    static SearchIndexResult Generate() => SearchIndexGenerator.Generate(GalleryDir(), EditorialPath(), RepoRoot());

    static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only reflection-based deserialization of the generated index; `dotnet test` is JIT, never trimmed.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test-only reflection-based deserialization (see the IL2026 note).")]
    static IndexRoot Parse(string json) => JsonSerializer.Deserialize<IndexRoot>(json, ReadOptions)!;

    // ── the extractor ────────────────────────────────────────────────────────

    // ```csharp fences inside a lifted details block. Singleline so `.` spans the body: without
    // it this matches nothing and the whole gate passes vacuously.
    static readonly Regex CSharpFence = new(@"```csharp\r?\n(.*?)```", RegexOptions.Singleline);

    // Reactor's two agent-facing API shapes: hooks (`UseState`) and modifiers (`.OnKeyDown`).
    // Deliberately narrow — a broad identifier scan would need a name universe that goes stale.
    static readonly Regex ReactorApi = new(@"\bUse[A-Z]\w*|\.On[A-Z]\w*");

    internal static IReadOnlyList<string> ApisNamedInFences(string details) =>
        CSharpFence.Matches(details)
            .SelectMany(m => ReactorApi.Matches(m.Groups[1].Value).Select(a => a.Value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(a => a, StringComparer.Ordinal)
            .ToList();

    // ── the gate ─────────────────────────────────────────────────────────────

    [Fact]
    public void EveryApiTheSkillDemonstrates_AppearsInAnEmittedSample()
    {
        var entries = Parse(Generate().Json).Controls.Where(c => c.Details is not null).ToList();
        Assert.NotEmpty(entries);

        var gaps = new List<string>();
        var apisChecked = 0;

        foreach (var entry in entries)
        {
            var code = string.Join("\n", entry.Samples.Select(s => s.Code));
            foreach (var api in ApisNamedInFences(entry.Details!))
            {
                apisChecked++;
                if (!code.Contains(api, StringComparison.Ordinal))
                    gaps.Add($"{entry.Id}: the skill demonstrates `{api}` but no gallery card does");
            }
        }

        // Without this the loop above could check nothing — a changed fence language tag or a
        // dropped `details` field would read as a clean pass.
        Assert.True(apisChecked > 0, "no API was extracted from any skill fence — the gate measured nothing");

        Assert.True(gaps.Count == 0,
            "The index's prose is lifted from the skills, so a gallery page that does not demonstrate what\n" +
            "its skill teaches ships an entry whose samples contradict its own details. Add a SampleCard to\n" +
            "the Fundamentals page, or drop the API from the marked block:\n  " +
            string.Join("\n  ", gaps));
    }

    /// <summary>
    /// Instrument check. The gate's whole value rests on the extractor finding fences and APIs;
    /// a regex that silently matches nothing reports a perfect score. Two known-good inputs and
    /// one known-bad prove it discriminates.
    /// </summary>
    [Fact]
    public void TheGate_CanFail()
    {
        const string details = """
            Prose about hooks.

            ```csharp
            var (count, setCount) = UseState(0);
            TextBox(text, setText).OnKeyDown((s, e) => { });
            ```

            More prose naming UseEffect outside any fence, which must NOT be extracted.
            """;

        var apis = ApisNamedInFences(details);

        Assert.Equal(new[] { ".OnKeyDown", "UseState" }, apis);
        Assert.DoesNotContain("UseEffect", apis); // prose-only mentions are not demands

        // A sample set missing one of them is what a real gap looks like.
        const string partialSamples = "var (count, setCount) = UseState(0);";
        Assert.Contains(apis, a => !partialSamples.Contains(a, StringComparison.Ordinal));
    }

    /// <summary>
    /// The nine mechanics topics all carry lifted prose. If a marker were deleted the entry
    /// would quietly lose its <c>details</c> and the gate above would simply skip it, so the
    /// roster is pinned explicitly.
    /// </summary>
    [Fact]
    public void EveryFundamentalsTopic_CarriesLiftedProse()
    {
        var root = Parse(Generate().Json);
        var fundamentals = root.Controls.Where(c => c.Category == "Fundamentals").ToList();

        Assert.Equal(9, fundamentals.Count);
        foreach (var c in fundamentals)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Details), $"{c.Id} lost its `<!-- index:{c.Id} -->` marker");
            Assert.NotNull(c.CuratedKeywords);
            Assert.NotNull(c.Docs);
        }
    }

    /// <summary>
    /// A concept can span sections — pointer events and gestures sit either side of the keyboard
    /// section — so repeated markers concatenate. If that were lost, only one half would ship.
    /// </summary>
    [Fact]
    public void RepeatedMarkers_ConcatenateInDocumentOrder()
    {
        var pointer = Parse(Generate().Json).Controls.Single(c => c.Id == "pointer-input");

        var pointerAt = pointer.Details!.IndexOf("OnPointerEntered", StringComparison.Ordinal);
        var gestureAt = pointer.Details!.IndexOf("OnPan", StringComparison.Ordinal);

        Assert.True(pointerAt >= 0, "the §1 pointer block is missing from the lifted prose");
        Assert.True(gestureAt >= 0, "the §4 gesture block is missing from the lifted prose");
        Assert.True(pointerAt < gestureAt, "blocks must concatenate in document order");
    }
}
