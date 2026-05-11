// Phase-2 args parser tests for `mur check` (spec 038 §8).
//
// What this file covers:
//   • `--` passthrough split: bare `--` is the boundary; tokens after are
//     forwarded verbatim; unknown mur-flags before `--` are an error.
//   • Default-merging: mur injects `--nologo`, `-v:m`, `-p:Platform=<arch>`
//     only if the user did not name the same flag in passthrough. Detection
//     is by flag name, not value.
//   • Mode flags: --strict / --final / --quiet round-trip through Mode.
//   • --emit-threshold parses and validates float in [0, 1].
//
// CheckArgsTests.cs covers the legacy surface (`--trace`,
// `--suggest-threshold`, positional path).

using Microsoft.UI.Reactor.Cli.Check;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests;

public class ArgsParserTests
{
    // -------- passthrough split --------

    [Fact]
    public void Bare_dashdash_splits_left_and_right()
    {
        Assert.True(ArgsParser.TryParse(new[] { "./app", "--", "-c", "Release" }, out var p, out _));
        Assert.Equal("./app", p.Path);
        Assert.Equal(new[] { "-c", "Release" }, p.Passthrough);
    }

    [Fact]
    public void No_dashdash_means_empty_passthrough()
    {
        Assert.True(ArgsParser.TryParse(new[] { "./app" }, out var p, out _));
        Assert.Empty(p.Passthrough);
    }

    [Fact]
    public void Only_first_dashdash_is_the_boundary_subsequent_are_forwarded()
    {
        // A passthrough section can itself contain `--` tokens (rare but
        // legal in MSBuild's argv). Only the *first* bare `--` is the
        // boundary; later occurrences ride along verbatim.
        Assert.True(ArgsParser.TryParse(new[] { "--", "-c", "--", "Release" }, out var p, out _));
        Assert.Equal(new[] { "-c", "--", "Release" }, p.Passthrough);
    }

    [Fact]
    public void Unknown_mur_flag_before_dashdash_errors_with_typo_hint()
    {
        Assert.False(ArgsParser.TryParse(new[] { "--quie", "--", "-c", "Release" }, out _, out var err));
        Assert.NotNull(err);
        Assert.Contains("--quie", err);
        // Hint that `--` separator may have been forgotten — guards against
        // the canonical "agent typo'd a mur flag and silent-forwarded it to
        // MSBuild" failure mode (spec §8 "Boundary semantics").
        Assert.Contains("--", err);
    }

    // -------- effective build args (default-merging) --------

    [Fact]
    public void Default_args_inject_nologo_v_m_and_platform_when_no_passthrough()
    {
        Assert.True(ArgsParser.TryParse(new[] { "./app" }, out var p, out _));

        Assert.Equal("build", p.EffectiveBuildArgs[0]);
        Assert.Equal("./app", p.EffectiveBuildArgs[1]);
        Assert.Contains("--nologo", p.EffectiveBuildArgs);
        Assert.Contains("-v:m", p.EffectiveBuildArgs);
        // Platform injection is host-arch dependent; assert the *name* is
        // present rather than the exact value so the test passes on both x64
        // and ARM64 CI agents.
        Assert.Contains(p.EffectiveBuildArgs, a => a.StartsWith("-p:Platform=", StringComparison.Ordinal));
    }

    [Fact]
    public void Passthrough_platform_suppresses_default_injection_by_flag_name()
    {
        Assert.True(ArgsParser.TryParse(new[] { "./app", "--", "-p:Platform=x64" }, out var p, out _));

        // Exactly one Platform= present — the user's. mur didn't double-inject.
        var platforms = p.EffectiveBuildArgs.Where(a => a.Contains("Platform=", StringComparison.Ordinal)).ToArray();
        Assert.Single(platforms);
        Assert.Equal("-p:Platform=x64", platforms[0]);
    }

    [Fact]
    public void Passthrough_verbosity_suppresses_default_v_m_injection()
    {
        Assert.True(ArgsParser.TryParse(new[] { "--", "-v:n" }, out var p, out _));

        Assert.Contains("-v:n", p.EffectiveBuildArgs);
        Assert.DoesNotContain("-v:m", p.EffectiveBuildArgs);
    }

    [Fact]
    public void Passthrough_long_verbosity_form_also_suppresses_injection()
    {
        Assert.True(ArgsParser.TryParse(new[] { "--", "--verbosity:diagnostic" }, out var p, out _));

        Assert.DoesNotContain("-v:m", p.EffectiveBuildArgs);
    }

