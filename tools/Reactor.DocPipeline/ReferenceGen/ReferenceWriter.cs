using System.Text;

namespace Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;

/// <summary>
/// One member as it appears on a page: the member itself plus the heading
/// (and matching anchor slug) it is filed under. Single-member pages carry
/// an empty heading/anchor — the page title already names the member.
/// </summary>
internal sealed record PageMember(MemberDoc Member, string Heading, string Anchor);

/// <summary>
/// Emits one Markdown page per routed short name using the template
/// in spec 041 §7.1.2 (Name / Signature / Summary / Parameters / Returns /
/// Discussion / Examples / Caveats / See Also). The writer takes the fully-
/// routed member set and the resolver so <c>&lt;see cref=&quot;...&quot;/&gt;</c>
/// links can be rewritten relative to this page.
///
/// A page may document several members. Overloads share a short name and
/// therefore a page: each gets its own <c>##</c> section so nothing is
/// dropped, and the page keeps its one-file-per-name identity so inbound
/// links and <c>&lt;!-- ref:Name --&gt;</c> markers stay valid.
/// </summary>
internal static class ReferenceWriter
{
    /// <summary>
    /// Build the page body. Returns the raw Markdown plus a list of
    /// findings (currently just unresolved cref warnings — REFGEN_001 lives
    /// in the orchestrator).
    /// </summary>
    public static WriteResult Write(MemberDoc member, RouterResult route, CrefResolver resolver) =>
        Write(new[] { new PageMember(member, string.Empty, string.Empty) }, route, resolver);

    /// <summary>
    /// Build a page documenting <paramref name="members"/> in the order
    /// given. The caller is responsible for producing a deterministic order
    /// and for assigning heading/anchor text (see
    /// <see cref="ReferenceGenerator"/>), so that anchors are known before
    /// any page is rendered and cross-page links can target them.
    /// </summary>
    public static WriteResult Write(
        IReadOnlyList<PageMember> members,
        RouterResult route,
        CrefResolver resolver)
    {
        var unresolved = new List<UnresolvedCref>();
        var missingSummary = new List<string>();
        var sb = new StringBuilder();
        var primary = members[0].Member;

        sb.AppendLine($"# {route.ShortName}");
        sb.AppendLine();
        sb.AppendLine($"`{KindLabel(primary.Kind)}`  ");
        sb.AppendLine($"_cref_: `{primary.Cref}`");
        sb.AppendLine();

        if (members.Count == 1)
        {
            AppendMemberSections(sb, primary, route, resolver, unresolved, missingSummary, headingLevel: 2);
            return new WriteResult(sb.ToString(), unresolved, missingSummary);
        }

        // Multi-member page: index first so a reader can jump straight to the
        // member they mean, then one section per member.
        //
        // Members that share a short name but come from unrelated declaring
        // types (REACTOR_DOC_REFGEN_002) are not overloads — e.g.
        // FocusManager.Register and PendingScope.Register. Calling that section
        // "Overloads" tells the reader they are one API with several forms.
        // Label it "Members" and qualify each entry with its declaring type.
        // The per-member headings are left alone: their anchors are computed
        // upstream and are what cref links resolve to.
        var scopes = members
            .Select(pm => DeclaringScopeOf(pm.Member))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var collision = scopes.Count > 1;

        sb.AppendLine(collision ? "## Members" : "## Overloads");
        sb.AppendLine();
        if (collision)
        {
            sb.AppendLine(
                "> These members share a name but are declared on unrelated types. " +
                "They are not overloads of one another.");
            sb.AppendLine();
        }
        foreach (var pm in members)
        {
            var qualifier = collision ? $" — `{DeclaringScopeOf(pm.Member)}`" : string.Empty;
            sb.AppendLine($"- [`{pm.Heading}`](#{pm.Anchor}){qualifier}");
        }
        sb.AppendLine();

        foreach (var pm in members)
        {
            // Code-span the heading: a bare `UseEffect<T1>(...)` would have
            // its `<T1>` eaten by the Markdown renderer's HTML sanitiser.
            // GitHub drops the backticks when slugifying, so the anchor is
            // unchanged by the wrapping.
            sb.AppendLine($"## `{pm.Heading}`");
            sb.AppendLine();
            sb.AppendLine($"`{KindLabel(pm.Member.Kind)}`  ");
            sb.AppendLine($"_cref_: `{pm.Member.Cref}`");
            sb.AppendLine();
            AppendMemberSections(sb, pm.Member, route, resolver, unresolved, missingSummary, headingLevel: 3);
        }

        return new WriteResult(sb.ToString(), unresolved, missingSummary);
    }

