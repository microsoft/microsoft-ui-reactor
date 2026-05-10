// Pipeline-level tests for `mur check`. We exercise the parsing + emission +
// trace plumbing without spinning up `dotnet build`. Spec 038 §0.5 + §0.3.

using System.Text.Json;
using Microsoft.UI.Reactor.Cli.Check;
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
