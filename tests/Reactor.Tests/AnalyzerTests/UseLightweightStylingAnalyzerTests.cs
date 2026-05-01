using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Unit tests for REACTOR_THEME_002: Set brush → lightweight styling analyzer.
/// </summary>
public class UseLightweightStylingAnalyzerTests
{
    private const string Preamble = @"
using System;
class FakeElement
{
    public object Background;
    public object Foreground;
    public int Width;
    public FakeElement Set(Action<FakeElement> configure) { configure(this); return this; }
    public FakeElement Apply(Action<FakeElement> configure) { configure(this); return this; }
}
";

    [Fact]
    public async Task No_Diagnostic_For_Unknown_Control_Type()
    {
        // FakeElement is not a known control type (Button, etc.),
        // so the analyzer should NOT report a diagnostic.
        var test = Preamble + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.Set(b => b.Background = null);
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<UseLightweightStylingAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task No_Diagnostic_For_Non_Brush_Property()
    {
        var test = Preamble + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.Set(b => b.Width = 100);
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<UseLightweightStylingAnalyzer, DefaultVerifier>
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
        el.Apply(b => b.Background = null);
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<UseLightweightStylingAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task No_Diagnostic_For_Multiple_Arguments()
    {
        // The Set() method only has one parameter, but test with a different type
        var test = @"
using System;
class FakeElement2
{
    public object Background;
    public FakeElement2 Set(Action<FakeElement2> configure, string extra) { configure(this); return this; }
}
class C
{
    void M()
    {
        var el = new FakeElement2();
        el.Set(b => b.Background = null, ""extra"");
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<UseLightweightStylingAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }
}
