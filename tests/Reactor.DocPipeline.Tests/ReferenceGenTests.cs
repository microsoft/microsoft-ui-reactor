using System.CodeDom.Compiler;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Reactor.Cli.Docs;
using Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Spec 041 §10.4 — the page layout of the API reference: one page per type with
/// member anchors, one page per hook, area pages for element records, factory
/// methods and modifiers, generated indexes, cref links, the nav fragment, and the
/// writer that owns <c>docs/guide/reference/</c>.
///
/// <para>The model is built by hand so each test controls the exact public surface.
/// It mirrors what <see cref="ApiModel.FromAssembly"/> reads from <c>Microsoft.UI.Reactor.dll</c>.</para>
/// </summary>
public class ReferenceGenTests
{
    private const string CoreNs = "Microsoft.UI.Reactor.Core";
    private const string HostingNs = "Microsoft.UI.Reactor.Hosting";
    private const string ElementId = "T:Microsoft.UI.Reactor.Core.Element";

    internal static ReferenceMap Map() => ReferenceMap.Parse("""
element-base: "Microsoft.UI.Reactor.Core.Element"
categories:
  - id: hooks
    title: Hooks
    layout: hooks
    summary: "The hooks."
    guide-pages: [hooks]
    groups:
      - id: state
        title: State
        summary: "Hooks that keep values."
        hooks: ["UseState*"]
  - id: components
    title: Components
    summary: "Component types."
  - id: elements
    title: Elements
    layout: elements
    summary: "Element records."
  - id: factories
    title: Factories
    layout: factories
    summary: "Factory methods."
  - id: modifiers
    title: Modifiers
    layout: modifiers
    summary: "Fluent modifiers."
  - id: hosting
    title: Hosting
    summary: "App hosting."
areas:
  - id: layout
    title: Layout
    summary: "Panels and stacks."
    elements: ["Stack"]
    factories: ["VStack", "Spacer"]
    guide-pages: [layout]
  - id: buttons
    title: Buttons
    summary: "Buttons."
    elements: ["Button"]
modifier-areas:
  - id: layout-and-spacing
    title: Layout and spacing
    summary: "Margins."
    match: ["Margin*"]
  - id: appearance
    title: Appearance
    summary: "Opacity."
    match: ["Opacity*"]
  - id: shared-controls
    title: Shared control modifiers
    summary: "Modifiers that several controls share."
defaults:
  - match: "Microsoft.UI.Reactor.Core.*"
    category: components
  - match: "Microsoft.UI.Reactor.Core.RenderContext.Use*"
    category: hooks
  - match: "Microsoft.UI.Reactor.Factories"
    category: factories
  - match: "Microsoft.UI.Reactor.ElementExtensions"
    category: modifiers
  - match: "Microsoft.UI.Reactor.Hosting.*"
    category: hosting
overrides:
  - cref: "T:Microsoft.UI.Reactor.Core.Element"
    category: elements
""");

    internal const string Xml = """
<?xml version="1.0"?>
<doc>
  <assembly><name>Reactor</name></assembly>
  <members>
    <member name="T:Microsoft.UI.Reactor.Core.Element"><summary>The base record of every element.</summary></member>
    <member name="T:Microsoft.UI.Reactor.Core.ButtonElement"><summary>Describes a button.</summary></member>
    <member name="P:Microsoft.UI.Reactor.Core.ButtonElement.Label"><summary>The button text.</summary></member>
    <member name="F:Microsoft.UI.Reactor.Core.ButtonElement.Descriptor"><summary>Reconciler plumbing.</summary></member>
    <member name="T:Microsoft.UI.Reactor.Core.StackElement"><summary>Describes a stack.</summary></member>
    <member name="P:Microsoft.UI.Reactor.Core.StackElement.Spacing"><summary>The space between children.</summary></member>
    <member name="T:Microsoft.UI.Reactor.Core.PickerElement"><summary>Describes a picker.</summary></member>
    <member name="M:Microsoft.UI.Reactor.Factories.Button(System.String)">
      <summary>Creates a button with a text label.</summary>
      <param name="content">The text to show.</param>
      <returns>The button element.</returns>
    </member>
    <member name="M:Microsoft.UI.Reactor.Factories.Button(Microsoft.UI.Reactor.Core.Element,System.Action)">
      <summary>Creates a button with custom content.</summary>
      <param name="content">The content to show.</param>
      <param name="onClick">Runs when the button is clicked.</param>
    </member>
    <member name="M:Microsoft.UI.Reactor.Factories.VStack(Microsoft.UI.Reactor.Core.Element[])"><summary>Stacks children vertically.</summary></member>
    <member name="M:Microsoft.UI.Reactor.Factories.Picker"><summary>Creates a picker.</summary></member>
    <member name="M:Microsoft.UI.Reactor.Factories.Spacer"><summary>Creates empty space.</summary></member>
    <member name="M:Microsoft.UI.Reactor.ElementExtensions.Margin``1(``0,System.Double)">
      <summary>Sets the outer margin. Works on a <see cref="T:Microsoft.UI.Reactor.Core.ButtonElement"/>, reads
      <see cref="M:Microsoft.UI.Reactor.Core.RenderContext.UseState``1(``0)"/>, and ignores <see cref="T:Microsoft.UI.Reactor.Core.Missing"/>.</summary>
      <param name="uniform">The margin on every side.</param>
      <seealso cref="M:Microsoft.UI.Reactor.ElementExtensions.Opacity``1(``0,System.Double)"/>
      <seealso cref="P:Microsoft.UI.Reactor.Hosting.ReactorApp.Count"/>
    </member>
    <member name="M:Microsoft.UI.Reactor.ElementExtensions.Opacity``1(``0,System.Double)"><summary>Sets the opacity.</summary></member>
    <member name="M:Microsoft.UI.Reactor.ElementExtensions.Label(Microsoft.UI.Reactor.Core.ButtonElement,System.String)"><summary>Sets the button label.</summary></member>
    <member name="M:Microsoft.UI.Reactor.ElementExtensions.Header``1(``0,System.String)"><summary>Sets a header.</summary></member>
    <member name="T:Microsoft.UI.Reactor.Core.RenderContext"><summary>The context passed to Render.</summary></member>
    <member name="M:Microsoft.UI.Reactor.Core.RenderContext.UseState``1(``0)">
      <summary>Declares a state value.</summary>
      <param name="initial">The first value.</param>
      <example><code>var (count, setCount) = ctx.UseState(0);</code></example>
    </member>
    <member name="M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect(System.Action,System.Object[])"><summary>Runs an effect.</summary></member>
    <member name="M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect(System.Func{System.Action},System.Object[])"><summary>Runs an effect with cleanup.</summary></member>
    <member name="M:Microsoft.UI.Reactor.Core.RenderContext.Rerender"><summary>Schedules another render.</summary></member>
    <member name="T:Microsoft.UI.Reactor.Core.Component"><summary>Base class for class components.</summary></member>
    <member name="M:Microsoft.UI.Reactor.Core.Component.UseState``1(``0)"><summary>Forwards to the render context.</summary></member>
    <member name="M:Microsoft.UI.Reactor.Core.Component.Render"><summary>Returns the element tree.</summary></member>
    <member name="P:Microsoft.UI.Reactor.Hosting.ReactorApp.Count"><summary>The number of open windows.</summary></member>
    <member name="P:Microsoft.UI.Reactor.Hosting.ReactorApp.Error"><summary>The last error.</summary></member>
    <member name="M:Microsoft.UI.Reactor.Hosting.ReactorApp.Run(System.Func{Microsoft.UI.Reactor.Core.Element})"><summary>Starts the app.</summary></member>
    <member name="T:Microsoft.UI.Reactor.Hosting.ReactorApp.Options"><summary>Options for the app.</summary></member>
    <member name="P:Microsoft.UI.Reactor.Hosting.ReactorApp.Options.Title"><summary>The window title.</summary></member>
    <member name="T:Microsoft.UI.Reactor.Hosting.AppMode"><summary>How the app starts.</summary></member>
    <member name="F:Microsoft.UI.Reactor.Hosting.AppMode.Window"><summary>Opens a window.</summary></member>
    <member name="F:Microsoft.UI.Reactor.Hosting.AppMode.Tray"><summary>Starts in the tray.</summary></member>
    <member name="T:Microsoft.UI.Reactor.Hosting.Backdrop"><summary>The window backdrop.</summary></member>
    <member name="T:Microsoft.UI.Reactor.Experimental.Probe"><summary>A type that no category matches.</summary></member>
  </members>
</doc>
""";

