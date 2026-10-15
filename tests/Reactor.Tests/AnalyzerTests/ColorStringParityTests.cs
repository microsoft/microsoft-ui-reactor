using AnalyzerClassifier = Microsoft.UI.Reactor.Analyzers.ColorStringClassifier;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// REACTOR_DYM_006 parity lock (issue #1375). Two copies of one color grammar must agree:
/// <c>BrushHelper.TryParseColor</c> (the runtime truth) and the netstandard2.0 analyzer's
/// <c>ColorStringClassifier</c>. If they drift, the analyzer either flags colors that render fine or
/// misses ones that render gray. (The <c>mur check</c> rule reads the analyzer's message, so it has no
/// grammar of its own; <c>DidYouMeanAnalyzerParityTests</c> covers it.)
/// </summary>
public class ColorStringParityTests
{
    // Covers every branch of BrushHelper's grammar, including its quirks: case folding, the
    // '#'-only hex route, TrimStart('#') swallowing repeated hashes, and NumberStyles.HexNumber
    // accepting leading/trailing whitespace inside a byte pair.
    public static TheoryData<string> Battery => new()
    {
        "", " ", "red", "RED", "Red", "green", "blue", "white", "black", "gray", "GREY", "lightgray",
        "LightGrey", "transparent", "Transparent", " red", "red ", "reds", "orange", "AliceBlue",
        "aliceblue", "LightCoral", "Yellow", "slategrey", "rebeccapurple", "accent", "Consolas",
        "Segoe UI", "#", "##", "#FF0000", "#ff0000", "#aAbBcC", "#80FF0000", "#00000000", "##FF0000",
        "###FF0000", "#FFF", "#ffff", "#8abc", "#12345", "#1234567", "#123456789", "#GGHHII",
        "#FF00GG", "#0 0000", "# 00000", "#00000 ", "# FF0000", "#-10000", "#+10000", "#0x1234",
        "FF0000", "80FF0000", "ff00ff", "FFF", "decade", "#FF0000\n", "\u0130", "#\u0661\u0662\u0663\u0664\u0665\u0666",
    };

    [Theory]
    [MemberData(nameof(Battery))]
    public void Analyzer_TryParse_matches_BrushHelper(string value)
    {
        var runtime = BrushHelper.TryParseColor(value, out _);
        Assert.Equal(runtime, AnalyzerClassifier.TryParse(value));
        // Classify must agree too: a finding exactly when BrushHelper falls back to gray.
        Assert.Equal(!runtime, AnalyzerClassifier.Classify(value, null, false).HasValue);
    }

    [Fact]
    public void Every_fixable_detail_ends_in_the_did_you_mean_the_cli_rule_reads()
    {
        // ColorStringLiteralRule lifts the rewrite out of "...; did you mean X?". Keep the suffix stable.
        foreach (var (value, key, overload) in new (string, string?, bool)[]
        {
            ("#888", null, false), ("FF0000", null, false), ("orange", null, false),
            ("Consolas", "ContentControlThemeFontFamily", true),
        })
        {
            var finding = AnalyzerClassifier.Classify(value, key, overload)!.Value;
            Assert.NotNull(finding.Replacement);
            Assert.EndsWith("; did you mean " + (finding.Issue.ToString() == "FontFamilyKey"
                ? AnalyzerClassifier.FontFamilySuggestion(finding.Replacement!, null)
                : $"\"{finding.Replacement}\"") + "?", finding.Detail, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("Consolas", null, "new Microsoft.UI.Xaml.Media.FontFamily(\"Consolas\")")]
    [InlineData("Consolas", "fontFamily", "fontFamily: new Microsoft.UI.Xaml.Media.FontFamily(\"Consolas\")")]
    [InlineData(@"C:\Fonts\a""b.ttf#A", null, @"new Microsoft.UI.Xaml.Media.FontFamily(""C:\\Fonts\\a\""b.ttf#A"")")]
    public void FontFamily_suggestion_is_compilable_CSharp(string value, string? argumentName, string expected)
    {
        Assert.Equal(expected, AnalyzerClassifier.FontFamilySuggestion(value, argumentName));
        // And it round-trips: the literal parses back to the original value.
        var creation = (Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax)
            Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression(AnalyzerClassifier.FontFamilySuggestion(value, null));
        Assert.Empty(creation.GetDiagnostics());
        var literal = (Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax)creation.ArgumentList!.Arguments[0].Expression;
        Assert.Equal(value, literal.Token.ValueText);
    }

    [Fact]
    public void Supported_names_list_matches_BrushHelper()
    {
        foreach (var name in AnalyzerClassifier.SupportedNames.Split(", ").SelectMany(n => n.Split('/')))
            Assert.True(BrushHelper.TryParseColor(name, out _), $"'{name}' is listed as supported but BrushHelper rejects it.");
    }

    [Fact]
    public void Every_named_color_is_unsupported_by_BrushHelper_and_maps_to_its_exact_hex()
    {
        Assert.True(AnalyzerClassifier.NamedColorHex.Count >= 130, $"Only {AnalyzerClassifier.NamedColorHex.Count} names.");
        foreach (var (name, hex) in AnalyzerClassifier.NamedColorHex)
        {
            // A name BrushHelper already parses would be a false positive.
            Assert.False(BrushHelper.TryParseColor(name, out _), $"BrushHelper already parses '{name}'.");

            // The suggested hex must parse, and must be the color the name means. System.Drawing's
            // KnownColor table is an independent oracle (it spells the grey aliases 'gray').
            Assert.True(BrushHelper.TryParseColor(hex, out var parsed), $"'{hex}' for '{name}' does not parse.");
            var known = global::System.Drawing.Color.FromName(name.Replace("grey", "gray", StringComparison.Ordinal));
            Assert.True(known.IsKnownColor, $"'{name}' is not a known color.");
            Assert.Equal((known.R, known.G, known.B), (parsed.R, parsed.G, parsed.B));
            Assert.Equal(255, parsed.A);
        }
    }

    [Theory]
    [InlineData("#888", "#888888")]
    [InlineData("#8abc", "#88aabbcc")]
    [InlineData("FF0000", "#FF0000")]
    [InlineData("80FF0000", "#80FF0000")]
    [InlineData("orange", "#FFA500")]
    public void Every_literal_replacement_actually_parses(string value, string expected)
    {
        var finding = AnalyzerClassifier.Classify(value, null, false);
        Assert.NotNull(finding);
        Assert.Equal(expected, finding!.Value.Replacement);
        Assert.True(BrushHelper.TryParseColor(expected, out _));
    }
}
