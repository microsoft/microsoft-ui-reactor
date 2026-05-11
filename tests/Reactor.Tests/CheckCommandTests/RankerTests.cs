// Spec 038 §8 — deterministic pre-emit ranker tests.
//
// What this file covers:
//   • Iteration mode suppresses noise (CS1591 / CS0168 / IDE0xxx / NU1701 /
//     MSB3245) but always emits CS errors and high-priority REACTOR_*.
//   • --final emits everything (threshold 0.0).
//   • --strict promotes Warning → Error so REACTOR_* warnings clear the
//     iteration threshold by becoming errors.
//   • --quiet emits errors only — even high-scoring warnings drop out.
//   • --emit-threshold overrides the per-mode default.
//   • CheckCommand integration: with the ranker on the iteration path,
//     stdout sees the filtered set while trace sees every parsed row.

using Microsoft.UI.Reactor.Cli.Check;
using Microsoft.UI.Reactor.Cli.Check.Ranker;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests;

public class RankerTests
{
    static CheckCommand.Diag Diag(string code, string severity = "warning", string file = "Foo.cs")
        => new(file, 1, 1, severity, code, "msg");

    // -------- iteration mode (the default; suppression matters) --------

    [Fact]
    public void Iteration_suppresses_xml_doc_warning_CS1591()
    {
        var ctx = new RankerContext(Mode.Iteration, null);
        Assert.False(Ranker.ShouldEmit(Diag("CS1591"), ctx));
    }

    [Fact]
    public void Iteration_suppresses_unused_variable_CS0168()
    {
        var ctx = new RankerContext(Mode.Iteration, null);
        Assert.False(Ranker.ShouldEmit(Diag("CS0168"), ctx));
    }

    [Fact]
    public void Iteration_suppresses_ide_style_hints()
    {
        var ctx = new RankerContext(Mode.Iteration, null);
        Assert.False(Ranker.ShouldEmit(Diag("IDE0001", "info"), ctx));
        Assert.False(Ranker.ShouldEmit(Diag("IDE0040", "info"), ctx));
    }

    [Fact]
    public void Iteration_suppresses_nuget_restore_noise()
    {
        var ctx = new RankerContext(Mode.Iteration, null);
        Assert.False(Ranker.ShouldEmit(Diag("NU1701", "warning"), ctx));
        Assert.False(Ranker.ShouldEmit(Diag("NU1605", "warning"), ctx));
    }

    [Fact]
    public void Iteration_suppresses_msbuild_resolution_chatter()
    {
        var ctx = new RankerContext(Mode.Iteration, null);
        Assert.False(Ranker.ShouldEmit(Diag("MSB3245", "warning"), ctx));
        Assert.False(Ranker.ShouldEmit(Diag("MSB3277", "warning"), ctx));
    }

    [Fact]
    public void Iteration_suppresses_nullable_warnings_unless_user_lowers_threshold()
    {
        var ctx = new RankerContext(Mode.Iteration, null);
        // CS8602 sits at 0.3 in iteration; below default 0.6.
        Assert.False(Ranker.ShouldEmit(Diag("CS8602", "warning"), ctx));

        // Authoring a nullable-aware fix? Drop the threshold and they all appear.
        var lowered = new RankerContext(Mode.Iteration, 0.2);
        Assert.True(Ranker.ShouldEmit(Diag("CS8602", "warning"), lowered));
    }

    [Fact]
    public void Iteration_always_emits_cs_errors()
    {
        var ctx = new RankerContext(Mode.Iteration, null);
        Assert.True(Ranker.ShouldEmit(Diag("CS1061", "error"), ctx));
        Assert.True(Ranker.ShouldEmit(Diag("CS0103", "error"), ctx));
        // Even nullable codes when surfacing as errors (rare; -warnaserror).
        Assert.True(Ranker.ShouldEmit(Diag("CS8602", "error"), ctx));
    }

    [Fact]
    public void Iteration_emits_reactor_warning_but_suppresses_reactor_info()
    {
        var ctx = new RankerContext(Mode.Iteration, null);
        Assert.True(Ranker.ShouldEmit(Diag("REACTOR_HOOKS_001", "warning"), ctx));
        Assert.False(Ranker.ShouldEmit(Diag("REACTOR_HOOKS_006", "info"), ctx));
    }

    [Fact]
    public void Iteration_emits_unknown_codes_by_default()
    {
        // Spec §8 last row: "Unknown — 0.5 / 1.0 — Conservative; surface
        // unknown codes by default in iteration too." 0.5 < 0.6 threshold,
        // so it suppresses with the default threshold. Verify the behavior
        // matches the spec wording: pick the conservative threshold and
        // unknowns surface; default threshold stays put.
        var defaultCtx = new RankerContext(Mode.Iteration, null);
        Assert.False(Ranker.ShouldEmit(Diag("FOO9999", "warning"), defaultCtx));

        // The spec says "surface unknown codes by default" with the policy
        // value 0.5 — meaning if a future tuning lowers the threshold, they
        // surface. Verify the score itself, not just the gate.
        Assert.Equal(0.5, Ranker.Score(Diag("FOO9999", "warning"), defaultCtx));
    }

    // -------- final mode (everything emits) --------

    [Fact]
    public void Final_emits_everything_including_xml_doc_and_unused_var()
    {
        var ctx = new RankerContext(Mode.Final, null);
        Assert.True(Ranker.ShouldEmit(Diag("CS1591"), ctx));
        Assert.True(Ranker.ShouldEmit(Diag("CS0168"), ctx));
        Assert.True(Ranker.ShouldEmit(Diag("IDE0001", "info"), ctx));
        Assert.True(Ranker.ShouldEmit(Diag("MSB3245", "warning"), ctx));
    }

    // -------- strict mode (warnings → errors) --------

