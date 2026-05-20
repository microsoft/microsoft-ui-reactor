#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Find;

internal partial class SearchEngine
{
    private readonly ScenarioCatalogue _catalogue;
    private readonly CorpusStats _stats;
    private readonly ScenarioEntry[] _entries;

    public SearchEngine(ScenarioCatalogue catalogue)
    {
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        _entries = catalogue.Scenarios.Select(CreateEntry).ToArray();
        _stats = BuildStats(_entries.Select(entry => entry.ScenarioDoc));
    }

    public SearchResult[] Search(string query, int maxResults = 5, string? category = null, bool includeAntiPatterns = false)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (maxResults <= 0 || _catalogue.Scenarios.Length == 0)
        {
            return [];
        }

        var queryTerms = Synonyms.ProcessQuery(query);
        if (queryTerms.Length == 0)
        {
            return [];
        }

        var normalizedCategory = string.IsNullOrWhiteSpace(category)
            ? null
            : category.Trim();

        var filteredEntries = _entries.Where(entry =>
            (includeAntiPatterns || !string.Equals(entry.Scenario.Priority, "anti-pattern", StringComparison.OrdinalIgnoreCase)) &&
            (normalizedCategory is null || string.Equals(entry.Scenario.Category, normalizedCategory, StringComparison.OrdinalIgnoreCase)));

        var filteredByFactory = filteredEntries
            .GroupBy(entry => entry.Factory, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        if (filteredByFactory.Count == 0)
        {
            return [];
        }

        var factoryStats = BuildStats(filteredByFactory.Values.Select(entries => MergeDocs(entries.Select(entry => entry.FactoryDoc))));
        var rawTerms = Tokenize(query);

        var topFactories = filteredByFactory
            .Select(pair =>
            {
                var factoryDoc = MergeDocs(pair.Value.Select(entry => entry.FactoryDoc));
                var score = BM25.Score(queryTerms, factoryDoc, factoryStats);
                if (score > 0.0 && rawTerms.Contains(pair.Key, StringComparer.Ordinal))
                {
                    score *= 2.0;
                }

                return new FactoryScore(pair.Key, score);
            })
            .Where(item => item.Score > 0.0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Factory, StringComparer.Ordinal)
            .Take(Math.Max(maxResults, 5))
            .ToArray();

        if (topFactories.Length == 0)
        {
            return [];
        }

        var scenarioStats = includeAntiPatterns && normalizedCategory is null
            ? _stats
            : BuildStats(filteredByFactory.Values.SelectMany(entries => entries).Select(entry => entry.ScenarioDoc));

        var results = topFactories
            .SelectMany(factory => filteredByFactory[factory.Factory])
            .Select(entry => new SearchResult(entry.Scenario, BM25.Score(queryTerms, entry.ScenarioDoc, scenarioStats)))
            .Where(result => result.Score > 0.0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Scenario.Title, StringComparer.Ordinal)
            .Take(maxResults)
            .ToArray();

        return results;
    }

    private static ScenarioEntry CreateEntry(Scenario scenario)
    {
        var factory = scenario.FactoryAnchors.FirstOrDefault() ?? string.Empty;
        var factoryDoc = BuildWeightedDoc(
            [
                (scenario.FactoryAnchors, 3.0),
                (scenario.Tags, 3.0),
                (new[] { scenario.Title }, 2.0),
                (new[] { scenario.Intent }, 1.5)
            ]);
        var scenarioDoc = BuildWeightedDoc(
            [
                (scenario.Tags, 1.0),
                (new[] { scenario.Title }, 1.0),
                (new[] { scenario.Intent }, 1.0)
            ]);

        return new ScenarioEntry(scenario, factory, factoryDoc, scenarioDoc);
    }

    private static WeightedDoc BuildWeightedDoc((IEnumerable<string> Values, double Weight)[] fields)
    {
        var termWeights = new Dictionary<string, double>(StringComparer.Ordinal);
        var docLength = 0;

        foreach (var (values, weight) in fields)
        {
            foreach (var value in values)
            {
                foreach (var term in Tokenize(value))
                {
                    termWeights[term] = termWeights.TryGetValue(term, out var existing)
                        ? existing + weight
                        : weight;
                    docLength++;
                }
            }
        }

        return new WeightedDoc(termWeights, docLength);
    }

    private static WeightedDoc MergeDocs(IEnumerable<WeightedDoc> docs)
    {
        var mergedWeights = new Dictionary<string, double>(StringComparer.Ordinal);
        var docLength = 0;

        foreach (var doc in docs)
        {
            docLength += doc.DocLength;
            foreach (var (term, weight) in doc.TermWeights)
            {
                mergedWeights[term] = mergedWeights.TryGetValue(term, out var existing)
                    ? existing + weight
                    : weight;
            }
        }

        return new WeightedDoc(mergedWeights, docLength);
    }

    private static CorpusStats BuildStats(IEnumerable<WeightedDoc> docs)
    {
        var docCount = 0;
        var totalDocLength = 0;
        var docFrequency = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var doc in docs)
        {
            docCount++;
            totalDocLength += doc.DocLength;

            foreach (var term in doc.TermWeights.Keys)
            {
                docFrequency[term] = docFrequency.TryGetValue(term, out var count)
                    ? count + 1
                    : 1;
            }
        }

        var avgDocLength = docCount == 0 ? 0.0 : (double)totalDocLength / docCount;
        return new CorpusStats(docCount, avgDocLength, docFrequency);
    }

    private static string[] Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return TokenRegex()
            .Matches(text.ToLowerInvariant())
            .Cast<Match>()
            .Select(match => match.Value)
            .Where(term => term.Length > 0 && !StopWords.IsStopWord(term))
            .ToArray();
    }

    [GeneratedRegex("[a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    private sealed record ScenarioEntry(Scenario Scenario, string Factory, WeightedDoc FactoryDoc, WeightedDoc ScenarioDoc);

    private sealed record FactoryScore(string Factory, double Score);
}
