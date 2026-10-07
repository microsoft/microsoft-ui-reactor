using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;

/// <summary>
/// Maps a member (by cref) to the on-disk output path under
/// <c>docs/guide/reference/&lt;category&gt;/&lt;name&gt;.md</c>. Routing is
/// driven by the <see cref="ReferenceMap"/>; this type doesn't itself
/// hold doc-only metadata.
/// </summary>
internal sealed class MemberRouter
{
    private readonly ReferenceMap _map;
    private readonly string _referenceRoot;

    public MemberRouter(ReferenceMap map, string referenceRoot)
    {
        _map = map;
        _referenceRoot = referenceRoot;
    }

    /// <summary>
    /// Resolve a member to its category and on-disk MD path. Returns
    /// <c>null</c> for members the registry doesn't recognise — the
    /// caller decides whether to emit <c>REACTOR_DOC_REGISTRY_W001</c>.
    /// </summary>
    public RouterResult? Route(MemberDoc member)
    {
        var match = _map.Resolve(member.Cref);
        if (match is null) return null;
        if (string.IsNullOrEmpty(match.Category)) return null;

        var name = ShortName(member);
        var filename = Sanitize(name) + ".md";
        var relativePath = Path.Combine("reference", match.Category, filename);
        var fullPath = Path.Combine(_referenceRoot, "reference", match.Category, filename);
        return new RouterResult(match.Category, name, fullPath, relativePath, match.GuidePages);
    }

    /// <summary>
    /// Extract the human-readable "short name" of a member: the trailing
    /// identifier for types, or the method/property/etc name for members.
    /// Method overloads collapse to the same short name; the caller is
    /// responsible for detecting collisions.
    /// </summary>
    internal static string ShortName(MemberDoc member)
    {
        var stem = StripKind(member.Cref);
        // Drop parameter list for methods.
        var paren = stem.IndexOf('(');
        if (paren >= 0) stem = stem[..paren];
        // Drop generic arity (``N).
        stem = Regex.Replace(stem, @"`+\d+$", "");
        var dot = stem.LastIndexOf('.');
        var name = dot >= 0 ? stem[(dot + 1)..] : stem;
        // For types, the dot-split yields the bare type name. For methods,
        // it yields the method identifier — the type prefix is dropped by
        // design so output paths are flat per-category.
        return name;
    }

    private static string StripKind(string cref) =>
        cref.Length >= 2 && cref[1] == ':' ? cref[2..] : cref;

    private static string Sanitize(string name)
    {
        // Trim characters illegal on Windows filesystems and on URL paths.
        // Reactor's API surface uses ordinary identifiers, so this is mostly
        // a safety net for `#ctor` and the like.
        var invalid = Path.GetInvalidFileNameChars();
        var span = new char[name.Length];
        int idx = 0;
        foreach (var c in name)
        {
            if (Array.IndexOf(invalid, c) >= 0 || c == '#' || c == '`')
                span[idx++] = '_';
            else
                span[idx++] = c;
        }
        return new string(span, 0, idx);
    }
}

internal sealed record RouterResult(
    string Category,
    string ShortName,
    string AbsolutePath,
    string RelativePath,
    IReadOnlyList<string> GuidePages);
