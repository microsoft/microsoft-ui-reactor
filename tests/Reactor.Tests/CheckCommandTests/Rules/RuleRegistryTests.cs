// Phase 3.1 — RuleRegistry discovery, disable, and self-disable tests.
// Spec 038 §3.1 + §3.1a.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.UI.Reactor.Cli.Check.Rules;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Rules;

public class RuleRegistryTests
{
    const string ReactorStub = @"
namespace Microsoft.UI.Reactor.Core
{
    public sealed class ButtonElement {}
}";

    sealed class HelloRule : IRulePattern
    {
        public string Name => "HelloRule";
        public string Provenance => "cluster:test";
        public IReadOnlyList<string> DiagnosticCodes => new[] { "CS9999" };
        public IReadOnlyList<string> DeclaredTargets => Array.Empty<string>();
        public RuleSuggestion TryMatch(in RuleContext ctx)
            => new(".hello()", 0.9, "stub");
    }

    sealed class WorldRule : IRulePattern
    {
        public string Name => "WorldRule";
        public string Provenance => "vocab:WinUI3";
        public IReadOnlyList<string> DiagnosticCodes => new[] { "CS9999" };
        public IReadOnlyList<string> DeclaredTargets => Array.Empty<string>();
        public RuleSuggestion TryMatch(in RuleContext ctx)
            => new(".world()", 0.95, "stub");
    }

    sealed class UnresolvedTargetRule : IRulePattern
    {
        public string Name => "UnresolvedTargetRule";
        public string Provenance => "cluster:x";
        public IReadOnlyList<string> DiagnosticCodes => new[] { "CS9999" };
        public IReadOnlyList<string> DeclaredTargets => new[] { "Acme.NotPresent" };
        public RuleSuggestion TryMatch(in RuleContext ctx)
            => new("should not fire", 1.0, "");
    }

    sealed class ThrowingRule : IRulePattern
    {
        public string Name => "ThrowingRule";
        public string Provenance => "cluster:throw";
        public IReadOnlyList<string> DiagnosticCodes => new[] { "CS9999" };
        public IReadOnlyList<string> DeclaredTargets => Array.Empty<string>();
        public RuleSuggestion TryMatch(in RuleContext ctx)
            => throw new InvalidOperationException("rule blew up");
    }

    sealed class DupNameRule : IRulePattern
    {
        public string Name => "HelloRule"; // collides with HelloRule above
        public string Provenance => "cluster:dup";
        public IReadOnlyList<string> DiagnosticCodes => Array.Empty<string>();
        public IReadOnlyList<string> DeclaredTargets => Array.Empty<string>();
        public RuleSuggestion TryMatch(in RuleContext ctx) => RuleSuggestion.Silent;
    }

    [Fact]
    public void Of_orders_rules_by_name_for_stable_listing()
    {
        var registry = RuleRegistry.Of(new IRulePattern[] { new WorldRule(), new HelloRule() });
        Assert.Equal(new[] { "HelloRule", "WorldRule" }, registry.All.Select(r => r.Name));
    }

    [Fact]
    public void Of_rejects_duplicate_rule_names()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RuleRegistry.Of(new IRulePattern[] { new HelloRule(), new DupNameRule() }));
        Assert.Contains("HelloRule", ex.Message);
    }

    [Fact]
    public void TryGet_returns_registered_rule_by_name()
    {
        var registry = RuleRegistry.Of(new IRulePattern[] { new HelloRule() });
        Assert.True(registry.TryGet("HelloRule", out var rule));
        Assert.Equal("HelloRule", rule.Name);
        Assert.False(registry.TryGet("missing", out _));
    }

    [Fact]
    public void BestMatch_returns_highest_confidence_match()
    {
        var registry = RuleRegistry.Of(new IRulePattern[] { new HelloRule(), new WorldRule() });
        var ctx = MakeContext("CS9999");
        var hit = registry.BestMatch(in ctx);
        Assert.NotNull(hit);
        Assert.Equal("WorldRule", hit!.Value.Rule.Name);  // 0.95 > 0.9
    }

    [Fact]
    public void BestMatch_skips_user_disabled_rules()
    {
        var registry = RuleRegistry.Of(new IRulePattern[] { new HelloRule(), new WorldRule() });
        var ctx = MakeContext("CS9999");
        var disabled = new HashSet<string>(new[] { "WorldRule" }, StringComparer.Ordinal);
        var hit = registry.BestMatch(in ctx, disabled);
        Assert.NotNull(hit);
        Assert.Equal("HelloRule", hit!.Value.Rule.Name);
    }

    [Fact]
    public void BestMatch_self_skips_rules_whose_targets_do_not_resolve()
    {
        var registry = RuleRegistry.Of(new IRulePattern[] { new UnresolvedTargetRule() });
        var reported = new List<(string Name, string Target)>();
        var ctx = MakeContext("CS9999");
        var hit = registry.BestMatch(in ctx, disabledNames: null,
            onSelfDisabled: (name, target) => reported.Add((name, target)));
        Assert.Null(hit);
        Assert.Single(reported);
        Assert.Equal(("UnresolvedTargetRule", "Acme.NotPresent"), reported[0]);
    }

    [Fact]
    public void BestMatch_swallows_rule_exceptions()
    {
        var registry = RuleRegistry.Of(new IRulePattern[] { new ThrowingRule(), new HelloRule() });
        var ctx = MakeContext("CS9999");
        // Throwing rule must not crash the run; HelloRule still wins.
        var hit = registry.BestMatch(in ctx);
        Assert.NotNull(hit);
        Assert.Equal("HelloRule", hit!.Value.Rule.Name);
    }

    [Fact]
    public void Statuses_reports_user_disabled_self_disabled_and_enabled()
    {
        var registry = RuleRegistry.Of(new IRulePattern[]
        {
            new HelloRule(), new WorldRule(), new UnresolvedTargetRule(),
        });
        var c = TestCompilation.Create(ReactorStub);
        var disabled = new HashSet<string>(new[] { "WorldRule" }, StringComparer.Ordinal);
        var statuses = registry.Statuses(c, disabled);

        Assert.Equal(RuleState.Enabled, statuses.First(s => s.Name == "HelloRule").State);
        Assert.Equal(RuleState.UserDisabled, statuses.First(s => s.Name == "WorldRule").State);
        var self = statuses.First(s => s.Name == "UnresolvedTargetRule");
        Assert.Equal(RuleState.SelfDisabled, self.State);
        Assert.Equal("Acme.NotPresent", self.UnresolvedTarget);
    }

    [Fact]
    public void Default_registry_is_lazy_and_singleton()
    {
        var r1 = RuleRegistry.Default;
        var r2 = RuleRegistry.Default;
        Assert.Same(r1, r2);
    }

    static RuleContext MakeContext(string code)
    {
        var c = TestCompilation.Create(ReactorStub);
        var tree = c.SyntaxTrees.First();
        var sm = c.GetSemanticModel(tree);
        var node = tree.GetRoot().DescendantNodes().First();
        var diag = Diagnostic.Create(
            new DiagnosticDescriptor(code, "t", "m", "c", DiagnosticSeverity.Error, true),
            Location.None);
        var resolver = RuleSymbolResolver.For(c);
        return new RuleContext(node, diag, null, sm, c, resolver);
    }
}
