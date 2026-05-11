// Local-first, opt-in telemetry for `mur check`. Spec 038 §10 + §1.7.
//
// Behaviour:
//   • Disabled by default; opted in via env var `MUR_TELEMETRY=1`.
//   • Appends one JSON row per emitted-suggestion event to
//     ~/.mur/telemetry/<yyyy-mm-dd>.jsonl.
//   • Payload: { ts, code, suggester, confidence, evidence_short, rule? }.
//   • Source code text, file paths, machine identifiers — none of these are
//     ever logged. Every field that could plausibly carry user content is
//     bounded to 256 bytes.

using System.Text;
using System.Text.Json;

namespace Microsoft.UI.Reactor.Cli.Check;

internal static class Telemetry
{
    public const string OptInEnv = "MUR_TELEMETRY";
    public const int MaxFieldBytes = 256;

    public static bool IsEnabled => Environment.GetEnvironmentVariable(OptInEnv) == "1";

    /// <summary>
    /// Default telemetry directory: ~/.mur/telemetry/. Tests override via
    /// <see cref="WriteEvent"/>'s <c>directory</c> parameter to keep $HOME
    /// untouched.
    /// </summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".mur", "telemetry");

    public static void OnSuggestionEmitted(string code, Suggestion suggestion, string? directory = null)
    {
        if (!IsEnabled) return;
        try { WriteEvent(code, suggestion, directory ?? DefaultDirectory); }
        catch { /* best-effort; never throw out of telemetry */ }
    }

    internal static void WriteEvent(string code, Suggestion suggestion, string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyy-MM-dd") + ".jsonl");

        var row = new TelemetryRow(
            ts: DateTime.UtcNow.ToString("o"),
            code: Truncate(code),
            suggester: Truncate(suggestion.SuggesterName),
            confidence: suggestion.Confidence,
            evidence_short: Truncate(suggestion.Evidence));

        var json = JsonSerializer.Serialize(row);
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var sw = new StreamWriter(fs);
        sw.WriteLine(json);
    }

    static string Truncate(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        // Bound is byte-count, not char-count: non-ASCII content can push past
        // MaxFieldBytes in UTF-8 even when char length is under the limit.
        // Cheap path first — pure-ASCII strings have byte == char count.
        if (s.Length <= MaxFieldBytes && Encoding.UTF8.GetByteCount(s) <= MaxFieldBytes)
            return s;
        // Truncate on a char boundary that fits MaxFieldBytes in UTF-8. Walk
        // down from the char-length upper bound; each iteration shaves one
        // char until the byte budget is satisfied.
        int chars = Math.Min(s.Length, MaxFieldBytes);
        while (chars > 0 && Encoding.UTF8.GetByteCount(s.AsSpan(0, chars)) > MaxFieldBytes)
            chars--;
        return s[..chars];
    }

    internal sealed record TelemetryRow(
        string ts,
        string code,
        string suggester,
        double confidence,
        string evidence_short);
}
