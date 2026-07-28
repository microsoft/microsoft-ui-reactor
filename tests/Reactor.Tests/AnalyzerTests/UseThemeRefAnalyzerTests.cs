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
            .WithArguments("SolidBackground", "#FFFFFF");

        var analyzerTest = new CSharpAnalyzerTest<UseThemeRefAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };
        await analyzerTest.RunAsync(TestContext.Current.CancellationToken);
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
        await analyzerTest.RunAsync(TestContext.Current.CancellationToken);
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
        await analyzerTest.RunAsync(TestContext.Current.CancellationToken);
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
            .WithArguments("Accent", "#AABBCC");

        var analyzerTest = new CSharpAnalyzerTest<UseThemeRefAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };
        await analyzerTest.RunAsync(TestContext.Current.CancellationToken);
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
        await analyzerTest.RunAsync(TestContext.Current.CancellationToken);
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
        await analyzerTest.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── REACTOR_THEME_004: inline SolidColorBrush escape hatch ──────────────

    // Minimal WinUI/Theme-shaped stubs so `new SolidColorBrush(Colors.X)` and `Theme.X` compile in
    // the analyzer/code-fix harness (which has no WindowsAppSDK reference). Mirrors the real shapes:
    // Colors.X : Windows.UI.Color, SolidColorBrush(Color) : Brush, Theme.X : ThemeRef.
    private const string Stubs = @"
