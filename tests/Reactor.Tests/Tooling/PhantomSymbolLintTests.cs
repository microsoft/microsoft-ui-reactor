using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Cli.Docs;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Tooling;

/// <summary>
/// Gate for <see cref="PhantomSymbolLint"/> (<c>REACTOR_DOC_PHANTOM_001</c>).
///
/// <para>Reactor validates executable code (the compiler, plus the
/// <c>REACTOR_DYM_*</c> analyzers) and validates snippet-backed doc blocks
/// (they compile as part of the doc apps). Everything else in the docset is
/// unchecked, and all four unchecked surfaces have shipped a phantom API:
/// bare <c>```csharp</c> template blocks, example source inside <b>string
/// literals</b> in doc apps, gallery <c>SourceCode</c> strings, and <c>///</c>
/// doc comments in <c>src/</c> — the last of which ship in <c>Reactor.xml</c>
/// as IntelliSense and have been observed re-seeding the guide templates that
/// quote them.</para>
///
/// <para>Every positive case below is a <b>real historical defect</b> with
/// provenance, not a synthetic fixture. Every negative case is a
/// false-positive class that was actually observed while spiking the matcher —
/// including two that are invisible until you look: a case-insensitive
/// <c>Text\(</c> matches the ordinary English phrase "…text (for…", and a
/// lookbehind that excludes <c>&gt;</c> silently misses the <c>&lt;c&gt;Optional.Of(</c>
/// form the phantom almost always appears in.</para>
/// </summary>
public sealed class PhantomSymbolLintTests
{
    readonly ITestOutputHelper _output;

    public PhantomSymbolLintTests(ITestOutputHelper output) => _output = output;

    static List<PhantomFinding> LintCSharpDoc(string line) =>
        PhantomSymbolLint.Lint("probe.cs", line, PhantomSymbolLint.Surface.CSharpDocComments);

    static List<PhantomFinding> LintMarkdown(string text) =>
        PhantomSymbolLint.Lint("probe.md.dt", text, PhantomSymbolLint.Surface.Markdown);

    static List<PhantomFinding> LintExample(string text) =>
        PhantomSymbolLint.Lint("probe.cs", text, PhantomSymbolLint.Surface.ExampleText);

    /// <summary>
    /// Real defects, each cited to where it shipped. If the matcher stops
    /// firing on any of these the rule has regressed into silence — which is
    /// the failure mode that matters, because a lint that never fires looks
    /// exactly like a clean tree.
    /// </summary>
    public static TheoryData<string, string, string> HistoricalDefects() => new()
    {
        // src/Reactor/Core/Element.cs:33 — shipped in Reactor.xml.
        { "Text", "/// Set via fluent extension methods: Text(\"hi\").Margin(10).Width(200)", "doc" },
        // src/Reactor/Core/Element.cs:340.
        { "Text", "/// Allows writing: VStack(\"Hello\", \"World\") instead of VStack(Text(\"Hello\"), Text(\"World\"))", "doc" },
        // src/Reactor/Elements/ElementExtensions.cs:1205 — inside <c>, not a cref.
        { "Text", "/// Usage: <c>Text(\"Hello\").Foreground(Theme.PrimaryText)</c>", "doc" },
        // src/Reactor/Hooks/UseMemoCells.cs:60.
        { "UseTheme", "/// var theme = ctx.UseTheme();", "doc" },
        // src/Reactor/Core/Animation.cs:49.
        { "WithImplicitTransition", "/// per-element modifiers such as <c>WithImplicitTransition</c>. Scoping", "doc" },
        // src/Reactor/Core/Element.cs:111.
        { "WithOpacityTransition", "/// Set via fluent extension methods: Rectangle().WithOpacityTransition()", "doc" },
        // src/Reactor/Core/Element.cs:124.
        { "WithThemeTransitions", "/// Set via fluent extension methods: VStack(children).WithThemeTransitions(...)", "doc" },
        // src/Reactor/Core/Optional.cs:57 — the <c>-wrapped form.
        { "Optional.Of", "/// <c>Optional.Of(null)</c>, not <see cref=\"Unset\"/>; use <see cref=\"Unset\"/>", "doc" },
    };

