// Jaro–Winkler similarity for fuzzy member-name matching. Pure (no I/O, no
// shared state). Allocates two short bool[]s per Jaro call — fine at our call
// volume (≤ a few hundred candidates per diagnostic); revisit with stackalloc
// if perf-trait tests show it on the hot path. Spec 038 §5 confidence formula:
// JW ≥ 0.85 → 1.0; below 0.7 → 0.0; the suggester rescales these into a
// confidence score.

namespace Microsoft.UI.Reactor.Cli.Check.Suggesters;

internal static class StringSimilarity
{
    /// <summary>
    /// Jaro–Winkler similarity in [0, 1]. Higher is more similar.
    /// Case-sensitive. Caller is expected to normalize casing.
    /// </summary>
    public static double JaroWinkler(string s, string t, double prefixScale = 0.1)
    {
        if (s.Length == 0 && t.Length == 0) return 1.0;
        if (s.Length == 0 || t.Length == 0) return 0.0;

        var jaro = Jaro(s, t);
        if (jaro <= 0.0) return 0.0;

        // Common prefix up to 4 characters.
        int prefix = 0;
        var max = Math.Min(4, Math.Min(s.Length, t.Length));
        for (int i = 0; i < max; i++)
        {
            if (s[i] != t[i]) break;
            prefix++;
        }

        return jaro + prefix * prefixScale * (1.0 - jaro);
    }

    static double Jaro(string s, string t)
    {
        int sLen = s.Length;
        int tLen = t.Length;
        if (sLen == 0 || tLen == 0) return 0.0;

        int matchWindow = Math.Max(0, Math.Max(sLen, tLen) / 2 - 1);
        var sMatched = new bool[sLen];
        var tMatched = new bool[tLen];

        int matches = 0;
        for (int i = 0; i < sLen; i++)
        {
            int from = Math.Max(0, i - matchWindow);
            int to = Math.Min(i + matchWindow + 1, tLen);
            for (int j = from; j < to; j++)
            {
                if (tMatched[j]) continue;
                if (s[i] != t[j]) continue;
                sMatched[i] = true;
                tMatched[j] = true;
                matches++;
                break;
            }
        }
        if (matches == 0) return 0.0;

        int transpositions = 0;
        int k = 0;
        for (int i = 0; i < sLen; i++)
        {
            if (!sMatched[i]) continue;
            while (!tMatched[k]) k++;
            if (s[i] != t[k]) transpositions++;
            k++;
        }

        double m = matches;
        return (m / sLen + m / tLen + (m - transpositions / 2.0) / m) / 3.0;
    }
}
