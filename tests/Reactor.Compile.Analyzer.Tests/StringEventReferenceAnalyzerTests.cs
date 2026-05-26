using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.UI.Reactor.Compile.Analyzer;
using Xunit;

namespace Microsoft.UI.Reactor.Compile.Analyzer.Tests;

/// <summary>
/// Phase 1 fixture for REACTOR1001. The rule body is a documented no-op
/// until the Phase 2 descriptor model lands (see
/// <see cref="StringEventReferenceAnalyzer"/> XML doc). We assert here
/// only that:
/// <list type="bullet">
///   <item>the analyzer runs cleanly against a representative Phase 1
///         source (no false positives);</item>
///   <item>the diagnostic descriptor is registered and discoverable.</item>
/// </list>
/// The "should fail" fixture lands when REACTOR1001 activates in Phase 2.
/// </summary>
public class StringEventReferenceAnalyzerTests
{
    [Fact]
    public async Task NoOp_Today_Does_Not_Fire_On_Clean_Source()
    {
        // Representative Phase 1 source — a plain invocation. Even with
        // a string literal that *looks* event-shaped, the rule must not
        // fire today.
        const string source = @"
public static class C
{
    public static void M()
    {
        var s = ""Toggled"";
        System.Console.WriteLine(s);
    }
}
";

        await new CSharpAnalyzerTest<StringEventReferenceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public void DiagnosticDescriptor_Is_Registered()
    {
        var analyzer = new StringEventReferenceAnalyzer();
        var descriptors = analyzer.SupportedDiagnostics;
        Assert.Single(descriptors);
        Assert.Equal("REACTOR1001", descriptors[0].Id);
    }
}
