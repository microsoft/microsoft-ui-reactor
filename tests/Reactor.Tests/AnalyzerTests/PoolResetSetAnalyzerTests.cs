using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="PoolResetSetAnalyzer"/> (<c>REACTOR_POOL_001</c>) and its
/// <see cref="PoolResetSetCodeFix"/>. Stubs a minimal Reactor-shaped fluent
/// element so the analyzer's syntactic match against <c>.Set(fe =&gt; fe.PROP = ...)</c>
/// fires without pulling the framework in.
/// </summary>
public class PoolResetSetAnalyzerTests
{
    // Mirrors the real Reactor shape: FakeElement carries the raw FE properties
    // that .Set writes to, and the modifiers (MaxHeight/Margin/HorizontalAlignment/...)
    // are extension methods — same as ElementExtensions.cs in src/Reactor.
    private const string Stubs = @"
using System;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Xaml
{
    public enum HorizontalAlignment { Left, Center, Right, Stretch }
    public enum VerticalAlignment { Top, Center, Bottom, Stretch }
    public struct Thickness { public Thickness(double u) {} }
}

public class FakeElement
{
    public double MaxHeight;
    public double MinHeight;
    public double MaxWidth;
    public double MinWidth;
    public double Width;
    public double Height;
    public double Opacity;
    public Thickness Margin;
    public HorizontalAlignment HorizontalAlignment;
    public VerticalAlignment VerticalAlignment;

    // Unrelated property — should never trigger.
    public string Text = string.Empty;

    public FakeElement Set(Action<FakeElement> configure) { configure(this); return this; }
    public FakeElement Apply(Action<FakeElement> configure) { configure(this); return this; }
}

public static class FakeElementExtensions
{
    public static FakeElement MaxHeight(this FakeElement el, double v) => el;
    public static FakeElement MinHeight(this FakeElement el, double v) => el;
    public static FakeElement MaxWidth(this FakeElement el, double v) => el;
    public static FakeElement MinWidth(this FakeElement el, double v) => el;
    public static FakeElement Width(this FakeElement el, double v) => el;
    public static FakeElement Height(this FakeElement el, double v) => el;
    public static FakeElement Opacity(this FakeElement el, double v) => el;
    public static FakeElement Margin(this FakeElement el, double u) => el;
    public static FakeElement HorizontalAlignment(this FakeElement el, HorizontalAlignment a) => el;
    public static FakeElement VerticalAlignment(this FakeElement el, VerticalAlignment a) => el;
}
";

    [Fact]
    public async Task Fires_For_MaxHeight()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.Set(fe => fe.MaxHeight = 260)|};
    }
}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task Fires_For_HorizontalAlignment()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.Set(fe => fe.HorizontalAlignment = HorizontalAlignment.Center)|};
    }
}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task Fires_With_Parenthesized_Lambda()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.Set((fe) => fe.MinWidth = 100)|};
    }
}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task No_Diagnostic_For_Untrapped_Property()
    {
        // .Text is not in ElementPool.CleanElement's FE-prop reset list and has
        // no equivalent modifier — .Set is legitimate here.
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.Set(fe => fe.Text = ""hi"");
    }
}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task No_Diagnostic_For_Non_Set_Method()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.Apply(fe => fe.MaxHeight = 260);
    }
}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task CodeFix_Rewrites_MaxHeight()
    {
        var before = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.Set(fe => fe.MaxHeight = 260)|};
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.MaxHeight(260);
    }
}";

        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync();
    }

    [Fact]
    public async Task CodeFix_Rewrites_HorizontalAlignment()
    {
        var before = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.Set(fe => fe.HorizontalAlignment = HorizontalAlignment.Center)|};
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.HorizontalAlignment(HorizontalAlignment.Center);
    }
}";

        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync();
    }
}
