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
}
