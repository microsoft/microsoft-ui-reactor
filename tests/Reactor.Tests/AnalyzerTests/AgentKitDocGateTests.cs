using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.UI.Reactor.Analyzers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Gates the C# in the agent-kit documents <c>Microsoft.UI.Reactor.nupkg</c> ships against the
/// modifier rules the same package's analyzers enforce. Issue #1121.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect class.</b> <c>src/Reactor/Reactor.csproj</c> packs ~60 documents under
/// <c>agentkit/</c>. Nothing checked that they agree with the framework, so #1119 was able to ship
/// a rule in <c>reactor-design/SKILL.md</c> — "Flex containers take FlexPadding, NOT Padding […]
/// do not reach for a Border wrapper" — inside the same NuGet as two documents saying the
/// opposite. A consuming agent received the rule and its contradiction with no way to tell which
/// was current.
/// </para>
/// <para>
/// <b>Two facts, because one is not enough.</b> #1121 proposes running <c>REACTOR_MOD_003</c> over
/// the snippets. That is <see cref="Shipped_Snippets_Never_Apply_A_Modifier_Its_Receiver_Drops"/>,
/// and it is worth having — but it would <em>not</em> have caught #1119. The offending sample was
/// <c>Border(FlexColumn(…)).Padding(24)</c>, and <c>ModifierTable</c> gates <c>Padding</c> to
/// <c>Control/Border/Grid/StackPanel/RelativePanel/TextBlock</c>: <c>Border</c> is a legal
/// receiver, so the analyzer was correctly silent. What was wrong was that the sample demonstrated
/// the Border-wrapper workaround the new rule forbids. That is
/// <see cref="Shipped_Snippets_Never_Reach_For_A_Wrapper_Instead_Of_The_Replacement"/>.
/// </para>
/// <para>
/// <b>Why the corpus is derived.</b> The document list comes out of the csproj's <c>agentkit/</c>
/// <c>&lt;None&gt;</c> globs, not a list in this file. #1121's complaint is that the <em>next</em>
/// divergence lands unnoticed; a hand-maintained path list would reproduce that defect one level
/// up, in the gate itself.
/// </para>
/// <para>
/// <b>Non-vacuity.</b> Both facts pass with almost nothing to report today (two exempted
/// counterexamples, zero workarounds), which is indistinguishable from a broken walker unless
/// something proves the walker can still fire. <see cref="AgentKitDocGateInstrumentTests"/> is that
/// proof: it replays the pre-#1119 snippet and an unmarked violation through the same code path and
/// requires both to be reported, and it floors the corpus and resolution counts.
/// </para>
/// </remarks>
public class AgentKitDocGateTests
{
    /// <summary>
    /// The conventions this repo already uses to mark a deliberately-broken sample, surveyed across
    /// <c>skills/</c> and <c>plugins/</c>: <c>// Wrong:</c>, <c>// Bad:</c>, <c>// ❌ WRONG</c>,
    /// <c>### ❌ The anti-pattern …</c>, <c>// Never hardcode …</c>.
    /// </summary>
    internal static readonly Regex CounterexampleMarker = new(
        @"\b(wrong|bad|don'?t|do not|avoid|never|incorrect|anti-?pattern)\b|❌",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A bare URL, stripped from prose before the marker is matched against it.</summary>
    internal static readonly Regex Urls = new(@"\b\w+://\S+", RegexOptions.Compiled);

    /// <summary>
    /// No shipped sample may apply a common modifier to a receiver
    /// <c>Reconciler.ApplyModifiers</c> never writes it to — the documentation form of
    /// <c>REACTOR_MOD_003</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberate counterexamples are allowed, because the corpus contains two of them: #1119 added
    /// <c>// Wrong: no effect, and costs a build-check cycle</c> above
    /// <c>FlexColumn(children).Padding(16)</c> in both design documents, and a gate that failed on
    /// those would be deleted within the week. The exemption carries a condition — the document
    /// must also name the replacement — so "here is the broken form" can never ship without
    /// "here is what to write instead", which is the half of #1119 that was actually missing.
    /// </para>
    /// </remarks>
    [Fact]
    public void Shipped_Snippets_Never_Apply_A_Modifier_Its_Receiver_Drops()
    {
        var repoRoot = AgentKitCorpus.RepoRoot;
        var scan = AgentKitCorpus.Scan;
        var documents = new DocumentCache(repoRoot);

        var problems = new List<string>();

        foreach (var finding in scan.Of(AgentKitFindingKind.DroppedModifier))
        {
            if (!IsMarkedCounterexample(documents.Lines(finding.Path), finding))
            {
                problems.Add(
                    $"{finding.Path}:{finding.Line} — {finding.Detail}. " +
                    (finding.Replacement is { } fix
                        ? $"Use .{fix}(...) instead"
                        : "Apply the modifier to an element whose control supports it") +
                    ", or mark the line as a counterexample (`// Wrong: …`) if it is meant to " +
                    "demonstrate the mistake");
                continue;
            }

            if (finding.Replacement is { } replacement
                && !documents.Text(finding.Path).Contains(replacement, StringComparison.Ordinal))
            {
                problems.Add(
                    $"{finding.Path}:{finding.Line} — marked as a counterexample, but the document " +
                    $"never names the replacement `{replacement}`. A shipped 'do not write this' " +
                    "with no 'write this instead' is the half of #1119 that caused the contradiction");
            }
        }

        Assert.True(
            problems.Count == 0,
            "Shipped agent-kit C# applies a modifier that ApplyModifiers drops for that receiver. " +
            "These documents are packed into Microsoft.UI.Reactor.nupkg and read as guidance, so a " +
            "sample the framework's own analyzer would reject is advice to write broken code:\n  " +
            string.Join("\n  ", problems));
    }

    /// <summary>
    /// No shipped sample may introduce a wrapper element purely to receive a modifier the element
    /// it wraps silently drops, when a first-class replacement exists. This is the #1119 shape.
    /// </summary>
    /// <remarks>
    /// The rule is stated in prose by the very document that shipped alongside the violation —
    /// <c>reactor-design/SKILL.md</c>: "Don't add a Border solely to get padding; a Border is still
    /// right when you also need its background, corner radius, or border brush." This fact applies
    /// that rule to the package it ships in, parameterised off
    /// <see cref="NoOpModifierAnalyzer.ElementReplacements"/> so a second entry there extends it
    /// with no edit here.
    /// </remarks>
    [Fact]
    public void Shipped_Snippets_Never_Reach_For_A_Wrapper_Instead_Of_The_Replacement()
    {
        var repoRoot = AgentKitCorpus.RepoRoot;
        var scan = AgentKitCorpus.Scan;
        var documents = new DocumentCache(repoRoot);

        var problems = scan.Of(AgentKitFindingKind.WrapperWorkaround)
            .Where(finding => !IsMarkedCounterexample(documents.Lines(finding.Path), finding))
            .Select(finding => $"{finding.Path}:{finding.Line} — {finding.Detail}")
            .ToList();

        Assert.True(
            problems.Count == 0,
            "Shipped agent-kit C# demonstrates a wrapper workaround for a dropped modifier. This is " +
            "the exact contradiction #1119 fixed by hand: reactor-design/SKILL.md tells the reader " +
            "not to reach for a wrapper, while a sample packed beside it does:\n  " +
            string.Join("\n  ", problems));
    }

    /// <summary>
    /// True when the sample is marked as deliberately wrong — on the offending line itself, or in
    /// the comment block above the line the chain starts on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both lines are consulted because a multi-line chain puts its marker above the head:
    /// <c>// Wrong:</c> sits above <c>Border(</c>, while the modifier this fires on is three lines
    /// further down past a <c>)</c>. Anchoring only at the modifier would read that <c>)</c> as
    /// "real code above" and declare an obviously-marked counterexample unmarked.
    /// </para>
    /// <para>
    /// Only genuine <em>comment trivia</em> is considered, obtained by lexing the line with Roslyn
    /// rather than scanning for <c>//</c>. A textual search cannot tell a comment from a string:
    /// <c>FlexColumn(TextBlock("https://example/avoid")).Padding(16)</c> contains both <c>//</c>
    /// and the word "avoid", and would silently exempt itself. Reading the whole line has the same
    /// defect in a plainer form — <c>Button("Never")</c> would do it.
    /// </para>
    /// <para>
    /// The prose lines above a fence are matched with URLs stripped, for the same reason in the
    /// markdown direction: a link ending in <c>/avoid</c> is a citation, not an instruction to
    /// treat the block below it as wrong.
    /// </para>
    /// </remarks>
    internal static bool IsMarkedCounterexample(IReadOnlyList<string> lines, AgentKitFinding finding) =>
        IsMarkedAt(lines, finding.Line) || IsMarkedAt(lines, finding.ChainStartLine);

    internal static bool IsMarkedAt(IReadOnlyList<string> lines, int line)
    {
        var index = line - 1;
        if (index < 0 || index >= lines.Count)
            return false;

        if (CounterexampleMarker.IsMatch(CommentText(lines[index])))
            return true;

        // Walk up through the comment block, and — once past the fence — the markdown heading or
        // lead-in paragraph, which is where `### ❌ The anti-pattern that breaks everything` lives.
        for (var i = index - 1; i >= 0 && index - i <= 8; i--)
        {
            var text = lines[i].Trim();
            if (text.Length == 0)
                continue;

            var isComment = text.StartsWith("//", StringComparison.Ordinal);
            var isProse = text.StartsWith("```", StringComparison.Ordinal)
                          || text.StartsWith("~~~", StringComparison.Ordinal)
                          || text.StartsWith("#", StringComparison.Ordinal)
                          || text.StartsWith(">", StringComparison.Ordinal)
                          || text.StartsWith("-", StringComparison.Ordinal);

            if (!isComment && !isProse)
                return false;   // real code above: any marker up there belongs to that line.

            var candidate = isComment ? CommentText(text) : Urls.Replace(text, " ");
            if (CounterexampleMarker.IsMatch(candidate))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The comment trivia on one line of C#, concatenated — everything outside a comment removed.
    /// </summary>
    /// <remarks>
    /// Lexing is enough: comment trivia is recognised without the fragment having to parse, which
    /// matters because these lines are snippets pulled out of markdown with no enclosing class.
    /// </remarks>
    internal static string CommentText(string line)
    {
        if (!line.Contains("//", StringComparison.Ordinal) && !line.Contains("/*", StringComparison.Ordinal))
            return string.Empty;

        var trivia = CSharpSyntaxTree.ParseText(line)
            .GetRoot()
            .DescendantTrivia(descendIntoTrivia: true)
            .Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia))
            .Select(t => t.ToString());

        return string.Join(" ", trivia);
    }

    /// <summary>Reads each document once; a scan touches the same file for every finding in it.</summary>
    private sealed class DocumentCache(string repoRoot)
    {
        private readonly Dictionary<string, string[]> _lines = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _text = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> Lines(string path)
        {
            if (!_lines.TryGetValue(path, out var lines))
                _lines[path] = lines = Text(path).Replace("\r\n", "\n").Split('\n');

            return lines;
        }

        public string Text(string path)
        {
            if (!_text.TryGetValue(path, out var text))
            {
                _text[path] = text = File.ReadAllText(
                    Path.Combine(repoRoot, path.Replace('/', Path.DirectorySeparatorChar)));
            }

            return text;
        }
    }
}