    [Theory]
    [MemberData(nameof(HistoricalDefects))]
    public void Fires_OnRealDocCommentDefects(string phantom, string line, string _)
    {
        var findings = LintCSharpDoc(line);
        Assert.True(
            findings.Any(f => f.Message.Contains($"'{phantom}'")),
            $"Expected '{phantom}' to be reported for: {line}");
    }

    /// <summary>
    /// docs/_pipeline/apps/docking/App.cs:58 and :86 as they stood before
    /// commit 6e80de46. Both sat inside string literals in a file that built
    /// green in Release/x64 for months — the compiler cannot see them, which is
    /// the entire reason this lint exists. They were inside <c>snippet:</c>
    /// regions, so they published to docs/guide/docking.md.
    /// </summary>
    [Fact]
    public void Fires_OnPhantomInsideDocAppStringLiteral()
    {
        var prefix = "TextBlock(\"    public override Element Render() => Text(\\\"Hello\\\");\"),";
        Assert.Contains(LintExample(prefix), f => f.Message.Contains("'Text'"));
    }

    /// <summary>
    /// docs/_pipeline/templates/layout.md.dt:206 and :229 — bare (never
    /// compiled) csharp fences wrapped in <c>&lt;!-- ai:lock --&gt;</c>. The lock
    /// is why they rotted: authors and AI passes skip locked regions, so the
    /// error is never revisited. The lint must reach inside the lock.
    /// </summary>
    [Fact]
    public void Fires_InsideAiLockedTemplateFence()
    {
        var body = "<!-- ai:lock -->\n```csharp\nText(\"Centered\").HAlign(HorizontalAlignment.Center)\n```\n<!-- /ai:lock -->\n";
        Assert.Contains(LintMarkdown(body), f => f.Message.Contains("'Text'"));
    }

    // ── False-positive classes ────────────────────────────────────────────

    /// <summary>
    /// Observed at src/Reactor/Accessibility/AccessibilityScanner.cs:94. A
    /// case-INSENSITIVE probe matches the English words "text (" here. C#
    /// identifiers are case-sensitive, so the matcher must be too.
    /// </summary>
    [Fact]
    public void Silent_OnEnglishProseContainingLowercaseText()
    {
        Assert.Empty(LintCSharpDoc("/// <summary>Placeholder text (for TextBoxElement etc.).</summary>"));
    }

    /// <summary>
    /// Receiver-qualified members named Text are all real: CellRenderers.Text
    /// and Editors.Text (both Func&lt;object, Element&gt;), DragData.Text, and
    /// D3Charts.Text. Qualification alone must exclude them.
    /// </summary>
    [Theory]
    [InlineData("/// <c>CellRenderers.Text(\"C2\")</c> formats the cell.")]
    [InlineData("/// <c>DragData.Text(\"payload\")</c> builds the drag payload.")]
    [InlineData("/// <c>D3Charts.Text(16, 16, \"hi\")</c> draws positioned text.")]
    public void Silent_OnReceiverQualifiedRealMembers(string line) =>
        Assert.Empty(LintCSharpDoc(line));

    /// <summary>
    /// Even unqualified — under <c>using static D3Charts</c> — the real
    /// D3Charts.Text is positional (x, y, text), so its first argument is
    /// numeric. The string-literal-first-argument discriminator excludes it.
    /// </summary>
    [Fact]
    public void Silent_OnUnqualifiedPositionalD3Text() =>
        Assert.Empty(LintCSharpDoc("/// Text(16, 16, \"hi\") draws at a canvas coordinate."));

