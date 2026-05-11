// Spec 038 §8 — guardrail tests. Drives GuardrailRunner.Audit against
// synthetic trace inputs so we don't depend on the deferred WindowsAppSDK
// integration fixture (same scope decision as Phase-1 §1.6).

using System.Text;
using Microsoft.UI.Reactor.MurCheckGuardrail;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests;

public class GuardrailRunnerTests
{
    static TraceDiag E(string code, string file = "X.cs", int line = 1, int col = 1)
        => new(code, "E", file, line, col);
    static TraceDiag W(string code, string file = "X.cs", int line = 1, int col = 1)
        => new(code, "W", file, line, col);

    [Fact]
    public void Empty_traces_pass()
    {
        var (rc, stdout, stderr) = Run(iter: Array.Empty<TraceDiag>(), final: Array.Empty<TraceDiag>());
        Assert.Equal(0, rc);
        Assert.Contains("OK", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Final_emits_a_CS_error_only_present_in_table_universal_floor_passes()
    {
        // CS1061 surfaces as Error in final — the universal floor scores it
        // 1.0 in iteration too, so no violation.
        var (rc, _, _) = Run(iter: Array.Empty<TraceDiag>(), final: new[] { E("CS1061") });
        Assert.Equal(0, rc);
    }

    [Fact]
    public void Suppressed_warning_promoted_to_error_in_final_emits_advisory_not_failure()
    {
        // CS1591 is suppressed warning in iteration (0.0); appears as Error
        // in final (the warnaserror case). Advisory in stdout, exit 0.
        var iter = new[] { W("CS1591", line: 10) };
        var final = new[] { E("CS1591", line: 10) };

        var (rc, stdout, stderr) = Run(iter, final);
        Assert.Equal(0, rc);
        Assert.Contains("advisory", stdout);
        Assert.Contains("CS1591", stdout);
        Assert.DoesNotContain("FAIL", stderr);
    }

    [Fact]
    public void Trace_parser_round_trips_a_command_row_without_treating_it_as_a_diagnostic()
    {
        // The TraceWriter writes one `kind: "command"` row at the head. The
        // guardrail must skip it (no `code` field), not crash, not count it
        // as a diagnostic.
        var lines = new[]
        {
            """{"ts":"2026-05-11T00:00:00Z","kind":"command","argv":["dotnet","build","./app","--nologo"],"mode":"iteration"}""",
            """{"ts":"2026-05-11T00:00:01Z","code":"CS1061","severity":"E","file":"X.cs","line":1,"col":1,"msg":"m","mode":"iteration"}""",
        };

        var rows = GuardrailRunner.ParseTrace(lines);
        Assert.Single(rows);
        Assert.Equal("CS1061", rows[0].Code);
        Assert.Equal("E", rows[0].Severity);
    }

    [Fact]
    public void Trace_parser_tolerates_blank_lines()
    {
        var lines = new[]
        {
            "",
            """{"ts":"x","code":"CS1061","severity":"E","file":"X.cs","line":1,"col":1,"msg":"m","mode":"iteration"}""",
            "   ",
        };
        var rows = GuardrailRunner.ParseTrace(lines);
        Assert.Single(rows);
    }

    [Fact]
    public void Cli_with_wrong_arg_count_exits_2()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var rc = GuardrailRunner.Run(Array.Empty<string>(), stdout, stderr);
        Assert.Equal(2, rc);
        Assert.Contains("usage", stderr.ToString());
    }

    [Fact]
    public void Cli_with_missing_iter_file_exits_2()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var rc = GuardrailRunner.Run(new[] { @"C:\does-not-exist-1.jsonl", @"C:\does-not-exist-2.jsonl" }, stdout, stderr);
        Assert.Equal(2, rc);
        Assert.Contains("not found", stderr.ToString());
    }

    [Fact]
    public void End_to_end_against_temp_files_passes_clean_case()
    {
        var iterPath = WriteTraceFile(new[] { W("CS1591") });
        var finalPath = WriteTraceFile(new[] { E("CS1061") });
        try
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var rc = GuardrailRunner.Run(new[] { iterPath, finalPath }, stdout, stderr);
            Assert.Equal(0, rc);
            Assert.Contains("OK", stdout.ToString());
        }
        finally
        {
            try { File.Delete(iterPath); File.Delete(finalPath); } catch { }
        }
    }

    static (int rc, string stdout, string stderr) Run(IReadOnlyList<TraceDiag> iter, IReadOnlyList<TraceDiag> final)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var rc = GuardrailRunner.Audit(iter, final, stdout, stderr);
        return (rc, stdout.ToString(), stderr.ToString());
    }

    static string WriteTraceFile(IReadOnlyList<TraceDiag> rows)
    {
        var path = Path.Combine(Path.GetTempPath(), "mur-check-guardrail-" + Guid.NewGuid() + ".jsonl");
        var sb = new StringBuilder();
        foreach (var r in rows)
        {
            sb.AppendLine($$"""{"ts":"x","code":"{{r.Code}}","severity":"{{r.Severity}}","file":"{{r.File}}","line":{{r.Line}},"col":{{r.Col}},"msg":"m","mode":"iteration"}""");
        }
        File.WriteAllText(path, sb.ToString());
        return path;
    }
}
