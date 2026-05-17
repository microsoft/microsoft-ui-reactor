using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// Extracts code snippets from C# source files using <c>// &lt;snippet:id&gt;</c> markers.
/// Supports nested snippets: the outer snippet includes inner snippet code but not markers.
/// </summary>
internal static partial class SnippetExtractor
{
    internal record Snippet(string Id, string FullId, string Code, string SourceFile, int StartLine);

    public static Dictionary<string, Snippet> ExtractFromApp(string appDir, string topicId)
    {
        var snippets = new Dictionary<string, Snippet>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(appDir, "*.cs", SearchOption.AllDirectories))
        {
            // Skip obj/bin directories
            var relativePath = Path.GetRelativePath(appDir, file);
            if (relativePath.StartsWith("obj" + Path.DirectorySeparatorChar) || relativePath.StartsWith("bin" + Path.DirectorySeparatorChar)) continue;

            ExtractFromFile(file, topicId, snippets);
        }
        return snippets;
    }

    /// <summary>
    /// Recognize the spec §10.2 <c>source:&lt;path&gt;#&lt;region&gt;</c> form
    /// in a snippet reference. Returns <c>true</c> if the input is a source
    /// reference; the path is relative to <paramref name="repoRoot"/> (under
    /// <c>src/</c> by convention) and the region is the identifier captured
    /// in <c>// &lt;snippet:region&gt;</c> markers.
    /// </summary>
    public static bool TryParseSourceReference(string snippetId, out string path, out string region)
    {
        path = "";
        region = "";
        const string prefix = "source:";
        if (!snippetId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var rest = snippetId[prefix.Length..];
        var hashIdx = rest.IndexOf('#');
        if (hashIdx < 0) return false;
        path = rest[..hashIdx].Trim();
        region = rest[(hashIdx + 1)..].Trim();
        if (path.Length == 0 || region.Length == 0) return false;
        return true;
    }

    /// <summary>
    /// Resolve a <c>source:&lt;path&gt;#&lt;region&gt;</c> snippet by walking
    /// the file at <paramref name="repoRoot"/>/<paramref name="path"/> and
    /// extracting the body between matching <c>&lt;snippet:&lt;region&gt;&gt;</c>
    /// and <c>&lt;/snippet:&lt;region&gt;&gt;</c> markers. Recognizes any
    /// line-comment style: <c>//</c>, <c>&lt;!-- ... --&gt;</c>, or a leading
    /// single quote.
    /// </summary>
    /// <exception cref="DocPipelineException">
    /// Raises <c>REACTOR_DOC_SNIPPET_001</c> when the file is missing,
    /// <c>_002</c> when no open marker is found, <c>_003</c> when an open
    /// marker has no matching close, and <c>_004</c> when a nested region
    /// with the same name is detected.
    /// </exception>
    public static Snippet ExtractFromSource(string repoRoot, string path, string region)
    {
        var full = Path.IsPathRooted(path) ? path : Path.Combine(repoRoot, path);
        if (!File.Exists(full))
            throw new DocPipelineException(
                "REACTOR_DOC_SNIPPET_001",
                $"source snippet file not found: {path}");

        var lines = File.ReadAllLines(full);
        var openPattern = OpenMarkerPattern(region);
        var closePattern = CloseMarkerPattern(region);

        int startLine = -1;
        int endLine = -1;
        var content = new List<string>();
        var nestedOpen = false;

        for (int i = 0; i < lines.Length; i++)
        {
            if (openPattern.IsMatch(lines[i]))
            {
                if (startLine < 0)
                {
                    startLine = i + 1;
                    continue;
                }
                // A second open before the first closes is a nested region
                // of the same name — error per spec.
                nestedOpen = true;
                break;
            }

            if (closePattern.IsMatch(lines[i]))
            {
                if (startLine < 0) continue; // close before any open: ignore (treat as missing open)
                endLine = i;
                break;
            }

            if (startLine >= 0)
                content.Add(lines[i]);
        }

        if (nestedOpen)
            throw new DocPipelineException(
                "REACTOR_DOC_SNIPPET_004",
                $"nested region '{region}' with the same name inside {path}");

        if (startLine < 0)
            throw new DocPipelineException(
                "REACTOR_DOC_SNIPPET_002",
                $"region '{region}' not found in {path}");

        if (endLine < 0)
            throw new DocPipelineException(
                "REACTOR_DOC_SNIPPET_003",
                $"region '{region}' opened in {path} without a matching close marker");

        var code = TrimCommonIndentation(content);
        var fullId = $"source:{path}#{region}";
        return new Snippet(region, fullId, code, full, startLine);
    }

    // Match any of:
    //   // <snippet:region>
    //   <!-- <snippet:region> -->     (also <!-- snippet:region -->)
    //   ' <snippet:region>
    // We accept the marker token bracketed or unbracketed inside HTML-style
    // comments because hand-authored markdown / xml templates use both.
    private static Regex OpenMarkerPattern(string region) =>
        new(@"(?ix)
              ^\s*
              (?:
                  //              # C# line comment
                | <!--            # HTML comment open
                | '               # VB-style line comment
              )
              \s*
              <? \s* snippet : " + Regex.Escape(region) + @" \s* >?
              \s* (?:-->)?
              \s*$",
            RegexOptions.Compiled);

    private static Regex CloseMarkerPattern(string region) =>
        new(@"(?ix)
              ^\s*
              (?:
                  //
                | <!--
                | '
              )
              \s*
              <? \s* / \s* snippet : " + Regex.Escape(region) + @" \s* >?
              \s* (?:-->)?
              \s*$",
            RegexOptions.Compiled);

    private static void ExtractFromFile(string filePath, string topicId, Dictionary<string, Snippet> results)
    {
        var lines = File.ReadAllLines(filePath);
        var activeSnippets = new Dictionary<string, (List<string> lines, int startLine)>();
        var openStack = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();

            // Opening marker: // <snippet:id>
            if (trimmed.StartsWith("// <snippet:") && trimmed.EndsWith(">") && !trimmed.Contains("/snippet:"))
            {
                var id = trimmed["// <snippet:".Length..^1];
                openStack.Add(id);
                activeSnippets[id] = (new List<string>(), i + 1);
                continue;
            }

            // Closing marker: // </snippet:id>
            if (trimmed.StartsWith("// </snippet:") && trimmed.EndsWith(">"))
            {
                var id = trimmed["// </snippet:".Length..^1];
                openStack.Remove(id);

                if (activeSnippets.TryGetValue(id, out var data))
                {
                    var code = TrimCommonIndentation(data.lines);
                    var fullId = $"{topicId}/{id}";
                    results[fullId] = new Snippet(id, fullId, code, filePath, data.startLine);
                }
                continue;
            }

            // Add line to all currently open snippets
            foreach (var id in openStack)
            {
                activeSnippets[id].lines.Add(lines[i]);
            }
        }

        // Warn about unclosed snippets
        foreach (var id in openStack)
        {
            Console.Error.WriteLine($"  ⚠ Unclosed snippet '{id}' in {filePath}");
        }
    }

    private static string TrimCommonIndentation(List<string> lines)
    {
        if (lines.Count == 0) return "";

        // Remove leading and trailing blank lines
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
            lines.RemoveAt(0);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            lines.RemoveAt(lines.Count - 1);

        if (lines.Count == 0) return "";

        // Find minimum indentation among non-empty lines
        var nonEmpty = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (nonEmpty.Count == 0) return "";

        var minIndent = nonEmpty.Min(l => l.Length - l.TrimStart().Length);

        return string.Join('\n', lines.Select(l =>
            string.IsNullOrWhiteSpace(l) ? "" : (l.Length > minIndent ? l[minIndent..] : l.TrimStart())
        ));
    }
}
