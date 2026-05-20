#nullable enable

using System.Collections.Frozen;

namespace Microsoft.UI.Reactor.Cli.Find;

internal static class StopWords
{
    private static readonly FrozenSet<string> _set = new[]
    {
        "a",
        "an",
        "and",
        "are",
        "as",
        "at",
        "be",
        "been",
        "by",
        "can",
        "could",
        "did",
        "do",
        "does",
        "for",
        "from",
        "had",
        "has",
        "have",
        "hook",
        "in",
        "is",
        "it",
        "may",
        "might",
        "of",
        "on",
        "or",
        "reactor",
        "should",
        "that",
        "the",
        "this",
        "to",
        "was",
        "were",
        "will",
        "with",
        "would",
        "element",
        "factory"
    }.ToFrozenSet();

    public static bool IsStopWord(string term) => _set.Contains(term);
}
