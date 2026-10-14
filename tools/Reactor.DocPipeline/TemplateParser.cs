namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// Tier label declared via the <c>tier:</c> front-matter field; controls
/// the §11 tier-lint checklist applied during validation. Public so the
/// validator's per-tier rule set can be exposed from test fixtures.
/// </summary>
public enum DocTier
{
    Stub,
    Solid,
    Comprehensive,
}

internal class DocTemplate
{
    public string Title { get; set; } = "";
    public string App { get; set; } = "";

    /// <summary>
    /// Sequential nav order. Held as <c>double</c> so spec-041 §7.2 `.5`
    /// slots (e.g. <c>thinking-in-reactor: 1.5</c>) can slot in between
    /// existing integer orders without renumbering churn until the Phase-4
    /// rebase to integers.
    /// </summary>
    public double Order { get; set; }
    public string Audience { get; set; } = "";
    public string Goal { get; set; } = "";
    public DocTier Tier { get; set; } = DocTier.Solid;

    /// <summary>
    /// True when the template explicitly declared a <c>tier:</c> field.
    /// Pages without a declared tier still parse successfully (default
    /// Solid) but the validator treats them as info-only.
    /// </summary>
    public bool TierDeclared { get; set; }

    /// <summary>Optional <c>winui-ref:</c> URL; empty if not declared.</summary>
    public string WinUiRef { get; set; } = "";

    /// <summary>
    /// Extra concept names this page owns beyond its title. Spec 041 §4.5
    /// cross-link analyzer (<c>REACTOR_DOC_XLINK_001</c>) uses the union of
    /// <see cref="Title"/> + this list when scanning prose for missed links.
    /// Declared in front-matter as comma- or list-separated values, e.g.
    /// <c>concept-aliases: "Trampoline, Cross-thread dispatch"</c>.
    /// </summary>
    public List<string> ConceptAliases { get; set; } = [];

    public string Body { get; set; } = "";
    public List<LockedSection> LockedSections { get; set; } = [];
    public List<CaveatSection> Caveats { get; set; } = [];
    public string FilePath { get; set; } = "";
}

internal class LockedSection
{
    public string Content { get; set; } = "";
}

internal class CaveatSection
{
    public string Content { get; set; } = "";
}

/// <summary>
/// Parses <c>.md.dt</c> doc templates — YAML front-matter + Markdown body with directives.
/// </summary>
internal static class TemplateParser
{
    internal const string CaveatOpenTag = "<!-- ai:caveat -->";
    internal const string CaveatCloseTag = "<!-- /ai:caveat -->";
    internal const string LockOpenTag = "<!-- ai:lock -->";
    internal const string LockCloseTag = "<!-- /ai:lock -->";

    public static DocTemplate Parse(string templatePath)
    {
        var content = File.ReadAllText(templatePath);
        return ParseContent(content, templatePath);
    }

    /// <summary>
    /// Parses raw template <paramref name="content"/>. Exposed for unit tests
    /// so they can author fixtures inline rather than on disk.
    /// </summary>
    internal static DocTemplate ParseContent(string content, string templatePath = "")
    {
        var template = new DocTemplate { FilePath = templatePath };

        // Extract YAML front-matter between --- delimiters
        if (content.StartsWith("---"))
        {
            var endIndex = content.IndexOf("\n---", 3);
            if (endIndex > 0)
            {
                var frontMatter = content[3..endIndex].Trim();
                ParseFrontMatter(frontMatter, template);
                // Skip past the closing --- and any immediately following newline
                var bodyStart = endIndex + 4; // "\n---" = 4 chars
                if (bodyStart < content.Length && content[bodyStart] == '\r') bodyStart++;
                if (bodyStart < content.Length && content[bodyStart] == '\n') bodyStart++;
                template.Body = bodyStart < content.Length ? content[bodyStart..] : "";
            }
        }
        else
        {
            template.Body = content;
        }

        ExtractLockedSections(template);
        ExtractCaveatSections(template);

        // Prepend winui-ref callout to the body so generated output carries it
        // at the top of the page (front-matter is stripped from output).
        if (!string.IsNullOrEmpty(template.WinUiRef))
        {
            var callout = BuildWinUiRefCallout(template.WinUiRef);
            template.Body = callout + "\n\n" + template.Body.TrimStart('\r', '\n');
        }

        return template;
    }

