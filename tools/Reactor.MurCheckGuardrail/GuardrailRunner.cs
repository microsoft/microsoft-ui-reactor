// Pure logic for the suppress→error guardrail (spec 038 §8). Program.cs is
// a thin shell over this. Lifting the logic into a separate type so the
// unit tests can drive it without a process boundary.

using System.Text.Json;
using Microsoft.UI.Reactor.Cli.Check;
using Microsoft.UI.Reactor.Cli.Check.Ranker;

namespace Microsoft.UI.Reactor.MurCheckGuardrail;

internal static class GuardrailRunner
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length != 2)
        {
            stderr.WriteLine("usage: Reactor.MurCheckGuardrail <iter-trace.jsonl> <final-trace.jsonl>");
            return 2;
        }

        var iterPath = args[0];
        var finalPath = args[1];

        if (!File.Exists(iterPath))
        {
            stderr.WriteLine($"iter trace not found: {iterPath}");
            return 2;
        }
        if (!File.Exists(finalPath))
        {
            stderr.WriteLine($"final trace not found: {finalPath}");
            return 2;
        }

        IReadOnlyList<TraceDiag> iterRows;
        IReadOnlyList<TraceDiag> finalRows;
        try
        {
            iterRows = ParseTrace(File.ReadAllLines(iterPath));
            finalRows = ParseTrace(File.ReadAllLines(finalPath));
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"failed to parse trace: {ex.Message}");
            return 2;
        }

        return Audit(iterRows, finalRows, stdout, stderr);
    }

    /// <summary>
    /// Run the audit against two parsed-trace lists. Returns 0 on pass,
    /// 1 on violation. Test seam — unit tests drive this directly.
    /// </summary>
    internal static int Audit(IReadOnlyList<TraceDiag> iterRows, IReadOnlyList<TraceDiag> finalRows, TextWriter stdout, TextWriter stderr)
    {
        // Primary invariant: iteration-suppresses ⇒ not Error in final.
        // Walk the final-trace's error rows and check each code's iteration
        // score at Error severity. Anything <= 0 violates the floor.
        var violations = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in finalRows)
        {
            if (row.Severity != "E") continue;
            if (!seen.Add(row.Code)) continue; // one violation per code

            var iterScore = PolicyTable.Score(row.Code, "error", Mode.Iteration);
            if (iterScore <= 0.0)
            {
                violations.Add(
                    $"PolicyTable suppresses '{row.Code}' in iteration even when surfaced as Error " +
                    $"(score={iterScore:F2}); this code appeared as Error in the --final trace " +
                    $"({Sample(row)}). The 'universal-error floor' rule from spec 038 §8 is broken " +
                    $"— a real build failure would be hidden mid-iteration. Restore the floor or " +
                    $"adjust this row.");
            }
        }

        // Secondary advisory: warnings suppressed in iteration that later
        // surface as errors in final (e.g. -warnaserror upgrade). Not a
        // hard fail — just print so a maintainer can decide whether the
        // suppression is hiding a load-bearing signal.
        var advisories = new List<string>();
        var iterByLocation = iterRows
            .Where(r => r.Severity == "W")
            .ToHashSet(TraceDiag.LocationCodeComparer);
        foreach (var f in finalRows)
        {
            if (f.Severity != "E") continue;
            var warnVariant = new TraceDiag(f.Code, "W", f.File, f.Line, f.Col);
            if (!iterByLocation.Contains(warnVariant)) continue;
            // The same code+location was a Warning in iter and Error in final.
            // Was iteration suppressing it?
            var iterWarnScore = PolicyTable.Score(f.Code, "warning", Mode.Iteration);
            if (iterWarnScore < PolicyTable.IterationThreshold)
            {
                advisories.Add(
                    $"'{f.Code}' was a suppressed warning in iteration (iter-warn score " +
                    $"{iterWarnScore:F2} < threshold {PolicyTable.IterationThreshold:F2}) and " +
                    $"surfaced as Error in --final at {f.File}:{f.Line}:{f.Col}. If this is a " +
                    $"-warnaserror upgrade, the agent never saw the warning that became the error. " +
                    $"Consider whether to raise the iter score for this code.");
            }
        }

        foreach (var a in advisories)
            stdout.WriteLine($"[advisory] {a}");

        if (violations.Count == 0)
        {
            stdout.WriteLine($"guardrail: OK ({finalRows.Count} final rows audited; {advisories.Count} advisory).");
            return 0;
        }

        foreach (var v in violations) stderr.WriteLine($"[violation] {v}");
        stderr.WriteLine($"guardrail: FAIL ({violations.Count} violation(s)).");
        return 1;
    }

    static string Sample(TraceDiag r) => $"{r.File}:{r.Line}:{r.Col}";

    internal static IReadOnlyList<TraceDiag> ParseTrace(IEnumerable<string> lines)
    {
        var rows = new List<TraceDiag>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            // Skip command/meta rows. Diagnostic rows are identified by the
            // presence of the diag schema fields; command rows carry kind.
            if (doc.RootElement.TryGetProperty("kind", out var k) && k.GetString() == "command")
                continue;
            if (!doc.RootElement.TryGetProperty("code", out var codeEl)) continue;
            var code = codeEl.GetString();
            if (string.IsNullOrEmpty(code)) continue;
            var sev = doc.RootElement.GetProperty("severity").GetString() ?? "";
            var file = doc.RootElement.GetProperty("file").GetString() ?? "";
            var line2 = doc.RootElement.GetProperty("line").GetInt32();
            var col = doc.RootElement.GetProperty("col").GetInt32();
            rows.Add(new TraceDiag(code, sev, file, line2, col));
        }
        return rows;
    }
}

internal readonly record struct TraceDiag(string Code, string Severity, string File, int Line, int Col)
{
    public static readonly IEqualityComparer<TraceDiag> LocationCodeComparer = new LocationCodeEq();

    sealed class LocationCodeEq : IEqualityComparer<TraceDiag>
    {
        public bool Equals(TraceDiag x, TraceDiag y) =>
            x.Code == y.Code && x.Severity == y.Severity &&
            x.File == y.File && x.Line == y.Line && x.Col == y.Col;
        public int GetHashCode(TraceDiag o) =>
            HashCode.Combine(o.Code, o.Severity, o.File, o.Line, o.Col);
    }
}
