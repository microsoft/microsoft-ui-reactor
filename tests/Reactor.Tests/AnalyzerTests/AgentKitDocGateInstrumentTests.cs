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
    // A load-bearing single-child wrapper. Deleting the ScrollViewer deletes scrolling, so this is
    // correct code and the wrapper rule must not claim otherwise. (A Viewbox would not do as the
    // example here: it mounts a Viewbox, which is outside Padding's gate, so the dropped-modifier
    // rule fires on it for a different and entirely correct reason.)
    [InlineData("ScrollViewer(FlexColumn(children)).Padding(16)")]
    public void Walker_Stays_Silent_On_Sound_Code(string snippet)
    {
        var scan = AgentKitSnippetWalker.Scan(new[] { new AgentKitSnippet("fixture/sound.md", 1, snippet) });

        Assert.Empty(scan.Findings);
    }

    /// <summary>
    /// A constant-null argument must not produce a finding, because
    /// <c>NoOpModifierAnalyzer.HasConstantNullArgument</c> returns before reporting on exactly this
    /// shape.
    /// </summary>
    /// <remarks>
    /// A documentation gate that is stricter than the packaged analyzer produces findings a reader
    /// cannot act on: the sample is correct, the analyzer agrees it is correct, and the only remedy
    /// on offer is to delete the gate. This keeps the two aligned in the direction that matters.
    /// </remarks>
    [Theory]
    [InlineData("Rectangle().Background((Brush)null)")]
    [InlineData("Rectangle().Background(null)")]
    [InlineData("Rectangle().Background(((Brush)null))")]
    [InlineData("Rectangle().Background(default(Brush))")]
    [InlineData("FlexColumn(children).Padding(null)")]
    public void Walker_Mirrors_The_Analyzers_Constant_Null_Gate(string snippet)
    {
        var scan = AgentKitSnippetWalker.Scan(new[] { new AgentKitSnippet("fixture/null.md", 1, snippet) });

        Assert.Empty(scan.Findings);

        // The chain still had to resolve, or this would be passing because the walker could not
        // read the line at all rather than because the null gate fired.
        Assert.True(
            scan.ResolvedChains >= 1,
            $"'{snippet}' resolved no modifier chain, so this asserts nothing about the null gate.");
    }

    /// <summary>
    /// A <c>default</c> that resolves to a value type is <b>not</b> null, so it must not inherit the
    /// constant-null exemption.
    /// </summary>
    /// <remarks>
    /// <c>default</c> is target-typed: <c>.Background(default)</c> is <c>default(Brush)</c> and
    /// null, while <c>.Padding(default(double))</c> is <c>0</c> — a real write that
    /// <c>ApplyModifiers</c> really does drop on a Flex receiver, and which
    /// <c>NoOpModifierAnalyzer</c> correctly reports. Treating every <c>default</c> as null would
    /// hide that, which is a silent miss: the failure mode this gate is least able to detect in
    /// itself, since the result is indistinguishable from clean documentation.
    /// </remarks>
    [Theory]
    [InlineData("FlexColumn(children).Padding(default(double))")]
    [InlineData("FlexColumn(children).Padding(default(Thickness))")]
    [InlineData("FlexColumn(children).Padding(default)")]
    public void Walker_Still_Reports_A_Value_Typed_Default(string snippet)
    {
        var scan = AgentKitSnippetWalker.Scan(new[] { new AgentKitSnippet("fixture/default.md", 1, snippet) });

        var finding = Assert.Single(scan.Of(AgentKitFindingKind.DroppedModifier));
        Assert.Equal("Padding", finding.Modifier);
        Assert.Equal("FlexElement", finding.ElementName);
    }

    /// <summary>
    /// The reference/value split behind that exemption is read off the real modifier signatures,
    /// not guessed.
    /// </summary>
    [Fact]
    public void Default_Is_Provably_Null_Only_For_Reference_Typed_Modifiers()
    {
        // Explicit type settles it, whichever way.
        Assert.True(ReactorSurface.Instance.DefaultIsProvablyNull("Background", "Brush"));
        Assert.False(ReactorSurface.Instance.DefaultIsProvablyNull("Padding", "double"));
        Assert.False(ReactorSurface.Instance.DefaultIsProvablyNull("Padding", "Thickness"));

        // Bare `default` must hold for every overload it could bind to. Background takes
        // string/Brush/ThemeRef, and ThemeRef is a readonly record struct — so `.Background(default)`
        // is not provably null (and is in fact ambiguous C#, which is why refusing costs nothing).
        Assert.False(ReactorSurface.Instance.DefaultIsProvablyNull("Background"));
        Assert.False(ReactorSurface.Instance.DefaultIsProvablyNull("Padding"));

        // An unknown modifier or type must not exempt: unprovable is not the same as null.
        Assert.False(ReactorSurface.Instance.DefaultIsProvablyNull("NoSuchModifierExists"));
        Assert.False(ReactorSurface.Instance.DefaultIsProvablyNull("Background", "NoSuchType"));

        // Guard the reading above: if ThemeRef ever stopped being a value type, the Background
        // assertions would start passing for a reason unrelated to what they claim to test.
        Assert.Contains(
            ReactorSurface.Instance.SingleArgumentModifierTypes("Background"),
            t => t.Name == "ThemeRef" && t.IsValueType);
    }

    /// <summary>
    /// A counterexample marker must come from a real comment, not from a string literal or a URL
    /// that happens to contain one of the marker words.
    /// </summary>
    /// <remarks>
    /// The exemption is the one place this gate deliberately declines to report, so anything that
    /// widens it silently converts a real violation into a pass. A textual <c>IndexOf("//")</c>
    /// cannot tell <c>// Wrong:</c> from <c>"https://example/avoid"</c>, and the second case is a
    /// perfectly ordinary thing for a sample to contain.
    /// </remarks>
    [Theory]
    // Genuine markers: exempt.
    [InlineData("FlexColumn(children).Padding(16) // Wrong: no effect", true)]
    [InlineData("FlexColumn(children).Padding(16) // ❌ dropped", true)]
    [InlineData("FlexColumn(children).Padding(16) /* Wrong */", true)]
    // A marker word inside a string is not a marker.
    [InlineData("FlexColumn(TextBlock(\"https://example/avoid\")).Padding(16)", false)]
    [InlineData("FlexColumn(TextBlock(\"never do this\")).Padding(16)", false)]
    [InlineData("Button(\"Never\").Padding(16)", false)]
    // A marker inside a string does not become one just because a real comment follows.
    [InlineData("FlexColumn(TextBlock(\"https://x/avoid\")).Padding(16) // see docs", false)]
    [InlineData("FlexColumn(children).Padding(16)", false)]
    public void Counterexample_Markers_Come_Only_From_Comments(string line, bool expected)
    {
        Assert.Equal(expected, AgentKitDocGateTests.IsMarkedAt(new[] { line }, 1));
    }

    /// <summary>
    /// A URL in the prose above a fence is a citation, not an instruction that the block below it
    /// is wrong.
    /// </summary>
    [Fact]
    public void Prose_Urls_Do_Not_Mark_The_Block_Below_As_A_Counterexample()
    {
        var withUrl = new[]
        {
            "- background reading: https://example.com/patterns/avoid",
            "```csharp",
            "FlexColumn(children).Padding(16)",
        };

        Assert.False(AgentKitDocGateTests.IsMarkedAt(withUrl, 3));

        // Positive control: the same prose shape, marker in the words rather than the link.
        var withMarker = new[]
        {
            "- Wrong, and it costs a build-check cycle:",
            "```csharp",
            "FlexColumn(children).Padding(16)",
        };

        Assert.True(AgentKitDocGateTests.IsMarkedAt(withMarker, 3));
    }

    /// <summary>
    /// A counterexample marker must be a <em>label</em>, not any occurrence of one of those words.
    /// </summary>
    /// <remarks>
    /// The exemption is the only place this gate declines to report, so anything that widens it
    /// converts a real violation into a pass. <c>// avoid re-enumerating children</c> is an
    /// ordinary explanatory comment, and treating it as a label would ship the broken sample
    /// beneath it unnoticed.
    /// </remarks>
    [Theory]
    // Labels, as this repo actually writes them.
    [InlineData("// Wrong: no effect, and costs a build-check cycle", true)]
    [InlineData("// ❌ WRONG — feeds the host's shape back", true)]
    [InlineData("// Bad: skipping levels", true)]
    [InlineData("/* Wrong */", true)]
    [InlineData("Avoid this:", true)]
    [InlineData("### ❌ The anti-pattern that breaks everything", true)]
    [InlineData("- Wrong, and it costs a build-check cycle:", true)]
    // Explanations that merely contain a marker word.
    [InlineData("// avoid re-enumerating children", false)]
    [InlineData("// Never hardcode hex on themed surfaces — reviewers reject it", false)]
    [InlineData("// Good: proper hierarchy", false)]
    [InlineData("- background reading: https://example.com/patterns/avoid", false)]
    [InlineData("// Common modifiers", false)]
    public void Counterexample_Labels_Are_Labels_Not_Incidental_Words(string text, bool expected)
    {
        Assert.Equal(expected, AgentKitDocGateTests.IsCounterexampleLabel(text));
    }

    /// <summary>
    /// An ordinary markdown lead-in paragraph above a fence must be read as prose.
    /// </summary>
    /// <remarks>
    /// A lead-in usually has no <c>#</c>, <c>&gt;</c> or <c>-</c> to recognise it by, so inferring
    /// "prose" from a line's shape read <c>Avoid this:</c> as code and abandoned the walk — turning
    /// a clearly labelled counterexample into a CI failure the author cannot satisfy without
    /// deleting their own example.
    /// </remarks>
    [Fact]
    public void A_Plain_Lead_In_Paragraph_Above_The_Fence_Is_Prose()
    {
        var leadIn = new[]
        {
            "Avoid this:",
            "",
            "```csharp",
            "FlexColumn(children).Padding(16)",
        };

        Assert.True(AgentKitDocGateTests.IsMarkedAt(leadIn, 4));

        // Negative control: the same shape with an ordinary sentence must NOT exempt, or the
        // assertion above would be passing on "prose is reachable" rather than "the label matched".
        var unlabelled = new[]
        {
            "Here is how spacing works:",
            "",
            "```csharp",
            "FlexColumn(children).Padding(16)",
        };

        Assert.False(AgentKitDocGateTests.IsMarkedAt(unlabelled, 4));

        // The walk must not cross into an earlier, unrelated block and borrow its label.
        var previousBlock = new[]
        {
            "Wrong:",
            "",
            "```csharp",
            "VStack(8, items).Padding(15)",
            "```",
            "",
            "```csharp",
            "FlexColumn(children).Padding(16)",
        };

        Assert.False(AgentKitDocGateTests.IsMarkedAt(previousBlock, 8));
    }

    /// <summary>
    /// An unrelated modifier that merely ends in "Set" does not justify a wrapper.
    /// </summary>
    /// <remarks>
    /// <c>.Set(...)</c> justifies one because its lambda is opaque to this walker. As a substring
    /// that also swallowed <c>.PositionInSet(...)</c>, an accessibility modifier that can live on
    /// the inner element — so a Border that is still only a padding workaround stopped being
    /// reported.
    /// </remarks>
    [Fact]
    public void Only_An_Exact_Set_Justifies_A_Wrapper()
    {
        var suppressed = AgentKitSnippetWalker.Scan(new[]
        {
            new AgentKitSnippet("fixture/set.md", 1, "Border(FlexColumn(children)).Padding(16).Set(b => b.Tag = x)"),
        });

        Assert.Empty(suppressed.Of(AgentKitFindingKind.WrapperWorkaround));

        var reported = AgentKitSnippetWalker.Scan(new[]
        {
            new AgentKitSnippet("fixture/set.md", 1, "Border(FlexColumn(children)).Padding(16).PositionInSet(2, 5)"),
        });

        var finding = Assert.Single(reported.Of(AgentKitFindingKind.WrapperWorkaround));
        Assert.Equal("FlexPadding", finding.Replacement);
    }

    /// <summary>
    /// A styled factory that happens to return a <c>BorderElement</c> is not a passive wrapper.
    /// </summary>
    /// <remarks>
    /// <c>Factories.Card</c> is
    /// <c>Border(child).Background(...).WithBorder(...).CornerRadius(8).Padding(16)</c> — its
    /// decoration is baked in where no chain inspection can see it, and its own XML doc offers
    /// <c>Card(child).Padding(24)</c> as the sanctioned way to override the preset. Judging
    /// passivity by element type reported that as a workaround, which is a false positive on
    /// documented usage. The same applies to a style applied from a resource.
    /// </remarks>
    [Theory]
    [InlineData("Card(FlexColumn(children)).Padding(24)")]
    [InlineData("Border(FlexColumn(children)).Padding(24).ApplyStyle(\"CardStyle\")")]
    public void A_Decorated_Wrapper_Is_Not_A_Padding_Workaround(string snippet)
    {
        var scan = AgentKitSnippetWalker.Scan(new[] { new AgentKitSnippet("fixture/card.md", 1, snippet) });

        Assert.Empty(scan.Of(AgentKitFindingKind.WrapperWorkaround));

        // Positive control on the same shape: the plain factory, undecorated, must still report —
        // otherwise this would pass because the wrapper rule had stopped working altogether.
        var plain = AgentKitSnippetWalker.Scan(new[]
        {
            new AgentKitSnippet("fixture/card.md", 1, "Border(FlexColumn(children)).Padding(24)"),
        });

        Assert.Single(plain.Of(AgentKitFindingKind.WrapperWorkaround));
    }

    /// <summary>
    /// A pack item is selected by where it ships, whichever separator the csproj spells it with.
    /// </summary>
    /// <remarks>
    /// NuGet accepts both, and this project already uses backslashes for other items. An
    /// <c>agentkit\…</c> item would ship and never enter the corpus, and since the entry is dropped
    /// whole, <see cref="Every_AgentKit_Pack_Glob_Still_Matches_Files"/> could not see the gap
    /// either — the corpus would simply be smaller than the package, silently.
    /// </remarks>
    [Fact]
    public void Pack_Entries_Are_Found_Whichever_Separator_The_Csproj_Uses()
    {
        const string Project = """
            <Project>
              <ItemGroup>
                <None Include="..\..\skills\*.md" Pack="true" PackagePath="agentkit/skills/" />
                <None Include="..\..\SKILL.md" Pack="true" PackagePath="agentkit\" />
                <None Include="..\..\LICENSE" Pack="true" PackagePath="" />
              </ItemGroup>
            </Project>
            """;

        var packagePaths = AgentKitDocCorpus.AgentKitPackagePaths(Project);

        Assert.Equal(2, packagePaths.Count);
        Assert.Contains(@"agentkit\", packagePaths);
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
    /// Every C# fence a naive, indentation-blind probe can see in the packed corpus must have
    /// produced a snippet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the fact that would have caught the four-space fences under list items 10 and 11 of
    /// <c>skills/design-docs/typography-and-colors.md</c>, which the original 0–3 space indent cap
    /// silently skipped. The aggregate snippet floor did <b>not</b> catch it and could not: 372
    /// blocks clear a floor of 150 just as comfortably as 374 do. A floor bounds the corpus from
    /// below; only a differential comparison against an independent derivation can say the
    /// extractor saw <em>everything</em>.
    /// </para>
    /// <para>
    /// The comparison is one-directional by design. <see cref="AgentKitDocCorpus.CSharpFenceProbe"/>
    /// requires the language to stand alone on the line, so the extractor may legitimately find
    /// fences the probe cannot (an info string carrying extra words). The reverse — a probe hit
    /// with no snippet — is always either a miss or an empty block.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_CSharp_Fence_In_The_Corpus_Is_Extracted()
    {
        var repoRoot = AgentKitCorpus.RepoRoot;

        var extracted = AgentKitCorpus.Snippets
            .GroupBy(s => s.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var probed = 0;
        var missed = new List<string>();

        foreach (var document in AgentKitCorpus.Documents.Where(d => d.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
        {
            var lines = File.ReadAllText(Path.Combine(repoRoot, document.Replace('/', Path.DirectorySeparatorChar)))
                .Replace("\r\n", "\n")
                .Split('\n');

            extracted.TryGetValue(document, out var snippets);
            snippets ??= new List<AgentKitSnippet>();

            // A snippet's own body may quote a fence as literal text; that line is content the
            // extractor already read, not a block it skipped.
            var covered = snippets
                .SelectMany(s => Enumerable.Range(s.StartLine, s.Text.Split('\n').Length))
                .ToHashSet();

            var openedAt = snippets.Select(s => s.StartLine - 1).ToHashSet();

            for (var i = 0; i < lines.Length; i++)
            {
                if (!AgentKitDocCorpus.CSharpFenceProbe.IsMatch(lines[i]))
                    continue;

                var line = i + 1;
                if (covered.Contains(line))
                    continue;

                probed++;

                if (!openedAt.Contains(line))
                    missed.Add($"{document}:{line} — `{lines[i].TrimEnd()}` opens a C# block that ExtractFences did not return");
            }
        }

        // Positive control. Zero probe hits would make the loop above vacuous, and it would read
        // exactly like a corpus the extractor covers perfectly.
        Assert.True(
            probed >= 150,
            $"The indentation-blind probe found only {probed} C# fences in the packed corpus; " +
            "expected 150+. The probe itself has stopped matching, so this comparison is measuring " +
            "nothing and would pass over an extractor that returned no snippets at all.");

        Assert.True(
            missed.Count == 0,
            $"{missed.Count} C# fence(s) in the shipped agent kit are not reaching the doc gate, so " +
            "their samples ship unchecked:\n  " + string.Join("\n  ", missed));
    }

    /// <summary>
    /// The fence reader must respect CommonMark's same-character, at-least-as-long closing rule,
    /// must un-nest a fence indented inside a list item, and must report the body's real starting
    /// line.
    /// </summary>
    /// <remarks>
    /// Every property here is load-bearing and none is exercised by the corpus in a way that would
    /// fail visibly: getting the close wrong would swallow the rest of a document into one snippet
    /// (findings reported at wildly wrong lines), getting the line wrong would point every failure
    /// at the wrong place, and getting the indent wrong drops whole blocks — which is exactly what
    /// shipped in the first revision of this gate, silently, past a green floor.
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

            10. **A list item** — its fenced block is indented to the item's content column,
                which is four spaces, not the three CommonMark allows at top level:

                ```csharp
                TextBlock(paragraph).TextWrapping(TextWrapping.WrapWholeWords)
                ```
            """;

        var snippets = AgentKitDocCorpus.ExtractFences("fixture/fences.md", Markdown);

        Assert.Equal(3, snippets.Count);

        Assert.Equal(4, snippets[0].StartLine);
        Assert.Equal("FlexColumn(children).FlexPadding(16)", snippets[0].Text);

        Assert.Equal(12, snippets[1].StartLine);
        Assert.Contains("Border(child).Padding(8)", snippets[1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("not C#", snippets[1].Text, StringComparison.Ordinal);

        // The list-nested block: found at all (the regression this pins), reported at its real
        // line, and un-nested from the four-space list indent rather than carrying it into the
        // snippet text.
        Assert.Equal(21, snippets[2].StartLine);
        Assert.Equal(
            "TextBlock(paragraph).TextWrapping(TextWrapping.WrapWholeWords)",
            snippets[2].Text);
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
