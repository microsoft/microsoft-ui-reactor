using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.UI.Reactor.Analyzers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

using AnalyzerVerifier = CSharpAnalyzerVerifier<StringForElementArgumentAnalyzer, DefaultVerifier>;

/// <summary>
/// Tests for REACTOR_DYM_005 (<see cref="StringForElementArgumentAnalyzer"/>): a <c>string</c> passed
/// where a Reactor <c>Element</c> is expected (compiler CS1503). This mirrors the one narrow CS1503
/// special case the CLI's <c>SymbolSuggester</c> encodes; the negatives pin the deliberately tight
/// scope — no general type mismatches, multi-overload factories, params calls, cascading errors,
/// non-Reactor look-alikes, named-argument calls, and (documented) no <c>Action&lt;T&gt;</c>-vs-<c>Action</c>.
/// Compiler-diagnostic verification is off because the analyzer only fires alongside the compiler's CS1503.
/// </summary>
public class StringForElementArgumentAnalyzerTests
{
    private const string Stubs = @"
using System;
namespace Microsoft.UI.Reactor.Core
{
    public abstract record Element;
    public sealed record SpecialElement : Element;
}
namespace Microsoft.UI.Reactor
{
    using Microsoft.UI.Reactor.Core;
    using System;

    public static class Factories
    {
        public static Element ScrollViewer(Element child) => null!;
        public static Element Border(Element? child) => null!;
        public static Element TextBlock(string content) => null!;
        public static Element Button(string label, Action? onClick = null) => null!;
        public static Element Button(Element content, Action? onClick = null) => null!;
        public static Element VStack(params Element[] children) => null!;
        public static Element Grid(int rows, int cols) => null!;
        public static Element OnTapped(Action handler) => null!;
        public static Element SpecialHost(SpecialElement child) => null!;
        public static Element Panel2(Element a, Element b) => null!;
    }

    public static class Ext
    {
        public static Element Margin(this Element e, int all) => e;
    }
}
namespace Other
{
    public class Widget { }
    public static class Factories
    {
        public static Widget Panel(Widget child) => null!;
    }
}
";

    private static CSharpAnalyzerTest<StringForElementArgumentAnalyzer, DefaultVerifier> MakeAnalyzerTest(string body)
    {
        var test = new CSharpAnalyzerTest<StringForElementArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + body,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.DisabledDiagnostics.Add("CS1591");
        return test;
    }

    // ── Positives ───────────────────────────────────────────────────

    [Fact]
    public async Task Flags_String_For_Element_ScrollViewer()
    {
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => ScrollViewer({|REACTOR_DYM_005:""x""|}); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Flags_String_For_Nullable_Element_Border()
    {
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => Border({|REACTOR_DYM_005:""x""|}); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Flags_String_For_Element_Subtype()
    {
        // SpecialHost takes a SpecialElement (a subtype of Element); the base-type walk in IsElementType
        // must recognise it so the string-for-Element hint still fires.
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => SpecialHost({|REACTOR_DYM_005:""x""|}); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Reports_Factory_Name_As_Message_Argument()
    {
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => ScrollViewer({|#0:""x""|}); }
}";
        var test = MakeAnalyzerTest(body);
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier.Diagnostic(StringForElementArgumentAnalyzer.DiagnosticId)
                .WithLocation(0)
                .WithArguments("ScrollViewer"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Negatives (false-positive gating) ───────────────────────────

    [Fact]
    public async Task Does_Not_Flag_Valid_Calls()
    {
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C
    {
        static object A() => ScrollViewer(Border(null));
        static object B() => TextBlock(""x"");
        static object D() => Button(""x"");
    }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Multi_Overload_Factory()
    {
        // Button(123) fails every overload — several candidates, no unique best — so we stay silent.
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => Button(123); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_NonString_Type_Mismatch()
    {
        // Grid(1, "y"): exactly one failing argument, but its parameter is int, not Element — this is
        // not the string-for-element shape.
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => Grid(1, ""y""); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Multiple_Failing_Arguments()
    {
        // Grid("x", "y"): two failing arguments — never the clean single-string-arg shape.
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => Grid(""x"", ""y""); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Surplus_Arguments()
    {
        // ScrollViewer("x", 1): more arguments than parameters (CS1501) — a different error shape, not
        // a single string-for-Element mismatch.
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => ScrollViewer(""x"", 1); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_When_An_Untyped_Argument_Also_Fails()
    {
        // Panel2(() => {}, "x"): the string fails against Element, but the lambda also fails against the
        // first Element parameter — two failing arguments, so the single-string-arg gate must not fire.
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => Panel2(() => {}, ""x""); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Params_Call()
    {
        // VStack(1): a params tail makes positional arg->parameter mapping unsafe, so we bail.
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => VStack(1); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Cascading_Error_Argument()
    {
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => ScrollViewer(Undefined()); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_NonReactor_Factory()
    {
        var body = @"
namespace App
{
    static class C { static object M() => Other.Factories.Panel(""x""); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Reactor_Extension_Method()
    {
        // Ext.Margin(this Element, int) is a Reactor-namespace method but NOT on Factories.
        // `using Microsoft.UI.Reactor;` brings the extension into scope so the failing call resolves to
        // Ext.Margin (OverloadResolutionFailure), exercising the Factories-type gate rather than a
        // "no such member" (CandidateReason.None) bail.
        var body = @"
namespace App
{
    using Microsoft.UI.Reactor;
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => Border(null).Margin(""x""); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Named_Argument_Call()
    {
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => ScrollViewer(child: ""x""); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_ActionOfT_For_Action()
    {
        // Documented deliberate skip (spec 061 §7): Action<int> supplied where Action is expected is a
        // real CS1503, but the parameter is not an Element, so REACTOR_DYM_005 stays silent. The common
        // lambda flavour of this mistake has no argument type to classify at all.
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C
    {
        static System.Action<int> Handler = _ => {};
        static object M() => OnTapped(Handler);
    }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_When_Compilation_Has_No_Reactor()
    {
        var test = new CSharpAnalyzerTest<StringForElementArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = @"
namespace App
{
    static class C { static object M() => ScrollViewer(""x""); }
}",
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.DisabledDiagnostics.Add("CS1591");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
