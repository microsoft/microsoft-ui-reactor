// Pipeline-level tests for `mur check`. We exercise the parsing + emission +
// trace plumbing without spinning up `dotnet build`. Spec 038 §0.5 + §0.3.

using System.Text.Json;
using Microsoft.UI.Reactor.Cli.Check;
using Microsoft.UI.Reactor.Cli.Check.Ranker;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests;

public class CheckCommandPipelineTests
{
    const string SampleMsBuildOutput = """
        Determining projects to restore...
        Build started 2026-05-09 12:00:00.
        Program.cs(34,16): error CS1061: 'ButtonElement' does not contain a definition for 'OnClick' [C:\src\Foo\Foo.csproj]
        Program.cs(34,16): error CS1061: 'ButtonElement' does not contain a definition for 'OnClick' [C:\src\Foo\Foo.csproj]
        Program.cs(40,8): warning CS8602: Dereference of a possibly null reference. [C:\src\Foo\Foo.csproj]
        Build FAILED.
        """;

    [Fact]
    public void Parses_msbuild_lines_into_diag_records()
    {
        var diags = CheckCommand.ParseDiagnostics(SampleMsBuildOutput);

        Assert.Equal(3, diags.Count);

        Assert.Equal("CS1061", diags[0].Code);
        Assert.Equal("error", diags[0].Severity);
        Assert.Equal(34, diags[0].Line);
        Assert.Equal(16, diags[0].Col);
        Assert.Contains("OnClick", diags[0].Message);

        Assert.Equal("CS8602", diags[2].Code);
        Assert.Equal("warning", diags[2].Severity);
    }

    [Fact]
    public void Parses_msbuild_lines_with_parens_in_file_path()
    {
        // Reluctant file capture must still anchor on (line,col): even when
        // the path itself contains parentheses (agent/temp dirs with labels).
        const string output = """
            C:\src\Reactor (test)\Program.cs(10,5): error CS1061: 'X' does not contain a definition for 'Y' [C:\src\Foo.csproj]
            """;

        var diags = CheckCommand.ParseDiagnostics(output);

        Assert.Single(diags);
        Assert.Equal(@"C:\src\Reactor (test)\Program.cs", diags[0].File);
        Assert.Equal(10, diags[0].Line);
        Assert.Equal(5, diags[0].Col);
        Assert.Equal("CS1061", diags[0].Code);
    }

