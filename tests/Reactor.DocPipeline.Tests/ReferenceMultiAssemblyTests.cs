using Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Issue #1721: the reference documents <c>Microsoft.UI.Reactor</c> and
/// <c>Microsoft.UI.Reactor.Advanced</c> as one merged model. The core surface is
/// <see cref="ReferenceGenTests.Model"/>; this adds a small "Advanced" assembly
/// that derives from, extends and links to it.
/// </summary>
public class ReferenceMultiAssemblyTests
{
    private const string Core = "Microsoft.UI.Reactor";
    private const string Advanced = "Microsoft.UI.Reactor.Advanced";
    private const string ElementId = "T:Microsoft.UI.Reactor.Core.Element";
    private const string ChartId = "T:Microsoft.UI.Reactor.Charting.ChartElement";
    private const string AdvancedFactoriesId = "T:Microsoft.UI.Reactor.Advanced.Factories";
    private const string ExtensionsId = "T:Microsoft.UI.Reactor.ElementExtensions";
    private const string GlowId = "M:Microsoft.UI.Reactor.ElementExtensions.Glow``1(``0)";

    private const string AdvancedXml = """
<?xml version="1.0"?>
<doc>
  <assembly><name>Microsoft.UI.Reactor.Advanced</name></assembly>
  <members>
    <member name="T:Microsoft.UI.Reactor.Charting.ChartElement"><summary>Describes a chart.</summary></member>
    <member name="M:Microsoft.UI.Reactor.Advanced.Factories.Chart">
      <summary>Creates a chart. Put it in a <see cref="T:Microsoft.UI.Reactor.Core.StackElement"/>.</summary>
    </member>
    <member name="M:Microsoft.UI.Reactor.ElementExtensions.Glow``1(``0)"><summary>Adds a glow.</summary></member>
  </members>
</doc>
""";

    private static ApiModel AdvancedModel()
    {
        var chart = new ApiType
        {
            DocId = ChartId, Namespace = "Microsoft.UI.Reactor.Charting", Name = "ChartElement", DisplayName = "ChartElement",
            Kind = ApiTypeKind.Record, Declaration = "public record ChartElement : Element", BaseDocId = ElementId,
        };
        var factories = new ApiType
        {
            DocId = AdvancedFactoriesId, Namespace = Advanced, Name = "Factories", DisplayName = "Factories",
            Kind = ApiTypeKind.StaticClass, Declaration = "public static class Factories",
        };
        factories.Members.Add(new ApiMember
        {
            DocId = "M:Microsoft.UI.Reactor.Advanced.Factories.Chart", Kind = ApiMemberKind.Method, Name = "Chart",
            DeclaringType = factories, Signature = "public static ChartElement Chart()", ShortSignature = "Chart()",
            ReturnType = "ChartElement", ReturnDocId = ChartId, IsStatic = true,
        });
        // The same doc id as the core class: a partial class split across assemblies.
        var extensions = new ApiType
        {
            DocId = ExtensionsId, Namespace = Core, Name = "ElementExtensions", DisplayName = "ElementExtensions",
            Kind = ApiTypeKind.StaticClass, Declaration = "public static class ElementExtensions",
        };
        extensions.Members.Add(new ApiMember
        {
            DocId = GlowId, Kind = ApiMemberKind.Method, Name = "Glow", DeclaringType = extensions,
            Signature = "public static T Glow<T>(this T element) where T : Element", ShortSignature = "Glow<T>(T)",
            Params = new[] { new ApiParam("element", "T", null, null, "this") }, ReturnType = "T",
            IsStatic = true, IsExtension = true, ExtendedTypeDocIds = new[] { ElementId }, TypeParameters = new[] { "T" },
        });
        var types = new[] { chart, factories, extensions };
        foreach (var t in types) t.SetPackage(Advanced);
        return new ApiModel(types, fromMetadata: true);
    }

