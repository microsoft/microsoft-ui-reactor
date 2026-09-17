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
    /// Accepting arbitrary indentation before a bare fence slightly over-reads: four-space text
    /// outside any list is an indented code block, so a literal <c>```csharp</c> there is content,
    /// not a fence. That direction is the safe one — it inspects an extra snippet rather than
    /// skipping a real one — and the corpus contains no such construct today. A leading <em>list
    /// marker</em> is different: it is container syntax, and four columns of indent make it literal
    /// text rather than a list (<c>Md4cCommonMarkSpecTest.Example_0288</c>), so the marker is
    /// validated against its own container below instead of being taken from the line alone.
    /// </para>
    /// <para>
    /// One blockquote run then at most one list marker is the deliberate boundary. CommonMark
    /// nests containers arbitrarily, so <c>- &gt; ```csharp</c> is a legal opener this does not
    /// match. <see cref="CSharpFenceProbe"/> does not match it either, which is what makes the miss
    /// safe rather than silent in the dangerous direction: the two agree, so the completeness fact
    /// still means what it says, and the cost is an unscanned block rather than a finding on prose.
    /// The shipped corpus contains no such opener — measured, with the single-container forms as
    /// positive controls — so parsing containers recursively would guard nothing today.
    /// </para>
    /// </remarks>
    private static readonly Regex FenceOpen = new(
        @"^(?<indent>[ \t]*)((?<marker>[-*+]|\d+[.)])(?<pad>[ \t]+))?(?<fence>`{3,}|~{3,})[ ]*(?<info>[^`\r\n]*)$",
        RegexOptions.Compiled);

    /// <summary>
    /// A blockquote container prefix: any run of <c>&gt;</c> markers with optional spacing.
    /// </summary>
    /// <summary>
    /// A single blockquote level: up to three columns of indent, a <c>&gt;</c>, and optional space.
    /// </summary>
    /// <remarks>
    /// Four columns is an indented code block, not a container: the repository's own CommonMark
    /// conformance fixture pins <c>    &gt; # Foo</c> as literal <c>&gt; # Foo</c> text
    /// (<c>Md4cCommonMarkSpecTest.Example_0230</c>). Accepting any leading whitespace stripped that
    /// prefix and scanned the literal as a real fence, so a gated modifier quoted inside one would
    /// have failed the gate on text that is not a sample. <see cref="CSharpFenceProbe"/> shares the
    /// limit, or it would corroborate the same misclassification. Zero instances in the corpus
    /// today, measured; this closes the hole before one lands.
    /// </remarks>
    private static readonly Regex BlockquoteLevel = new(@"^[ ]{0,3}>[ \t]?", RegexOptions.Compiled);

    /// <summary>
    /// Splits a line into its blockquote container prefix and the content inside it, removing at
    /// most <paramref name="maxDepth"/> levels.
    /// </summary>
    /// <remarks>
    /// The depth bound is what keeps a nested quote from closing an outer fence. Stripping every
    /// level reduced <c>&gt; &gt; ```</c> — a legitimate nested-blockquote line inside a
    /// <c>&gt; ```csharp</c> block — to a bare <c>```</c>, which closed the block early and left
    /// the rest of the sample unscanned. The differential probe computes its covered lines from the
    /// same scan, so the truncation would have stayed green.
    /// </remarks>
    internal static (int PrefixLength, string Content) StripBlockquote(string line, int maxDepth = int.MaxValue)
    {
        var offset = 0;

        for (var level = 0; level < maxDepth; level++)
        {
            var match = BlockquoteLevel.Match(line[offset..]);
            if (!match.Success || match.Length == 0)
                break;

            offset += match.Length;
        }

        return (offset, line[offset..]);
    }

    /// <summary>Number of blockquote levels a line opens with.</summary>
    internal static int BlockquoteDepth(string line)
    {
        var offset = 0;
        var depth = 0;

        while (true)
        {
            var match = BlockquoteLevel.Match(line[offset..]);
            if (!match.Success || match.Length == 0)
                return depth;

            offset += match.Length;
            depth++;
        }
    }

    /// <summary>
    /// An opening C# fence, found with no regard for indentation or for surrounding block
    /// structure — the independent probe
    /// <c>AgentKitDocGateInstrumentTests.Every_CSharp_Fence_In_The_Corpus_Is_Extracted</c> measures
    /// <see cref="ExtractFences"/> against.
    /// </summary>
    /// <remarks>
    /// Deliberately a different mechanism from <see cref="FenceOpen"/> rather than a second call to
    /// it: two derivations of the same number only corroborate each other when they can fail
    /// independently. The structural rules it does share are the container limits: a list marker
    /// carries at most four columns of padding and sits at most three columns past its container,
    /// and a blockquote prefix likewise — beyond those a line is indented code rather than a
    /// container. A probe that accepted what the scanner rejects would report literal text as an
    /// unscanned sample and fail the completeness fact on prose.
    /// </remarks>
    internal static readonly Regex CSharpFenceProbe = new(
        @"^(?:[ ]{0,3}(?:>[ \t]?)+)?(?:[ ]{0,3}([-*+]|\d+[.)])[ \t]{1,4}|[ ]*)(`{3,}|~{3,})[ ]*(csharp|cs|c\#)([ \t,][^\r\n]*)?$",
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
            .Select(e => (
                Path: (string?)e.Attribute("PackagePath"),
                Include: (string?)e.Attribute("Include"),
                Pack: (string?)e.Attribute("Pack")))
            .Where(item => item.Path is not null && item.Include is not null
                           && IsPacked(item.Pack) && IsAgentKitPath(item.Path))
            .Select(item => (item.Path!, item.Include!))
            .ToList();

    /// <summary>
    /// True when an item is actually shipped, i.e. carries <c>Pack="true"</c>.
    /// </summary>
    /// <remarks>
    /// A <c>&lt;None&gt;</c> item is not packed unless it opts in, so an <c>agentkit/</c>
    /// <c>PackagePath</c> alone does not mean the file reaches a consumer. Without this,
    /// <c>&lt;None Include="draft.md" Pack="false" PackagePath="agentkit/" /&gt;</c> would be
    /// scanned and could fail the gate over a document nobody receives — breaking the one property
    /// this corpus is built on, that it is the same list the pack target consumes.
    /// </remarks>
    private static bool IsPacked(string? pack) =>
        pack is not null && pack.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

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

            if (!IsPacked((string?)none.Attribute("Pack")) || !IsAgentKitPath(packagePath))
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
    internal readonly record struct FenceRegion(int OpenLine, int BodyStartLine, int BodyEndLine, string Language, int Indent, int BlockquoteDepth);

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
            var depth = BlockquoteDepth(lines[i]);
            var content = StripBlockquote(lines[i]).Content;

            var open = FenceOpen.Match(content);
            if (!open.Success)
                continue;

            var indent = VisualWidth(open.Groups["indent"].Value);

            // A fence may open as the content of a list item on the marker's own line
            // (`- ```csharp`). The marker establishes the content column, so measure — and strip —
            // against that rather than the line's leading whitespace. The column follows the same
            // padding rule as an ordinary item: five or more spaces after the marker put content
            // one space along and make the rest indented code, so `-     ```csharp` is literal
            // text, not a fence. Treating all the padding as list padding scanned it as a sample
            // and could fail the gate on prose.
            var inlineMarker = open.Groups["marker"];

            // A marker four columns past its own container is indented code, not list syntax, so
            // `    - ```csharp` at top level is literal text. Deriving the content column from the
            // line alone read it as a list item and scanned the literal as a sample, which can fail
            // the gate on quoted prose. Validate the marker against its container first.
            if (inlineMarker.Success && indent - ContainerIndent(lines, i) > 3)
                continue;

            var container = inlineMarker.Success
                ? ContentColumn(open)
                : ContainerIndent(lines, i);

            // Where the fence characters actually start. Equal to the line's indentation unless a
            // marker precedes them, and what the three-column limit and body de-indentation are
            // both measured with.
            var fenceColumn = inlineMarker.Success
                ? indent + inlineMarker.Length + VisualWidth(open.Groups["pad"].Value, indent + inlineMarker.Length)
                : indent;

            if (fenceColumn - container > 3)
                continue;

            var fenceChar = open.Groups["fence"].Value[0];
            var fenceLength = open.Groups["fence"].Value.Length;

            // Find the close first, so a non-C# fence still advances past its own body instead
            // of letting the body's contents be re-read as markdown.
            var close = i + 1;
            var containerExited = false;

            while (close < lines.Length)
            {
                // A blockquoted fence also ends when its container does. Stripping *up to* the
                // opening depth without requiring that depth to still be present let an unclosed
                // `> ```text` swallow a following top-level ```csharp block, which then shipped
                // unscanned — and the completeness fact derives its covered set from this same
                // scan, so it could not have seen it. Blank lines do not end it; they are routinely
                // written without the marker.
                if (depth > 0
                    && lines[close].Trim().Length > 0
                    && BlockquoteDepth(lines[close]) < depth)
                {
                    containerExited = true;
                    break;
                }

                if (IsClosingFence(lines[close], fenceChar, fenceLength, container, depth))
                    break;

                // A fence inside a list item ends when that item does, for the same reason the
                // blockquote rule above exists: an unclosed indented ```text under a bullet
                // otherwise swallowed the next top-level ```csharp block, which then shipped
                // unscanned — and the completeness fact derives its covered set from this same
                // scan, so it could not have reported the gap. A non-blank line left of the item's
                // content column has left the container. Checked after the close so a properly
                // terminated block is never truncated by it.
                //
                // Only when the fence itself sits at or past that column. `ContainerIndent` reports
                // the nearest list above the line, which for a block that has already de-indented
                // out of the list is a container it is not in; applying the rule there ended the
                // region on its own first body line and emptied it.
                if (container > 0 && fenceColumn >= container)
                {
                    var outside = StripBlockquote(lines[close], depth).Content;
                    if (outside.Trim().Length > 0 && LeadingWidth(outside) < container)
                    {
                        containerExited = true;
                        break;
                    }
                }

                close++;
            }

            regions.Add(new FenceRegion(
                OpenLine: i + 1,
                BodyStartLine: i + 2,
                BodyEndLine: close,          // 1-based last body line; == i+1 when empty.
                // Info strings separate the language from its attributes with any whitespace, not
                // just a space: `csharp\tlinenos` is a C# block. Splitting on spaces alone read the
                // whole run as the language, so the body never reached the gate — and the probe
                // accepted only spaces too, so the completeness fact stayed green over it.
                Language: open.Groups["info"].Value.Trim().Split(' ', '\t', ',')[0],
                // Body lines have the opening fence's own offset removed, which for a fence opened
                // on a list marker's line is the column the fence characters start at, not the
                // line's leading whitespace.
                Indent: fenceColumn,
                BlockquoteDepth: depth));

            // On container exit the terminating line belongs to whatever follows, so leave it for
            // the outer loop to reconsider rather than consuming it as a closing fence.
            i = containerExited ? close - 1 : close;
        }

        return regions;
    }

    /// <summary>A markdown list item marker: <c>- </c>, <c>* </c>, <c>+ </c> or <c>10. </c>.</summary>
    private static readonly Regex ListItemMarker = new(
        @"^(?<indent>[ \t]*)(?<marker>[-*+]|\d+[.)])(?<pad>[ \t]+)",
        RegexOptions.Compiled);

    /// <summary>
    /// Visual width of a run of leading whitespace, with tabs advancing to four-column tab stops.
    /// </summary>
    /// <remarks>
    /// The block rules this scanner implements are stated in columns, and CommonMark advances a tab
    /// to the next multiple of four (<c>Md4cFixtures/spec.txt</c>, "Tabs"). Counting one column per
    /// character made a single leading tab measure 1, so a tab-indented <c>```csharp</c> at top
    /// level read as a fence when it is really an indented code block — the gate could then fail on
    /// quoted, code-like prose. Every indentation measurement here goes through this.
    /// </remarks>
    /// <remarks>
    /// The width a tab contributes depends on where it starts, so a run measured mid-line must say
    /// where: in <c>10.\t```csharp</c> the tab advances from column 3 to 4 and is worth one column,
    /// not four. Measuring it from zero put the content column at 7 and read the block's own body
    /// as having left the list.
    /// </remarks>
    private static int VisualWidth(string whitespace, int startColumn = 0)
    {
        var column = startColumn;

        foreach (var c in whitespace)
            column = c == '\t' ? (column / 4 * 4) + 4 : column + 1;

        return column - startColumn;
    }

    /// <summary>
    /// Width of a line's leading whitespace in columns.
    /// </summary>
    private static int LeadingWidth(string line) =>
        VisualWidth(line[..(line.Length - line.TrimStart(' ', '\t').Length)]);

    /// <summary>
    /// A line with any leading list marker removed, leaving the item's content.
    /// </summary>
    /// <remarks>
    /// The fence scanner accepts a fence opening on the marker's own line (<c>- ```csharp</c>), so
    /// anything else that has to recognise a fence must strip the marker the same way or the two
    /// disagree about the same document — which, in the counterexample walk, read the opener as
    /// code and failed a deliberately marked sample.
    /// </remarks>
    internal static string StripListMarker(string line)
    {
        var marker = ListItemMarker.Match(line);

        return marker.Success ? line[marker.Length..] : line;
    }

    /// <summary>
    /// The content column a list item establishes, per CommonMark's padding rule.
    /// </summary>
    /// <remarks>
    /// One to four spaces after the marker put the content at the marker's end plus that padding —
    /// so <c>-    item</c> has content column 5, not 2. Five or more means the item's content
    /// starts one space after the marker and the rest is indented code within it. Consuming a
    /// single whitespace character reported column 2 for that item and rejected a fence three
    /// spaces past its real content column as though it were literal.
    /// </remarks>
    private static int ContentColumn(Match marker)
    {
        var beforePad = VisualWidth(marker.Groups["indent"].Value) + marker.Groups["marker"].Length;
        var padding = VisualWidth(marker.Groups["pad"].Value, beforePad);

        return beforePad + (padding <= 4 ? padding : 1);
    }

    /// <summary>
    /// The content column of the innermost list item containing <paramref name="index"/>, or
    /// <c>0</c> at top level — the indentation a fence there is measured against.
    /// </summary>
    /// <remarks>
    /// CommonMark measures a fence's indent relative to its container, not absolutely. A fence
    /// inside <c>10. </c> legitimately sits at four spaces because that is the item's content
    /// column; the identical four spaces under <c>- </c> (content column 2) is two past the column
    /// and still fine, while six would be four past it and therefore literal indented code. Testing
    /// absolute indentation alone got both ends wrong: it dropped real blocks at top level and
    /// accepted literal ones inside shallow list items.
    /// </remarks>
    private static int ContainerIndent(string[] lines, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            var line = StripBlockquote(lines[i]).Content;
            if (line.Trim().Length == 0)
                continue;   // a blank line does not end a list item's content.

            var marker = ListItemMarker.Match(line);
            if (marker.Success)
                return ContentColumn(marker);

            // Only an unindented line closes the container. Comparing against a hard-coded 4 was
            // wrong because content columns are marker-dependent: in `- item` / `  explanation`,
            // the continuation sits at 2, so the old test read it as closing the list and measured
            // a following fence against column 0 — rejecting a fence that is three spaces past the
            // bullet's content column and therefore valid.
            var indent = line.Length - line.TrimStart(' ', '\t').Length;
            if (indent == 0)
                return 0;
        }

        return 0;
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
                        .Select(line => StripIndent(StripBlockquote(line, region.BlockquoteDepth).Content, region.Indent)))))
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

    /// <summary>
    /// True when the line closes a fence opened with <paramref name="openLength"/> run of
    /// <paramref name="fenceChar"/> inside a container at <paramref name="containerIndent"/>.
    /// </summary>
    /// <remarks>
    /// The indentation bound matters as much as the run length. Accepting any indentation let an
    /// indented <c>```</c> — literal text inside a C# sample, for instance in a raw string
    /// demonstrating Markdown — truncate the block, so the rest of that sample was never scanned.
    /// The differential probe trusts <see cref="Fences"/> for block structure, so it cannot see it.
    /// Measured in visual columns like every other indentation here: counting characters made a
    /// leading tab worth 1, so a tab-indented <c>```</c> closed a top-level block three columns
    /// early and everything after it in that sample went unscanned.
    /// </remarks>
    private static bool IsClosingFence(string rawLine, char fenceChar, int openLength, int containerIndent, int blockquoteDepth)
    {
        var line = StripBlockquote(rawLine, blockquoteDepth).Content;
        var text = line.TrimStart(' ', '\t');

        if (VisualWidth(line[..(line.Length - text.Length)]) - containerIndent > 3)
            return false;

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
