using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.UI.Reactor.Analyzers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

using AnalyzerVerifier = CSharpAnalyzerVerifier<MissingFactoryArgumentAnalyzer, DefaultVerifier>;

/// <summary>
/// Tests for REACTOR_DYM_004 (<see cref="MissingFactoryArgumentAnalyzer"/>): a Reactor factory called
/// with too few arguments (compiler CS7036), where a single unique overload lets us suggest the full
/// parameter shape. As with the other did-you-mean analyzers, the bulk of these are <b>negatives</b>
/// that pin the false-positive gating proven clean by the spec 061 §7 spike — the analyzer must stay
/// silent on multi-overload factories, params calls, cascading errors, non-Reactor look-alikes, and
/// calls that are simultaneously short an argument and type-mismatched. Compiler-diagnostic
/// verification is off because the analyzer only ever fires alongside the compiler's own CS7036.
/// </summary>
public class MissingFactoryArgumentAnalyzerTests
{
    // Reactor-shaped surface: single-overload factories with required args (ScrollViewer/Border/Grid),
    // a factory with trailing optionals (HyperlinkButton), a multi-overload factory (Button, to prove
    // the unique-candidate gate), a params factory (VStack), a Reactor extension modifier (Ext.Margin,
    // to prove the Factories-type gate), and a non-Reactor look-alike (Other.Factories).
    private const string Stubs = @"
using System;
namespace Microsoft.UI.Reactor.Core
{
    public abstract record Element;
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
        public static Element HyperlinkButton(string content, Uri? navigateUri = null, Action? onClick = null) => null!;
        public static Element Button(string label, Action? onClick = null) => null!;
        public static Element Button(Element content, Action? onClick = null) => null!;
        public static Element VStack(params Element[] children) => null!;
        public static Element Grid(int rows, int cols) => null!;
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

    private static CSharpAnalyzerTest<MissingFactoryArgumentAnalyzer, DefaultVerifier> MakeAnalyzerTest(string body)
    {
        var test = new CSharpAnalyzerTest<MissingFactoryArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + body,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.DisabledDiagnostics.Add("CS1591");
        return test;
    }

    // ── Positives ───────────────────────────────────────────────────

    [Fact]
    public async Task Flags_ScrollViewer_Missing_Child()
    {
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => {|REACTOR_DYM_004:ScrollViewer|}(); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Flags_Grid_Too_Few_Arguments()
    {
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => {|REACTOR_DYM_004:Grid|}(1); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Flags_Factory_With_Trailing_Optionals()
    {
        // Only `content` is required; the analyzer still fires (provided 0 < required 1).
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => {|REACTOR_DYM_004:HyperlinkButton|}(); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Flags_Qualified_Member_Access_Call()
    {
        // No `using static`: the callee is `Factories.ScrollViewer` (member access). The analyzer works
        // off the resolved candidate symbol, so member-access calls are covered and the squiggle lands
        // on the method name.
        var body = @"
namespace App
{
    static class C { static object M() => Microsoft.UI.Reactor.Factories.{|REACTOR_DYM_004:ScrollViewer|}(); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Reports_Name_And_Parameter_Shape_As_Message_Arguments()
    {
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => {|#0:ScrollViewer|}(); }
}";
        var test = MakeAnalyzerTest(body);
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier.Diagnostic(MissingFactoryArgumentAnalyzer.DiagnosticId)
                .WithLocation(0)
                .WithArguments("ScrollViewer", "child: <Element>"));
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
        static object B() => Grid(1, 2);
        static object D() => HyperlinkButton(""x"");
    }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Multi_Overload_Factory()
    {
        // Button() leaves several candidates (no unique best overload), so there is no single shape to
        // suggest — the unique-candidate gate keeps it silent even though it is genuinely CS7036.
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => Button(); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Empty_Params_Call()
    {
        // VStack() binds fine (params can be empty) — Symbol is non-null, so no diagnostic.
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => VStack(); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Cascading_Error_Argument()
    {
        // Grid(Undefined()) is short an argument AND the supplied one is an error type — an
        // edit-in-progress cascade whose overload resolution can't be trusted.
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => Grid(Undefined()); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Too_Few_With_Type_Mismatch()
    {
        // Grid("x") is both short an argument and type-mismatched (string, not int). The
        // all-provided-args-must-convert guard keeps it silent — "missing argument" is the wrong hint.
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => Grid(""x""); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_NonReactor_Factory()
    {
        var body = @"
namespace App
{
    static class C { static object M() => Other.Factories.Panel(); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Reactor_Extension_Method()
    {
        // Ext.Margin(this Element, int) is a Reactor-namespace method but NOT on Factories — the
        // Factories-type gate excludes fluent modifiers.
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => Border(null).Margin(); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Named_Argument_Call()
    {
        // Grid(rows: 1) is short an argument, but a named argument is a different reasoning problem —
        // the analyzer only reasons about positional calls.
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => Grid(rows: 1); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_When_Compilation_Has_No_Reactor()
    {
        var test = new CSharpAnalyzerTest<MissingFactoryArgumentAnalyzer, DefaultVerifier>
        {
            TestCode = @"
namespace App
{
    static class C { static object M() => ScrollViewer(); }
}",
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.DisabledDiagnostics.Add("CS1591");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