    /// <summary>
    /// ElementExtensions.cs carried <c>Text(statusMessage).LiveRegion(…)</c> — a
    /// real phantom fixed on this branch that the string-literal discriminator
    /// alone did not match, so nothing would have caught its reintroduction.
    /// A single identifier argument cannot be D3Charts.Text, which is positional.
    /// </summary>
    [Fact]
    public void Fires_OnDynamicArgumentText() =>
        Assert.Contains(
            LintCSharpDoc("/// <example>Text(statusMessage).LiveRegion(AutomationLiveSetting.Polite)</example>"),
            f => f.Message.Contains("'Text'"));

    /// <summary>
    /// The dynamic-argument arm must stay anchored to the unqualified spelling
    /// and to a *single* argument, or it would swallow the qualified renderers
    /// and D3's positional overload.
    /// </summary>
    [Fact]
    public void Silent_OnQualifiedOrMultiArgDynamicText()
    {
        Assert.Empty(LintCSharpDoc("/// CellRenderers.Text(row) renders a cell."));
        Assert.Empty(LintCSharpDoc("/// Text(x, y, label) draws at a coordinate."));
    }

    /// <summary>
    /// The opening quote of an embedded example takes three spellings depending
    /// on how the example is carried. All three must fire — matching only the
    /// plain form would miss the escaped spelling, which is the exact form the
    /// docking defect took.
    /// </summary>
    [Theory]
    [InlineData("Text(\"Hello\")")]            // plain
    [InlineData("Text(\\\"Hello\\\")")]        // escaped inside a C# string literal
    [InlineData("Text(\"\"Hello\"\")")]        // doubled inside a verbatim string
    public void Fires_OnEveryEmbeddedQuoteSpelling(string example) =>
        Assert.Contains(LintExample(example), f => f.Message.Contains("'Text'"));

    /// <summary>
    /// <c>cref</c> is compiler-validated (CS1574 fires on an unresolvable
    /// target), so it is not an unvalidated surface and must never be matched.
    /// </summary>
    [Fact]
    public void Silent_OnCompilerValidatedCref() =>
        Assert.Empty(LintCSharpDoc("/// Defaults to <see cref=\"Optional{T}.Unset\"/>; see <see cref=\"Optional{T}.Of\"/>."));

    /// <summary>Correct spelling must not be flagged.</summary>
    [Fact]
    public void Silent_OnCorrectlySpelledOptionalOf() =>
        Assert.Empty(LintCSharpDoc("/// use <c>Optional&lt;int&gt;.Of(-1)</c> to force no selection."));

    /// <summary>
    /// The warning-sentence problem, from a real page: charting.md.dt now says
    /// "no core Text(...) element factory exists" in order to inoculate the
    /// reader. A naive rule flags the very sentence that fixes the problem.
    /// Plain prose is never linted, so the warning stays silent.
    /// </summary>
    [Fact]
    public void Silent_OnProseThatNamesAPhantomToWarnAgainstIt()
    {
        var body = "Reactor has no core Text(\"x\") element factory — use TextBlock(...).\n";
        Assert.Empty(LintMarkdown(body));
    }

    /// <summary>
    /// Inline code spans in prose <b>are</b> linted. Fencing was never the only
    /// way a doc endorses an API: <c>`Optional.Of(x)`</c> in a sentence reads as
    /// real code to both a human and an assistant, and four such spans were
    /// live in the docset while the fence-only rule reported clean.
    /// </summary>
    [Fact]
    public void Fires_OnPhantomInsideInlineCodeSpanInProse()
    {
        var body = "Prefer the generic form; `Optional.Of(x)` will not bind.\n";
        Assert.Contains(LintMarkdown(body), f => f.Message.Contains("'Optional.Of'"));
    }

    /// <summary>
    /// …but only inside the span. The surrounding sentence must stay immune, or
    /// the case-sensitive <c>Text\(</c> pattern starts matching English again —
    /// the false-positive class the whole matcher was tuned to avoid.
    /// </summary>
    [Fact]
    public void Silent_OnEnglishAroundAnInlineCodeSpan()
    {
        var body = "Placeholder text (for `TextBlock` etc.) is set separately.\n";
        Assert.Empty(LintMarkdown(body));
    }

