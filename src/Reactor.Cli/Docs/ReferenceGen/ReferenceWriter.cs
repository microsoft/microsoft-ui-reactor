using System.Text;

namespace Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;

/// <summary>
/// Emits one Markdown page per <see cref="MemberDoc"/> using the template
/// in spec 041 §7.1.2 (Name / Signature / Summary / Parameters / Returns /
/// Discussion / Examples / Caveats / See Also). The writer takes a fully-
/// routed member and the resolver so <c>&lt;see cref=&quot;...&quot;/&gt;</c>
/// links can be rewritten relative to this page.
/// </summary>
internal static class ReferenceWriter
{
    /// <summary>
    /// Build the page body. Returns the raw Markdown plus a list of
    /// findings (currently just unresolved cref warnings — REFGEN_001 lives
    /// in the orchestrator).
    /// </summary>
    public static WriteResult Write(MemberDoc member, RouterResult route, CrefResolver resolver)
    {
        var unresolved = new List<string>();
        var sb = new StringBuilder();

        sb.AppendLine($"# {route.ShortName}");
        sb.AppendLine();
        sb.AppendLine($"`{KindLabel(member.Kind)}`  ");
        sb.AppendLine($"_cref_: `{member.Cref}`");
        sb.AppendLine();

        // Summary (Spec 041 §7.1.2 — "## Summary"). Authors writing the XML
        // doc may omit it; PHASE-1B emits a placeholder so the page is still
        // visible. The REACTOR_DOC_001 analyzer (1.8) is the canonical
        // enforcement; here we only warn so ref-gen can complete.
        sb.AppendLine("## Summary");
        sb.AppendLine();
        if (string.IsNullOrWhiteSpace(member.Summary))
        {
            sb.AppendLine("*Summary pending.*");
        }
        else
        {
            sb.AppendLine(resolver.Rewrite(member.Summary, route.RelativePath, unresolved));
        }
        sb.AppendLine();

        if (member.Params.Count > 0)
        {
            sb.AppendLine("## Parameters");
            sb.AppendLine();
            foreach (var p in member.Params)
            {
                sb.AppendLine($"- **{p.Name}** — {resolver.Rewrite(p.Text, route.RelativePath, unresolved)}");
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(member.Returns))
        {
            sb.AppendLine("## Returns");
            sb.AppendLine();
            sb.AppendLine(resolver.Rewrite(member.Returns, route.RelativePath, unresolved));
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(member.Remarks))
        {
            sb.AppendLine("## Discussion");
            sb.AppendLine();
            sb.AppendLine(resolver.Rewrite(member.Remarks, route.RelativePath, unresolved));
            sb.AppendLine();
        }

        if (member.Examples.Count > 0)
        {
            sb.AppendLine("## Examples");
            sb.AppendLine();
            foreach (var ex in member.Examples)
            {
                sb.AppendLine(resolver.Rewrite(ex, route.RelativePath, unresolved));
                sb.AppendLine();
            }
        }

        if (member.Caveats.Count > 0)
        {
            sb.AppendLine("## Caveats");
            sb.AppendLine();
            foreach (var c in member.Caveats)
            {
                sb.AppendLine($"> {resolver.Rewrite(c, route.RelativePath, unresolved)}");
                sb.AppendLine();
            }
        }

        if (member.SeeAlsos.Count > 0)
        {
            sb.AppendLine("## See Also");
            sb.AppendLine();
            foreach (var seeAlsoCref in member.SeeAlsos)
            {
                var target = resolver.Resolve(seeAlsoCref);
                if (target is not null)
                {
                    var fromDir = Path.GetDirectoryName(route.RelativePath)?.Replace('\\', '/') ?? string.Empty;
                    var link = MakeRelativeLink(fromDir, target.RelativePath);
                    sb.AppendLine($"- [{target.ShortName}]({link})");
                }
                else
                {
                    unresolved.Add(seeAlsoCref);
                    sb.AppendLine($"- `{seeAlsoCref}`");
                }
            }
            sb.AppendLine();
        }

        return new WriteResult(sb.ToString(), unresolved, MissingSummary: string.IsNullOrWhiteSpace(member.Summary));
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

    private static string MakeRelativeLink(string fromDir, string targetRelativePath)
    {
        var fromSegments = string.IsNullOrEmpty(fromDir)
            ? Array.Empty<string>()
            : fromDir.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var toSegments = targetRelativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        int common = 0;
        while (common < fromSegments.Length && common < toSegments.Length &&
               string.Equals(fromSegments[common], toSegments[common], StringComparison.Ordinal))
            common++;

        var ups = string.Concat(Enumerable.Repeat("../", fromSegments.Length - common));
        var rest = string.Join('/', toSegments.Skip(common));
        var combined = ups + rest;
        return combined.Length == 0 ? "." : combined;
    }
}

internal sealed record WriteResult(string Body, IReadOnlyList<string> UnresolvedCrefs, bool MissingSummary);
