using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;

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

    public CrefResolver(IEnumerable<KeyValuePair<string, RouterResult>> routedMembers)
    {
        _byCref = routedMembers.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Resolve a cref to the routed target — useful for callers that need to
    /// emit links manually (e.g. the conceptual-guide link injector).
    /// </summary>
    public RouterResult? Resolve(string cref) =>
        _byCref.TryGetValue(cref, out var r) ? r : null;

    public IReadOnlyDictionary<string, RouterResult> Routes => _byCref;

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
        var fromDir = Path.GetDirectoryName(fromPath)?.Replace('\\', '/') ?? string.Empty;

        // <see cref="X" /> or <seealso cref="X" /> — both produce inline links
        // in MD. (Block-level seealso under the dedicated section is handled
        // by ReferenceWriter; this rewrite covers the inline cases.)
        return SeeCrefPattern.Replace(xml, m =>
        {
            var cref = m.Groups["cref"].Value;
            if (_byCref.TryGetValue(cref, out var target))
            {
                var link = MakeRelativeLink(fromDir, target.RelativePath);
                return $"[{target.ShortName}]({link})";
            }
            // Author may have referenced a member outside the registry (e.g.
            // a System.* type). Render as inline code so the doc still reads
            // and record an unresolved entry so the caller can choose between
            // warn / fail.
            unresolved?.Add(cref);
            var name = ShortNameFallback(cref);
            return $"`{name}`";
        });
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
        return dot >= 0 ? stem[(dot + 1)..] : stem;
    }

    internal static readonly Regex SeeCrefPattern = new(
        @"<(?:see|seealso)\s+cref=""(?<cref>[^""]+)""\s*/?>(?:\s*</(?:see|seealso)>)?",
        RegexOptions.Compiled);
}
