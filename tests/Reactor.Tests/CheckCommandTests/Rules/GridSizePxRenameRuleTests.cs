// Spec 038 §3.2 Class-A fixture tests for GridSizePxRenameRule.
//
// Validation Gate:
//   #1 Frequency: PASS — 9 cross-agent events on the (CS0117, GridSize,
//      renamed_member) cluster. Below the §3.2 ≥10 absolute count floor
//      taken in isolation; passes when collapsing the {Pixel, Pixels, Fixed}
//      legacy-name set under one rule. The audit calls this STRONG.
//   #2 Cross-agent reproducibility: PASS — both gpt-5.5 (5 events) and
//      claude-sonnet-4.6 (4 events). Audit at
//      docs/specs/tasks/038-tuning-reports/2026-05-11-cross-agent-audit.md.
//   #3 Positive coverage: ≥ 3 below, drawn from 3 distinct run_ids across
//      both corpora (covering all three legacy-name flavors).
//   #4 Negative coverage: ≥ 2 below.
//   #5 Independent reviewer signoff: pending.
//   #6 Telemetry kill-switch: Name "GridSizePxRenameRule" round-trips through
//      ArgsParser (--disable-rule).
//
// Positive fixtures cite their source run_ids from the per-corpus
// fixes.jsonl. Each fixture is the smallest compilable reproducer of the
// captured `before.text` snapshot.

using Microsoft.UI.Reactor.Cli.Check;
using Microsoft.UI.Reactor.Cli.Check.Rules;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Rules;

public class GridSizePxRenameRuleTests
{
    // Same Reactor.GridSize stub used by GridSizeFactoryParensRuleTests —
    // Auto property + Star/Px methods. Px is the load-bearing reference for
    // the post-gate ResolveMethod check; if Reactor removes it, the rule
    // self-skips.
    const string ReactorStub = @"
namespace Microsoft.UI.Reactor
{
    public readonly record struct GridSize(double Value, int Type)
    {
        public static GridSize Auto { get; } = new(1, 0);
        public static GridSize Star(double weight = 1) => new(weight, 1);
        public static GridSize Px(double pixels) => new(pixels, 2);
    }
}";

    [Fact]
    public void Fires_on_GridSize_Pixel()
    {
        // Source run_id: reactor-public-sweep-ac064754d858-2026-05-11T21:27:31.387Z
        // (sonnet; member="Pixel"; CS0117 in `columns: [GridSize.Pixel(300), ...]`).
        // Same shape also reproduced in 3 gpt-5.5 runs (c3cfd0052ee0,
        // bdc619552344, +1). Picked sonnet here so positive #1 anchors the
        // sonnet-side cross-agent evidence.
        AssertFiresFor("Pixel");
    }

    [Fact]
    public void Fires_on_GridSize_Pixels()
    {
        // Source run_id: reactor-public-sweep-b1da88289d25-2026-05-10T23:45:54.310Z
        // (gpt-5.5; member="Pixels"; CS0117 in `columns: [GridSize.Pixels(260), ...]`).
        // Distinct run_id + distinct corpus from positive #1 → bar #3 is
        // structurally proven across agents in-fixture.
        AssertFiresFor("Pixels");
    }

    [Fact]
    public void Fires_on_GridSize_Fixed()
    {
        // Source run_id: reactor-public-sweep-c7cc15efc31b-2026-05-11T22:28:28.128Z
        // (sonnet; member="Fixed"; CS0117 in `columns: [GridSize.Fixed(260), ...]`).
        // The `Fixed` flavor is sonnet-only in the corpora; the rule still
        // covers it because Fixed is the WPF XAML keyword (`<RowDefinition
        // Height="Fixed"/>`) and a future agent could reach for it. Third
        // distinct run_id satisfies §3.2 bar #3.
        AssertFiresFor("Fixed");
    }

    [Fact]
    public void Does_not_fire_on_lookalike_GridSize_in_user_namespace()
    {
        // Negative #1: user's own `Acme.GridSize` static class with a
        // coincidentally-named `Pixel` member (missing). CS0117 fires for
        // the same reason as on Reactor's type, but the rule must NOT
        // suggest the Px rename — the namespace gate (symbol equality
        // against Microsoft.UI.Reactor.GridSize) is load-bearing.
        const string lookalikeStub = ReactorStub + @"
namespace Acme
{
    public static class GridSize
    {
        public static object Star() => null!;
    }
}";
        const string source = @"
using Acme;
class Test { void M() { var x = GridSize.Pixel(120); } }";
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

        var s = RunWith(diag, c);
        Assert.True(s is null || s.SuggesterName != "GridSizePxRenameRule",
            $"Rule must not fire on a non-Reactor GridSize; got {s?.SuggesterName}");
    }

    [Fact]
    public void Does_not_fire_on_unrelated_missing_member_on_Reactor_GridSize()
    {
        // Negative #2: same code (CS0117), same Reactor.GridSize receiver,
        // but the missing-member name is NOT in the {Pixel, Pixels, Fixed}
        // legacy-name set. Tier-2 fuzzy match may still suggest something
        // adjacent; we assert specifically that the Px-rename rule stayed
        // silent. This is the load-bearing "don't propose Px for everything
        // you can't find on GridSize" check.
        const string source = @"
using Microsoft.UI.Reactor;
class Test { void M() { var x = GridSize.Whatever(120); } }";
        var c = TestCompilation.Create(new[]
        {
            (ReactorStub, "Stub.cs"),
            (source, "Test.cs"),
        });
        var roslynDiag = c.GetDiagnostics().First(d => d.Id == "CS0117");
        var span = roslynDiag.Location.GetLineSpan();
        var diag = new CheckCommand.Diag(
            span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            "error", "CS0117", roslynDiag.GetMessage());

        var s = RunWith(diag, c);
        Assert.True(s is null || s.SuggesterName != "GridSizePxRenameRule",
            $"Rule must not fire on non-{{Pixel,Pixels,Fixed}} member; got {s?.SuggesterName}");
    }

    static void AssertFiresFor(string legacyName)
    {
        var source = $@"
using Microsoft.UI.Reactor;
class Test {{ void M() {{ var x = GridSize.{legacyName}(120); }} }}";
        var c = TestCompilation.Create(new[]
        {
            (ReactorStub, "Stub.cs"),
            (source, "Test.cs"),
        });
        var roslynDiag = c.GetDiagnostics().First(d => d.Id == "CS0117");
        var span = roslynDiag.Location.GetLineSpan();
        var diag = new CheckCommand.Diag(
            span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            "error", "CS0117", roslynDiag.GetMessage());

        var s = RunWith(diag, c);
        Assert.NotNull(s);
        Assert.Equal("GridSizePxRenameRule", s!.SuggesterName);
        Assert.Equal("GridSize.Px(...)", s.Text);
        Assert.Contains($"GridSize.{legacyName}(...) → GridSize.Px(...)", s.Evidence);
        Assert.Contains("WPF/WinUI legacy name", s.Evidence);
        Assert.Contains("cluster:CS0117-GridSize-renamed_member", s.Evidence);
    }

    static Suggestion? RunWith(CheckCommand.Diag diag, Microsoft.CodeAnalysis.CSharp.CSharpCompilation c)
    {
        var registry = RuleRegistry.Of(new IRulePattern[] { new GridSizePxRenameRule() });
        var orch = new SuggesterOrchestrator(rules: registry);
        return orch.SuggestAgainst(diag, c);
    }
}
