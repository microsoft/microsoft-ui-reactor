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
    /// Markdown prose is outside every fence, so it is never linted.
    /// </summary>
    [Fact]
    public void Silent_OnProseThatNamesAPhantomToWarnAgainstIt()
    {
        var body = "Reactor has no core `Text(\"x\")` element factory — use `TextBlock(...)`.\n";
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
    [Fact]
    public void PhantomTable_IsPopulatedAndDistinct()
    {
        Assert.NotEmpty(PhantomSymbolLint.Phantoms);
        var names = PhantomSymbolLint.Phantoms.Select(p => p.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    /// <summary>
    /// The durable gate: sweep every <c>///</c> doc comment in <c>src/</c> with
    /// the shipping matcher and hold the result against an explicit allow-list.
    /// <c>src/</c> is the propagation source — these comments ship in
    /// <c>Reactor.xml</c> as IntelliSense, and guide templates have been
    /// observed quoting them almost verbatim — so this is the surface where
    /// letting a new phantom in costs the most.
    ///
    /// <para>The allow-list is per-file counts of occurrences known at the time
    /// this landed, each already owned by an in-flight fix on another branch.
    /// It fails in <b>both</b> directions on purpose: a new phantom anywhere
    /// fails, and clearing a known one also fails, so the list shrinks
    /// deliberately instead of drifting.</para>
    /// </summary>
    [Fact]
    public void SrcDocComments_ContainOnlyTheKnownPhantomBacklog()
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
            var name = global::System.IO.Path.GetFileName(file);
            actual[name] = actual.TryGetValue(name, out var n) ? n + findings.Count : findings.Count;
        }

        // Known backlog at the time this gate landed. Every entry is a real
        // defect, not a suppression: each is an unwritable spelling shipped in
        // Reactor.xml. They are listed rather than fixed here because each file
        // is owned by a concurrent branch — fixing them from this branch would
        // collide. Shrink this list as those land.
        var expected = new SortedDictionary<string, int>(global::System.StringComparer.Ordinal)
        {
            ["Animation.cs"] = 1,             // WithImplicitTransition
            ["Element.cs"] = 21,              // Text ×6, transitions ×4, Optional.Of ×11
            ["ElementExtensions.cs"] = 4,     // Text
            ["GridExtensions.cs"] = 1,        // Text
            ["KeyNamer.cs"] = 1,              // Text
            ["NoOpModifierAnalyzer.cs"] = 1,  // Optional.Of
            ["Optional.cs"] = 1,              // Optional.Of
            ["OptionalSentinelAnalyzer.cs"] = 3, // Optional.Of
            ["ReactorHostControl.cs"] = 1,    // Text
            ["UseFocusTrap.cs"] = 1,          // Text
            ["UseMemoCells.cs"] = 1,          // UseTheme
        };

        Assert.Equal(expected, actual);
    }
}
