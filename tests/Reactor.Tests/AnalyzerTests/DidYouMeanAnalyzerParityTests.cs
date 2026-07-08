using System.Linq;
using Microsoft.UI.Reactor.Analyzers;
using Microsoft.UI.Reactor.Cli.Check;
using Microsoft.UI.Reactor.Cli.Check.Rules;
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
}
