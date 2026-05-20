#nullable enable

using Microsoft.UI.Reactor.Cli.Find;
using Xunit;

namespace Reactor.Tests.Find;

public class BM25Tests
{
    [Fact]
    public void Score_MatchingTerm_ReturnsPositive()
    {
        var doc = new WeightedDoc(new Dictionary<string, double> { ["button"] = 1.0 }, 1);
        var stats = new CorpusStats(1, 1, new Dictionary<string, int> { ["button"] = 1 });

        var score = BM25.Score(["button"], doc, stats);

        Assert.True(score > 0);
    }

    [Fact]
    public void Score_NoMatch_ReturnsZero()
    {
        var doc = new WeightedDoc(new Dictionary<string, double> { ["button"] = 1.0 }, 1);
        var stats = new CorpusStats(1, 1, new Dictionary<string, int> { ["button"] = 1 });

        var score = BM25.Score(["dialog"], doc, stats);

        Assert.Equal(0, score);
    }

    [Fact]
    public void Score_HigherWeight_ScoresHigher()
    {
        var lowWeightDoc = new WeightedDoc(new Dictionary<string, double> { ["button"] = 1.0 }, 10);
        var highWeightDoc = new WeightedDoc(new Dictionary<string, double> { ["button"] = 3.0 }, 10);
        var stats = new CorpusStats(2, 10, new Dictionary<string, int> { ["button"] = 2 });

        var lowScore = BM25.Score(["button"], lowWeightDoc, stats);
        var highScore = BM25.Score(["button"], highWeightDoc, stats);

        Assert.True(highScore > lowScore);
    }

    [Fact]
    public void Score_ShorterDoc_ScoresHigher()
    {
        var shortDoc = new WeightedDoc(new Dictionary<string, double> { ["button"] = 1.0 }, 4);
        var longDoc = new WeightedDoc(new Dictionary<string, double> { ["button"] = 1.0 }, 20);
        var stats = new CorpusStats(2, 12, new Dictionary<string, int> { ["button"] = 2 });

        var shortScore = BM25.Score(["button"], shortDoc, stats);
        var longScore = BM25.Score(["button"], longDoc, stats);

        Assert.True(shortScore > longScore);
    }
}
