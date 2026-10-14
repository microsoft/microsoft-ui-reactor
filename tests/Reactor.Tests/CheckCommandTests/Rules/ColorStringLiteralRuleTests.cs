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
    public void Suggests_FontFamily_when_the_overload_exists()
    {
        var (diags, c) = Analyze(@"new ResourceBuilder().Set(""ContentControlThemeFontFamily"", ""Consolas"")", fontFamilyOverload: true);
        var s = RunRule(Assert.Single(diags), c);
        Assert.NotNull(s);
        Assert.Equal("new FontFamily(\"Consolas\")", s!.Text);
    }

    [Theory]
    // No concrete rewrite exists for these — the analyzer's message carries the advice.
    [InlineData(@"new ResourceBuilder().Set(""ContentControlThemeFontFamily"", ""Consolas"")")]
    [InlineData(@"TextBlock(""hi"").Background(""accent"")")]
    [InlineData(@"TextBlock(""hi"").Background(""#12345"")")]
    public void Silent_when_there_is_no_concrete_rewrite(string expression)
    {
        var (diags, c) = Analyze(expression);
        Assert.Null(RunRule(Assert.Single(diags), c));
    }

    [Fact]
    public void Each_branch_gets_its_own_rewrite_and_disagreeing_branches_stay_silent()
    {
        // Two unparseable branches with different rewrites: the rule only sees the argument, so it
        // cannot tell which literal the diagnostic is about — silent beats a wrong suggestion.
        var (diags, c) = Analyze(@"TextBlock(""hi"").Background(on ? ""orange"" : ""teal"")");
        Assert.Equal(2, diags.Length);
        Assert.All(diags, d => Assert.Null(RunRule(d, c)));

        // One unparseable branch: unambiguous.
        (diags, c) = Analyze(@"TextBlock(""hi"").Background(on ? ""orange"" : ""red"")");
        Assert.Equal("\"#FFA500\"", RunRule(Assert.Single(diags), c)!.Text);
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
        return (found.Where(d => d.Id == ColorStringLiteralAnalyzer.DiagnosticId).ToArray(), c);
    }

    internal static Suggestion? RunRule(CheckCommand.Diag diag, CSharpCompilation c)
    {
        var registry = RuleRegistry.Of(new IRulePattern[] { new ColorStringLiteralRule() });
        return new SuggesterOrchestrator(rules: registry).SuggestAgainst(diag, c);
    }
}