    /// <summary>
    /// Build the styled WinUI-reference blockquote. Host name is derived from
    /// the URL's last non-empty path segment, title-cased; if no path segment
    /// can be extracted, the URL itself is the link text.
    /// </summary>
    internal static string BuildWinUiRefCallout(string url)
    {
        var label = DeriveHostName(url);
        return $"> **WinUI reference:** For the full property surface and design guidance, see [{label}]({url}).";
    }

    private static string DeriveHostName(string url)
    {
        try
        {
            var uri = new Uri(url, UriKind.Absolute);
            var segments = uri.Segments
                .Select(s => s.Trim('/').Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
            if (segments.Count == 0) return uri.Host;
            var last = segments[^1];
            // Drop any extension (e.g. .html)
            var dot = last.LastIndexOf('.');
            if (dot > 0) last = last[..dot];
            // Replace separators with spaces and title-case
            last = last.Replace('-', ' ').Replace('_', ' ');
            var parts = last.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => char.ToUpperInvariant(p[0]) + p[1..]);
            return string.Join(' ', parts);
        }
        catch
        {
            return url;
        }
    }

    private static void ParseFrontMatter(string yaml, DocTemplate template)
    {
        var lines = yaml.Split('\n');
        string? currentKey = null;
        var multiLineValue = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            // Multi-line continuation (indented)
            if (currentKey != null && (line.StartsWith("  ") || line.StartsWith("\t")))
            {
                multiLineValue.Add(line.TrimStart());
                continue;
            }

            // Flush previous multi-line value
            if (currentKey != null && multiLineValue.Count > 0)
            {
                SetField(template, currentKey, string.Join('\n', multiLineValue));
                currentKey = null;
                multiLineValue.Clear();
            }

            var colonIdx = line.IndexOf(':');
            if (colonIdx < 0) continue;

            var key = line[..colonIdx].Trim();
            var value = line[(colonIdx + 1)..].Trim();

            if (value is "|" or ">")
            {
                currentKey = key;
                multiLineValue.Clear();
            }
            else
            {
                value = value.Trim('"').Trim('\'');
                SetField(template, key, value);
            }
        }