    /// <summary>
    /// A prose span that must name the phantom takes the same scoped marker the
    /// doc-comment surface uses — this is how the two live warning sentences in
    /// advanced.md.dt and migration/050-optional-t.md.dt stay green.
    /// </summary>
    [Fact]
    public void ScopedSkipMarker_SilencesAnInlineCodeSpanInProse()
    {
        var body = "<!-- phantom:skip \"Optional.Of\" -->\nThere is no `Optional.Of(x)`; use `Optional<int>.Of(-1)`.\n";
        Assert.Empty(LintMarkdown(body));
    }

    /// <summary>
    /// An unpaired backtick opens no span, so a lone "`" in prose cannot drag
    /// the rest of the line into the linted region.
    /// </summary>
    [Fact]
    public void Silent_OnUnpairedBacktickFollowedByProseText()
    {
        var body = "A stray ` tick then Text(\"x\") in plain prose.\n";
        Assert.Empty(LintMarkdown(body));
    }

    /// <summary>
    /// Where prose is not enough — a doc comment that must name the phantom —
    /// the scoped opt-out marker mirrors the pipeline's existing
    /// <c>&lt;!-- xlink:skip --&gt;</c> convention and is valid in both Markdown
    /// and XML doc comments.
    /// </summary>
    [Fact]
    public void Silent_WhenScopedSkipMarkerNamesThePhantom() =>
        Assert.Empty(LintCSharpDoc("/// <!-- phantom:skip \"Text\" --> There is no Text(\"x\") factory."));

    /// <summary>A scoped marker must only silence the phantom it names.</summary>
    [Fact]
    public void ScopedSkipMarker_DoesNotSilenceOtherPhantoms()
    {
        var findings = LintCSharpDoc("/// <!-- phantom:skip \"Text\" --> ctx.UseTheme() and Text(\"x\")");
        Assert.Contains(findings, f => f.Message.Contains("'UseTheme'"));
        Assert.DoesNotContain(findings, f => f.Message.Contains("'Text'"));
    }

    /// <summary>
    /// Executable code is explicitly out of scope — the compiler and the
    /// REACTOR_DYM_* analyzers own it, and firing there is the noise path that
    /// gets a rule globally disabled. A non-<c>///</c> line must be ignored.
    /// </summary>
    [Fact]
    public void Silent_OnExecutableCode() =>
        Assert.Empty(LintCSharpDoc("        var e = Text(\"Hello\");"));

    /// <summary>
    /// Guards the matcher against decaying into a tautology. If the phantom
    /// table were emptied, every "must fire" fact above would still pass
    /// trivially only if it asserted non-null; they assert a named phantom, and
    /// this asserts the table is populated and each entry is distinct.
    /// </summary>
    /// <summary>
    /// The phantoms this PR fixed by hand. Without a table entry each one is
    /// free to come back: the fix lives in a doc, and nothing but this rule
    /// reads that doc again. Every spelling below was verified absent from
    /// <c>skills/reactor.api.txt</c> at word-boundary precision — a substring
    /// check reports <c>VStackElement</c> as present, because
    /// <c>LazyVStackElement&lt;T&gt;</c> contains it.
    /// </summary>
    [Theory]
    [InlineData("VStackElement", "var e = (VStackElement)tree;")]
    [InlineData("HStackElement", "var e = (HStackElement)tree;")]
    [InlineData("RenderContext.Current", "var c = RenderContext.Current;")]
    [InlineData("ElementDescription.Of", "ElementDescription.Of(\"row\")")]
    [InlineData("A11Y_KEYBOARD_001", "suppress A11Y_KEYBOARD_001 here")]
    [InlineData("ProgressBar", "return ProgressBar(0.5);")]
    public void Fires_OnPhantomsThisPrFixedByHand(string phantom, string line) =>
        Assert.Contains(LintExample(line), f => f.Message.Contains($"'{phantom}'"));

