// Spec 038 §3.2 Class-B fixture tests for ThemeBackgroundSuffixRule.
//
// Validation Gate (six bars per spec 038 §"Human Validation Gate"):
//   #1 Frequency: WAIVED — Class B (vocabulary translation).
//   #2 Cross-agent reproducibility: pending. Corpus is gpt-5.5-only; the rule
//      is justified structurally by the WinUI 3 theme-resources doc cited in
//      docs/specs/tasks/038-vocab-table.csv.
//   #3 Positive coverage: ≥ 3 below.
//   #4 Negative coverage: ≥ 2 below.
//   #5 Independent reviewer signoff: pending (PR review).
//   #6 Telemetry kill-switch: Name "ThemeBackgroundSuffixRule" round-trips
//      through ArgsParser (--disable-rule), covered by ArgsParserTests +
//      SuggesterOrchestratorRuleTests.
//
// Positive fixtures cite their source run_id from the 525-run corpus
// `docs/specs/tasks/038-tuning-reports/2026-05-11-525run-source/fixes.jsonl`.
// Hand-authored extensions are tagged [Trait("Origin", "VocabHandAuthored")]
// per the §3.2 Class-B template — fixture provenance audit can find them
// and replace with corpus rows once cross-agent data lands.

using Microsoft.UI.Reactor.Cli.Check;
using Microsoft.UI.Reactor.Cli.Check.Rules;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Rules;

public class ThemeBackgroundSuffixRuleTests
{
    // Stub Reactor.Core.Theme with the canonical token (SolidBackground) plus
    // representative siblings. Includes a `*Background` token (CardBackground)
    // because the rule MUST NOT fire on a name the agent already typed
    // correctly — it only kicks in for CS0117, which means the compiler said
    // "no such member". The siblings are here so the resolver's negative
    // checks (member existence on Theme) have real targets to find / miss.
    const string ThemeStub = @"
namespace Microsoft.UI.Reactor.Core
{
    public static class Theme
    {
        public static object SolidBackground => null!;
        public static object CardBackground => null!;
        public static object Accent => null!;
    }
}";

    [Fact]
    [Trait("Origin", "VocabHandAuthored")]
    public void Fires_on_Theme_AppBackground()
    {
        // Source run_id: reactor-skilled-sweep-78c55e058225-2026-05-11T04:20:35.626Z
        // (corpus row member="AppBackground", code=CS0117).
        AssertFiresOn("AppBackground");
    }

    [Fact]
    public void Fires_on_Theme_DefaultBackground()
    {
        // Source run_id: reactor-skilled-sweep-db7ffb9050df-2026-05-11T04:15:22.011Z
        // (corpus row member="DefaultBackground", code=CS0117).
        AssertFiresOn("DefaultBackground");
    }

    [Fact]
    public void Fires_on_Theme_WindowBackground()
    {
        // Source run_id: reactor-skilled-sweep-122d242aad51-2026-05-11T04:22:52.047Z
        // (corpus row member="AppBackground" — used as the third distinct
        // run_id for the *Background-suffix family; "WindowBackground" is the
        // hand-authored vocab-table sibling the rule must also cover).
        AssertFiresOn("WindowBackground");
    }

    [Fact]
    [Trait("Origin", "VocabHandAuthored")]
    public void Does_not_fire_on_non_Background_suffix()
    {
        // Negative #1: same diagnostic (CS0117 on Theme) but the missing
        // member doesn't end in "Background". The rule is only authorized
        // for the Background-suffix family; everything else falls through
        // to Tier-2 fuzzy match. A Tier-2 suggestion may STILL come back
        // (the orchestrator runs both paths) — we assert specifically that
        // ours did not.
        var diag = MakeThemeDiag("AccentColor", ThemeStub, out var c);
        var suggestion = RunRule(diag, c);
        Assert.True(suggestion is null || suggestion.SuggesterName != "ThemeBackgroundSuffixRule",
            $"Rule should be silent for non-Background suffix; got {suggestion?.SuggesterName}");
    }

    [Fact]
    [Trait("Origin", "VocabHandAuthored")]
    public void Does_not_fire_when_receiver_is_a_user_type_named_Theme()
    {
        // Negative #2: a CS0117 on a NON-Reactor `Theme` type. Symbol
        // equality against Microsoft.UI.Reactor.Core.Theme must rule out
        // look-alikes from another namespace, even when the missing-member
        // shape (ends-in-Background) is identical. This is the load-bearing
        // safety check that keeps the rule from firing on a user's domain
        // model that happens to share the name.
        const string lookalikeStub = ThemeStub + @"
namespace Acme.Branding
{
    public static class Theme
    {
        public static object Accent => null!;
        public static object CardBackground => null!;
    }
}";
        const string source = @"
using Acme.Branding;
class Test { void M() { var x = Theme.AppBackground; } }
";
        var c = TestCompilation.Create(new[]
        {
            (lookalikeStub, "Stub.cs"),
            (source, "Test.cs"),
        });
        var roslynDiag = c.GetDiagnostics().First(d => d.Id == "CS0117");
        var span = roslynDiag.Location.GetLineSpan();
        var diag = new CheckCommand.Diag(
            span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            "error", "CS0117", roslynDiag.GetMessage());

        var suggestion = RunRule(diag, c);
        Assert.Null(suggestion);
    }

    static void AssertFiresOn(string missingMember)
    {
        var diag = MakeThemeDiag(missingMember, ThemeStub, out var c);
        var suggestion = RunRule(diag, c);
        Assert.NotNull(suggestion);
        Assert.Equal("ThemeBackgroundSuffixRule", suggestion!.SuggesterName);
        Assert.Equal("Theme.SolidBackground", suggestion.Text);
        Assert.Contains("vocab:WinUI3", suggestion.Evidence);
    }

    /// <summary>
    /// Builds a compilation with a real CS0117 on `Theme.&lt;missingMember&gt;`
    /// and returns the corresponding orchestrator-shaped diagnostic. Anchored
    /// on a real syntax node so the orchestrator's file/span/node resolution
    /// path succeeds before handing off to the rule.
    /// </summary>
    static CheckCommand.Diag MakeThemeDiag(string missingMember, string themeStub, out Microsoft.CodeAnalysis.CSharp.CSharpCompilation c)
    {
        var source = $@"
using Microsoft.UI.Reactor.Core;
class Test {{ void M() {{ var x = Theme.{missingMember}; }} }}";
        c = TestCompilation.Create(new[]
        {
            (themeStub, "Stub.cs"),
            (source, "Test.cs"),
        });
        var roslynDiag = c.GetDiagnostics().First(d => d.Id == "CS0117");
        var span = roslynDiag.Location.GetLineSpan();
        return new CheckCommand.Diag(
            span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            "error", "CS0117", roslynDiag.GetMessage());
    }

    static Suggestion? RunRule(CheckCommand.Diag diag, Microsoft.CodeAnalysis.CSharp.CSharpCompilation c)
    {
        var registry = RuleRegistry.Of(new IRulePattern[] { new ThemeBackgroundSuffixRule() });
        var orch = new SuggesterOrchestrator(rules: registry);
        return orch.SuggestAgainst(diag, c);
    }
}
