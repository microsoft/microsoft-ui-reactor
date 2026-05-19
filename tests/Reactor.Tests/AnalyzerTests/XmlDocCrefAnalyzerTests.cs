using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="XmlDocCrefAnalyzer"/> — REACTOR_DOC_002
/// (XML doc cref does not resolve). Spec 041 §10.4.
/// </summary>
public class XmlDocCrefAnalyzerTests
{
    [Fact]
    public async Task ValidCref_NoDiagnostic()
    {
        var test = @"
/// <summary>Foo.</summary>
public class Foo
{
    /// <summary>Bar. See <see cref=""Foo""/>.</summary>
    public void Bar() {}
}
";
        await new CSharpAnalyzerTest<XmlDocCrefAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            // The analyzer doesn't itself control whether CS1591 is on; only check
            // our diagnostic doesn't trip on valid crefs.
            CompilerDiagnostics = CompilerDiagnostics.None,
        }.RunAsync();
    }

    [Fact]
    public async Task InvalidCref_RaisesDoc002()
    {
        var test = @"
/// <summary>Foo. See <see cref=""DoesNotExist""/>.</summary>
public class Foo {}
";
        var expected = new DiagnosticResult(XmlDocCrefAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithSpan(2, 34, 2, 46)
            .WithArguments("DoesNotExist");

        await new CSharpAnalyzerTest<XmlDocCrefAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            CompilerDiagnostics = CompilerDiagnostics.None,
            ExpectedDiagnostics = { expected },
        }.RunAsync();
    }
}
