using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.UI.Reactor.Analyzers;
using Microsoft.UI.Reactor.Cli.Check;
using Microsoft.UI.Reactor.Cli.Check.Rules;
using Microsoft.UI.Reactor.Cli.Check.Suggesters;
using Microsoft.UI.Reactor.Tests.CheckCommandTests;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Divergence guard (spec 061 §6/§7): the in-build "did you mean" analyzer keeps its own small
/// vocabulary map (netstandard2.0 can't share the CLI's net8 <c>FactoryIndex</c> engine), so this
/// test drives the corresponding <c>mur check</c> Tier-3 rule for every analyzer map entry and
/// asserts both engines resolve the same target. If someone edits one map without the other, this
/// fails — the two can't silently drift while the full shared-engine unification remains a follow-up.
/// </summary>
public class DidYouMeanAnalyzerParityTests
{
    // REACTOR_DYM_002 (ThemeBackgroundSuffixAnalyzer) <-> ThemeBackgroundSuffixRule.

    private const string ThemeStub = @"
namespace Microsoft.UI.Reactor.Core
{
    public static class Theme
    {
        public static object SolidBackground => null!;
        public static object CardBackground => null!;
        public static object LayerFill => null!;
        public static object Accent => null!;
    }
}";

    [Fact]
    public void Theme_analyzer_overrides_match_cli_rule()
    {
        Assert.NotEmpty(ThemeBackgroundSuffixAnalyzer.ExactOverrides);
        foreach (var (invented, target) in ThemeBackgroundSuffixAnalyzer.ExactOverrides)
        {
            var s = RunThemeRule(invented);
            Assert.NotNull(s);
            Assert.Equal("ThemeBackgroundSuffixRule", s!.SuggesterName);
            Assert.Equal($"Theme.{target}", s.Text);
        }
    }

    [Fact]
    public void Theme_analyzer_suffix_fallback_matches_cli_rule()
    {
        // A representative invented *Background name with no exact override must resolve to the same
        // surface-background fallback in both engines.
        const string invented = "AppBackground";
        Assert.Equal(
            ThemeBackgroundSuffixAnalyzer.SuffixFallbackTarget,
            ThemeBackgroundSuffixAnalyzer.ResolveTarget(invented));

        var s = RunThemeRule(invented);
        Assert.NotNull(s);
        Assert.Equal("ThemeBackgroundSuffixRule", s!.SuggesterName);
        Assert.Equal($"Theme.{ThemeBackgroundSuffixAnalyzer.SuffixFallbackTarget}", s.Text);
    }

    private static Suggestion? RunThemeRule(string missingMember)
    {
        var source = $@"
using Microsoft.UI.Reactor.Core;
class Test {{ void M() {{ var x = Theme.{missingMember}; }} }}";
        var c = TestCompilation.Create(new[] { (ThemeStub, "Stub.cs"), (source, "Test.cs") });
        var roslynDiag = c.GetDiagnostics().First(d => d.Id == "CS0117");
        var span = roslynDiag.Location.GetLineSpan();
        var diag = new CheckCommand.Diag(
            span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            "error", "CS0117", roslynDiag.GetMessage());
        var registry = RuleRegistry.Of(new IRulePattern[] { new ThemeBackgroundSuffixRule() });
        return new SuggesterOrchestrator(rules: registry).SuggestAgainst(diag, c);
    }

    // ── REACTOR_DYM_004 / _005 (argument-shape) <-> SymbolSuggester CS7036 / CS1503 ──
    //
    // These analyzers have no shared map to compare (they are structural, not vocabulary), so parity
    // is asserted behaviourally: for the representative shape each analyzer targets, BOTH the analyzer
    // AND the CLI's SymbolSuggester must emit a hint. If someone removes the CLI's CS1503
    // string-for-element / CS7036 named-arg heuristic, or weakens the analyzer, one side goes silent
    // and this fails — so the in-build and CLI experiences can't silently diverge on these shapes.

    private const string ArgShapeStub = @"
using System;
namespace Microsoft.UI.Reactor.Core { public abstract record Element; }
namespace Microsoft.UI.Reactor
{
    using Microsoft.UI.Reactor.Core;
    public static class Factories
    {
        public static Element ScrollViewer(Element child) => null!;
        public static Element TextBlock(string content) => null!;
        public static Element Heading(string content) => null!;
        public static Element Caption(string content) => null!;
    }
}";

    [Fact]
    public async Task CS1503_string_for_element_fires_in_both_engines()
    {
        // CLI side: the SymbolSuggester Element-vs-string special case still emits.
        var cli = RunSymbolSuggester(@"class T { object M() => ScrollViewer(""x""); }", "CS1503");
        Assert.True(cli.HasSuggestion, $"CLI SymbolSuggester went silent on CS1503 string-for-element ({cli.Evidence}).");

        // Analyzer side: REACTOR_DYM_005 fires on the same shape.
        var test = new CSharpAnalyzerTest<StringForElementArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = ArgShapeStub + @"
namespace App { using static Microsoft.UI.Reactor.Factories; static class C { static object M() => ScrollViewer({|REACTOR_DYM_005:""x""|}); } }",
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.DisabledDiagnostics.Add("CS1591");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CS7036_missing_argument_fires_in_both_engines()
    {
        // CLI side: the SymbolSuggester overload-shape suggestion still emits.
        var cli = RunSymbolSuggester(@"class T { object M() => ScrollViewer(); }", "CS7036");
        Assert.True(cli.HasSuggestion, $"CLI SymbolSuggester went silent on CS7036 missing-argument ({cli.Evidence}).");
        Assert.Contains("ScrollViewer(", cli.Text);

        // Analyzer side: REACTOR_DYM_004 fires on the same shape.
        var test = new CSharpAnalyzerTest<MissingFactoryArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = ArgShapeStub + @"
namespace App { using static Microsoft.UI.Reactor.Factories; static class C { static object M() => {|REACTOR_DYM_004:ScrollViewer|}(); } }",
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.DisabledDiagnostics.Add("CS1591");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    private static SuggestionResult RunSymbolSuggester(string userCode, string code)
    {
        var src = "using static Microsoft.UI.Reactor.Factories;\nusing Microsoft.UI.Reactor.Core;\n" + userCode;
        var comp = TestCompilation.Create(new[] { (ArgShapeStub, "Stub.cs"), (src, "User.cs") });
        var diag = comp.GetDiagnostics().First(d => d.Id == code);
        var tree = diag.Location.SourceTree!;
        var node = tree.GetRoot().FindNode(diag.Location.SourceSpan, getInnermostNodeForTie: true);

        SyntaxNode pick = node;
        for (var n = node; n is not null; n = n.Parent)
        {
            if (n is MemberAccessExpressionSyntax or InvocationExpressionSyntax or IdentifierNameSyntax or ArgumentSyntax)
            {
                pick = n;
                break;
            }
        }

        var sm = comp.GetSemanticModel(tree);
        ITypeSymbol? receiver = pick is MemberAccessExpressionSyntax m
            ? sm.GetTypeInfo(m.Expression).Type
            : pick.Parent is MemberAccessExpressionSyntax mp ? sm.GetTypeInfo(mp.Expression).Type : null;

        var ctx = new SuggesterContext(comp, diag, pick, receiver, FactoryIndex.Build(comp));
        return new SymbolSuggester().Suggest(ctx);
    }
}
