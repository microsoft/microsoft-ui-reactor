using System.Xml.Linq;
using Microsoft.UI.Reactor.Cli.Docs;
using Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Spec 041 §7.1.2 — a reference page is keyed by short name, so every
/// overload of a method lands on the same page. These tests pin the two
/// behaviours that used to be broken:
///
/// <list type="bullet">
/// <item>Overloads beyond the first were dropped from the docset entirely
///   (<c>REACTOR_DOC_REFGEN_002</c>) — the page must now carry all of
///   them.</item>
/// <item>A cref to a member with no XML-doc entry of its own — a positional
///   record's compiler-generated properties, most visibly — degraded to
///   inline code even when its declaring type had a page
///   (<c>REACTOR_DOC_REFGEN_001</c>).</item>
/// </list>
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

    private static ReferenceGenResult Generate(string xmlPath) =>
        new ReferenceGenerator().Generate(
            xmlPath, HooksMap(), referenceRoot: "/tmp/docs/guide",
            categoryAllowList: new HashSet<string>() { "hooks" });

    private static ReferenceGenResult GenerateFixture() => Generate(FixturePath);

    private static GeneratedPage Page(ReferenceGenResult r, string shortName) =>
        r.Pages.Single(p => p.Route.ShortName == shortName);

    /// <summary>
    /// The eight <c>UseEffect</c> overloads the framework declares.
    /// </summary>
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

    // ── Bug 1: overloads must not be silently discarded ───────────────────

    /// <summary>
    /// The regression oracle. Routing by short name used to let the first
    /// overload win the page and drop the other seven — including the
    /// <c>Func&lt;Action&gt;</c> cleanup form, which is the only way to
    /// register an effect that tears itself down. Every overload's cref and
    /// its own summary prose must appear on the page.
    /// </summary>
    [Fact]
    public void EveryOverload_IsRenderedOnTheSharedPage()
    {
        var page = Page(GenerateFixture(), "UseEffect");

        foreach (var cref in UseEffectCrefs)
            Assert.Contains(cref, page.Body, StringComparison.Ordinal);

        // Prose unique to a non-first overload — a cref alone could in
        // principle be emitted by an index without the body being there.
        Assert.Contains("Like UseEffect but the effect returns a cleanup function.", page.Body, StringComparison.Ordinal);
        Assert.Contains("Three-dependency cleanup-flavor overload.", page.Body, StringComparison.Ordinal);

        Assert.Equal(UseEffectCrefs.Length, page.Members.Count);
    }

    /// <summary>
    /// Each overload gets its own <c>##</c> section carrying a readable
    /// signature, and the page opens with an index linking to each anchor.
    /// </summary>
    [Fact]
    public void EachOverload_GetsItsOwnSectionAndAnchor()
    {
        var page = Page(GenerateFixture(), "UseEffect");

        Assert.Contains("## Overloads", page.Body, StringComparison.Ordinal);
        // Non-generic params flavour, and a typed-dependency flavour whose
        // documented <typeparam> name is used in preference to a placeholder.
        Assert.Contains("## `UseEffect(Action, object[])`", page.Body, StringComparison.Ordinal);
        Assert.Contains("## `UseEffect<TDep>(Action, TDep)`", page.Body, StringComparison.Ordinal);
        Assert.Contains("## `UseEffect<T1, T2, T3>(Func<Action>, T1, T2, T3)`", page.Body, StringComparison.Ordinal);

        // Every index entry points at a heading that exists on the page.
        var anchors = System.Text.RegularExpressions.Regex
            .Matches(page.Body, @"^- \[`(?<sig>[^`]+)`\]\(#(?<anchor>[a-z0-9\-_]+)\)\r?$",
                System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.Equal(UseEffectCrefs.Length, anchors.Count);
        foreach (System.Text.RegularExpressions.Match m in anchors)
        {
            var heading = $"## `{m.Groups["sig"].Value}`";
            Assert.Contains(heading, page.Body, StringComparison.Ordinal);
            Assert.Equal(m.Groups["anchor"].Value, CrefSignature.Anchor(m.Groups["sig"].Value));
        }
    }

    /// <summary>
    /// Per-overload docs (parameters, returns) survive the merge instead of
    /// being flattened onto the first member.
    /// </summary>
    [Fact]
    public void OverloadSections_KeepTheirOwnParametersAndReturns()
    {
        var page = Page(GenerateFixture(), "UseEffect");

        // Only the Func<Action> params overload documents parameters.
        Assert.Contains("- **effect** — Factory returning the cleanup action.", page.Body, StringComparison.Ordinal);
        Assert.Contains("### Returns", page.Body, StringComparison.Ordinal);
        // Sub-sections nest under the overload heading, not at page level.
        Assert.DoesNotContain("\n## Returns", page.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Overloads of one method are the normal case, not a collision.
    /// </summary>
    [Fact]
    public void Overloads_DoNotEmit_REFGEN_002()
    {
        var findings = GenerateFixture().Findings
            .Where(f => f.Code == "REACTOR_DOC_REFGEN_002")
            .ToList();

        Assert.DoesNotContain(findings, f => f.Message.Contains("UseEffect", StringComparison.Ordinal));
    }

    /// <summary>
    /// A short name claimed by two unrelated declaring types is still
    /// ambiguous and still reported — but both members are rendered, because
    /// dropping one is the content loss this work removed.
    /// </summary>
    [Fact]
    public void UnrelatedDeclaringTypes_StillWarn_ButBothAreRendered()
    {
        var result = GenerateFixture();
        var page = Page(result, "Register");

        Assert.Contains(result.Findings, f => f.Code == "REACTOR_DOC_REFGEN_002"
            && f.Message.Contains("Register", StringComparison.Ordinal));
        Assert.Contains("Registers a pending token.", page.Body, StringComparison.Ordinal);
        Assert.Contains("Registers a focusable field.", page.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A cref naming any overload — not just the one that used to win the
    /// page — is routed, and links to it carry that overload's anchor.
    /// </summary>
    [Fact]
    public void CrefToAnyOverload_ResolvesToItsAnchor()
    {
        var result = GenerateFixture();
        var routed = result.Pages
            .SelectMany(p => p.Members.Select(m => m.Cref))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var cref in UseEffectCrefs)
            Assert.Contains(cref, routed);

        // Anchors are distinct per overload, so a link can address the
        // cleanup flavour specifically rather than the page as a whole.
        var page = Page(result, "UseEffect");
        Assert.Contains("#useeffectfuncaction-object", page.Body, StringComparison.Ordinal);
        Assert.Contains("#useeffectaction-object", page.Body, StringComparison.Ordinal);

        var slugs = System.Text.RegularExpressions.Regex
            .Matches(page.Body, @"\(#(?<anchor>[a-z0-9\-_]+)\)")
            .Select(m => m.Groups["anchor"].Value)
            .ToList();
        Assert.Equal(slugs.Count, slugs.Distinct(StringComparer.Ordinal).Count());
    }

    // ── Determinism ───────────────────────────────────────────────────────

    /// <summary>
    /// Render order is derived from the cref, never from the order members
    /// happen to appear in the XML doc file — otherwise a source-file
    /// reshuffle would churn the whole reference tree.
    /// </summary>
    [Fact]
    public void OverloadOrder_IsIndependentOfXmlDeclarationOrder()
    {
        var original = GenerateFixture();

        var doc = XDocument.Load(FixturePath);
        var membersEl = doc.Root!.Element("members")!;
        var reversed = membersEl.Elements("member").Reverse().ToList();
        membersEl.RemoveNodes();
        foreach (var m in reversed) membersEl.Add(m);

        var tmp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".xml");
        doc.Save(tmp);
        try
        {
            var shuffled = Generate(tmp);
            Assert.Equal(original.Pages.Count, shuffled.Pages.Count);
            foreach (var page in original.Pages)
            {
                var other = Page(shuffled, page.Route.ShortName);
                Assert.Equal(page.Body, other.Body);
            }
        }
        finally { File.Delete(tmp); }
    }

    // ── Bug 2: member crefs resolve through their declaring type ──────────

    /// <summary>
    /// <c>MutationOptions&lt;TInput,TResult&gt;</c> is a positional record:
    /// its <c>OnSuccess</c> / <c>InvalidateKeys</c> properties are
    /// compiler-generated and never appear as <c>&lt;member&gt;</c> entries.
    /// A cref to one must land on the declaring type's page rather than
    /// degrade to inline code.
    /// </summary>
    [Fact]
    public void PositionalRecordProperty_ResolvesToDeclaringTypePage()
    {
        var result = GenerateFixture();
        var page = Page(result, "UseMutation");

        Assert.Contains("[OnSuccess](MutationOptions.md)", page.Body, StringComparison.Ordinal);
        Assert.Contains("[InvalidateKeys](MutationOptions.md)", page.Body, StringComparison.Ordinal);
        // and no longer degraded to inline code
        Assert.DoesNotContain("`OnSuccess`", page.Body, StringComparison.Ordinal);

        Assert.DoesNotContain(result.Findings, f => f.Code == "REACTOR_DOC_REFGEN_001"
            && f.Message.Contains("MutationOptions", StringComparison.Ordinal));
    }

    /// <summary>
    /// Negative control, shape-matched to the subject: the declaring-type
    /// fallback must not become a blanket "resolve anything".
    ///
    /// The control has to be a <em>member</em> cref whose declaring type has
    /// no page — a type cref would never reach the member-fallback branch,
    /// so it could not vouch for the branch staying narrow.
    /// <c>Microsoft.UI.Reactor.Input.FocusManager</c> is outside the gated
    /// hooks category, and it deliberately shares a short name with
    /// <c>Microsoft.UI.Reactor.Hooks.FocusManager</c>, which does have a
    /// page: resolving by short name instead of by full declaring-type cref
    /// would send the reader to a same-named type in another namespace.
    /// </summary>
    [Fact]
    public void MemberOfUnroutedType_StillDegradesAndWarns()
    {
        var result = GenerateFixture();
        var page = Page(result, "UseMutation");

        // A member of an unrouted type. The fallback keeps the declaring type
        // so the sentence still says *which* Focus — an unqualified `Focus`
        // produced sentences like "the ambient QueryCache from QueryCache".
        Assert.Contains("`FocusManager.Focus`", page.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("[Focus](", page.Body, StringComparison.Ordinal);
        Assert.Contains(result.Findings, f => f.Code == "REACTOR_DOC_REFGEN_001"
            && f.Message.Contains("M:Microsoft.UI.Reactor.Input.FocusManager.Focus", StringComparison.Ordinal));

        // And a plain type cref outside the category — types stay unqualified,
        // since the namespace adds noise without disambiguating.
        Assert.Contains("`OperationCanceledException`", page.Body, StringComparison.Ordinal);
        Assert.Contains(result.Findings, f => f.Code == "REACTOR_DOC_REFGEN_001"
            && f.Message.Contains("T:System.OperationCanceledException", StringComparison.Ordinal));
    }

    /// <summary>
    /// The fallback keys off the full declaring-type cref, so a member of
    /// <c>Input.FocusManager</c> must not be captured by the page generated
    /// for the unrelated <c>Hooks.FocusManager</c>.
    /// </summary>
    [Fact]
    public void DeclaringTypeFallback_DoesNotMatchASameNamedTypeInAnotherNamespace()
    {
        var result = GenerateFixture();
        // Hooks.FocusManager.Register is routed, so the short name has a page.
        Assert.Contains(result.Pages, p => p.Route.ShortName == "Register");

        Assert.Equal(
            "T:Microsoft.UI.Reactor.Input.FocusManager",
            CrefSignature.DeclaringTypeCref(
                "M:Microsoft.UI.Reactor.Input.FocusManager.Focus(Microsoft.UI.Reactor.Input.ElementRef,Microsoft.UI.Xaml.FocusState)"));
        Assert.NotEqual(
            "T:Microsoft.UI.Reactor.Hooks.FocusManager",
            CrefSignature.DeclaringTypeCref(
                "M:Microsoft.UI.Reactor.Input.FocusManager.Focus(Microsoft.UI.Reactor.Input.ElementRef,Microsoft.UI.Xaml.FocusState)"));
    }

    [Fact]
    public void DeclaringTypeFallback_IsNotAppliedToTypeCrefs()
    {
        // A T: cref has no declaring *type*; resolving `T:Ns.Missing` must
        // not accidentally match a page for the namespace.
        Assert.Null(CrefSignature.DeclaringTypeCref("T:Microsoft.UI.Reactor.Hooks.MutationOptions`2"));
        Assert.Equal(
            "T:Microsoft.UI.Reactor.Hooks.MutationOptions`2",
            CrefSignature.DeclaringTypeCref("P:Microsoft.UI.Reactor.Hooks.MutationOptions`2.OnSuccess"));
        Assert.Equal(
            "T:Microsoft.UI.Reactor.Core.RenderContext",
            CrefSignature.DeclaringTypeCref("M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``2(System.Action,``0,``1)"));
    }

    /// <summary>
    /// XML-doc markup must not reach the page. Roslyn entity-escapes doc text,
    /// and a Markdown code span is literal, so wrapping the serialized text
    /// without decoding published <c>UseElementRef&amp;lt;Button&amp;gt;()</c>.
    /// The overload and link assertions elsewhere stay green through that
    /// defect, so it needs its own gate.
    /// </summary>
    [Fact]
    public void XmlDocFormatting_IsRewrittenToMarkdown()
    {
        var page = Page(GenerateFixture(), "Probe");

        // Inline <c> and <paramref> become code spans, with entities decoded.
        Assert.Contains("`UseNavigation<TRoute>()`", page.Body, StringComparison.Ordinal);
        Assert.Contains("`() => items.ToArray()`", page.Body, StringComparison.Ordinal);
        Assert.Contains("`factory`", page.Body, StringComparison.Ordinal);
        Assert.Contains("**bold**", page.Body, StringComparison.Ordinal);

        // Block <code> becomes a fenced block, also decoded.
        Assert.Contains("```csharp", page.Body, StringComparison.Ordinal);
        Assert.Contains("ctx.UseElementRef<Button>()", page.Body, StringComparison.Ordinal);
        Assert.Contains("Array.Empty<object>()", page.Body, StringComparison.Ordinal);

        // <para>/<list> become real Markdown. Left as raw HTML blocks,
        // CommonMark would stop parsing and emit the inline rewrites literally.
        Assert.Contains("A wrapped paragraph with a `code span` and **bold**.", page.Body, StringComparison.Ordinal);
        Assert.Contains("- First item with `inline code`.", page.Body, StringComparison.Ordinal);
        Assert.Contains("- Second item.", page.Body, StringComparison.Ordinal);

        // No raw markup or entities survive anywhere on the page.
        Assert.DoesNotContain("<c>", page.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("<code>", page.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", page.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("<paramref", page.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("<para>", page.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("<list", page.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("<item>", page.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("&lt;", page.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("&gt;", page.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// An &lt;example&gt; without a &lt;code&gt; wrapper would otherwise be
    /// emitted as bare lines and collapsed into one prose paragraph, leaving the
    /// sample unreadable and uncopyable.
    /// </summary>
    [Fact]
    public void UnwrappedExample_IsStillFenced()
    {
        var page = Page(GenerateFixture(), "Probe");
        var examples = page.Body[page.Body.IndexOf("Examples", StringComparison.Ordinal)..];

        Assert.Contains("```csharp", examples, StringComparison.Ordinal);
        Assert.Contains("var (value, setValue) = ctx.UseState(0);", examples, StringComparison.Ordinal);
    }
}
