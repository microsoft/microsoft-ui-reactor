using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Reactor.Analyzers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Proves that <see cref="AgentKitDocGateTests"/> is measuring something.
/// </summary>
/// <remarks>
/// <para>
/// Both facts in that class pass with almost nothing to report — two exempted counterexamples and
/// zero wrapper workarounds. A walker whose parser stopped matching, a factory map that resolved to
/// nothing, or an emptied <see cref="NoOpModifierAnalyzer.ElementReplacements"/> would all read
/// exactly the same way from the outside. A no-match is not a measurement until a positive control
/// shows the same probe, through the same code path, can still match.
/// </para>
/// <para>
/// So: two known violations replayed through <see cref="AgentKitSnippetWalker.Scan"/>, and floors
/// under the corpus size, the resolution counts, and the tables the rules are derived from.
/// </para>
/// </remarks>
public class AgentKitDocGateInstrumentTests
{
    /// <summary>
    /// <c>skills/design.md</c> as it stood at <c>43a751b7^</c> — the canonical window shell #1119
    /// rewrote, verbatim.
    /// </summary>
    /// <remarks>
    /// Pinned as a literal rather than read from git so it cannot rot with the working tree. This
    /// is the sample that shipped contradicting <c>reactor-design/SKILL.md</c>: a <c>Border</c>
    /// wrapping a <c>FlexColumn</c> for no reason but to carry <c>.Padding(24)</c>. Note that
    /// <c>REACTOR_MOD_003</c> is silent on it — <c>Border</c> is inside <c>Padding</c>'s gate —
    /// which is precisely why the second rule has to exist.
    /// </remarks>
    private const string Pre1119AppShell = """
        ReactorApp.Run<App>("MyApp", width: 900, height: 600);

        class App : Component
        {
            public override Element Render()
            {
                var titleBar = TitleBar("MyApp").Flex(shrink: 0);

                var body = Border(
                    FlexColumn(/* page content */) with { RowGap = 16 }
                ).Padding(24).Flex(grow: 1, basis: 0);

                return FlexColumn(titleBar, body)
                    .Backdrop(BackdropKind.Mica);
            }
        }
        """;

    [Fact]
    public void Walker_Reports_The_Pre1119_Border_Workaround()
    {
        var scan = AgentKitSnippetWalker.Scan(new[]
        {
            new AgentKitSnippet("fixture/pre-1119-design.md", 1, Pre1119AppShell),
        });

        var finding = Assert.Single(scan.Of(AgentKitFindingKind.WrapperWorkaround));

        Assert.Equal("Padding", finding.Modifier);
        Assert.Equal("FlexElement", finding.ElementName);
        Assert.Equal("FlexPadding", finding.Replacement);

        // Line 11 of the fixture is `).Padding(24).Flex(grow: 1, basis: 0);` — the modifier itself,
        // which is where a reader looks; line 9 is `var body = Border(`, where the chain starts and
        // where a `// Wrong:` marker would sit. Pinning both keeps the span→line arithmetic honest:
        // an off-by-one would send every real failure message to a line nobody can find, and
        // nothing else in the suite would notice.
        Assert.Equal(11, finding.Line);
        Assert.Equal(9, finding.ChainStartLine);

        // The sibling rule must stay quiet on this input. If it ever fires here, the two rules have
        // started overlapping and this fixture has stopped proving what it claims to.
        Assert.Empty(scan.Of(AgentKitFindingKind.DroppedModifier));
    }