    // ── Model builder ────────────────────────────────────────────────────

    private static ApiType Type(string ns, string name, ApiTypeKind kind, string declaration,
        string? baseDocId = null, ApiType? outer = null)
    {
        var t = new ApiType
        {
            DocId = "T:" + (outer is null ? ns : outer.DocId[2..]) + "." + name,
            Namespace = ns,
            Name = name,
            DisplayName = outer is null ? name : outer.DisplayName + "." + name,
            Kind = kind,
            DeclaringType = outer,
        };
        t.Declaration = declaration;
        t.BaseDocId = baseDocId;
        return t;
    }

    private static ApiMember Add(ApiType owner, ApiMemberKind kind, string docId, string name, string signature,
        string shortSignature, ApiParam[]? ps = null, string? returnType = null, string? returnDocId = null,
        bool isStatic = false, string[]? extends = null, string[]? typeParams = null)
    {
        var m = new ApiMember
        {
            DocId = docId,
            Kind = kind,
            Name = name,
            DeclaringType = owner,
            Signature = signature,
            ShortSignature = shortSignature,
            Params = ps ?? Array.Empty<ApiParam>(),
            ReturnType = returnType,
            ReturnDocId = returnDocId,
            IsStatic = isStatic || extends is not null,
            IsExtension = extends is not null,
            ExtendedTypeDocIds = extends ?? Array.Empty<string>(),
            TypeParameters = typeParams ?? Array.Empty<string>(),
        };
        owner.Members.Add(m);
        return m;
    }

    private static ApiParam P(string name, string type, string? typeDocId = null, string modifier = "") =>
        new(name, type, typeDocId, null, modifier);

    private static void AddValue(ApiType owner, string name, string value) =>
        owner.EnumValues.Add(new ApiMember
        {
            DocId = "F:" + owner.DocId[2..] + "." + name,
            Kind = ApiMemberKind.Field,
            Name = name,
            DeclaringType = owner,
            Signature = name + " = " + value,
            ShortSignature = name,
            ConstantValue = value,
            IsStatic = true,
        });

