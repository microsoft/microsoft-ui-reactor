using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;

/// <summary>
/// Decomposition of a canonical XML-doc cref
/// (<c>M:Ns.Type.Method``1(System.Func{``0})</c>) into the pieces the
/// reference writer needs: declaring type, member name, generic arity and
/// the (still cref-encoded) parameter type list.
/// </summary>
/// <param name="Kind">The single-letter kind prefix — <c>T</c>, <c>M</c>,
/// <c>P</c>, <c>F</c>, <c>E</c> — or the empty string when the cref carried
/// no prefix.</param>
/// <param name="DeclaringName">For members, the declaring type's dotted name
/// including its own generic arity marker (<c>Ns.Options`2</c>). For types,
/// the containing namespace.</param>
/// <param name="Name">The bare member (or type) identifier with any arity
/// marker removed.</param>
/// <param name="GenericArity">Number of generic parameters declared by this
/// member (methods) or type.</param>
/// <param name="ParameterTypes">Cref-encoded parameter types in declaration
/// order. Empty when the member takes no parameters or declares no list.</param>
/// <param name="HasParameterList">Whether the cref carried a
/// <c>(...)</c> section at all — distinguishes <c>Clear()</c> from a
/// property.</param>
internal sealed record CrefParts(
    string Kind,
    string DeclaringName,
    string Name,
    int GenericArity,
    IReadOnlyList<string> ParameterTypes,
    bool HasParameterList);

