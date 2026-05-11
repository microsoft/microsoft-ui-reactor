// Per-diagnostic-code confidence thresholds for Tier-2 SymbolSuggester.
// Spec 038 §5 / Phase-1 ship gate (see docs/specs/tasks/038…implementation.md
// → Data Checkpoint B pickup procedure).
//
// Below threshold, the suggester returns Silent. Different CS codes have
// different signal-to-noise — e.g. a near-perfect JaroWinkler on a CS0117
// static-member typo is a much stronger fix-here signal than the same number
// on a CS1503 argument-type mismatch (where the "right answer" is often a
// structural rewrite, not a renamed token).
//
// Default `Default = 0.75` covers any CS code not explicitly listed.
//
// CALIBRATION HISTORY:
//   2026-05-10 — Initial 50-run corpus (51 fixes; 12 hitting handled codes).
//                See docs/specs/tasks/038-tuning-reports/2026-05-10-50run.md
//                for the raw run output. Highlights:
//                  • CS1061: 1 firing at conf 0.43 (no-match in corpus). 0.80
//                    threshold keeps it silent — no false positive observed.
//                  • CS0103: 2 firings at conf 1.00, both matched the human
//                    fix. Holding at 0.75 default — strong signal.
//                  • CS0117 / CS1503 / CS7036: no firings (corpus rows for
//                    these codes either resolved as no-diag against our
//                    stubs or stayed silent). Held at default; revisit at
//                    next corpus drop.
//                Most CS1955 / CS1061 fixes in this corpus are STRUCTURAL
//                rewrites (`.OnClick(x)` → `.Set(...)`, `.Auto()` → `.Auto`),
//                which the SymbolSuggester correctly cannot offer — those
//                are Tier-3 rule territory (Phase 3, blocked on Data
//                Checkpoint C). To regenerate the report:
//                  set MUR_TUNING_CORPUS=<path-to-fixes.jsonl>
//                  dotnet test tests/Reactor.Tests --filter \\
//                    FullyQualifiedName~ThresholdTuningTests.EndToEnd_corpus_run
//
//   2026-05-11 — 525-run corpus (1,027 fixes; 308 hitting handled codes).
//                Single agent (`gpt-5.5`); cross-agent reproducibility bar
//                from spec 037 §11 not met by this drop. See
//                docs/specs/tasks/038-tuning-reports/2026-05-11-525run.md.
//                Findings (harness ClassifyMatch is an over-approximation;
//                "no match" can mean wrong suggestion OR irrelevant-but-not-
//                wrong suggestion after a structural rewrite):
//                  • CS0103: 60 firings, 45 match / 15 no-match (75% match
//                    rate). Confidence cluster at 1.00 + 0.88. Holding at
//                    0.75 — the empirical signal is good and the 15 no-match
//                    cases are plausibly harness under-counts.
//                  • CS1061: 9 firings, 0 match / 9 no-match. Inspecting the
//                    six emissions at conf >= 0.80: all propose receiver
//                    sibling members that don't appear in the human fix.
//                    Pattern: agent writes WinUI-style `.VerticalAlignment(x)`
//                    on TextBlockElement; right answer is Reactor's `.VAlign`
//                    or `.Set(b => b.VerticalAlignment = x)`, but our fuzzy
//                    match picks `TextAlignment`. Fundamentally a Phase-3
//                    rule problem — JaroWinkler can't reach `VAlign` from
//                    `VerticalAlignment` (similarity below the 0.70 floor),
//                    so it picks a wrong sibling. Holding 0.80 for now; the
//                    diagnostic-count gate (default T=3, see CheckCommand
//                    .ShouldEmitSuggestions) shields small/typo-only builds
//                    where wrong CS1061 suggestions would hurt most.
//                  • CS0117: 13 firings, 0 match / 13 no-match. Same shape:
//                    agent writes `Theme.AppBackground` or
//                    `Theme.DefaultBackground` (non-existent); suggester
//                    proposes `Theme.Background`; correct is
//                    `Theme.SolidBackground` (cluster C0019 in patterns.json,
//                    16 events). Holding at 0.75 with gate protection;
//                    Phase-3 rule "Theme.<X>Background → Theme.SolidBackground"
//                    is the high-frequency target.
//                  • CS1503: 0 firings (the two hand-coded heuristics didn't
//                    match any corpus row). Hold default.
//                  • CS7036: 3 firings at conf 0.78, all no-match. Hold
//                    default — parameter-count distance is a weak signal
//                    and Hamming over (kind, type) is a deferred follow-up.
//                Phase-3 priority targets surfaced by this drop (clusters
//                with frequency >= 1% and a clear transformation):
//                  • CS0117 / Theme — "*Background → SolidBackground"
//                    (C0019, 1.6%)
//                  • CS1061 / TextBlockElement — "VerticalAlignment →
//                    VAlign" + "Style → fluent shortcuts" (C0017, 1.2%)
//                  • CS1955 / GridSize — "missing parens on factory call"
//                    (C0004, 10.7%) — but cross-agent reproducibility bar
//                    still applies before authoring.
//
// Tuner override: Reactor.Tests' ThresholdTuningTests scopes `PerCode` to
// its async-local context so concurrent xUnit tests reading thresholds in
// parallel still see the production defaults. Production behaviour is
// unchanged.

