using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.UI.Reactor.Analyzers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

using AnalyzerVerifier = CSharpAnalyzerVerifier<FuzzyFactoryNameAnalyzer, DefaultVerifier>;

/// <summary>
/// Tests for REACTOR_DYM_003 (<see cref="FuzzyFactoryNameAnalyzer"/> +
/// <see cref="FuzzyFactoryNameCodeFix"/>): a mistyped Reactor factory name in call position, e.g.
/// <c>Buton("x")</c> for <c>Button("x")</c>. This is the first <b>fuzzy</b> did-you-mean analyzer, so
/// the bulk of these tests are <b>negatives</b> that pin the false-positive gating — CS0103 fires on
/// any unknown name, and the analyzer must suggest a factory for <i>none</i> of the non-factory ones.
/// Compiler-diagnostic verification is off because the analyzer only ever fires alongside the
/// compiler's own CS0103.
/// </summary>
public class FuzzyFactoryNameAnalyzerTests
{
    // Reactor-shaped surface: a `Microsoft.UI.Reactor.Factories` static class whose public static
    // methods are the live factory set the analyzer enumerates. Includes the HStack/VStack pair so
    // the `Stack` ambiguity (tie-guard) can be exercised, and a spread of lengths so the length-delta
    // gate can be exercised (Text vs TextBlock).
    private const string Stubs = @"
namespace Microsoft.UI.Reactor
{
    public sealed class El { }

    public static class Factories
    {
        public static El Button(string label) { return new El(); }
        public static El TextBlock(string content) { return new El(); }
        public static El Heading(string content) { return new El(); }
        public static El VStack(params El[] children) { return new El(); }
        public static El HStack(params El[] children) { return new El(); }
        public static El CheckBox() { return new El(); }
        public static El Border(El child) { return new El(); }
        public static El ComboBox(string[] items) { return new El(); }
        public static El NumberBox() { return new El(); }
        public static El Slider() { return new El(); }
    }
}
";

    private static CSharpAnalyzerTest<FuzzyFactoryNameAnalyzer, DefaultVerifier> MakeAnalyzerTest(string body)
    {
        var test = new CSharpAnalyzerTest<FuzzyFactoryNameAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + body,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.DisabledDiagnostics.Add("CS1591");
        return test;
    }

    // ── Positives ───────────────────────────────────────────────────

    [Fact]
    public async Task Flags_Misspelled_Factory_Buton()
    {
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => {|REACTOR_DYM_003:Buton|}(""x""); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Flags_Wrong_Case_Factory_Vstack()
    {
        // 'Vstack' keeps the leading uppercase (passes the PascalCase gate) but lower-cases the 'S'.
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => {|REACTOR_DYM_003:Vstack|}(); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Flags_Misspelled_Factory_NumbrBox()
    {
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => {|REACTOR_DYM_003:NumbrBox|}(); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Flags_Misspelled_Factory_Nested_In_Valid_Factory()
    {
        // Realistic factory-call context: the inner typo is an argument to a real factory call. Only
        // the inner `Buton` should be flagged (the outer VStack is a real factory, exact-match excluded).
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => VStack({|REACTOR_DYM_003:Buton|}(""x"")); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Reports_Name_And_Suggestion_As_Message_Arguments()
    {
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => {|#0:Buton|}(""x""); }
}";
        var test = MakeAnalyzerTest(body);
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier.Diagnostic(FuzzyFactoryNameAnalyzer.DiagnosticId)
                .WithLocation(0)
                .WithArguments("Buton", "Button"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Negatives (false-positive gating) ───────────────────────────

    [Fact]
    public async Task Does_Not_Flag_Valid_Factory_Calls()
    {
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C
    {
        static object A() => Button(""x"");
        static object B() => VStack();
        static object D() => TextBlock(""x"");
    }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_CamelCase_Unbound_Name()
    {
        // 'buton' is very close to Button, but a camelCase unbound name is almost always a typo'd
        // local/parameter, not a factory. The PascalCase gate must suppress it.
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => buton(""x""); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Unrelated_Unknown_Name()
    {
        // A PascalCase unknown name with no close factory (below the similarity threshold).
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => Widget(""x""); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Exact_Factory_Name_Without_Using_Static()
    {
        // 'Button' is unbound here (no `using static`), so this is CS0103 — but the name IS a factory,
        // so the fix is a missing import, not a rename. The analyzer must stay silent.
        var body = @"
namespace App
{
    static class C { static object M() => Button(""x""); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Bound_NonFactory_Method()
    {
        // 'Bordr' is close to Border, but it binds to a real local method — Symbol != null, so this
        // is not the CS0103 shape and must not be flagged.
        var body = @"
namespace App
{
    static class C
    {
        static object Bordr() { return new object(); }
        static object M() => Bordr();
    }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Member_Access_Typo()
    {
        // Member-access typo (x.Buton()) is a different shape (CS1061 phase), not a bare factory call.
        var body = @"
namespace App
{
    static class C { static object M() => """".Buton(); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Short_Prefix_Of_Long_Factory()
    {
        // 'Text' shares a 4-char prefix with TextBlock and scores high on raw Jaro-Winkler, but the
        // length-delta gate (|4 - 9| = 5 > 2) rejects it — the key defence against prefix inflation.
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => Text(""x""); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Ambiguous_Tie()
    {
        // 'Stack' is exactly equidistant from HStack and VStack — a genuine tie the tie-guard refuses
        // to guess at.
        var body = @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => Stack(); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_When_Compilation_Has_No_Reactor()
    {
        // No `Microsoft.UI.Reactor.Factories` type in the compilation — the analyzer never fires.
        var test = new CSharpAnalyzerTest<FuzzyFactoryNameAnalyzer, DefaultVerifier>
        {
            TestCode = @"
namespace App
{
    static class C { static object M() => Buton(""x""); }
}",
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.DisabledDiagnostics.Add("CS1591");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Code fix ────────────────────────────────────────────────────

    [Fact]
    public async Task CodeFix_Renames_To_Suggested_Factory()
    {
        var before = Stubs + @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => {|REACTOR_DYM_003:Buton|}(""x""); }
}";
        var after = Stubs + @"
namespace App
{
    using static Microsoft.UI.Reactor.Factories;
    static class C { static object M() => Button(""x""); }
}";
        var test = new CSharpCodeFixTest<FuzzyFactoryNameAnalyzer, FuzzyFactoryNameCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.DisabledDiagnostics.Add("CS1591");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
