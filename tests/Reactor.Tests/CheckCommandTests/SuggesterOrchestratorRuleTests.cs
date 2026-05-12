// Phase 3.1 — SuggesterOrchestrator + RuleRegistry integration.
// Spec 038 §6 + §3.1. Covers the wiring that the per-rule fixture tests
// (one file per rule) cannot exercise in isolation:
//   - rules can match diagnostic codes outside Tier-2's SupportedCodes
//     (e.g. CS1955), unblocking Class-A rules for codes Tier-2 doesn't reach;
//   - when both Tier-2 and a rule match, the rule wins (spec §6);
//   - the rule's Name + Provenance round-trip into the Suggestion that
//     CheckCommand.EmitDiagnostics formats;
//   - --disable-rule plumbing actually suppresses the rule.

using Microsoft.UI.Reactor.Cli.Check;
using Microsoft.UI.Reactor.Cli.Check.Rules;
using Microsoft.UI.Reactor.Cli.Check.Suggesters;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests;

public class SuggesterOrchestratorRuleTests
{
    const string Stubs = @"
namespace Microsoft.UI.Reactor.Core
{
    public sealed class ButtonElement
    {
        public string Label { get; set; } = """";
    }
}";

    /// <summary>
    /// Test rule that fires on any CS1955 diagnostic with a hard-coded
    /// suggestion. CS1955 is not in SuggesterOrchestrator.SupportedCodes —
    /// the rule's job is to unblock that code.
    /// </summary>
    sealed class CS1955TestRule : IRulePattern
    {
        public string Name => "CS1955TestRule";
        public string Provenance => "cluster:C0004";
        public IReadOnlyList<string> DiagnosticCodes => new[] { "CS1955" };
        public IReadOnlyList<string> DeclaredTargets => Array.Empty<string>();
        public RuleSuggestion TryMatch(in RuleContext ctx)
            => new("GridSize() — add parens to invoke the factory", 0.95, "missing parens on GridSize");
    }

    /// <summary>
    /// Test rule that fires on CS1061 — this is in Tier-2 territory, so
    /// the rule and the SymbolSuggester both have a shot. Rule must win.
    /// </summary>
    sealed class CS1061TestRule : IRulePattern
    {
        public string Name => "CS1061TestRule";
        public string Provenance => "cluster:C0017";
        public IReadOnlyList<string> DiagnosticCodes => new[] { "CS1061" };
        public IReadOnlyList<string> DeclaredTargets => Array.Empty<string>();
        public RuleSuggestion TryMatch(in RuleContext ctx)
            => new(".Set(b => b.Label = \"x\")", 0.96, "structural rewrite");
    }

    [Fact]
    public void Rule_matches_diagnostic_code_outside_Tier2_SupportedCodes()
    {
        // CS1955 isn't in SupportedCodes, so without rules the orchestrator
        // returns null. With a registry that covers CS1955, the rule fires.
        // We anchor the diagnostic on a real syntax node so the orchestrator's
        // file/span resolution succeeds; the rule itself doesn't read the
        // node, but the orchestrator's pre-flight does.
        var diag = MakeDiagFor("CS1955", "GridSize", out var c);

        var registry = RuleRegistry.Of(new IRulePattern[] { new CS1955TestRule() });
        var orch = new SuggesterOrchestrator(rules: registry);
        var s = orch.SuggestAgainst(diag, c);

        Assert.NotNull(s);
        Assert.Equal("CS1955TestRule", s!.SuggesterName);
        Assert.Contains("GridSize()", s.Text);
        Assert.Contains("cluster:C0004", s.Evidence);
    }

    [Fact]
    public void Without_rule_registry_unsupported_code_returns_null()
    {
        // Regression guard: same input, no registry → null. Confirms the
        // gate logic doesn't accidentally admit Tier-2-incompatible codes.
        var diag = MakeDiagFor("CS1955", "GridSize", out var c);

        var orch = new SuggesterOrchestrator();  // no rules
        var s = orch.SuggestAgainst(diag, c);

        Assert.Null(s);
    }

    /// <summary>
    /// Anchors a synthetic diagnostic on a real identifier (<paramref name="anchor"/>)
    /// in a buildable source file so the orchestrator's file/span/node
    /// resolution path succeeds. The rule itself doesn't depend on the
    /// anchor's identity — it's just there to keep the orchestrator's
    /// pre-flight from returning early.
    /// </summary>
    static CheckCommand.Diag MakeDiagFor(string code, string anchor, out Microsoft.CodeAnalysis.CSharp.CSharpCompilation c)
    {
        var source = $"class Anchor {{ void M() {{ var x = {anchor}; }} }}";
        c = TestCompilation.Create(source);
        var tree = c.SyntaxTrees.First();
        var token = tree.GetRoot().DescendantTokens().First(t => t.Text == anchor);
        var pos = token.GetLocation().GetLineSpan();
        return new CheckCommand.Diag(
            tree.FilePath,
            pos.StartLinePosition.Line + 1,
            pos.StartLinePosition.Character + 1,
            "error", code, "msg");
    }

    [Fact]
    public void Rule_wins_over_Tier2_when_both_match()
    {
        // CS1061 on a ButtonElement receiver: Tier-2 (SymbolSuggester) would
        // produce a fuzzy match against Label; the rule produces a
        // structural rewrite. Spec §6: rule wins.
        var (c, diag) = MakeCS1061OnButtonLabl();

        var registry = RuleRegistry.Of(new IRulePattern[] { new CS1061TestRule() });
        var orch = new SuggesterOrchestrator(rules: registry);
        var s = orch.SuggestAgainst(diag, c);

        Assert.NotNull(s);
        Assert.Equal("CS1061TestRule", s!.SuggesterName);
        Assert.Contains(".Set(b => b.Label", s.Text);
    }

    [Fact]
    public void Disabled_rule_yields_to_Tier2()
    {
        // Same setup as the win-over-Tier-2 case, but with the rule disabled.
        // Tier-2 must take over and produce its own fuzzy Label suggestion.
        var (c, diag) = MakeCS1061OnButtonLabl();

        var registry = RuleRegistry.Of(new IRulePattern[] { new CS1061TestRule() });
        var disabled = new HashSet<string>(new[] { "CS1061TestRule" }, StringComparer.Ordinal);
        var orch = new SuggesterOrchestrator(rules: registry, disabledRules: disabled);
        var s = orch.SuggestAgainst(diag, c);

        Assert.NotNull(s);
        Assert.Equal("SymbolSuggester", s!.SuggesterName);
        Assert.Contains("Label", s.Text);
    }

    /// <summary>
    /// Rule that declares a target which won't exist in TestCompilation — the
    /// registry's self-disable path is the one we want to exercise here.
    /// Confidence is arbitrary; the rule never runs because target resolution
    /// fails first.
    /// </summary>
    sealed class UnresolvedTargetRule : IRulePattern
    {
        public string Name => "UnresolvedTargetTestRule";
        public string Provenance => "vocab:WinUI3";
        public IReadOnlyList<string> DiagnosticCodes => new[] { "CS1955" };
        public IReadOnlyList<string> DeclaredTargets => new[] { "Acme.NotPresentInAnyCompilation" };
        public RuleSuggestion TryMatch(in RuleContext ctx)
            => new("should never reach", 1.0, "");
    }

    [Fact]
    public void Orchestrator_propagates_onRuleSelfDisabled_callback()
    {
        // Spec 038 §3.1a residual — the SuggesterOrchestrator threads
        // onRuleSelfDisabled through to RuleRegistry.BestMatch so a trace
        // hook can record "rule X disabled because target Y didn't resolve".
        // Without the wiring, the registry's callback fires into the void
        // and a Reactor minor that retired a rule's target would degrade
        // silently — the exact failure mode §3.1a was created to prevent.
        var diag = MakeDiagFor("CS1955", "GridSize", out var c);

        var registry = RuleRegistry.Of(new IRulePattern[] { new UnresolvedTargetRule() });
        var captured = new List<(string Name, string Target)>();
        var orch = new SuggesterOrchestrator(
            rules: registry,
            onRuleSelfDisabled: (name, target) => captured.Add((name, target)));
        var s = orch.SuggestAgainst(diag, c);

        Assert.Null(s);
        Assert.Single(captured);
        Assert.Equal(("UnresolvedTargetTestRule", "Acme.NotPresentInAnyCompilation"), captured[0]);
    }

    [Fact]
    public void Rule_fires_even_when_tier2_is_disabled_by_the_suggest_gate()
    {
        // Spec 038 EC2 watch-item: when the suggest-gate closes (fewer than
        // --suggest-threshold unique CS diagnostics in the build), Tier-2
        // fuzzy match is suppressed. Tier-3 rules are precision-anchored
        // (Roslyn ISymbol binding, not text fuzz) and must keep firing
        // regardless — the gate's calibration is about Tier-2 noise on
        // small builds, not rule output. Without this carve-out the entire
        // rule registry silently disables on iteration-mode workflows that
        // surface 1–2 diagnostics per build.
        var diag = MakeDiagFor("CS1955", "GridSize", out var c);

        var registry = RuleRegistry.Of(new IRulePattern[] { new CS1955TestRule() });
        var orch = new SuggesterOrchestrator(rules: registry, tier2Enabled: false);
        var s = orch.SuggestAgainst(diag, c);

        Assert.NotNull(s);
        Assert.Equal("CS1955TestRule", s!.SuggesterName);
    }

    [Fact]
    public void Tier2_only_code_returns_null_when_tier2_is_disabled_and_no_rule_covers()
    {
        // Complement of the rule-fires-when-gated test: a CS1061 (in Tier-2's
        // SupportedCodes) with no rule covering it must return null when the
        // gate is closed. Confirms tier2Enabled=false actually gates Tier-2
        // rather than no-oping.
        var (c, diag) = MakeCS1061OnButtonLabl();

        // Empty registry — no rules at all, so the only path is Tier-2.
        var orch = new SuggesterOrchestrator(
            rules: RuleRegistry.Of(Array.Empty<IRulePattern>()),
            tier2Enabled: false);
        var s = orch.SuggestAgainst(diag, c);

        Assert.Null(s);
    }

    [Fact]
    public void AnyDiagnosticIsSuggestable_returns_false_for_empty_list()
    {
        // Clean build (no diagnostics) — caller should skip the compilation
        // load entirely. Spec 038 EC3-final review: avoid wall-time
        // regression on the happy path.
        var empty = new List<CheckCommand.Diag>();
        Assert.False(SuggesterOrchestrator.AnyDiagnosticIsSuggestable(empty, tier2Enabled: true, rules: null));
        Assert.False(SuggesterOrchestrator.AnyDiagnosticIsSuggestable(empty, tier2Enabled: false, rules: null));
    }

    [Fact]
    public void AnyDiagnosticIsSuggestable_returns_false_for_unrelated_CS_codes()
    {
        // Build emitted CS warnings only (e.g. nullable-reference noise)
        // — none in Tier-2's SupportedCodes and none covered by any rule.
        // Loading the compilation for a build like this would be pure
        // wall-time waste.
        var diags = new List<CheckCommand.Diag>
        {
            new("App.cs", 10, 5, "warning", "CS8602", "possible null reference"),
            new("App.cs", 12, 8, "warning", "CS8618", "non-nullable field uninitialized"),
        };
        var registry = RuleRegistry.Of(new IRulePattern[] { new CS1955TestRule() });
        Assert.False(SuggesterOrchestrator.AnyDiagnosticIsSuggestable(diags, tier2Enabled: true, rules: registry));
    }

    [Fact]
    public void AnyDiagnosticIsSuggestable_true_when_Tier2_applies_and_enabled()
    {
        // CS1061 is in SupportedCodes. With tier2Enabled=true and no rules,
        // the answer is true.
        var diags = new List<CheckCommand.Diag>
        {
            new("App.cs", 5, 3, "error", "CS1061", "'Foo' has no 'Bar'"),
        };
        Assert.True(SuggesterOrchestrator.AnyDiagnosticIsSuggestable(diags, tier2Enabled: true, rules: null));
    }

    [Fact]
    public void AnyDiagnosticIsSuggestable_false_when_Tier2_applies_but_gated()
    {
        // Same CS1061 diag but tier2Enabled=false (gate closed). Without a
        // rule covering CS1061, the answer flips to false — loading the
        // compilation gains nothing.
        var diags = new List<CheckCommand.Diag>
        {
            new("App.cs", 5, 3, "error", "CS1061", "'Foo' has no 'Bar'"),
        };
        Assert.False(SuggesterOrchestrator.AnyDiagnosticIsSuggestable(diags, tier2Enabled: false, rules: null));
    }

    [Fact]
    public void AnyDiagnosticIsSuggestable_true_when_rule_covers_code_even_with_Tier2_gated()
    {
        // CS1955 is OUTSIDE Tier-2's SupportedCodes. With a rule covering
        // it, the answer is true regardless of the Tier-2 gate — Tier-3
        // rules always run when their code surfaces. This is the
        // load-bearing case for the gate carve-out from Phase 3.
        var diags = new List<CheckCommand.Diag>
        {
            new("App.cs", 5, 3, "error", "CS1955", "non-invocable member"),
        };
        var registry = RuleRegistry.Of(new IRulePattern[] { new CS1955TestRule() });
        Assert.True(SuggesterOrchestrator.AnyDiagnosticIsSuggestable(diags, tier2Enabled: false, rules: registry));
    }

    [Fact]
    public void Rule_evidence_carries_provenance_tag()
    {
        var diag = MakeDiagFor("CS1955", "GridSize", out var c);

        var registry = RuleRegistry.Of(new IRulePattern[] { new CS1955TestRule() });
        var orch = new SuggesterOrchestrator(rules: registry);
        var s = orch.SuggestAgainst(diag, c);

        Assert.NotNull(s);
        Assert.Equal("missing parens on GridSize (cluster:C0004)", s!.Evidence);
    }

    /// <summary>
    /// Builds a compilation with a real CS1061 on `ButtonElement.Labl`
    /// (typo for `Label`). Usings come BEFORE the stub namespace
    /// declaration — `using` after a top-level namespace is itself an
    /// error and would mask the CS1061 we're trying to surface.
    /// </summary>
    static (Microsoft.CodeAnalysis.CSharp.CSharpCompilation Compilation, CheckCommand.Diag Diag) MakeCS1061OnButtonLabl()
    {
        const string source = @"
using Microsoft.UI.Reactor.Core;
namespace Microsoft.UI.Reactor.Core
{
    public sealed class ButtonElement
    {
        public string Label { get; set; } = """";
    }
}
class Test { void M() { var b = new ButtonElement(); var x = b.Labl; } }
";
        var c = TestCompilation.Create(source);
        var roslynDiag = c.GetDiagnostics().First(d => d.Id == "CS1061");
        var span = roslynDiag.Location.GetLineSpan();
        var diag = new CheckCommand.Diag(
            span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            "error", "CS1061", roslynDiag.GetMessage());
        return (c, diag);
    }
}