namespace Microsoft.UI.Reactor.Cli.Check.Suggesters;

internal static class Thresholds
{
    /// <summary>Fallback for any diagnostic code not explicitly listed.</summary>
    public const double Default = 0.75;

    /// <summary>JaroWinkler floor below which we never propose a candidate.</summary>
    /// <remarks>
    /// Below ~0.7 the candidate is qualitatively unrelated; suggesting it at
    /// any confidence corrupts the agent's reasoning. This is independent
    /// from the per-code emit threshold and is enforced inside the suggester.
    /// </remarks>
    public const double SimilarityFloor = 0.70;

    static readonly IReadOnlyDictionary<string, double> _defaultPerCode = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        // CS1061 (member missing). The 50-run corpus shows most CS1061 fixes
        // are structural rewrites (`.HorizontalAlignment(...)` → `.Set(...)`),
        // not member renames. Conservative default reduces wrong-direction
        // suggestions; the OnClick / Bar (synthetic) cases still clear it.
        ["CS1061"] = 0.80,

        // CS0103 (name not in scope). Strongest case for fuzzy match: the
        // user mistyped a factory name. Held at default; tune up if the
        // larger corpus shows false positives.
        ["CS0103"] = 0.75,

        // CS0117 (no static member on type). Same reasoning as CS0103 —
        // typo on an enum/constant member is the canonical case.
        ["CS0117"] = 0.75,

        // CS1503 (argument type mismatch). Suggester only fires on two
        // hand-coded heuristics (Element-vs-string, Action-vs-Action<T>).
        // Both are quite specific so the default is fine.
        ["CS1503"] = 0.75,

        // CS7036 (no overload takes N args). We rank overloads by parameter
        // count distance only (full Hamming over (kind, type) is a deferred
        // follow-up). Held at default until corpus signal arrives.
        ["CS7036"] = 0.75,
    };

    // The override is async-local rather than static-mutable so the threshold
    // tuning harness can scope a zeroed map to its own logical thread without
    // racing against other xUnit tests reading `For(code)` in parallel.
    static readonly AsyncLocal<IReadOnlyDictionary<string, double>?> _override = new();

    /// <summary>Threshold for a given diagnostic code, or Default if not listed.</summary>
    public static double For(string code)
    {
        var map = _override.Value ?? _defaultPerCode;
        return map.TryGetValue(code, out var t) ? t : Default;
    }

    /// <summary>
    /// Test-only override. Used by the threshold-tuning harness to capture
    /// raw confidences across all codes. Production code paths must not
    /// call this. The override is async-local — it only applies to the
    /// calling logical thread, so concurrent tests are unaffected.
    /// </summary>
    internal static IReadOnlyDictionary<string, double> PerCode
    {
        get => _override.Value ?? _defaultPerCode;
        set => _override.Value = value;
    }
}