    [Fact]
    public void Passthrough_nologo_suppresses_default_injection()
    {
        Assert.True(ArgsParser.TryParse(new[] { "--", "--nologo" }, out var p, out _));

        // Exactly one --nologo — the user's, not double-injected.
        Assert.Equal(1, p.EffectiveBuildArgs.Count(a => a.Equals("--nologo", StringComparison.OrdinalIgnoreCase) || a.Equals("-nologo", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Slash_prefix_passthrough_flags_are_recognized()
    {
        // MSBuild accepts /flag the same as -flag and --flag. Detection must
        // be prefix-agnostic so `/p:Platform=arm64` also suppresses our
        // default injection.
        Assert.True(ArgsParser.TryParse(new[] { "--", "/p:Platform=arm64" }, out var p, out _));

        var platforms = p.EffectiveBuildArgs.Where(a => a.Contains("Platform=", StringComparison.Ordinal)).ToArray();
        Assert.Single(platforms);
        Assert.Equal("/p:Platform=arm64", platforms[0]);
    }

    [Fact]
    public void Different_property_in_passthrough_does_not_suppress_platform_injection()
    {
        // `-p:DefineConstants=FOO` shares the `-p:` flag with Platform but
        // names a different property — Platform must still be auto-injected.
        Assert.True(ArgsParser.TryParse(new[] { "--", "-p:DefineConstants=FOO" }, out var p, out _));

        Assert.Contains(p.EffectiveBuildArgs, a => a.StartsWith("-p:Platform=", StringComparison.Ordinal));
        Assert.Contains("-p:DefineConstants=FOO", p.EffectiveBuildArgs);
    }

    [Fact]
    public void Multiple_passthrough_properties_pass_through_verbatim_in_order()
    {
        Assert.True(ArgsParser.TryParse(new[] { "--", "-p:Platform=x64", "-p:DefineConstants=FOO" }, out var p, out _));

        Assert.Equal(new[] { "-p:Platform=x64", "-p:DefineConstants=FOO" }, p.Passthrough);

        // Both appear in the effective vector, in passthrough-emit order.
        var idxA = Array.IndexOf(p.EffectiveBuildArgs.ToArray(), "-p:Platform=x64");
        var idxB = Array.IndexOf(p.EffectiveBuildArgs.ToArray(), "-p:DefineConstants=FOO");
        Assert.True(idxA >= 0 && idxB > idxA);
    }

    [Fact]
    public void Release_config_with_no_restore_full_command_round_trip()
    {
        // Spec §8 example: `mur check --final -- -c Release --no-restore`
        Assert.True(ArgsParser.TryParse(new[] { "--final", "--", "-c", "Release", "--no-restore" }, out var p, out _));

        Assert.Equal(Mode.Final, p.Mode);
        Assert.Equal(new[] { "-c", "Release", "--no-restore" }, p.Passthrough);
        // All three of the passthrough tokens survived.
        Assert.Contains("-c", p.EffectiveBuildArgs);
        Assert.Contains("Release", p.EffectiveBuildArgs);
        Assert.Contains("--no-restore", p.EffectiveBuildArgs);
    }

    [Fact]
    public void Non_default_path_with_passthrough()
    {
        // Spec §8 example: `mur check ./MyApp -- -c Release -p:Platform=x64`
        Assert.True(ArgsParser.TryParse(new[] { "./MyApp", "--", "-c", "Release", "-p:Platform=x64" }, out var p, out _));

        Assert.Equal("./MyApp", p.Path);
        Assert.Equal("./MyApp", p.EffectiveBuildArgs[1]);
        Assert.Contains("-p:Platform=x64", p.EffectiveBuildArgs);
        Assert.Equal(1, p.EffectiveBuildArgs.Count(a => a.StartsWith("-p:Platform=", StringComparison.Ordinal)));
    }

    // -------- mode flags --------

    [Fact]
    public void Default_mode_is_iteration()
    {
        Assert.True(ArgsParser.TryParse(Array.Empty<string>(), out var p, out _));
        Assert.Equal(Mode.Iteration, p.Mode);
    }

    [Fact]
    public void Mode_flags_round_trip_through_parser()
    {
        Assert.True(ArgsParser.TryParse(new[] { "--strict" }, out var s, out _));
        Assert.Equal(Mode.Strict, s.Mode);

        Assert.True(ArgsParser.TryParse(new[] { "--final" }, out var f, out _));
        Assert.Equal(Mode.Final, f.Mode);

        Assert.True(ArgsParser.TryParse(new[] { "--quiet" }, out var q, out _));
        Assert.Equal(Mode.Quiet, q.Mode);
    }

    [Fact]
    public void Conflicting_mode_flags_error()
    {
        Assert.False(ArgsParser.TryParse(new[] { "--strict", "--final" }, out _, out var err));
        Assert.NotNull(err);
        Assert.Contains("one of", err);
    }

    // -------- emit threshold --------

    [Fact]
    public void Emit_threshold_default_null_means_ranker_uses_mode_default()
    {
        Assert.True(ArgsParser.TryParse(Array.Empty<string>(), out var p, out _));
        Assert.Null(p.EmitThreshold);
    }

    [Fact]
    public void Emit_threshold_parses_float_in_unit_interval()
    {
        Assert.True(ArgsParser.TryParse(new[] { "--emit-threshold", "0.5" }, out var p, out _));
        Assert.Equal(0.5, p.EmitThreshold);

        Assert.True(ArgsParser.TryParse(new[] { "--emit-threshold", "0" }, out var p0, out _));
        Assert.Equal(0.0, p0.EmitThreshold);

        Assert.True(ArgsParser.TryParse(new[] { "--emit-threshold", "1" }, out var p1, out _));
        Assert.Equal(1.0, p1.EmitThreshold);
    }

    [Fact]
    public void Emit_threshold_rejects_out_of_range_or_garbage()
    {
        Assert.False(ArgsParser.TryParse(new[] { "--emit-threshold", "-0.1" }, out _, out var e1));
        Assert.Contains("0.0", e1!);

        Assert.False(ArgsParser.TryParse(new[] { "--emit-threshold", "1.5" }, out _, out var e2));
        Assert.Contains("0.0", e2!);

        Assert.False(ArgsParser.TryParse(new[] { "--emit-threshold", "many" }, out _, out var e3));
        Assert.Contains("0.0", e3!);
    }

    [Fact]
    public void Emit_threshold_without_value_errors()
    {
        Assert.False(ArgsParser.TryParse(new[] { "--emit-threshold" }, out _, out var err));
        Assert.NotNull(err);
        Assert.Contains("--emit-threshold", err);
    }

    [Fact]
    public void Help_text_mentions_all_new_phase2_flags()
    {
        var help = CheckArgs.HelpText;
        Assert.Contains("--strict", help);
        Assert.Contains("--final", help);
        Assert.Contains("--quiet", help);
        Assert.Contains("--emit-threshold", help);
        Assert.Contains("-- ", help); // the passthrough boundary marker appears in the synopsis
    }

    // -------- Phase 3 rule flags (spec §3.1) --------

    [Fact]
    public void Disable_rule_round_trips_through_parser()
    {
        Assert.True(ArgsParser.TryParse(new[] { "--disable-rule", "FooRule" }, out var p, out _));
        Assert.Equal(new[] { "FooRule" }, p.DisabledRules);
    }

    [Fact]
    public void Disable_rule_is_repeatable_and_preserves_order()
    {
        Assert.True(ArgsParser.TryParse(
            new[] { "--disable-rule", "FooRule", "--disable-rule", "BarRule" },
            out var p, out _));
        Assert.Equal(new[] { "FooRule", "BarRule" }, p.DisabledRules);
    }

    [Fact]
    public void Disable_rule_without_name_errors()
    {
        Assert.False(ArgsParser.TryParse(new[] { "--disable-rule" }, out _, out var err));
        Assert.NotNull(err);
        Assert.Contains("--disable-rule", err);
    }

    [Fact]
    public void Disable_rule_rejects_flag_shaped_name()
    {
        // A name starting with `-` is almost certainly the next flag, not a
        // rule name; reject so a typo like `--disable-rule --quiet` doesn't
        // silently consume the next flag as the rule name.
        Assert.False(ArgsParser.TryParse(
            new[] { "--disable-rule", "--quiet" }, out _, out var err));
        Assert.NotNull(err);
        Assert.Contains("--disable-rule", err);
    }

    [Fact]
    public void List_rules_round_trips_through_parser()
    {
        Assert.True(ArgsParser.TryParse(new[] { "--list-rules" }, out var p, out _));
        Assert.True(p.ListRules);
    }

    [Fact]
    public void Default_args_have_no_disabled_rules_and_list_rules_off()
    {
        Assert.True(ArgsParser.TryParse(Array.Empty<string>(), out var p, out _));
        Assert.Empty(p.DisabledRules);
        Assert.False(p.ListRules);
    }

    [Fact]
    public void Help_text_mentions_phase3_rule_flags()
    {
        var help = CheckArgs.HelpText;
        Assert.Contains("--disable-rule", help);
        Assert.Contains("--list-rules", help);
    }
}