    /// <summary>The test surface. <paramref name="reverse"/> builds the same types in
    /// the opposite order, which must not change a byte of output.</summary>
    internal static ApiModel Model(bool reverse = false)
    {
        var element = Type(CoreNs, "Element", ApiTypeKind.Record, "public abstract record Element");
        var button = Type(CoreNs, "ButtonElement", ApiTypeKind.Record, "public record ButtonElement(string Label) : Element", ElementId);
        Add(button, ApiMemberKind.Property, "P:Microsoft.UI.Reactor.Core.ButtonElement.Label", "Label",
            "public string Label { get; init; }", "Label", returnType: "string");
        Add(button, ApiMemberKind.Field, "F:Microsoft.UI.Reactor.Core.ButtonElement.Descriptor", "Descriptor",
            "public static readonly object Descriptor", "Descriptor", returnType: "object", isStatic: true);
        var stack = Type(CoreNs, "StackElement", ApiTypeKind.Record, "public record StackElement(Element[] Children) : Element", ElementId);
        Add(stack, ApiMemberKind.Property, "P:Microsoft.UI.Reactor.Core.StackElement.Spacing", "Spacing",
            "public double Spacing { get; init; }", "Spacing", returnType: "double");
        var picker = Type(CoreNs, "PickerElement", ApiTypeKind.Record, "public record PickerElement : Element", ElementId);

        var factories = Type("Microsoft.UI.Reactor", "Factories", ApiTypeKind.StaticClass, "public static class Factories");
        Add(factories, ApiMemberKind.Method, "M:Microsoft.UI.Reactor.Factories.Button(System.String)", "Button",
            "public static ButtonElement Button(string content)", "Button(string)",
            new[] { P("content", "string") }, "ButtonElement", button.DocId, isStatic: true);
        Add(factories, ApiMemberKind.Method, "M:Microsoft.UI.Reactor.Factories.Button(Microsoft.UI.Reactor.Core.Element,System.Action)", "Button",
            "public static ButtonElement Button(Element content, Action onClick)", "Button(Element, Action)",
            new[] { P("content", "Element", ElementId), P("onClick", "Action") }, "ButtonElement", button.DocId, isStatic: true);
        Add(factories, ApiMemberKind.Method, "M:Microsoft.UI.Reactor.Factories.VStack(Microsoft.UI.Reactor.Core.Element[])", "VStack",
            "public static StackElement VStack(params Element[] children)", "VStack(Element[])",
            new[] { P("children", "Element[]", null, "params") }, "StackElement", stack.DocId, isStatic: true);
        Add(factories, ApiMemberKind.Method, "M:Microsoft.UI.Reactor.Factories.Picker", "Picker",
            "public static PickerElement Picker()", "Picker()", returnType: "PickerElement", returnDocId: picker.DocId, isStatic: true);
        // Returns the element base, which has no area, so only the factory glob places it.
        Add(factories, ApiMemberKind.Method, "M:Microsoft.UI.Reactor.Factories.Spacer", "Spacer",
            "public static Element Spacer()", "Spacer()", returnType: "Element", returnDocId: ElementId, isStatic: true);

        var ext = Type("Microsoft.UI.Reactor", "ElementExtensions", ApiTypeKind.StaticClass, "public static class ElementExtensions");
        Add(ext, ApiMemberKind.Method, "M:Microsoft.UI.Reactor.ElementExtensions.Margin``1(``0,System.Double)", "Margin",
            "public static T Margin<T>(this T element, double uniform) where T : Element", "Margin<T>(T, double)",
            new[] { P("element", "T", null, "this"), P("uniform", "double") }, "T", extends: new[] { ElementId }, typeParams: new[] { "T" });
        Add(ext, ApiMemberKind.Method, "M:Microsoft.UI.Reactor.ElementExtensions.Opacity``1(``0,System.Double)", "Opacity",
            "public static T Opacity<T>(this T element, double value) where T : Element", "Opacity<T>(T, double)",
            new[] { P("element", "T", null, "this"), P("value", "double") }, "T", extends: new[] { ElementId }, typeParams: new[] { "T" });
        Add(ext, ApiMemberKind.Method, "M:Microsoft.UI.Reactor.ElementExtensions.Label(Microsoft.UI.Reactor.Core.ButtonElement,System.String)", "Label",
            "public static ButtonElement Label(this ButtonElement button, string text)", "Label(ButtonElement, string)",
            new[] { P("button", "ButtonElement", button.DocId, "this"), P("text", "string") }, "ButtonElement", button.DocId,
            extends: new[] { button.DocId });
        Add(ext, ApiMemberKind.Method, "M:Microsoft.UI.Reactor.ElementExtensions.Header``1(``0,System.String)", "Header",
            "public static T Header<T>(this T element, string header) where T : Element", "Header<T>(T, string)",
            new[] { P("element", "T", null, "this"), P("header", "string") }, "T",
            extends: new[] { button.DocId, stack.DocId }, typeParams: new[] { "T" });

        var ctx = Type(CoreNs, "RenderContext", ApiTypeKind.Class, "public sealed class RenderContext");
        Add(ctx, ApiMemberKind.Method, "M:Microsoft.UI.Reactor.Core.RenderContext.UseState``1(``0)", "UseState",
            "public (T Value, Action<T> Set) UseState<T>(T initial)", "UseState<T>(T)",
            new[] { P("initial", "T") }, "(T Value, Action<T> Set)", typeParams: new[] { "T" });
        Add(ctx, ApiMemberKind.Method, "M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect(System.Action,System.Object[])", "UseEffect",
            "public void UseEffect(Action effect, params object[] deps)", "UseEffect(Action, object[])",
            new[] { P("effect", "Action"), P("deps", "object[]", null, "params") }, "void");
        Add(ctx, ApiMemberKind.Method, "M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect(System.Func{System.Action},System.Object[])", "UseEffect",
            "public void UseEffect(Func<Action> effect, params object[] deps)", "UseEffect(Func<Action>, object[])",
            new[] { P("effect", "Func<Action>"), P("deps", "object[]", null, "params") }, "void");
        Add(ctx, ApiMemberKind.Method, "M:Microsoft.UI.Reactor.Core.RenderContext.Rerender", "Rerender",
            "public void Rerender()", "Rerender()", returnType: "void");

        var component = Type(CoreNs, "Component", ApiTypeKind.Class, "public abstract class Component");
        Add(component, ApiMemberKind.Method, "M:Microsoft.UI.Reactor.Core.Component.UseState``1(``0)", "UseState",
            "protected (T Value, Action<T> Set) UseState<T>(T initial)", "UseState<T>(T)",
            new[] { P("initial", "T") }, "(T Value, Action<T> Set)", typeParams: new[] { "T" });
        Add(component, ApiMemberKind.Method, "M:Microsoft.UI.Reactor.Core.Component.Render", "Render",
            "public abstract Element Render()", "Render()", returnType: "Element", returnDocId: ElementId);

        var app = Type(HostingNs, "ReactorApp", ApiTypeKind.Class, "public sealed class ReactorApp");
        Add(app, ApiMemberKind.Property, "P:Microsoft.UI.Reactor.Hosting.ReactorApp.Count", "Count",
            "public int Count { get; }", "Count", returnType: "int");
        Add(app, ApiMemberKind.Property, "P:Microsoft.UI.Reactor.Hosting.ReactorApp.Error", "Error",
            "public Exception? Error { get; }", "Error", returnType: "Exception?");
        Add(app, ApiMemberKind.Method, "M:Microsoft.UI.Reactor.Hosting.ReactorApp.Run(System.Func{Microsoft.UI.Reactor.Core.Element})", "Run",
            "public static void Run(Func<Element> root)", "Run(Func<Element>)",
            new[] { P("root", "Func<Element>") }, "void", isStatic: true);
        var options = Type(HostingNs, "Options", ApiTypeKind.Class, "public sealed class ReactorApp.Options", outer: app);
        Add(options, ApiMemberKind.Property, "P:Microsoft.UI.Reactor.Hosting.ReactorApp.Options.Title", "Title",
            "public string Title { get; set; }", "Title", returnType: "string");

        var mode = Type(HostingNs, "AppMode", ApiTypeKind.Enum, "public enum AppMode");
        AddValue(mode, "Window", "0");
        AddValue(mode, "Tray", "1");
        var backdrop = Type(HostingNs, "Backdrop", ApiTypeKind.Enum, "public enum Backdrop");
        AddValue(backdrop, "Mica", "0");
        AddValue(backdrop, "Acrylic", "1");

        var probe = Type("Microsoft.UI.Reactor.Experimental", "Probe", ApiTypeKind.Class, "public sealed class Probe");

        var types = new List<ApiType> { element, button, stack, picker, factories, ext, ctx, component, app, options, mode, backdrop, probe };
        if (reverse)
        {
            types.Reverse();
            foreach (var t in types) t.Members.Reverse();
        }
        return new ApiModel(types, fromMetadata: true);
    }

