// Tests for ColorStringLiteralRule — the `mur check` mirror of REACTOR_DYM_006
// (issue #1375).
//
// There is no compiler error for this mistake, so the rule anchors on the
// analyzer's own diagnostic. These tests therefore run the REAL analyzer over
// a stub compilation, turn each REACTOR_DYM_006 it reports into the
// MSBuild-shaped `CheckCommand.Diag` that `mur check` parses, and drive the
// orchestrator with it — the same path a `dotnet build` line takes.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.UI.Reactor.Analyzers;
using Microsoft.UI.Reactor.Cli.Check;
using Microsoft.UI.Reactor.Cli.Check.Rules;
using Microsoft.UI.Reactor.Tests.AnalyzerTests;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Rules;

public class ColorStringLiteralRuleTests
{
    [Theory]
    [InlineData(@"TextBlock(""hi"").Background(""orange"")", "\"#FFA500\"")]
    [InlineData(@"TextBlock(""hi"").Foreground(""#888"")", "\"#888888\"")]
    [InlineData(@"TextBlock(""hi"").BorderBrush(""FF0000"")", "\"#FF0000\"")]
    [InlineData(@"BrushHelper.Parse(""AliceBlue"")", "\"#F0F8FF\"")]
    [InlineData(@"new ResourceBuilder().Set(""ButtonBackground"", ""LightCoral"")", "\"#F08080\"")]
    [InlineData(@"TextBlock(""hi"").Background(Brand)", "\"#FFA500\"")]
    public void Fires_with_the_concrete_rewrite(string expression, string expected)
    {
        var (diags, c) = Analyze(expression);
        var diag = Assert.Single(diags);
        var s = RunRule(diag, c);
        Assert.NotNull(s);
        Assert.Equal("ColorStringLiteralRule", s!.SuggesterName);
        Assert.True(s.IsRule);
        Assert.Equal(expected, s.Text);
        Assert.Contains("analyzer:REACTOR_DYM_006", s.Evidence);
    }

    [Fact]
    public void Reads_the_rewrite_from_the_diagnostic_message_not_the_source()
    {
        // The rule trusts the analyzer's message: a message from another source shape still yields its
        // own did-you-mean, and a REACTOR_DYM_006 whose message carries none stays silent.
        var (diags, c) = Analyze(@"TextBlock(""hi"").Background(""orange"")");
        var diag = Assert.Single(diags);
        var forged = diag with { Message = "'teal' is not a color string Reactor can parse, so it renders gray — the only built-in names are red; did you mean \"#008080\"?" };
        Assert.Equal("\"#008080\"", RunRule(forged, c)!.Text);
        Assert.Null(RunRule(diag with { Message = "something else entirely" }, c));
    }

    [Fact]
    public void Suggests_FontFamily_when_the_overload_exists()
    {
        var (diags, c) = Analyze(@"new ResourceBuilder().Set(""ContentControlThemeFontFamily"", ""Consolas"")", fontFamilyOverload: true);
        var s = RunRule(Assert.Single(diags), c);
        Assert.NotNull(s);
        Assert.Equal("new Microsoft.UI.Xaml.Media.FontFamily(\"Consolas\")", s!.Text);
    }

    [Fact]
    public void Without_the_FontFamily_overload_the_modifier_guidance_survives_mur_check_truncation()
    {
        // No rewrite of the argument exists, but the message's `.FontFamily(...)` workaround must still
        // reach the agent: CheckCommand truncates messages to 100 chars, which cuts it off.
        var (diags, c) = Analyze(@"new ResourceBuilder().Set(""ContentControlThemeFontFamily"", ""Consolas"")");
        var diag = Assert.Single(diags);
        var s = RunRule(diag, c);
        Assert.NotNull(s);
        Assert.Equal(".FontFamily(\"Consolas\")", s!.Text);

        var line = diag.Format(s);
        Assert.DoesNotContain("to change the font", line, StringComparison.Ordinal); // truncated away...
        Assert.Contains("→ try: .FontFamily(\"Consolas\")", line, StringComparison.Ordinal); // ...but surfaced here
    }

