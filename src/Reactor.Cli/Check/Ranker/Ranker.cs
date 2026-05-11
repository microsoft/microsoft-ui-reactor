// Spec 038 §8 — deterministic pre-emit ranker.
//
// Ranker.Score(diag, mode, ctx) → 0.0..1.0. The Phase-2 ranker is the
// PolicyTable lookup unchanged; the other terms in the spec §8 formula
// (severity_weight, location_weight, recency_weight, accept_history) all
// require signals that aren't available in this phase:
//   • severity_weight is already encoded in the table's per-severity rows.
//   • location_weight needs user/generated/nuget-cache file categorisation.
//   • recency_weight needs turn-tracking state across invocations.
//   • accept_history needs the telemetry pipeline (Phase 4).
//
// Those terms ship in Phase 4 when the learned ranker arrives; for Phase 2
// the deterministic table IS the score. ShouldEmit applies the threshold
// (mode default or user-overridden) and is what CheckCommand calls.

namespace Microsoft.UI.Reactor.Cli.Check.Ranker;

internal readonly record struct RankerContext(Mode Mode, double? UserEmitThreshold)
{
    public double EffectiveThreshold => PolicyTable.ThresholdFor(Mode, UserEmitThreshold);
}

internal static class Ranker
{
    /// <summary>0.0..1.0 emit-worthiness. Phase-2: pure PolicyTable lookup.</summary>
    public static double Score(CheckCommand.Diag d, in RankerContext ctx)
        => PolicyTable.Score(d.Code, d.Severity, ctx.Mode);

    /// <summary>True if the diagnostic should reach stdout in this mode.</summary>
    public static bool ShouldEmit(CheckCommand.Diag d, in RankerContext ctx)
        => Score(d, ctx) >= ctx.EffectiveThreshold;
}
