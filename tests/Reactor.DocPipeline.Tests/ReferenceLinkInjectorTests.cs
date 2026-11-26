using Microsoft.UI.Reactor.Cli.Docs;
using Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Spec 041 §10.4.1 — <c>&lt;!-- ref:X --&gt;</c> marker expansion in guide
/// templates, the reverse marker index, and the orphaned-guide-page lint.
/// </summary>
public class ReferenceLinkInjectorTests
{
    private static ReferenceMap StandardMap() => ReferenceMap.Parse("""
defaults:
  - match: "Microsoft.UI.Reactor.Hooks.*"
    category: hooks
    guide-pages: [hooks, effects]
""");

    private const string TinyXml = """
<?xml version="1.0"?>
<doc>
  <assembly><name>Reactor</name></assembly>
  <members>
    <member name="T:Microsoft.UI.Reactor.Hooks.UseState">
      <summary>State hook.</summary>
    </member>
    <member name="M:Microsoft.UI.Reactor.Hooks.UseState.SetValue(System.Int32)">
      <summary>Updates the value.</summary>
      <seealso cref="T:Microsoft.UI.Reactor.Hooks.UseState"/>
    </member>
  </members>
</doc>
""";

    private static ReferenceGenResult GenerateFromXml(string xml) =>
        new ReferenceGenerator().Generate(XmlDocReader.ReadString(xml), StandardMap());

    [Fact]
    public void MarkerExpansion_ResolvesShortName()
    {
        var result = GenerateFromXml(TinyXml);
        var findings = new List<RefGenFinding>();

        var template = "# Hooks page\n\nSee <!-- ref:UseState --> for state.";
        var expanded = ReferenceLinkInjector.ExpandMarkers(template, "hooks", result, findings);

        Assert.Contains("[UseState](reference/hooks/UseState.md)", expanded);
        Assert.Empty(findings);
    }

    [Fact]
    public void MarkerExpansion_ResolvesFullCref()
    {
        var result = GenerateFromXml(TinyXml);
        var findings = new List<RefGenFinding>();

        var template = "<!-- ref:T:Microsoft.UI.Reactor.Hooks.UseState -->";
        var expanded = ReferenceLinkInjector.ExpandMarkers(template, "hooks", result, findings);

        Assert.Equal("[UseState](reference/hooks/UseState.md)", expanded);
        Assert.Empty(findings);
    }

    /// <summary>
    /// A member marker lands on the section of its type page, not on a page of
    /// its own: the new layout has no page per property or method.
    /// </summary>
    [Fact]
    public void MarkerExpansion_MemberCref_LinksToTheSectionAnchor()
    {
        var result = GenerateFromXml(TinyXml);
        var findings = new List<RefGenFinding>();

        var expanded = ReferenceLinkInjector.ExpandMarkers(
            "<!-- ref:M:Microsoft.UI.Reactor.Hooks.UseState.SetValue(System.Int32) -->", "hooks", result, findings);

        Assert.Empty(findings);
        Assert.StartsWith("[SetValue](reference/hooks/UseState.md#", expanded, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Pages, p => p.RelativePath.EndsWith("/SetValue.md", StringComparison.Ordinal));

        // The anchor must name a heading that is really on the page.
        var anchor = expanded[(expanded.IndexOf('#') + 1)..^1];
        var page = result.Pages.Single(p => p.RelativePath == "reference/hooks/UseState.md");
        var headings = page.Body.Replace("\r\n", "\n").Split('\n')
            .Where(l => l.StartsWith('#'))
            .Select(l => l.TrimStart('#').Trim());
        var tracker = new SlugTracker();
        Assert.Contains(anchor, headings.Select(tracker.Add));
    }

    /// <summary>
    /// Links are relative to the guide page, so a template that lives in a
    /// sub-folder climbs one level per segment of its id.
    /// </summary>
    [Fact]
    public void MarkerExpansion_NestedTemplate_ClimbsToTheGuideRoot()
    {
        var result = GenerateFromXml(TinyXml);
        var findings = new List<RefGenFinding>();

        var expanded = ReferenceLinkInjector.ExpandMarkers("<!-- ref:UseState -->", "recipes/forms", result, findings);

        Assert.Equal("[UseState](../reference/hooks/UseState.md)", expanded);
    }

    [Fact]
    public void MarkerExpansion_UnknownMember_EmitsFinding()
    {
        var result = GenerateFromXml(TinyXml);
        var findings = new List<RefGenFinding>();

        var template = "<!-- ref:DoesNotExist -->";
        var expanded = ReferenceLinkInjector.ExpandMarkers(template, "hooks", result, findings);

        // The marker must not survive into the output: it's an HTML comment,
        // so passing it through renders as nothing at all and silently
        // deletes the cross-reference from the published page.
        Assert.DoesNotContain("<!-- ref:", expanded, StringComparison.Ordinal);
        Assert.Equal("`DoesNotExist`", expanded);
        Assert.Contains(findings, f => f.Code == "REACTOR_DOC_REFMARKER_001");
    }

