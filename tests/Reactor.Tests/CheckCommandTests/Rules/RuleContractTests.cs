// Phase 3.1 — rule contract shape tests. Spec 038 §3.1.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.UI.Reactor.Cli.Check.Rules;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Rules;

public class RuleContractTests
{
    [Fact]
    public void RuleContext_is_a_readonly_record_struct()
    {
        var t = typeof(RuleContext);
        Assert.True(t.IsValueType);
        Assert.True(t.IsSealed);
    }

    [Fact]
    public void RuleContext_constructs_with_required_fields()
    {
        var compilation = (CSharpCompilation)CSharpCompilation.Create("X");
        var tree = CSharpSyntaxTree.ParseText("class C {}");
        compilation = compilation.AddSyntaxTrees(tree);
        var sm = compilation.GetSemanticModel(tree);
        var node = tree.GetRoot().DescendantNodes().First();
        var diag = Diagnostic.Create(
            new DiagnosticDescriptor("CS1061", "t", "m", "c", DiagnosticSeverity.Error, true),
            Location.None);
        var resolver = RuleSymbolResolver.For(compilation);

        var ctx = new RuleContext(
            Node: node,
            Diagnostic: diag,
            Receiver: null,
            SemanticModel: sm,
            Compilation: compilation,
            Resolver: resolver);

        Assert.Same(node, ctx.Node);
        Assert.Same(diag, ctx.Diagnostic);
        Assert.Null(ctx.Receiver);
        Assert.Same(sm, ctx.SemanticModel);
        Assert.Same(compilation, ctx.Compilation);
        Assert.Same(resolver, ctx.Resolver);
    }

    [Fact]
    public void Silent_match_has_null_text_and_zero_confidence()
    {
        var s = RuleSuggestion.Silent;
        Assert.Null(s.Text);
        Assert.Equal(0.0, s.Confidence);
        Assert.False(s.HasMatch);
    }

    [Fact]
    public void Match_with_text_reports_HasMatch()
    {
        var s = new RuleSuggestion(".SolidBackground(Theme.Foo)", 0.95, "Theme.AppBackground → Theme.SolidBackground");
        Assert.True(s.HasMatch);
        Assert.Equal(0.95, s.Confidence);
    }
}
