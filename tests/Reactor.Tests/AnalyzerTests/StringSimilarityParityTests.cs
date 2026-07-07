using System.Collections.Generic;
using Xunit;
using AnalyzerSim = Microsoft.UI.Reactor.Analyzers.StringSimilarity;
using CliSim = Microsoft.UI.Reactor.Cli.Check.Suggesters.StringSimilarity;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Parity guard for REACTOR_DYM_003. The netstandard2.0 analyzer cannot reference the net10 CLI
/// suggester engine, so it carries a <b>verbatim copy</b> of the CLI's Jaro-Winkler
/// (<see cref="AnalyzerSim"/> vs <see cref="CliSim"/>). These tests assert the two return
/// <b>bit-identical</b> scores across a battery of inputs, so the in-build "did you mean" can never
/// silently diverge from <c>mur check</c>. If someone edits one copy without the other, this fails.
/// </summary>
public class StringSimilarityParityTests
{
    // A mix of real factory names, realistic typos, and structural edge cases (empty, single char,
    // shared prefixes, transpositions, case differences, differing lengths).
    private static readonly string[] Words =
    {
        "", "a", "A", "ab", "Button", "Buton", "Buttonn", "button",
        "TextBlock", "TextBock", "Text", "VStack", "Vstack", "HStack", "Stack",
        "ComboBox", "ComboBx", "NumberBox", "NumbrBox", "ScrollViewer", "ScrollView",
        "RadioButton", "RadioButtons", "Border", "Order", "Compute", "Component",
        "Heading", "Headig", "ListBox", "ListView", "List", "Slider", "Slidr",
        "ProgressRing", "ProgresRing", "CheckBox", "Chekbox", "Grid", "Gird",
    };

    public static IEnumerable<object[]> Pairs()
    {
        foreach (var s in Words)
            foreach (var t in Words)
                yield return new object[] { s, t };
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Port_matches_Cli_default_scale(string s, string t)
    {
        // Exact equality: identical algorithm, identical inputs, so identical IEEE-754 result.
        Assert.Equal(CliSim.JaroWinkler(s, t), AnalyzerSim.JaroWinkler(s, t));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.05)]
    [InlineData(0.1)]
    [InlineData(0.2)]
    [InlineData(0.25)]
    public void Port_matches_Cli_across_prefix_scales(double prefixScale)
    {
        foreach (var s in Words)
            foreach (var t in Words)
                Assert.Equal(
                    CliSim.JaroWinkler(s, t, prefixScale),
                    AnalyzerSim.JaroWinkler(s, t, prefixScale));
    }
}
