using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="UnsizedWebViewInStackAnalyzer"/> (<c>REACTOR_MEDIA_001</c>).
/// Stubs a minimal Reactor-shaped DSL — the four auto-layout stack factories, the
/// <c>WebView2</c> factory, and the <c>.Width</c>/<c>.Height</c>/<c>.Size</c>/<c>.Set</c>
/// modifiers — so the analyzer's syntactic match fires without pulling the framework in.
/// The analyzer is purely syntactic, so the stub signatures only need to make the test
/// source compile.
/// </summary>
public class UnsizedWebViewInStackAnalyzerTests
{
    private const string Stubs = @"
using System;
using static Factories;

public abstract class Element { }
public sealed class WebView2Element : Element { }
public sealed class StackElement : Element { }
public sealed class FlexElement : Element { }
public sealed class BorderElement : Element { }
public sealed class ImageElement : Element { }

// Stand-in for the WinUI control surface a `.Set(w => w.Width = …)` lambda writes to.
public sealed class WebView2Control
{
    public double Width;
    public double Height;
    public string DefaultBackgroundColor = string.Empty;
    public WebView2Control Child;
}

public static class Factories
{
    public static WebView2Element WebView2() => new WebView2Element();
    public static StackElement HStack(params Element[] children) => new StackElement();
    public static StackElement HStack(double spacing, params Element[] children) => new StackElement();
    public static StackElement VStack(params Element[] children) => new StackElement();
    public static FlexElement FlexRow(params Element[] children) => new FlexElement();
    public static FlexElement FlexColumn(params Element[] children) => new FlexElement();
    public static BorderElement Border(Element child) => new BorderElement();
    public static ImageElement Image(string source) => new ImageElement();
    public static Func<Element> WebFactory() => () => new WebView2Element();
}

public static class ElementExtensions
{
    public static T Width<T>(this T el, double width) where T : Element => el;
    public static T Height<T>(this T el, double height) where T : Element => el;
    public static T Size<T>(this T el, double width, double height) where T : Element => el;
    public static T NavigationCompleted<T>(this T el, Action<Uri> handler) where T : Element => el;
    public static T Set<T>(this T el, Action<WebView2Control> configure) where T : Element => el;
}
";