    internal static ReferenceGenResult Generate(bool reverse = false) =>
        new ReferenceGenerator().Generate(XmlDocReader.ReadString(Xml), Map(), Model(reverse));

    private static ReferencePage Page(ReferenceGenResult r, string relativePath) =>
        Assert.Single(r.Pages, p => p.RelativePath == relativePath);

    private static string Body(ReferenceGenResult r, string relativePath) => Page(r, relativePath).Body;

    private static HashSet<string> Ids(ReferenceGenResult r, string relativePath) =>
        ReferenceGenOverloadTests.HeadingIds(Body(r, relativePath));

    private static readonly Regex MarkdownLink = new(@"\]\(([^)\s]+)\)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Every link from one reference page to another (or to a heading on the same
    /// page) whose target page or anchor does not exist. Links to guide pages and
    /// to Microsoft Learn are outside the reference and not checked here.
    /// </summary>
    internal static List<string> BrokenLinks(IReadOnlyList<ReferencePage> pages)
    {
        var ids = pages.ToDictionary(p => p.RelativePath, p => ReferenceGenOverloadTests.HeadingIds(p.Body), StringComparer.Ordinal);
        var broken = new List<string>();
        foreach (var page in pages)
        {
            var inFence = false;
            foreach (var line in page.Body.Split('\n'))
            {
                if (line.StartsWith("```", StringComparison.Ordinal)) { inFence = !inFence; continue; }
                if (inFence) continue;
                foreach (Match m in MarkdownLink.Matches(line))
                {
                    var href = m.Groups[1].Value;
                    if (href.StartsWith("https://", StringComparison.Ordinal)) continue;
                    var hash = href.IndexOf('#');
                    var file = hash < 0 ? href : href[..hash];
                    var anchor = hash < 0 ? null : href[(hash + 1)..];
                    var target = file.Length == 0 ? page.RelativePath : Combine(page.RelativePath, file);
                    if (!target.StartsWith("reference/", StringComparison.Ordinal)) continue;
                    if (!ids.TryGetValue(target, out var targetIds)) broken.Add($"{page.RelativePath}: {href} (no page)");
                    else if (anchor is not null && !targetIds.Contains(anchor)) broken.Add($"{page.RelativePath}: {href} (no anchor)");
                }
            }
        }
        return broken;
    }

    private static string Combine(string fromPage, string href)
    {
        var parts = fromPage.Split('/')[..^1].ToList();
        foreach (var segment in href.Split('/'))
        {
            if (segment == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else if (segment != ".") parts.Add(segment);
        }
        return string.Join("/", parts);
    }

    // ── Page layout ──────────────────────────────────────────────────────

    /// <summary>
    /// A type gets one page. Its properties, methods and nested types are sections
    /// with their own anchors, not pages of their own (the old layout wrote
    /// <c>Count.md</c> and <c>Error.md</c>).
    /// </summary>
    [Fact]
    public void TypePage_HoldsMembersAsSections_NotAsPages()
    {
        var r = Generate();
        var page = Page(r, "reference/hosting/ReactorApp.md");
        Assert.Equal(PageKind.Type, page.Kind);

        var headings = ReferenceGenOverloadTests.Headings(page.Body);
        Assert.Contains("## Properties", headings);
        Assert.Contains("### `Count`", headings);
        Assert.Contains("### `Error`", headings);
        Assert.Contains("## Methods", headings);
        Assert.Contains("### `Run`", headings);
        Assert.Contains("## `ReactorApp.Options`", headings);
        Assert.Contains("#### `Title`", headings);

        var names = r.Pages.Select(p => Path.GetFileNameWithoutExtension(p.RelativePath)).ToHashSet(StringComparer.Ordinal);
        foreach (var member in new[] { "Count", "Error", "Run", "Title", "Options", "Rerender", "Render", "Label", "Spacing" })
            Assert.DoesNotContain(member, names);

        Assert.Equal(new RefLocation("reference/hosting/ReactorApp.md", "count", "ReactorApp.Count"),
            r.LinksByDocId["P:Microsoft.UI.Reactor.Hosting.ReactorApp.Count"]);
        Assert.Equal("reactorappoptions", r.LinksByDocId["T:Microsoft.UI.Reactor.Hosting.ReactorApp.Options"].Anchor);
        Assert.Equal("title", r.LinksByDocId["P:Microsoft.UI.Reactor.Hosting.ReactorApp.Options.Title"].Anchor);
    }

    /// <summary>A member section reads summary first, then the signature.</summary>
    [Fact]
    public void TypePage_MemberSection_ShowsSummaryBeforeSignature()
    {
        var body = Body(Generate(), "reference/hosting/ReactorApp.md");
        var count = body.IndexOf("### `Count`", StringComparison.Ordinal);
        var summary = body.IndexOf("The number of open windows.", count, StringComparison.Ordinal);
        var signature = body.IndexOf("public int Count { get; }", count, StringComparison.Ordinal);
        Assert.True(count >= 0 && summary > count && signature > summary, body);
    }

    /// <summary>Every anchor the generator records, and every link it writes between
    /// reference pages, lands on a heading that exists on the target page.</summary>
    [Fact]
    public void EveryRecordedLocationAndLink_LandsOnAnExistingHeading()
    {
        var r = Generate();
        var paths = r.Pages.Select(p => p.RelativePath).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, loc) in r.LinksByDocId.Concat(r.LinksByName))
        {
            Assert.True(paths.Contains(loc.RelativePath), $"{key} -> {loc.RelativePath} has no page");
            if (loc.Anchor is not null)
                Assert.True(Ids(r, loc.RelativePath).Contains(loc.Anchor), $"{key} -> {loc.RelativePath}#{loc.Anchor} has no heading");
        }
        Assert.Empty(BrokenLinks(r.Pages));
    }

