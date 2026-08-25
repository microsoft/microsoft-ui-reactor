using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using Microsoft.UI.Reactor.Cli.Pack;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// One glob from a <c>&lt;None&gt;</c> item in <c>src/Reactor/Reactor.csproj</c> that packs into
/// <c>agentkit/</c>, resolved to the files it actually matches on disk.
/// </summary>
/// <remarks>
/// One entry per <em>pattern</em>, not per item. An item's <c>Include</c> may carry several
/// semicolon-separated globs — <c>skills\recipes\*.md;skills\recipes\*.cs</c> is packed that way —
/// and aggregating them would let a dead glob hide behind a live sibling: the item still matches
/// files, so an emptiness check on it stays false while half its content has stopped shipping.
/// </remarks>
internal sealed record AgentKitPackEntry(string Pattern, string PackagePath, IReadOnlyList<string> Files);

/// <summary>
/// One unit of C# taken from a shipped agent-kit document: a fenced block from a
/// <c>.md</c>, or the whole text of a packed <c>.cs</c>.
/// </summary>
/// <param name="Path">Repo-relative path, forward-slashed, so failures name the file a
/// reader can open.</param>
/// <param name="StartLine">1-based line in <paramref name="Path"/> that
/// <paramref name="Text"/> starts on, so a syntax offset can be turned back into a real
/// line number.</param>
internal sealed record AgentKitSnippet(string Path, int StartLine, string Text);

/// <summary>
/// The set of documents <c>Microsoft.UI.Reactor.nupkg</c> ships to consumers under
/// <c>agentkit/</c>, and the C# they contain.
/// </summary>
/// <remarks>
/// <para>
/// <b>Derived, never hardcoded.</b> The corpus is read out of the <c>&lt;None&gt;</c> items in
/// <c>src/Reactor/Reactor.csproj</c> whose <c>PackagePath</c> starts with <c>agentkit/</c>.
/// That is the same list the pack target consumes, so a skill folder added to the package is
/// covered by every fact built on this type the moment it is packed — which is the whole point
/// of issue #1121, whose complaint is that the <em>next</em> divergence would land unnoticed.
/// A hand-maintained path list would reproduce the defect one level up.
/// </para>
/// <para>
/// Only file reads happen here; nothing is compiled and no WinUI type is touched, so this is
/// safe in the headless <c>Reactor.Tests</c> host.
/// </para>
/// </remarks>
internal static class AgentKitDocCorpus
{
    /// <summary>The package path prefix that marks an item as shipped agent guidance.</summary>
    private const string AgentKitPrefix = "agentkit/";

    /// <summary>
    /// Fence languages that mean "this is Reactor C#". Markdown in this repo uses
    /// <c>```csharp</c> almost exclusively; <c>cs</c> is accepted because it is the other
    /// spelling a contributor reaches for and silently skipping it would shrink the corpus
    /// without saying so.
    /// </summary>
    private static readonly string[] CSharpFenceLanguages = { "csharp", "cs", "c#" };

    /// <summary>
    /// Opening fence: any leading whitespace, then at least three backticks or tildes, then an
    /// optional info string. The closing fence must use the same character and be at least as
    /// long, per CommonMark, which is what keeps a nested <c>```</c> inside a longer fence from
    /// ending the block early.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Indentation is not capped at 3.</b> CommonMark measures a fence's indent relative to its
    /// <em>container</em>, so a fence inside a numbered list item legitimately sits at four or more
    /// absolute spaces — <c>skills/design-docs/typography-and-colors.md</c> has two such blocks
    /// under list items 10 and 11. Capping at 3 silently dropped them from the corpus, and the
    /// aggregate snippet floor stayed green because the other 370-odd blocks more than covered it:
    /// a gate that inspects less than it claims, which is the failure mode issue #1121 exists to
    /// prevent. <c>Every_CSharp_Fence_In_The_Corpus_Is_Extracted</c> now measures this directly
    /// rather than trusting a floor.
    /// </para>
    /// <para>
    /// Accepting arbitrary indentation slightly over-reads: four-space text outside any list is an
    /// indented code block, so a literal <c>```csharp</c> there is content, not a fence. That
    /// direction is the safe one — it inspects an extra snippet rather than skipping a real one —
    /// and the corpus contains no such construct today.
    /// </para>
    /// </remarks>
    private static readonly Regex FenceOpen = new(
        @"^(?<indent>[ \t]*)(?<fence>`{3,}|~{3,})[ ]*(?<info>[^`\r\n]*)$",
        RegexOptions.Compiled);

