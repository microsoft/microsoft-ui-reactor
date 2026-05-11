// Spec 038 §8 — base_policy(code) table for the deterministic pre-emit ranker.
//
// The table answers "given a diagnostic code, what is its emit-worthiness?"
// for each mur-check mode. Score range is [0.0, 1.0]:
//   1.0 → always emit
//   0.0 → never emit (in this mode)
//   in between → conditionally emit depending on threshold
//
// Iteration threshold defaults to ≥ 0.6; final to ≥ 0.0 (everything). Tunable
// via `mur check --emit-threshold` — ArgsParser surfaces the flag; the Ranker
// reads the parsed value.
//
// Keep this table compact. The Phase-4 learned ranker (§7) is the place for
// nuance — this table is the always-emit / never-emit floor that the model
// builds on top of. Twelve well-chosen rows beat fifty noisy ones.

using System.Globalization;

namespace Microsoft.UI.Reactor.Cli.Check.Ranker;

internal static class PolicyTable
{
    /// <summary>Default iteration-mode threshold. Spec §8.</summary>
    public const double IterationThreshold = 0.6;

    /// <summary>Default final-mode threshold. Spec §8.</summary>
    public const double FinalThreshold = 0.0;

    /// <summary>Default strict-mode threshold (same as iteration; the
    /// difference is that strict promotes Warning → Error first).</summary>
    public const double StrictThreshold = 0.6;

    /// <summary>
    /// Quiet mode: any non-Error diagnostic scores 0 regardless of code, so
    /// the threshold for quiet is effectively "must be E severity AND must
    /// be > 0 in the table" — see <see cref="Ranker.Score"/>.
    /// </summary>
    public const double QuietThreshold = 0.6;

    /// <summary>
    /// Lookup: (code, severity, mode) → 0.0..1.0 base-policy score. Anything
    /// not explicitly listed falls through to the "Unknown" tier (0.5 in
    /// iteration / strict / quiet; 1.0 in final). The conservative-unknown
    /// default is per spec §8: better to over-emit a novel code than hide a
    /// real bug behind silence.
    /// </summary>
    public static double Score(string code, string severity, Mode mode)
    {
        // Strict: promote Warning → Error before lookup. The "more aggressive
        // of strict / -p:TreatWarningsAsErrors=true wins" rule (spec §8) is
        // satisfied because strict NEVER lowers a severity — it can only raise.
        if (mode == Mode.Strict && severity == "warning") severity = "error";

        var (iter, final) = LookupBase(code, severity);

        return mode switch
        {
            Mode.Final => final,
            // Quiet emits errors only. A non-error always scores 0 regardless
            // of base_policy. Errors keep their iteration score, which is
            // 1.0 for any CS / REACTOR_* / unknown error.
            Mode.Quiet => severity == "error" ? iter : 0.0,
            // Strict and iteration share the per-row "iter" column; the
            // difference is the severity promotion above.
            _ => iter,
        };
    }

    /// <summary>
    /// Threshold below which a diagnostic is suppressed in the given mode.
    /// User --emit-threshold overrides; passed in here as <paramref name="userOverride"/>.
    /// </summary>
    public static double ThresholdFor(Mode mode, double? userOverride)
    {
        if (userOverride is { } u) return u;
        return mode switch
        {
            Mode.Final => FinalThreshold,
            Mode.Strict => StrictThreshold,
            Mode.Quiet => QuietThreshold,
            _ => IterationThreshold,
        };
    }

    static (double Iter, double Final) LookupBase(string code, string severity)
    {
        // Universal floor: any diagnostic that surfaces as Error severity
        // always emits, regardless of which row in the table it would have
        // matched. An "unused variable" surfaced under -warnaserror is a
        // real gate, not noise. This rule is what makes the table safe to
        // edit — adding a (0.0, 0.7) row can't hide a real build break.
        if (severity == "error") return (1.0, 1.0);

        // Order below: most-specific first. Some rows are id-prefix; others
        // are id-range; the unknown fallback is at the bottom.

        // CS nullable warnings: noisy mid-iteration, useful in final.
        if (IsNullableCode(code)) return (0.3, 1.0);

        // Exact id matches first (cheaper than prefix scans for common codes).
        switch (code)
        {
            case "CS0168":  // unused variable
                return (0.0, 0.7);
            case "CS1591":  // missing XML doc
                return (0.0, 0.5);
            case "NU1701":
            case "NU1605":
                return (0.0, 0.6);
            case "MSB3245":
            case "MSB3270":
            case "MSB3277":
                return (0.0, 0.4);
        }

        // IDE0xxx — style hints. Pattern: IDE followed by 4 digits.
        if (code.StartsWith("IDE", StringComparison.Ordinal) && code.Length >= 4 && char.IsDigit(code[3]))
            return (0.0, 0.3);

        // REACTOR_* — the Reactor analyzer family. Severity gates which row.
        if (code.StartsWith("REACTOR_", StringComparison.Ordinal))
        {
            // Errors handled by the universal floor above; warnings here.
            return severity == "warning"
                ? (0.9, 1.0)   // Hooks / A11y / Theme correctness-adjacent
                : (0.2, 1.0);  // Info/heuristic
        }

        // Unknown. Conservative: surface unknowns by default in iteration too
        // — better over-emit than hide a novel real bug. The score sits
        // *below* the default 0.6 threshold so the gate decides, but users
        // who want surfacing can lower --emit-threshold to 0.5.
        return (0.5, 1.0);
    }

    /// <summary>
    /// CS8600–CS8625 are the nullable-reference family. Range check is faster
    /// than a switch with 26 entries and self-documenting.
    /// </summary>
    static bool IsNullableCode(string code)
    {
        if (code.Length != 6) return false;
        if (code[0] != 'C' || code[1] != 'S') return false;
        if (!int.TryParse(code.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return false;
        return n >= 8600 && n <= 8625;
    }
}
