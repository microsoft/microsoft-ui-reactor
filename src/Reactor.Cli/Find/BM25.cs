#nullable enable

using System;
using System.Collections.Generic;

namespace Microsoft.UI.Reactor.Cli.Find;

internal static class BM25
{
    private const double K1 = 1.2;
    private const double B = 0.75;

    public static double Score(string[] queryTerms, WeightedDoc doc, CorpusStats stats)
    {
        ArgumentNullException.ThrowIfNull(queryTerms);
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(stats);

        if (queryTerms.Length == 0 || stats.DocCount <= 0 || doc.DocLength <= 0 || stats.AvgDocLength <= 0)
        {
            return 0.0;
        }

        var score = 0.0;
        var norm = K1 * (1.0 - B + B * (doc.DocLength / stats.AvgDocLength));

        foreach (var term in queryTerms)
        {
            if (!doc.TermWeights.TryGetValue(term, out var tf) || tf <= 0.0)
            {
                continue;
            }

            stats.DocFrequency.TryGetValue(term, out var n);
            var idf = Math.Log(((stats.DocCount - n + 0.5) / (n + 0.5)) + 1.0);
            score += idf * ((tf * (K1 + 1.0)) / (tf + norm));
        }

        return score;
    }
}

internal record WeightedDoc(Dictionary<string, double> TermWeights, int DocLength);

internal record CorpusStats(int DocCount, double AvgDocLength, Dictionary<string, int> DocFrequency);