    /// <summary>
    /// An opening C# fence, found with no regard for indentation or for surrounding block
    /// structure — the independent probe
    /// <c>AgentKitDocGateInstrumentTests.Every_CSharp_Fence_In_The_Corpus_Is_Extracted</c> measures
    /// <see cref="ExtractFences"/> against.
    /// </summary>
    /// <remarks>
    /// Deliberately a different mechanism from <see cref="FenceOpen"/> rather than a second call to
    /// it: two derivations of the same number only corroborate each other when they can fail
    /// independently.
    /// </remarks>
    internal static readonly Regex CSharpFenceProbe = new(
        @"^[ \t]*(`{3,}|~{3,})[ ]*(csharp|cs|c\#)[ ]*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// The <c>PackagePath</c> of every <c>&lt;None&gt;</c> item in a project that ships under
    /// <c>agentkit/</c> — the selection predicate <see cref="PackEntries"/> uses, exposed so it can
    /// be tested against project XML directly rather than only through the working tree.
    /// </summary>
    internal static IReadOnlyList<string> AgentKitPackagePaths(string projectXml) =>
        AgentKitItems(projectXml).Select(item => item.Path).ToList();

    /// <summary>
    /// The raw <c>Include</c> of every agentkit item, before it is split on <c>;</c>.
    /// </summary>
    internal static IReadOnlyList<string> AgentKitIncludes(string projectXml) =>
        AgentKitItems(projectXml).Select(item => item.Include).ToList();

    private static IEnumerable<(string Path, string Include)> AgentKitItems(string projectXml) =>
        XDocument.Parse(projectXml)
            .Descendants()
            .Where(e => e.Name.LocalName == "None")
            .Select(e => (Path: (string?)e.Attribute("PackagePath"), Include: (string?)e.Attribute("Include")))
            .Where(item => item.Path is not null && item.Include is not null && IsAgentKitPath(item.Path))
            .Select(item => (item.Path!, item.Include!))
            .ToList();

    /// <summary>
    /// True when an item's <c>PackagePath</c> puts it under <c>agentkit/</c>.
    /// </summary>
    /// <remarks>
    /// NuGet accepts either separator, and this very project uses backslashes elsewhere
    /// (<c>Reactor.csproj</c> packs <c>lib\$(TargetFramework)\Reactor\Hosting\</c>). An agentkit
    /// item written that way would ship and simply never enter this corpus — and because the entry
    /// is dropped whole, the unmatched-glob guard could not see the gap either. Normalising is what
    /// keeps "packed" and "inspected" the same set.
    /// </remarks>
    private static bool IsAgentKitPath(string packagePath) =>
        packagePath.Replace('\\', '/').StartsWith(AgentKitPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every <c>&lt;None&gt;</c> item packed under <c>agentkit/</c>, in declaration order, with
    /// its glob expanded against the working tree.
    /// </summary>
    /// <remarks>
    /// Entries whose glob matches nothing are returned with an empty <see cref="AgentKitPackEntry.Files"/>
    /// rather than dropped: a folder that was renamed without updating the csproj stops shipping
    /// silently, and a caller that never sees the entry cannot report it.
    /// </remarks>
    public static IReadOnlyList<AgentKitPackEntry> PackEntries(string repoRoot)
    {
        var projectDirectory = Path.Combine(repoRoot, "src", "Reactor");
        var project = XDocument.Load(Path.Combine(projectDirectory, "Reactor.csproj"));

        var entries = new List<AgentKitPackEntry>();

        foreach (var none in project.Descendants().Where(e => e.Name.LocalName == "None"))
        {
            var packagePath = (string?)none.Attribute("PackagePath");
            var include = (string?)none.Attribute("Include");

            if (packagePath is null || include is null)
                continue;

            if (!IsAgentKitPath(packagePath))
                continue;

            // One entry per pattern. Aggregating an item's globs would let a dead one hide behind a
            // live sibling: `skills\recipes\*.md;skills\recipes\*.cs` still matches files if only
            // the .cs half survives, so an item-level emptiness check would stay false while half
            // the recipes stopped shipping.
            foreach (var pattern in include.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = pattern.Trim();
                if (trimmed.Length == 0)
                    continue;

                entries.Add(new AgentKitPackEntry(
                    trimmed,
                    packagePath,
                    Expand(projectDirectory, trimmed)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(f => f, StringComparer.Ordinal)
                        .ToList()));
            }
        }

        return entries;
    }

    /// <summary>
    /// Every packed agent-kit file, repo-relative and forward-slashed.
    /// </summary>
    public static IReadOnlyList<string> Documents(string repoRoot) =>
        PackEntries(repoRoot)
            .SelectMany(entry => entry.Files)
            .Select(file => Relative(repoRoot, file))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Every unit of C# in the packed corpus: fenced blocks from <c>.md</c>, whole files from
    /// packed <c>.cs</c>.
    /// </summary>
    /// <remarks>
    /// Non-C# packed artifacts (<c>plugin.json</c>, <c>reactor.api.txt</c>) contribute nothing
    /// and are simply absent from the result — they carry no samples to be wrong about.
    /// </remarks>
    public static IReadOnlyList<AgentKitSnippet> Snippets(string repoRoot)
    {
        var snippets = new List<AgentKitSnippet>();

        foreach (var file in PackEntries(repoRoot).SelectMany(entry => entry.Files).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var extension = Path.GetExtension(file);
            var relative = Relative(repoRoot, file);

            if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase))
                snippets.AddRange(ExtractFences(relative, File.ReadAllText(file)));
            else if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
                snippets.Add(new AgentKitSnippet(relative, 1, File.ReadAllText(file)));
        }

        return snippets
            .OrderBy(s => s.Path, StringComparer.Ordinal)
            .ThenBy(s => s.StartLine)
            .ToList();
    }

