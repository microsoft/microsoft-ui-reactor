// Spec 038 §3.2 Class-A fixture tests for ThemeRawResourceKeyRule.
//
// Validation Gate (six bars per spec 038 §"Human Validation Gate"):
//   #1 Frequency: run5 opus-4.7 corpus — each mapping has ≥ 2 distinct trials
//      (highest: SubtleFillSecondary at 12 trials, LayerFillColorDefault and
//      SubtleFillColorSecondary at 6 each).
//   #2 Cross-agent reproducibility: PARTIAL — single-agent (opus-4.7) at time
//      of authoring. Structural justification (WinUI 3 ThemeResource key →
//      Reactor short alias) carries the cross-agent gap.
//   #3 Positive coverage: ≥ 3 below (one per major mapping family).
//   #4 Negative coverage: ≥ 2 below.
//   #5 Independent reviewer signoff: pending (PR review).
//   #6 Telemetry kill-switch: Name "ThemeRawResourceKeyRule" round-trips
//      through --disable-rule via RuleRegistry auto-discovery.

using Microsoft.UI.Reactor.Cli.Check;
using Microsoft.UI.Reactor.Cli.Check.Rules;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Rules;

public class ThemeRawResourceKeyRuleTests
{
    // Stub including all targets the rule maps TO. If any of these are
    // missing the rule self-skips for that name — which is the correct
    // behavior under API churn but makes negative tests hard, so we keep
    // them present here.
    const string ThemeStub = @"
namespace Microsoft.UI.Reactor.Core
{
    public static class Theme
    {
        public static object Accent => null!;
        public static object LayerFill => null!;
        public static object SubtleFill => null!;
        public static object SecondaryText => null!;
        public static object CardBackground => null!;
        public static object SolidBackground => null!;
    }
}";

    [Fact]
    public void Maps_LayerFillColorDefault_To_LayerFill()
        => AssertMapsTo("LayerFillColorDefault", "Theme.LayerFill");

    [Fact]
    public void Maps_SubtleFillColorSecondary_To_SubtleFill()
        => AssertMapsTo("SubtleFillColorSecondary", "Theme.SubtleFill");

    [Fact]
    public void Maps_ColorStripped_SubtleFillSecondary_To_SubtleFill()
        => AssertMapsTo("SubtleFillSecondary", "Theme.SubtleFill");

    [Fact]
    public void Maps_AccentFillColorDefault_To_Accent()
        => AssertMapsTo("AccentFillColorDefault", "Theme.Accent");

    [Fact]
    public void Maps_TextFillColorSecondary_To_SecondaryText()
        => AssertMapsTo("TextFillColorSecondary", "Theme.SecondaryText");

    [Fact]
    public void Does_not_fire_on_unmapped_invented_name()
    {
        // Negative #1: an invented name that is NOT in our map. Tier-2 fuzzy
        // and/or ThemeBackgroundSuffixRule may still respond, but this rule
        // must stay silent.
        var diag = MakeThemeDiag("AppFillColorRandom", ThemeStub, out var c);
        var suggestion = RunRule(diag, c);
        Assert.True(suggestion is null || suggestion.SuggesterName != "ThemeRawResourceKeyRule",
            $"Rule should be silent on unmapped names; got {suggestion?.SuggesterName}");
    }

    [Fact]
    public void Does_not_fire_when_receiver_is_a_user_type_named_Theme()
    {
        // Negative #2: look-alike Theme type in a different namespace. Same
        // safety as ThemeBackgroundSuffixRule — symbol equality must rule out
        // a user's domain model that happens to share the type name.
        const string lookalikeStub = ThemeStub + @"
namespace Acme.Branding
{
    public static class Theme
    {
        public static object Accent => null!;
    }
}";
        const string source = @"
using Acme.Branding;
class Test { void M() { var x = Theme.LayerFillColorDefault; } }
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

    static void AssertMapsTo(string invented, string expectedSuggestion)
    {
        var diag = MakeThemeDiag(invented, ThemeStub, out var c);
        var suggestion = RunRule(diag, c);
        Assert.NotNull(suggestion);
        Assert.Equal("ThemeRawResourceKeyRule", suggestion!.SuggesterName);
        Assert.Equal(expectedSuggestion, suggestion.Text);
        Assert.Contains("resource-key transliteration", suggestion.Evidence);
    }

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
        var registry = RuleRegistry.Of(new IRulePattern[] { new ThemeRawResourceKeyRule() });
        var orch = new SuggesterOrchestrator(rules: registry);
        return orch.SuggestAgainst(diag, c);
    }
}
