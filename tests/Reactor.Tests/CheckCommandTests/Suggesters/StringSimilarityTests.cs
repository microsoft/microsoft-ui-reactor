// Phase 1.4 — Jaro–Winkler similarity unit tests.

using Microsoft.UI.Reactor.Cli.Check.Suggesters;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Suggesters;

public class StringSimilarityTests
{
    [Theory]
    [InlineData("", "", 1.0)]
    [InlineData("Bar", "Bar", 1.0)]
    public void Identical_strings_return_one(string s, string t, double expected)
    {
        Assert.Equal(expected, StringSimilarity.JaroWinkler(s, t), 3);
    }

    [Theory]
    [InlineData("", "anything")]
    [InlineData("anything", "")]
    public void Either_empty_returns_zero(string s, string t)
    {
        Assert.Equal(0.0, StringSimilarity.JaroWinkler(s, t), 3);
    }

    [Fact]
    public void Common_close_misspellings_score_above_threshold()
    {
        // "Brr" vs "Bar" — typo by one char, both 3 chars long.
        var score = StringSimilarity.JaroWinkler("Brr", "Bar");
        Assert.True(score >= 0.7, $"got {score:F2}");
    }

    [Fact]
    public void OnClick_vs_onClick_is_high_similarity()
    {
        var score = StringSimilarity.JaroWinkler("OnClick", "onClick");
        // Differ only in first-letter case; very similar.
        Assert.True(score >= 0.85, $"got {score:F2}");
    }

    [Fact]
    public void Far_apart_strings_score_low()
    {
        var score = StringSimilarity.JaroWinkler("Heading", "Foreground");
        Assert.True(score < 0.7, $"got {score:F2}");
    }

    [Fact]
    public void Heading_vs_Headig_is_typo_score()
    {
        var score = StringSimilarity.JaroWinkler("Headig", "Heading");
        Assert.True(score >= 0.85, $"got {score:F2}");
    }
}
