using System.Xml.Linq;
using Microsoft.UI.Reactor.Cli.Docs;
using Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Spec 041 §10.4 — the reference generator run on XML doc comments alone (no
/// assembly), with the real shapes that used to break it: eight
/// <c>UseEffect</c> overloads, a positional record whose properties have no
/// <c>&lt;member&gt;</c> entry, and the same member name on two unrelated types.
/// </summary>
public class ReferenceGenOverloadTests
{
    /// <summary>Mirrors the shipping <c>docs/_pipeline/reference-map.yaml</c> rules
    /// that put hooks (including <c>RenderContext.Use*</c>) in the hooks category.</summary>
    private static ReferenceMap HooksMap() => ReferenceMap.Parse("""
defaults:
  - match: "Microsoft.UI.Reactor.Core.RenderContext.Use*"
    category: hooks
    guide-pages: [hooks, effects]
  - match: "Microsoft.UI.Reactor.Hooks.*"
    category: hooks
    guide-pages: [hooks, effects]
""");

    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "refgen", "overloads.xml");

    private static ReferenceGenResult Generate(IReadOnlyList<MemberDoc> docs) =>
        new ReferenceGenerator().Generate(docs, HooksMap());

    private static ReferenceGenResult GenerateFixture() => Generate(XmlDocReader.Read(FixturePath));

    private static ReferencePage Page(ReferenceGenResult r, string relativePath) =>
        r.Pages.Single(p => p.RelativePath == relativePath);

    private const string UseEffectPage = "reference/hooks/UseEffect.md";

    /// <summary>The eight <c>UseEffect</c> overloads the framework declares.</summary>
    private static readonly string[] UseEffectCrefs =
    {
        "M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect(System.Action,System.Object[])",
        "M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect(System.Func{System.Action},System.Object[])",
        "M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``1(System.Action,``0)",
        "M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``1(System.Func{System.Action},``0)",
        "M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``2(System.Action,``0,``1)",
        "M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``2(System.Func{System.Action},``0,``1)",
        "M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``3(System.Action,``0,``1,``2)",
        "M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``3(System.Func{System.Action},``0,``1,``2)",
    };

    /// <summary>The heading lines of a page outside code fences, in document order.</summary>
    internal static List<string> Headings(string body)
    {
        var result = new List<string>();
        var inFence = false;
        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal)) { inFence = !inFence; continue; }
            if (!inFence && line.StartsWith('#')) result.Add(line);
        }
        return result;
    }

    /// <summary>The ids the site gives each heading on the page, replayed in order.</summary>
    internal static HashSet<string> HeadingIds(string body)
    {
        var slugs = new SlugTracker();
        return Headings(body).Select(h => slugs.Add(h.TrimStart('#').Trim())).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Every overload, including the <c>Func&lt;Action&gt;</c> cleanup form, lands
    /// on the one hook page with its own summary.
    /// </summary>
    [Fact]
    public void EveryOverload_IsRenderedOnTheHookPage()
    {
        var result = GenerateFixture();
        var page = Page(result, UseEffectPage);
        Assert.Equal(PageKind.Hook, page.Kind);

        foreach (var summary in new[]
        {
            "Runs a side effect after render.",
            "Like UseEffect but the effect returns a cleanup function.",
            "Single-dependency overload.",
            "Single-dependency cleanup-flavor overload.",
            "Two-dependency overload.",
            "Two-dependency cleanup-flavor overload.",
            "Three-dependency overload.",
            "Three-dependency cleanup-flavor overload.",
        })
        {
            Assert.Contains(summary, page.Body, StringComparison.Ordinal);
        }

        // No page per overload.
        Assert.Single(result.Pages, p => p.RelativePath.Contains("UseEffect", StringComparison.Ordinal));
    }

    /// <summary>
    /// The overload table links each row to a section heading, and each link's
    /// anchor is the id the site will give that heading.
    /// </summary>
    [Fact]
    public void OverloadTable_LinksEachRowToItsSectionAnchor()
    {
        var page = Page(GenerateFixture(), UseEffectPage);
        var ids = HeadingIds(page.Body);

        var rows = page.Body.Replace("\r\n", "\n").Split('\n')
            .Where(l => l.StartsWith("| [`UseEffect", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(8, rows.Count);

        var anchors = rows.Select(r =>
        {
            var start = r.IndexOf("](#", StringComparison.Ordinal) + 3;
            return r[start..r.IndexOf(')', start)];
        }).ToList();
        Assert.Equal(8, anchors.Distinct(StringComparer.Ordinal).Count());
        Assert.All(anchors, a => Assert.Contains(a, ids));
    }

    [Fact]
    public void OverloadSections_KeepTheirOwnParametersAndReturns()
    {
        var body = Page(GenerateFixture(), UseEffectPage).Body.Replace("\r\n", "\n");
        var section = body[body.IndexOf("### `UseEffect(Func<Action>, object[])`", StringComparison.Ordinal)..];
        section = section[..section.IndexOf("\n### ", 1, StringComparison.Ordinal)];

        Assert.Contains("| `effect` | `Func<Action>` | Factory returning the cleanup action. |", section, StringComparison.Ordinal);
        Assert.Contains("| `deps` | `object[]` | Dependency list. |", section, StringComparison.Ordinal);
        Assert.Contains("**Returns:** Nothing.", section, StringComparison.Ordinal);

        // The return text belongs to that overload, not to the page as a whole.
        Assert.DoesNotContain("\n## Returns", body, StringComparison.Ordinal);
    }

    [Fact]
    public void CrefToAnyOverload_ResolvesToItsAnchor()
    {
        var result = GenerateFixture();
        var ids = HeadingIds(Page(result, UseEffectPage).Body);
        var anchors = new HashSet<string>(StringComparer.Ordinal);

        foreach (var cref in UseEffectCrefs)
        {
            Assert.True(result.LinksByDocId.TryGetValue(cref, out var loc), cref);
            Assert.Equal(UseEffectPage, loc!.RelativePath);
            Assert.False(string.IsNullOrEmpty(loc.Anchor), cref);
            Assert.Contains(loc.Anchor!, ids);
            anchors.Add(loc.Anchor!);
        }
        Assert.Equal(UseEffectCrefs.Length, anchors.Count);
    }

    /// <summary>
    /// The fixture declares the overloads in a shuffled order. Reversing the file
    /// must not change a single byte of any page.
    /// </summary>
    [Fact]
    public void Output_IsIndependentOfXmlDeclarationOrder()
    {
        var doc = XDocument.Load(FixturePath);
        var members = doc.Root!.Element("members")!;
        var reversed = members.Elements("member").Reverse().ToList();
        members.RemoveNodes();
        members.Add(reversed);

        var a = GenerateFixture();
        var b = Generate(XmlDocReader.ReadString(doc.ToString()));

        Assert.Equal(a.Pages.Select(p => p.RelativePath), b.Pages.Select(p => p.RelativePath));
        foreach (var page in a.Pages)
            Assert.Equal(page.Body, Page(b, page.RelativePath).Body);
        Assert.Equal(a.NavFragment, b.NavFragment);
    }

    /// <summary>
    /// Roslyn emits no <c>&lt;member&gt;</c> for a positional record's properties,
    /// so a cref to one resolves to its declaring type's page.
    /// </summary>
    [Fact]
    public void PositionalRecordProperty_ResolvesToDeclaringTypePage()
    {
        var result = GenerateFixture();
        var body = Page(result, "reference/hooks/UseMutation.md").Body;

        Assert.Contains("[`MutationOptions.OnSuccess`](MutationOptions.md)", body, StringComparison.Ordinal);
        Assert.Contains("[`MutationOptions.InvalidateKeys`](MutationOptions.md)", body, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Findings, f => f.Code == "REACTOR_DOC_REFGEN_001"
            && f.Message.Contains("MutationOptions", StringComparison.Ordinal));
    }

    /// <summary>
    /// A cref to a Reactor type with no page becomes plain code and a warning,
    /// never a broken link. A .NET type links to Microsoft Learn instead.
    /// </summary>
    [Fact]
    public void UnknownReactorCref_IsCodeText_AndDotNetCref_LinksToLearn()
    {
        var result = GenerateFixture();
        var body = Page(result, "reference/hooks/UseMutation.md").Body;

        Assert.Contains("restored via `FocusManager.Focus`.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("[`FocusManager.Focus`]", body, StringComparison.Ordinal);
        Assert.Contains(result.Findings, f => f.Code == "REACTOR_DOC_REFGEN_001"
            && f.Message.Contains("M:Microsoft.UI.Reactor.Input.FocusManager.Focus(", StringComparison.Ordinal)
            && f.Message.Contains("does not resolve", StringComparison.Ordinal));

        Assert.Contains(
            "[`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception)",
            body, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Findings, f => f.Message.Contains("OperationCanceledException", StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>Input.FocusManager</c> (unknown) and <c>Hooks.FocusManager</c> share a
    /// simple name. The unknown one must not be captured by the other's page.
    /// </summary>
    [Fact]
    public void DeclaringTypeFallback_DoesNotMatchASameNamedTypeInAnotherNamespace()
    {
        var result = GenerateFixture();
        Assert.Contains(result.Pages, p => p.RelativePath == "reference/hooks/FocusManager.md");
        Assert.Contains(result.Pages, p => p.RelativePath == "reference/hooks/PendingScope.md");
        Assert.DoesNotContain("FocusManager.md#", Page(result, "reference/hooks/UseMutation.md").Body, StringComparison.Ordinal);

        Assert.Equal(
            "T:Microsoft.UI.Reactor.Input.FocusManager",
            CrefSignature.DeclaringTypeCref(
                "M:Microsoft.UI.Reactor.Input.FocusManager.Focus(Microsoft.UI.Reactor.Input.ElementRef,Microsoft.UI.Xaml.FocusState)"));
    }

    [Fact]
    public void DeclaringTypeFallback_IsNotAppliedToTypeCrefs()
    {
        Assert.Null(CrefSignature.DeclaringTypeCref("T:Microsoft.UI.Reactor.Hooks.MutationOptions`2"));
        Assert.Equal(
            "T:Microsoft.UI.Reactor.Hooks.MutationOptions`2",
            CrefSignature.DeclaringTypeCref("P:Microsoft.UI.Reactor.Hooks.MutationOptions`2.OnSuccess"));
        Assert.Equal(
            "T:Microsoft.UI.Reactor.Core.RenderContext",
            CrefSignature.DeclaringTypeCref("M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``2(System.Action,``0,``1)"));
    }

    /// <summary>
    /// XML-doc markup must not reach the page. Roslyn entity-escapes doc text, and a
    /// Markdown code span is literal, so text wrapped without decoding would publish
    /// <c>UseElementRef&amp;lt;Button&amp;gt;()</c>.
    /// </summary>
    [Fact]
    public void XmlDocFormatting_IsRewrittenToMarkdown()
    {
        var body = Page(GenerateFixture(), "reference/hooks/FormattingProbe.md").Body;

        Assert.Contains("`UseNavigation<TRoute>()`", body, StringComparison.Ordinal);
        Assert.Contains("`() => items.ToArray()`", body, StringComparison.Ordinal);
        Assert.Contains("`factory`", body, StringComparison.Ordinal);
        Assert.Contains("**bold**", body, StringComparison.Ordinal);

        Assert.Contains("```csharp", body, StringComparison.Ordinal);
        Assert.Contains("ctx.UseElementRef<Button>()", body, StringComparison.Ordinal);
        Assert.Contains("Array.Empty<object>()", body, StringComparison.Ordinal);

        Assert.Contains("A wrapped paragraph with a `code span` and **bold**.", body, StringComparison.Ordinal);
        Assert.Contains("1. First item with `inline code`.", body, StringComparison.Ordinal);
        Assert.Contains("2. Second item.", body, StringComparison.Ordinal);

        foreach (var raw in new[] { "<c>", "<code>", "<b>", "<paramref", "<para>", "<list", "<item>", "&lt;", "&gt;" })
            Assert.DoesNotContain(raw, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// An &lt;example&gt; without a &lt;code&gt; wrapper would otherwise collapse
    /// into one prose paragraph.
    /// </summary>
    [Fact]
    public void UnwrappedExample_IsStillFenced()
    {
        var body = Page(GenerateFixture(), "reference/hooks/FormattingProbe.md").Body;
        var example = body[body.IndexOf("**Example**", StringComparison.Ordinal)..];

        Assert.Contains("```csharp\nvar (value, setValue) = ctx.UseState(0);", example.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Plain-text docs often mark a new paragraph with a blank line instead of
    /// <c>&lt;para&gt;</c>. The page keeps the break; a table cell stays on one line.
    /// </summary>
    [Fact]
    public void BlankLineInPlainText_StartsANewParagraph()
    {
        const string xml = """
<doc><assembly><name>P</name></assembly><members>
<member name="M:Microsoft.UI.Reactor.Hooks.ParagraphProbe.UseProbe(System.Int32)">
  <summary>
  First paragraph, wrapped
  over two lines.

  Second paragraph with <c>code</c>.
  </summary>
  <param name="count">One line.

  Still the same cell.</param>
</member>
</members></doc>
""";
        var pages = Generate(XmlDocReader.ReadString(xml)).Pages;
        var page = pages.FirstOrDefault(p => p.Body.Contains("ParagraphProbe", StringComparison.Ordinal) && p.Kind != PageKind.CategoryIndex && p.Kind != PageKind.RootIndex);
        Assert.True(page is not null, string.Join(", ", pages.Select(p => p.RelativePath)));
        var body = page!.Body.Replace("\r\n", "\n");

        Assert.Contains("First paragraph, wrapped over two lines.\n\nSecond paragraph with `code`.", body, StringComparison.Ordinal);
        Assert.Contains("| One line. Still the same cell. |", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A generic type documented with its <c>typeparam</c> tags in reverse order
    /// must give the same headings. XML keeps authoring order and a cref carries
    /// no ordinals, so any difference means authoring order leaks into member
    /// signatures and anchors. The two overloads matter: without member sections
    /// there are no signature headings to compare.
    /// </summary>
    [Fact]
    public void TypeParamTagOrder_DoesNotChangeMemberSignatures()
    {
        const string body = """
<member name="M:Microsoft.UI.Reactor.Hooks.Pair`2.Swap(`0,`1)">
  <summary>Swap.</summary>
</member>
<member name="M:Microsoft.UI.Reactor.Hooks.Pair`2.Swap(`1,`0)">
  <summary>Swap the other way.</summary>
</member>
</members></doc>
""";
        const string head = """
<doc><assembly><name>P</name></assembly><members>
<member name="T:Microsoft.UI.Reactor.Hooks.Pair`2">
  <summary>A pair.</summary>
""";

        var inOrder = HeadingsFor(head +
            "  <typeparam name=\"TKey\">k</typeparam>\n  <typeparam name=\"TValue\">v</typeparam>\n</member>\n" + body);
        var reversed = HeadingsFor(head +
            "  <typeparam name=\"TValue\">v</typeparam>\n  <typeparam name=\"TKey\">k</typeparam>\n</member>\n" + body);

        Assert.Contains(inOrder, h => h.StartsWith("#### `Swap(", StringComparison.Ordinal));
        Assert.Equal(inOrder, reversed);
        Assert.DoesNotContain(inOrder, h => h.Contains("TKey", StringComparison.Ordinal));
    }

    private static List<string> HeadingsFor(string xml) =>
        Generate(XmlDocReader.ReadString(xml)).Pages
            .SelectMany(p => Headings(p.Body))
            .OrderBy(h => h, StringComparer.Ordinal)
            .ToList();
}
