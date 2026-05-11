// Phase 1.6 — orchestrator wiring tests. Spec 038 §1.6.

using Microsoft.UI.Reactor.Cli.Check;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests;

public class SuggesterOrchestratorTests
{
    const string Stubs = @"
namespace Microsoft.UI.Reactor.Core
{
    public sealed class ButtonElement
    {
        public string Label { get; set; } = """";
    }
    public abstract class Element { }
    public sealed class TextBlockElement : Element { }
}

namespace Microsoft.UI.Reactor
{
    using Microsoft.UI.Reactor.Core;
    using System;

    public static class Factories
    {
        public static ButtonElement Button(string label, Action? onClick = null) => new();
        public static TextBlockElement TextBlock(string content) => new();
        public static TextBlockElement Heading(string content) => new();
    }
}";

    [Fact]
    public void Unsupported_code_short_circuits_to_null()
    {
        var c = TestCompilation.Create(Stubs);
        var orch = new SuggesterOrchestrator();
        var diag = new CheckCommand.Diag("Whatever.cs", 1, 1, "warning", "CS8602", "nullable thing");
        Assert.Null(orch.SuggestAgainst(diag, c));
    }

    [Fact]
    public void CS1061_on_NonReactor_receiver_is_filtered_out()
    {
        const string user = @"
class Foo { public void Bar() {} }
class Test { void M() { var f = new Foo(); f.Brr(); } }
";
        var c = TestCompilation.Create(user);
        var roslynDiag = c.GetDiagnostics().First(d => d.Id == "CS1061");
        var (line, col, file) = AsMSBuild(roslynDiag);
        var diag = new CheckCommand.Diag(file, line, col, "error", "CS1061", roslynDiag.GetMessage());
        var orch = new SuggesterOrchestrator();
        Assert.Null(orch.SuggestAgainst(diag, c));
    }

    [Fact]
    public void CS1061_on_Reactor_receiver_emits_suggestion()
    {
        const string usings = @"
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
";
        const string user = @"
class Test
{
    void M() { Button(""x"").OnClick(() => {}); }
}";
        var c = TestCompilation.Create(usings + Stubs + user);
        var roslynDiag = c.GetDiagnostics().First(d => d.Id == "CS1061");
        var (line, col, file) = AsMSBuild(roslynDiag);
        var diag = new CheckCommand.Diag(file, line, col, "error", "CS1061", roslynDiag.GetMessage());
        var orch = new SuggesterOrchestrator();
        var s = orch.SuggestAgainst(diag, c);
        Assert.NotNull(s);
        Assert.Contains("Button", s!.Text);
        Assert.Contains("onClick:", s.Text);
        Assert.True(s.Confidence >= 0.75);
    }

    [Fact]
    public void EmitDiagnostics_includes_suggestion_text_and_evidence_in_line()
    {
        var diags = new[]
        {
            new CheckCommand.Diag("Program.cs", 10, 5, "error", "CS1061", "msg"),
        };
        var sw = new StringWriter();
        CheckCommand.EmitDiagnostics(diags, sw, trace: null,
            suggest: _ => new Suggestion("Button(label, onClick: x)", 0.9, "factory has Action onClick parameter", "SymbolSuggester"));

        var line = sw.ToString();
        Assert.Contains("→ try: Button(label, onClick: x)", line);
        Assert.Contains("[factory has Action onClick parameter]", line);
    }

    [Fact]
    public void Tier1_hint_wins_over_Tier2_suggestion()
    {
        // REACTOR_HOOKS_001 is in HintFor — its skill pointer must win.
        var diags = new[]
        {
            new CheckCommand.Diag("X.cs", 1, 1, "error", "REACTOR_HOOKS_001", "msg"),
        };
        var sw = new StringWriter();
        CheckCommand.EmitDiagnostics(diags, sw, trace: null,
            suggest: _ => new Suggestion("never-emitted", 0.95, "should-not-show", "SymbolSuggester"));
        var line = sw.ToString();

        Assert.Contains("SKILL.md §Hooks", line);
        Assert.DoesNotContain("never-emitted", line);
        Assert.DoesNotContain("should-not-show", line);
    }

    [Fact]
    public void When_no_suggestion_line_format_is_unchanged()
    {
        var diags = new[]
        {
            new CheckCommand.Diag("Program.cs", 10, 5, "error", "CS9999", "msg"),
        };
        var sw = new StringWriter();
        CheckCommand.EmitDiagnostics(diags, sw, trace: null, suggest: _ => null);
        var line = sw.ToString().TrimEnd('\r', '\n');
        Assert.Equal("Program.cs:10:5  E  CS9999  msg", line);
    }

    [Fact]
    public void FindTreeFor_suffix_match_requires_path_separator_boundary()
    {
        // Regression for PR-243 review: a diagnostic on a bare "Program.cs"
        // must not accidentally bind to a sibling tree like "MyProgram.cs"
        // just because the longer name ends with the shorter one.
        // Strategy: put a Reactor CS1061 with a near-Label typo in Program.cs,
        // and unrelated valid code in MyProgram.cs. Pre-fix, the suffix match
        // would prefer MyProgram.cs (last-write-wins on the suffix branch) and
        // the suggestion would go silent because the node at the diag's
        // (line, col) wouldn't be inside ButtonElement's reach. Post-fix, we
        // correctly bind to Program.cs and the suggester emits a member hint.
        const string sibling = @"
class Sibling
{
    public void Run()
    {
        // padding so MyProgram.cs is longer than Program.cs — defeats any
        // length-based tiebreak that might mask the bug.
        var x = 1; var y = 2; var z = x + y;
    }
}";
        const string main = @"
using Microsoft.UI.Reactor.Core;
class Test { void M() { var b = new ButtonElement(); var x = b.Labl; } }
";
        var c = TestCompilation.Create(new[]
        {
            (Stubs, "Stubs.cs"),
            (sibling, "MyProgram.cs"),
            (main, "Program.cs"),
        });

        var roslynDiag = c.GetDiagnostics().First(d => d.Id == "CS1061");
        var span = roslynDiag.Location.GetLineSpan();
        // Diagnostic carries the bare filename only — this is the case the
        // pre-fix EndsWith logic mishandled.
        var diag = new CheckCommand.Diag(
            "Program.cs",
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            "error", "CS1061", roslynDiag.GetMessage());

        var orch = new SuggesterOrchestrator();
        var s = orch.SuggestAgainst(diag, c);
        Assert.NotNull(s);
        Assert.Contains("Label", s!.Text);
    }

    static (int line, int col, string file) AsMSBuild(Microsoft.CodeAnalysis.Diagnostic d)
    {
        var span = d.Location.GetLineSpan();
        return (span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1, span.Path);
    }
}