    /// <summary>
    /// The real neighbours each of those patterns sits next to. These are the
    /// spellings a careless pattern would take down with it, and every one of
    /// them resolves in <c>reactor.api.txt</c>.
    /// </summary>
    [Theory]
    [InlineData("LazyVStackElement<T> e = LazyVStack(items, build);")]
    [InlineData("StackElement s = VStack(a, b);")]
    [InlineData("ProgressElement.Set(Action<ProgressBar> configure)")]
    [InlineData("p.Set(pb => pb.IsIndeterminate = true);")]
    [InlineData("var native = new ProgressBar();")]
    [InlineData("ctx.UseState(0); // ctx, not an ambient accessor")]
    public void Silent_OnTheRealNeighboursOfThosePhantoms(string line) =>
        Assert.Empty(LintExample(line));

    /// <summary>
    /// A prose marker must not leak into a later fenced block. It previously
    /// survived until the next <i>closing</i> fence, so a marker written above a
    /// paragraph also silenced the whole unrelated snippet after it — the gate
    /// reported clean on code it had stopped reading. Scoping is per paragraph.
    /// </summary>
    [Fact]
    public void ScopedSkipMarker_DoesNotLeakIntoALaterFencedBlock()
    {
        var body =
            "<!-- phantom:skip \"Optional.Of\" -->\n" +
            "There is no `Optional.Of(x)`; spell the type argument.\n" +
            "\n" +
            "```csharp\n" +
            "var v = Optional.Of(5.0);\n" +
            "```\n";

        var findings = LintMarkdown(body);
        Assert.Contains(findings, f => f.Line == 5);
        Assert.DoesNotContain(findings, f => f.Line == 2);
    }

    /// <summary>
    /// The documented use still works: a marker attached directly above a fence,
    /// with no blank line between, annotates that fence.
    /// </summary>
    [Fact]
    public void ScopedSkipMarker_StillAnnotatesTheFenceItSitsOn()
    {
        var body =
            "<!-- phantom:skip \"Optional.Of\" -->\n" +
            "```csharp\n" +
            "var v = Optional.Of(5.0);\n" +
            "```\n";

        Assert.Empty(LintMarkdown(body));
    }

    /// <summary>
    /// The member-qualified blind spot. Every other pattern excludes a leading
    /// '.' so real members like <c>D3Charts.Text(</c> survive, which meant a
    /// phantom <i>receiver</i> — <c>UI.Text()</c> in <c>skills/design.md</c> —
    /// passed the agent-kit sweep while it reported clean. Matching on the
    /// receiver is what closes it.
    /// </summary>
    [Theory]
    [InlineData("compose with UI.Text(\"hi\") and UI.VStack(a, b)")]
    [InlineData("UI.Button(\"ok\")")]
    public void Fires_OnThePhantomUiFacade(string line) =>
        Assert.Contains(LintExample(line), f => f.Message.Contains("'UI.'"));

    /// <summary>
    /// The real namespaces this must not touch. Both put a '.' before <c>UI</c>
    /// and continue with another segment, so neither can match.
    /// </summary>
    [Theory]
    [InlineData("using Microsoft.UI.Xaml.Controls;")]
    [InlineData("var t = new Microsoft.UI.Xaml.Controls.TextBlock();")]
    [InlineData("using static Microsoft.UI.Reactor.Factories;")]
    [InlineData("Microsoft.UI.Reactor.Core.RenderContext ctx")]
    public void Silent_OnRealMicrosoftUiNamespaces(string line) =>
        Assert.Empty(LintExample(line));

    /// <summary>
    /// Expression-shaped single arguments. The identifier arm demands a ')'
    /// straight after the name, so every one of these slipped through on the
    /// uncompiled surfaces the gate exists to protect.
    /// </summary>
    [Theory]
    [InlineData("var e = Text(GetLabel());")]
    [InlineData("var e = Text(items[0]);")]
    [InlineData("var e = Text(model.Name ?? \"\");")]
    [InlineData("return Text(vm.Title());")]
    public void Fires_OnExpressionShapedArguments(string line) =>
        Assert.Contains(LintExample(line), f => f.Message.Contains("'Text'"));

