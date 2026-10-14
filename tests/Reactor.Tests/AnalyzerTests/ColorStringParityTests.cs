using AnalyzerClassifier = Microsoft.UI.Reactor.Analyzers.ColorStringClassifier;
using CliClassifier = Microsoft.UI.Reactor.Cli.Check.Rules.ColorStringClassifier;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// REACTOR_DYM_006 parity lock (issue #1375). Three copies of one color grammar must agree:
/// <c>BrushHelper.TryParseColor</c> (the runtime truth), the netstandard2.0 analyzer's
/// <c>ColorStringClassifier</c>, and the <c>mur check</c> CLI's copy. If any of them drifts, the
/// analyzer either flags colors that render fine or misses ones that render gray.
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
        Assert.Equal(runtime, CliClassifier.TryParse(value));
    }

    [Theory]
    [MemberData(nameof(Battery))]
    public void Analyzer_and_cli_classify_identically(string value)
    {
        foreach (var key in new[] { null, "ButtonBackground", "ContentControlThemeFontFamily", "FontFamily", "fontfamily" })
        {
            foreach (var overload in new[] { false, true })
            {
                var a = AnalyzerClassifier.Classify(value, key, overload);
                var c = CliClassifier.Classify(value, key, overload);
                Assert.Equal(a.HasValue, c.HasValue);
                if (a is { } af && c is { } cf)
                {
                    Assert.Equal(af.Issue.ToString(), cf.Issue.ToString());
                    Assert.Equal(af.Replacement, cf.Replacement);
                    Assert.Equal(af.Detail, cf.Detail);
                }
            }
        }
    }

    [Fact]
    public void Named_color_tables_are_identical()
    {
        Assert.Equal(
            AnalyzerClassifier.NamedColorHex.OrderBy(p => p.Key, StringComparer.Ordinal),
            CliClassifier.NamedColorHex.OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.Equal(AnalyzerClassifier.SupportedNames, CliClassifier.SupportedNames);
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
