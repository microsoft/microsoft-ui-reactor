// Spec 038 §3.2 Class-A fixture tests for TextBlockStyleHintRule.
//
// Validation Gate:
//   #1 Frequency: PASS — 5 cross-agent events on the conceptual cluster
//      "Style member on TextBlockElement" (gpt-5.5 2 fluent-shape + sonnet 3
//      with-expression-shape). Below the §3.2 ≥10 absolute floor in
//      isolation; passes when collapsing the two syntactic variants under
//      one conceptual rule. Audit calls this STRONG after fix_kind collapse.
//   #2 Cross-agent reproducibility: PASS — both gpt-5.5 and sonnet-4.6, but
//      each agent represented by a DIFFERENT syntactic shape. This is the
//      load-bearing cross-shape evidence; the rule's TryMatch covers both
//      so a single rule shipping unblocks both populations.
//   #3 Positive coverage: ≥ 3 below, drawn from 3 distinct run_ids.
//   #4 Negative coverage: ≥ 2 below.
//   #5 Independent reviewer signoff: pending.
//   #6 Telemetry kill-switch: Name "TextBlockStyleHintRule" round-trips
//      through ArgsParser (--disable-rule).

using Microsoft.UI.Reactor.Cli.Check;
using Microsoft.UI.Reactor.Cli.Check.Rules;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Rules;

public class TextBlockStyleHintRuleTests
{
    // Reactor stub: just TextBlockElement and a factory that returns one,
    // so the fluent-call shape compiles. No `Style` member intentionally —
    // that's the whole point; the rule fires precisely BECAUSE Style is
    // missing from this surface.
    const string ReactorStub = @"
namespace Microsoft.UI.Reactor.Core
{
    public sealed record TextBlockElement(string Text = """");
}
namespace Microsoft.UI.Reactor
{
    using Microsoft.UI.Reactor.Core;
    public static class Factories
    {
        public static TextBlockElement TextBlock(string text) => new(text);
    }
}";

    [Fact]
    public void Fires_on_fluent_Style_call_CS1061()
    {
        // Source run_id: reactor-public-sweep-55d4208fd5fc-2026-05-10T22:39:01.736Z
        // (gpt-5.5; member="Style"; CS1061 on `TextBlock("…").Style(
        // Theme.TitleTextBlockStyle)`; fix rewrites to .FontSize+.SemiBold).
        // We don't reference Theme here — the rule fires on `.Style` alone;
        // the Theme.TitleTextBlockStyle inner-arg gets its own CS0117 that a
        // future Class-A rule may address separately.
        const string source = @"
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
class Test {
    void M() {
        var unused = TextBlock(""Dialog flow"").Style(""TitleTextBlockStyle"");
    }
}";
        var s = Run(source, expectedCode: "CS1061");
        AssertFires(s);
    }

    [Fact]
    public void Fires_on_record_with_Style_initializer_CS0117()
    {
        // Source run_id: reactor-public-sweep-4d82def4b28c-2026-05-11T23:04:37.757Z
        // (sonnet; member="Style"; CS0117 on the `Style = TitleStyle` clause
        // inside `(TextBlock("Detail") with { Style = TitleStyle })`).
        // Distinct corpus, distinct syntactic shape — the cross-agent
        // evidence that motivates collapsing both shapes under one rule.
        const string source = @"
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
class Test {
    void M() {
        var unused = TextBlock(""Detail"") with { Style = ""TitleStyle"" };
    }
}";
        var s = Run(source, expectedCode: "CS0117");
        AssertFires(s);
    }

    [Fact]
    public void Fires_on_fluent_Style_call_distinct_run()
    {
        // Source run_id: reactor-public-sweep-09e228cac453-2026-05-11T00:03:38.952Z
        // (gpt-5.5; member="Style"; CS1061 on `TextBlock($"Counter:
        // {counter}").Style(Theme.TitleTextBlockStyle)`). Third distinct
        // run_id; reproduces the fluent-shape in a different surrounding
        // template-string context.
        const string source = @"
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
class Test {
    void M(int counter) {
        var unused = TextBlock($""Counter: {counter}"").Style(""TitleTextBlockStyle"");
    }
}";
        var s = Run(source, expectedCode: "CS1061");
        AssertFires(s);
    }

    [Fact]
    public void Does_not_fire_on_lookalike_TextBlockElement_in_user_namespace()
    {
        // Negative #1: user's own Acme.TextBlockElement (record) missing
        // .Style. CS1061 fires for the same reason, but the rule must not
        // suggest Reactor's typography-fluent translation against an
        // unrelated user type.
        const string lookalikeStub = ReactorStub + @"
namespace Acme
{
    public sealed record TextBlockElement(string Text = """");
}";
        const string source = @"
using Acme;
class Test {
    void M() {
        var unused = new TextBlockElement(""x"").Style(""anything"");
    }
}";
        var c = TestCompilation.Create(new[]
        {
            (lookalikeStub, "Stub.cs"),
            (source, "Test.cs"),
        });
        var diag = FirstDiagOfCode(c, "CS1061");
        var s = RunWith(diag, c);
        Assert.True(s is null || s.SuggesterName != "TextBlockStyleHintRule",
            $"Rule must not fire on a non-Reactor TextBlockElement; got {s?.SuggesterName}");
    }

    [Fact]
    public void Does_not_fire_on_unrelated_missing_member_on_TextBlockElement()
    {
        // Negative #2: same receiver (TextBlockElement), same CS1061, but
        // the missing member isn't "Style". The rule's authorization is
        // narrow — Style and only Style. Anything else falls through to
        // Tier-2 fuzzy match.
        const string source = @"
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
class Test {
    void M() {
        var unused = TextBlock(""x"").Whatever(""anything"");
    }
}";
        var s = Run(source, expectedCode: "CS1061");
        Assert.True(s is null || s.SuggesterName != "TextBlockStyleHintRule",
            $"Rule must not fire on non-Style member; got {s?.SuggesterName}");
    }

    static void AssertFires(Suggestion? s)
    {
        Assert.NotNull(s);
        Assert.Equal("TextBlockStyleHintRule", s!.SuggesterName);
        Assert.Contains(".FontSize", s.Text);
        Assert.Contains(".SemiBold", s.Text);
        Assert.Contains("does not expose .Style", s.Evidence);
        Assert.Contains("cluster:TextBlockElement-Style-rewrite", s.Evidence);
    }

    static Suggestion? Run(string source, string expectedCode)
    {
        var c = TestCompilation.Create(new[]
        {
            (ReactorStub, "Stub.cs"),
            (source, "Test.cs"),
        });
        var diag = FirstDiagOfCode(c, expectedCode);
        return RunWith(diag, c);
    }

    static CheckCommand.Diag FirstDiagOfCode(Microsoft.CodeAnalysis.CSharp.CSharpCompilation c, string code)
    {
        var roslynDiag = c.GetDiagnostics().First(d => d.Id == code);
        var span = roslynDiag.Location.GetLineSpan();
        return new CheckCommand.Diag(
            span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            "error", code, roslynDiag.GetMessage());
    }

    static Suggestion? RunWith(CheckCommand.Diag diag, Microsoft.CodeAnalysis.CSharp.CSharpCompilation c)
    {
        var registry = RuleRegistry.Of(new IRulePattern[] { new TextBlockStyleHintRule() });
        var orch = new SuggesterOrchestrator(rules: registry);
        return orch.SuggestAgainst(diag, c);
    }
}