    /// <summary>
    /// …and the widening must not reopen the prose class. Each of these is
    /// identifier-led only in the English sense; none is followed by '(', '['
    /// or '??', which is exactly what the new arms require.
    /// </summary>
    [Theory]
    [InlineData("Text (the element) is set separately.")]
    [InlineData("Placeholder text (for TextBoxElement etc.) is set separately.")]
    [InlineData("var e = D3Charts.Text(16, 16, \"hi\");")]
    [InlineData("var e = CellRenderers.Text(row);")]
    public void Silent_OnProseAndQualifiedMembersAfterWidening(string line) =>
        Assert.Empty(LintExample(line));

    /// <summary>
    /// CommonMark delimits an inline span with a <i>run</i> of backticks and
    /// closes it on a run of equal length, so a phantom can hide inside a
    /// multi-backtick span. Toggling on each individual backtick mis-parses
    /// these and masks the phantom away as prose.
    /// </summary>
    [Theory]
    [InlineData("Write ``Optional.Of(`x`)`` to see it fail.")]
    [InlineData("Write ```Optional.Of(y)``` in a triple span.")]
    public void Fires_OnPhantomInsideAMultiBacktickSpan(string body) =>
        Assert.Contains(LintMarkdown(body + "\n"), f => f.Message.Contains("'Optional.Of'"));

    /// <summary>An unterminated run opens nothing, so prose after it stays inert.</summary>
    [Fact]
    public void Silent_OnUnterminatedBacktickRun() =>
        Assert.Empty(LintMarkdown("A stray ``run then Text(\"x\") in prose.\n"));

