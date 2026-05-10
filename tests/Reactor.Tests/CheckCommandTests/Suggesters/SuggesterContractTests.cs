// Phase 1.1 — suggester contract shape tests. Spec 038 §1.1.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.UI.Reactor.Cli.Check;
using Microsoft.UI.Reactor.Cli.Check.Suggesters;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Suggesters;

public class SuggesterContractTests
{
    [Fact]
    public void SuggesterContext_is_a_readonly_record_struct()
    {
        var t = typeof(SuggesterContext);
        Assert.True(t.IsValueType);
        // record struct is sealed implicitly when emitted by the compiler
        Assert.True(t.IsSealed);
    }

    [Fact]
    public void SuggesterContext_constructs_with_required_fields_and_optional_nulls()
    {
        var compilation = (CSharpCompilation)CSharpCompilation.Create("X");
        var diag = Diagnostic.Create(
            new DiagnosticDescriptor("CS1061", "t", "m", "c", DiagnosticSeverity.Error, true),
            Location.None);

        var ctx = new SuggesterContext(
            Compilation: compilation,
            Diagnostic: diag,
            Node: null,
            Receiver: null,
            Factories: FactoryIndex.Empty);

        Assert.Same(compilation, ctx.Compilation);
        Assert.Same(diag, ctx.Diagnostic);
        Assert.Null(ctx.Node);
        Assert.Null(ctx.Receiver);
        Assert.Same(FactoryIndex.Empty, ctx.Factories);
    }

    [Fact]
    public void Silent_result_has_null_text_and_zero_confidence()
    {
        var s = SuggestionResult.Silent;
        Assert.Null(s.Text);
        Assert.Equal(0.0, s.Confidence);
        Assert.False(s.HasSuggestion);
    }

    [Fact]
    public void Suggestion_with_text_reports_HasSuggestion()
    {
        var s = new SuggestionResult("try: Button(label, onClick: x)", 0.91, "[factory has Action onClick parameter]");
        Assert.True(s.HasSuggestion);
        Assert.Equal(0.91, s.Confidence);
    }
}
