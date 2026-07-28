using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.UI.Reactor.Analyzers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for REACTOR_DYM_001 (<see cref="NonInvocableMemberParensAnalyzer"/> +
/// <see cref="NonInvocableMemberParensCodeFix"/>): a Reactor property/field invoked like a
/// method, e.g. <c>GridSize.Auto()</c>. Compiler-diagnostic verification is turned off because
/// the analyzer only ever fires alongside the compiler's own CS1955; the tests assert only the
/// Reactor diagnostic and the fix round-trip.
/// </summary>
public class NonInvocableMemberParensAnalyzerTests
{
    // Reactor-shaped surface mirroring the real GridSize: Auto is a PROPERTY; Star/Px are METHODS.
    // `Other.Widget.Thing` is a property in a NON-Reactor namespace (the gating negative).
    private const string Stubs = @"
namespace Microsoft.UI.Reactor
{
    public readonly struct GridSize
    {
        public static GridSize Auto { get { return default; } }
        public static readonly GridSize Fill;   // a static FIELD (invoked -> 'field' wording)
        public static GridSize Star(double weight = 1) { return default; }
        public static GridSize Px(double pixels) { return default; }
    }

    public sealed class Dimension
    {
        public int Value { get { return 0; } }   // an instance PROPERTY
    }

    public class BaseWidget
    {
        public int Inherited { get { return 0; } }   // declared on the BASE type
    }

    public sealed class DerivedWidget : BaseWidget { }
}
namespace Other
{
    public static class Widget
    {
        public static int Thing { get { return 0; } }
    }
}
";

    private static CSharpAnalyzerTest<NonInvocableMemberParensAnalyzer, DefaultVerifier> MakeAnalyzerTest(string body)
    {
        var test = new CSharpAnalyzerTest<NonInvocableMemberParensAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + body,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.DisabledDiagnostics.Add("CS1591");
        return test;
    }

    [Fact]
    public async Task Flags_Property_Invoked_Like_Method()
    {
        var body = @"
namespace App
{
    using Microsoft.UI.Reactor;
    static class C { static object M() => {|REACTOR_DYM_001:GridSize.Auto()|}; }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Valid_Method_Or_Property()
    {
        var body = @"
namespace App
{
    using Microsoft.UI.Reactor;
    static class C
    {
        static object M1() => GridSize.Star();   // Star is a method — valid
        static object M2() => GridSize.Px(1);    // method with an arg — valid
        static GridSize M3() => GridSize.Auto;   // property, no parens — valid
    }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Non_Reactor_Type()
    {
        // Other.Widget.Thing() is also CS1955, but the receiver is not under Microsoft.UI.Reactor.
        var body = @"
namespace App
{
    using Other;
    static class C { static object M() => Widget.Thing(); }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Flags_Instance_Property_Invoked_Like_Method()
    {
        // Exercises the instance-receiver branch (GetTypeInfo) rather than static type access.
        var body = @"
namespace App
{
    using Microsoft.UI.Reactor;
    static class C { static object M(Dimension d) => {|REACTOR_DYM_001:d.Value()|}; }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Flags_Field_Invoked_Like_Method()
    {
        // The member-kind branch reports the field wording (vs property) for a field.
        var body = @"
namespace App
{
    using Microsoft.UI.Reactor;
    static class C { static object M() => {|REACTOR_DYM_001:GridSize.Fill()|}; }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Flags_Inherited_Property_Invoked_Like_Method()
    {
        // Member lookup walks the base-type chain: Inherited is declared on BaseWidget, not DerivedWidget.
        var body = @"
namespace App
{
    using Microsoft.UI.Reactor;
    static class C { static object M(DerivedWidget w) => {|REACTOR_DYM_001:w.Inherited()|}; }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Flags_Generic_Name_Invocation()
    {
        // Type arguments on a property (GridSize.Auto<int>()) are still just a property invoked
        // like a method; the diagnostic fires and the fix drops the type args too (see CodeFix test).
        var body = @"
namespace App
{
    using Microsoft.UI.Reactor;
    static class C { static object M() => {|REACTOR_DYM_001:GridSize.Auto<int>()|}; }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Removes_Parentheses()
    {
        var before = Stubs + @"
namespace App
{
    using Microsoft.UI.Reactor;
    static class C { static object M() => {|REACTOR_DYM_001:GridSize.Auto()|}; }
}";
        var after = Stubs + @"
namespace App
{
    using Microsoft.UI.Reactor;
    static class C { static object M() => GridSize.Auto; }
}";
        var test = new CSharpCodeFixTest<NonInvocableMemberParensAnalyzer, NonInvocableMemberParensCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.DisabledDiagnostics.Add("CS1591");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Removes_Parentheses_And_Type_Arguments()
    {
        // Reviewer scenario: GridSize.Auto<int>() must become GridSize.Auto (not GridSize.Auto<int>),
        // since a property/field can't carry type arguments.
        var before = Stubs + @"
namespace App
{
    using Microsoft.UI.Reactor;
    static class C { static object M() => {|REACTOR_DYM_001:GridSize.Auto<int>()|}; }
}";
        var after = Stubs + @"
namespace App
{
    using Microsoft.UI.Reactor;
    static class C { static object M() => GridSize.Auto; }
}";
        var test = new CSharpCodeFixTest<NonInvocableMemberParensAnalyzer, NonInvocableMemberParensCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.DisabledDiagnostics.Add("CS1591");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