    [Theory]
    // No concrete next step exists for these — the analyzer's message carries the advice.
    [InlineData(@"TextBlock(""hi"").Background(""accent"")")]
    [InlineData(@"TextBlock(""hi"").Background(""#12345"")")]
    public void Silent_when_there_is_no_concrete_rewrite(string expression)
    {
        var (diags, c) = Analyze(expression);
        Assert.Null(RunRule(Assert.Single(diags), c));
    }

    [Fact]
    public void Each_branch_gets_its_own_rewrite()
    {
        var (diags, c) = Analyze(@"TextBlock(""hi"").Background(on ? ""orange"" : ""teal"")");
        Assert.Equal(2, diags.Length);
        Assert.Equal(new[] { "\"#FFA500\"", "\"#008080\"" }, diags.Select(d => RunRule(d, c)!.Text));

        // A branch with no rewrite doesn't silence its fixable sibling.
        (diags, c) = Analyze(@"TextBlock(""hi"").Background(on ? ""orange"" : ""accent"")");
        Assert.Equal(new[] { "\"#FFA500\"", null }, diags.Select(d => RunRule(d, c)?.Text));
    }

    [Fact]
    public void FontFamily_value_inside_a_branch_gets_no_FontFamily_suggestion()
    {
        // Matches the code fix, which can't wrap one branch of a ?: in new FontFamily(...); the element
        // modifier is the remaining advice.
        var (diags, c) = Analyze(@"new ResourceBuilder().Set(""ContentControlThemeFontFamily"", on ? ""Consolas"" : ""red"")", fontFamilyOverload: true);
        Assert.Equal(".FontFamily(\"Consolas\")", RunRule(Assert.Single(diags), c)?.Text);
    }

    [Fact]
    public void Silent_on_other_diagnostic_codes()
    {
        var (diags, c) = Analyze(@"TextBlock(""hi"").Background(""orange"")");
        var other = Assert.Single(diags) with { Code = "REACTOR_THEME_001" };
        Assert.Null(RunRule(other, c));
    }

