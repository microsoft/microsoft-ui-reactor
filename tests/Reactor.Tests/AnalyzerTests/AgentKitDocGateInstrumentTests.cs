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
    [InlineData("Rectangle().Background(null!)")]
    [InlineData("Rectangle().Background(((Brush)null)!)")]
    [InlineData("Rectangle().Background(default(Brush)!)")]
    [InlineData("Rectangle().Background(default(Microsoft.UI.Xaml.Media.Brush))")]
    [InlineData("Rectangle().Background(default(global::Microsoft.UI.Xaml.Media.Brush))")]
    [InlineData("Rectangle().Background(default(Brush?))")]
    [InlineData("Rectangle().Background((Brush)default)")]
    [InlineData("Rectangle().Background(((Brush)default)!)")]
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
    [InlineData("// avoid allocations", false)]
    [InlineData("// never cache", false)]
    [InlineData("// Show ❌ when validation fails", false)]
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
    /// A generic DSL factory must resolve like any other, so its chains are actually inspected.
    /// </summary>
    /// <remarks>
    /// Two things kept them out: a generic factory returns an open constructed type
    /// (<c>ListView&lt;T&gt;</c> gives <c>TemplatedListViewElement&lt;T&gt;</c>), and its call site
    /// is <c>GenericNameSyntax</c> rather than <c>IdentifierNameSyntax</c>. A skipped chain neither
    /// reports nor counts, so the blind spot was invisible to every floor — while
    /// <c>ListView&lt;Todo&gt;(...)</c>, <c>ListView&lt;Item&gt;(...)</c> and
    /// <c>ListView&lt;Order&gt;(...)</c> all appear in the shipped corpus.
    /// </remarks>
    [Theory]
    [InlineData("ListView")]
    [InlineData("GridView")]
    [InlineData("LazyVStack")]
    [InlineData("FlipView")]
    public void Generic_Factories_Resolve_To_An_Element(string factory)
    {
        var element = ReactorSurface.Instance.Element(factory, typeArgumentCount: 1);

        Assert.NotNull(element);
        Assert.True(
            typeof(Microsoft.UI.Reactor.Core.Element).IsAssignableFrom(element),
            $"{factory}<T> resolved to {element}, which is not an Element.");
    }

    /// <summary>
    /// A generic factory call site reaches the walker, rather than being dropped before it counts.
    /// </summary>
    [Fact]
    public void A_Generic_Factory_Call_Site_Is_Walked()
    {
        var scan = AgentKitSnippetWalker.Scan(new[]
        {
            new AgentKitSnippet("fixture/generic.md", 1, "ListView<Todo>(items, (t, _) => Row(t)).Padding(16)"),
        });

        Assert.True(
            scan.ResolvedChains >= 1,
            "A `ListView<Todo>(...)` chain resolved nothing, so generic factories are still invisible " +
            "to the gate and their samples ship uninspected.");
    }

    /// <summary>
    /// A justification later in the chain still counts when the chain is parenthesised.
    /// </summary>
    /// <remarks>
    /// The outward walk that collects chain modifiers has to treat the same nodes as transparent
    /// that the downward walk does. While it did not,
    /// <c>(Border(FlexColumn(children)).Padding(16)).Background(...)</c> was reported as a
    /// workaround — the walk never reached the <c>Background</c> that justifies the Border — which
    /// is a CI failure on valid documentation.
    /// </remarks>
    [Theory]
    [InlineData("(Border(FlexColumn(children)).Padding(16)).Background(Theme.CardBackground)")]
    [InlineData("(Border(FlexColumn(children)).Padding(16))!.Background(Theme.CardBackground)")]
    public void A_Justification_Survives_Parentheses(string snippet)
    {
        var scan = AgentKitSnippetWalker.Scan(new[] { new AgentKitSnippet("fixture/parens.md", 1, snippet) });

        Assert.Empty(scan.Of(AgentKitFindingKind.WrapperWorkaround));

        // Positive control on the same shape: without the justification it must still report, or
        // this would pass because parenthesising had disabled the rule outright.
        var unjustified = AgentKitSnippetWalker.Scan(new[]
        {
            new AgentKitSnippet("fixture/parens.md", 1, "(Border(FlexColumn(children)).Padding(16)).Margin(8)"),
        });

        Assert.Single(unjustified.Of(AgentKitFindingKind.WrapperWorkaround));
    }

    /// <summary>
    /// A generic factory written without explicit type arguments still resolves.
    /// </summary>
    /// <remarks>
    /// C# infers <c>T</c>, so <c>LazyVStack(items, (x, _) => Row(x))</c> reaches the walker as a
    /// plain identifier with arity 0 while the surface map holds it at arity 1. Keying on the
    /// written arity alone therefore reintroduced the same blind spot for the far more common call
    /// shape — and, as ever, a skipped chain neither reports nor counts, so no floor would say so.
    /// </remarks>
    [Theory]
    [InlineData("LazyVStack")]
    [InlineData("LazyHStack")]
    public void An_Inferred_Generic_Factory_Resolves_Without_Type_Arguments(string factory)
    {
        var written = ReactorSurface.Instance.Element(factory, typeArgumentCount: 0);
        var explicitly = ReactorSurface.Instance.Element(factory, typeArgumentCount: 1);

        Assert.NotNull(explicitly);
        Assert.Equal(explicitly, written);
    }

    /// <summary>
    /// The fallback stays conservative where the name is genuinely ambiguous.
    /// </summary>
    /// <remarks>
    /// Several names are both. <c>ListView(...)</c> returns <c>ListViewElement</c> while
    /// <c>ListView&lt;T&gt;(...)</c> returns <c>TemplatedListViewElement&lt;T&gt;</c>. The written
    /// form is honoured exactly, and inference is never used to overrule an arity that really
    /// exists — guessing there would attribute a chain to the wrong control, which is how a gate
    /// starts reporting findings that are not true.
    /// </remarks>
    [Theory]
    [InlineData("ListView")]
    [InlineData("GridView")]
    [InlineData("FlipView")]
    [InlineData("TreeView")]
    public void Inference_Never_Overrules_An_Arity_That_Exists(string factory)
    {
        var nonGeneric = ReactorSurface.Instance.Element(factory, typeArgumentCount: 0);
        var generic = ReactorSurface.Instance.Element(factory, typeArgumentCount: 1);

        Assert.NotNull(nonGeneric);
        Assert.NotNull(generic);
        Assert.NotEqual(nonGeneric, generic);
    }

    [Fact]
    public void An_Inferred_Generic_Call_Site_Is_Walked()
    {
        var scan = AgentKitSnippetWalker.Scan(new[]
        {
            new AgentKitSnippet("fixture/inferred.md", 1, "LazyVStack(items, (x, _) => Row(x)).Padding(16)"),
        });

        Assert.True(
            scan.ResolvedChains >= 1,
            "A `LazyVStack(items, ...)` chain resolved nothing, so generic factories written without " +
            "explicit type arguments — the common form — are still invisible to the gate.");
    }

    /// <summary>
    /// A standalone block-comment label marks the block below it.
    /// </summary>
    /// <remarks>
    /// <c>IsCounterexampleLabel</c> documents <c>/* Wrong */</c> as supported and
    /// <c>CommentText</c> reads multi-line trivia, but the upward walk accepted only <c>//</c>
    /// lines — so the documented placement produced a CI failure. Same class of defect as the
    /// prose lead-in: a contract stated in one place and not honoured in another.
    /// </remarks>
    [Fact]
    public void A_Block_Comment_Label_Marks_The_Line_Below()
    {
        var marked = new[]
        {
            "```csharp",
            "/* Wrong */",
            "FlexColumn(children).Padding(16)",
        };

        Assert.True(AgentKitDocGateTests.IsMarkedAt(marked, 3));

        // Negative control: a block comment that is not a label must not exempt.
        var unmarked = new[]
        {
            "```csharp",
            "/* spacing sample */",
            "FlexColumn(children).Padding(16)",
        };

        Assert.False(AgentKitDocGateTests.IsMarkedAt(unmarked, 3));
    }

    /// <summary>
    /// A <c>with</c> mutation anywhere on the chain suppresses the wrapper finding.
    /// </summary>
    /// <remarks>
    /// The chain walkers step through <c>with</c> to reach the receiver, so its assignments are
    /// never seen: <c>with { Background = brush }</c> supplies the decoration that would justify
    /// the wrapper, and <c>with { Child = other }</c> replaces the very element the finding names.
    /// Element records are configured this way by design, so reporting through one is a false
    /// positive on idiomatic code.
    /// </remarks>
    [Theory]
    [InlineData("Border(FlexColumn(children)).Padding(16) with { Background = brush }")]
    [InlineData("Border(FlexColumn(children)).Padding(16) with { Child = other }")]
    [InlineData("(Border(FlexColumn(children)) with { Background = brush }).Padding(16)")]
    public void A_With_Mutation_Suppresses_The_Wrapper_Finding(string snippet)
    {
        var scan = AgentKitSnippetWalker.Scan(new[] { new AgentKitSnippet("fixture/with.md", 1, snippet) });

        Assert.Empty(scan.Of(AgentKitFindingKind.WrapperWorkaround));

        // Positive control on the same shape: the identical chain with no `with` must still report,
        // or this would pass because the rule had stopped working rather than because it deferred.
        var plain = AgentKitSnippetWalker.Scan(new[]
        {
            new AgentKitSnippet("fixture/with.md", 1, "Border(FlexColumn(children)).Padding(16)"),
        });

        Assert.Single(plain.Of(AgentKitFindingKind.WrapperWorkaround));
    }

    /// <summary>
    /// A type-changing modifier decides the chain's element, so a gated modifier after it is
    /// judged against the type it produced.
    /// </summary>
    /// <remarks>
    /// <c>Semantics</c> is the framework's only one. <c>Border(child).Semantics(...)</c> is a
    /// <c>SemanticElement</c> mounting a <c>SemanticPanel</c> — a <c>Panel</c>, outside
    /// <c>Padding</c>'s gate — so a trailing <c>.Padding(16)</c> really is dropped, even though the
    /// head is a Border and a Border is a legal receiver. Walking through it resolved the wrong
    /// element, reported nothing, and still counted the chain as resolved, which is the shape of
    /// shortfall the floors are meant to expose.
    /// </remarks>
    [Fact]
    public void A_Type_Changing_Modifier_Decides_The_Element()
    {
        Assert.Contains("Semantics", ReactorSurface.Instance.TypeChangingModifiers.Keys);

        // The chain's element is the SemanticElement, not the Border that opened it.
        Assert.Equal(
            ReactorSurface.Instance.TypeChangingModifiers["Semantics"],
            ReactorSurface.Instance.Element("Semantics", 0));

        // ...and because SemanticElement declares no `Set` overload, the receiver is unresolvable,
        // so the chain is skipped rather than attributed to the element that opened it. That
        // matches the analyzer, which is silent on receivers it cannot resolve the same way. The
        // value of the stop is therefore *non*-attribution: without it the walk would judge a
        // SemanticPanel against the opening element's gate.
        //
        // The head has to be one that would produce a finding when misattributed, or the assertion
        // proves nothing. A `Border` head cannot: Border is a legal `Padding` receiver, so deleting
        // the stop leaves this silent for the wrong reason and the test passes either way. A
        // FlexColumn mounts a FlexPanel, outside Padding's gate, so misattribution is visible.
        var scan = AgentKitSnippetWalker.Scan(new[]
        {
            new AgentKitSnippet("fixture/semantics.md", 1, "FlexColumn(children).Semantics(role: \"button\").Padding(16)"),
        });

        Assert.Empty(scan.Findings);

        // Positive control: the same chain without the type-changing modifier does report, so the
        // emptiness above is the stop working rather than the walker being unable to see the shape.
        var withoutStop = AgentKitSnippetWalker.Scan(new[]
        {
            new AgentKitSnippet("fixture/semantics.md", 1, "FlexColumn(children).Padding(16)"),
        });

        Assert.NotEmpty(withoutStop.Findings);

        Assert.Null(ReactorSurface.Instance.MountedControl(ReactorSurface.Instance.Element("Semantics", 0)!));
    }

    /// <summary>
    /// A shape replacement is only named once it exists on that element.
    /// </summary>
    /// <remarks>
    /// The analyzer resolves candidates in order and skips any that is not an invocable member on
    /// the receiver, which is why <c>LineElement</c> — having no <c>Fill</c> — falls through to
    /// <c>Stroke</c>. Taking the first candidate blindly named a remedy that does not compile, and
    /// would reject a Line counterexample correctly documenting <c>.Stroke(...)</c>.
    /// </remarks>
    [Theory]
    [InlineData("Rectangle().Background(brush)", "Fill")]
    [InlineData("Line().Background(brush)", "Stroke")]
    public void A_Shape_Replacement_Must_Exist_On_That_Element(string snippet, string expected)
    {
        var scan = AgentKitSnippetWalker.Scan(new[] { new AgentKitSnippet("fixture/shape.md", 1, snippet) });

        Assert.Equal(expected, Assert.Single(scan.Of(AgentKitFindingKind.DroppedModifier)).Replacement);
    }

    /// <summary>
    /// The mounted control comes from the <c>Set</c> overload alone, as the analyzer's does.
    /// </summary>
    /// <remarks>
    /// An earlier revision fell back to the generator attribute, which made this gate report on
    /// receivers <c>NoOpModifierAnalyzer.TryGetMountedControl</c> cannot resolve — a documentation
    /// gate stricter than the rule it enforces, producing findings a reader cannot act on.
    /// </remarks>
    [Fact]
    public void An_Element_With_No_Set_Overload_Is_Unresolvable()
    {
        var semantic = ReactorSurface.Instance.Element("Semantics", 0);

        Assert.NotNull(semantic);
        Assert.Empty(ReactorSurface.Instance.SetControls(semantic!));
        Assert.Null(ReactorSurface.Instance.MountedControl(semantic!));

        // Control: an element that *does* declare Set still resolves, so this is not passing
        // because MountedControl stopped working.
        var border = ReactorSurface.Instance.Element("Border", 0);
        Assert.NotNull(ReactorSurface.Instance.MountedControl(border!));
    }

    /// <summary>
    /// An item that is not packed is not inspected.
    /// </summary>
    /// <remarks>
    /// A <c>&lt;None&gt;</c> item ships only if it opts in with <c>Pack="true"</c>, so an
    /// <c>agentkit/</c> path alone does not mean a consumer receives the file. Scanning one anyway
    /// could fail the gate over a document nobody gets, breaking the property the whole corpus
    /// rests on — that it is the same list the pack target consumes.
    /// </remarks>
    [Fact]
    public void Only_Items_That_Actually_Pack_Are_Inspected()
    {
        const string Project = """
            <Project>
              <ItemGroup>
                <None Include="a.md" Pack="true"  PackagePath="agentkit/skills/" />
                <None Include="b.md" Pack="false" PackagePath="agentkit/skills/" />
                <None Include="c.md"              PackagePath="agentkit/skills/" />
              </ItemGroup>
            </Project>
            """;

        Assert.Equal(new[] { "a.md" }, AgentKitDocCorpus.AgentKitIncludes(Project));
    }

    /// <summary>
    /// Fence indentation is measured against the container, at both ends.
    /// </summary>
    /// <remarks>
    /// Three distinct ways to get this wrong, all pinned here: a top-level indented block whose
    /// literal fence must not open; a shallow list item where the same absolute indent <em>is</em>
    /// too deep; and an indented <c>```</c> inside a sample that must not close it early. Each
    /// leaves real C# outside the gate, and the differential probe cannot see any of them because
    /// it trusts this scanner for block structure.
    /// </remarks>
    [Fact]
    public void Fence_Indentation_Is_Measured_Against_Its_Container()
    {
        // Top level: four spaces is an indented code block, so the fence in it is literal.
        const string TopLevel = """
            Prose, then an indented code block quoting a fence:

                ```text
                not a real fence

            ```csharp
            FlexColumn(children).FlexPadding(16)
            ```
            """;

        Assert.Equal(
            "FlexColumn(children).FlexPadding(16)",
            Assert.Single(AgentKitDocCorpus.ExtractFences("fixture/top.md", TopLevel)).Text);

        // `10. ` has content column 4, so a fence at 4 is flush with it and real.
        const string NumberedItem = """
            10. **An item** — its block sits at the item's content column:

                ```csharp
                TextBlock(p).TextWrapping(TextWrapping.WrapWholeWords)
                ```
            """;

        Assert.Equal(
            "TextBlock(p).TextWrapping(TextWrapping.WrapWholeWords)",
            Assert.Single(AgentKitDocCorpus.ExtractFences("fixture/numbered.md", NumberedItem)).Text);

        // `- ` has content column 2, so six spaces is four past it — literal, not a fence.
        const string ShallowItem = """
            - item

                  ```text
                  not a real fence

            ```csharp
            Border(child).Padding(8)
            ```
            """;

        Assert.Equal(
            "Border(child).Padding(8)",
            Assert.Single(AgentKitDocCorpus.ExtractFences("fixture/shallow.md", ShallowItem)).Text);

        // A continuation line between the marker and the fence must not be read as closing the
        // list: `- ` has content column 2, so five spaces is three past it and still a fence.
        const string Continuation = """
            - item
              explanation

                 ```csharp
                 FlexColumn(children).FlexPadding(16)
                 ```
            """;

        Assert.Equal(
            "FlexColumn(children).FlexPadding(16)",
            Assert.Single(AgentKitDocCorpus.ExtractFences("fixture/continuation.md", Continuation)).Text);

        // An info string separates the language from its attributes with any whitespace, so a
        // tab-delimited attribute must not be read as part of the language name.
        const string Tabbed = "```csharp\tlinenos\nFlexColumn(children).FlexPadding(16)\n```";

        Assert.Equal(
            "FlexColumn(children).FlexPadding(16)",
            Assert.Single(AgentKitDocCorpus.ExtractFences("fixture/tab.md", Tabbed)).Text);

        Assert.Matches(AgentKitDocCorpus.CSharpFenceProbe, "```csharp\tlinenos");

        // The probe must accept every info-string syntax the extractor does, or the differential
        // oracle is blind to exactly the blocks it is meant to police.
        Assert.Matches(AgentKitDocCorpus.CSharpFenceProbe, "```csharp,linenos");

        const string CommaInfo = "```csharp,linenos\nFlexColumn(children).FlexPadding(16)\n```";

        Assert.Equal(
            "FlexColumn(children).FlexPadding(16)",
            Assert.Single(AgentKitDocCorpus.ExtractFences("fixture/comma.md", CommaInfo)).Text);

        // Marker padding sets the content column: `-    item` puts content at 5, so a fence eight
        // spaces in is three past it and valid. Consuming a single space read the column as 2 and
        // rejected it.
        const string PaddedMarker = """
            -    item

                    ```csharp
                    FlexColumn(children).FlexPadding(16)
                    ```
            """;

        Assert.Equal(
            "FlexColumn(children).FlexPadding(16)",
            Assert.Single(AgentKitDocCorpus.ExtractFences("fixture/padded.md", PaddedMarker)).Text);

        // A fence may also open on a list marker's own line (`- ```csharp`); the marker sets the
        // content column, and the probe accepts the shape too so the oracle is not blind to it.
        const string InlineMarker = """
            - ```csharp
              FlexColumn(children).FlexPadding(16)
              ```
            """;

        Assert.Equal(
            "FlexColumn(children).FlexPadding(16)",
            Assert.Single(AgentKitDocCorpus.ExtractFences("fixture/inline.md", InlineMarker)).Text);

        Assert.Matches(AgentKitDocCorpus.CSharpFenceProbe, "- ```csharp");

        // Five or more spaces after the marker put the item's content one space along and make the
        // rest indented code, so this is literal text rather than a fence. Treating all the padding
        // as list padding scanned it as a sample, and the gate can fail on prose that way. The
        // probe has to agree, or the completeness fact reports the same literal text as unscanned.
        const string OverPaddedMarker = """
            -     ```csharp
                  FlexColumn(children).Padding(16)
                  ```
            """;

        Assert.Empty(AgentKitDocCorpus.ExtractFences("fixture/overpadded.md", OverPaddedMarker));
        Assert.DoesNotMatch(AgentKitDocCorpus.CSharpFenceProbe, "-     ```csharp");

        // Four is still list padding, and both halves must still see it.
        const string MaxPaddedMarker = """
            -    ```csharp
                 FlexColumn(children).FlexPadding(16)
                 ```
            """;

        Assert.Equal(
            "FlexColumn(children).FlexPadding(16)",
            Assert.Single(AgentKitDocCorpus.ExtractFences("fixture/maxpadded.md", MaxPaddedMarker)).Text);
        Assert.Matches(AgentKitDocCorpus.CSharpFenceProbe, "-    ```csharp");

        // A container marker four columns past its own container is indented code, not container
        // syntax — the repository's CommonMark fixtures pin `    > # Foo` (Example_0230) and
        // `    1.  A paragraph` (Example_0288) as literal text. Scanning those as samples can fail
        // the gate on quoted prose, and the probe has to agree or it corroborates the same
        // misclassification and the completeness fact reddens on a document that is correct.
        const string DeepMarker = """
            Some prose.

                - ```csharp
                  FlexColumn(children).Padding(16)
                  ```
            """;

        Assert.Empty(AgentKitDocCorpus.ExtractFences("fixture/deepmarker.md", DeepMarker));
        Assert.DoesNotMatch(AgentKitDocCorpus.CSharpFenceProbe, "    - ```csharp");
        Assert.DoesNotMatch(AgentKitDocCorpus.CSharpFenceProbe, "    > ```csharp");

        const string DeepQuote = """
                > ```csharp
                > FlexColumn(children).Padding(16)
                > ```
            """;

        Assert.Empty(AgentKitDocCorpus.ExtractFences("fixture/deepquote.md", DeepQuote));

        // Positive control at three columns, where both are still container syntax.
        Assert.Matches(AgentKitDocCorpus.CSharpFenceProbe, "   - ```csharp");
        Assert.Matches(AgentKitDocCorpus.CSharpFenceProbe, "   > ```csharp");
    }

    /// <summary>
    /// An indented <c>```</c> inside a sample does not close the block early.
    /// </summary>
    [Fact]
    public void An_Indented_Run_Does_Not_Close_A_Top_Level_Fence()
    {
        // Four-quote delimiter so the sample can contain a three-quote raw string of its own.
        const string Markdown = """"
            ```csharp
            var doc = """
                ```
                """;
            FlexColumn(children).Padding(16)
            ```
            """";

        var snippet = Assert.Single(AgentKitDocCorpus.ExtractFences("fixture/raw.md", Markdown));

        Assert.Contains("FlexColumn(children).Padding(16)", snippet.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A disagreement between overloads poisons a type-changing modifier permanently.
    /// </summary>
    /// <remarks>
    /// The A, B, A ordering is the point. Removing the entry on disagreement let the third overload
    /// re-add it, so an ambiguous modifier resolved to one arbitrary answer — and since
    /// <c>GetMethods</c> order is unspecified, which answer was not reproducible. The live surface
    /// cannot reach this state (one type-changing modifier, all overloads agreeing), so the merge
    /// is tested directly rather than through reflection.
    /// </remarks>
    [Theory]
    [InlineData("A,B,A")]
    [InlineData("A,A,B")]
    [InlineData("B,A,A")]
    [InlineData("A,B,A,B,A")]
    public void Disagreeing_Overloads_Poison_A_Type_Changing_Modifier(string order)
    {
        var types = new Dictionary<string, Type> { ["A"] = typeof(int), ["B"] = typeof(string) };
        var map = new Dictionary<string, Type?>(StringComparer.Ordinal);

        foreach (var step in order.Split(','))
            ReactorSurface.MergeTypeChange(map, "Modifier", types[step]);

        Assert.True(map.ContainsKey("Modifier"), "The name must stay known so the walk still stops on it.");
        Assert.Null(map["Modifier"]);
    }

    /// <summary>Agreeing overloads, however many, still resolve.</summary>
    [Fact]
    public void Agreeing_Overloads_Resolve_A_Type_Changing_Modifier()
    {
        var map = new Dictionary<string, Type?>(StringComparer.Ordinal);

        for (var i = 0; i < 4; i++)
            ReactorSurface.MergeTypeChange(map, "Modifier", typeof(int));

        Assert.Equal(typeof(int), map["Modifier"]);
    }

    /// <summary>
    /// Only modifiers proven relocatable let the wrapper finding through; everything else
    /// suppresses it.
    /// </summary>
    /// <remarks>
    /// The allowlist replaced a denylist whose failure mode was backwards: an unrecognised modifier
    /// counted as "the wrapper contributes nothing" and produced a finding, so
    /// <c>Border(...).Padding(16).OnTapped(...)</c> was reported as removable even though the
    /// Border is the routed-event source and hit-test boundary. Events, flyouts, tooltips and
    /// connected animations are an open-ended set that cannot be enumerated safely, so the default
    /// has to be "suppress".
    /// </remarks>
    [Theory]
    // Positional / identity modifiers the inner element inherits: the finding still stands.
    [InlineData("Border(FlexColumn(children)).Padding(16)", true)]
    [InlineData("Border(FlexColumn(children)).Padding(16).Flex(grow: 1, basis: 0)", true)]
    [InlineData("Border(FlexColumn(children)).Padding(16).Margin(8)", true)]
    [InlineData("Border(FlexColumn(children)).Padding(16).WithKey(\"k\")", true)]
    // Inert: does nothing, so it cannot be what the wrapper is for.
    [InlineData("Border(FlexColumn(children)).Padding(16).Background((Brush)null)", true)]
    // A null argument to an *ungated* modifier proves nothing: `.Stagger(delay, null)` still
    // installs a stagger, and moving it inward changes which children animate.
    [InlineData("Border(FlexColumn(children)).Padding(16).Stagger(delay, null)", false)]
    // Behaviour-bearing or opaque: the wrapper is load-bearing, so no finding.
    [InlineData("Border(FlexColumn(children)).Padding(16).OnTapped((s, e) => Handle(e))", false)]
    [InlineData("Border(FlexColumn(children)).Padding(16).WithFlyout(menu)", false)]
    [InlineData("Border(FlexColumn(children)).Padding(16).ToolTip(\"hint\")", false)]
    [InlineData("Border(FlexColumn(children)).Padding(16).Background(Theme.CardBackground)", false)]
    [InlineData("Border(FlexColumn(children)).Padding(16).Set(b => b.Tag = x)", false)]
    [InlineData("Border(FlexColumn(children)).Padding(16).OnMount(fe => Configure(fe))", false)]
    [InlineData("Border(FlexColumn(children)).Padding(16).Ref(borderRef)", false)]
    [InlineData("Border(FlexColumn(children)).Padding(16).ApplyStyle(\"CardStyle\")", false)]
    // Parent-relative modifiers on the *inner* chain: deleting the wrapper reparents that element,
    // so a `.Flex` that is inert inside the Border starts driving layout once it is gone. The
    // remedy is not behaviour-preserving, so it is not recommended.
    [InlineData("Border(FlexColumn(children).Flex(grow: 1)).Padding(16)", false)]
    [InlineData("Border(FlexColumn(children).Dock(Dock.Top)).Padding(16)", false)]
    [InlineData("Border(FlexColumn(children).Margin(8)).Padding(16)", false)]
    // ...but an inner modifier the parent does not interpret leaves the remedy sound.
    [InlineData("Border(FlexColumn(children).Background(brush)).Padding(16)", true)]
    public void Only_Relocatable_Modifiers_Leave_A_Wrapper_Removable(string snippet, bool reported)
    {
        var scan = AgentKitSnippetWalker.Scan(new[] { new AgentKitSnippet("fixture/relocatable.md", 1, snippet) });
        var findings = scan.Of(AgentKitFindingKind.WrapperWorkaround);

        if (reported)
            Assert.Equal("FlexPadding", Assert.Single(findings).Replacement);
        else
            Assert.Empty(findings);
    }

    /// <summary>
    /// A fence inside a blockquote is extracted, with the container prefix stripped.
    /// </summary>
    /// <remarks>
    /// The opener previously allowed only whitespace before the delimiter, so <c>&gt; ```csharp</c>
    /// was skipped — and <see cref="AgentKitDocCorpus.CSharpFenceProbe"/> shared the restriction,
    /// so the differential oracle stayed green while the block shipped uninspected. The corpus has
    /// four blockquoted fences today, all untagged shell, so this closes the gap before a C# one
    /// appears rather than after.
    /// </remarks>
    [Fact]
    public void A_Fence_Inside_A_Blockquote_Is_Extracted()
    {
        const string Markdown = """
            > ### Note
            >
            > ```csharp
            > FlexColumn(children).FlexPadding(16)
            > ```
            """;

        var snippet = Assert.Single(AgentKitDocCorpus.ExtractFences("fixture/quote.md", Markdown));

        // Line 3 opens the fence, so the body — and StartLine — is line 4.
        Assert.Equal(4, snippet.StartLine);
        Assert.Equal("FlexColumn(children).FlexPadding(16)", snippet.Text);

        // The probe must see it too, or the differential oracle would stay blind to this shape even
        // once the extractor handles it.
        Assert.Matches(AgentKitDocCorpus.CSharpFenceProbe, "> ```csharp");
    }

    /// <summary>
    /// A counterexample label inside a blockquote marks the blockquoted sample below it.
    /// </summary>
    /// <remarks>
    /// The extractor learned about blockquotes before the marker walk did, so the walk saw a raw
    /// <c>&gt;</c> on <c>&gt; ```csharp</c>, failed its fence test and treated the opener as code —
    /// meaning a deliberately marked counterexample failed CI. Two readers of the same document
    /// disagreeing about its structure is its own defect class, so both sides strip the prefix now.
    /// </remarks>
    [Fact]
    public void A_Blockquoted_Label_Marks_The_Blockquoted_Sample()
    {
        var marked = new[]
        {
            "> Avoid this:",
            ">",
            "> ```csharp",
            "> FlexColumn(children).Padding(16)",
        };

        Assert.True(AgentKitDocGateTests.IsMarkedAt(marked, 4));

        // Negative control: the same blockquoted shape with ordinary prose must not exempt.
        var unmarked = new[]
        {
            "> Here is how spacing works:",
            ">",
            "> ```csharp",
            "> FlexColumn(children).Padding(16)",
        };

        Assert.False(AgentKitDocGateTests.IsMarkedAt(unmarked, 4));

        // And a blockquoted comment label works too.
        var comment = new[]
        {
            "> ```csharp",
            "> // Wrong: no effect",
            "> FlexColumn(children).Padding(16)",
        };

        Assert.True(AgentKitDocGateTests.IsMarkedAt(comment, 3));
    }

    /// <summary>
    /// A label in a <em>deeper</em> blockquote does not mark a sample in the container around it.
    /// </summary>
    /// <remarks>
    /// The walk stripped every quote level before matching, so <c>&gt; &gt; Avoid this:</c> read as
    /// an ordinary label and exempted the parent quote's sample below it. That nested line belongs
    /// to its own container and introduces its own example; borrowing it silently suppressed a real
    /// finding one level out — the mirror of the depth bookkeeping the fence scanner already does.
    /// </remarks>
    [Fact]
    [Trait("Category", "AgentKitDocGate")]
    public void A_Deeper_Blockquoted_Label_Does_Not_Mark_The_Enclosing_Sample()
    {
        var nested = new[]
        {
            "> > Avoid this:",
            ">",
            "> ```csharp",
            "> FlexColumn(children).Padding(16)",
        };

        Assert.False(AgentKitDocGateTests.IsMarkedAt(nested, 4));

        // Positive control: the identical shape with the label at the sample's own depth must
        // exempt, so the assertion above fails on the depth test rather than on the walk being
        // unable to reach that line at all.
        var sameDepth = new[]
        {
            "> Avoid this:",
            ">",
            "> ```csharp",
            "> FlexColumn(children).Padding(16)",
        };

        Assert.True(AgentKitDocGateTests.IsMarkedAt(sameDepth, 4));

        // A shallower label still counts: it encloses the quote, so it does introduce what is
        // inside it. Exempting is the safe direction for an ambiguous container boundary.
        var shallower = new[]
        {
            "Avoid this:",
            "",
            "> ```csharp",
            "> FlexColumn(children).Padding(16)",
        };

        Assert.True(AgentKitDocGateTests.IsMarkedAt(shallower, 4));
    }

    /// <summary>
    /// A lead-in above a fence opened on a list marker's own line still marks that sample.
    /// </summary>
    /// <remarks>
    /// The extractor accepts <c>- ```csharp</c>, so the marker walk has to as well. Recognising a
    /// fence only by a trimmed line starting with the delimiter read that opener as C# code and
    /// abandoned the search, turning a deliberately labelled counterexample into a CI failure —
    /// the two halves disagreeing about the same document.
    /// </remarks>
    [Fact]
    [Trait("Category", "AgentKitDocGate")]
    public void A_Label_Marks_A_Sample_In_An_Inline_List_Fence()
    {
        var marked = new[]
        {
            "Avoid this:",
            "",
            "- ```csharp",
            "  FlexColumn(children).Padding(16)",
        };

        Assert.True(AgentKitDocGateTests.IsMarkedAt(marked, 4));

        // Negative control: the same shape with ordinary prose must not exempt, so the assertion
        // above is failing on the label rather than on the walk reaching further than it should.
        var unmarked = new[]
        {
            "Here is the layout:",
            "",
            "- ```csharp",
            "  FlexColumn(children).Padding(16)",
        };

        Assert.False(AgentKitDocGateTests.IsMarkedAt(unmarked, 4));
    }

    /// <summary>
    /// A label in the preceding list item marks a sample in the next one — deliberately.
    /// </summary>
    /// <remarks>
    /// <para>
    /// `- Avoid this:` above a `- ```csharp` item is one list making one point, and it is how these
    /// documents are written. Treating the item boundary as a barrier would report a labelled
    /// counterexample and fail CI on correct documentation, which is the failure mode this gate
    /// cannot afford; over-reaching costs a missed finding instead.
    /// </para>
    /// <para>
    /// Pinned as a test rather than left implicit so the trade-off is a decision, not an accident.
    /// It stays bounded by the eight-line budget and the previous block's fence, and an exemption
    /// still requires the document to name the remedy.
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Category", "AgentKitDocGate")]
    public void A_Label_In_The_Preceding_List_Item_Marks_The_Next_One()
    {
        var siblings = new[]
        {
            "- Avoid this:",
            "- ```csharp",
            "  FlexColumn(children).Padding(16)",
        };

        Assert.True(AgentKitDocGateTests.IsMarkedAt(siblings, 3));

        // The reach is bounded, not unlimited: an earlier block's fence ends the walk, so a label
        // introducing *that* block cannot be borrowed by this one.
        var previousBlock = new[]
        {
            "- Avoid this:",
            "- ```csharp",
            "  FlexColumn(children).Padding(16)",
            "  ```",
            "- ```csharp",
            "  FlexRow(children).Padding(16)",
        };

        Assert.False(AgentKitDocGateTests.IsMarkedAt(previousBlock, 6));

        // Negative control: ordinary sibling prose must not exempt, or the first assertion is
        // passing on reachability rather than on the label matching.
        var unlabelled = new[]
        {
            "- Here is the layout:",
            "- ```csharp",
            "  FlexColumn(children).Padding(16)",
        };

        Assert.False(AgentKitDocGateTests.IsMarkedAt(unlabelled, 3));
    }

    /// <summary>
    /// The remedy must be named at identifier boundaries, not as a substring of a longer name.
    /// </summary>
    /// <remarks>
    /// This is the clause that <em>permits</em> an otherwise-invalid snippet, so a loose match
    /// exempts the sample it was meant to hold to account: a document mentioning only
    /// <c>FillColor</c> counted as having named <c>Fill</c>.
    /// </remarks>
    [Fact]
    [Trait("Category", "AgentKitDocGate")]
    public void A_Remedy_Named_Only_Inside_A_Longer_Identifier_Does_Not_Count()
    {
        Assert.False(AgentKitDocGateTests.NamesRemedy("use the FillColorDefaultBrush resource", "Fill"));
        Assert.False(AgentKitDocGateTests.NamesRemedy("see FlexPaddingThickness", "FlexPadding"));

        // Positive controls: the bare name, and the name in the shapes documents actually use.
        Assert.True(AgentKitDocGateTests.NamesRemedy("reach for Fill instead", "Fill"));
        Assert.True(AgentKitDocGateTests.NamesRemedy("write `.FlexPadding(16)`", "FlexPadding"));

        // No replacement to name is not a failure to name it.
        Assert.True(AgentKitDocGateTests.NamesRemedy("anything", null));
    }

    /// <summary>
    /// A nested blockquote inside a quoted fence does not close it.
    /// </summary>
    /// <remarks>
    /// Stripping every <c>&gt;</c> level reduced <c>&gt; &gt; ```</c> to a bare <c>```</c>, which
    /// closed the outer block early and left the rest of the sample unscanned. Only the opener's
    /// depth is removed now. The differential probe computes its covered lines from the same scan,
    /// so this truncation could not have been caught downstream.
    /// </remarks>
    [Fact]
    public void A_Nested_Blockquote_Does_Not_Close_The_Outer_Fence()
    {
        const string Markdown = """
            > ```csharp
            > // the sample quotes a nested blockquote:
            > > ```
            > FlexColumn(children).Padding(16)
            > ```
            """;

        var snippet = Assert.Single(AgentKitDocCorpus.ExtractFences("fixture/nested.md", Markdown));

        // Truncating at the nested line would drop this, leaving the sample uninspected.
        Assert.Contains("FlexColumn(children).Padding(16)", snippet.Text, StringComparison.Ordinal);

        // One level is still stripped, so the nested quote survives as content rather than syntax.
        Assert.Contains("> ```", snippet.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A blockquoted fence ends when its container does, even if it is never closed.
    /// </summary>
    /// <remarks>
    /// The close scan stripped up to the opening depth without requiring that depth to still be
    /// present, so an unclosed <c>&gt; ```text</c> absorbed a following top-level
    /// ```` ```csharp ```` block and that sample shipped unscanned. The completeness fact derives
    /// its covered set from the same scan, so it could not have seen it either.
    /// </remarks>
    [Fact]
    public void An_Unclosed_Blockquoted_Fence_Does_Not_Swallow_What_Follows()
    {
        const string Markdown = """
            > ```text
            > an unclosed quoted block

            ```csharp
            FlexColumn(children).FlexPadding(16)
            ```
            """;

        var snippet = Assert.Single(AgentKitDocCorpus.ExtractFences("fixture/exit.md", Markdown));

        Assert.Equal("FlexColumn(children).FlexPadding(16)", snippet.Text);
    }

    /// <summary>
    /// An unclosed fence inside a list item does not swallow the block that follows the list.
    /// </summary>
    /// <remarks>
    /// The counterpart of the blockquote rule above. Ending a region only at a closing fence let an
    /// unclosed indented <c>```text</c> under a bullet absorb the next top-level C# block, which
    /// then shipped unscanned — and <c>Every_CSharp_Fence_In_The_Corpus_Is_Extracted</c> derives
    /// its covered set from this same scan, so it could not have reported the gap either.
    /// </remarks>
    [Fact]
    [Trait("Category", "AgentKitDocGate")]
    public void An_Unclosed_Fence_In_A_List_Item_Does_Not_Swallow_What_Follows()
    {
        const string Markdown = """
            - a bullet

              ```text
              an unclosed block inside the item

            ```csharp
            FlexColumn(children).FlexPadding(16)
            ```
            """;

        var snippet = Assert.Single(AgentKitDocCorpus.ExtractFences("fixture/listexit.md", Markdown));

        Assert.Equal("FlexColumn(children).FlexPadding(16)", snippet.Text);

        // Negative control: a *closed* block in the same position must keep its body intact, so
        // the rule above is ending unterminated regions rather than truncating every indented one.
        const string Closed = """
            - a bullet

              ```csharp
              FlexColumn(children).FlexPadding(16)
              ```
            """;

        Assert.Equal(
            "FlexColumn(children).FlexPadding(16)",
            Assert.Single(AgentKitDocCorpus.ExtractFences("fixture/listclosed.md", Closed)).Text);
    }

    /// <summary>
    /// Both facts require a marked counterexample to name its remedy.
    /// </summary>
    /// <remarks>
    /// This is the condition that closes the #1119 gap, and on the wrapper fact nothing else can
    /// cover for it: the wrapper is a legal receiver, so no other check objects to the sample at
    /// all. Tested directly because the corpus currently produces zero wrapper findings, which
    /// would leave the branch unexecuted and the guard vacuous.
    /// </remarks>
    [Theory]
    [InlineData("Use .FlexPadding(16) instead.", "FlexPadding", true)]
    [InlineData("Never wrap in a Border just for padding.", "FlexPadding", false)]
    [InlineData("anything at all", null, true)]
    public void A_Counterexample_Must_Name_Its_Remedy(string documentText, string? replacement, bool ok)
    {
        Assert.Equal(ok, AgentKitDocGateTests.NamesRemedy(documentText, replacement));
    }

    /// <summary>
    /// Floors over the corpus and the reflection behind it. Each one turns a silent collapse — a
    /// glob that stopped matching, a factory map that resolved to nothing, a fence parser that
    /// stopped recognising ```` ```csharp ```` — into a failure.
    /// </summary>
    /// <remarks>
    /// The thresholds are set well under the measured values (63 documents, 376 snippets, 348
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
            .Select(entry => $"{entry.Pattern} → {entry.PackagePath}")
            .ToList();

        Assert.True(
            empty.Count == 0,
            "An agentkit/ pack glob matches no file on disk, so the NuGet ships without it and this " +
            "gate never inspects it:\n  " + string.Join("\n  ", empty));

        // Granularity control. The guard above is per-glob, not per-item, and that difference is
        // the whole point: an item's `*.md` half can go dead while its `*.cs` half still matches,
        // and an item-level check stays green through it. Asserting no entry carries a `;` is what
        // pins the split — comparing entry and item counts would not, because several agentkit
        // items legitimately share one PackagePath.
        Assert.DoesNotContain(entries, entry => entry.Pattern.Contains(';', StringComparison.Ordinal));

        // ...and the assertion above is only meaningful while some item really does carry several
        // globs. Today that is the recipes item, `skills\recipes\*.md;skills\recipes\*.cs`.
        var repoRootPath = Path.Combine(AgentKitCorpus.RepoRoot, "src", "Reactor", "Reactor.csproj");
        Assert.Contains(
            AgentKitDocCorpus.AgentKitIncludes(File.ReadAllText(repoRootPath)),
            include => include.Contains(';', StringComparison.Ordinal));

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
    /// silently skipped. The aggregate snippet floor did <b>not</b> catch it and could not: 374
    /// blocks clear a floor of 150 just as comfortably as 376 do. A floor bounds the corpus from
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
            var markdown = File.ReadAllText(Path.Combine(repoRoot, document.Replace('/', Path.DirectorySeparatorChar)));
            var lines = markdown.Replace("\r\n", "\n").Split('\n');

            extracted.TryGetValue(document, out var snippets);
            snippets ??= new List<AgentKitSnippet>();

            // Lines inside *any* fenced block, C# or not. A ````csharp` line quoted as literal text
            // — inside a ```text block demonstrating fences, or inside a longer ```` block — is
            // content the scanner already accounted for, not a block it skipped. Scoping this to
            // extracted C# bodies alone would fail the moment a document explained fence syntax.
            var covered = AgentKitDocCorpus.Fences(markdown)
                .Where(region => region.BodyEndLine >= region.BodyStartLine)
                .SelectMany(region => Enumerable.Range(region.BodyStartLine, region.BodyEndLine - region.BodyStartLine + 1))
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
    /// A C# fence quoted as literal text inside another block is content, not a missed block.
    /// </summary>
    /// <remarks>
    /// <see cref="Every_CSharp_Fence_In_The_Corpus_Is_Extracted"/> compares an indentation-blind
    /// probe against the extractor, and the probe has no idea about block structure. Scoping its
    /// exclusions to extracted <em>C#</em> bodies would fail the moment a document explained fence
    /// syntax inside a <c>```text</c> block — a CI failure on correct documentation, triggered by
    /// writing about the very feature this gate depends on. Excluding every fence body, whatever
    /// its language, is what makes the comparison sound.
    /// </remarks>
    [Fact]
    public void A_Fence_Quoted_Inside_Another_Block_Is_Not_A_Missed_Snippet()
    {
        const string Markdown = """
            # Heading

            ```text
            Write a C# sample like this:
            ```csharp
            FlexColumn(children).Padding(16)
            ```

            ````csharp
            // a real one, inside a longer fence
            ```csharp
            Border(child).Padding(8)
            ````
            """;

        var fences = AgentKitDocCorpus.Fences(Markdown);
        var extracted = AgentKitDocCorpus.ExtractFences("fixture/quoted.md", Markdown);

        var covered = fences
            .Where(r => r.BodyEndLine >= r.BodyStartLine)
            .SelectMany(r => Enumerable.Range(r.BodyStartLine, r.BodyEndLine - r.BodyStartLine + 1))
            .ToHashSet();

        var openedAt = extracted.Select(s => s.StartLine - 1).ToHashSet();
        var lines = Markdown.Replace("\r\n", "\n").Split('\n');

        var missed = new List<int>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!AgentKitDocCorpus.CSharpFenceProbe.IsMatch(lines[i]))
                continue;

            var line = i + 1;
            if (!covered.Contains(line) && !openedAt.Contains(line))
                missed.Add(line);
        }

        Assert.Empty(missed);

        // The probe must have had something to find here, or this proves nothing about quoting.
        Assert.Contains(lines, line => AgentKitDocCorpus.CSharpFenceProbe.IsMatch(line));
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