    [Fact]
    public void Walker_Reports_A_Modifier_Its_Receiver_Drops()
    {
        var scan = AgentKitSnippetWalker.Scan(new[]
        {
            new AgentKitSnippet("fixture/dropped.md", 1, "FlexColumn(children).Padding(16)"),
        });

        var finding = Assert.Single(scan.Of(AgentKitFindingKind.DroppedModifier));

        Assert.Equal("Padding", finding.Modifier);
        Assert.Equal("FlexElement", finding.ElementName);
        Assert.Equal("FlexPadding", finding.Replacement);
        Assert.Contains("FlexPanel", finding.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The receivers the gate admits must stay admitted. Without this, a walker that reported
    /// <em>everything</em> would still pass the two positive controls above while making the real
    /// facts fail for the wrong reason.
    /// </summary>
    [Theory]
    [InlineData("Border(child).Padding(16)")]
    [InlineData("VStack(8, items).Padding(16)")]
    [InlineData("Button(\"Go\").Padding(12)")]
    [InlineData("TextBlock(\"Hello\").Padding(8)")]
    [InlineData("FlexColumn(children).FlexPadding(16)")]
    [InlineData("FlexRow(items).FlexPadding(horizontal: 16, vertical: 8)")]
    [InlineData("VStack(8, FlexColumn(children), Button(\"Go\")).Padding(16)")]
    [InlineData("Border(FlexColumn(children)).Padding(16).Background(Theme.CardBackground)")]
    public void Walker_Stays_Silent_On_Sound_Code(string snippet)
    {
        var scan = AgentKitSnippetWalker.Scan(new[] { new AgentKitSnippet("fixture/sound.md", 1, snippet) });

        Assert.Empty(scan.Findings);
    }

    /// <summary>
    /// Floors over the corpus and the reflection behind it. Each one turns a silent collapse — a
    /// glob that stopped matching, a factory map that resolved to nothing, a fence parser that
    /// stopped recognising ```` ```csharp ```` — into a failure.
    /// </summary>
    /// <remarks>
    /// The thresholds are set well under the measured values (63 documents, 374 snippets, 345
    /// resolved chains, 115 factories at the time of writing) because they are guards against a
    /// mechanism breaking, not pins on the corpus. Raise one only if it stops discriminating; do
    /// <b>not</b> lower one to make a red build green, because the collapse it is describing is
    /// exactly the failure it exists to report.
    /// </remarks>
    [Fact]
    public void The_Corpus_And_Its_Resolution_Are_Not_Empty()
    {
        var documents = AgentKitCorpus.Documents;
        var snippets = AgentKitCorpus.Snippets;
        var scan = AgentKitCorpus.Scan;

        Assert.True(
            documents.Count >= 50,
            $"Only {documents.Count} agent-kit documents were discovered from Reactor.csproj; " +
            "expected 50+. The agentkit/ <None> globs have probably stopped resolving, which would " +
            "make every fact in AgentKitDocGateTests pass over an empty corpus.");

        Assert.True(
            snippets.Count >= 150,
            $"Only {snippets.Count} C# snippets were extracted from {documents.Count} documents; " +
            "expected 150+. The fence parser has probably stopped matching ```csharp blocks.");

        Assert.True(
            scan.ResolvedChains >= 100,
            $"Only {scan.ResolvedChains} modifier chains resolved to a known element across the " +
            "corpus; expected 100+. Factory or element reflection has probably stopped resolving, " +
            "in which case both gates are inspecting nothing.");

        Assert.True(
            ReactorSurface.Instance.ResolvableFactoryCount >= 100,
            $"Only {ReactorSurface.Instance.ResolvableFactoryCount} DSL factory names resolved to a " +
            "single element type; expected 100+.");

        // The wrapper rule is generated from this table. Emptying it would silence that fact
        // entirely, and nothing downstream would say so.
        Assert.NotEmpty(NoOpModifierAnalyzer.ElementReplacements);
        Assert.Contains(
            "Microsoft.UI.Reactor.Core.FlexElement|Padding",
            NoOpModifierAnalyzer.ElementReplacements.Keys);
    }

    /// <summary>
    /// Every <c>agentkit/</c> glob must match at least one file, and the corpus must contain the
    /// three documents #1119 had to fix by hand.
    /// </summary>
    /// <remarks>
    /// A folder renamed without updating the csproj stops shipping silently — the package simply
    /// loses a skill. That is a packaging defect in its own right, and it is also the quietest way
    /// for this gate's coverage to shrink.
    /// </remarks>
    [Fact]
    public void Every_AgentKit_Pack_Glob_Still_Matches_Files()
    {
        var entries = AgentKitCorpus.PackEntries;

        var empty = entries
            .Where(entry => entry.Files.Count == 0)
            .Select(entry => $"{entry.Include} → {entry.PackagePath}")
            .ToList();

        Assert.True(
            empty.Count == 0,
            "An agentkit/ pack item matches no file on disk, so the NuGet ships without it and this " +
            "gate never inspects it:\n  " + string.Join("\n  ", empty));

        var documents = AgentKitCorpus.Documents;

        foreach (var required in new[]
                 {
                     "skills/design.md",
                     "plugins/reactor/skills/reactor-design/SKILL.md",
                     "plugins/reactor/skills/reactor-build-and-check/SKILL.md",
                 })
        {
            Assert.True(
                documents.Contains(required, StringComparer.OrdinalIgnoreCase),
                $"{required} is no longer in the packed agent kit. It is one of the three documents " +
                "#1119 had to reconcile by hand; if it genuinely stopped shipping, remove it here " +
                "deliberately rather than leaving this gate pointed at a file nobody receives.");
        }
    }

    /// <summary>
    /// The fence reader must respect CommonMark's same-character, at-least-as-long closing rule,
    /// and must report the body's real starting line.
    /// </summary>
    /// <remarks>
    /// Both properties are load-bearing and neither is exercised by the corpus today: getting the
    /// close wrong would swallow the rest of a document into one snippet (findings reported at
    /// wildly wrong lines), and getting the line wrong would point every failure at the wrong place.
    /// </remarks>
    [Fact]
    public void Fence_Extraction_Handles_Nesting_And_Reports_Real_Line_Numbers()
    {
        const string Markdown = """
            # Heading

            ```csharp
            FlexColumn(children).FlexPadding(16)
            ```

            ```text
            not C#
            ```

            ````csharp
            // a longer fence survives an inner ``` run
            ```
            Border(child).Padding(8)
            ````
            """;

        var snippets = AgentKitDocCorpus.ExtractFences("fixture/fences.md", Markdown);

        Assert.Equal(2, snippets.Count);

        Assert.Equal(4, snippets[0].StartLine);
        Assert.Equal("FlexColumn(children).FlexPadding(16)", snippets[0].Text);

        Assert.Equal(12, snippets[1].StartLine);
        Assert.Contains("Border(child).Padding(8)", snippets[1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("not C#", snippets[1].Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The corpus really is read from the working tree, not from a stale copy under
    /// <c>bin/</c>.
    /// </summary>
    [Fact]
    public void The_Corpus_Is_Read_From_The_Working_Tree()
    {
        var repoRoot = AgentKitCorpus.RepoRoot;

        Assert.True(
            File.Exists(Path.Combine(repoRoot, "src", "Reactor", "Reactor.csproj")),
            $"Repo root resolved to {repoRoot}, which has no src/Reactor/Reactor.csproj.");

        var design = Path.Combine(repoRoot, "skills", "design.md");
        Assert.True(File.Exists(design), $"skills/design.md not found at {design}");
    }
}