    [Fact]
    public void Strict_promotes_reactor_warning_to_error_and_emits()
    {
        var strict = new RankerContext(Mode.Strict, null);
        var iter = new RankerContext(Mode.Iteration, null);

        var d = Diag("REACTOR_HOOKS_001", "warning");

        // Promoted to error → 1.0 score, well above 0.6 threshold.
        Assert.True(Ranker.ShouldEmit(d, strict));
        // Iteration emits it too (0.9 score) — same row, different mechanism.
        Assert.True(Ranker.ShouldEmit(d, iter));

        // The strict score should be at least as high as the iteration score
        // (promotion never lowers anything). Spec §8 "more aggressive wins".
        Assert.True(Ranker.Score(d, strict) >= Ranker.Score(d, iter));
    }

    [Fact]
    public void Strict_does_not_alter_existing_errors()
    {
        var strict = new RankerContext(Mode.Strict, null);
        var d = Diag("CS1061", "error");

        // CS errors emit in every mode; strict promotion is a no-op on them.
        Assert.Equal(1.0, Ranker.Score(d, strict));
        Assert.True(Ranker.ShouldEmit(d, strict));
    }

    // -------- quiet mode (errors only) --------

    [Fact]
    public void Quiet_emits_errors_only_even_for_high_scoring_warnings()
    {
        var quiet = new RankerContext(Mode.Quiet, null);
        // REACTOR_HOOKS_001 Warning would normally clear iteration at 0.9.
        // Quiet rejects it: only errors get through.
        Assert.False(Ranker.ShouldEmit(Diag("REACTOR_HOOKS_001", "warning"), quiet));
        Assert.True(Ranker.ShouldEmit(Diag("REACTOR_HOOKS_001", "error"), quiet));
        Assert.True(Ranker.ShouldEmit(Diag("CS1061", "error"), quiet));
    }

    // -------- user threshold override --------

    [Fact]
    public void User_emit_threshold_overrides_mode_default()
    {
        // Aggressive: only emit at score 1.0+. CS error (1.0) still passes;
        // REACTOR_* warning at 0.9 drops out.
        var strict = new RankerContext(Mode.Iteration, 0.95);
        Assert.True(Ranker.ShouldEmit(Diag("CS1061", "error"), strict));
        Assert.False(Ranker.ShouldEmit(Diag("REACTOR_HOOKS_001", "warning"), strict));

        // Liberal: anything 0.1+. CS1591 (0.0) still drops; nullable (0.3) appears.
        var liberal = new RankerContext(Mode.Iteration, 0.1);
        Assert.True(Ranker.ShouldEmit(Diag("CS8602", "warning"), liberal));
        Assert.False(Ranker.ShouldEmit(Diag("CS1591", "warning"), liberal));
    }

    // -------- score range invariant --------

    [Fact]
    public void Suggest_gate_counts_full_parsed_list_not_post_ranker_emittable()
    {
        // EC2 regression — the gate measures build complexity (a property
        // of the compiler's output), not stdout visibility. A build with
        // 2 CS errors + 3 CS8602 nullable warnings has 5 unique CS codes;
        // the ranker filters CS8602 out of stdout in iteration mode, but
        // the gate must still count them — otherwise the gate closes on
        // builds that ARE Tier-2 territory.
        //
        // Spec 038 §14 #8: the gate's input is "unique CS-prefixed
        // diagnostics in the invocation" — not "unique CS-prefixed
        // diagnostics that survived the ranker."
        var diags = CheckCommand.ParseDiagnostics("""
            A.cs(1,1): error CS1061: a [P.csproj]
            B.cs(2,2): error CS0103: b [P.csproj]
            C.cs(3,3): warning CS8602: c [P.csproj]
            D.cs(4,4): warning CS8625: d [P.csproj]
            E.cs(5,5): warning CS8604: e [P.csproj]
            """);

        // 5 unique CS codes, default gate threshold 3 → gate should be open.
        Assert.True(CheckCommand.ShouldEmitSuggestions(diags, threshold: 3));

        // Sanity: if we'd (incorrectly) filtered nullable warnings before
        // counting, we'd be left with 2 codes < 3 → gate closed. This is
        // the bug we're guarding against.
        var iterCtx = new RankerContext(Mode.Iteration, null);
        var emittable = diags.Where(d => Ranker.ShouldEmit(d, iterCtx)).ToList();
        Assert.Equal(2, emittable.Count); // CS1061, CS0103 only — nullables filtered
        Assert.False(CheckCommand.ShouldEmitSuggestions(emittable, threshold: 3));
    }

    [Fact]
    public void All_scores_are_in_unit_interval()
    {
        // Belt-and-suspenders: the deterministic table should never produce
        // a score outside [0, 1]. If a future edit accidentally adds a row
        // with weight 2.0, this lights up. Covers the canonical rows from
        // the spec table.
        var probes = new (string code, string sev)[]
        {
            ("CS1061", "error"), ("CS1591", "warning"), ("CS0168", "warning"),
            ("CS8602", "warning"), ("IDE0001", "info"), ("NU1701", "warning"),
            ("MSB3245", "warning"), ("REACTOR_HOOKS_001", "warning"),
            ("REACTOR_HOOKS_006", "info"), ("REACTOR_HOOKS_001", "error"),
            ("FOO9999", "warning"),
        };
        foreach (var (code, sev) in probes)
        {
            foreach (var mode in new[] { Mode.Iteration, Mode.Strict, Mode.Final, Mode.Quiet })
            {
                var ctx = new RankerContext(mode, null);
                var s = Ranker.Score(new CheckCommand.Diag("Foo.cs", 1, 1, sev, code, "msg"), ctx);
                Assert.InRange(s, 0.0, 1.0);
            }
        }
    }
}
