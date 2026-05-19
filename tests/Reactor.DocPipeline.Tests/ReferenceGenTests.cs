using Microsoft.UI.Reactor.Cli.Docs;
using Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Spec 041 §10.4 / §7.1.2 — XML-doc → MD reference generation.
/// </summary>
public class ReferenceGenTests
{
    private static ReferenceMap StandardMap() => ReferenceMap.Parse("""
defaults:
  - match: "Microsoft.UI.Reactor.Hooks.*"
    category: hooks
    guide-pages: [hooks, effects]
""");

    private static string FixtureDir =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "refgen");

    [Fact]
    public void XmlDocReader_ParsesMembersAndKinds()
    {
        var path = Path.Combine(FixtureDir, "tiny.xml");
        var members = XmlDocReader.Read(path);

        Assert.Equal(2, members.Count);
        var t = members.Single(m => m.Cref.StartsWith("T:"));
        var meth = members.Single(m => m.Cref.StartsWith("M:"));
        Assert.Equal(MemberKind.Type, t.Kind);
        Assert.Equal(MemberKind.Method, meth.Kind);
        Assert.Contains("Stores a value", t.Summary);
        Assert.Single(meth.Params);
        Assert.Equal("value", meth.Params[0].Name);
        Assert.Equal("The previous value.", meth.Returns);
        Assert.Single(meth.SeeAlsos);
    }

    [Fact]
    public void Generator_EmitsPageForEachRoutedMember()
    {
        var path = Path.Combine(FixtureDir, "tiny.xml");
        var gen = new ReferenceGenerator();
        var result = gen.Generate(path, StandardMap(), referenceRoot: "/tmp/docs/guide",
            categoryAllowList: new HashSet<string>() { "hooks" });

        Assert.Equal(2, result.Pages.Count);
        // Methods + types share the same naming policy (shortname only) so
        // the output paths are deterministic.
        Assert.Contains(result.Pages, p => p.Route.ShortName == "UseState");
        Assert.Contains(result.Pages, p => p.Route.ShortName == "SetValue");
    }

    [Fact]
    public void CrefResolution_ProducesRelativeLinkToTargetPage()
    {
        var path = Path.Combine(FixtureDir, "tiny.xml");
        var gen = new ReferenceGenerator();
        var result = gen.Generate(path, StandardMap(), referenceRoot: "/tmp/docs/guide",
            categoryAllowList: new HashSet<string>() { "hooks" });

        // SetValue references UseState via both <see> and <seealso>.
        var page = result.Pages.Single(p => p.Route.ShortName == "SetValue");
        // Both pages live in reference/hooks/, so the link is a sibling.
        Assert.Contains("[UseState](UseState.md)", page.Body);
        // And the See Also section is present.
        Assert.Contains("## See Also", page.Body);
    }

    [Fact]
    public void MissingSummary_EmitsWarningAndPlaceholder()
    {
        var xml = """
<?xml version="1.0"?>
<doc>
  <assembly><name>Reactor</name></assembly>
  <members>
    <member name="T:Microsoft.UI.Reactor.Hooks.UseUndocumented"></member>
  </members>
</doc>
""";
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, xml);
        try
        {
            var gen = new ReferenceGenerator();
            var result = gen.Generate(tmp, StandardMap(), referenceRoot: "/tmp/docs/guide",
                categoryAllowList: new HashSet<string>() { "hooks" });

            var page = Assert.Single(result.Pages);
            Assert.Contains("*Summary pending.*", page.Body);
            Assert.Contains(result.Findings, f => f.Code == "REACTOR_DOC_REFGEN_W001");
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void NameCollision_Across_TypesAndProperties_Emits_REFGEN_002()
    {
        // Pathological case: a type and an unrelated property share the same
        // trailing identifier in the same category.
        var xml = """
<?xml version="1.0"?>
<doc>
  <assembly><name>Reactor</name></assembly>
  <members>
    <member name="T:Microsoft.UI.Reactor.Hooks.Pending">
      <summary>Pending type.</summary>
    </member>
    <member name="P:Microsoft.UI.Reactor.Hooks.UseState.Pending">
      <summary>Pending property on UseState.</summary>
    </member>
  </members>
</doc>
""";
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, xml);
        try
        {
            var gen = new ReferenceGenerator();
            var result = gen.Generate(tmp, StandardMap(), referenceRoot: "/tmp/docs/guide",
                categoryAllowList: new HashSet<string>() { "hooks" });

            Assert.Contains(result.Findings, f => f.Code == "REACTOR_DOC_REFGEN_002");
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void OverloadsDoNotTriggerCollision()
    {
        var xml = """
<?xml version="1.0"?>
<doc>
  <assembly><name>Reactor</name></assembly>
  <members>
    <member name="M:Microsoft.UI.Reactor.Hooks.UseState.SetValue(System.Int32)">
      <summary>Set the value to an int.</summary>
    </member>
    <member name="M:Microsoft.UI.Reactor.Hooks.UseState.SetValue(System.String)">
      <summary>Set the value to a string.</summary>
    </member>
  </members>
</doc>
""";
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, xml);
        try
        {
            var gen = new ReferenceGenerator();
            var result = gen.Generate(tmp, StandardMap(), referenceRoot: "/tmp/docs/guide",
                categoryAllowList: new HashSet<string>() { "hooks" });

            Assert.DoesNotContain(result.Findings, f => f.Code == "REACTOR_DOC_REFGEN_002");
            // First-seen overload wins the page in Phase 1B.
            Assert.Single(result.Pages);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void CategoryAllowList_FiltersOutOtherNamespaces()
    {
        var xml = """
<?xml version="1.0"?>
<doc>
  <assembly><name>Reactor</name></assembly>
  <members>
    <member name="T:Microsoft.UI.Reactor.Hooks.UseFoo">
      <summary>Hook.</summary>
    </member>
    <member name="T:Microsoft.UI.Reactor.Factories.Foo">
      <summary>Factory.</summary>
    </member>
  </members>
</doc>
""";
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, xml);
        try
        {
            var map = ReferenceMap.Parse("""
defaults:
  - match: "Microsoft.UI.Reactor.Hooks.*"
    category: hooks
    guide-pages: [hooks]
  - match: "Microsoft.UI.Reactor.Factories.*"
    category: factories
    guide-pages: [layout]
""");
            var gen = new ReferenceGenerator();
            var result = gen.Generate(tmp, map, referenceRoot: "/tmp/docs/guide",
                categoryAllowList: new HashSet<string>() { "hooks" });
            Assert.Single(result.Pages);
            Assert.Equal("hooks", result.Pages[0].Route.Category);
        }
        finally { File.Delete(tmp); }
    }
}