    /// <summary>Positive control for <see cref="BrokenLinks"/>: it does find a missing
    /// page and a missing anchor, so an empty result above means something.</summary>
    [Fact]
    public void BrokenLinks_FindsAMissingPageAndAMissingAnchor()
    {
        var pages = Generate().Pages.ToList();
        pages.Add(new ReferencePage("reference/hooks/Probe.md", "hooks", "Probe", "", "# Probe\n\n[a](UseState.md#nope) [b](Gone.md) [c](UseState.md#example) [d](../../hooks.md)\n", PageKind.Hook));
        Assert.Equal(
            new[] { "reference/hooks/Probe.md: UseState.md#nope (no anchor)", "reference/hooks/Probe.md: Gone.md (no page)" },
            BrokenLinks(pages));
    }

    /// <summary>Two enums on one page both have a "Values" heading; each value links
    /// to the de-duplicated anchor of its own enum.</summary>
    [Fact]
    public void EnumValues_OnASharedPage_LinkToTheirOwnValuesHeading()
    {
        var r = Generate();
        var page = Page(r, "reference/hosting/enums.md");
        Assert.Equal(PageKind.Group, page.Kind);
        Assert.Contains("## `AppMode`", page.Body);
        Assert.Contains("## `Backdrop`", page.Body);
        Assert.Contains("| `Tray` | `1` | Starts in the tray. |", page.Body);
        Assert.Equal("values", r.LinksByDocId["F:Microsoft.UI.Reactor.Hosting.AppMode.Tray"].Anchor);
        Assert.Equal("values_1", r.LinksByDocId["F:Microsoft.UI.Reactor.Hosting.Backdrop.Mica"].Anchor);
        Assert.DoesNotContain(r.Pages, p => p.RelativePath.EndsWith("/AppMode.md", StringComparison.Ordinal));
    }

    // ── Hooks ────────────────────────────────────────────────────────────

    /// <summary>Each hook is one page with all of its overloads. The class-component
    /// forwarder joins the same page instead of making a second one.</summary>
    [Fact]
    public void Hook_IsOnePage_WithOverloadsAndTheComponentForwarder()
    {
        var r = Generate();
        Assert.Equal(new[] { "reference/hooks/UseEffect.md", "reference/hooks/UseState.md" },
            r.Pages.Where(p => p.Kind == PageKind.Hook).Select(p => p.RelativePath).OrderBy(p => p, StringComparer.Ordinal));

        var effect = Body(r, "reference/hooks/UseEffect.md");
        Assert.Contains("## Overloads", effect);
        Assert.Contains("[`UseEffect(Action, object[])`](#useeffectaction-object)", effect);
        Assert.Contains("[`UseEffect(Func<Action>, object[])`](#useeffectfuncaction-object)", effect);
        Assert.Contains("Declared on [`RenderContext`](../components/RenderContext.md).", effect);

        var state = Body(r, "reference/hooks/UseState.md");
        Assert.Contains("## Use from a class component", state);
        Assert.Contains("protected (T Value, Action<T> Set) UseState<T>(T initial)", state);
        Assert.Equal("reference/hooks/UseState.md", r.LinksByDocId["M:Microsoft.UI.Reactor.Core.Component.UseState``1(``0)"].RelativePath);

        // The owning type pages keep their other members and list the hooks by page.
        var ctx = Body(r, "reference/components/RenderContext.md");
        Assert.Contains("### `Rerender`", ctx);
        Assert.DoesNotContain("### `UseEffect`", ctx);
        Assert.Contains("- [`UseEffect`](../hooks/UseEffect.md)\n", ctx);
        var component = Body(r, "reference/components/Component.md");
        Assert.DoesNotContain("### `UseState`", component);
        Assert.Contains("- [`UseState`](../hooks/UseState.md)", component);
    }

    /// <summary>The hooks index groups hooks as the map says; the rest go under "Other hooks".</summary>
    [Fact]
    public void HooksIndex_FollowsTheMapGroups()
    {
        var body = Body(Generate(), "reference/hooks/index.md");
        var state = body.IndexOf("## State", StringComparison.Ordinal);
        var other = body.IndexOf("## Other hooks", StringComparison.Ordinal);
        Assert.True(state >= 0 && other > state, body);
        Assert.InRange(body.IndexOf("[`UseState`](UseState.md)", StringComparison.Ordinal), state, other);
        Assert.True(body.IndexOf("[`UseEffect`](UseEffect.md)", StringComparison.Ordinal) > other, body);
        Assert.Contains("**Learn more:** [Hooks](../../hooks.md)", body);
    }

    // ── Factories, modifiers, elements ───────────────────────────────────

