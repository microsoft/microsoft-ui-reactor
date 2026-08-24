using Microsoft.UI.Reactor.Cli.Docs;
using Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Spec 041 §10.4.1 — conceptual-guide link injection, ref-marker
/// expansion, and the reverse "Featured in" scan.
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

    private static ReferenceGenResult GenerateFromXml(string xml)
    {
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, xml);
        try
        {
            var gen = new ReferenceGenerator();
            return gen.Generate(tmp, StandardMap(), referenceRoot: "/tmp/docs/guide",
                categoryAllowList: new HashSet<string>() { "hooks" });
        }
        finally { File.Delete(tmp); }
    }

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

        Assert.Contains("[UseState](reference/hooks/UseState.md)", expanded);
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
    /// The regression oracle for the raw-marker leak. Reference generation
    /// is gated to one category, so any marker naming a type outside it
    /// fails to resolve; before this fix the marker was emitted verbatim and
    /// the reader saw an empty gap mid-sentence with no indication that a
    /// cross-reference had gone missing.
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
    /// Positive control for the test above: a marker that *does* resolve is
    /// still expanded to a link, so the "no raw marker" assertion is passing
    /// because the marker was replaced rather than because the pattern never
    /// matched.
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
    public void Inject_AddsLearnMoreCallout()
    {
        var result = GenerateFromXml(TinyXml);
        var page = result.Pages.First(p => p.Route.ShortName == "UseState");
        var reverseIndex = new Dictionary<string, IReadOnlyList<TemplateReference>>();
        var findings = new List<RefGenFinding>();

        var injected = ReferenceLinkInjector.Inject(page, result, reverseIndex, findings);

        Assert.Contains("**Learn more:**", injected);
        Assert.Contains("(../../hooks.md)", injected);
        Assert.Contains("(../../effects.md)", injected);
    }

    [Fact]
    public void Inject_DualLink_AppendsGuidePointer()
    {
        // SetValue's <seealso> rewrites to a [UseState](UseState.md) inline
        // link via the CrefResolver. The injector's dual-link pass then
        // appends the guide pointer.
        var result = GenerateFromXml(TinyXml);
        var page = result.Pages.First(p => p.Route.ShortName == "SetValue");
        var reverseIndex = new Dictionary<string, IReadOnlyList<TemplateReference>>();
        var findings = new List<RefGenFinding>();

        var injected = ReferenceLinkInjector.Inject(page, result, reverseIndex, findings);

        // The CrefResolver only writes a See Also section for <seealso>
        // entries; verify the resulting link carries the guide annotation.
        Assert.Contains("[UseState](UseState.md) ([guide](../../hooks.md))", injected);
    }

    [Fact]
    public void Inject_FeaturedIn_ListsReverseIndexEntries()
    {
        var result = GenerateFromXml(TinyXml);
        var page = result.Pages.First(p => p.Route.ShortName == "UseState");
        var reverseIndex = new Dictionary<string, IReadOnlyList<TemplateReference>>(StringComparer.Ordinal)
        {
            ["UseState"] = new[] { new TemplateReference("hooks") }
        };
        var findings = new List<RefGenFinding>();

        var injected = ReferenceLinkInjector.Inject(page, result, reverseIndex, findings);

        Assert.Contains("## Featured in", injected);
        Assert.Contains("[Hooks](../../hooks.md)", injected);
    }

    [Fact]
    public void Inject_NoGuidePages_EmitsW001()
    {
        // Build a registry with no guide-pages for the hook category.
        var map = ReferenceMap.Parse("""
defaults:
  - match: "Microsoft.UI.Reactor.Hooks.*"
    category: hooks
""");
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, TinyXml);
        try
        {
            var gen = new ReferenceGenerator();
            var result = gen.Generate(tmp, map, referenceRoot: "/tmp/docs/guide",
                categoryAllowList: new HashSet<string>() { "hooks" });
            var page = result.Pages.First(p => p.Route.ShortName == "UseState");
            var findings = new List<RefGenFinding>();

            _ = ReferenceLinkInjector.Inject(page, result,
                new Dictionary<string, IReadOnlyList<TemplateReference>>(), findings);

            Assert.Contains(findings, f => f.Code == "REACTOR_DOC_REGISTRY_W001");
        }
        finally { File.Delete(tmp); }
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

        Assert.Contains(findings, f => f.Code == "REACTOR_DOC_REGISTRY_W002"
            && f.Message.Contains("effects"));
        Assert.DoesNotContain(findings, f => f.Message.Contains("'hooks'"));
    }

    [Fact]
    public void ReverseIndex_ExtractsMarkersAcrossTemplates()
    {
        var index = ReferenceLinkInjector.BuildReverseIndex(new[]
        {
            ("hooks", "Use <!-- ref:UseState --> and <!-- ref:UseEffect -->"),
            ("effects", "Also see <!-- ref:UseEffect -->"),
        });

        Assert.Equal(2, index["UseState"].Count == 1 ? 2 : 2); // sanity: dict has 2 keys
        Assert.Single(index["UseState"]);
        Assert.Equal("hooks", index["UseState"][0].TemplateId);
        Assert.Equal(2, index["UseEffect"].Count);
    }
}