    /// <summary>
    /// The regression oracle for the raw-marker leak. Before the fix an
    /// unresolvable marker was emitted verbatim and the reader saw an empty
    /// gap mid-sentence with no sign that a cross-reference had gone missing.
    /// </summary>
    [Fact]
    public void MarkerExpansion_UnresolvableMarker_NeverLeaksIntoOutput()
    {
        var result = GenerateFromXml(TinyXml);
        var findings = new List<RefGenFinding>();

        var template = """
            Draw with <!-- ref:Win2DCanvas --> and size it via
            <!-- ref:T:Microsoft.UI.Reactor.Factories.Win2DCanvasElement --> today.
            """;
        var expanded = ReferenceLinkInjector.ExpandMarkers(template, "win2d-canvas", result, findings);

        Assert.DoesNotContain("<!-- ref:", expanded, StringComparison.Ordinal);
        Assert.DoesNotContain("-->", expanded, StringComparison.Ordinal);
        // Degrades exactly as an unresolvable <see cref> does, so the
        // sentence still names the thing it is talking about.
        Assert.Contains("Draw with `Win2DCanvas` and size it via", expanded, StringComparison.Ordinal);
        Assert.Contains("`Win2DCanvasElement` today.", expanded, StringComparison.Ordinal);
        Assert.Equal(2, findings.Count(f => f.Code == "REACTOR_DOC_REFMARKER_001"));
    }

    /// <summary>
    /// Positive control for the test above: a marker that does resolve is
    /// expanded to a link, so the "no raw marker" assertion passes because the
    /// marker was replaced, not because the pattern never matched.
    /// </summary>
    [Fact]
    public void MarkerExpansion_ResolvableMarker_AlsoLeavesNoRawMarker()
    {
        var result = GenerateFromXml(TinyXml);
        var findings = new List<RefGenFinding>();

        var expanded = ReferenceLinkInjector.ExpandMarkers(
            "Use <!-- ref:UseState --> here.", "hooks", result, findings);

        Assert.DoesNotContain("<!-- ref:", expanded, StringComparison.Ordinal);
        Assert.Equal("Use [UseState](reference/hooks/UseState.md) here.", expanded);
        Assert.Empty(findings);
    }

    [Fact]
    public void LintOrphanedGuidePages_EmitsW002_ForUnreferencedPage()
    {
        // hooks.md and effects.md are declared as guide pages; only hooks.md
        // has an inbound marker.
        var reverseIndex = ReferenceLinkInjector.BuildReverseIndex(new[]
        {
            ("hooks", "<!-- ref:UseState -->"),
            ("effects", "no markers here"),
        });
        var findings = ReferenceLinkInjector.LintOrphanedGuidePages(
            new[] { "hooks", "effects" },
            new[] { "hooks", "effects" },
            reverseIndex).ToList();

        var finding = Assert.Single(findings);
        Assert.Equal("REACTOR_DOC_REGISTRY_W002", finding.Code);
        Assert.Contains("'effects'", finding.Message, StringComparison.Ordinal);
        Assert.Equal("effects.md.dt", finding.FilePath);
        Assert.DoesNotContain("hooks", finding.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Many orphans become one finding, so the warning does not bury the rest
    /// of the phase output. Pages with no template are not orphans.
    /// </summary>
    [Fact]
    public void LintOrphanedGuidePages_AggregatesSeveralOrphansIntoOneFinding()
    {
        var reverseIndex = ReferenceLinkInjector.BuildReverseIndex(new[] { ("hooks", "<!-- ref:UseState -->") });

        var findings = ReferenceLinkInjector.LintOrphanedGuidePages(
            new[] { "layout", "hooks", "effects", "effects", "no-template" },
            new[] { "hooks", "effects", "layout" },
            reverseIndex).ToList();

        var finding = Assert.Single(findings);
        Assert.StartsWith("2 guide pages have", finding.Message, StringComparison.Ordinal);
        Assert.EndsWith(": effects, layout", finding.Message, StringComparison.Ordinal);
        Assert.Equal("docs/_pipeline/reference-map.yaml", finding.FilePath);
    }

    [Fact]
    public void ReverseIndex_ExtractsMarkersAcrossTemplates()
    {
        var index = ReferenceLinkInjector.BuildReverseIndex(new[]
        {
            ("hooks", "Use <!-- ref:UseState --> and <!-- ref:UseEffect --> and <!-- ref:UseState --> again"),
            ("effects", "Also see <!-- ref:UseEffect -->"),
        });

        Assert.Equal(2, index.Count);
        // A template that names the same member twice is listed once.
        Assert.Equal("hooks", Assert.Single(index["UseState"]).TemplateId);
        Assert.Equal(new[] { "hooks", "effects" }, index["UseEffect"].Select(r => r.TemplateId));
    }
}
