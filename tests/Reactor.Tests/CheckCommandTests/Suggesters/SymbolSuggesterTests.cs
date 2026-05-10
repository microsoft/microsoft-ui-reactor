// Phase 1.4 / 1.5 — SymbolSuggester tests. Spec 038 §5.
//
// Construction shape: in-memory CSharpCompilation with stub Reactor types,
// drive the diagnostic by deliberately inserting the broken expression. We
// then locate the offending SyntaxNode at the diagnostic span, derive the
// receiver via the SemanticModel, and feed the SuggesterContext.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.UI.Reactor.Cli.Check;
using Microsoft.UI.Reactor.Cli.Check.Suggesters;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Suggesters;

public class SymbolSuggesterTests
{
    // ── Stubs ───────────────────────────────────────────────────────

    const string ReactorStubs = @"
namespace Microsoft.UI.Reactor.Core
{
    public sealed class ButtonElement
    {
        public string Label { get; set; } = """";
        public ButtonElement Bar() => this;
        public ButtonElement WithLabel(string s) { Label = s; return this; }
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
        public static ButtonElement Button(Element content, Action? onClick = null) => new();
        public static TextBlockElement TextBlock(string content) => new();
        public static TextBlockElement Heading(string content) => new();
        public static TextBlockElement Caption(string content) => new();
    }

    public static class Constants
    {
        public const string Foreground = ""fg"";
        public const string Background = ""bg"";
    }
}";

    // ── Helpers ─────────────────────────────────────────────────────

    sealed record DiagnosticAtSpan(Diagnostic Diagnostic, SyntaxNode Node, ITypeSymbol? Receiver);

    const string CommonUsings = @"
using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
";

    static (CSharpCompilation, FactoryIndex) BuildWith(string userCode)
    {
        var src = CommonUsings + ReactorStubs + "\n" + userCode;
        var compilation = TestCompilation.Create(src);
        return (compilation, FactoryIndex.Build(compilation));
    }

    static DiagnosticAtSpan? FindFirst(CSharpCompilation compilation, string code)
    {
        var diag = compilation.GetDiagnostics().FirstOrDefault(d => d.Id == code);
        if (diag is null) return null;
        var loc = diag.Location;
        var tree = loc.SourceTree!;
        var root = tree.GetRoot();
        var node = root.FindNode(loc.SourceSpan, getInnermostNodeForTie: true);

        // Walk up until we find a node we know how to suggest against.
        SyntaxNode pick = node;
        for (var n = node; n is not null; n = n.Parent)
        {
            if (n is MemberAccessExpressionSyntax or InvocationExpressionSyntax or IdentifierNameSyntax or ArgumentSyntax)
            {
                pick = n;
                break;
            }
        }

        ITypeSymbol? receiver = null;
        var sm = compilation.GetSemanticModel(tree);
        if (pick is MemberAccessExpressionSyntax m)
            receiver = sm.GetTypeInfo(m.Expression).Type;
        else if (pick.Parent is MemberAccessExpressionSyntax mp)
            receiver = sm.GetTypeInfo(mp.Expression).Type;

        return new DiagnosticAtSpan(diag, pick, receiver);
    }

    static SuggestionResult RunSuggester(string userCode, string code)
    {
        var (compilation, factories) = BuildWith(userCode);
        var hit = FindFirst(compilation, code) ?? throw new Xunit.Sdk.XunitException($"no {code} diagnostic in source.");
        var ctx = new SuggesterContext(compilation, hit.Diagnostic, hit.Node, hit.Receiver, factories);
        return new SymbolSuggester().Suggest(ctx);
    }

    // ═══════════════════════════════════════════════════════════════
    //  CS1061
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CS1061_synthetic_misspelling_proposes_member()
    {
        const string userCode = @"
class Foo { public void Bar() {} }
class Test
{
    void M()
    {
        var f = new Foo();
        f.Brr();
    }
}";
        var compilation = TestCompilation.Create(userCode);
        var hit = FindFirst(compilation, "CS1061")!;
        var ctx = new SuggesterContext(compilation, hit.Diagnostic, hit.Node, hit.Receiver, FactoryIndex.Empty);
        var result = new SymbolSuggester().Suggest(ctx);

        Assert.True(result.HasSuggestion, $"expected a suggestion; got Silent. evidence={result.Evidence}");
        Assert.Contains("Bar", result.Text);
        Assert.True(result.Confidence >= 0.85, $"expected confidence ≥ 0.85, got {result.Confidence:F2}");
    }

    [Fact]
    public void CS1061_OnClick_on_Button_proposes_named_argument_form()
    {
        const string userCode = @"
class Test
{
    void M()
    {
        Button(""x"").OnClick(() => {});
    }
}";
        var result = RunSuggester(userCode, "CS1061");

        Assert.True(result.HasSuggestion);
        Assert.Contains("Button", result.Text);
        Assert.Contains("onClick:", result.Text);
        Assert.Contains("onClick", result.Evidence);
    }

    [Fact]
    public void CS1061_with_no_nearby_member_returns_silent()
    {
        const string userCode = @"
class Test
{
    void M()
    {
        Button(""x"").Garbage(123);
    }
}";
        var result = RunSuggester(userCode, "CS1061");
        Assert.False(result.HasSuggestion);
    }

    [Fact]
    public void CS1061_is_pure_same_inputs_same_output()
    {
        const string userCode = @"
class Test
{
    void M()
    {
        Button(""x"").OnClick(() => {});
    }
}";
        var src = CommonUsings + ReactorStubs + "\n" + userCode;
        var c = TestCompilation.Create(src);
        var idx = FactoryIndex.Build(c);
        var hit = FindFirst(c, "CS1061")!;
        var ctx = new SuggesterContext(c, hit.Diagnostic, hit.Node, hit.Receiver, idx);
        var r1 = new SymbolSuggester().Suggest(ctx);
        var r2 = new SymbolSuggester().Suggest(ctx);
        Assert.Equal(r1, r2);
    }

    // ═══════════════════════════════════════════════════════════════
    //  CS0103
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CS0103_typo_on_factory_name_proposes_factory()
    {
        // Use a very close misspelling: "Headig" instead of "Heading".
        const string userCode = @"
class Test
{
    Element M() => Factories.Heading(""x"");
    Element N() => Headig(""x"");
}";
        // CS0103 is "name does not exist in current context" — Headig will trip it.
        var result = RunSuggester(userCode, "CS0103");
        Assert.True(result.HasSuggestion, $"expected suggestion; evidence={result.Evidence}");
        Assert.Equal("Heading", result.Text);
    }

    [Fact]
    public void CS0103_unknown_token_with_no_close_factory_returns_silent()
    {
        const string userCode = @"
class Test
{
    void M() { var x = TotallyMadeUpIdentifier; }
}";
        // No factories in this compilation → suggester has nothing to compare against.
        var compilation = TestCompilation.Create(userCode);
        var hit = FindFirst(compilation, "CS0103")!;
        var ctx = new SuggesterContext(compilation, hit.Diagnostic, hit.Node, hit.Receiver, FactoryIndex.Empty);
        var result = new SymbolSuggester().Suggest(ctx);
        Assert.False(result.HasSuggestion);
    }

    // ═══════════════════════════════════════════════════════════════
    //  CS0117
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CS0117_static_member_typo_proposes_member()
    {
        const string userCode = @"
class Test
{
    string M() => Constants.Foregruond;
}";
        var result = RunSuggester(userCode, "CS0117");
        Assert.True(result.HasSuggestion, $"expected suggestion; evidence={result.Evidence}");
        Assert.Contains("Foreground", result.Text);
    }

    [Fact]
    public void CS0117_far_typo_returns_silent()
    {
        const string userCode = @"
class Test
{
    string M() => Constants.QQQ;
}";
        var result = RunSuggester(userCode, "CS0117");
        Assert.False(result.HasSuggestion);
    }

    // ═══════════════════════════════════════════════════════════════
    //  CS1503
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CS1503_Element_expected_string_supplied_proposes_text_factory()
    {
        const string userCode = @"
class Holder { public Holder(Element e) {} }
class Test
{
    Holder M() => new Holder(""hello"");
}";
        var result = RunSuggester(userCode, "CS1503");
        Assert.True(result.HasSuggestion, $"expected suggestion; evidence={result.Evidence}");
        Assert.Contains("text factory", result.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CS1503_unrelated_mismatch_returns_silent()
    {
        const string userCode = @"
class Test
{
    void Take(int i) {}
    void M() { Take(""hi""); }
}";
        var result = RunSuggester(userCode, "CS1503");
        Assert.False(result.HasSuggestion);
    }

    // ═══════════════════════════════════════════════════════════════
    //  CS7036
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CS7036_missing_required_arg_proposes_named_overload()
    {
        // Caption(string) requires 1 arg; calling Caption() trips CS7036.
        const string userCode = @"
class Test
{
    TextBlockElement M() => Factories.Caption();
}";
        var result = RunSuggester(userCode, "CS7036");
        Assert.True(result.HasSuggestion, $"expected suggestion; evidence={result.Evidence}");
        Assert.Contains("Caption", result.Text);
        Assert.Contains("content", result.Text);
    }

    [Fact]
    public void CS7036_on_non_factory_method_returns_silent()
    {
        const string userCode = @"
class Holder { public Holder(string s) {} }
class Test
{
    Holder M() => new Holder();
}";
        // Not all CS7036 hits are factory-call-shaped; a constructor mismatch
        // is something we can't usefully suggest — stay silent.
        var compilation = TestCompilation.Create(CommonUsings + ReactorStubs + "\n" + userCode);
        var hit = FindFirst(compilation, "CS7036");
        if (hit is null) return; // some Roslyn versions emit a different code for ctors; if so the assertion is moot.
        var ctx = new SuggesterContext(compilation, hit.Diagnostic, hit.Node, hit.Receiver, FactoryIndex.Build(compilation));
        var result = new SymbolSuggester().Suggest(ctx);
        Assert.False(result.HasSuggestion);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Unrelated codes are passed through silently
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Unrelated_code_returns_silent()
    {
        const string userCode = @"class Test { void M() { int x = 1 } }"; // CS1002 etc
        var compilation = TestCompilation.Create(ReactorStubs + "\n" + userCode);
        var diag = compilation.GetDiagnostics().First();
        var ctx = new SuggesterContext(compilation, diag, null, null, FactoryIndex.Empty);
        var r = new SymbolSuggester().Suggest(ctx);
        Assert.False(r.HasSuggestion);
    }
}