    private static ApiModel CoreModel()
    {
        var model = ReferenceGenTests.Model();
        foreach (var t in model.Types) t.SetPackage(Core);
        return model;
    }

    private static ReferenceGenResult Generate()
    {
        var docs = XmlDocReader.ReadString(ReferenceGenTests.Xml).Concat(XmlDocReader.ReadString(AdvancedXml)).ToList();
        var model = ApiModel.Merge(new[] { CoreModel(), AdvancedModel() });
        return new ReferenceGenerator().Generate(docs, ReferenceGenTests.Map(), model);
    }

    private static string Body(ReferenceGenResult r, string path) => Assert.Single(r.Pages, p => p.RelativePath == path).Body;

    [Fact]
    public void Merge_UnionsAPartialTypeAndKeepsEachMembersPackage()
    {
        var model = ApiModel.Merge(new[] { CoreModel(), AdvancedModel() });

        var extensions = Assert.Single(model.Types, t => t.DocId == ExtensionsId);
        Assert.Equal(Core, extensions.Package);
        Assert.Equal(Advanced, extensions.Members.Single(m => m.DocId == GlowId).Package);
        Assert.Equal(Core, extensions.Members.Single(m => m.Name == "Margin").Package);
        Assert.Same(extensions.Members.Single(m => m.DocId == GlowId), model.MembersByDocId[GlowId]);
        Assert.Equal(Advanced, model.TypesByDocId[ChartId].Package);
    }

    /// <summary>An Advanced record that derives from the core <c>Element</c> is an
    /// element, and its page links the base type and names its package.</summary>
    [Fact]
    public void AdvancedElement_InheritsAcrossAssemblies_AndNamesItsPackage()
    {
        var r = Generate();
        var location = r.LinksByDocId[ChartId];
        Assert.StartsWith("reference/elements/", location.RelativePath, StringComparison.Ordinal);

        var body = Body(r, location.RelativePath);
        Assert.Contains("Namespace: `Microsoft.UI.Reactor.Charting`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`Element`]", body);
        Assert.Contains("**Created by:** [`Chart`]", body);

        // Core pages stay as they were: no package note.
        Assert.DoesNotContain("Package:", Body(r, "reference/elements/buttons.md"));
    }

    [Fact]
    public void AdvancedFactories_AreFactories_ThatNameTheirUsingStaticAndPackage()
    {
        var r = Generate();
        var location = r.LinksByDocId["M:Microsoft.UI.Reactor.Advanced.Factories.Chart"];
        Assert.StartsWith("reference/factories/", location.RelativePath, StringComparison.Ordinal);

        var body = Body(r, location.RelativePath);
        Assert.Contains("Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.", body);
        // A cref from the Advanced XML to a core type resolves to the core page.
        Assert.Contains("](../elements/layout.md#stackelement)", body);
        Assert.DoesNotContain(r.Findings, f => f.Code == "REACTOR_DOC_REFGEN_001" && f.Message.Contains("StackElement", StringComparison.Ordinal));
        Assert.Empty(ReferenceGenTests.BrokenLinks(r.Pages));
    }

    /// <summary>A modifier added to a core class by Advanced is documented beside the
    /// core ones, with its own package; the core modifiers on that page have none.</summary>
    [Fact]
    public void AdvancedMemberOfASharedClass_GetsItsPackage_CoreMembersDoNot()
    {
        var r = Generate();
        var glow = Body(r, r.LinksByDocId[GlowId].RelativePath);
        Assert.Contains("Declared in `ElementExtensions` (package `Microsoft.UI.Reactor.Advanced`).", glow);

        var margin = Body(r, r.LinksByDocId["M:Microsoft.UI.Reactor.ElementExtensions.Margin``1(``0,System.Double)"].RelativePath);
        Assert.Contains("Declared in `ElementExtensions`.", margin);
        Assert.DoesNotContain("(package", margin);
    }
}
