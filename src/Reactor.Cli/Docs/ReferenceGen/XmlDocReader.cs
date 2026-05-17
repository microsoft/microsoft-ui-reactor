using System.Xml.Linq;

namespace Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;

/// <summary>
/// One canonical member as extracted from an XML doc file. Spec 041 §7.1.2
/// uses this shape to drive the <see cref="ReferenceWriter"/> templates.
/// </summary>
internal enum MemberKind
{
    Unknown,
    Type,
    Method,
    Property,
    Field,
    Event,
}

internal sealed record ParamDoc(string Name, string Text);

/// <summary>
/// Author-facing model of one <c>&lt;member&gt;</c> element from
/// <c>Reactor.xml</c>. Crefs use the canonical Roslyn form
/// (<c>T:Namespace.Type</c>, <c>M:Namespace.Type.Method(System.String)</c>,
/// <c>P:</c>, <c>F:</c>, <c>E:</c>).
/// </summary>
internal sealed record MemberDoc(
    string Cref,
    MemberKind Kind,
    string Summary,
    IReadOnlyList<ParamDoc> Params,
    string Returns,
    string Remarks,
    IReadOnlyList<string> Examples,
    IReadOnlyList<string> Caveats,
    IReadOnlyList<string> SeeAlsos);

/// <summary>
/// Parses <c>Reactor.xml</c> documentation files using
/// <see cref="System.Xml.Linq"/>. The reader is lossy on element ordering
/// (only the documented section list above) but preserves inline XML
/// inside summaries / remarks so <see cref="CrefResolver"/> can rewrite
/// <c>&lt;see cref="..."/&gt;</c> at render time.
/// </summary>
internal static class XmlDocReader
{
    public static List<MemberDoc> Read(string xmlPath)
    {
        var doc = XDocument.Load(xmlPath);
        return Parse(doc);
    }

    public static List<MemberDoc> ReadString(string xml)
    {
        var doc = XDocument.Parse(xml);
        return Parse(doc);
    }

    private static List<MemberDoc> Parse(XDocument doc)
    {
        var members = new List<MemberDoc>();
        var root = doc.Root;
        if (root is null) return members;
        var membersEl = root.Element("members");
        if (membersEl is null) return members;

        foreach (var m in membersEl.Elements("member"))
        {
            var cref = (string?)m.Attribute("name") ?? string.Empty;
            if (string.IsNullOrEmpty(cref)) continue;

            members.Add(new MemberDoc(
                Cref: cref,
                Kind: ParseKind(cref),
                Summary: GetInnerText(m.Element("summary")),
                Params: m.Elements("param")
                    .Select(p => new ParamDoc(
                        (string?)p.Attribute("name") ?? string.Empty,
                        GetInnerText(p)))
                    .ToList(),
                Returns: GetInnerText(m.Element("returns")),
                Remarks: GetInnerText(m.Element("remarks")),
                Examples: m.Elements("example").Select(GetInnerText).ToList(),
                // `<caveat>` is the spec 041 §7.1.2 custom tag. Authors haven't
                // adopted it yet; an empty list is the steady state until they do.
                Caveats: m.Elements("caveat").Select(GetInnerText).ToList(),
                SeeAlsos: m.Elements("seealso")
                    .Select(s => (string?)s.Attribute("cref") ?? string.Empty)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList()));
        }
        return members;
    }

    private static MemberKind ParseKind(string cref) => cref.Length < 2 || cref[1] != ':' ? MemberKind.Unknown
        : cref[0] switch
        {
            'T' => MemberKind.Type,
            'M' => MemberKind.Method,
            'P' => MemberKind.Property,
            'F' => MemberKind.Field,
            'E' => MemberKind.Event,
            _ => MemberKind.Unknown,
        };

    /// <summary>
    /// Concatenate child node text, preserving inline elements (e.g.
    /// <c>&lt;see cref="..."/&gt;</c>) as raw markup so post-processing can
    /// rewrite them. Whitespace inside the element is trimmed at the leaf
    /// and re-joined with single spaces (the Roslyn convention).
    /// </summary>
    internal static string GetInnerText(XElement? el)
    {
        if (el is null) return string.Empty;
        var raw = string.Concat(el.Nodes().Select(n => n switch
        {
            XText t => t.Value,
            XElement e => e.ToString(SaveOptions.DisableFormatting),
            _ => string.Empty,
        }));
        // Trim each line and join with newlines so authored multi-line
        // summaries stay multi-line in MD output. Outer whitespace stripped.
        var lines = raw.Replace("\r\n", "\n").Split('\n');
        var trimmed = lines.Select(l => l.Trim()).ToArray();
        return string.Join("\n", trimmed).Trim();
    }
}