    /// <summary>
    /// Factory methods land on area pages: by the area's factory glob first, then by
    /// the area of the element they return, then on "other". Each factory is a section
    /// whose Returns line links to the element record's section.
    /// </summary>
    [Fact]
    public void Factories_AreGroupedOnAreaPages()
    {
        var r = Generate();
        Assert.Equal("reference/factories/buttons.md", r.LinksByDocId["M:Microsoft.UI.Reactor.Factories.Button(System.String)"].RelativePath);
        Assert.Equal("reference/factories/layout.md", r.LinksByDocId["M:Microsoft.UI.Reactor.Factories.VStack(Microsoft.UI.Reactor.Core.Element[])"].RelativePath);
        Assert.Equal("reference/factories/layout.md", r.LinksByDocId["M:Microsoft.UI.Reactor.Factories.Spacer"].RelativePath);
        Assert.Equal("reference/factories/other.md", r.LinksByDocId["M:Microsoft.UI.Reactor.Factories.Picker"].RelativePath);
        Assert.Equal("reference/factories/index.md", r.LinksByDocId["T:Microsoft.UI.Reactor.Factories"].RelativePath);
        Assert.All(r.Pages.Where(p => p.CategoryId == "factories"), p => Assert.Contains(p.Kind, new[] { PageKind.FactoryArea, PageKind.CategoryIndex }));

        var buttons = Page(r, "reference/factories/buttons.md");
        Assert.Equal("Factories: Buttons", buttons.Title);
        Assert.Contains("## `Button`", buttons.Body);
        Assert.Contains("ButtonElement Button(string content)\nButtonElement Button(Element content, Action onClick)", buttons.Body);
        Assert.Contains("**Returns:** [`ButtonElement`](../elements/buttons.md#buttonelement).", buttons.Body);
        // Overloads that use different types for the same parameter list both.
        Assert.Contains("| `content` | `string` or [`Element`](../elements/Element.md) | The text to show. |", buttons.Body);
        Assert.Contains("**Learn more:** [Layout](../../layout.md)", Body(r, "reference/factories/layout.md"));
    }

    /// <summary>
    /// Modifiers land on area pages: by the first name glob, then by the one control
    /// area every extended type shares, then on the fallback area.
    /// </summary>
    [Fact]
    public void Modifiers_AreGroupedOnAreaPages()
    {
        var r = Generate();
        Assert.Equal("reference/modifiers/layout-and-spacing.md", r.LinksByDocId["M:Microsoft.UI.Reactor.ElementExtensions.Margin``1(``0,System.Double)"].RelativePath);
        Assert.Equal("reference/modifiers/appearance.md", r.LinksByDocId["M:Microsoft.UI.Reactor.ElementExtensions.Opacity``1(``0,System.Double)"].RelativePath);
        Assert.Equal("reference/modifiers/buttons.md", r.LinksByDocId["M:Microsoft.UI.Reactor.ElementExtensions.Label(Microsoft.UI.Reactor.Core.ButtonElement,System.String)"].RelativePath);
        Assert.Equal("reference/modifiers/shared-controls.md", r.LinksByDocId["M:Microsoft.UI.Reactor.ElementExtensions.Header``1(``0,System.String)"].RelativePath);
        Assert.Equal("reference/modifiers/index.md", r.LinksByDocId["T:Microsoft.UI.Reactor.ElementExtensions"].RelativePath);

        Assert.Equal("Modifiers: Layout and spacing", Page(r, "reference/modifiers/layout-and-spacing.md").Title);
        Assert.Equal("Control modifiers: Buttons", Page(r, "reference/modifiers/buttons.md").Title);
        var shared = Body(r, "reference/modifiers/shared-controls.md");
        Assert.Contains("**Applies to:** [`ButtonElement`](../elements/buttons.md#buttonelement), [`StackElement`](../elements/layout.md#stackelement).", shared);
        Assert.Contains("**Applies to:** any element.", Body(r, "reference/modifiers/appearance.md"));

        var index = Body(r, "reference/modifiers/index.md");
        var common = index.IndexOf("## Common modifiers", StringComparison.Ordinal);
        var controls = index.IndexOf("## Control modifiers", StringComparison.Ordinal);
        Assert.True(common >= 0 && controls > common, index);
        Assert.InRange(index.IndexOf("(appearance.md)", StringComparison.Ordinal), common, controls);
        Assert.True(index.IndexOf("(buttons.md)", StringComparison.Ordinal) > controls, index);
    }

    /// <summary>Element records share area pages with links to what creates and changes
    /// them. Reconciler plumbing (the static <c>Descriptor</c>) is not shown.</summary>
    [Fact]
    public void Elements_AreGroupedOnAreaPages_WithoutDescriptorPlumbing()
    {
        var r = Generate();
        var buttons = Page(r, "reference/elements/buttons.md");
        Assert.Equal(PageKind.ElementArea, buttons.Kind);
        Assert.Contains("## `ButtonElement`", buttons.Body);
        Assert.Contains("**Created by:** [`Button`](../factories/buttons.md#button)", buttons.Body);
        Assert.Contains("**Modifiers:** [`Header`](../modifiers/shared-controls.md#header), [`Label`](../modifiers/buttons.md#label)", buttons.Body);
        Assert.Contains("| `Label` | `string` | The button text. |", buttons.Body);
        Assert.DoesNotContain("Descriptor", buttons.Body);
        Assert.Contains("## `PickerElement`", Body(r, "reference/elements/other.md"));
        Assert.Equal(PageKind.Type, Page(r, "reference/elements/Element.md").Kind);
    }

    // ── Cref links ───────────────────────────────────────────────────────

    /// <summary>Crefs in summaries and see-also lists link to the new page and anchor.
    /// An unknown Reactor cref is plain code text and a finding, never a broken link.</summary>
    [Fact]
    public void Crefs_ResolveToTheNewPagesAndAnchors()
    {
        var r = Generate();
        var margin = Body(r, "reference/modifiers/layout-and-spacing.md");
        Assert.Contains("Works on a [`ButtonElement`](../elements/buttons.md#buttonelement), reads [`UseState`](../hooks/UseState.md), and ignores `Missing`.", margin);
        Assert.Contains("- [`Opacity`](appearance.md#opacity)", margin);
        Assert.Contains("- [`ReactorApp.Count`](../hosting/ReactorApp.md#count)", margin);
        Assert.DoesNotContain("Missing](", margin);

        var unresolved = Assert.Single(r.Findings, f => f.Code == "REACTOR_DOC_REFGEN_001");
        Assert.Contains("T:Microsoft.UI.Reactor.Core.Missing", unresolved.Message);
    }

    // ── Indexes ──────────────────────────────────────────────────────────