namespace Windows.UI
{
    public struct Color { }
}
namespace Microsoft.UI
{
    public static class Colors
    {
        public static Windows.UI.Color White => default;
        public static Windows.UI.Color Black => default;
        public static Windows.UI.Color Red => default;
    }
}
namespace Microsoft.UI.Xaml.Media
{
    public class Brush { }
    public class SolidColorBrush : Brush
    {
        public SolidColorBrush() { }
        public SolidColorBrush(Windows.UI.Color color) { }
    }
}
namespace Microsoft.UI.Reactor.Core
{
    public readonly struct ThemeRef { }
    public static class Theme
    {
        public static ThemeRef SolidBackground => default;
        public static ThemeRef PrimaryText => default;
        public static ThemeRef Accent => default;
        public static ThemeRef CardBackground => default;
    }
}
";

    [Fact]
    public async Task Detects_Inline_SolidColorBrush_On_Background()
    {
        var test = Stubs + @"
namespace TestApp
{
    using Microsoft.UI;
    using Microsoft.UI.Xaml.Media;

    class C
    {
        void M(dynamic el)
        {
            el.Background({|REACTOR_THEME_004:new SolidColorBrush(Colors.Red)|});
        }
    }
}";
        await new CSharpAnalyzerTest<UseThemeRefAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Detects_Inline_SolidColorBrush_Suggests_Mapped_Token()
    {
        var test = Stubs + @"
namespace TestApp
{
    using Microsoft.UI;
    using Microsoft.UI.Xaml.Media;

    class C
    {
        void M(dynamic el)
        {
            el.Foreground({|#0:new SolidColorBrush(Colors.Black)|});
        }
    }
}";
        await new CSharpAnalyzerTest<UseThemeRefAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics =
            {
                AnalyzerVerifier.Diagnostic(UseThemeRefAnalyzer.BrushDiagnosticId)
                    .WithLocation(0)
                    .WithArguments("PrimaryText"),
            },
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Theme_Token_Argument()
    {
        // The declarative, theme-reactive form — must NOT fire.
        var test = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    class C
    {
        void M(dynamic el)
        {
            el.Background(Theme.CardBackground);
        }
    }
}";
        await new CSharpAnalyzerTest<UseThemeRefAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Field_Held_Brush()
    {
        // A brush read from a field may already hold a resolved token brush — inline-creation-only
        // detection keeps it safe.
        var test = Stubs + @"
namespace TestApp
{
    using Microsoft.UI;
    using Microsoft.UI.Xaml.Media;

    class C
    {
        static readonly SolidColorBrush _brush = new SolidColorBrush(Colors.Red);
        void M(dynamic el)
        {
            el.Background(_brush);
        }
    }
}";
        await new CSharpAnalyzerTest<UseThemeRefAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Non_Target_Modifier_With_Brush()
    {
        // Near-miss: a brush argument to a modifier outside the {Background,Foreground,WithBorder} gate.
        var test = Stubs + @"
namespace TestApp
{
    using Microsoft.UI;
    using Microsoft.UI.Xaml.Media;

    class C
    {
        void M(dynamic el)
        {
            el.Fill(new SolidColorBrush(Colors.Red));
        }
    }
}";
        await new CSharpAnalyzerTest<UseThemeRefAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fix_Rewrites_Mapped_White_To_Theme_SolidBackground()
    {
        var test = Stubs + @"
namespace TestApp
{
    using Microsoft.UI;
    using Microsoft.UI.Xaml.Media;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        void M(dynamic el)
        {
            el.Background({|REACTOR_THEME_004:new SolidColorBrush(Colors.White)|});
        }
    }
}";
        var fixedCode = Stubs + @"
namespace TestApp
{
    using Microsoft.UI;
    using Microsoft.UI.Xaml.Media;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        void M(dynamic el)
        {
            el.Background(Theme.SolidBackground);
        }
    }
}";
        await new CSharpCodeFixTest<UseThemeRefAnalyzer, UseThemeRefCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            CodeActionEquivalenceKey = "REACTOR_THEME_004_SolidBackground",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fix_Rewrites_Mapped_Black_To_Theme_PrimaryText()
    {
        var test = Stubs + @"
namespace TestApp
{
    using Microsoft.UI;
    using Microsoft.UI.Xaml.Media;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        void M(dynamic el)
        {
            el.Foreground({|REACTOR_THEME_004:new SolidColorBrush(Colors.Black)|});
        }
    }
}";
        var fixedCode = Stubs + @"
namespace TestApp
{
    using Microsoft.UI;
    using Microsoft.UI.Xaml.Media;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        void M(dynamic el)
        {
            el.Foreground(Theme.PrimaryText);
        }
    }
}";
        await new CSharpCodeFixTest<UseThemeRefAnalyzer, UseThemeRefCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            CodeActionEquivalenceKey = "REACTOR_THEME_004_PrimaryText",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Fix_For_Unmapped_Inline_Brush()
    {
        // Red has no token mapping — the diagnostic stands, but no auto-fix is offered (the analyzer
        // can't invent a resource key). FixedCode == TestCode asserts nothing is rewritten.
        var code = Stubs + @"
namespace TestApp
{
    using Microsoft.UI;
    using Microsoft.UI.Xaml.Media;

    class C
    {
        void M(dynamic el)
        {
            el.Background({|REACTOR_THEME_004:new SolidColorBrush(Colors.Red)|});
        }
    }
}";
        await new CSharpCodeFixTest<UseThemeRefAnalyzer, UseThemeRefCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Fix_When_Token_Family_Mismatches_Modifier()
    {
        // white maps to SolidBackground (a surface token); auto-fixing that onto .Foreground would
        // invert text/background across themes, so the diagnostic fires but no fix is offered.
        var code = Stubs + @"
namespace TestApp
{
    using Microsoft.UI;
    using Microsoft.UI.Xaml.Media;

    class C
    {
        void M(dynamic el)
        {
            el.Foreground({|REACTOR_THEME_004:new SolidColorBrush(Colors.White)|});
        }
    }
}";
        await new CSharpCodeFixTest<UseThemeRefAnalyzer, UseThemeRefCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Fix_When_Theme_Token_Unresolved()
    {
        // The mapped token (SolidBackground) is not defined on this Theme, so the fix is withheld to
        // avoid emitting a reference that wouldn't compile; the diagnostic still fires.
        var stubs = @"
namespace Windows.UI { public struct Color { } }
namespace Microsoft.UI
{
    public static class Colors { public static Windows.UI.Color White => default; }
}
namespace Microsoft.UI.Xaml.Media
{
    public class Brush { }
    public class SolidColorBrush : Brush { public SolidColorBrush(Windows.UI.Color color) { } }
}
namespace Microsoft.UI.Reactor.Core
{
    public readonly struct ThemeRef { }
    public static class Theme { public static ThemeRef PrimaryText => default; }
}
";
        var code = stubs + @"
namespace TestApp
{
    using Microsoft.UI;
    using Microsoft.UI.Xaml.Media;

    class C
    {
        void M(dynamic el)
        {
            el.Background({|REACTOR_THEME_004:new SolidColorBrush(Colors.White)|});
        }
    }
}";
        await new CSharpCodeFixTest<UseThemeRefAnalyzer, UseThemeRefCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Fix_For_Non_WinUi_SolidColorBrush()
    {
        // A same-named brush type from another namespace fires the syntactic diagnostic, but the fix
        // confirms WinUI's SolidColorBrush semantically and withholds the rewrite.
        var code = Stubs + @"
namespace Custom
{
    public class SolidColorBrush { public SolidColorBrush(Windows.UI.Color c) { } }
}
namespace TestApp
{
    using Microsoft.UI;

    class C
    {
        void M(dynamic el)
        {
            el.Background({|REACTOR_THEME_004:new Custom.SolidColorBrush(Colors.White)|});
        }
    }
}";
        await new CSharpCodeFixTest<UseThemeRefAnalyzer, UseThemeRefCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Fix_For_Look_Alike_Colors_Class()
    {
        // A non-WinUI `MyCompany.UI.Colors.White` must not be mapped to a theme token: the diagnostic
        // still fires (inline brush) but with a generic suggestion and no auto-fix.
        var code = Stubs + @"
namespace MyCompany.UI
{
    public static class Colors { public static Windows.UI.Color White => default; }
}
namespace TestApp
{
    using Microsoft.UI.Xaml.Media;

    class C
    {
        void M(dynamic el)
        {
            el.Background({|REACTOR_THEME_004:new SolidColorBrush(MyCompany.UI.Colors.White)|});
        }
    }
}";
        await new CSharpCodeFixTest<UseThemeRefAnalyzer, UseThemeRefCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fix_Rewrites_Global_Qualified_WinUi_Colors()
    {
        // The framework itself writes `global::Microsoft.UI.Colors.X`; that fully-qualified form must
        // still map + auto-fix.
        var test = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Xaml.Media;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        void M(dynamic el)
        {
            el.Background({|REACTOR_THEME_004:new SolidColorBrush(global::Microsoft.UI.Colors.White)|});
        }
    }
}";
        var fixedCode = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Xaml.Media;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        void M(dynamic el)
        {
            el.Background(Theme.SolidBackground);
        }
    }
}";
        await new CSharpCodeFixTest<UseThemeRefAnalyzer, UseThemeRefCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            CodeActionEquivalenceKey = "REACTOR_THEME_004_SolidBackground",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fix_Rewrites_Global_Qualified_SolidColorBrush_Type()
    {
        // The brush *type* itself written fully-qualified with `global::` must still be detected + fixed.
        var test = Stubs + @"
namespace TestApp
{
    using Microsoft.UI;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        void M(dynamic el)
        {
            el.Background({|REACTOR_THEME_004:new global::Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.White)|});
        }
    }
}";
        var fixedCode = Stubs + @"
namespace TestApp
{
    using Microsoft.UI;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        void M(dynamic el)
        {
            el.Background(Theme.SolidBackground);
        }
    }
}";
        await new CSharpCodeFixTest<UseThemeRefAnalyzer, UseThemeRefCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            CodeActionEquivalenceKey = "REACTOR_THEME_004_SolidBackground",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Fix_For_Surface_Token_On_WithBorder()
    {
        // .WithBorder sets a stroke; the map only has a surface token (SolidBackground) for white, so
        // the diagnostic fires but no auto-fix is offered (a fill token is a poor border suggestion).
        var code = Stubs + @"
namespace TestApp
{
    using Microsoft.UI;
    using Microsoft.UI.Xaml.Media;

    class C
    {
        void M(dynamic el)
        {
            el.WithBorder({|REACTOR_THEME_004:new SolidColorBrush(Colors.White)|});
        }
    }
}";
        await new CSharpCodeFixTest<UseThemeRefAnalyzer, UseThemeRefCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Fix_For_Look_Alike_Colors_Via_Unqualified_Identifier()
    {
        // Bare `Colors` here resolves to a non-WinUI palette (via `using MyCompany.UI;`, no
        // `using Microsoft.UI;`). The diagnostic fires syntactically, but the fix semantically
        // confirms the color source is Microsoft.UI/Windows.UI Colors and withholds the rewrite.
        var code = Stubs + @"
namespace MyCompany.UI
{
    public static class Colors { public static Windows.UI.Color White => default; }
}
namespace TestApp
{
    using MyCompany.UI;
    using Microsoft.UI.Xaml.Media;

    class C
    {
        void M(dynamic el)
        {
            el.Background({|REACTOR_THEME_004:new SolidColorBrush(Colors.White)|});
        }
    }
}";
        await new CSharpCodeFixTest<UseThemeRefAnalyzer, UseThemeRefCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
