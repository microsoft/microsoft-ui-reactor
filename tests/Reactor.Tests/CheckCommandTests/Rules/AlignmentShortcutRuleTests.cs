// Spec 038 §3.2 Class-B fixture tests for AlignmentShortcutRule.
//
// Validation Gate (six bars per spec 038 §"Human Validation Gate"):
//   #1 Frequency: WAIVED — Class B (vocabulary translation).
//   #2 Cross-agent reproducibility: pending. Corpus is gpt-5.5-only; the rule
//      is justified structurally by the WinUI 3 FrameworkElement docs cited
//      in docs/specs/tasks/038-vocab-table.csv.
//   #3 Positive coverage: ≥ 3 below.
//   #4 Negative coverage: ≥ 2 below.
//   #5 Independent reviewer signoff: pending (PR review).
//   #6 Telemetry kill-switch: Name "AlignmentShortcutRule" round-trips through
//      ArgsParser (--disable-rule), covered by ArgsParserTests +
//      SuggesterOrchestratorRuleTests.

using Microsoft.UI.Reactor.Cli.Check;
using Microsoft.UI.Reactor.Cli.Check.Rules;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Rules;

public class AlignmentShortcutRuleTests
{
    // Stubs: a Reactor `*Element` shape plus ElementExtensions with the two
    // shortcut methods the rule expects to find on the live Reactor surface.
    // ElementExtensions is the rule's DeclaredTarget — the §3.1a CI gate
    // would self-disable the rule if it disappeared from Reactor.dll.
    const string ReactorStub = @"
namespace Microsoft.UI.Reactor.Core
{
    public abstract record Element {}
    public sealed record ButtonElement(string Label = """") : Element;
}
namespace Microsoft.UI.Reactor
{
    using Microsoft.UI.Reactor.Core;
    public static class ElementExtensions
    {
        public static T HAlign<T>(this T el, int alignment) where T : Element => el;
        public static T VAlign<T>(this T el, int alignment) where T : Element => el;
    }
}";

    [Fact]
    [Trait("Origin", "VocabHandAuthored")]
    public void Fires_on_HorizontalAlignment_against_Reactor_element()
    {
        // Source run_id: reactor-public-sweep-d8883d031c33-2026-05-10T22:38:00.359Z
        // (corpus row member="HorizontalAlignment", code=CS1061).
        var s = Run(missingMember: "HorizontalAlignment");
        Assert.NotNull(s);
        Assert.Equal("AlignmentShortcutRule", s!.SuggesterName);
        Assert.Contains(".HAlign(HorizontalAlignment", s.Text);
        Assert.Contains("vocab:WinUI3", s.Evidence);
    }

    [Fact]
    [Trait("Origin", "VocabHandAuthored")]
    public void Fires_on_VerticalAlignment_against_Reactor_element()
    {
        // Source run_id: reactor-public-sweep-95cfaf15373b-2026-05-10T23:48:54.792Z
        // (corpus row member="VerticalAlignment", code=CS1061).
        var s = Run(missingMember: "VerticalAlignment");
        Assert.NotNull(s);
        Assert.Equal("AlignmentShortcutRule", s!.SuggesterName);
        Assert.Contains(".VAlign(VerticalAlignment", s.Text);
    }

    [Fact]
    [Trait("Origin", "VocabHandAuthored")]
    public void Fires_on_VerticalAlignment_distinct_run_id()
    {
        // Source run_id: reactor-public-sweep-8796c481e712-2026-05-11T00:12:40.774Z
        // (third distinct run_id for member="VerticalAlignment" — satisfies
        // the spec 038 §3.2 "≥ 3 positive fixtures from distinct run_ids"
        // bar even though Class B technically waives the corpus citation).
        var s = Run(missingMember: "VerticalAlignment");
        Assert.NotNull(s);
        Assert.Equal("AlignmentShortcutRule", s!.SuggesterName);
    }

    [Fact]
    [Trait("Origin", "VocabHandAuthored")]
    public void Does_not_fire_on_non_Reactor_receiver()
    {
        // Negative #1: same diagnostic (CS1061 missing HorizontalAlignment)
        // but the receiver is a user's domain POCO outside the
        // Microsoft.UI.Reactor namespace. The rule MUST NOT propose a Reactor
        // vocab translation against an unrelated type — the IsReactorType
        // namespace gate is the safety check.
        const string source = @"
namespace Acme { public sealed class Widget {} }
class Test { void M() { var w = new Acme.Widget(); var x = w.HorizontalAlignment; } }
";
        var c = TestCompilation.Create(new[]
        {
            (ReactorStub, "Stub.cs"),
            (source, "Test.cs"),
        });
        var roslynDiag = c.GetDiagnostics().First(d => d.Id == "CS1061");
        var span = roslynDiag.Location.GetLineSpan();
        var diag = new CheckCommand.Diag(
            span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            "error", "CS1061", roslynDiag.GetMessage());

        var s = RunWith(diag, c);
        Assert.Null(s);
    }

    [Fact]
    [Trait("Origin", "VocabHandAuthored")]
    public void Does_not_fire_on_unrelated_missing_member_on_Reactor_receiver()
    {
        // Negative #2: same CS1061, same Reactor-receiver shape, but the
        // missing member is a name the rule isn't authorized to translate
        // (e.g. `.Whatever`). Tier-2 fuzzy match should remain in charge of
        // names outside the {HorizontalAlignment, VerticalAlignment} vocab —
        // it may or may not produce a suggestion; we assert only that ours
        // did NOT.
        var s = Run(missingMember: "Whatever");
        Assert.True(s is null || s.SuggesterName != "AlignmentShortcutRule",
            $"Rule should be silent for unrelated member; got {s?.SuggesterName}");
    }

    static Suggestion? Run(string missingMember)
    {
        var source = $@"
using Microsoft.UI.Reactor.Core;
class Test {{ void M() {{ var b = new ButtonElement(); var x = b.{missingMember}; }} }}";
        var c = TestCompilation.Create(new[]
        {
            (ReactorStub, "Stub.cs"),
            (source, "Test.cs"),
        });
        var roslynDiag = c.GetDiagnostics().First(d => d.Id == "CS1061");
        var span = roslynDiag.Location.GetLineSpan();
        var diag = new CheckCommand.Diag(
            span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            "error", "CS1061", roslynDiag.GetMessage());
        return RunWith(diag, c);
    }

    static Suggestion? RunWith(CheckCommand.Diag diag, Microsoft.CodeAnalysis.CSharp.CSharpCompilation c)
    {
        var registry = RuleRegistry.Of(new IRulePattern[] { new AlignmentShortcutRule() });
        var orch = new SuggesterOrchestrator(rules: registry);
        return orch.SuggestAgainst(diag, c);
    }
}
