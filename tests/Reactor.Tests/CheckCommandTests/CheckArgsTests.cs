// Phase-0 args parser tests for `mur check`. Spec: docs/specs/038-...
// §0.3 (--trace) and §0.5 (no regression in existing arg handling).

using Microsoft.UI.Reactor.Cli.Check;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests;

public class CheckArgsTests
{
    [Fact]
    public void Empty_args_default_path_to_dot()
    {
        Assert.True(CheckArgs.TryParse(Array.Empty<string>(), out var parsed, out var err));
        Assert.Null(err);
        Assert.Equal(".", parsed.Path);
        Assert.Null(parsed.TracePath);
    }

    [Fact]
    public void Single_positional_path_is_the_project_path()
    {
        Assert.True(CheckArgs.TryParse(new[] { "./MyApp" }, out var parsed, out _));
        Assert.Equal("./MyApp", parsed.Path);
        Assert.Null(parsed.TracePath);
    }

    [Fact]
    public void Trace_flag_consumes_next_token_as_path()
    {
        Assert.True(CheckArgs.TryParse(new[] { "--trace", "C:/tmp/x.jsonl" }, out var parsed, out _));
        Assert.Equal(".", parsed.Path);
        Assert.Equal("C:/tmp/x.jsonl", parsed.TracePath);
    }

    [Fact]
    public void Trace_flag_with_path_in_either_order()
    {
        Assert.True(CheckArgs.TryParse(new[] { "./app", "--trace", "out.jsonl" }, out var parsed1, out _));
        Assert.Equal("./app", parsed1.Path);
        Assert.Equal("out.jsonl", parsed1.TracePath);

        Assert.True(CheckArgs.TryParse(new[] { "--trace", "out.jsonl", "./app" }, out var parsed2, out _));
        Assert.Equal("./app", parsed2.Path);
        Assert.Equal("out.jsonl", parsed2.TracePath);
    }

    [Fact]
    public void Trace_flag_without_value_errors()
    {
        Assert.False(CheckArgs.TryParse(new[] { "--trace" }, out _, out var err));
        Assert.Contains("--trace", err);
    }

    [Fact]
    public void Unknown_flag_errors_with_clear_message()
    {
        Assert.False(CheckArgs.TryParse(new[] { "--bogus" }, out _, out var err));
        Assert.NotNull(err);
        Assert.Contains("--bogus", err);
    }

    [Fact]
    public void Two_positional_paths_error()
    {
        Assert.False(CheckArgs.TryParse(new[] { "./a", "./b" }, out _, out var err));
        Assert.NotNull(err);
        Assert.Contains("only one positional path", err);
    }

    [Fact]
    public void Help_text_mentions_trace_flag()
    {
        Assert.Contains("--trace", CheckArgs.HelpText);
    }

    [Fact]
    public void Suggest_threshold_defaults_to_null_so_command_can_apply_its_own_default()
    {
        Assert.True(CheckArgs.TryParse(Array.Empty<string>(), out var parsed, out _));
        Assert.Null(parsed.SuggestThreshold);
    }

    [Fact]
    public void Suggest_threshold_parses_non_negative_integer()
    {
        Assert.True(CheckArgs.TryParse(new[] { "--suggest-threshold", "5" }, out var parsed, out _));
        Assert.Equal(5, parsed.SuggestThreshold);

        Assert.True(CheckArgs.TryParse(new[] { "--suggest-threshold", "0" }, out var zero, out _));
        Assert.Equal(0, zero.SuggestThreshold);
    }

    [Fact]
    public void Suggest_threshold_without_value_errors()
    {
        Assert.False(CheckArgs.TryParse(new[] { "--suggest-threshold" }, out _, out var err));
        Assert.NotNull(err);
        Assert.Contains("--suggest-threshold", err);
    }

    [Fact]
    public void Suggest_threshold_rejects_negative_or_garbage()
    {
        Assert.False(CheckArgs.TryParse(new[] { "--suggest-threshold", "-1" }, out _, out var err1));
        Assert.NotNull(err1);
        Assert.Contains("non-negative", err1);

        Assert.False(CheckArgs.TryParse(new[] { "--suggest-threshold", "many" }, out _, out var err2));
        Assert.NotNull(err2);
        Assert.Contains("non-negative", err2);
    }

    [Fact]
    public void Help_text_mentions_suggest_threshold_flag()
    {
        Assert.Contains("--suggest-threshold", CheckArgs.HelpText);
    }
}