        if (currentKey != null && multiLineValue.Count > 0)
            SetField(template, currentKey, string.Join('\n', multiLineValue));
    }

    private static void SetField(DocTemplate template, string key, string value)
    {
        switch (key.ToLowerInvariant())
        {
            case "title": template.Title = value; break;
            case "app": template.App = value; break;
            case "order":
                // Spec 041 §7.2: new pages slot in as `.5` between existing
                // integer orders until the Phase-4 rebase, so we parse as
                // double rather than int. Invariant culture so `1.5` works
                // regardless of the machine locale.
                if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var o))
                    template.Order = o;
                break;
            case "audience": template.Audience = value; break;
            case "goal": template.Goal = value; break;
            case "tier":
                template.Tier = ParseTier(value, template.FilePath);
                template.TierDeclared = true;
                break;
            case "winui-ref": template.WinUiRef = value; break;
            case "concept-aliases":
                template.ConceptAliases = ParseAliasList(value);
                break;
        }
    }

    /// <summary>
    /// Parse a comma- or YAML-flow-list-shaped aliases value into a trimmed
    /// non-empty list. Accepts <c>"A, B, C"</c>, <c>[A, B, C]</c>, or a
    /// single token. Spec 041 §4.5.
    /// </summary>
    internal static List<string> ParseAliasList(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        var s = raw.Trim();
        if (s.StartsWith('[') && s.EndsWith(']')) s = s[1..^1];
        return s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Trim('"').Trim('\'').Trim())
            .Where(p => p.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Parse the <c>tier:</c> value, throwing a <see cref="DocPipelineException"/>
    /// for unknown literals. Empty values fall back to the default (Solid)
    /// since they're indistinguishable from a missing declaration.
    /// </summary>
    internal static DocTier ParseTier(string value, string filePath = "")
    {
        if (string.IsNullOrWhiteSpace(value)) return DocTier.Solid;
        return value.Trim().ToLowerInvariant() switch
        {
            "stub" => DocTier.Stub,
            "solid" => DocTier.Solid,
            "comprehensive" => DocTier.Comprehensive,
            _ => throw new DocPipelineException(
                "REACTOR_DOC_TIER_VALUE",
                $"{filePath}: unknown tier '{value}'. Expected one of: stub, solid, comprehensive."),
        };
    }

    private static void ExtractLockedSections(DocTemplate template)
    {
        var body = template.Body;
        var idx = 0;

        while (true)
        {
            var openIdx = body.IndexOf(LockOpenTag, idx, StringComparison.OrdinalIgnoreCase);
            if (openIdx < 0) break;

            var closeIdx = body.IndexOf(LockCloseTag, openIdx + LockOpenTag.Length, StringComparison.OrdinalIgnoreCase);
            if (closeIdx < 0) break;

            var contentStart = openIdx + LockOpenTag.Length;
            var content = body[contentStart..closeIdx].Trim();
            template.LockedSections.Add(new LockedSection { Content = content });

            idx = closeIdx + LockCloseTag.Length;
        }
    }

    /// <summary>
    /// Extract <c>&lt;!-- ai:caveat --&gt;</c>...<c>&lt;!-- /ai:caveat --&gt;</c>
    /// blocks and replace them in-body with the rendered "**Caveat:**"-led
    /// blockquote. Unclosed blocks raise <see cref="DocPipelineException"/>
    /// so a malformed template fails compile rather than silently dropping
    /// content.
    /// </summary>
    private static void ExtractCaveatSections(DocTemplate template)
    {
        var body = template.Body;
        var result = new System.Text.StringBuilder(body.Length);
        var idx = 0;

        while (idx < body.Length)
        {
            var openIdx = body.IndexOf(CaveatOpenTag, idx, StringComparison.OrdinalIgnoreCase);
            if (openIdx < 0)
            {
                result.Append(body, idx, body.Length - idx);
                break;
            }

            // Append everything up to the open tag
            result.Append(body, idx, openIdx - idx);

            var contentStart = openIdx + CaveatOpenTag.Length;
            var closeIdx = body.IndexOf(CaveatCloseTag, contentStart, StringComparison.OrdinalIgnoreCase);
            if (closeIdx < 0)
            {
                throw new DocPipelineException(
                    "REACTOR_DOC_CAVEAT_001",
                    $"{template.FilePath}: <!-- ai:caveat --> opened without a matching <!-- /ai:caveat -->.");
            }

            var content = body[contentStart..closeIdx].Trim();
            template.Caveats.Add(new CaveatSection { Content = content });
            result.Append(RenderCaveat(content));

            idx = closeIdx + CaveatCloseTag.Length;
        }

        template.Body = result.ToString();
    }

    /// <summary>
    /// Render a caveat block as a Markdown blockquote led by
    /// <c>**Caveat:**</c>. Each line of the body becomes a quoted line.
    /// </summary>
    internal static string RenderCaveat(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var sb = new System.Text.StringBuilder();
        sb.Append("> **Caveat:** ");
        for (int i = 0; i < lines.Length; i++)
        {
            if (i == 0)
            {
                sb.Append(lines[i].TrimStart());
            }
            else
            {
                sb.Append('\n');
                sb.Append("> ");
                sb.Append(lines[i]);
            }
        }
        return sb.ToString();
    }
}