/// <summary>
/// Renders canonical crefs as human-readable C#-flavoured signatures so a
/// reference page carrying several overloads can give each one a heading
/// that a reader recognises. Purely lexical — no Roslyn symbols involved,
/// because ref-gen only ever sees the XML doc file.
/// </summary>
internal static class CrefSignature
{
    /// <summary>
    /// Split a cref into its structural parts. Never throws: an
    /// unparseable cref degrades to a parts record whose <c>Name</c> is the
    /// whole input.
    /// </summary>
    public static CrefParts Parse(string cref)
    {
        var s = cref ?? string.Empty;
        var kind = string.Empty;
        if (s.Length >= 2 && s[1] == ':')
        {
            kind = s[..1];
            s = s[2..];
        }

        // Conversion operators encode the return type after '~'; it isn't
        // part of the member identity for routing purposes.
        var tilde = s.IndexOf('~');
        if (tilde >= 0) s = s[..tilde];

        IReadOnlyList<string> paramTypes = Array.Empty<string>();
        var hasList = false;
        var paren = s.IndexOf('(');
        if (paren >= 0)
        {
            var close = s.LastIndexOf(')');
            var inner = close > paren ? s[(paren + 1)..close] : s[(paren + 1)..];
            hasList = true;
            if (inner.Trim().Length > 0) paramTypes = SplitTopLevel(inner);
            s = s[..paren];
        }

        var arity = 0;
        var am = ArityPattern.Match(s);
        if (am.Success)
        {
            arity = int.Parse(am.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            s = s[..am.Index];
        }

        var declaring = string.Empty;
        var name = s;
        var dot = s.LastIndexOf('.');
        if (dot >= 0)
        {
            declaring = s[..dot];
            name = s[(dot + 1)..];
        }

        return new CrefParts(kind, declaring, name, arity, paramTypes, hasList);
    }

    /// <summary>
    /// The <c>T:</c> cref of the type that declares <paramref name="cref"/>,
    /// or <c>null</c> when the cref is itself a type (or has no declaring
    /// portion). Used by <see cref="CrefResolver"/> so a reference to a
    /// member that has no page of its own — notably a positional record's
    /// compiler-generated properties, which never appear in the XML doc file
    /// — still lands on the declaring type's page.
    /// </summary>
    public static string? DeclaringTypeCref(string cref)
    {
        var parts = Parse(cref);
        if (parts.Kind is "T" or "") return null;
        return string.IsNullOrEmpty(parts.DeclaringName) ? null : "T:" + parts.DeclaringName;
    }

    /// <summary>
    /// Render a display signature for <paramref name="member"/>, e.g.
    /// <c>UseEffect&lt;T1, T2&gt;(Func&lt;Action&gt;, T1, T2)</c>.
    ///
    /// Parameter <em>types</em> only — never names. The signature is a
    /// heading, and its slug is a linkable anchor: deriving it purely from
    /// the cref means it moves only when the API moves, so adding a
    /// <c>&lt;param&gt;</c> doc doesn't silently break inbound links.
    /// Parameter names and descriptions live in the section body.
    /// </summary>
    public static string Format(MemberDoc member, IReadOnlyList<string>? declaringTypeParams = null)
    {
        var parts = Parse(member.Cref);
        var methodTypeParams = member.TypeParams.Select(p => p.Name).ToList();
        var sb = new StringBuilder();

        var name = parts.Name;
        if (name.Equals("#ctor", StringComparison.Ordinal))
        {
            // A constructor's display name is the declaring type's name.
            var declDot = parts.DeclaringName.LastIndexOf('.');
            var declName = declDot >= 0 ? parts.DeclaringName[(declDot + 1)..] : parts.DeclaringName;
            name = StripArity(declName);
        }
        sb.Append(name);

        // `methodTypeParams` comes from <typeparam> tags, which are in XML
        // comment order and omit undocumented parameters — it is NOT indexed by
        // declaration ordinal, which is what ``N means. Documenting only the
        // second parameter would otherwise label ``0 with the second one's name.
        // Names are only trustworthy as a positional list when the documented
        // set is complete; short of that, fall back to placeholders for all of
        // them rather than emit a confidently wrong name.
        var namesAreOrdinal = methodTypeParams.Count == parts.GenericArity
            && methodTypeParams.All(n => !string.IsNullOrEmpty(n));

        var ownTypeParams = new List<string>(parts.GenericArity);
        for (int i = 0; i < parts.GenericArity; i++)
        {
            ownTypeParams.Add(namesAreOrdinal
                ? methodTypeParams[i]
                : "T" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (ownTypeParams.Count > 0)
            sb.Append('<').Append(string.Join(", ", ownTypeParams)).Append('>');

        if (parts.Kind is "T" or "P" or "F" or "E" && !parts.HasParameterList)
            return sb.ToString();

        var effectiveMethodParams = ownTypeParams.Count > 0 ? ownTypeParams : methodTypeParams;
        var rendered = parts.ParameterTypes
            .Select(t => FormatType(t, declaringTypeParams, effectiveMethodParams));
        sb.Append('(').Append(string.Join(", ", rendered)).Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// Convert a cref-encoded type reference to a readable form:
    /// <c>System.Func{System.Action}</c> → <c>Func&lt;Action&gt;</c>,
    /// <c>System.Nullable{System.TimeSpan}</c> → <c>TimeSpan?</c>,
    /// <c>``0</c> → the method's first type-parameter name.
    /// </summary>
    public static string FormatType(
        string type,
        IReadOnlyList<string>? typeParams,
        IReadOnlyList<string>? methodTypeParams)
    {
        var t = (type ?? string.Empty).Trim();
        if (t.Length == 0) return t;

        if (t[^1] == '@')
            return "ref " + FormatType(t[..^1], typeParams, methodTypeParams);

        if (t[^1] == ']')
        {
            var open = LastTopLevelBracket(t);
            if (open > 0)
            {
                var suffix = t[open..];
                var rank = suffix.Count(c => c == ',') + 1;
                var dims = rank == 1 ? "[]" : "[" + new string(',', rank - 1) + "]";
                return FormatType(t[..open], typeParams, methodTypeParams) + dims;
            }
        }

        var brace = t.IndexOf('{');
        if (brace > 0 && t[^1] == '}')
        {
            var outer = t[..brace];
            var args = SplitTopLevel(t[(brace + 1)..^1])
                .Select(a => FormatType(a, typeParams, methodTypeParams))
                .ToList();
            if (outer.Equals("System.Nullable", StringComparison.Ordinal) && args.Count == 1)
                return args[0] + "?";
            return StripArity(ShortTypeName(outer)) + "<" + string.Join(", ", args) + ">";
        }

        if (t.StartsWith("``", StringComparison.Ordinal) &&
            int.TryParse(t[2..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var mi))
        {
            return methodTypeParams is not null && mi < methodTypeParams.Count && !string.IsNullOrEmpty(methodTypeParams[mi])
                ? methodTypeParams[mi]
                : "T" + (mi + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (t.StartsWith("`", StringComparison.Ordinal) &&
            int.TryParse(t[1..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var ci))
        {
            return typeParams is not null && ci < typeParams.Count && !string.IsNullOrEmpty(typeParams[ci])
                ? typeParams[ci]
                : "T" + (ci + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return Alias(t);
    }

    /// <summary>
    /// GitHub-flavoured heading slug: lowercase, non-word characters
    /// dropped, spaces collapsed to hyphens. Matches the anchors GitHub
    /// generates for the <c>##</c> headings this generator emits.
    /// </summary>
    public static string Anchor(string heading)
    {
        var sb = new StringBuilder(heading.Length);
        foreach (var c in heading)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (c == ' ' || c == '-' || c == '_') sb.Append(c == ' ' ? '-' : c);
            // everything else (parens, angle brackets, commas, dots) is dropped
        }
        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Split a comma-separated cref fragment, ignoring commas nested inside
    /// generic-argument braces or array-rank brackets.
    /// </summary>
    internal static List<string> SplitTopLevel(string inner)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (int i = 0; i < inner.Length; i++)
        {
            var c = inner[i];
            if (c is '{' or '[' or '(') depth++;
            else if (c is '}' or ']' or ')') depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(inner[start..i]);
                start = i + 1;
            }
        }
        parts.Add(inner[start..]);
        return parts;
    }

    private static int LastTopLevelBracket(string t)
    {
        // Walk back from the closing ']' to its matching '['.
        var depth = 0;
        for (int i = t.Length - 1; i >= 0; i--)
        {
            if (t[i] == ']') depth++;
            else if (t[i] == '[')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static string ShortTypeName(string dotted)
    {
        var dot = dotted.LastIndexOf('.');
        return dot >= 0 ? dotted[(dot + 1)..] : dotted;
    }

    internal static string StripArity(string name) => ArityPattern.Replace(name, string.Empty);

    private static string Alias(string dotted) =>
        Aliases.TryGetValue(dotted, out var a) ? a : StripArity(ShortTypeName(dotted));

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["System.Boolean"] = "bool",
        ["System.Byte"] = "byte",
        ["System.SByte"] = "sbyte",
        ["System.Char"] = "char",
        ["System.Decimal"] = "decimal",
        ["System.Double"] = "double",
        ["System.Single"] = "float",
        ["System.Int16"] = "short",
        ["System.UInt16"] = "ushort",
        ["System.Int32"] = "int",
        ["System.UInt32"] = "uint",
        ["System.Int64"] = "long",
        ["System.UInt64"] = "ulong",
        ["System.Object"] = "object",
        ["System.String"] = "string",
        ["System.Void"] = "void",
    };

    /// <summary>
    /// Matches a trailing generic-arity marker: <c>`1</c> on types,
    /// <c>``2</c> on methods.
    /// </summary>
    private static readonly Regex ArityPattern = new(@"`{1,2}(\d+)$", RegexOptions.Compiled);
}
