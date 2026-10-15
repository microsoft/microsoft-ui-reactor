using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.UI.Reactor.Analyzers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for REACTOR_DYM_006 (<see cref="ColorStringLiteralAnalyzer"/> +
/// <see cref="ColorStringLiteralCodeFix"/>): a constant color string that BrushHelper can't parse and
/// so silently renders gray. Unlike DYM_001–005 the flagged code compiles, so compiler-error
/// verification stays ON — every fixed snippet must still compile.
/// </summary>
public class ColorStringLiteralAnalyzerTests
{
    // The color-string API surface, minus the FontFamily overload of ResourceBuilder.Set (PR #1370).
    internal const string CommonStubs = @"
namespace Microsoft.UI.Xaml.Media
{
    public class Brush { }
    public class SolidColorBrush : Brush { }
    public class FontFamily { public FontFamily(string familyName) { } }
}
namespace Microsoft.UI.Reactor.Core
{
    public abstract class Element { }
    public sealed class ThemeRef { }
    public sealed class TextBlockElement : Element { }
}
namespace Microsoft.UI.Reactor
{
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Xaml.Media;
    public sealed class RichTextRun { }
    public static class BrushHelper
    {
        public static SolidColorBrush Parse(string color) => null!;
    }
    public static partial class ElementExtensions
    {
        public static T Background<T>(this T el, string color) where T : Element => el;
        public static T Background<T>(this T el, Brush brush) where T : Element => el;
        public static T Background<T>(this T el, ThemeRef theme) where T : Element => el;
        public static T Foreground<T>(this T el, string color) where T : Element => el;
        public static T BorderBrush<T>(this T el, string color) where T : Element => el;
        public static T WithBorder<T>(this T el, string color, double thickness = 1) where T : Element => el;
        public static RichTextRun Foreground(this RichTextRun run, string color) => run;
        public static T Margin<T>(this T el, string label) where T : Element => el;
    }
    public static class Factories
    {
        public static TextBlockElement TextBlock(string text) => null!;
    }
}
namespace Acme
{
    using Microsoft.UI.Reactor.Core;
    public static class ElementExtensions
    {
        public static T Paint<T>(this T el, string color) where T : Element => el;
    }
    public static class BrushHelper
    {
        public static object Parse(string color) => null!;
    }
}
";

    internal const string ResourceBuilderWithoutFontFamily = @"
namespace Microsoft.UI.Reactor.Elements
{
    using Microsoft.UI.Xaml.Media;
    public sealed class ResourceBuilder
    {
        public ResourceBuilder Set(string key, string color) => this;
        public ResourceBuilder Set(string key, Brush brush) => this;
        public ResourceBuilder Set(string key, double value) => this;
    }
}
";

    internal const string ResourceBuilderWithFontFamily = @"
namespace Microsoft.UI.Reactor.Elements
{
    using Microsoft.UI.Xaml.Media;
    public sealed class ResourceBuilder
    {
        public ResourceBuilder Set(string key, string color) => this;
        public ResourceBuilder Set(string key, Brush brush) => this;
        public ResourceBuilder Set(string key, double value) => this;
        public ResourceBuilder Set(string key, FontFamily fontFamily) => this;
    }
}
";

    private const string Usings = @"
namespace App
{
    using Microsoft.UI.Reactor;
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Reactor.Elements;
    using Microsoft.UI.Xaml.Media;
    using static Microsoft.UI.Reactor.Factories;
";

    private static string Wrap(string members, bool fontFamilyOverload = false) =>
        CommonStubs
        + (fontFamilyOverload ? ResourceBuilderWithFontFamily : ResourceBuilderWithoutFontFamily)
        + Usings
        + "    static class C\n    {\n" + members + "\n    }\n}\n";

    private static CSharpAnalyzerTest<ColorStringLiteralAnalyzer, DefaultVerifier> MakeTest(string code)
    {
        var test = new CSharpAnalyzerTest<ColorStringLiteralAnalyzer, DefaultVerifier> { TestCode = code };
        test.DisabledDiagnostics.Add("CS1591");
        return test;
    }

    private static Task RunAsync(string members, bool fontFamilyOverload = false) =>
        MakeTest(Wrap(members, fontFamilyOverload)).RunAsync(TestContext.Current.CancellationToken);

    private static DiagnosticResult Dym006(int markup) =>
        new DiagnosticResult(ColorStringLiteralAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(markup);

    // ── Positives: every covered API ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Flags_Background_with_css_name_and_suggests_hex()
    {
        var test = MakeTest(Wrap(@"        static object M() => TextBlock(""hi"").Background({|#0:""orange""|});"));
        test.ExpectedDiagnostics.Add(Dym006(0).WithArguments(
            "orange",
            "the only built-in names are red, green, blue, white, black, gray/grey, lightgray/lightgrey, transparent; did you mean \"#FFA500\"?"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task Flags_Foreground_hex_shorthand() =>
        RunAsync(@"        static object M() => TextBlock(""hi"").Foreground({|REACTOR_DYM_006:""#888""|});");

    [Fact]
    public Task Flags_BorderBrush_missing_hash() =>
        RunAsync(@"        static object M() => TextBlock(""hi"").BorderBrush({|REACTOR_DYM_006:""FF0000""|});");

    [Fact]
    public Task Flags_WithBorder_malformed_hex() =>
        RunAsync(@"        static object M() => TextBlock(""hi"").WithBorder({|REACTOR_DYM_006:""#12345""|}, 2);");

    [Fact]
    public Task Flags_RichTextRun_Foreground() =>
        RunAsync(@"        static object M(RichTextRun run) => run.Foreground({|REACTOR_DYM_006:""Yellow""|});");

    [Fact]
    public Task Flags_BrushHelper_Parse() =>
        RunAsync(@"        static object M() => BrushHelper.Parse({|REACTOR_DYM_006:""AliceBlue""|});");

    [Fact]
    public Task Flags_ResourceBuilder_Set_color_key() =>
        RunAsync(@"        static object M(ResourceBuilder r) => r.Set(""ButtonBackground"", {|REACTOR_DYM_006:""LightCoral""|});");

    [Fact]
    public Task Flags_named_argument() =>
        RunAsync(@"        static object M() => TextBlock(""hi"").Background(color: {|REACTOR_DYM_006:""orange""|});");

    [Fact]
    public async Task Flags_unknown_name_with_supported_list()
    {
        var test = MakeTest(Wrap(@"        static object M() => TextBlock(""hi"").Background({|#0:""accent""|});"));
        test.ExpectedDiagnostics.Add(Dym006(0).WithArguments(
            "accent",
            "use #RRGGBB or #AARRGGBB, a Theme token, or one of red, green, blue, white, black, gray/grey, lightgray/lightgrey, transparent"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task Flags_empty_string() =>
        RunAsync(@"        static object M() => TextBlock(""hi"").Background({|REACTOR_DYM_006:""""|});");

    // ── Positives: constant shapes ───────────────────────────────────────────────────────────────

    [Fact]
    public Task Flags_const_reference() =>
        RunAsync(@"        const string Brand = ""orange"";
        static object M() => TextBlock(""hi"").Background({|REACTOR_DYM_006:Brand|});");

    [Fact]
    public Task Flags_constant_concatenation() =>
        RunAsync(@"        const string Hash = ""#"";
        static object M() => TextBlock(""hi"").Background({|REACTOR_DYM_006:Hash + ""888""|});");

    [Fact]
    public Task Flags_only_the_unparseable_conditional_branch() =>
        RunAsync(@"        static object M(bool on) => TextBlock(""hi"").Background(on ? ""red"" : {|REACTOR_DYM_006:""orange""|});");

    [Fact]
    public Task Flags_coalesce_and_switch_branches() =>
        RunAsync(@"        static object M(string s, int i) => TextBlock(""hi"")
            .Background(s ?? {|REACTOR_DYM_006:""teal""|})
            .Foreground(i switch { 0 => ""black"", _ => {|REACTOR_DYM_006:""navy""|} });");

    // ── FontFamily key ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Flags_FontFamily_key_and_suggests_FontFamily_when_overload_exists()
    {
        var test = MakeTest(Wrap(
            @"        static object M(ResourceBuilder r) => r.Set(""ContentControlThemeFontFamily"", {|#0:""Consolas""|});",
            fontFamilyOverload: true));
        test.ExpectedDiagnostics.Add(Dym006(0).WithArguments(
            "Consolas",
            "'ContentControlThemeFontFamily' is a FontFamily resource; did you mean new Microsoft.UI.Xaml.Media.FontFamily(\"Consolas\")?"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Flags_FontFamily_key_without_suggestion_when_overload_missing()
    {
        var test = MakeTest(Wrap(
            @"        static object M(ResourceBuilder r) => r.Set(""ContentControlThemeFontFamily"", {|#0:""Consolas""|});"));
        test.ExpectedDiagnostics.Add(Dym006(0).WithArguments(
            "Consolas",
            "'ContentControlThemeFontFamily' is a FontFamily resource, but this Set overload only stores color brushes; to change the font, use .FontFamily(\"Consolas\") on the element"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FontFamily_key_inside_a_branch_gets_no_FontFamily_suggestion()
    {
        // new FontFamily(...) can't replace one branch of a ?: — the other branch is still a string.
        var test = MakeTest(Wrap(
            @"        static object M(ResourceBuilder r, bool on) => r.Set(""ContentControlThemeFontFamily"", on ? {|#0:""Consolas""|} : ""red"");",
            fontFamilyOverload: true));
        test.ExpectedDiagnostics.Add(Dym006(0).WithArguments(
            "Consolas",
            "'ContentControlThemeFontFamily' is a FontFamily resource, but this Set overload only stores color brushes; to change the font, use .FontFamily(\"Consolas\") on the element"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Negatives ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public Task Silent_on_every_supported_name_in_any_casing() =>
        RunAsync(@"        static object M() => TextBlock(""hi"")
            .Background(""red"").Background(""Green"").Background(""BLUE"").Background(""White"")
            .Background(""black"").Background(""Gray"").Background(""grey"").Background(""LightGray"")
            .Background(""lightgrey"").Background(""Transparent"");");

    [Fact]
    public Task Silent_on_valid_hex() =>
        RunAsync(@"        static object M() => TextBlock(""hi"")
            .Background(""#FF0000"").Foreground(""#80ff0000"").BorderBrush(""#aAbBcC"").WithBorder(""#00000000"");");

    [Fact]
    public Task Silent_on_non_constant_values() =>
        RunAsync(@"        static object M(string c, int n) => TextBlock(""hi"")
            .Background(c).Foreground($""#{n:X6}"").BorderBrush(c + ""00"");");

    [Fact]
    public Task Silent_on_brush_and_theme_overloads_and_non_color_params() =>
        RunAsync(@"        static object M(Brush b, ThemeRef t, ResourceBuilder r)
        {
            r.Set(""ButtonBorderThemeThickness"", 2.0);
            r.Set(""ButtonBackground"", ""#0078D4"");
            r.Set(""ContentControlThemeFontFamily"", b);
            return TextBlock(""orange"").Background(b).Background(t).Margin(""orange"");
        }");

    [Fact]
    public Task Silent_on_lookalike_apis_outside_Reactor() =>
        RunAsync(@"        static object M() => Acme.BrushHelper.Parse(""orange"") ?? Acme.ElementExtensions.Paint(TextBlock(""hi""), ""orange"");");

    [Fact]
    public Task Silent_on_FontFamily_key_with_a_valid_color() =>
        RunAsync(@"        static object M(ResourceBuilder r) => r.Set(""SomethingFontFamily"", ""red"");", fontFamilyOverload: true);

    [Fact]
    public Task Silent_on_unreachable_constant_branch() =>
        // The whole expression is a constant ("red"), so the dead "orange" branch is never parsed.
        RunAsync(@"        static object M() => TextBlock(""hi"").Background(true ? ""red"" : ""orange"");");

    [Fact]
    public Task Silent_on_dead_branches_the_compiler_does_not_fold() =>
        // Each expression as a whole is non-constant, so only the constant condition / left side
        // proves which branch runs. The live branches are still checked.
        RunAsync(@"        const bool DebugBuild = false;
        const string Mode = ""dark"";
        static object M(string c) => TextBlock(""hi"")
            .Background(DebugBuild ? ""magenta"" : c)
            .Foreground(!DebugBuild ? c : ""magenta"")
            .BorderBrush(""red"" ?? c)
            .WithBorder(Mode switch { ""light"" => ""orange"", ""dark"" => c, _ => ""teal"" })
            .Background(DebugBuild ? c : {|REACTOR_DYM_006:""navy""|});");

    [Fact]
    public Task Non_constant_switch_keeps_every_arm() =>
        RunAsync(@"        static object M(string mode, string c) => TextBlock(""hi"")
            .Background(mode switch { ""light"" => {|REACTOR_DYM_006:""orange""|}, _ => c });");

    [Fact]
    public Task Coalesce_fallback_is_dead_when_a_nested_selector_is_provably_non_null() =>
        RunAsync(@"        const bool DebugBuild = false;
        static object M(string c, bool on) => TextBlock(""hi"")
            .Background((DebugBuild ? c : ""red"") ?? ""orange"")
            .Foreground((on ? ""red"" : ""white"") ?? ""teal"")
            .BorderBrush((on ? c : ""red"") ?? {|REACTOR_DYM_006:""navy""|});");

    [Fact]
    public Task Silent_through_a_user_defined_conversion() =>
        // `token ?? "orange"` is a ColorToken; its operator decides the string BrushHelper receives,
        // so "orange" is never parsed. Positive control: once the coalesce is over strings, it is.
        RunAsync(@"        sealed class ColorToken
        {
            public static implicit operator ColorToken(string s) => new ColorToken();
            public static implicit operator string(ColorToken t) => ""red"";
        }
        static object M(ColorToken token, bool on) => BrushHelper.Parse(token ?? ""orange"")
            ?? BrushHelper.Parse(on switch { true => token ?? ""orange"", _ => ""red"" })
            ?? BrushHelper.Parse((string)token ?? {|REACTOR_DYM_006:""orange""|});");

    [Fact]
    public Task Coalesce_over_a_one_way_user_defined_conversion_is_not_provably_non_null() =>
        // `token ?? "red"` is a string, but a non-null token converts through an operator that may
        // return null, so the outer "orange" fallback is reachable.
        RunAsync(@"        sealed class NullableToken
        {
            public static implicit operator string(NullableToken t) => null!;
        }
        static object M(NullableToken token) => TextBlock(""hi"").Background((token ?? ""red"") ?? {|REACTOR_DYM_006:""orange""|});");

    [Fact]
    public Task Coalesce_fallback_is_dead_when_the_other_branch_throws() =>
        // Execution either yields "red" or throws, so "orange" is unreachable.
        RunAsync(@"        static object M(string c, bool on) => TextBlock(""hi"")
            .Background((on ? ""red"" : throw new global::System.InvalidOperationException()) ?? ""orange"")
            .Foreground((on ? c : throw new global::System.InvalidOperationException()) ?? {|REACTOR_DYM_006:""teal""|});");

    [Fact]
    public Task Switch_pruning_compares_the_converted_pattern_constant() =>
        // `1` is an int literal but the pattern converts it to the governing `long`.
        RunAsync(@"        const long Mode = 1;
        static object M(string c) => TextBlock(""hi"")
            .Background(Mode switch { 1 => c, _ => ""orange"" })
            .Foreground(Mode switch { 2 => c, _ => {|REACTOR_DYM_006:""teal""|} });");

    [Fact]
    public async Task FontFamily_suggestion_renames_a_named_argument_and_escapes_the_value()
    {
        var test = MakeTest(Wrap(
            @"        static object M(ResourceBuilder r) => r.Set(key: ""ContentControlThemeFontFamily"", color: {|#0:@""C:\Fonts\Mono.ttf#Mono""|});",
            fontFamilyOverload: true));
        test.ExpectedDiagnostics.Add(Dym006(0).WithArguments(
            @"C:\Fonts\Mono.ttf#Mono",
            @"'ContentControlThemeFontFamily' is a FontFamily resource; did you mean fontFamily: new Microsoft.UI.Xaml.Media.FontFamily(""C:\\Fonts\\Mono.ttf#Mono"")?"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Silent_without_any_Reactor_color_api()
    {
        var test = MakeTest(@"static class C { static string M() => ""orange""; }");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Live Reactor surface ────────────────────────────────────────────────────────────────────
    // Every other test runs against stubs that hardcode `string color`. This one binds to the real
    // Reactor assembly, so renaming a `color` parameter or moving a type (which would silently turn
    // the analyzer off) fails here.

    [Fact]
    public async Task Fires_on_every_covered_api_of_the_live_Reactor_assembly()
    {
        var live = Microsoft.UI.Reactor.Tests.CheckCommandTests.Rules.RuleTargetResolutionTests.BuildLiveReactorCompilation();

        var targets = ColorStringLiteralAnalyzer.Targets.Resolve(live);
        Assert.NotNull(targets);
        var covered = targets!.Methods
            .Select(m => $"{m.ContainingType.Name}.{m.Name}({string.Join(", ", m.Parameters.Select(p => p.Type.Name + " " + p.Name))})")
            .OrderBy(s => s, global::System.StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[]
        {
            "BrushHelper.Parse(String color)",
            "ElementExtensions.Background(T el, String color)",
            "ElementExtensions.BorderBrush(T el, String color)",
            "ElementExtensions.Foreground(RichTextBlockElement el, String color)",
            "ElementExtensions.Foreground(RichTextHyperlink link, String color)",
            "ElementExtensions.Foreground(RichTextParagraph paragraph, String color)",
            "ElementExtensions.Foreground(RichTextRun run, String color)",
            "ElementExtensions.Foreground(T el, String color)",
            "ElementExtensions.WithBorder(T el, String color, Double thickness)",
            "ResourceBuilder.Set(String key, String color)",
        }, covered);

        var source = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(@"
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Elements;
using static Microsoft.UI.Reactor.Factories;
static class LiveProbe
{
    static object A() => TextBlock(""x"").Background(""orange"");
    static object B() => new ResourceBuilder().Set(""ButtonBackground"", ""#888"");
    static object C() => BrushHelper.Parse(""FF0000"");
    static object D() => TextBlock(""x"").Foreground(""red"").BorderBrush(""#80FF0000"");
}", cancellationToken: TestContext.Current.CancellationToken);
        var compilation = live.AddSyntaxTrees(source);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Location.SourceTree == source));

        var diagnostics = await compilation
            .WithAnalyzers(global::System.Collections.Immutable.ImmutableArray.Create<Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer>(new ColorStringLiteralAnalyzer()))
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            new[] { "orange", "#888", "FF0000" },
            diagnostics.Where(d => d.Id == ColorStringLiteralAnalyzer.DiagnosticId)
                .OrderBy(d => d.Location.SourceSpan.Start)
                .Select(d => d.Properties.ContainsKey(ColorStringLiteralAnalyzer.ReplacementProperty) ? source.GetText(TestContext.Current.CancellationToken).ToString(d.Location.SourceSpan).Trim('"') : "?"));
    }

    // ── Code fixes ──────────────────────────────────────────────────────────────────────────────

    private static Task RunFixAsync(string before, string after, bool fontFamilyOverload = false)
    {
        var test = new CSharpCodeFixTest<ColorStringLiteralAnalyzer, ColorStringLiteralCodeFix, DefaultVerifier>
        {
            TestCode = Wrap(before, fontFamilyOverload),
            FixedCode = Wrap(after, fontFamilyOverload),
        };
        test.DisabledDiagnostics.Add("CS1591");
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task Fix_expands_hex_shorthand() =>
        RunFixAsync(
            @"        static object M() => TextBlock(""hi"").Foreground({|REACTOR_DYM_006:""#888""|}).Background({|REACTOR_DYM_006:""#8abc""|});",
            @"        static object M() => TextBlock(""hi"").Foreground(""#888888"").Background(""#88aabbcc"");");

    [Fact]
    public Task Fix_adds_missing_hash() =>
        RunFixAsync(
            @"        static object M() => TextBlock(""hi"").Background({|REACTOR_DYM_006:""FF0000""|});",
            @"        static object M() => TextBlock(""hi"").Background(""#FF0000"");");

    [Fact]
    public Task Fix_maps_css_name_to_hex_including_inside_a_branch() =>
        RunFixAsync(
            @"        static object M(bool on) => BrushHelper.Parse(on ? {|REACTOR_DYM_006:""AliceBlue""|} : ""white"");",
            @"        static object M(bool on) => BrushHelper.Parse(on ? ""#F0F8FF"" : ""white"");");

    [Fact]
    public Task Fix_rewrites_FontFamily_key_to_FontFamily_overload() =>
        RunFixAsync(
            @"        static object M(ResourceBuilder r) => r.Set(""ContentControlThemeFontFamily"", {|REACTOR_DYM_006:""Consolas""|});",
            @"        static object M(ResourceBuilder r) => r.Set(""ContentControlThemeFontFamily"", new FontFamily(""Consolas""));",
            fontFamilyOverload: true);

    [Fact]
    public Task Fix_wraps_const_FontFamily_reference() =>
        RunFixAsync(
            @"        const string Mono = ""Consolas"";
        static object M(ResourceBuilder r) => r.Set(""ContentControlThemeFontFamily"", {|REACTOR_DYM_006:Mono|});",
            @"        const string Mono = ""Consolas"";
        static object M(ResourceBuilder r) => r.Set(""ContentControlThemeFontFamily"", new FontFamily(Mono));",
            fontFamilyOverload: true);

    [Fact]
    public Task Fix_renames_named_color_argument_to_the_FontFamily_parameter() =>
        RunFixAsync(
            @"        static object M(ResourceBuilder r) => r.Set(key: ""ContentControlThemeFontFamily"", color: {|REACTOR_DYM_006:""Consolas""|});",
            @"        static object M(ResourceBuilder r) => r.Set(key: ""ContentControlThemeFontFamily"", fontFamily: new FontFamily(""Consolas""));",
            fontFamilyOverload: true);

    [Fact]
    public Task No_FontFamily_fix_inside_a_branch() =>
        RunFixAsync(
            @"        static object M(ResourceBuilder r, bool on) => r.Set(""ContentControlThemeFontFamily"", on ? {|REACTOR_DYM_006:""Consolas""|} : ""red"");",
            @"        static object M(ResourceBuilder r, bool on) => r.Set(""ContentControlThemeFontFamily"", on ? {|REACTOR_DYM_006:""Consolas""|} : ""red"");",
            fontFamilyOverload: true);

    [Fact]
    public Task No_fix_without_FontFamily_overload_or_for_unknown_or_const_color() =>
        RunFixAsync(
            @"        const string Brand = ""orange"";
        static object M(ResourceBuilder r)
        {
            r.Set(""ContentControlThemeFontFamily"", {|REACTOR_DYM_006:""Consolas""|});
            return TextBlock(""hi"").Background({|REACTOR_DYM_006:""accent""|}).Foreground({|REACTOR_DYM_006:Brand|}).BorderBrush({|REACTOR_DYM_006:""#12345""|});
        }",
            @"        const string Brand = ""orange"";
        static object M(ResourceBuilder r)
        {
            r.Set(""ContentControlThemeFontFamily"", {|REACTOR_DYM_006:""Consolas""|});
            return TextBlock(""hi"").Background({|REACTOR_DYM_006:""accent""|}).Foreground({|REACTOR_DYM_006:Brand|}).BorderBrush({|REACTOR_DYM_006:""#12345""|});
        }");
}
