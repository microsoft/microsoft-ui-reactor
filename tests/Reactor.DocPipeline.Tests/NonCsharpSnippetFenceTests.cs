using System.Text.RegularExpressions;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Guards the language-agnostic snippet fence.
/// <para>
/// The two halves of the snippet pipeline disagreed silently.
/// <see cref="CompileCommand.ExtractSnippetRefs"/> (discovery) matches
/// <c>snippet="..."</c> inside <em>any</em> fence, so a non-C# fence was
/// discovered, extracted, validated, and logged as <c>✓ resolved</c>.
/// <see cref="DocAssembler"/> (substitution) hard-coded <c>```csharp</c>, so the
/// same fence was never replaced. The three ```xml project-shape fences in
/// <c>packaging.md</c> therefore rendered as <em>empty</em> code blocks — under
/// prose that promised to show the shape and then discussed properties the
/// reader could not see — with the raw <c>snippet="..."</c> attribute left in the
/// fence info string.
/// </para>
/// <para>
/// Nothing failed: no error, no warning, and a clean resolve log. That is the
/// defect class these tests exist for, so each one is written to fail if the
/// language capture is reverted to a <c>csharp</c> literal.
/// </para>
/// </summary>
public class NonCsharpSnippetFenceTests
{
    private static readonly Dictionary<string, ScreenshotInfo> NoScreenshots = new();

    private static Dictionary<string, SnippetExtractor.Snippet> OneSnippet(string id, string code) =>
        new() { [id] = new SnippetExtractor.Snippet(id, id, code, "/virtual/file", 1) };

    [Fact]
    public void Xml_fence_is_substituted_and_keeps_its_language()
    {
        const string id = "source:samples/App/App.csproj#shape";
        const string code = "<PropertyGroup>\n  <UseWinUI>true</UseWinUI>\n</PropertyGroup>";
        var body = $"```xml snippet=\"{id}\"\n```";

        var output = DocAssembler.Assemble(
            body, OneSnippet(id, code), NoScreenshots, out var errors, out _, null, null);

        Assert.Empty(errors);
        // The body must actually arrive. Before the fix this was the whole bug:
        // the fence stayed empty and the attribute survived.
        Assert.Contains("<UseWinUI>true</UseWinUI>", output);
        Assert.DoesNotContain("snippet=", output);
        // ...and it must arrive under `xml`, not relabelled `csharp`.
        Assert.Contains("```xml", output);
        Assert.DoesNotContain("```csharp", output);
    }

    [Fact]
    public void Language_is_carried_through_not_assumed()
    {
        // Differential isolation: identical snippet, identical id, identical
        // shape — the *only* difference is the fence language. If the emitted
        // language were a constant (either literal), these two outputs would be
        // equal and this fails. A test that only checked the xml case would pass
        // against a ````` + match.Groups[1] ```` bug that echoed the id instead.
        const string id = "shared/id";
        const string code = "value";

        var asXml = DocAssembler.Assemble(
            $"```xml snippet=\"{id}\"\n```", OneSnippet(id, code), NoScreenshots, out _, out _, null, null);
        var asCsharp = DocAssembler.Assemble(
            $"```csharp snippet=\"{id}\"\n```", OneSnippet(id, code), NoScreenshots, out _, out _, null, null);

        Assert.NotEqual(asXml, asCsharp);
        Assert.Contains("```xml", asXml);
        Assert.Contains("```csharp", asCsharp);
        // Both still deliver the code — the language is the only axis that moved.
        Assert.Contains(code, asXml);
        Assert.Contains(code, asCsharp);
    }

    [Fact]
    public void Csharp_fence_behaviour_is_unchanged()
    {
        // The regression side of the change: generalising the language must not
        // alter the overwhelmingly common case.
        const string id = "hooks/usestate";
        const string code = "var (count, setCount) = UseState(0);";

        var output = DocAssembler.Assemble(
            $"```csharp snippet=\"{id}\"\n```", OneSnippet(id, code), NoScreenshots, out var errors, out _, null, null);

        Assert.Empty(errors);
        Assert.Equal($"```csharp\n{code}\n```", output.ReplaceLineEndings("\n").Trim());
    }

    [Fact]
    public void Missing_snippet_still_reports_an_error_for_any_language()
    {
        // Fail-closed: a widened regex must not turn an unresolvable non-C#
        // reference into a silent pass. Without this, a typo'd xml region would
        // be indistinguishable from a correct one.
        var output = DocAssembler.Assemble(
            "```xml snippet=\"nope/missing\"\n```", new(), NoScreenshots, out var errors, out _, null, null);

        Assert.Contains(errors, e => e.Contains("nope/missing"));
        Assert.Contains("snippet=", output); // left verbatim so the author sees it
    }

    [Theory]
    [InlineData("xml", "<!-- Project shape -->")]
    [InlineData("xaml", "<!-- Project shape -->")]
    [InlineData("html", "<!-- Project shape -->")]
    [InlineData("csharp", "// Project shape")]
    [InlineData("json", "// Project shape")]
    [InlineData("powershell", "# Project shape")]
    [InlineData("bash", "# Project shape")]
    [InlineData("yaml", "# Project shape")]
    [InlineData("sql", "-- Project shape")]
    public void Title_uses_a_comment_syntax_valid_for_the_language(string language, string expected)
    {
        // `title=` previously emitted `// Title` unconditionally. Inside an xml
        // fence that is not a comment; and defaulting every non-XML language to
        // `//` just moved the bug — PowerShell, shell and YAML all read `//` as
        // code, so the "title" would corrupt the example it labels.
        Assert.Equal(expected, DocAssembler.TitleComment(language, "Project shape"));
    }

