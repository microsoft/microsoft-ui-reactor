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
/// One <c>&lt;None&gt;</c> item from <c>src/Reactor/Reactor.csproj</c> that packs into
/// <c>agentkit/</c>, resolved to the files it actually matches on disk.
/// </summary>
internal sealed record AgentKitPackEntry(string Include, string PackagePath, IReadOnlyList<string> Files);

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
    /// Opening fence: at least three backticks or tildes, then an optional info string. The
    /// closing fence must use the same character and be at least as long, per CommonMark, which
    /// is what keeps a nested <c>```</c> inside a longer fence from ending the block early.
    /// </summary>
    private static readonly Regex FenceOpen = new(
        @"^(?<indent>[ ]{0,3})(?<fence>`{3,}|~{3,})[ ]*(?<info>[^`\r\n]*)$",
        RegexOptions.Compiled);

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

            if (packagePath is null || include is null
                || !packagePath.StartsWith(AgentKitPrefix, StringComparison.Ordinal))
                continue;

            var files = new List<string>();

            foreach (var pattern in include.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var file in Expand(projectDirectory, pattern.Trim()))
                    files.Add(file);
            }

            entries.Add(new AgentKitPackEntry(
                include,
                packagePath,
                files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.Ordinal).ToList()));
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
    /// Pulls the C# fenced blocks out of one markdown document.
    /// </summary>
    /// <remarks>
    /// Hand-rolled rather than routed through the repo's markdown parser: this needs the
    /// <em>source line number</em> of each fence so a finding can be reported at
    /// <c>path:line</c>, and it must run on documents that are not otherwise parsed at build
    /// time. The rules implemented are the two that matter for fence extraction — same fence
    /// character, closing run at least as long as the opening run.
    /// </remarks>
    internal static IReadOnlyList<AgentKitSnippet> ExtractFences(string path, string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var snippets = new List<AgentKitSnippet>();

        for (var i = 0; i < lines.Length; i++)
        {
            var open = FenceOpen.Match(lines[i]);
            if (!open.Success)
                continue;

            var fenceChar = open.Groups["fence"].Value[0];
            var fenceLength = open.Groups["fence"].Value.Length;
            var language = open.Groups["info"].Value.Trim().Split(' ', ',')[0];

            // Find the close first, so a non-C# fence still advances past its own body instead
            // of letting the body's contents be re-read as markdown.
            var close = i + 1;
            while (close < lines.Length && !IsClosingFence(lines[close], fenceChar, fenceLength))
                close++;

            if (CSharpFenceLanguages.Contains(language, StringComparer.OrdinalIgnoreCase) && close > i + 1)
            {
                snippets.Add(new AgentKitSnippet(
                    path,
                    i + 2,  // 1-based, and the body starts on the line after the fence.
                    string.Join("\n", lines[(i + 1)..close])));
            }

            i = close;
        }

        return snippets;
    }

    private static bool IsClosingFence(string line, char fenceChar, int openLength)
    {
        var text = line.TrimStart(' ');
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
/// Reading 63 documents and parsing 374 snippets is not free, and every fact over the agent kit
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