    /// <summary>Each category index links every page in the category, and the root
    /// index links every category index.</summary>
    [Fact]
    public void Indexes_LinkEveryPage()
    {
        var r = Generate();
        foreach (var category in r.Pages.Where(p => p.CategoryId is not null).GroupBy(p => p.CategoryId!))
        {
            var index = Body(r, $"reference/{category.Key}/index.md");
            foreach (var page in category.Where(p => p.Kind != PageKind.CategoryIndex))
                Assert.Contains("](" + Path.GetFileName(page.RelativePath) + ")", index);
        }

        var root = Page(r, "reference/index.md");
        Assert.Equal(PageKind.RootIndex, root.Kind);
        foreach (var id in new[] { "hooks", "components", "elements", "factories", "modifiers", "hosting" })
            Assert.Contains($"]({id}/index.md)", root.Body);
    }

    // ── Findings ─────────────────────────────────────────────────────────

    [Fact]
    public void UnmatchedType_IsReported_AndGetsNoPage()
    {
        var r = Generate();
        Assert.Equal(new[] { "T:Microsoft.UI.Reactor.Experimental.Probe" }, r.Uncategorized);
        Assert.Contains(r.Findings, f => f.Code == "REACTOR_DOC_REFGEN_004" && f.Message.Contains("Experimental.Probe", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Pages, p => p.Body.Contains("Probe", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingSummary_IsAWarning()
    {
        var findings = Generate().Findings.Where(f => f.Code == "REACTOR_DOC_REFGEN_W001").ToList();
        Assert.All(findings, f => Assert.Equal(TierLintSeverity.Warning, f.Severity));
        Assert.Equal(
            new[] { "F:Microsoft.UI.Reactor.Hosting.Backdrop.Acrylic", "F:Microsoft.UI.Reactor.Hosting.Backdrop.Mica", "T:Microsoft.UI.Reactor.Hosting.ReactorApp" },
            findings.Select(f => f.Message[(f.Message.LastIndexOf(' ') + 1)..]).OrderBy(m => m, StringComparer.Ordinal));
    }

    [Fact]
    public void MissingXmlFile_IsOneError_AndNoPages()
    {
        var r = new ReferenceGenerator().Generate(Path.Combine(AppContext.BaseDirectory, "no-such-file.xml"), Map());
        Assert.Empty(r.Pages);
        var finding = Assert.Single(r.Findings);
        Assert.Equal("REACTOR_DOC_REFGEN_003", finding.Code);
        Assert.Equal(TierLintSeverity.Error, finding.Severity);
    }

    // ── Determinism ──────────────────────────────────────────────────────

    /// <summary>Two runs, and a run over the same surface declared in the opposite
    /// order, give byte-identical pages, links and nav fragment.</summary>
    [Fact]
    public void Output_IsDeterministic()
    {
        static string Snapshot(ReferenceGenResult r) =>
            string.Join("\n\u0001\n", r.Pages.Select(p => p.RelativePath + "\n" + p.Title + "\n" + p.Summary + "\n" + p.Body))
            + "\n\u0002\n" + string.Join("\n", r.LinksByDocId.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + " " + k.Value))
            + "\n\u0002\n" + string.Join("\n", r.LinksByName.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + " " + k.Value))
            + "\n\u0002\n" + r.NavFragment
            + "\n\u0002\n" + string.Join("\n", r.Findings.Select(f => f.Format()));

        var first = Snapshot(Generate());
        Assert.Equal(first, Snapshot(Generate()));
        Assert.Equal(first, Snapshot(Generate(reverse: true)));
    }

    // ── Writing to disk ──────────────────────────────────────────────────

    /// <summary>
    /// The generator owns <c>reference/</c>: it deletes Markdown it did not produce
    /// (pages from the old layout) and folders left empty, fixes the case of a
    /// renamed page, keeps files marked as hand-written, leaves other files alone,
    /// and does not rewrite a page that has not changed.
    /// </summary>
    [Fact]
    public void WriteToDisk_OwnsTheReferenceFolder()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "refgen-tmp-" + Guid.NewGuid().ToString("N"));
        try
        {
            string Ref(string relative) => Path.Combine(root, "reference", relative.Replace('/', Path.DirectorySeparatorChar));
            void Put(string relative, string text)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Ref(relative))!);
                File.WriteAllText(Ref(relative), text);
            }
            Put("hooks/Count.md", "# Count\n");
            Put("hooks/Clear.md", "# Clear\n");
            Put("old/deep/Gone.md", "# Gone\n");
            Put("hooks/Notes.md", "# Notes\n\n" + ReferenceGenerator.KeepMarker + "\n");
            Put("hosting/reactorapp.md", "# old casing\n");
            Put("hooks/diagram.png", "not markdown");

            var r = Generate();
            var (written, deleted) = ReferenceGenerator.WriteToDisk(r, root);

            Assert.Equal(r.Pages.Count, written);
            Assert.Equal(3, deleted);
            Assert.False(File.Exists(Ref("hooks/Count.md")));
            Assert.False(File.Exists(Ref("hooks/Clear.md")));
            Assert.False(Directory.Exists(Path.Combine(root, "reference", "old")));
            Assert.True(File.Exists(Ref("hooks/Notes.md")));
            Assert.True(File.Exists(Ref("hooks/diagram.png")));
            Assert.Contains("ReactorApp.md", Directory.GetFiles(Path.Combine(root, "reference", "hosting")).Select(Path.GetFileName));
            foreach (var page in r.Pages)
                Assert.Equal(CompileCommand.NormalizeLineEndings(page.Body), File.ReadAllText(Path.Combine(root, page.RelativePath)));

            Assert.Equal((0, 0), ReferenceGenerator.WriteToDisk(r, root));
        }
        finally
        {
            FixtureCleanup.DeleteTree(root);
        }
    }

    /// <summary>A page marked as hand-written keeps its text even where the generator
    /// plans a page of the same name.</summary>
    [Fact]
    public void WriteToDisk_NeverOverwritesAKeptPage()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "refgen-tmp-" + Guid.NewGuid().ToString("N"));
        try
        {
            var index = Path.Combine(root, "reference", "hooks", "index.md");
            Directory.CreateDirectory(Path.GetDirectoryName(index)!);
            var handWritten = "# Hooks\n\n" + ReferenceGenerator.KeepMarker + "\n\nWritten by hand.\n";
            File.WriteAllText(index, handWritten);

            var r = Generate();
            var (written, _) = ReferenceGenerator.WriteToDisk(r, root);
            Assert.Equal(r.Pages.Count - 1, written);
            Assert.Equal(handWritten, File.ReadAllText(index));
        }
        finally
        {
            FixtureCleanup.DeleteTree(root);
        }
    }

    // ── Sidebar fragment ─────────────────────────────────────────────────

    /// <summary>
    /// The nav fragment is valid JSON in the <c>nav.json</c> group shape: the root
    /// overview first, one numbered folder per category with its overview first,
    /// unique folders, and one catch-all glob per category on its last group, so
    /// every generated page is placed.
    /// </summary>
    [Fact]
    public void NavFragment_PlacesEveryPage()
    {
        var r = Generate();
        Assert.EndsWith("\n", r.NavFragment);
        using var doc = JsonDocument.Parse(r.NavFragment);
        var groups = doc.RootElement.EnumerateArray().ToList();

        var dirs = groups.Select(g => g.GetProperty("dir").GetString()!).ToList();
        Assert.Equal(dirs.Count, dirs.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("reference", dirs[0]);
        Assert.Equal("reference/index.md", groups[0].GetProperty("pages")[0].GetProperty("file").GetString());
        Assert.All(dirs.Skip(1), d => Assert.Matches(@"^reference/\d{2}-[a-z-]+(/\d{2}-\([a-z-]+\))?$", d));

        var listed = new HashSet<string>(StringComparer.Ordinal);
        var globs = new List<string>();
        foreach (var category in groups.Skip(1).GroupBy(g => string.Join("/", g.GetProperty("dir").GetString()!.Split('/').Take(2))))
        {
            var list = category.ToList();
            var overview = list[0].GetProperty("pages")[0];
            Assert.Equal("Overview", overview.GetProperty("label").GetString());
            Assert.EndsWith("/index.md", overview.GetProperty("file").GetString());
            var withGlob = list.Where(g => g.TryGetProperty("glob", out _)).ToList();
            Assert.Equal(list[^1].GetProperty("dir").GetString(), Assert.Single(withGlob).GetProperty("dir").GetString());
            globs.Add(list[^1].GetProperty("glob").GetString()!);
        }
        foreach (var g in groups)
        {
            if (!g.TryGetProperty("pages", out var pages)) continue;
            foreach (var p in pages.EnumerateArray()) listed.Add(p.GetProperty("file").GetString()!);
        }

        var paths = r.Pages.Select(p => p.RelativePath).ToHashSet(StringComparer.Ordinal);
        Assert.All(listed, f => Assert.Contains(f, paths));
        Assert.All(r.Pages.Where(p => p.Kind != PageKind.RootIndex), p =>
            Assert.Contains($"reference/{p.RelativePath.Split('/')[1]}/*.md", globs));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    [Fact]
    public void LeadSummary_SkipsSummariesThatOnlyNameAnOverload()
    {
        Assert.Equal("Runs an effect.", ReferenceRenderer.LeadSummary(new[] { "", "Parameterless overload of the effect.", "Runs an effect." }));
        Assert.Equal("Overload with deps.", ReferenceRenderer.LeadSummary(new[] { "", "Overload with deps." }));
        Assert.Equal(string.Empty, ReferenceRenderer.LeadSummary(new[] { "", "" }));
    }

    [Theory]
    [InlineData("reference/hooks/UseState.md", "reference/hooks/UseEffect.md", null, "UseEffect.md")]
    [InlineData("reference/hooks/UseState.md", "reference/components/RenderContext.md", "rerender", "../components/RenderContext.md#rerender")]
    [InlineData("reference/hooks/UseState.md", "reference/hooks/UseState.md", "example", "#example")]
    [InlineData("reference/hooks/UseState.md", "reference/hooks/UseState.md", null, "UseState.md")]
    [InlineData("reference/index.md", "reference/hooks/index.md", null, "hooks/index.md")]
    public void RelativeHref_IsRelativeToThePage(string from, string to, string? anchor, string expected) =>
        Assert.Equal(expected, ReferenceRenderer.RelativeHref(from, to, anchor));

    /// <summary>Anchors match what the docs site computes, including code spans with
    /// generics and arrays, and the <c>_1</c> suffix for a repeated heading.</summary>
    [Fact]
    public void Slugs_MatchTheSiteHeadingIds()
    {
        Assert.Equal("useeffectaction-object", Slug.FromHeading("`UseEffect(Action, object[])`"));
        Assert.Equal("useeffectfuncaction-object", Slug.FromHeading("`UseEffect(Func<Action>, object[])`"));
        Assert.Equal("reactorappoptions", Slug.FromHeading("`ReactorApp.Options`"));
        Assert.Equal("use-from-a-class-component", Slug.FromHeading("Use from a class component"));

        var slugs = new SlugTracker();
        Assert.Equal("values", slugs.Add("Values"));
        Assert.Equal("values_1", slugs.Add("Values"));
        Assert.Equal("values_2", slugs.Add("Values"));
    }

    [Fact]
    public void FromAssembly_SkipsXamlMarkupCompilerMembersOnly()
    {
        var path = Path.Combine(AppContext.BaseDirectory, typeof(XamlGeneratedMembersFixture).Assembly.GetName().Name + ".dll");
        var model = ApiModel.FromAssembly(path);
        var names = model.TypesByDocId["T:" + typeof(XamlGeneratedMembersFixture).FullName]
            .Members.Select(m => m.Name).ToList();

        Assert.DoesNotContain(nameof(XamlGeneratedMembersFixture.Connect), names);
        Assert.Contains(nameof(XamlGeneratedMembersFixture.Authored), names);
        Assert.Contains(nameof(XamlGeneratedMembersFixture.FromOtherGenerator), names);
    }
}

/// <summary>Read from this test assembly's metadata by
/// <see cref="ReferenceGenTests.FromAssembly_SkipsXamlMarkupCompilerMembersOnly"/>.</summary>
public class XamlGeneratedMembersFixture
{
    public void Authored() { }

    [GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2606")]
    public void Connect(int connectionId, object target) { }

    [GeneratedCode("Reactor.Wrappers.Generator", "1.0.0.0")]
    public void FromOtherGenerator() { }
}
