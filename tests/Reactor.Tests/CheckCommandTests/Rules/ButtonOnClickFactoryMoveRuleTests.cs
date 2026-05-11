// Spec 038 §3.2 Class-B fixture tests for ButtonOnClickFactoryMoveRule.
//
// Positive fixtures are hand-authored against the canonical Button("x").OnClick
// shape documented in SKILL.md and referenced throughout the spec. Class-B
// frequency bar is waived (spec §6); structural justification is the citation
// to the WinUI 3 Button.Click event docs via the vocab table.

using Microsoft.UI.Reactor.Cli.Check;
using Microsoft.UI.Reactor.Cli.Check.Rules;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Rules;

public class ButtonOnClickFactoryMoveRuleTests
{
    // Reactor stubs: ButtonElement (the rule's receiver target) + a Button
    // factory whose `onClick` named parameter the rule probes for as a
    // load-bearing post-gate check. A second Border factory with NO onClick
    // parameter is included so the look-alike negative tests can exercise
    // the "receiver isn't ButtonElement" path against a real adjacent shape.
    const string ReactorStub = @"
namespace Microsoft.UI.Reactor.Core
{
    public abstract record Element {}
    public sealed record ButtonElement(string Label = """") : Element;
    public sealed record BorderElement(Element? Child = null) : Element;
}
namespace Microsoft.UI.Reactor
{
    using System;
    using Microsoft.UI.Reactor.Core;
    public static class Factories
    {
        public static ButtonElement Button(string label, Action? onClick = null)
            => new(label);
        public static BorderElement Border(Element child) => new(child);
    }
}";

    [Fact]
    [Trait("Origin", "VocabHandAuthored")]
    public void Fires_on_ButtonElement_OnClick_chained()
    {
        var s = Run(receiverType: "ButtonElement", missingMember: "OnClick");
        Assert.NotNull(s);
        Assert.Equal("ButtonOnClickFactoryMoveRule", s!.SuggesterName);
        Assert.Contains("Button(..., onClick: ...)", s.Text);
        Assert.Contains("not .OnTapped", s.Evidence);
        Assert.Contains("vocab:WinUI3", s.Evidence);
    }

    [Fact]
    [Trait("Origin", "VocabHandAuthored")]
    public void Fires_on_ButtonElement_OnClick_via_local_variable()
    {
        // Same translation through a local-variable receiver — proves the
        // rule binds on the receiver TYPE rather than any factory-call AST
        // shape. The SymbolSuggester's factory-move path used to require
        // the receiver to literally be a `Button(...)` invocation; the rule
        // is shape-agnostic and matches the type alone.
        const string source = @"
using Microsoft.UI.Reactor.Core;
class Test {
    void M() {
        var b = new ButtonElement(""x"");
        var x = b.OnClick;
    }
}";
        var c = TestCompilation.Create(new[]
        {
            (ReactorStub, "Stub.cs"),
            (source, "Test.cs"),
        });
        var diag = FirstCS1061(c);
        var s = RunWith(diag, c);
        Assert.NotNull(s);
        Assert.Equal("ButtonOnClickFactoryMoveRule", s!.SuggesterName);
    }

    [Fact]
    [Trait("Origin", "VocabHandAuthored")]
    public void Fires_on_ButtonElement_OnClick_chained_directly_on_factory()
    {
        // The SKILL.md canonical shape: `Button("x").OnClick(handler)` — the
        // case the rule was named after. Distinct fixture from the
        // local-variable version above so a future regression that breaks
        // factory-anchored receiver resolution lights up here.
        const string source = @"
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
class Test {
    void M() {
        var x = Button(""x"").OnClick;
    }
}";
        var c = TestCompilation.Create(new[]
        {
            (ReactorStub, "Stub.cs"),
            (source, "Test.cs"),
        });
        var diag = FirstCS1061(c);
        var s = RunWith(diag, c);
        Assert.NotNull(s);
        Assert.Equal("ButtonOnClickFactoryMoveRule", s!.SuggesterName);
    }

    [Fact]
    [Trait("Origin", "VocabHandAuthored")]
    public void Does_not_fire_on_BorderElement_OnClick()
    {
        // Negative #1: the cousin pattern in the 525-run corpus (cluster
        // C0102, BorderElement.OnClick → factory-named-arg) is a different
        // rule — Border's factory does NOT accept onClick. This rule must
        // not bleed onto Border; symbol-equality on receiver type rules
        // that out without a separate string check.
        var s = Run(receiverType: "BorderElement", missingMember: "OnClick");
        Assert.True(s is null || s.SuggesterName != "ButtonOnClickFactoryMoveRule",
            $"Rule must not fire on BorderElement; got {s?.SuggesterName}");
    }

    [Fact]
    [Trait("Origin", "VocabHandAuthored")]
    public void Does_not_fire_on_non_OnClick_member_on_ButtonElement()
    {
        // Negative #2: same receiver (ButtonElement), but the missing member
        // isn't OnClick. The rule's authorization is narrow — it speaks
        // only to the OnClick → factory-arg translation. Anything else
        // (typos, unrelated members, gesture events) falls through to
        // Tier-2 fuzzy match.
        var s = Run(receiverType: "ButtonElement", missingMember: "OnTapped");
        Assert.True(s is null || s.SuggesterName != "ButtonOnClickFactoryMoveRule",
            $"Rule must not fire on non-OnClick member; got {s?.SuggesterName}");
    }

    static Suggestion? Run(string receiverType, string missingMember)
    {
        var source = $@"
using Microsoft.UI.Reactor.Core;
class Test {{
    void M() {{
        var r = new {receiverType}({(receiverType == "BorderElement" ? "null" : "")});
        var x = r.{missingMember};
    }}
}}";
        var c = TestCompilation.Create(new[]
        {
            (ReactorStub, "Stub.cs"),
            (source, "Test.cs"),
        });
        var diag = FirstCS1061(c);
        return RunWith(diag, c);
    }

    static CheckCommand.Diag FirstCS1061(Microsoft.CodeAnalysis.CSharp.CSharpCompilation c)
    {
        var roslynDiag = c.GetDiagnostics().First(d => d.Id == "CS1061");
        var span = roslynDiag.Location.GetLineSpan();
        return new CheckCommand.Diag(
            span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            "error", "CS1061", roslynDiag.GetMessage());
    }

    static Suggestion? RunWith(CheckCommand.Diag diag, Microsoft.CodeAnalysis.CSharp.CSharpCompilation c)
    {
        var registry = RuleRegistry.Of(new IRulePattern[] { new ButtonOnClickFactoryMoveRule() });
        var orch = new SuggesterOrchestrator(rules: registry);
        return orch.SuggestAgainst(diag, c);
    }
}
