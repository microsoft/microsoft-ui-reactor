using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;

/// <summary>
/// Where a cref points once ref-gen has routed everything: the page that
/// documents it, the display text to use in prose, and — when the page
/// carries a heading for that specific member — the in-page anchor.
/// </summary>
/// <param name="Route">The page the cref resolves to.</param>
/// <param name="DisplayName">Text for the generated link. For a direct hit
/// this is the page's short name; for a member resolved via its declaring
/// type it is the member's own name, which is what reads correctly in the
/// surrounding sentence.</param>
/// <param name="Anchor">Slug of the member's heading on that page, or
/// <c>null</c> when the page has no per-member heading to jump to.</param>
/// <param name="ViaDeclaringType">True when the cref had no page of its own
/// and was resolved through its declaring type.</param>
internal sealed record CrefTarget(
    RouterResult Route,
    string DisplayName,
    string? Anchor,
    bool ViaDeclaringType);

/// <summary>
/// Rewrites <c>&lt;see cref="..."/&gt;</c> and
/// <c>&lt;seealso cref="..."/&gt;</c> elements embedded in XML doc
/// strings into relative Markdown links pointed at the generated
/// reference pages. An unresolvable cref raises
/// <c>REACTOR_DOC_REFGEN_001</c> via <see cref="UnresolvedCrefException"/>.
/// </summary>
internal sealed class CrefResolver
{
    private readonly Dictionary<string, RouterResult> _byCref;
    private readonly Dictionary<string, string> _anchorsByCref;

    /// <param name="routedMembers">Every cref that has a page, mapped to it.
    /// Overloads all map to the same page.</param>
    /// <param name="anchorsByCref">Per-member heading slugs for pages that
    /// document more than one member. Optional.</param>
    public CrefResolver(
        IEnumerable<KeyValuePair<string, RouterResult>> routedMembers,
        IReadOnlyDictionary<string, string>? anchorsByCref = null)
    {
        _byCref = routedMembers.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        _anchorsByCref = anchorsByCref is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(anchorsByCref, StringComparer.Ordinal);
    }

    /// <summary>
    /// Resolve a cref to the routed target — useful for callers that need to
    /// emit links manually (e.g. the conceptual-guide link injector).
    /// </summary>
    public RouterResult? Resolve(string cref) => ResolveTarget(cref)?.Route;

    /// <summary>
    /// Resolve a cref to the page that documents it.
    ///
    /// Two passes. A direct hit wins. Failing that, a member cref
    /// (<c>M:</c>/<c>P:</c>/<c>F:</c>/<c>E:</c>) falls back to its declaring
    /// type's page: members frequently have no XML-doc entry of their own —
    /// most visibly the compiler-generated properties of a positional
    /// record, whose docs live on the primary constructor — yet the type
    /// that declares them is documented and routed. Sending the reader to
    /// the declaring type's page is strictly better than degrading to inline
    /// code, and it is where the member is actually described.
    /// </summary>
    public CrefTarget? ResolveTarget(string cref)
    {
        if (string.IsNullOrEmpty(cref)) return null;

        if (_byCref.TryGetValue(cref, out var direct))
        {
            _anchorsByCref.TryGetValue(cref, out var directAnchor);
            return new CrefTarget(direct, direct.ShortName, directAnchor, ViaDeclaringType: false);
        }

        var declaringCref = CrefSignature.DeclaringTypeCref(cref);
        if (declaringCref is not null && _byCref.TryGetValue(declaringCref, out var viaType))
        {
            _anchorsByCref.TryGetValue(cref, out var memberAnchor);
            var parts = CrefSignature.Parse(cref);
            var display = string.IsNullOrEmpty(parts.Name) ? viaType.ShortName : parts.Name;
            return new CrefTarget(viaType, display, memberAnchor, ViaDeclaringType: true);
        }

        return null;
    }

    public IReadOnlyDictionary<string, RouterResult> Routes => _byCref;

    /// <summary>
    /// Build the relative Markdown link (including any in-page anchor) from
    /// the page at <paramref name="fromPath"/> to <paramref name="target"/>.
    /// </summary>
    public static string LinkTo(CrefTarget target, string fromPath)
    {
        var fromDir = Path.GetDirectoryName(fromPath)?.Replace('\\', '/') ?? string.Empty;
        var link = MakeRelativeLink(fromDir, target.Route.RelativePath);
        return string.IsNullOrEmpty(target.Anchor) ? link : link + "#" + target.Anchor;
    }

    /// <summary>
    /// Rewrite all inline <c>&lt;see cref=&quot;...&quot;/&gt;</c> elements in the
    /// supplied XML-doc fragment to relative Markdown links pointing at the
    /// generated reference page for the target member. <paramref name="fromPath"/>
    /// is the relative-to-output-root path of the page being emitted; links
    /// are built relative to that page's directory.
    /// </summary>
    public string Rewrite(string xml, string fromPath, IList<string>? unresolved = null)
    {
        if (string.IsNullOrEmpty(xml)) return xml;

        // <see cref="X" /> or <seealso cref="X" /> — both produce inline links
        // in MD. (Block-level seealso under the dedicated section is handled
        // by ReferenceWriter; this rewrite covers the inline cases.)
        var rewritten = SeeCrefPattern.Replace(xml, m =>
        {
            var cref = m.Groups["cref"].Value;
            var target = ResolveTarget(cref);
            if (target is not null)
                return $"[{target.DisplayName}]({LinkTo(target, fromPath)})";

            // Author may have referenced a member outside the registry (e.g.
            // a System.* type). Render as inline code so the doc still reads
            // and record an unresolved entry so the caller can choose between
            // warn / fail.
            unresolved?.Add(cref);
            var name = ShortNameFallback(cref);
            return $"`{name}`";
        });

        // <paramref name="x"/> and <typeparamref name="T"/> carry no visible
        // text, so leaving them intact drops the word from the rendered
        // sentence entirely. Emit them as inline code.
        return ParamRefPattern.Replace(rewritten, m => $"`{m.Groups["name"].Value}`");
    }

    private static string MakeRelativeLink(string fromDir, string targetRelativePath)
    {
        // Build a POSIX relative path. The compiler runs on Windows; force
        // forward slashes so GitHub renders the link correctly.
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

    private static string ShortNameFallback(string cref)
    {
        var stem = cref;
        if (stem.Length >= 2 && stem[1] == ':') stem = stem[2..];
        var paren = stem.IndexOf('(');
        if (paren >= 0) stem = stem[..paren];
        var dot = stem.LastIndexOf('.');
        var name = dot >= 0 ? stem[(dot + 1)..] : stem;

        // Strip the CLR metadata arity suffix (`1, ``2) — rendering
        // `NavigationHandle`1` inside backticks both reads as noise and
        // terminates the code span early in Markdown.
        var tick = name.IndexOf('`');
        return tick >= 0 ? name[..tick] : name;
    }

    internal static readonly Regex ParamRefPattern = new(
        @"<(?:paramref|typeparamref)\s+name=""(?<name>[^""]+)""\s*/?>(?:\s*</(?:paramref|typeparamref)>)?",
        RegexOptions.Compiled);

    internal static readonly Regex SeeCrefPattern = new(
        @"<(?:see|seealso)\s+cref=""(?<cref>[^""]+)""\s*/?>(?:\s*</(?:see|seealso)>)?",
        RegexOptions.Compiled);
}