    /// <summary>
    /// One fenced block found in a markdown document, C# or not.
    /// </summary>
    /// <param name="BodyStartLine">1-based first line of the body.</param>
    /// <param name="BodyEndLine">1-based last line of the body; less than
    /// <paramref name="BodyStartLine"/> for an empty block.</param>
    internal readonly record struct FenceRegion(int OpenLine, int BodyStartLine, int BodyEndLine, string Language, int Indent);

    /// <summary>
    /// Every fenced block in a markdown document, in source order, whatever its language.
    /// </summary>
    /// <remarks>
    /// The single fence scanner. <see cref="ExtractFences"/> filters this to C#, and the
    /// independent-probe fact uses the full list to know which lines are inside <em>some</em>
    /// block — a <c>```csharp</c> quoted as literal text inside a <c>```text</c> block is content,
    /// not a block the extractor skipped. Deriving both from one scan is what keeps them from
    /// disagreeing about where blocks begin and end.
    /// </remarks>
    internal static IReadOnlyList<FenceRegion> Fences(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var regions = new List<FenceRegion>();

        for (var i = 0; i < lines.Length; i++)
        {
            var open = FenceOpen.Match(lines[i]);
            if (!open.Success)
                continue;

            var fenceChar = open.Groups["fence"].Value[0];
            var fenceLength = open.Groups["fence"].Value.Length;

            // Find the close first, so a non-C# fence still advances past its own body instead
            // of letting the body's contents be re-read as markdown.
            var close = i + 1;
            while (close < lines.Length && !IsClosingFence(lines[close], fenceChar, fenceLength))
                close++;

            regions.Add(new FenceRegion(
                OpenLine: i + 1,
                BodyStartLine: i + 2,
                BodyEndLine: close,          // 1-based last body line; == i+1 when empty.
                Language: open.Groups["info"].Value.Trim().Split(' ', ',')[0],
                Indent: open.Groups["indent"].Value.Length));

            i = close;
        }

        return regions;
    }

    /// <summary>
    /// Pulls the C# fenced blocks out of one markdown document.
    /// </summary>
    /// <remarks>
    /// Hand-rolled rather than routed through the repo's markdown parser: this needs the
    /// <em>source line number</em> of each fence so a finding can be reported at
    /// <c>path:line</c>, and it must run on documents that are not otherwise parsed at build
    /// time. The rules implemented are the three that matter for fence extraction — same fence
    /// character, closing run at least as long as the opening run, and the opening fence's
    /// indentation stripped from the body.
    /// </remarks>
    internal static IReadOnlyList<AgentKitSnippet> ExtractFences(string path, string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        return Fences(markdown)
            .Where(region => CSharpFenceLanguages.Contains(region.Language, StringComparer.OrdinalIgnoreCase)
                             && region.BodyEndLine >= region.BodyStartLine)
            .Select(region => new AgentKitSnippet(
                path,
                region.BodyStartLine,
                string.Join(
                    "\n",
                    lines[(region.BodyStartLine - 1)..(region.BodyEndLine - 1 + 1)]
                        .Select(line => StripIndent(line, region.Indent)))))
            .ToList();
    }