    [Fact]
    public void PhantomTable_IsPopulatedAndDistinct()
    {
        Assert.NotEmpty(PhantomSymbolLint.Phantoms);
        var names = PhantomSymbolLint.Phantoms.Select(p => p.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    /// <summary>
    /// The durable gate: sweep every <c>///</c> doc comment in <c>src/</c> with
    /// the shipping matcher and hold the result against an explicit budget.
    /// <c>src/</c> is the propagation source — these comments ship in
    /// <c>Reactor.xml</c> as IntelliSense, and guide templates have been
    /// observed quoting them almost verbatim — so this is the surface where
    /// letting a new phantom in costs the most.
    ///
    /// <para><b>Ceiling, not equality.</b> The budget is a maximum per
    /// (file, phantom): a new file, a new phantom in a known file, or an
    /// increased count fails; a decrease passes and is reported as a stale
    /// entry to trim. Equality would be tidier bookkeeping but it turns
    /// "somebody fixed a phantom" into a red build, landing on whoever merges
    /// with no context — and this backlog is being cleared concurrently across
    /// several branches. Anti-drift is preserved in the direction that
    /// matters: the count can never silently grow.</para>
    ///
    /// <para><b>Keyed per (file, phantom), not per file.</b> A per-file total
    /// would let five fixed <c>Text</c> occurrences mask five newly introduced
    /// <c>Optional.Of</c> ones in the same file and still pass. Each phantom
    /// class is bounded independently.</para>
    ///
    /// <para>Every entry is a real defect, not a suppression — each is an
    /// unwritable spelling shipped in <c>Reactor.xml</c>. They are budgeted
    /// rather than fixed here because each file is owned by a concurrent
    /// branch; fixing them from this branch would collide.</para>
    /// </summary>
    [Fact]
    public void SrcDocComments_StayWithinTheKnownPhantomBudget()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        var src = global::System.IO.Path.Combine(root!, "src");
        Assert.True(global::System.IO.Directory.Exists(src), $"src/ not found under {root}");

        var actual = new SortedDictionary<string, int>(global::System.StringComparer.Ordinal);
        foreach (var file in global::System.IO.Directory.EnumerateFiles(src, "*.cs", global::System.IO.SearchOption.AllDirectories))
        {
            var findings = PhantomSymbolLint.Lint(
                file,
                global::System.IO.File.ReadAllText(file),
                PhantomSymbolLint.Surface.CSharpDocComments);
            if (findings.Count == 0) continue;

            var rel = global::System.IO.Path.GetRelativePath(root!, file).Replace('\\', '/');
            foreach (var f in findings)
            {
                // The message opens with 'Name' — the phantom that fired.
                var phantom = f.Message.Split('\'')[1];
                var key = $"{rel}::{phantom}";
                actual[key] = actual.TryGetValue(key, out var n) ? n + 1 : 1;
            }
        }

        var over = new List<string>();
        foreach (var kv in actual)
        {
            if (!PhantomBudget.TryGetValue(kv.Key, out var budget))
                over.Add($"  NEW    {kv.Key} = {kv.Value}   (no budget entry — this is new rot)");
            else if (kv.Value > budget)
                over.Add($"  GREW   {kv.Key} = {kv.Value}   (budget {budget})");
        }

        var stale = PhantomBudget.Keys
            .Where(k => !actual.ContainsKey(k) || actual[k] < PhantomBudget[k])
            .OrderBy(k => k, global::System.StringComparer.Ordinal)
            .ToList();
        foreach (var k in stale)
        {
            var now = actual.TryGetValue(k, out var n) ? n : 0;
            _output.WriteLine($"stale budget entry (fixed elsewhere — trim it): {k} budget {PhantomBudget[k]}, now {now}");
        }

        if (over.Count > 0)
        {
            Assert.Fail(
                "REACTOR_DOC_PHANTOM_001: a phantom API entered a /// doc comment in src/.\n" +
                "These ship in Reactor.xml as IntelliSense and are quoted by guide templates,\n" +
                "so nothing else in the toolchain will catch them.\n\n" +
                string.Join("\n", over) +
                "\n\nFix the doc comment. If the text names the phantom in order to warn\n" +
                "against it, add <!-- phantom:skip \"Name\" --> on that line instead.\n" +
                "Only raise the budget in PhantomBudget if you are deliberately accepting it.");
        }
    }

    /// <summary>
    /// Maximum tolerated occurrences per (repo-relative path, phantom), measured
    /// on <c>main</c> when this gate landed. Shrink entries as the concurrent
    /// fixes merge; the test reports stale entries on every run.
    /// </summary>
    static readonly SortedDictionary<string, int> PhantomBudget = new(global::System.StringComparer.Ordinal)
    {
        // Owned by azchohfi-guide-sample-audit @ a38a6b44 (Text + transitions).
        ["src/Reactor/Core/Animation.cs::WithImplicitTransition"] = 1,
        ["src/Reactor/Core/Element.cs::Text"] = 6,
        ["src/Reactor/Core/Element.cs::WithOpacityTransition"] = 2,
        ["src/Reactor/Core/Element.cs::WithThemeTransitions"] = 2,
        ["src/Reactor/Elements/ElementExtensions.cs::Text"] = 4,
        ["src/Reactor/Elements/GridExtensions.cs::Text"] = 1,
        ["src/Reactor/Hooks/UseFocusTrap.cs::Text"] = 1,
        ["src/Reactor/Hosting/ReactorHostControl.cs::Text"] = 1,
        ["src/Reactor.Cli/Loc/KeyNamer.cs::Text"] = 1,

        // Owned by azchohfi-guide-sample-audit @ c4cc8f41 (Optional.Of).
        ["src/Reactor/Core/Element.cs::Optional.Of"] = 11,
        ["src/Reactor.Analyzers/NoOpModifierAnalyzer.cs::Optional.Of"] = 1,
        ["src/Reactor.Analyzers/OptionalSentinelAnalyzer.cs::Optional.Of"] = 3,

        // Owned by the internals branch.
        ["src/Reactor/Core/Optional.cs::Optional.Of"] = 1,

        // Owned by the core-framework branch.
        ["src/Reactor/Hooks/UseMemoCells.cs::UseTheme"] = 1,
    };
}
