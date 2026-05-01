using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

using AnalyzerVerifier = CSharpAnalyzerVerifier<RequestedThemeSetAnalyzer, DefaultVerifier>;

/// <summary>
/// Unit tests for REACTOR_THEME_003: RequestedTheme Set → modifier analyzer.
/// </summary>
public class RequestedThemeSetAnalyzerTests
{
    private const string Preamble = @"
using System;
class FakeElement
{
    public int RequestedTheme;
    public int Width;
    public FakeElement Set(Action<FakeElement> configure) { configure(this); return this; }
    public FakeElement Apply(Action<FakeElement> configure) { configure(this); return this; }
}
";

    [Fact]
    public async Task Detects_SimpleLambda_Set_RequestedTheme()
    {
        var test = Preamble + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.Set(fe => fe.RequestedTheme = 1);
    }
}";
        var expected = AnalyzerVerifier.Diagnostic(RequestedThemeSetAnalyzer.DiagnosticId)
            .WithSpan(16, 9, 16, 44)
            .WithArguments("1");

        var analyzerTest = new CSharpAnalyzerTest<RequestedThemeSetAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Detects_ParenthesizedLambda_Set_RequestedTheme()
    {
        var test = Preamble + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.Set((fe) => fe.RequestedTheme = 2);
    }
}";
        var expected = AnalyzerVerifier.Diagnostic(RequestedThemeSetAnalyzer.DiagnosticId)
            .WithSpan(16, 9, 16, 46)
            .WithArguments("2");

        var analyzerTest = new CSharpAnalyzerTest<RequestedThemeSetAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task No_Diagnostic_For_Other_Properties()
    {
        var test = Preamble + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.Set(fe => fe.Width = 100);
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<RequestedThemeSetAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task No_Diagnostic_For_Non_Set_Method()
    {
        var test = Preamble + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.Apply(fe => fe.RequestedTheme = 1);
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<RequestedThemeSetAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }
}
