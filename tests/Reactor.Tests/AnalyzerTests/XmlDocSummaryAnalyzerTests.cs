using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="XmlDocSummaryAnalyzer"/> — REACTOR_DOC_001
/// (public API missing XML doc summary). Spec 041 §10.4.
/// </summary>
public class XmlDocSummaryAnalyzerTests
{
    [Fact]
    public async Task PublicType_WithoutSummary_RaisesDoc001()
    {
        var test = @"
public class Foo {}
";
        var expected = new DiagnosticResult(XmlDocSummaryAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithSpan(2, 14, 2, 17)
            .WithArguments("class", "Foo");

        await new CSharpAnalyzerTest<XmlDocSummaryAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        }.RunAsync();
    }

    [Fact]
    public async Task PublicType_WithSummary_NoDiagnostic()
    {
        var test = @"
/// <summary>A foo.</summary>
public class Foo {}
";
        await new CSharpAnalyzerTest<XmlDocSummaryAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        }.RunAsync();
    }

    [Fact]
    public async Task InternalType_NoDiagnostic()
    {
        var test = @"
internal class Foo {}
";
        await new CSharpAnalyzerTest<XmlDocSummaryAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        }.RunAsync();
    }

    [Fact]
    public async Task Override_InheritsSummary_NoDiagnostic()
    {
        var test = @"
/// <summary>Base.</summary>
public abstract class Base
{
    /// <summary>The abstract M.</summary>
    public abstract void M();
}

/// <summary>Derived.</summary>
public class Derived : Base
{
    public override void M() {}
}
";
        await new CSharpAnalyzerTest<XmlDocSummaryAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        }.RunAsync();
    }

    [Fact]
    public async Task GeneratedCode_Attribute_NoDiagnostic()
    {
        var test = @"
[System.CodeDom.Compiler.GeneratedCode(""tool"", ""1.0"")]
public class GeneratedFoo {}
";
        await new CSharpAnalyzerTest<XmlDocSummaryAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        }.RunAsync();
    }

    [Fact]
    public async Task PublicMethod_WithoutSummary_RaisesDoc001()
    {
        var test = @"
/// <summary>Foo.</summary>
public class Foo
{
    public void Bar() {}
}
";
        var expected = new DiagnosticResult(XmlDocSummaryAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithSpan(5, 17, 5, 20)
            .WithArguments("method", "Foo.Bar()");

        await new CSharpAnalyzerTest<XmlDocSummaryAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        }.RunAsync();
    }
}