    /// <summary>
    /// Removes up to <paramref name="indent"/> leading whitespace characters, which is how
    /// CommonMark un-nests a fenced block from its list container.
    /// </summary>
    /// <remarks>
    /// C# is whitespace-insensitive, so this changes no verdict — it exists so a reported snippet
    /// reads the way the document's author wrote it rather than carrying its list indentation.
    /// </remarks>
    private static string StripIndent(string line, int indent)
    {
        var strip = 0;
        while (strip < indent && strip < line.Length && (line[strip] == ' ' || line[strip] == '\t'))
            strip++;

        return line[strip..];
    }

    private static bool IsClosingFence(string line, char fenceChar, int openLength)
    {
        var text = line.TrimStart(' ', '\t');
        var run = 0;
        while (run < text.Length && text[run] == fenceChar)
            run++;

        return run >= openLength && text[run..].Trim().Length == 0;
    }

    /// <summary>
    /// Expands one MSBuild <c>Include</c> pattern relative to <c>src/Reactor</c>. The agent-kit
    /// items use only <c>dir\*.ext</c> and <c>dir\*</c> — no <c>**</c> — so this handles a
    /// literal path and a single-directory glob and deliberately nothing else: a pattern shape
    /// this does not understand returns nothing, and the "every glob matches a file" fact then
    /// fails loudly instead of quietly shrinking the corpus.
    /// </summary>
    private static IEnumerable<string> Expand(string projectDirectory, string pattern)
    {
        var normalized = pattern.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(projectDirectory, normalized));

        var fileName = Path.GetFileName(full);
        var directory = Path.GetDirectoryName(full);

        if (directory is null || !Directory.Exists(directory))
            return Array.Empty<string>();

        if (!fileName.Contains('*') && !fileName.Contains('?'))
            return File.Exists(full) ? new[] { full } : Array.Empty<string>();

        return Directory.EnumerateFiles(directory, fileName, SearchOption.TopDirectoryOnly);
    }

    private static string Relative(string repoRoot, string file) =>
        Path.GetRelativePath(repoRoot, file).Replace('\\', '/');

    /// <summary>Locates the repository root, failing with a usable message rather than a null deref.</summary>
    public static string RepoRoot()
    {
        var root = RepoRootFinder.FindRepoRoot();
        if (root is null)
            throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);

        return root;
    }
}

/// <summary>
/// The corpus and its scan, computed once per test process.
/// </summary>
/// <remarks>
/// <para>
/// Reading 63 documents and parsing 376 snippets is not free, and every fact over the agent kit
/// wants the same answer. Recomputing it per fact ran the scan three times concurrently — xUnit
/// runs test classes in parallel — and the resulting CPU spike intermittently blew the wall-clock
/// budget in <c>CompilationLoaderTests.Cold_load_under_500ms_warm_under_50ms_for_minimal_project</c>,
/// a test with nothing to do with any of this. Sharing one lazy scan removes the spike at its
/// source rather than widening someone else's budget to absorb it.
/// </para>
/// <para>
/// Caching is sound because nothing in the suite writes to the shipped documents; the corpus is
/// fixed for the life of the process.
/// </para>
/// </remarks>
internal static class AgentKitCorpus
{
    private static readonly Lazy<string> LazyRepoRoot =
        new(AgentKitDocCorpus.RepoRoot, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<IReadOnlyList<AgentKitPackEntry>> LazyPackEntries =
        new(() => AgentKitDocCorpus.PackEntries(RepoRoot), LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<IReadOnlyList<string>> LazyDocuments =
        new(() => AgentKitDocCorpus.Documents(RepoRoot), LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<IReadOnlyList<AgentKitSnippet>> LazySnippets =
        new(() => AgentKitDocCorpus.Snippets(RepoRoot), LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<AgentKitScan> LazyScan =
        new(() => AgentKitSnippetWalker.Scan(Snippets), LazyThreadSafetyMode.ExecutionAndPublication);

    public static string RepoRoot => LazyRepoRoot.Value;

    public static IReadOnlyList<AgentKitPackEntry> PackEntries => LazyPackEntries.Value;

    public static IReadOnlyList<string> Documents => LazyDocuments.Value;

    public static IReadOnlyList<AgentKitSnippet> Snippets => LazySnippets.Value;

    public static AgentKitScan Scan => LazyScan.Value;
}