    [Fact]
    public void Title_on_an_unmapped_language_is_reported_rather_than_guessed()
    {
        // Fail-closed: emitting a plausible-looking comment for a language we do not
        // actually know is how the `//`-for-everything bug happened. An unmapped
        // language must surface as an error, not as a silently wrong line.
        Assert.Null(DocAssembler.TitleComment("brainfuck", "Title"));

        const string id = "probe/id";
        var output = DocAssembler.Assemble(
            $"```brainfuck snippet=\"{id}\" title=\"T\"\n```",
            OneSnippet(id, "BODY"), NoScreenshots, out var errors, out _, null, null);

        Assert.Contains(errors, e => e.Contains("title=") && e.Contains("brainfuck"));
        // The snippet body still ships; only the unrepresentable title is dropped.
        Assert.Contains("BODY", output);
        Assert.DoesNotContain("T\n", output.Replace("BODY", string.Empty));
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("xml")]
    public void Title_is_emitted_inside_the_fence_not_above_it(string language)
    {
        // The title used to be appended *before* the opening fence, which put it
        // in markdown body text instead of the code block. As `// Title` that
        // rendered as a stray literal line of prose above the example; as an
        // XML/HTML comment it would be parsed as raw markdown HTML and disappear
        // from the page entirely — a title that silently does not exist.
        const string id = "probe/id";
        var output = DocAssembler.Assemble(
            $"```{language} snippet=\"{id}\" title=\"My Title\"\n```",
            OneSnippet(id, "BODY"), NoScreenshots, out _, out _, null, null)
            .ReplaceLineEndings("\n");

        var lines = output.Split('\n');
        var fenceIndex = Array.FindIndex(lines, l => l.StartsWith("```"));
        var titleIndex = Array.FindIndex(lines, l => l.Contains("My Title"));

        Assert.True(fenceIndex >= 0, $"no opening fence in output:\n{output}");
        Assert.True(titleIndex >= 0, $"title was dropped entirely from output:\n{output}");
        // The ordering assertion is the whole point: with the old code the title
        // sat at index 0 and the fence at index 1.
        Assert.True(
            titleIndex > fenceIndex,
            $"title must be inside the fence, but appeared above it:\n{output}");
        Assert.Contains("BODY", output);
    }

    [Fact]
    public void No_generated_guide_page_carries_an_unexpanded_snippet_attribute()
    {
        // Corpus-level fail-closed check. `snippet=` is a pipeline directive: if
        // one survives into docs/guide it was never substituted, and the page is
        // showing the reader an empty or stale block.
        var guideDir = Path.Join(FindRepoRoot(), "docs", "guide");
        var pages = Directory.GetFiles(guideDir, "*.md", SearchOption.AllDirectories);

        // Positive control #1 — prove we are actually reading a corpus. A broken
        // path would otherwise scan zero files and report a confident zero.
        Assert.True(pages.Length > 50, $"expected the full guide corpus, found {pages.Length} page(s) in {guideDir}");

        var probe = new Regex("snippet=\"");

        // Positive control #2 — same probe, same wrapping, against text known to
        // contain the defect. If the pattern itself stopped matching, the real
        // assertion below would pass for the wrong reason.
        Assert.Matches(probe, "```xml snippet=\"source:a/b.csproj#shape\"\n```");

        var offenders = pages
            .Select(p => (Page: Path.GetRelativePath(guideDir, p), Text: File.ReadAllText(p)))
            .Where(x => probe.IsMatch(x.Text))
            .Select(x => x.Page)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Unexpanded snippet directive(s) reached generated output — the fence language is probably "
            + "not handled by DocAssembler.SnippetDirective(): " + string.Join(", ", offenders));
    }

    [Fact]
    public void Every_language_used_with_a_snippet_attribute_in_templates_is_substitutable()
    {
        // Forward-looking: the corpus test above only sees languages that are
        // already present. This asserts the *regex* accepts every language an
        // author has actually written next to a `snippet=`, so adding a new one
        // (```json, ```yaml) fails here rather than silently producing an empty
        // block three pipeline stages later.
        var templateDir = Path.Join(FindRepoRoot(), "docs", "_pipeline", "templates");
        var fence = new Regex(@"```(?<lang>[^\s`]+)\s+snippet=""");

        var languages = Directory.GetFiles(templateDir, "*.md.dt", SearchOption.AllDirectories)
            .SelectMany(f => fence.Matches(File.ReadAllText(f)).Select(m => m.Groups["lang"].Value))
            .Distinct()
            .ToList();

        Assert.True(languages.Count > 0, $"no snippet fences found under {templateDir}");

        foreach (var language in languages)
        {
            const string id = "probe/id";
            var output = DocAssembler.Assemble(
                $"```{language} snippet=\"{id}\"\n```",
                OneSnippet(id, "PROBE_BODY"),
                NoScreenshots, out _, out _, null, null);

            Assert.True(
                output.Contains("PROBE_BODY"),
                $"DocAssembler does not substitute ```{language} fences, so every "
                + $"`{language}` snippet in the templates renders as an empty code block.");
        }
    }

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            if (File.Exists(Path.Join(dir, "Reactor.slnx")) || Directory.Exists(Path.Join(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Reactor repo root not found from test cwd.");
    }
}