    [Fact]
    public void Silent_when_the_targets_are_missing()
    {
        // Same source, but the Reactor color APIs were renamed (API churn): the rule self-disables
        // instead of guessing. Positive control first so the null below isn't a mis-anchored span.
        var (diags, c) = Analyze(@"TextBlock(""hi"").Background(""orange"")");
        var diag = Assert.Single(diags);
        Assert.NotNull(RunRule(diag, c));

        var renamed = c.ReplaceSyntaxTree(
            c.SyntaxTrees.First(t => t.FilePath == "Stub.cs"),
            CSharpSyntaxTree.ParseText(
                ColorStringLiteralAnalyzerTests.CommonStubs
                    .Replace("public static partial class ElementExtensions", "public static partial class RenamedExtensions", StringComparison.Ordinal)
                    .Replace("public static class BrushHelper\r\n    {\r\n        public static SolidColorBrush", "public static class RenamedBrushHelper\r\n    {\r\n        public static SolidColorBrush", StringComparison.Ordinal)
                    .Replace("public static class BrushHelper\n    {\n        public static SolidColorBrush", "public static class RenamedBrushHelper\n    {\n        public static SolidColorBrush", StringComparison.Ordinal),
                path: "Stub.cs",
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Null(renamed.GetTypeByMetadataName("Microsoft.UI.Reactor.ElementExtensions"));
        Assert.Null(renamed.GetTypeByMetadataName("Microsoft.UI.Reactor.BrushHelper"));
        Assert.Null(RunRule(diag, renamed));
    }

    [Theory]
    [InlineData(@"TextBlock(""hi"").Background(""orange"")", false)]
    [InlineData(@"TextBlock(""hi"").Foreground(color: ""#888"")", false)]
    [InlineData(@"new ResourceBuilder().Set(""ContentControlThemeFontFamily"", ""Consolas"")", true)]
    [InlineData(@"new ResourceBuilder().Set(key: ""ContentControlThemeFontFamily"", color: ""Consolas"")", true)]
    [InlineData(@"new ResourceBuilder().Set(""ContentControlThemeFontFamily"", @""C:\Fonts\a""""b.ttf#A"")", true)]
    public async Task Applying_the_suggestion_compiles_and_clears_the_diagnostic(string expression, bool fontFamilyOverload)
    {
        // The source under test has no `using Microsoft.UI.Xaml.Media;`, so an unqualified
        // `new FontFamily(...)` suggestion would fail with CS0246 here.
        var (diagnostics, c) = AnalyzeRaw(expression, fontFamilyOverload);
        var d = Assert.Single(diagnostics);
        var s = RunRule(ToCheckDiag(d), c);
        Assert.NotNull(s);

        // A FontFamily suggestion replaces the whole argument (it may carry a renamed `name:`); a color
        // suggestion replaces only the flagged literal, keeping any `color:` name.
        var tree = d.Location.SourceTree!;
        var flagged = tree.GetRoot(TestContext.Current.CancellationToken).FindNode(d.Location.SourceSpan, getInnermostNodeForTie: true);
        var span = s!.Text.Contains(".FontFamily(", StringComparison.Ordinal)
            ? flagged.FirstAncestorOrSelf<Microsoft.CodeAnalysis.CSharp.Syntax.ArgumentSyntax>()!.Span
            : d.Location.SourceSpan;
        var text = tree.GetText(TestContext.Current.CancellationToken).Replace(span, s.Text);
        var fixedCompilation = c.ReplaceSyntaxTree(tree, tree.WithChangedText(text));

        Assert.Empty(fixedCompilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(x => x.Severity == DiagnosticSeverity.Error));
        var after = await fixedCompilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ColorStringLiteralAnalyzer()))
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(after, x => x.Id == ColorStringLiteralAnalyzer.DiagnosticId);
    }

    internal static (CheckCommand.Diag[] Diags, CSharpCompilation Compilation) Analyze(string expression, bool fontFamilyOverload = false)
    {
        var (found, c) = AnalyzeRaw(expression, fontFamilyOverload);
        return (found.Select(ToCheckDiag).ToArray(), c);
    }

    internal static CheckCommand.Diag ToCheckDiag(Diagnostic d)
    {
        var span = d.Location.GetLineSpan();
        return new CheckCommand.Diag(
            span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            "warning", d.Id, d.GetMessage());
    }

    internal static (Diagnostic[] Diagnostics, CSharpCompilation Compilation) AnalyzeRaw(string expression, bool fontFamilyOverload = false)
    {
        var source = $@"
namespace App
{{
    using Microsoft.UI.Reactor;
    using Microsoft.UI.Reactor.Elements;
    using static Microsoft.UI.Reactor.Factories;
    static class C
    {{
        const string Brand = ""orange"";
        static object M(bool on) => {expression};
    }}
}}";
        var c = TestCompilation.Create(new[]
        {
            (ColorStringLiteralAnalyzerTests.CommonStubs, "Stub.cs"),
            (fontFamilyOverload ? ColorStringLiteralAnalyzerTests.ResourceBuilderWithFontFamily : ColorStringLiteralAnalyzerTests.ResourceBuilderWithoutFontFamily, "ResourceBuilder.cs"),
            (source, "Test.cs"),
        });
        Assert.Empty(c.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        var found = c.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ColorStringLiteralAnalyzer()))
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        return (found.Where(d => d.Id == ColorStringLiteralAnalyzer.DiagnosticId).OrderBy(d => d.Location.SourceSpan.Start).ToArray(), c);
    }

    internal static Suggestion? RunRule(CheckCommand.Diag diag, CSharpCompilation c)
    {
        var registry = RuleRegistry.Of(new IRulePattern[] { new ColorStringLiteralRule() });
        return new SuggesterOrchestrator(rules: registry).SuggestAgainst(diag, c);
    }
}