    private static Task Verify(string body)
    {
        var source = Stubs + @"
class C
{
    void M()
    {
" + body + @"
    }
}";
        return new CSharpAnalyzerTest<UnsizedWebViewInStackAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Positive: unsized WebView2 in each of the four auto-layout stacks ──────

    [Fact]
    public Task Fires_For_HStack() =>
        Verify(@"        HStack({|REACTOR_MEDIA_001:WebView2()|});");

    [Fact]
    public Task Fires_For_VStack() =>
        Verify(@"        VStack({|REACTOR_MEDIA_001:WebView2()|});");

    [Fact]
    public Task Fires_For_FlexRow() =>
        Verify(@"        FlexRow({|REACTOR_MEDIA_001:WebView2()|});");

    [Fact]
    public Task Fires_For_FlexColumn() =>
        Verify(@"        FlexColumn({|REACTOR_MEDIA_001:WebView2()|});");

    // ── Positive: chain has non-size modifiers only → still fires ─────────────

    [Fact]
    public Task Fires_When_Chain_Has_Only_NonSize_Modifiers() =>
        Verify(@"        HStack({|REACTOR_MEDIA_001:WebView2()|}.NavigationCompleted(u => { }));");

    [Fact]
    public Task Fires_When_Set_Does_Not_Assign_Size() =>
        Verify(@"        HStack({|REACTOR_MEDIA_001:WebView2()|}.Set(w => w.DefaultBackgroundColor = ""white""));");

    [Fact]
    public Task Fires_When_Set_Sizes_An_Unrelated_Receiver()
    {
        // The .Set lambda assigns Width, but to an unrelated object — not the
        // WebView2 parameter — so the WebView2 itself is still unsized and fires.
        return Verify(@"        var other = new WebView2Control();
        HStack({|REACTOR_MEDIA_001:WebView2()|}.Set(w => other.Width = 800));");
    }

    [Fact]
    public Task Fires_When_Set_Sizes_A_Nested_Child()
    {
        // Sizing a nested member (w.Child.Width) is not sizing the WebView2 itself —
        // the assignment receiver is a member-access, not the lambda parameter — so it fires.
        return Verify(@"        HStack({|REACTOR_MEDIA_001:WebView2()|}.Set(w => w.Child.Width = 800));");
    }

    // ── Positive: spacing overload + sibling children (child is still direct) ─

    [Fact]
    public Task Fires_In_Spacing_Overload() =>
        Verify(@"        HStack(8, {|REACTOR_MEDIA_001:WebView2()|});");

    [Fact]
    public Task Fires_For_WebView_Sibling_Among_Children() =>
        Verify(@"        VStack(Image(""a""), {|REACTOR_MEDIA_001:WebView2()|});");

    // ── Positive: qualified factory calls (Factories.HStack / Factories.WebView2) ─

    [Fact]
    public Task Fires_For_Qualified_Factory_Calls() =>
        Verify(@"        Factories.HStack({|REACTOR_MEDIA_001:Factories.WebView2()|});");

    // ── Negative: any pinned size suppresses ─────────────────────────────────

    [Fact]
    public Task No_Diagnostic_When_Width_And_Height_Set() =>
        Verify(@"        HStack(WebView2().Width(800).Height(600));");

    [Fact]
    public Task No_Diagnostic_When_Size_Set() =>
        Verify(@"        HStack(WebView2().Size(800, 600));");

    [Fact]
    public Task No_Diagnostic_When_Only_Width_Set() =>
        Verify(@"        HStack(WebView2().Width(800));");

    [Fact]
    public Task No_Diagnostic_When_Only_Height_Set() =>
        Verify(@"        HStack(WebView2().Height(600));");

    [Fact]
    public Task No_Diagnostic_When_Size_Set_Imperatively_Via_Set() =>
        Verify(@"        HStack(WebView2().Set(w => w.Width = 800));");

    [Fact]
    public Task No_Diagnostic_When_Size_Set_Via_BlockBody_Set() =>
        Verify(@"        HStack(WebView2().Set(w => { w.Width = 800; w.Height = 600; }));");

    [Fact]
    public Task No_Diagnostic_When_Size_Set_Via_Parenthesized_Set() =>
        Verify(@"        HStack(WebView2().Set((w) => w.Height = 600));");

    // ── Negative: not a direct child of a stack ──────────────────────────────

    [Fact]
    public Task No_Diagnostic_When_Not_In_A_Stack() =>
        Verify(@"        Border(WebView2());");

    [Fact]
    public Task No_Diagnostic_For_NonWebView_Child() =>
        Verify(@"        HStack(Image(""a""));");

    // ── Near-miss: WebView2 is lexically inside the stack call but not a direct,
    //    visible, inline child — the analyzer must stay quiet. ────────────────

    [Fact]
    public Task No_Diagnostic_When_Wrapped_In_Border() =>
        Verify(@"        VStack(Border(WebView2()));");

    [Fact]
    public Task No_Diagnostic_When_Wrapped_In_Sized_Border() =>
        Verify(@"        VStack(Border(WebView2()).Width(800).Height(600));");

    [Fact]
    public Task No_Diagnostic_For_Opaque_Variable_Child()
    {
        // The WebView2 is built into a variable, so its modifier chain is invisible
        // to a syntactic pass — bail rather than risk a false positive.
        return Verify(@"        var w = WebView2();
        HStack(w);");
    }

    [Fact]
    public Task No_Diagnostic_For_Conditional_Child()
    {
        // A conditional child is not the literal inline WebView2(...) shape the rule
        // matches — analysing through the branches is out of scope, so it stays quiet.
        return Verify(@"        HStack(true ? WebView2() : WebView2());");
    }

    [Fact]
    public Task No_Diagnostic_For_Exotic_Invocation_Child()
    {
        // An invocation whose callee is itself an invocation (a factory-returning-factory)
        // is neither an identifier nor a member-access call — the peel bails safely.
        return Verify(@"        HStack(WebFactory()());");
    }
}
