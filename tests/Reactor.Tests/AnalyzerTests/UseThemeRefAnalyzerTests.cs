using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

using AnalyzerVerifier = CSharpAnalyzerVerifier<UseThemeRefAnalyzer, DefaultVerifier>;

/// <summary>
/// Unit tests for REACTOR_THEME_001: hard-coded color → ThemeRef analyzer.
/// </summary>
public class UseThemeRefAnalyzerTests
{
    [Fact]
    public async Task Detects_Background_With_Known_Color()
    {
        var test = @"
class C
{
    void M(dynamic el)
    {
        el.Background(""#FFFFFF"");
    }
}";
        var expected = AnalyzerVerifier.Diagnostic(UseThemeRefAnalyzer.DiagnosticId)
            .WithSpan(6, 23, 6, 32)
            .WithArguments("PrimaryBackground", "#FFFFFF");

        var analyzerTest = new CSharpAnalyzerTest<UseThemeRefAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Detects_Foreground_With_Known_Color()
    {
        var test = @"
class C
{
    void M(dynamic el)
    {
        el.Foreground(""black"");
    }
}";
        var expected = AnalyzerVerifier.Diagnostic(UseThemeRefAnalyzer.DiagnosticId)
            .WithSpan(6, 23, 6, 30)
            .WithArguments("PrimaryText", "black");

        var analyzerTest = new CSharpAnalyzerTest<UseThemeRefAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Detects_WithBorder_With_Known_Color()
    {
        var test = @"
class C
{
    void M(dynamic el)
    {
        el.WithBorder(""#0078D4"");
    }
}";
        var expected = AnalyzerVerifier.Diagnostic(UseThemeRefAnalyzer.DiagnosticId)
            .WithSpan(6, 23, 6, 32)
            .WithArguments("Accent", "#0078D4");

        var analyzerTest = new CSharpAnalyzerTest<UseThemeRefAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Detects_Unknown_Color_With_Generic_Message()
    {
        var test = @"
class C
{
    void M(dynamic el)
    {
        el.Background(""#AABBCC"");
    }
}";
        var expected = AnalyzerVerifier.Diagnostic(UseThemeRefAnalyzer.DiagnosticId)
            .WithSpan(6, 23, 6, 32)
            .WithArguments("Accent or another semantic token", "#AABBCC");

        var analyzerTest = new CSharpAnalyzerTest<UseThemeRefAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task No_Diagnostic_For_Non_Target_Method()
    {
        var test = @"
class C
{
    void M(dynamic el)
    {
        el.SomeOtherMethod(""#FFFFFF"");
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<UseThemeRefAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task No_Diagnostic_For_Non_String_Argument()
    {
        var test = @"
class C
{
    void M(dynamic el, dynamic brush)
    {
        el.Background(brush);
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<UseThemeRefAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }
}
