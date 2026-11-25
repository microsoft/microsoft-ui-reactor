using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;

/// <summary>
/// Renders a <see cref="ReferencePlan"/> into Markdown pages.
/// <para>
/// Rendering runs twice. The first pass writes every page with links turned
/// off and records where each type and member ends up (page and heading
/// anchor). The second pass renders the final text, so a cref can link to a
/// section on a page that comes later in the plan. Headings never contain
/// links, so both passes produce the same anchors.
/// </para>
/// </summary>
internal sealed class ReferenceRenderer
{
    private const string FactoriesKindSentence =
        "Each factory method returns an element record. Call them through `using static Microsoft.UI.Reactor.Factories;`.";
    private const string ModifiersKindSentence =
        "Modifiers are extension methods that return a changed copy of the element, so you can chain them.";
    private const string ElementsKindSentence =
        "Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.";
    internal const string RootIntro =
        "The API reference lists every public type, hook, factory method and modifier in Reactor. It is generated from the XML doc comments in the source code.";

    private static readonly Regex MarkdownLink = new(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex Arity = new(@"`+\d+", RegexOptions.Compiled);
    private static readonly Regex Literal = new(@"""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private static readonly ApiMemberKind[] KindOrder =
    {
        ApiMemberKind.Constructor, ApiMemberKind.Property, ApiMemberKind.Method,
        ApiMemberKind.Event, ApiMemberKind.Field, ApiMemberKind.Operator,
    };

    private readonly ReferencePlan _plan;
    private readonly IReadOnlyDictionary<string, MemberDoc> _docs;
    private readonly IReadOnlySet<string>? _templateIds;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<TemplateReference>>? _reverseIndex;
    private readonly Func<string, string?>? _guideTitle;

    private readonly Dictionary<string, RefLocation> _locations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RefLocation> _byDeclName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RefLocation> _typeNoArity = new(StringComparer.Ordinal);
    private Dictionary<string, RefLocation> _linksByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SortedSet<string>> _pageTemplates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EffDoc> _eff = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _missingSummary = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unresolved = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unknownGuidePages = new(StringComparer.Ordinal);
    private readonly List<RefGenFinding> _findings = new();
    private bool _final;

    public ReferenceRenderer(
        ReferencePlan plan,
        IReadOnlyDictionary<string, MemberDoc> docsByCref,
        IReadOnlySet<string>? templateIds = null,
        IReadOnlyDictionary<string, IReadOnlyList<TemplateReference>>? reverseIndex = null,
        Func<string, string?>? guideTitle = null)
    {
        _plan = plan;
        _docs = docsByCref;
        _templateIds = templateIds;
        _reverseIndex = reverseIndex;
        _guideTitle = guideTitle;
    }

    public ReferenceGenResult Render()
    {
        _final = false;
        foreach (var page in _plan.Pages) RenderPage(page);

        FillLocationGaps();
        BuildIndexes();
        MapTemplatesToPages();

        _final = true;
        var pages = new List<ReferencePage>();
        foreach (var page in _plan.Pages)
        {
            var body = RenderPage(page);
            pages.Add(new ReferencePage(page.RelativePath, page.Category?.Id, page.Title, IndexDescription(page), body, page.Kind));
        }

        foreach (var docId in _missingSummary)
        {
            var path = _locations.TryGetValue(docId, out var loc) ? "docs/guide/" + loc.RelativePath : "docs/guide/reference";
            _findings.Add(new RefGenFinding("REACTOR_DOC_REFGEN_W001", $"missing <summary> for {docId}", path, TierLintSeverity.Warning));
        }

        return new ReferenceGenResult(pages, _findings)
        {
            LinksByDocId = new Dictionary<string, RefLocation>(_locations, StringComparer.Ordinal),
            LinksByName = _linksByName,
            NavFragment = BuildNavFragment(),
            Uncategorized = _plan.Uncategorized.Select(t => t.DocId).OrderBy(s => s, StringComparer.Ordinal).ToList(),
            DeclaredGuidePages = _plan.Pages.SelectMany(p => p.GuidePages)
                .Concat(_plan.Map.Categories.SelectMany(c => c.GuidePages))
                .Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList(),
        };
    }

    // ── Page dispatch ─────────────────────────────────────────────────────

    private string RenderPage(PlannedPage page)
    {
        var w = new PageWriter(this, page);
        switch (page.Kind)
        {
            case PageKind.Type: RenderTypePage(w); break;
            case PageKind.Group: RenderGroupPage(w); break;
            case PageKind.Hook: RenderHookPage(w); break;
            case PageKind.ElementArea: RenderElementAreaPage(w); break;
            case PageKind.FactoryArea:
            case PageKind.ModifierArea: RenderMethodAreaPage(w); break;
            case PageKind.CategoryIndex: RenderCategoryIndex(w); break;
            case PageKind.RootIndex: RenderRootIndex(w); break;
        }
        return w.Build();
    }

    // ── Type and group pages ──────────────────────────────────────────────

    private void RenderTypePage(PageWriter w)
    {
        var t = w.Page.Types[0];
        w.H1(DocMarkdown.EscapeProse(t.DisplayName), new[] { t.DocId });
        w.Add(SummaryMd(t.DocId, w.Md));
        w.Add(DeclarationFence(t));
        w.Add(NamespaceLine(t, w));
        w.Add(LearnMore(w));
        w.Add(ObsoleteNote(t.ObsoleteMessage, "type"));
        RenderTypeBody(w, t, 2);
        var seeAlso = SeeAlsoList(Eff(t.DocId), w.Md);
        if (seeAlso is not null)
        {
            w.H(2, "See also");
            w.Add(seeAlso);
        }
    }

    private void RenderGroupPage(PageWriter w)
    {
        w.H1(DocMarkdown.EscapeProse(w.Page.Title));
        w.Add(w.Page.Group?.Summary);
        w.Add(LearnMore(w));
        foreach (var t in w.Page.Types.OrderBy(t => t.Name, StringComparer.Ordinal).ThenBy(t => t.DocId, StringComparer.Ordinal))
        {
            w.H(2, DocMarkdown.CodeSpan(t.DisplayName), new[] { t.DocId });
            w.Add(SummaryMd(t.DocId, w.Md));
            w.Add(DeclarationFence(t));
            w.Add(NamespaceLine(t, w));
            w.Add(ObsoleteNote(t.ObsoleteMessage, "type"));
            RenderTypeBody(w, t, 3);
            var seeAlso = SeeAlsoList(Eff(t.DocId), w.Md);
            if (seeAlso is not null)
            {
                w.Add("**See also**");
                w.Add(seeAlso);
            }
        }
    }

    private void RenderTypeBody(PageWriter w, ApiType t, int level)
    {
        var d = Eff(t.DocId);
        if (d.Remarks is not null)
        {
            w.H(level, "Remarks");
            w.Add(w.Md.Block(d.Remarks));
        }
        var typeParams = TypeParamTable(t.TypeParameters, new[] { d }, w.Md);
        if (typeParams is not null)
        {
            w.H(level, "Type parameters");
            w.Add(typeParams);
        }
        var examples = Examples(new[] { d }, w.Md);
        if (examples.Count > 0)
        {
            w.H(level, examples.Count == 1 ? "Example" : "Examples");
            foreach (var e in examples) w.Add(e);
        }

        var owned = OwnedMembers(w.Page, t);
        foreach (var kind in KindOrder)
        {
            var ofKind = owned.Where(m => m.Kind == kind).ToList();
            if (ofKind.Count == 0) continue;
            w.H(level, KindTitle(kind));
            foreach (var group in ofKind.GroupBy(m => m.Name, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var overloads = ReferencePlanner.SortOverloads(group).ToList();
                var name = kind == ApiMemberKind.Constructor ? t.Name : group.Key;
                w.H(level + 1, DocMarkdown.CodeSpan(name), overloads.Select(o => o.DocId));
                RenderOverloadSet(w, overloads, level + 2);
            }
        }

        if (t.Kind == ApiTypeKind.Enum && t.EnumValues.Count > 0)
        {
            w.H(level, "Values", t.EnumValues.Select(v => v.DocId));
            var sb = new StringBuilder("| Name | Value | Description |\n| --- | --- | --- |");
            foreach (var v in t.EnumValues)
            {
                sb.Append("\n| ").Append(DocMarkdown.CodeSpan(v.Name))
                  .Append(" | ").Append(v.ConstantValue is null ? string.Empty : DocMarkdown.CodeSpan(v.ConstantValue))
                  .Append(" | ").Append(DocMarkdown.Cell(SummaryMd(v.DocId, w.Md))).Append(" |");
            }
            w.Add(sb.ToString());
        }

        var claimed = t.Members
            .Where(m => !_plan.HiddenMembers.Contains(m.DocId)
                && _plan.PageByMemberDocId.TryGetValue(m.DocId, out var p) && p != w.Page
                && p.Kind is PageKind.Hook or PageKind.FactoryArea or PageKind.ModifierArea)
            .ToList();
        foreach (var (kind, title) in new[] { (PageKind.Hook, "Hooks"), (PageKind.FactoryArea, "Factories"), (PageKind.ModifierArea, "Modifiers") })
        {
            var list = claimed.Where(m => _plan.PageByMemberDocId[m.DocId].Kind == kind)
                .GroupBy(m => m.Name, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.OrderBy(m => m.DocId, StringComparer.Ordinal).First())
                .ToList();
            if (list.Count == 0) continue;
            w.H(level, title);
            // A hook has its own page, so link to the top of it rather than to the first overload.
            w.Add(string.Join("\n", list.Select(m => "- " + (kind == PageKind.Hook
                ? $"[{DocMarkdown.CodeSpan(m.Name)}]({RelativeHref(w.Page.RelativePath, _plan.PageByMemberDocId[m.DocId].RelativePath, null)})"
                : w.Md.Link(m.DocId)))));
        }

        if (w.Page.Nested.TryGetValue(t.DocId, out var nested))
        {
            foreach (var n in nested.OrderBy(n => n.Name, StringComparer.Ordinal).ThenBy(n => n.DocId, StringComparer.Ordinal))
            {
                w.H(level, DocMarkdown.CodeSpan(n.DisplayName), new[] { n.DocId });
                w.Add(SummaryMd(n.DocId, w.Md));
                w.Add(DeclarationFence(n));
                w.Add(ObsoleteNote(n.ObsoleteMessage, "type"));
                RenderTypeBody(w, n, level + 1);
                var seeAlso = SeeAlsoList(Eff(n.DocId), w.Md);
                if (seeAlso is not null)
                {
                    w.Add("**See also**");
                    w.Add(seeAlso);
                }
            }
        }
    }

    /// <summary>
    /// Renders the overloads that share one heading. Overloads get their own
    /// sub-headings when their summaries differ (an empty summary counts as
    /// different); otherwise they share one summary and one merged set of sections.
    /// </summary>
    private void RenderOverloadSet(PageWriter w, IReadOnlyList<ApiMember> overloads, int subLevel)
    {
        var summaries = overloads.Select(o => SummaryMd(o.DocId, w.Md)).ToList();
        if (SummariesDiffer(summaries))
        {
            for (int i = 0; i < overloads.Count; i++)
            {
                var o = overloads[i];
                w.H(subLevel, DocMarkdown.CodeSpan(o.ShortSignature), new[] { o.DocId });
                w.Add(summaries[i]);
                w.Add(Fence(new[] { Sig(o, w.Page) }));
                w.Add(ObsoleteNote(o.ObsoleteMessage, "member"));
                Details(w, new[] { o }, headings: false, level: subLevel + 1, includeSeeAlso: true);
            }
            return;
        }
        w.Add(summaries[0]);
        w.Add(Fence(overloads.Select(o => Sig(o, w.Page))));
        w.Add(ObsoleteNote(overloads.Select(o => o.ObsoleteMessage).FirstOrDefault(m => m is not null), "member"));
        Details(w, overloads, headings: false, level: subLevel, includeSeeAlso: true);
    }

    /// <summary>
    /// True when a set of overloads cannot share one summary. An overload with no
    /// summary differs from one that has one, so a documented overload's text is
    /// never shown as if it described the undocumented ones too.
    /// </summary>
    internal static bool SummariesDiffer(IReadOnlyList<string> summaries) =>
        summaries.Distinct(StringComparer.Ordinal).Skip(1).Any();

    private List<ApiMember> OwnedMembers(PlannedPage page, ApiType t)
    {
        IEnumerable<ApiMember> source = page.TypeMembers.TryGetValue(t.DocId, out var list) ? list : t.Members;
        return source
            .Where(m => !_plan.HiddenMembers.Contains(m.DocId)
                && (!_plan.PageByMemberDocId.TryGetValue(m.DocId, out var p) || p == page))
            .ToList();
    }

    private static string KindTitle(ApiMemberKind kind) => kind switch
    {
        ApiMemberKind.Constructor => "Constructors",
        ApiMemberKind.Property => "Properties",
        ApiMemberKind.Method => "Methods",
        ApiMemberKind.Event => "Events",
        ApiMemberKind.Field => "Fields",
        _ => "Operators",
    };

    // ── Hook pages ────────────────────────────────────────────────────────

    private void RenderHookPage(PageWriter w)
    {
        var page = w.Page;
        var primaries = page.Members;
        w.H1(DocMarkdown.EscapeProse(page.Title), primaries.Concat(page.Forwarders).Select(m => m.DocId));
        if (primaries.Count == 0) return;

        var summaries = primaries.Select(o => SummaryMd(o.DocId, w.Md)).ToList();
        w.Add(LeadSummary(summaries));
        w.Add(Fence(primaries.Select(o => Sig(o, page))));
        w.Add(DeclaredOnLine(primaries[0], w));
        w.Add(LearnMore(w));

        if (primaries.Count == 1)
        {
            w.Add(ObsoleteNote(primaries[0].ObsoleteMessage, "hook"));
            Details(w, primaries, headings: true, level: 2, includeSeeAlso: false);
        }
        else
        {
            w.H(2, "Overloads");
            var sb = new StringBuilder("| Overload | Description |\n| --- | --- |");
            for (int i = 0; i < primaries.Count; i++)
            {
                var o = primaries[i];
                var code = DocMarkdown.CodeSpan(o.ShortSignature);
                var cell = _final && _locations.TryGetValue(o.DocId, out var loc) && loc.RelativePath == page.RelativePath && loc.Anchor is not null
                    ? $"[{code}](#{loc.Anchor})"
                    : code;
                sb.Append("\n| ").Append(cell).Append(" | ").Append(DocMarkdown.Cell(PlainFirstSentence(summaries[i]))).Append(" |");
            }
            w.Add(sb.ToString());
            for (int i = 0; i < primaries.Count; i++)
            {
                var o = primaries[i];
                w.H(3, DocMarkdown.CodeSpan(o.ShortSignature), new[] { o.DocId });
                w.Add(summaries[i]);
                w.Add(Fence(new[] { Sig(o, page) }));
                w.Add(ObsoleteNote(o.ObsoleteMessage, "overload"));
                Details(w, new[] { o }, headings: false, level: 4, includeSeeAlso: false);
            }
        }

        if (page.Forwarders.Count > 0)
        {
            var anchor = w.H(2, "Use from a class component");
            w.Add("Class components can call the same hook directly. These overloads forward to the ones above:");
            w.Add(Fence(page.Forwarders.Select(f => Sig(f, page))));
            foreach (var f in page.Forwarders)
            {
                var key = ReferencePlanner.HookKey(f);
                var primary = primaries.FirstOrDefault(p => ReferencePlanner.HookKey(p) == key);
                var target = primary is not null && _locations.TryGetValue(primary.DocId, out var loc) && loc.RelativePath == page.RelativePath
                    ? loc.Anchor
                    : anchor;
                Record(f.DocId, page, target);
            }
        }

        var seeAlso = SeeAlsoList(primaries.Select(p => Eff(p.DocId)), w.Md);
        if (seeAlso is not null)
        {
            w.H(2, "See also");
            w.Add(seeAlso);
        }
    }

    private string DeclaredOnLine(ApiMember m, PageWriter w)
    {
        if (m.DeclaringType.DocId == ReferencePlanner.RenderContextDocId)
            return $"Declared on {w.Md.Link(m.DeclaringType.DocId)}.";
        if (m.IsExtension && m.ExtendedTypeDocIds.Count > 0)
            return $"An extension method on {string.Join(", ", m.ExtendedTypeDocIds.Select(w.Md.Link))}, declared in {DocMarkdown.CodeSpan(m.DeclaringType.Name)}.";
        return $"Declared on {w.Md.Link(m.DeclaringType.DocId)}.";
    }

    // ── Factory and modifier area pages ───────────────────────────────────

    private void RenderMethodAreaPage(PageWriter w)
    {
        var page = w.Page;
        var factories = page.Kind == PageKind.FactoryArea;
        w.H1(DocMarkdown.EscapeProse(page.Title));
        w.Add(factories ? page.Area?.Summary : ModifierAreaSummary(page));
        w.Add(factories ? FactoriesKindSentence : ModifiersKindSentence);
        w.Add(LearnMore(w));

        foreach (var group in page.Members.GroupBy(m => m.Name, StringComparer.Ordinal))
        {
            var overloads = group.ToList();
            w.H(2, DocMarkdown.CodeSpan(group.Key), overloads.Select(o => o.DocId));
            var summaries = overloads.Select(o => SummaryMd(o.DocId, w.Md)).ToList();
            var differ = SummariesDiffer(summaries);
            // Differing overloads are described one by one in the table below.
            if (!differ) w.Add(summaries[0]);
            w.Add(Fence(overloads.Select(o => Sig(o, page))));

            var declaredIn = overloads.Select(o => o.DeclaringType)
                .Where(t => t.DocId != ReferencePlanner.FactoriesDocId)
                .Select(t => t.Name).Distinct(StringComparer.Ordinal).ToList();
            if (declaredIn.Count > 0)
                w.Add($"Declared in {string.Join(", ", declaredIn.Select(DocMarkdown.CodeSpan))}.");
            w.Add(ObsoleteNote(overloads.Select(o => o.ObsoleteMessage).FirstOrDefault(m => m is not null), "method"));

            if (differ)
            {
                var sb = new StringBuilder("| Overload | Description |\n| --- | --- |");
                for (int i = 0; i < overloads.Count; i++)
                    sb.Append("\n| ").Append(DocMarkdown.CodeSpan(overloads[i].ShortSignature)).Append(" | ")
                      .Append(DocMarkdown.Cell(summaries[i])).Append(" |");
                w.Add(sb.ToString());
            }

            if (factories)
            {
                var returns = overloads.Select(o => o.ReturnDocId).Where(r => r is not null)
                    .Distinct(StringComparer.Ordinal).Select(r => w.Md.Link(r!)).ToList();
                var doc = FirstBlock(overloads.Select(o => Eff(o.DocId).Returns), w.Md);
                var text = string.Join(" ", new[] { returns.Count > 0 ? string.Join(", ", returns) + "." : null, doc }.Where(s => !string.IsNullOrEmpty(s)));
                if (text.Length > 0) w.Add(IsSimpleParagraph(text) ? $"**Returns:** {text}" : $"**Returns**\n\n{text}");
            }
            else
            {
                var applies = overloads.SelectMany(o => o.ExtendedTypeDocIds).Distinct(StringComparer.Ordinal).ToList();
                if (applies.Count > 0)
                {
                    var any = applies.Any(IsElementBase);
                    var links = applies.Where(a => !IsElementBase(a)).Select(w.Md.Link).ToList();
                    if (any) links.Insert(0, "any element");
                    w.Add($"**Applies to:** {string.Join(", ", links)}.");
                }
            }
            Details(w, overloads, headings: false, level: 3, includeSeeAlso: true, includeReturns: !factories);
        }
    }

    private bool IsElementBase(string docId) =>
        docId == "T:" + _plan.Map.ElementBase || docId == _plan.Map.ElementBase;

    // ── Element area pages ────────────────────────────────────────────────

    private void RenderElementAreaPage(PageWriter w)
    {
        var page = w.Page;
        w.H1(DocMarkdown.EscapeProse(page.Title));
        w.Add(page.Area?.Summary);
        w.Add(ElementsKindSentence);
        w.Add(LearnMore(w));

        foreach (var t in page.Types.OrderBy(t => t.Name, StringComparer.Ordinal).ThenBy(t => t.DocId, StringComparer.Ordinal))
        {
            var ids = new List<string> { t.DocId };
            ids.AddRange(t.Members.Select(m => m.DocId)
                .Where(id => !_plan.PageByMemberDocId.TryGetValue(id, out var p) || p == page));
            ids.AddRange(t.EnumValues.Select(v => v.DocId));
            w.H(2, DocMarkdown.CodeSpan(t.DisplayName), ids);
            w.Add(SummaryMd(t.DocId, w.Md));
            w.Add(DeclarationFence(t));
            w.Add(NamespaceLine(t, w));
            w.Add(ObsoleteNote(t.ObsoleteMessage, "type"));

            if (_plan.FactoriesByElement.TryGetValue(t.DocId, out var makers) && makers.Count > 0)
                w.Add("**Created by:** " + LinkNames(makers, w));
            if (_plan.ModifiersByElement.TryGetValue(t.DocId, out var mods) && mods.Count > 0)
                w.Add("**Modifiers:** " + LinkNames(mods, w));

            var owned = OwnedMembers(page, t);
            var props = owned.Where(m => m.Kind == ApiMemberKind.Property).OrderBy(m => m.Name, StringComparer.Ordinal).ToList();
            if (props.Count > 0)
            {
                var ctorParams = t.PrimaryConstructor?.Params ?? Array.Empty<ApiParam>();
                var hasDefault = props.Any(p => ctorParams.Any(c => c.Name == p.Name && c.Default is not null));
                var sb = new StringBuilder(hasDefault
                    ? "| Property | Type | Default | Description |\n| --- | --- | --- | --- |"
                    : "| Property | Type | Description |\n| --- | --- | --- |");
                foreach (var p in props)
                {
                    sb.Append("\n| ").Append(DocMarkdown.CodeSpan(p.Name)).Append(" | ").Append(TypeCell(p.ReturnType, p.ReturnDocId, page));
                    if (hasDefault)
                    {
                        var def = ctorParams.FirstOrDefault(c => c.Name == p.Name)?.Default;
                        sb.Append(" | ").Append(def is null ? string.Empty : DocMarkdown.CodeSpan(def));
                    }
                    sb.Append(" | ").Append(DocMarkdown.Cell(SummaryMd(p.DocId, w.Md))).Append(" |");
                }
                w.Add(sb.ToString());
            }
            var others = owned.Where(m => m.Kind is not ApiMemberKind.Property and not ApiMemberKind.Constructor)
                .OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.DocId, StringComparer.Ordinal).ToList();
            if (others.Count > 0)
            {
                var sb = new StringBuilder("| Member | Description |\n| --- | --- |");
                foreach (var m in others)
                    sb.Append("\n| ").Append(DocMarkdown.CodeSpan(m.ShortSignature)).Append(" | ")
                      .Append(DocMarkdown.Cell(SummaryMd(m.DocId, w.Md))).Append(" |");
                w.Add(sb.ToString());
            }
            var d = Eff(t.DocId);
            if (d.Remarks is not null)
            {
                w.Add("**Remarks**");
                w.Add(w.Md.Block(d.Remarks));
            }
            foreach (var e in Examples(new[] { d }, w.Md)) w.Add(e);

            if (page.Nested.TryGetValue(t.DocId, out var nested))
            {
                foreach (var n in nested.OrderBy(n => n.Name, StringComparer.Ordinal))
                {
                    w.H(3, DocMarkdown.CodeSpan(n.DisplayName), new[] { n.DocId });
                    w.Add(SummaryMd(n.DocId, w.Md));
                    w.Add(DeclarationFence(n));
                    RenderTypeBody(w, n, 4);
                }
            }
        }
    }

    private string LinkNames(IEnumerable<ApiMember> members, PageWriter w) =>
        string.Join(", ", members
            .GroupBy(m => m.Name, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => w.Md.Link(g.OrderBy(m => m.DocId, StringComparer.Ordinal).First().DocId)));

    // ── Index pages ───────────────────────────────────────────────────────

    private void RenderCategoryIndex(PageWriter w)
    {
        var cat = w.Page.Category!;
        w.H1(DocMarkdown.EscapeProse(cat.Title));
        w.Add(cat.Summary);
        w.Add(cat.Intro);
        w.Add(LearnMore(w));

        var pages = _plan.PagesIn(cat).Where(p => p.Kind is not PageKind.CategoryIndex and not PageKind.RootIndex).ToList();
        foreach (var section in IndexSections(cat, pages))
        {
            w.H(2, DocMarkdown.EscapeProse(section.Title));
            w.Add(section.Summary);
            w.Add(PageTable(section.Column, section.Pages, w.Page));
        }
    }

    /// <summary>One titled list of pages on a category index, and the matching sidebar group.</summary>
    internal sealed record IndexSection(string Id, string Title, string? Summary, string Column, IReadOnlyList<PlannedPage> Pages);

    /// <summary>
    /// The sections of a category index, in order. The sidebar fragment uses the same
    /// sections, so the index page and the sidebar always agree.
    /// </summary>
    private static List<IndexSection> IndexSections(RefCategory cat, IReadOnlyList<PlannedPage> pages)
    {
        var sections = new List<IndexSection>();
        if (cat.Layout == "hooks")
        {
            var remaining = pages.Where(p => p.Kind == PageKind.Hook)
                .OrderBy(p => p.Title, StringComparer.Ordinal).ToList();
            foreach (var g in cat.Groups.Where(g => g.Hooks.Count > 0))
            {
                var mine = remaining.Where(p => g.Hooks.Any(glob => ReferenceMap.PrefixMatches(p.Title, glob))).ToList();
                if (mine.Count == 0) continue;
                remaining.RemoveAll(mine.Contains);
                sections.Add(new IndexSection(g.Id, g.Title, g.Summary, "Hook", mine));
            }
            if (remaining.Count > 0) sections.Add(new IndexSection("other-hooks", "Other hooks", null, "Hook", remaining));
        }
        else if (cat.Layout is "elements" or "factories")
        {
            var areas = pages.Where(p => p.Kind is PageKind.ElementArea or PageKind.FactoryArea).ToList();
            if (areas.Count > 0) sections.Add(new IndexSection("areas", "Areas", null, "Area", areas));
        }
        else if (cat.Layout == "modifiers")
        {
            var areas = pages.Where(p => p.Kind == PageKind.ModifierArea).ToList();
            var common = areas.Where(p => !p.IsControlModifierPage).ToList();
            var control = areas.Where(p => p.IsControlModifierPage).ToList();
            if (common.Count > 0)
                sections.Add(new IndexSection("common", "Common modifiers", "Modifiers that most elements have, grouped by purpose.", "Area", common));
            if (control.Count > 0)
                sections.Add(new IndexSection("controls", "Control modifiers", "Modifiers that only some controls have, grouped by the area of those controls.", "Area", control));
        }

        var typePages = pages.Where(p => p.Kind is PageKind.Type or PageKind.Group)
            .OrderBy(p => p.Kind == PageKind.Group ? 0 : 1)
            .ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Stem, StringComparer.Ordinal)
            .ToList();
        if (typePages.Count > 0)
            sections.Add(new IndexSection("types", cat.Layout == "hooks" ? "Types and options" : "Types", null, "Type", typePages));
        return sections;
    }

    private void RenderRootIndex(PageWriter w)
    {
        w.H1("API reference");
        w.Add(RootIntro);
        var sb = new StringBuilder("| Category | Description |\n| --- | --- |");
        foreach (var index in _plan.Pages.Where(p => p.Kind == PageKind.CategoryIndex))
        {
            var cat = index.Category!;
            sb.Append("\n| [").Append(DocMarkdown.EscapeProse(cat.Title)).Append("](")
              .Append(RelativeHref(w.Page.RelativePath, index.RelativePath, null)).Append(") | ")
              .Append(DocMarkdown.Cell(cat.Summary ?? string.Empty)).Append(" |");
        }
        w.Add(sb.ToString());
    }

    private string PageTable(string label, IEnumerable<PlannedPage> pages, PlannedPage from)
    {
        var sb = new StringBuilder($"| {label} | Description |\n| --- | --- |");
        foreach (var p in pages)
        {
            var title = p.Kind is PageKind.Hook or PageKind.Type ? DocMarkdown.CodeSpan(p.Title) : DocMarkdown.EscapeProse(p.Label);
            sb.Append("\n| [").Append(title).Append("](").Append(RelativeHref(from.RelativePath, p.RelativePath, null))
              .Append(") | ").Append(DocMarkdown.Cell(IndexDescription(p))).Append(" |");
        }
        return sb.ToString();
    }

    /// <summary>The summary of a modifier page: the modifier area's own, or one built from the control area it serves.</summary>
    private static string? ModifierAreaSummary(PlannedPage p) =>
        p.ModifierArea?.Summary is { Length: > 0 } s ? s
        : p.Area is { } a ? "Modifiers that only the controls in the " + a.Title + " area have." : null;

    /// <summary>A one-line, link-free description of a page for index tables.</summary>
    private string IndexDescription(PlannedPage p)
    {
        string? text = p.Kind switch
        {
            PageKind.Group => p.Group?.Summary,
            PageKind.ElementArea or PageKind.FactoryArea => p.Area?.Summary,
            PageKind.ModifierArea => ModifierAreaSummary(p),
            PageKind.CategoryIndex => p.Category?.Summary,
            PageKind.RootIndex => RootIntro,
            PageKind.Type => p.Types.Count > 0 ? QuietSummary(p.Types[0].DocId) : null,
            PageKind.Hook => LeadSummary(p.Members.Select(m => QuietSummary(m.DocId)).ToList()),
            _ => null,
        };
        return PlainFirstSentence(text ?? string.Empty);
    }

    /// <summary>
    /// The summary that best describes a set of overloads: the first one that does not
    /// call itself an overload ("Parameterless overload - ..."), else the first non-empty one.
    /// </summary>
    internal static string LeadSummary(IReadOnlyList<string> summaries) =>
        summaries.FirstOrDefault(s => s.Length > 0 && s.IndexOf("overload", StringComparison.OrdinalIgnoreCase) < 0)
        ?? summaries.FirstOrDefault(s => s.Length > 0)
        ?? string.Empty;

    /// <summary>A summary rendered without links and without recording a missing-summary finding.</summary>
    private string QuietSummary(string docId)
    {
        var md = new DocMarkdown(c => new CrefLink(null, LabelFor(c)));
        var d = Eff(docId);
        if (d.BoilerplateCref is not null)
            return d.BoilerplateCref.StartsWith("T:Microsoft.UI.Xaml", StringComparison.Ordinal)
                ? $"The element record that describes a WinUI {md.Link(d.BoilerplateCref)} control."
                : $"The element record that describes a {md.Link(d.BoilerplateCref)} control.";
        return d.Summary is not null ? md.Block(d.Summary) : d.GenericSummary ?? string.Empty;
    }

    private static string PlainFirstSentence(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
        var first = DocMarkdown.FirstSentence(markdown);
        return MarkdownLink.Replace(first, "$1").Trim();
    }

    // ── Shared building blocks ────────────────────────────────────────────

    private string? DeclarationFence(ApiType t) =>
        string.IsNullOrWhiteSpace(t.Declaration) ? null : Fence(new[] { SanitizeSignature(t.Declaration) });

    private string NamespaceLine(ApiType t, PageWriter w)
    {
        var sb = new StringBuilder("Namespace: ").Append(DocMarkdown.CodeSpan(t.Namespace)).Append('.');
        if (t.DeclaringType is not null)
            sb.Append(" Nested in ").Append(w.Md.Link(t.DeclaringType.DocId)).Append('.');
        if (t.BaseDocId is { } b && b is not ("T:System.Object" or "T:System.ValueType" or "T:System.Enum" or "T:System.MulticastDelegate"))
            sb.Append(" Inherits from ").Append(w.Md.Link(b)).Append('.');
        return sb.ToString();
    }

    private static string? ObsoleteNote(string? message, string what)
    {
        if (message is null) return null;
        return message.Length == 0
            ? $"> **Obsolete.** This {what} is obsolete."
            : $"> **Obsolete.** {DocMarkdown.EscapeProse(message)}";
    }

    private string? LearnMore(PageWriter w)
    {
        var slugs = new List<string>();
        foreach (var s in w.Page.GuidePages)
            if (!slugs.Contains(s)) slugs.Add(s);
        if (_pageTemplates.TryGetValue(w.Page.RelativePath, out var fromMarkers))
            foreach (var s in fromMarkers)
                if (!slugs.Contains(s)) slugs.Add(s);

        var links = new List<string>();
        foreach (var slug in slugs)
        {
            if (_templateIds is not null && !_templateIds.Contains(slug))
            {
                if (_final && _unknownGuidePages.Add(slug))
                {
                    _findings.Add(new RefGenFinding(
                        "REACTOR_DOC_REFGEN_W003",
                        $"reference-map.yaml names guide page '{slug}', but no template has that id. The link was left out.",
                        "docs/_pipeline/reference-map.yaml",
                        TierLintSeverity.Warning));
                }
                continue;
            }
            var title = _guideTitle?.Invoke(slug) ?? ReferenceLinkInjector.ToTitle(slug);
            links.Add($"[{DocMarkdown.EscapeProse(title)}]({RelativeHref(w.Page.RelativePath, slug + ".md", null)})");
        }
        return links.Count == 0 ? null : "**Learn more:** " + string.Join(", ", links);
    }

    /// <summary>
    /// Writes the parameters, type parameters, returns, value, remarks,
    /// examples, exceptions and see-also sections for a set of overloads,
    /// merged into one set.
    /// </summary>
    private void Details(PageWriter w, IReadOnlyList<ApiMember> overloads, bool headings, int level,
        bool includeSeeAlso, bool includeReturns = true)
    {
        var docs = overloads.Select(o => Eff(o.DocId)).ToList();
        Section(w, "Parameters", ParamTable(overloads, docs, w), headings, level, inline: false);
        var typeParamNames = overloads.SelectMany(o => o.TypeParameters).Distinct(StringComparer.Ordinal).ToList();
        Section(w, "Type parameters", TypeParamTable(typeParamNames, docs, w.Md), headings, level, inline: false);
        if (includeReturns)
            Section(w, "Returns", FirstBlock(docs.Select(d => d.Returns), w.Md), headings, level, inline: true);
        Section(w, "Value", FirstBlock(docs.Select(d => d.Value), w.Md), headings, level, inline: true);
        var remarks = docs.Where(d => d.Remarks is not null).Select(d => w.Md.Block(d.Remarks))
            .Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        Section(w, "Remarks", remarks.Count == 0 ? null : string.Join("\n\n", remarks), headings, level, inline: false);
        var examples = Examples(docs, w.Md);
        Section(w, examples.Count == 1 ? "Example" : "Examples", examples.Count == 0 ? null : string.Join("\n\n", examples), headings, level, inline: false);
        Section(w, "Exceptions", ExceptionTable(docs, w.Md), headings, level, inline: false);
        if (includeSeeAlso)
            Section(w, "See also", SeeAlsoList(docs, w.Md), headings, level, inline: false);
    }

    private static void Section(PageWriter w, string label, string? content, bool headings, int level, bool inline)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        if (headings)
        {
            w.H(level, label);
            w.Add(content);
        }
        else if (inline && IsSimpleParagraph(content))
        {
            w.Add($"**{label}:** {content}");
        }
        else
        {
            w.Add($"**{label}**");
            w.Add(content);
        }
    }

    private static bool IsSimpleParagraph(string s) =>
        !s.Contains('\n')
        && !s.StartsWith('|') && !s.StartsWith("```", StringComparison.Ordinal)
        && !s.StartsWith("- ", StringComparison.Ordinal) && !s.StartsWith('>')
        && !s.StartsWith('#') && !Regex.IsMatch(s, @"^\d+\. ");

    private string? ParamTable(IReadOnlyList<ApiMember> overloads, IReadOnlyList<EffDoc> docs, PageWriter w)
    {
        // One row per parameter name. When overloads take the same name with different
        // types (for example `string content` and `Element content`), the row lists each type.
        var rows = new List<(string Name, List<(string Type, string? DocId)> Types, string? Default, string Description)>();
        for (int i = 0; i < overloads.Count; i++)
        {
            foreach (var p in overloads[i].Params)
            {
                if (p.Modifier == "this") continue;
                var desc = docs[i].Params.TryGetValue(p.Name, out var el) ? DocMarkdown.Cell(w.Md.Inline(el)) : string.Empty;
                var at = rows.FindIndex(r => r.Name == p.Name);
                if (at < 0)
                {
                    rows.Add((p.Name, new List<(string, string?)> { (p.Type, p.TypeDocId) }, p.Default, desc));
                    continue;
                }
                if (!rows[at].Types.Any(t => t.Type == p.Type)) rows[at].Types.Add((p.Type, p.TypeDocId));
                if (rows[at].Description.Length == 0 && desc.Length > 0) rows[at] = rows[at] with { Description = desc };
                if (rows[at].Default is null && p.Default is not null) rows[at] = rows[at] with { Default = p.Default };
            }
        }
        if (rows.Count == 0) return null;
        var hasDefault = rows.Any(r => r.Default is not null);
        var sb = new StringBuilder(hasDefault
            ? "| Name | Type | Default | Description |\n| --- | --- | --- | --- |"
            : "| Name | Type | Description |\n| --- | --- | --- |");
        foreach (var r in rows)
        {
            sb.Append("\n| ").Append(DocMarkdown.CodeSpan(r.Name)).Append(" | ")
                .Append(string.Join(" or ", r.Types.Select(t => TypeCell(t.Type, t.DocId, w.Page))));
            if (hasDefault) sb.Append(" | ").Append(r.Default is null ? string.Empty : DocMarkdown.CodeSpan(r.Default));
            sb.Append(" | ").Append(r.Description).Append(" |");
        }
        return sb.ToString();
    }

    private static string? TypeParamTable(IReadOnlyList<string> names, IEnumerable<EffDoc> docs, DocMarkdown md)
    {
        var docList = docs.ToList();
        var ordered = names.ToList();
        var documented = docList.SelectMany(d => d.TypeParams.Keys).Distinct(StringComparer.Ordinal).ToList();
        // Without the assembly, a generic type with more than one parameter gets
        // placeholder names (T1, T2). Its documented names then replace them,
        // rather than both lists showing up.
        if (documented.Any(n => !ordered.Contains(n)))
            ordered.RemoveAll(n => !documented.Contains(n) && Regex.IsMatch(n, @"^T\d+$"));
        foreach (var n in documented)
            if (!ordered.Contains(n)) ordered.Add(n);
        var rows = ordered.Select(n => (Name: n, Text: docList.Select(d => d.TypeParams.TryGetValue(n, out var el) ? md.Inline(el) : string.Empty)
            .FirstOrDefault(s => s.Length > 0) ?? string.Empty)).ToList();
        if (rows.All(r => r.Text.Length == 0)) return null;
        var sb = new StringBuilder("| Name | Description |\n| --- | --- |");
        foreach (var r in rows)
            sb.Append("\n| ").Append(DocMarkdown.CodeSpan(r.Name)).Append(" | ").Append(DocMarkdown.Cell(r.Text)).Append(" |");
        return sb.ToString();
    }

    private string TypeCell(string? type, string? typeDocId, PlannedPage page)
    {
        if (string.IsNullOrEmpty(type)) return string.Empty;
        var code = DocMarkdown.CodeSpan(type);
        if (_final && typeDocId is not null && Locate(typeDocId) is { } loc)
            return $"[{code}]({RelativeHref(page.RelativePath, loc.RelativePath, loc.Anchor)})";
        return code;
    }

    private static string? FirstBlock(IEnumerable<XElement?> elements, DocMarkdown md) =>
        elements.Where(e => e is not null).Select(e => md.Block(e)).FirstOrDefault(s => s.Length > 0);

    private static List<string> Examples(IEnumerable<EffDoc> docs, DocMarkdown md)
    {
        var list = new List<string>();
        foreach (var d in docs)
            foreach (var ex in d.Examples)
            {
                var text = ExampleMd(ex, md);
                if (text.Length > 0 && !list.Contains(text)) list.Add(text);
            }
        return list;
    }

    /// <summary>
    /// Most examples in the source are bare code without a <c>&lt;code&gt;</c>
    /// element. Text that looks like code (it has a call or a lambda and does
    /// not end like a sentence) becomes a C# block.
    /// </summary>
    internal static string ExampleMd(XElement example, DocMarkdown md)
    {
        if (!example.HasElements)
        {
            var text = example.Value.Trim();
            if (text.Length > 0 && (text.Contains('(') || text.Contains("=>", StringComparison.Ordinal)) && !text.EndsWith('.'))
                return "```csharp\n" + DocMarkdown.Dedent(example.Value) + "\n```";
        }
        return md.Block(example);
    }

    private static string? ExceptionTable(IEnumerable<EffDoc> docs, DocMarkdown md)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var sb = new StringBuilder("| Exception | Condition |\n| --- | --- |");
        var any = false;
        foreach (var d in docs)
            foreach (var ex in d.Exceptions)
            {
                var cref = (string?)ex.Attribute("cref");
                if (cref is null || !seen.Add(cref)) continue;
                any = true;
                sb.Append("\n| ").Append(md.Link(cref)).Append(" | ").Append(DocMarkdown.Cell(md.Inline(ex))).Append(" |");
            }
        return any ? sb.ToString() : null;
    }

    private static string? SeeAlsoList(EffDoc doc, DocMarkdown md) => SeeAlsoList(new[] { doc }, md);

    private static string? SeeAlsoList(IEnumerable<EffDoc> docs, DocMarkdown md)
    {
        var items = new List<string>();
        foreach (var d in docs)
            foreach (var s in d.SeeAlso)
            {
                var cref = (string?)s.Attribute("cref");
                var href = (string?)s.Attribute("href");
                string? item = null;
                if (cref is not null) item = md.Link(cref);
                else if (href is not null)
                {
                    var text = s.Value.Trim();
                    item = $"[{DocMarkdown.EscapeProse(text.Length > 0 ? text : href)}]({href})";
                }
                if (item is not null && !items.Contains(item)) items.Add(item);
            }
        return items.Count == 0 ? null : string.Join("\n", items.Select(i => "- " + i));
    }

    private static string Fence(IEnumerable<string> lines) =>
        "```csharp\n" + string.Join("\n", lines) + "\n```";

    private static string Sig(ApiMember m, PlannedPage page)
    {
        var s = SanitizeSignature(m.Signature);
        if (page.Kind is PageKind.FactoryArea or PageKind.ModifierArea && s.StartsWith("public static ", StringComparison.Ordinal))
            s = s["public static ".Length..];
        return s;
    }

    /// <summary>
    /// Signature blocks must not contain <c>;</c>, <c>{</c> or <c>}</c>, so a
    /// default value that is a string with one of those characters is shortened.
    /// </summary>
    internal static string SanitizeSignature(string signature) =>
        Literal.Replace(signature, m => m.Value.IndexOfAny(new[] { ';', '{', '}' }) >= 0 ? "\"...\"" : m.Value);

    // ── Summaries and effective docs ──────────────────────────────────────

    private string SummaryMd(string docId, DocMarkdown md)
    {
        var d = Eff(docId);
        string text;
        if (d.BoilerplateCref is not null)
        {
            var link = md.Link(d.BoilerplateCref);
            text = d.BoilerplateCref.StartsWith("T:Microsoft.UI.Xaml", StringComparison.Ordinal)
                ? $"The element record that describes a WinUI {link} control."
                : $"The element record that describes a {link} control.";
        }
        else if (d.Summary is not null) text = md.Block(d.Summary);
        else text = d.GenericSummary ?? string.Empty;
        if (text.Length == 0 && _final) _missingSummary.Add(docId);
        return text;
    }

    private sealed class EffDoc
    {
        public XElement? Summary;
        public XElement? Remarks;
        public XElement? Returns;
        public XElement? Value;
        public readonly Dictionary<string, XElement> Params = new(StringComparer.Ordinal);
        public readonly Dictionary<string, XElement> TypeParams = new(StringComparer.Ordinal);
        public readonly List<XElement> Examples = new();
        public readonly List<XElement> Exceptions = new();
        public readonly List<XElement> SeeAlso = new();
        public string? BoilerplateCref;
        public string? GenericSummary;
    }

    private EffDoc Eff(string docId)
    {
        if (_eff.TryGetValue(docId, out var cached)) return cached;
        var d = new EffDoc();
        _eff[docId] = d;
        Fill(d, docId, new HashSet<string>(StringComparer.Ordinal) { docId }, 0);

        if (d.Summary is null && docId.StartsWith("P:", StringComparison.Ordinal)
            && CrefSignature.DeclaringTypeCref(docId) is { } decl
            && _docs.TryGetValue(decl, out var typeDoc) && typeDoc.Source is not null)
        {
            var name = SplitCref(docId).Name;
            d.Summary = typeDoc.Source.Elements("param")
                .FirstOrDefault(p => (string?)p.Attribute("name") == name && !IsEmpty(p));
        }

        if (d.Summary is null)
        {
            d.GenericSummary = SplitCref(docId).Name switch
            {
                "Dispose" => "Releases the resources that this object holds.",
                "ToString" => "Returns a string that represents this object.",
                "Equals" => "Returns whether this object equals another object.",
                "GetHashCode" => "Returns a hash code for this object.",
                _ => null,
            };
        }
        else
        {
            var collapsed = Whitespace.Replace(d.Summary.Value, " ").Trim();
            if (collapsed.StartsWith("Generated Reactor descriptor", StringComparison.Ordinal)
                || collapsed.StartsWith("Generated Reactor wrapper for", StringComparison.Ordinal))
            {
                d.BoilerplateCref = d.Summary.Descendants("see")
                    .Select(s => (string?)s.Attribute("cref"))
                    .FirstOrDefault(c => c is not null && c.StartsWith("T:", StringComparison.Ordinal));
            }
        }
        return d;
    }

    private void Fill(EffDoc d, string docId, HashSet<string> visiting, int depth)
    {
        if (!_docs.TryGetValue(docId, out var doc) || doc.Source is null) return;
        var src = doc.Source;
        d.Summary ??= NonEmpty(src.Element("summary"));
        d.Remarks ??= NonEmpty(src.Element("remarks"));
        d.Returns ??= NonEmpty(src.Element("returns"));
        d.Value ??= NonEmpty(src.Element("value"));
        foreach (var p in src.Elements("param"))
            if ((string?)p.Attribute("name") is { } n && !IsEmpty(p)) d.Params.TryAdd(n, p);
        foreach (var p in src.Elements("typeparam"))
            if ((string?)p.Attribute("name") is { } n && !IsEmpty(p)) d.TypeParams.TryAdd(n, p);
        if (d.Examples.Count == 0) d.Examples.AddRange(src.Elements("example").Where(e => !IsEmpty(e)));
        if (d.Exceptions.Count == 0) d.Exceptions.AddRange(src.Elements("exception"));
        if (d.SeeAlso.Count == 0) d.SeeAlso.AddRange(src.Elements("seealso"));
        if (depth >= 8) return;
        foreach (var inherit in src.Elements("inheritdoc"))
        {
            var cref = (string?)inherit.Attribute("cref");
            var candidates = cref is not null ? new List<string> { cref } : InheritCandidates(docId);
            foreach (var c in candidates)
                if (visiting.Add(c)) Fill(d, c, visiting, depth + 1);
        }
    }

    private List<string> InheritCandidates(string docId)
    {
        var result = new List<string>();
        var model = _plan.Model;
        if (docId.StartsWith("T:", StringComparison.Ordinal))
        {
            var guard = 0;
            var current = model.TypesByDocId.TryGetValue(docId, out var t) ? t.BaseDocId : null;
            while (current is not null && guard++ < 8)
            {
                result.Add(current);
                current = model.TypesByDocId.TryGetValue(current, out var bt) ? bt.BaseDocId : null;
            }
            return result;
        }

        var decl = CrefSignature.DeclaringTypeCref(docId);
        if (decl is not null && docId.Length > decl.Length)
        {
            var suffix = docId[decl.Length..];
            var guard = 0;
            var current = model.TypesByDocId.TryGetValue(decl, out var t) ? t.BaseDocId : null;
            while (current is not null && guard++ < 8)
            {
                result.Add(docId[..2] + current[2..] + suffix);
                current = model.TypesByDocId.TryGetValue(current, out var bt) ? bt.BaseDocId : null;
            }
        }
        if (model.MembersByDocId.TryGetValue(docId, out var m) && m.Name.StartsWith("Use", StringComparison.Ordinal)
            && model.TypesByDocId.TryGetValue(ReferencePlanner.RenderContextDocId, out var rc))
        {
            var key = ReferencePlanner.HookKey(m);
            result.AddRange(rc.Members.Where(r => r.DocId != docId && r.Name == m.Name && ReferencePlanner.HookKey(r) == key)
                .Select(r => r.DocId));
        }
        return result;
    }

    private static XElement? NonEmpty(XElement? e) => e is null || IsEmpty(e) ? null : e;

    private static bool IsEmpty(XElement e) => !e.HasElements && string.IsNullOrWhiteSpace(e.Value);

    // ── Locations, links and labels ───────────────────────────────────────

    private void Record(string docId, PlannedPage page, string? anchor)
    {
        if (_final) return;
        if (_locations.TryGetValue(docId, out var existing) && existing.RelativePath != page.RelativePath) return;
        _locations[docId] = new RefLocation(page.RelativePath, anchor, LabelFor(docId));
    }

    private void FillLocationGaps()
    {
        var model = _plan.Model;
        foreach (var docId in model.MembersByDocId.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (_locations.ContainsKey(docId)) continue;
            var m = model.MembersByDocId[docId];
            if (_locations.TryGetValue(m.DeclaringType.DocId, out var typeLoc))
                _locations[docId] = typeLoc with { Label = LabelFor(docId) };
            else if (_plan.PageByMemberDocId.TryGetValue(docId, out var page))
                _locations[docId] = new RefLocation(page.RelativePath, null, LabelFor(docId));
        }

        // A type with no page of its own whose members live on area pages
        // (Factories, ElementExtensions, ...) links to the category index of
        // its first placed member.
        foreach (var t in model.Types.OrderBy(t => t.DocId, StringComparer.Ordinal))
        {
            if (_locations.ContainsKey(t.DocId)) continue;
            var placed = t.Members.Select(m => m.DocId).OrderBy(id => id, StringComparer.Ordinal)
                .Select(id => _plan.PageByMemberDocId.TryGetValue(id, out var p) ? p : null)
                .FirstOrDefault(p => p is not null);
            if (placed?.Category is null) continue;
            var index = _plan.Pages.FirstOrDefault(p => p.Kind == PageKind.CategoryIndex && p.Category == placed.Category);
            if (index is not null) _locations[t.DocId] = new RefLocation(index.RelativePath, null, t.DisplayName);
        }
    }

    private void BuildIndexes()
    {
        var byName = new Dictionary<string, RefLocation>(StringComparer.Ordinal);
        foreach (var hook in _plan.Pages.Where(p => p.Kind == PageKind.Hook))
            byName.TryAdd(hook.Title, new RefLocation(hook.RelativePath, null, hook.Title));
        foreach (var t in _plan.Model.Types.OrderBy(t => t.DocId, StringComparer.Ordinal))
        {
            if (!_locations.TryGetValue(t.DocId, out var loc)) continue;
            if (!_plan.PageByTypeDocId.ContainsKey(t.DocId)) continue;
            byName.TryAdd(t.Name, loc);
            byName.TryAdd(t.DisplayName, loc);
        }
        foreach (var kind in new[] { PageKind.FactoryArea, PageKind.ModifierArea })
            foreach (var page in _plan.Pages.Where(p => p.Kind == kind))
                foreach (var m in page.Members)
                    if (_locations.TryGetValue(m.DocId, out var loc)) byName.TryAdd(m.Name, loc);
        _linksByName = byName;

        foreach (var (docId, loc) in _locations.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (docId.StartsWith("T:", StringComparison.Ordinal))
            {
                _typeNoArity.TryAdd(Arity.Replace(docId, string.Empty), loc);
                continue;
            }
            var decl = CrefSignature.DeclaringTypeCref(docId);
            if (decl is not null) _byDeclName.TryAdd(decl + "|" + SplitCref(docId).Name, loc);
        }
    }

    private void MapTemplatesToPages()
    {
        if (_reverseIndex is null) return;
        foreach (var (ident, refs) in _reverseIndex.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var loc = ReferenceLinkInjector.ResolveIdent(ident, _locations, _linksByName);
            if (loc is null) continue;
            if (!_pageTemplates.TryGetValue(loc.RelativePath, out var set))
                _pageTemplates[loc.RelativePath] = set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var r in refs) set.Add(r.TemplateId);
        }
    }

    private CrefLink Resolve(string cref, PlannedPage page)
    {
        if (cref.StartsWith("!:", StringComparison.Ordinal)) return new CrefLink(null, cref[2..]);
        var label = LabelFor(cref);
        if (!_final) return new CrefLink(null, label);

        if (Locate(cref) is { } loc)
            return new CrefLink(RelativeHref(page.RelativePath, loc.RelativePath, loc.Anchor), label);
        if (LearnUrl(cref) is { } url) return new CrefLink(url, label);

        if (cref.Contains("Microsoft.UI.Reactor.", StringComparison.Ordinal)
            && !_plan.Model.TypesByDocId.ContainsKey(cref)
            && !_plan.Model.MembersByDocId.ContainsKey(cref)
            && !IsExcluded(cref)
            && _unresolved.Add(cref))
        {
            _findings.Add(new RefGenFinding(
                "REACTOR_DOC_REFGEN_001",
                $"cref '{cref}' does not resolve to a reference page. It was rendered as inline code.",
                "docs/guide/" + page.RelativePath,
                TierLintSeverity.Warning));
        }
        return new CrefLink(null, label);
    }

    private bool IsExcluded(string cref)
    {
        var typeCref = cref.StartsWith("T:", StringComparison.Ordinal) ? cref : CrefSignature.DeclaringTypeCref(cref) ?? cref;
        return _plan.Map.Resolve(typeCref)?.Category == ReferenceMap.ExcludedCategory;
    }

    private RefLocation? Locate(string cref)
    {
        if (_locations.TryGetValue(cref, out var loc)) return loc;
        var decl = cref.StartsWith("T:", StringComparison.Ordinal) ? null : CrefSignature.DeclaringTypeCref(cref);
        if (decl is not null && _byDeclName.TryGetValue(decl + "|" + SplitCref(cref).Name, out loc)) return loc;
        if (cref.StartsWith("T:", StringComparison.Ordinal) && _typeNoArity.TryGetValue(Arity.Replace(cref, string.Empty), out loc)) return loc;
        var guard = 0;
        while (decl is not null && guard++ < 3)
        {
            if (_locations.TryGetValue(decl, out loc)) return loc;
            if (_typeNoArity.TryGetValue(Arity.Replace(decl, string.Empty), out loc)) return loc;
            var outer = SplitCref(decl).Decl;
            decl = outer.Length == 0 ? null : "T:" + outer;
        }
        return null;
    }

    internal static string? LearnUrl(string cref)
    {
        if (cref.Length < 3 || cref[1] != ':') return null;
        var body = cref[2..];
        var paren = body.IndexOf('(');
        if (paren >= 0) body = body[..paren];
        body = Regex.Replace(body, @"``\d+", string.Empty);
        body = Regex.Replace(body, @"`(\d+)", "-$1");
        body = body.Replace("#ctor", "-ctor", StringComparison.Ordinal);
        var lower = body.ToLowerInvariant();
        if (body.StartsWith("System.", StringComparison.Ordinal))
            return "https://learn.microsoft.com/dotnet/api/" + lower;
        if (body.StartsWith("Microsoft.UI.", StringComparison.Ordinal) && !body.StartsWith("Microsoft.UI.Reactor", StringComparison.Ordinal))
            return "https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/" + lower;
        if (body.StartsWith("Windows.", StringComparison.Ordinal))
            return "https://learn.microsoft.com/uwp/api/" + lower;
        return null;
    }

    private string LabelFor(string docId)
    {
        var model = _plan.Model;
        if (model.TypesByDocId.TryGetValue(docId, out var t)) return t.DisplayName;
        if (model.MembersByDocId.TryGetValue(docId, out var m))
        {
            if (_plan.PageByMemberDocId.TryGetValue(docId, out var p) && p.Kind is PageKind.Hook or PageKind.FactoryArea or PageKind.ModifierArea)
                return m.Name;
            if (m.Kind == ApiMemberKind.Constructor) return m.DeclaringType.Name;
            return m.DeclaringType.Name + "." + m.Name;
        }
        var (decl, name) = SplitCref(docId);
        name = Arity.Replace(name, string.Empty);
        if (docId.StartsWith("T:", StringComparison.Ordinal) || docId.Length < 2 || docId[1] != ':') return name;
        var declLast = Arity.Replace(decl[(decl.LastIndexOf('.') + 1)..], string.Empty);
        if (name == "#ctor") return declLast;
        return declLast.Length == 0 ? name : declLast + "." + name;
    }

    /// <summary>Splits a cref into its declaring part and its last name, without the parameter list.</summary>
    private static (string Decl, string Name) SplitCref(string cref)
    {
        var body = cref.Length > 2 && cref[1] == ':' ? cref[2..] : cref;
        var paren = body.IndexOf('(');
        if (paren >= 0) body = body[..paren];
        var tilde = body.IndexOf('~');
        if (tilde >= 0) body = body[..tilde];
        var dot = body.LastIndexOf('.');
        var name = dot < 0 ? body : body[(dot + 1)..];
        if (!cref.StartsWith("T:", StringComparison.Ordinal)) name = Arity.Replace(name, string.Empty);
        return dot < 0 ? (string.Empty, name) : (body[..dot], name);
    }

    /// <summary>
    /// The link from one generated page to another (both relative to
    /// <c>docs/guide</c>), with an optional heading anchor.
    /// </summary>
    internal static string RelativeHref(string from, string to, string? anchor)
    {
        if (from == to) return anchor is not null ? "#" + anchor : to[(to.LastIndexOf('/') + 1)..];
        var fromDir = from.Split('/')[..^1];
        var toParts = to.Split('/');
        var common = 0;
        while (common < fromDir.Length && common < toParts.Length - 1 && fromDir[common] == toParts[common]) common++;
        var href = string.Concat(Enumerable.Repeat("../", fromDir.Length - common)) + string.Join("/", toParts[common..]);
        return anchor is not null ? href + "#" + anchor : href;
    }

    // ── Sidebar fragment ──────────────────────────────────────────────────

    /// <summary>
    /// The <c>nav.json</c> groups for the generated pages, in category order.
    /// Each category is a numbered folder (the number orders the sidebar and drops
    /// out of the URL, so <c>reference/01-hooks/UseState</c> is served at
    /// <c>/reference/hooks/UseState</c>). The sections of the category index become
    /// <c>(group)</c> folders inside it. Pages that need a label other than their file
    /// name are listed; plain type pages are left to the catch-all glob, so a type that
    /// is added or removed does not need a <c>nav.json</c> change. The text uses the
    /// compact layout of <c>nav.json</c> (one page per line, groups indented for the
    /// <c>groups</c> array), so it can be pasted in as it is.
    /// </summary>
    private string BuildNavFragment()
    {
        var groups = new List<string>
        {
            NavGroup("reference", "API reference", new[] { ("reference/index.md", "Overview") }, null),
        };
        var number = 0;
        foreach (var index in _plan.Pages.Where(p => p.Kind == PageKind.CategoryIndex))
        {
            var cat = index.Category!;
            var dir = $"reference/{++number:00}-{cat.Id}";
            var glob = $"reference/{cat.Id}/*.md";
            var pages = _plan.PagesIn(cat).Where(p => p.Kind is not PageKind.CategoryIndex and not PageKind.RootIndex).ToList();
            var sections = IndexSections(cat, pages);
            var overview = new List<(string, string)> { (index.RelativePath, "Overview") };

            // Elements and factories show their area pages right under the overview;
            // a category with one section shows it there too.
            var inline = sections.Count == 1 || (sections.Count > 0 && sections[0].Id == "areas") ? sections[0] : null;
            if (inline is not null) overview.AddRange(NavPages(inline));
            var rest = sections.Where(s => s != inline).ToList();
            groups.Add(NavGroup(dir, cat.Title, overview, rest.Count == 0 ? glob : null));

            var sub = 0;
            foreach (var section in rest)
            {
                var last = section == rest[^1];
                groups.Add(NavGroup($"{dir}/{++sub:00}-({section.Id})", section.Title, NavPages(section), last ? glob : null));
            }
        }
        return "[\n" + string.Join(",\n", groups) + "\n]\n";
    }

    /// <summary>
    /// The pages of a sidebar section that must be listed by hand. A type page whose
    /// file name already reads well is left to the glob, which places pages in file-name
    /// order after the listed ones.
    /// </summary>
    private static IEnumerable<(string File, string Label)> NavPages(IndexSection section)
    {
        foreach (var p in section.Pages)
        {
            if (section.Id == "types" && p.Kind == PageKind.Type && !p.Stem.Contains('-', StringComparison.Ordinal)) continue;
            yield return (p.RelativePath, p.Kind == PageKind.Type ? p.Title : p.Label);
        }
    }

    private static string NavGroup(string dir, string title, IEnumerable<(string File, string Label)> pages, string? glob)
    {
        const string indent = "    ";
        var head = $"\"dir\": {JsonText(dir)}, \"title\": {JsonText(title)}";
        var globText = glob is null ? null : $"\"glob\": {JsonText(glob)}";
        var list = pages.Select(p => $"{{ \"file\": {JsonText(p.File)}, \"label\": {JsonText(p.Label)} }}").ToList();
        if (list.Count == 0) return $"{indent}{{ {head}{(globText is null ? "" : ", " + globText)} }}";

        var lines = new List<string>
        {
            $"{indent}  \"dir\": {JsonText(dir)}",
            $"{indent}  \"title\": {JsonText(title)}",
            list.Count == 1
                ? $"{indent}  \"pages\": [{list[0]}]"
                : $"{indent}  \"pages\": [\n" + string.Join(",\n", list.Select(p => $"{indent}    {p}")) + $"\n{indent}  ]",
        };
        if (globText is not null) lines.Add($"{indent}  {globText}");
        return $"{indent}{{\n" + string.Join(",\n", lines) + $"\n{indent}}}";
    }

    private static string JsonText(string value) =>
        "\"" + JsonEncodedText.Encode(value, System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping) + "\"";

    // ── Page writer ───────────────────────────────────────────────────────

    private sealed class PageWriter
    {
        private readonly ReferenceRenderer _owner;
        private readonly List<string> _blocks = new();
        private readonly SlugTracker _slugs = new();

        public PageWriter(ReferenceRenderer owner, PlannedPage page)
        {
            _owner = owner;
            Page = page;
            Md = new DocMarkdown(c => owner.Resolve(c, page));
        }

        public PlannedPage Page { get; }
        public DocMarkdown Md { get; }

        public void H1(string md, IEnumerable<string>? docIds = null)
        {
            _blocks.Add("# " + md);
            _slugs.Add(md);
            if (docIds is null) return;
            foreach (var id in docIds) _owner.Record(id, Page, null);
        }

        public string H(int level, string md, IEnumerable<string>? docIds = null)
        {
            level = Math.Clamp(level, 2, 6);
            var id = _slugs.Add(md);
            _blocks.Add(new string('#', level) + " " + md);
            if (docIds is not null)
                foreach (var d in docIds) _owner.Record(d, Page, id);
            return id;
        }

        public void Add(string? block)
        {
            if (!string.IsNullOrWhiteSpace(block)) _blocks.Add(block.TrimEnd());
        }

        public string Build() => string.Join("\n\n", _blocks) + "\n";
    }
}