    /// <summary>
    /// Declaring scope for a member, mirroring <c>ReferenceGenerator.DeclaringScope</c>.
    /// Used to tell genuine overloads apart from unrelated same-name members
    /// that route to one page (REACTOR_DOC_REFGEN_002).
    /// </summary>
    private static string DeclaringScopeOf(MemberDoc member)
    {
        var parts = CrefSignature.Parse(member.Cref);
        if (parts.Kind == "T")
            return string.IsNullOrEmpty(parts.DeclaringName)
                ? parts.Name
                : parts.DeclaringName + "." + parts.Name;
        return string.IsNullOrEmpty(parts.DeclaringName) ? parts.Name : parts.DeclaringName;
    }

    private static void AppendMemberSections(
        StringBuilder sb,
        MemberDoc member,
        RouterResult route,
        CrefResolver resolver,
        List<UnresolvedCref> unresolvedOut,
        List<string> missingSummary,
        int headingLevel)
    {
        var h = new string('#', headingLevel);
        var unresolved = new List<string>();

        // Summary (Spec 041 §7.1.2 — "## Summary"). Authors writing the XML
        // doc may omit it; PHASE-1B emits a placeholder so the page is still
        // visible. The REACTOR_DOC_001 analyzer (1.8) is the canonical
        // enforcement; here we only warn so ref-gen can complete.
        sb.AppendLine($"{h} Summary");
        sb.AppendLine();
        if (string.IsNullOrWhiteSpace(member.Summary))
        {
            sb.AppendLine("*Summary pending.*");
            missingSummary.Add(member.Cref);
        }
        else
        {
            sb.AppendLine(resolver.Rewrite(member.Summary, route.RelativePath, unresolved));
        }
        sb.AppendLine();

        if (member.Params.Count > 0)
        {
            sb.AppendLine($"{h} Parameters");
            sb.AppendLine();
            foreach (var p in member.Params)
            {
                sb.AppendLine($"- **{p.Name}** — {resolver.Rewrite(p.Text, route.RelativePath, unresolved)}");
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(member.Returns))
        {
            sb.AppendLine($"{h} Returns");
            sb.AppendLine();
            sb.AppendLine(resolver.Rewrite(member.Returns, route.RelativePath, unresolved));
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(member.Remarks))
        {
            sb.AppendLine($"{h} Discussion");
            sb.AppendLine();
            sb.AppendLine(resolver.Rewrite(member.Remarks, route.RelativePath, unresolved));
            sb.AppendLine();
        }

        if (member.Examples.Count > 0)
        {
            sb.AppendLine($"{h} Examples");
            sb.AppendLine();
            foreach (var ex in member.Examples)
            {
                var body = resolver.Rewrite(ex, route.RelativePath, unresolved);

                // An <example> whose author omitted the <code> wrapper arrives
                // as bare lines, and Markdown then collapses them into one
                // prose paragraph — unreadable and uncopyable. <example> is a
                // code sample by convention, so fence whatever did not already
                // come through as one.
                if (!body.Contains("```", StringComparison.Ordinal))
                    body = "```csharp\n" + body.Trim('\r', '\n') + "\n```";

                sb.AppendLine(body);
                sb.AppendLine();
            }
        }

        if (member.Caveats.Count > 0)
        {
            sb.AppendLine($"{h} Caveats");
            sb.AppendLine();
            foreach (var c in member.Caveats)
            {
                sb.AppendLine($"> {resolver.Rewrite(c, route.RelativePath, unresolved)}");
                sb.AppendLine();
            }
        }

        if (member.SeeAlsos.Count > 0)
        {
            sb.AppendLine($"{h} See Also");
            sb.AppendLine();
            foreach (var seeAlsoCref in member.SeeAlsos)
            {
                var target = resolver.ResolveTarget(seeAlsoCref);
                if (target is not null)
                {
                    sb.AppendLine($"- [{target.DisplayName}]({CrefResolver.LinkTo(target, route.RelativePath)})");
                }
                else
                {
                    unresolved.Add(seeAlsoCref);
                    sb.AppendLine($"- `{seeAlsoCref}`");
                }
            }
            sb.AppendLine();
        }

        foreach (var u in unresolved)
            unresolvedOut.Add(new UnresolvedCref(u, member.Cref));
    }

    private static string KindLabel(MemberKind kind) => kind switch
    {
        MemberKind.Type => "type",
        MemberKind.Method => "method",
        MemberKind.Property => "property",
        MemberKind.Field => "field",
        MemberKind.Event => "event",
        _ => "member",
    };
}

/// <summary>
/// Rendered page plus the diagnostics the orchestrator turns into findings.
/// </summary>
/// <param name="MissingSummaryCrefs">Crefs of members rendered with a
/// placeholder summary — one entry per member, because a page may document
/// several.</param>
internal sealed record WriteResult(
    string Body,
    IReadOnlyList<UnresolvedCref> UnresolvedCrefs,
    IReadOnlyList<string> MissingSummaryCrefs);

/// <summary>
/// A cref that had no reference page and was degraded to inline code,
/// together with the member whose docs referenced it.
/// </summary>
internal sealed record UnresolvedCref(string Cref, string InMemberCref);
