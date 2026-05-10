// Spec 038 §5: Thresholds is the per-code emit gate for SymbolSuggester.
// These tests pin the public contract — defaults exist for the five handled
// codes, an unknown code falls back to Default, and the test-only override
// channel restores cleanly.

using Microsoft.UI.Reactor.Cli.Check.Suggesters;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Suggesters;

public class ThresholdsTests
{
    [Theory]
    [InlineData("CS1061")]
    [InlineData("CS0103")]
    [InlineData("CS0117")]
    [InlineData("CS1503")]
    [InlineData("CS7036")]
    public void Each_handled_code_has_a_threshold_in_valid_range(string code)
    {
        var t = Thresholds.For(code);
        Assert.InRange(t, Thresholds.SimilarityFloor, 1.0);
    }

    [Fact]
    public void Unknown_code_falls_back_to_Default()
    {
        Assert.Equal(Thresholds.Default, Thresholds.For("CS9999"));
    }

    [Fact]
    public void SimilarityFloor_is_below_Default()
    {
        // The qualitative-relatedness floor must be more permissive than the
        // emit gate; otherwise the floor is dead code.
        Assert.True(Thresholds.SimilarityFloor < Thresholds.Default);
    }

    [Fact]
    public void PerCode_override_round_trips()
    {
        var saved = Thresholds.PerCode;
        try
        {
            var custom = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["CS1061"] = 0.42,
            };
            Thresholds.PerCode = custom;
            Assert.Equal(0.42, Thresholds.For("CS1061"));
            // Codes not in custom map fall back to Default, not to the
            // previously-installed dictionary.
            Assert.Equal(Thresholds.Default, Thresholds.For("CS0103"));
        }
        finally
        {
            Thresholds.PerCode = saved;
        }
        Assert.Same(saved, Thresholds.PerCode);
    }
}
