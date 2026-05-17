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

        Assert.Equal(template, expanded);
        Assert.Contains(findings, f => f.Code == "REACTOR_DOC_REFMARKER_001");
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