    [Fact]
    public void Emit_dedupes_repeated_diagnostics()
    {
        var diags = CheckCommand.ParseDiagnostics(SampleMsBuildOutput);
        var sw = new StringWriter();
        CheckCommand.EmitDiagnostics(diags, sw, trace: null);

        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length); // 3 diags - 1 dup
    }

    [Fact]
    public void Emit_with_trace_writes_one_jsonl_row_per_unique_diagnostic()
    {
        var diags = CheckCommand.ParseDiagnostics(SampleMsBuildOutput);
        var stdout = new StringWriter();
        var tracePath = Path.Combine(Path.GetTempPath(), "mur-check-pipeline-" + Guid.NewGuid() + ".jsonl");
        try
        {
            using (var trace = TraceWriter.Open(tracePath, Path.GetFullPath(".")))
            {
                CheckCommand.EmitDiagnostics(diags, stdout, trace);
            }

            var traceLines = File.ReadAllLines(tracePath);
            Assert.Equal(2, traceLines.Length); // dedupe matches stdout

            foreach (var line in traceLines)
            {
                using var doc = JsonDocument.Parse(line);
                Assert.True(doc.RootElement.TryGetProperty("ts", out _));
                Assert.True(doc.RootElement.TryGetProperty("code", out _));
                Assert.True(doc.RootElement.TryGetProperty("severity", out _));
                Assert.True(doc.RootElement.TryGetProperty("file", out _));
                Assert.True(doc.RootElement.TryGetProperty("line", out _));
                Assert.True(doc.RootElement.TryGetProperty("col", out _));
                Assert.True(doc.RootElement.TryGetProperty("msg", out _));
                Assert.True(doc.RootElement.TryGetProperty("mode", out _));
                Assert.Equal("iteration", doc.RootElement.GetProperty("mode").GetString());
            }

            // Trace is *in addition to* stdout — both populated.
            Assert.NotEmpty(stdout.ToString());
        }
        finally
        {
            try { File.Delete(tracePath); } catch { }
        }
    }

    [Fact]
    public void Gate_suppresses_suggestions_when_cs_count_below_threshold()
    {
        // Single CS diagnostic, threshold 3 → gate closed.
        var diags = CheckCommand.ParseDiagnostics(
            "Program.cs(10,5): error CS1061: 'Foo' does not contain a definition for 'Bar' [Foo.csproj]");

        Assert.False(CheckCommand.ShouldEmitSuggestions(diags, threshold: 3));
    }

    [Fact]
    public void Gate_opens_when_cs_count_meets_threshold()
    {
        var diags = CheckCommand.ParseDiagnostics("""
            A.cs(1,1): error CS1061: a [P.csproj]
            B.cs(2,2): error CS0103: b [P.csproj]
            C.cs(3,3): error CS0117: c [P.csproj]
            """);

        Assert.True(CheckCommand.ShouldEmitSuggestions(diags, threshold: 3));
    }

    [Fact]
    public void Gate_threshold_zero_always_opens()
    {
        var diags = CheckCommand.ParseDiagnostics(
            "Program.cs(10,5): error CS1061: 'Foo' does not contain a definition for 'Bar' [Foo.csproj]");

        Assert.True(CheckCommand.ShouldEmitSuggestions(diags, threshold: 0));
        Assert.True(CheckCommand.ShouldEmitSuggestions(Array.Empty<CheckCommand.Diag>(), threshold: 0));
    }

    [Fact]
    public void Gate_only_counts_cs_prefixed_codes()
    {
        // Two REACTOR_* hits + one CS hit. Threshold 2 should NOT open — the
        // REACTOR_* diagnostics are Tier-1 territory (static hint table) and
        // don't pay for Tier-2 setup.
        var diags = CheckCommand.ParseDiagnostics("""
            App.cs(1,1): warning REACTOR_HOOKS_001: bad [P.csproj]
            App.cs(2,1): warning REACTOR_HOOKS_004: bad [P.csproj]
            App.cs(3,1): error CS1061: 'Foo' has no member [P.csproj]
            """);

        Assert.False(CheckCommand.ShouldEmitSuggestions(diags, threshold: 2));
    }

    [Fact]
    public void Gate_dedupes_repeated_diagnostics_when_counting()
    {
        // MSBuild often prints the same diagnostic twice (per project). The
        // gate uses the same dedup key EmitDiagnostics uses, so a dup pair
        // does NOT push count over the threshold.
        var diags = CheckCommand.ParseDiagnostics("""
            X.cs(1,1): error CS1061: a [P.csproj]
            X.cs(1,1): error CS1061: a [P.csproj]
            Y.cs(2,2): error CS0103: b [P.csproj]
            """);

        Assert.False(CheckCommand.ShouldEmitSuggestions(diags, threshold: 3));
        Assert.True(CheckCommand.ShouldEmitSuggestions(diags, threshold: 2));
    }

    [Fact]
    public void Default_threshold_matches_documented_value()
    {
        // Lock the constant into a test so the EC1-tuned default can only
        // change with an intentional code edit (and a failing test the author
        // has to update).
        Assert.Equal(3, CheckCommand.DefaultSuggestThreshold);
    }

    [Fact]
    public void Ranker_filter_suppresses_stdout_but_trace_still_records_every_row()
    {
        // Spec 038 §8: ranker filters stdout; trace receives every parsed
        // diagnostic so the suppressed-then-resurfaced telemetry hook
        // (failure-mode mitigation) can mine the full stream.
        var diags = CheckCommand.ParseDiagnostics("""
            X.cs(1,1): error CS1061: missing member [P.csproj]
            X.cs(2,2): warning CS1591: missing xml doc [P.csproj]
            X.cs(3,3): warning IDE0001: simplify [P.csproj]
            """);

        var iterCtx = new RankerContext(Mode.Iteration, null);
        var stdout = new StringWriter();
        var tracePath = Path.Combine(Path.GetTempPath(), "mur-check-ranker-" + Guid.NewGuid() + ".jsonl");
        try
        {
            using (var trace = TraceWriter.Open(tracePath, Path.GetFullPath(".")))
            {
                CheckCommand.EmitDiagnostics(diags, stdout, trace,
                    suggest: null,
                    stdoutFilter: d => Ranker.ShouldEmit(d, iterCtx));
            }

            // stdout: only the CS1061 error (CS1591 + IDE0001 are 0.0 score).
            var stdoutLines = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Single(stdoutLines);
            Assert.Contains("CS1061", stdoutLines[0]);

            // trace: all three deduped rows.
            var traceLines = File.ReadAllLines(tracePath);
            Assert.Equal(3, traceLines.Length);
        }
        finally { try { File.Delete(tracePath); } catch { } }
    }

    [Fact]
    public void Ranker_final_mode_emits_everything_to_stdout()
    {
        var diags = CheckCommand.ParseDiagnostics("""
            X.cs(1,1): error CS1061: missing member [P.csproj]
            X.cs(2,2): warning CS1591: missing xml doc [P.csproj]
            X.cs(3,3): warning IDE0001: simplify [P.csproj]
            """);

        var finalCtx = new RankerContext(Mode.Final, null);
        var stdout = new StringWriter();
        CheckCommand.EmitDiagnostics(diags, stdout, trace: null,
            suggest: null,
            stdoutFilter: d => Ranker.ShouldEmit(d, finalCtx));

        var lines = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void Trace_command_row_records_full_effective_dotnet_build_args()
    {
        // Spec §8 "Tracing": replays need the exact effective command line.
        var tracePath = Path.Combine(Path.GetTempPath(), "mur-check-cmd-" + Guid.NewGuid() + ".jsonl");
        try
        {
            using (var trace = TraceWriter.Open(tracePath, Path.GetFullPath(".")))
            {
                trace.WriteCommand(new[] { "build", "./app", "--nologo", "-v:m", "-p:Platform=x64" }, "iteration");
            }

            var line = File.ReadAllLines(tracePath).Single();
            using var doc = JsonDocument.Parse(line);
            Assert.Equal("command", doc.RootElement.GetProperty("kind").GetString());
            var argv = doc.RootElement.GetProperty("argv").EnumerateArray()
                .Select(e => e.GetString()!).ToArray();
            Assert.Equal("dotnet", argv[0]);
            Assert.Contains("build", argv);
            Assert.Contains("-p:Platform=x64", argv);
            Assert.Equal("iteration", doc.RootElement.GetProperty("mode").GetString());
        }
        finally { try { File.Delete(tracePath); } catch { } }
    }

    [Fact]
    public void Trace_row_is_under_2KB_for_realistic_msbuild_output()
    {
        var diags = CheckCommand.ParseDiagnostics(SampleMsBuildOutput);
        var stdout = new StringWriter();
        var tracePath = Path.Combine(Path.GetTempPath(), "mur-check-pipeline-len-" + Guid.NewGuid() + ".jsonl");
        try
        {
            using (var trace = TraceWriter.Open(tracePath, Path.GetFullPath(".")))
                CheckCommand.EmitDiagnostics(diags, stdout, trace);

            foreach (var line in File.ReadAllLines(tracePath))
                Assert.True(line.Length <= 2048, $"trace row {line.Length} bytes exceeds 2 KB cap.");
        }
        finally
        {
            try { File.Delete(tracePath); } catch { }
        }
    }
}
