using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.UI.Reactor.Analyzers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for REACTOR_DYM_002 (<see cref="ThemeBackgroundSuffixAnalyzer"/> +
/// <see cref="ThemeBackgroundSuffixCodeFix"/>): an invented <c>Theme.*Background</c> token, e.g.
/// <c>Theme.AppBackground</c>. Compiler-diagnostic verification is turned off because the analyzer
/// only ever fires alongside the compiler's own CS0117; the tests assert only the Reactor diagnostic
/// and the fix round-trip.
/// </summary>
public class ThemeBackgroundSuffixAnalyzerTests
{
    // Reactor's Theme surface with the canonical token (SolidBackground), the override target
    // (LayerFill), a real *Background sibling (CardBackground — the binds-fine negative), plus a
    // non-Reactor look-alike Theme (the symbol-equality gating negative).
    private const string Stubs = @"
namespace Microsoft.UI.Reactor.Core
{
    public static class Theme
    {
        public static object SolidBackground => null!;
        public static object CardBackground => null!;
        public static object LayerFill => null!;
        public static object Accent => null!;
    }
}
namespace Acme.Branding
{
    public static class Theme
    {
        public static object Accent => null!;
        public static object CardBackground => null!;
    }
}
";

    private static CSharpAnalyzerTest<ThemeBackgroundSuffixAnalyzer, DefaultVerifier> MakeAnalyzerTest(string body)
    {
        var test = new CSharpAnalyzerTest<ThemeBackgroundSuffixAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + body,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.DisabledDiagnostics.Add("CS1591");
        return test;
    }

    [Fact]
    public async Task Flags_AppBackground()
    {
        var body = @"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    static class C { static object M() => Theme.{|REACTOR_DYM_002:AppBackground|}; }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Flags_WindowBackground()
    {
        var body = @"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    static class C { static object M() => Theme.{|REACTOR_DYM_002:WindowBackground|}; }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Flags_LayerBackground_Override()
    {
        // LayerBackground routes to the LayerFill override, not the SolidBackground fallback.
        var body = @"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    static class C { static object M() => Theme.{|REACTOR_DYM_002:LayerBackground|}; }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Existing_Background_Token()
    {
        // CardBackground is a real Theme member — the access binds, so nothing to suggest even though
        // the name ends in Background.
        var body = @"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    static class C { static object M() => Theme.CardBackground; }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Non_Background_Suffix()
    {
        // CS0117 on Theme, but the missing member doesn't end in Background — outside this rule's remit.
        var body = @"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    static class C { static object M() => Theme.AccentColor; }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_Lookalike_Theme_In_Other_Namespace()
    {
        // A non-Reactor `Theme` with the same missing-member shape must be ruled out by symbol equality.
        var body = @"
namespace App
{
    using Acme.Branding;
    static class C { static object M() => Theme.AppBackground; }
}";
        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Flag_When_Target_Missing_On_Theme()
    {
        // Self-disable: if the canonical target (SolidBackground) no longer exists on the live Theme
        // surface, the analyzer withholds rather than proposing a member that wouldn't compile.
        const string stubWithoutTarget = @"
namespace Microsoft.UI.Reactor.Core
{
    public static class Theme
    {
        public static object CardBackground => null!;
        public static object Accent => null!;
    }
}";
        var body = @"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    static class C { static object M() => Theme.AppBackground; }
}";
        var test = new CSharpAnalyzerTest<ThemeBackgroundSuffixAnalyzer, DefaultVerifier>
        {
            TestCode = stubWithoutTarget + body,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.DisabledDiagnostics.Add("CS1591");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Renames_AppBackground_To_SolidBackground()
    {
        var before = Stubs + @"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    static class C { static object M() => Theme.{|REACTOR_DYM_002:AppBackground|}; }
}";
        var after = Stubs + @"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    static class C { static object M() => Theme.SolidBackground; }
}";
        var test = new CSharpCodeFixTest<ThemeBackgroundSuffixAnalyzer, ThemeBackgroundSuffixCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.DisabledDiagnostics.Add("CS1591");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Renames_LayerBackground_To_LayerFill()
    {
        var before = Stubs + @"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    static class C { static object M() => Theme.{|REACTOR_DYM_002:LayerBackground|}; }
}";
        var after = Stubs + @"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    static class C { static object M() => Theme.LayerFill; }
}";
        var test = new CSharpCodeFixTest<ThemeBackgroundSuffixAnalyzer, ThemeBackgroundSuffixCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.DisabledDiagnostics.Add("CS1591");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
