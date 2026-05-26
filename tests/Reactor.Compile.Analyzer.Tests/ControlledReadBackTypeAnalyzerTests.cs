using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.UI.Reactor.Compile.Analyzer;
using Xunit;

namespace Microsoft.UI.Reactor.Compile.Analyzer.Tests;

/// <summary>
/// Phase 1 fixture for REACTOR1003. The rule body is a documented no-op
/// until the Phase 2 descriptor model lands (see
/// <see cref="ControlledReadBackTypeAnalyzer"/> XML doc). The "should
/// fail" fixture lands when REACTOR1003 activates in Phase 2.
/// </summary>
public class ControlledReadBackTypeAnalyzerTests
{
    [Fact]
    public async Task NoOp_Today_Does_Not_Fire_On_Clean_Source()
    {
        const string source = @"
public static class C
{
    public static int M(int x) => x + 1;
}
";

        await new CSharpAnalyzerTest<ControlledReadBackTypeAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public void DiagnosticDescriptor_Is_Registered()
    {
        var analyzer = new ControlledReadBackTypeAnalyzer();
        var descriptors = analyzer.SupportedDiagnostics;
        Assert.Single(descriptors);
        Assert.Equal("REACTOR1003", descriptors[0].Id);
    }
}
