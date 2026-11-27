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
    private const string Win2DNs = "Microsoft.UI.Reactor.Advanced.Win2D";
    private const string ElementId = "T:Microsoft.UI.Reactor.Core.Element";
    private const string ChartId = "T:Microsoft.UI.Reactor.Charting.ChartElement";
    private const string AdvancedFactoriesId = "T:Microsoft.UI.Reactor.Advanced.Factories";
    private const string ExtensionsId = "T:Microsoft.UI.Reactor.ElementExtensions";
    private const string GlowId = "M:Microsoft.UI.Reactor.ElementExtensions.Glow``1(``0)";
    private const string CoreMarginId = "M:Microsoft.UI.Reactor.ElementExtensions.Margin``1(``0,System.Double)";
    private const string AdvancedOpacityId = "M:Microsoft.UI.Reactor.ElementExtensions.Opacity``1(``0,System.Single)";
    private const string Win2DMarginId = "M:Microsoft.UI.Reactor.Advanced.Win2D.Win2DModifiers.Margin(Microsoft.UI.Reactor.Charting.ChartElement,System.Double)";
    private const string UseDrawId = "M:Microsoft.UI.Reactor.Advanced.Win2D.DrawHooks.UseDraw(Microsoft.UI.Reactor.Core.RenderContext)";
    private const string AppModeId = "T:Microsoft.UI.Reactor.Hosting.AppMode";

    private const string AdvancedXml = """
<?xml version="1.0"?>
<doc>
  <assembly><name>Microsoft.UI.Reactor.Advanced</name></assembly>
  <members>
    <member name="T:Microsoft.UI.Reactor.Charting.ChartElement"><summary>Describes a chart.</summary></member>
    <member name="P:Microsoft.UI.Reactor.Charting.ChartElement.OnPick">
      <summary>Runs when a point is picked.</summary>
      <remarks>Runs on a worker thread, so marshal UI work yourself.</remarks>
    </member>
    <member name="M:Microsoft.UI.Reactor.Advanced.Factories.Chart">
      <summary>Creates a chart. Put it in a <see cref="T:Microsoft.UI.Reactor.Core.StackElement"/>.</summary>
    </member>
    <member name="M:Microsoft.UI.Reactor.ElementExtensions.Glow``1(``0)"><summary>Adds a glow.</summary></member>
    <member name="M:Microsoft.UI.Reactor.Advanced.Win2D.DrawHooks.UseDraw(Microsoft.UI.Reactor.Core.RenderContext)">
      <summary>Keeps draw state. Read <see href="docs/guide/win2d-canvas.md#threading">the threading notes</see>.</summary>
      <seealso href="docs/guide/hooks.md">Hooks</seealso>
    </member>
  </members>
</doc>
""";

    /// <summary>The core test map, plus an area for the Advanced chart element so its factory gets a page of its own.</summary>
    private static ReferenceMap Map() => ReferenceMap.Parse(ReferenceGenTests.MapYaml.Replace(
        "modifier-areas:",
        "  - id: charts\n    title: Charts\n    summary: \"Charts.\"\n    elements: [\"Chart\"]\nmodifier-areas:",
        StringComparison.Ordinal));

    private static ApiType StaticClass(string ns, string name) => new()
    {
        DocId = "T:" + ns + "." + name, Namespace = ns, Name = name, DisplayName = name,
        Kind = ApiTypeKind.StaticClass, Declaration = "public static class " + name,
    };

    private static ApiMember Extension(ApiType owner, string docId, string name, string signature, string shortSignature,
        ApiParam[] ps, string returnType, string extends, string[]? typeParams = null, string? returnDocId = null)
    {
        var m = new ApiMember
        {
            DocId = docId, Kind = ApiMemberKind.Method, Name = name, DeclaringType = owner,
            Signature = signature, ShortSignature = shortSignature, Params = ps, ReturnType = returnType, ReturnDocId = returnDocId,
            IsStatic = true, IsExtension = true, ExtendedTypeDocIds = new[] { extends },
            TypeParameters = typeParams ?? Array.Empty<string>(),
        };
        owner.Members.Add(m);
        return m;
    }

    private static ApiModel AdvancedModel()
    {
        var chart = new ApiType
        {
            DocId = ChartId, Namespace = "Microsoft.UI.Reactor.Charting", Name = "ChartElement", DisplayName = "ChartElement",
            Kind = ApiTypeKind.Record, Declaration = "public record ChartElement : Element", BaseDocId = ElementId,
        };
        chart.Members.Add(new ApiMember
        {
            DocId = "P:Microsoft.UI.Reactor.Charting.ChartElement.OnPick", Kind = ApiMemberKind.Property, Name = "OnPick",
            DeclaringType = chart, Signature = "public Action? OnPick { get; init; }", ShortSignature = "OnPick", ReturnType = "Action?",
        });
        var factories = StaticClass(Advanced, "Factories");
        factories.Members.Add(new ApiMember
        {
            DocId = "M:Microsoft.UI.Reactor.Advanced.Factories.Chart", Kind = ApiMemberKind.Method, Name = "Chart",
            DeclaringType = factories, Signature = "public static ChartElement Chart()", ShortSignature = "Chart()",
            ReturnType = "ChartElement", ReturnDocId = ChartId, IsStatic = true,
        });

        // The same doc id as the core class: a partial class split across assemblies.
        var extensions = StaticClass(Core, "ElementExtensions");
        var t = new[] { new ApiParam("element", "T", null, null, "this") };
        Extension(extensions, GlowId, "Glow", "public static T Glow<T>(this T element) where T : Element", "Glow<T>(T)",
            t, "T", ElementId, new[] { "T" });
        // One more overload of a core modifier, in the same split class.
        Extension(extensions, AdvancedOpacityId, "Opacity", "public static T Opacity<T>(this T element, float value) where T : Element",
            "Opacity<T>(T, float)", new[] { t[0], new ApiParam("value", "float", null, null, "") }, "T", ElementId, new[] { "T" });
        // The same doc id as a core member: Merge keeps the core one.
        Extension(extensions, CoreMarginId, "Margin", "public static T Margin<T>(this T element, double uniform) where T : Element",
            "Margin<T>(T, double)", new[] { t[0], new ApiParam("uniform", "double", null, null, "") }, "T", ElementId, new[] { "T" });

        // An overload of a core modifier on an Advanced class.
        var win2dModifiers = StaticClass(Win2DNs, "Win2DModifiers");
        Extension(win2dModifiers, Win2DMarginId, "Margin", "public static ChartElement Margin(this ChartElement chart, double uniform)",
            "Margin(ChartElement, double)",
            new[] { new ApiParam("chart", "ChartElement", ChartId, null, "this"), new ApiParam("uniform", "double", null, null, "") },
            "ChartElement", ChartId, returnDocId: ChartId);

        var drawHooks = StaticClass(Win2DNs, "DrawHooks");
        Extension(drawHooks, UseDrawId, "UseDraw", "public static object UseDraw(this RenderContext ctx)", "UseDraw(RenderContext)",
            new[] { new ApiParam("ctx", "RenderContext", ReferencePlanner.RenderContextDocId, null, "this") },
            "object", ReferencePlanner.RenderContextDocId);

        // An enum both assemblies declare: one shared value and one new one.
        var mode = new ApiType
        {
            DocId = AppModeId, Namespace = "Microsoft.UI.Reactor.Hosting", Name = "AppMode", DisplayName = "AppMode",
            Kind = ApiTypeKind.Enum, Declaration = "public enum AppMode",
        };
        foreach (var (name, value) in new[] { ("Tray", "1"), ("Kiosk", "2") })
            mode.EnumValues.Add(new ApiMember
            {
                DocId = "F:Microsoft.UI.Reactor.Hosting.AppMode." + name, Kind = ApiMemberKind.Field, Name = name,
                DeclaringType = mode, Signature = name + " = " + value, ShortSignature = name, ConstantValue = value, IsStatic = true,
            });

        var types = new[] { chart, factories, extensions, win2dModifiers, drawHooks, mode };
        foreach (var type in types) type.SetPackage(Advanced);
        return new ApiModel(types, fromMetadata: true);
    }

    private static ApiModel CoreModel()
    {
        var model = ReferenceGenTests.Model();
        foreach (var t in model.Types) t.SetPackage(Core);
        return model;
    }

    private static ApiModel Merged() => ApiModel.Merge(new[] { CoreModel(), AdvancedModel() });

    private static ReferenceGenResult Generate()
    {
        var docs = XmlDocReader.ReadString(ReferenceGenTests.Xml).Concat(XmlDocReader.ReadString(AdvancedXml)).ToList();
        return new ReferenceGenerator().Generate(docs, Map(), Merged());
    }

    private static string Body(ReferenceGenResult r, string path) => Assert.Single(r.Pages, p => p.RelativePath == path).Body;

    private static string PageOf(ReferenceGenResult r, string docId) => Body(r, r.LinksByDocId[docId].RelativePath);

    [Fact]
    public void Merge_UnionsAPartialTypeAndKeepsEachMembersPackage()
    {
        var model = Merged();

        var extensions = Assert.Single(model.Types, t => t.DocId == ExtensionsId);
        Assert.Equal(Core, extensions.Package);
        Assert.Equal(Advanced, extensions.Members.Single(m => m.DocId == GlowId).Package);
        Assert.Same(extensions.Members.Single(m => m.DocId == GlowId), model.MembersByDocId[GlowId]);
        Assert.Equal(Advanced, model.TypesByDocId[ChartId].Package);

        // A member both declare is kept once, from the first assembly.
        var margin = Assert.Single(extensions.Members, m => m.DocId == CoreMarginId);
        Assert.Equal(Core, margin.Package);

        // Enum values are unioned the same way.
        var mode = model.TypesByDocId[AppModeId];
        Assert.Equal(new[] { "Window", "Tray", "Kiosk" }, mode.EnumValues.Select(v => v.Name));
        Assert.Equal(Core, mode.EnumValues.Single(v => v.Name == "Tray").Package);
        Assert.Equal(Advanced, mode.EnumValues.Single(v => v.Name == "Kiosk").Package);
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

        // A property's remarks (a callback's threading contract) follow the property table.
        Assert.Contains("| `OnPick` | `Action?` | Runs when a point is picked. |", body);
        Assert.Contains("**`OnPick`**\n\nRuns on a worker thread, so marshal UI work yourself.", body);

        // Core pages stay as they were: no package note.
        Assert.DoesNotContain("Package:", Body(r, "reference/elements/buttons.md"));
    }

    [Fact]
    public void AdvancedFactories_AreFactories_ThatNameTheirUsingStaticAndPackage()
    {
        var r = Generate();
        var location = r.LinksByDocId["M:Microsoft.UI.Reactor.Advanced.Factories.Chart"];
        Assert.Equal("reference/factories/charts.md", location.RelativePath);

        var body = Body(r, location.RelativePath);
        Assert.Contains("Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.", body);
        // The page intro names the Advanced class, not the core `Factories`.
        Assert.Contains("These are on `Microsoft.UI.Reactor.Advanced.Factories` in the `Microsoft.UI.Reactor.Advanced` package, " +
            "so add `using static Microsoft.UI.Reactor.Advanced.Factories;`.", body);
        Assert.DoesNotContain("using static Microsoft.UI.Reactor.Factories;", body);
        Assert.Contains("using static Microsoft.UI.Reactor.Factories;", Body(r, "reference/factories/layout.md"));

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
        Assert.Contains("Declared in `ElementExtensions` (package `Microsoft.UI.Reactor.Advanced`, `using Microsoft.UI.Reactor;`).", PageOf(r, GlowId));

        // A class split across the packages names only some of its overloads.
        Assert.Contains("Declared in `ElementExtensions` (some overloads in package `Microsoft.UI.Reactor.Advanced`, `using Microsoft.UI.Reactor;`).",
            PageOf(r, AdvancedOpacityId));
    }

    /// <summary>One overload set from a core class and an Advanced class labels only the
    /// Advanced class, with the namespace a reader imports, and lists the core class first.</summary>
    [Fact]
    public void MixedOverloadSet_LabelsOnlyTheAdvancedClass()
    {
        var r = Generate();
        var body = PageOf(r, Win2DMarginId);
        Assert.Equal(r.LinksByDocId[CoreMarginId].RelativePath, r.LinksByDocId[Win2DMarginId].RelativePath);
        Assert.Contains("Declared in `ElementExtensions`, `Win2DModifiers` (package `Microsoft.UI.Reactor.Advanced`, " +
            "`using Microsoft.UI.Reactor.Advanced.Win2D;`).", body);
    }

    /// <summary>An Advanced hook names its package and namespace, and the repo-root guide
    /// links in its doc comment (inline and see-also) are made relative to the page.</summary>
    [Fact]
    public void AdvancedHook_NamesPackageAndNamespace_AndRewritesGuideLinks()
    {
        var r = Generate();
        var location = r.LinksByDocId[UseDrawId];
        Assert.StartsWith("reference/hooks/", location.RelativePath, StringComparison.Ordinal);

        var body = Body(r, location.RelativePath);
        Assert.Contains("declared in `DrawHooks`. Package: `Microsoft.UI.Reactor.Advanced`. Add `using Microsoft.UI.Reactor.Advanced.Win2D;`.", body);
        Assert.Contains("[the threading notes](../../win2d-canvas.md#threading)", body);
        Assert.Contains("[Hooks](../../hooks.md)", body);
        Assert.DoesNotContain("](docs/guide/", body);

        Assert.DoesNotContain("Package:", Body(r, r.LinksByDocId["M:Microsoft.UI.Reactor.Core.RenderContext.UseState``1(``0)"].RelativePath));
    }
}
