using Microsoft.UI.Reactor.Cli.Check.Suggesters;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Tooling;

/// <summary>
/// `mur check` suggestion evidence carries a similarity score. It is printed to
/// stdout and written into the structured trace (<c>TraceWriter.WriteRuleFired</c>), so it is
/// machine-readable dev-tool output and must not pick up the ambient decimal separator.
/// </summary>
/// <remarks>
/// This was missed by the first sweep of the CLI: the search pattern required an alignment
/// specifier (<c>{x,9:F1}</c>), so the bare <c>{top.Score:F2}</c> sites here never matched.
/// The lesson is the repo's own — a no-match is not a measurement until a positive control
/// shows the probe could have matched.
/// </remarks>
public class SymbolSuggesterEvidenceTests
{
    [CulturedFact(new[] { "nl-NL" })]
    public void SimilarityEvidence_Is_Invariant_Under_Comma_Decimal_Culture()
    {
        var evidence = SymbolSuggester.SimilarityEvidence("Reactor factory", 0.95);

        Assert.Equal("Reactor factory, similarity 0.95", evidence);
        Assert.DoesNotContain("0,95", evidence);
    }

    [CulturedFact(new[] { "en-US" })]
    public void SimilarityEvidence_Formats_Two_Decimals()
    {
        // The "F2" contract itself, pinned so the literal is not host-dependent.
        Assert.Equal(
            "member of Colors, similarity 0.50",
            SymbolSuggester.SimilarityEvidence("member of Colors", 0.5));
    }
}
