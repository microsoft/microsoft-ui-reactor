// Phase-0 trace-writer tests. Spec 038 §0.3:
//   • Every row contains the documented schema fields.
//   • No row exceeds 2 KB (heuristic catch for source-leak regressions).
//   • Every `file` field is project-relative, "<external>", or ".".
//     Absolute paths inside the project root are normalized to relative form
//     so traces don't carry `C:\Users\<name>\...` prefixes.
//   • Trace is written *in addition to* stdout — separate concern, exercised
//     in the integration test under tests/Reactor.IntegrationTests.

using System.Text.Json;
using Microsoft.UI.Reactor.Cli.Check;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests;

public class TraceWriterTests
{
    [Fact]
    public void Trace_row_has_full_schema_for_a_typical_diagnostic()
    {
        var d = new CheckCommand.Diag(
            File: "Program.cs",
            Line: 34, Col: 16,
            Severity: "error", Code: "CS1061",
            Message: "'ButtonElement' does not contain a definition for 'OnClick'");
        var root = Path.GetFullPath(".");

        var row = TraceWriter.ToRow(d, root);

        Assert.NotNull(row.ts);
        Assert.Equal("CS1061", row.code);
        Assert.Equal("E", row.severity);
        Assert.Equal("Program.cs", row.file);
        Assert.Equal(34, row.line);
        Assert.Equal(16, row.col);
        Assert.Contains("OnClick", row.msg);
        Assert.Equal("iteration", row.mode);
        Assert.Null(row.receiver_type);
        Assert.Null(row.member);
    }

    [Fact]
    public void Severity_is_short_form_E_W_I()
    {
        var root = Path.GetFullPath(".");
        var e = TraceWriter.ToRow(new CheckCommand.Diag("a.cs", 1, 1, "error",   "CS1", "x"), root);
        var w = TraceWriter.ToRow(new CheckCommand.Diag("a.cs", 1, 1, "warning", "CS1", "x"), root);
        var i = TraceWriter.ToRow(new CheckCommand.Diag("a.cs", 1, 1, "info",    "CS1", "x"), root);
        Assert.Equal("E", e.severity);
        Assert.Equal("W", w.severity);
        Assert.Equal("I", i.severity);
    }

    [Fact]
    public void Long_messages_are_truncated_under_2KB_per_row()
    {
        var huge = new string('x', 8192);
        var d = new CheckCommand.Diag("a.cs", 1, 1, "error", "CS1061", huge);

        using var tmp = TempFile.Create();
        using (var w = TraceWriter.Open(tmp.Path, Path.GetFullPath(".")))
            w.Write(d);

        var line = File.ReadAllLines(tmp.Path).Single();
        Assert.True(line.Length <= 2048, $"trace row was {line.Length} bytes — exceeds 2 KB cap.");
    }

    [Fact]
    public void Absolute_paths_outside_project_root_are_redacted()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "reactor-trace-root-" + Guid.NewGuid()));
        Directory.CreateDirectory(root);
        try
        {
            var inside = Path.Combine(root, "src", "Foo.cs");
            var outside = Path.Combine(Path.GetTempPath(), "elsewhere-" + Guid.NewGuid(), "Bar.cs");

            // Inside the project root, absolute paths are normalized to
            // project-relative form (forward-slash separators).
            Assert.Equal("src/Foo.cs", TraceWriter.SanitizePath(inside, root));
            Assert.Equal("<external>", TraceWriter.SanitizePath(outside, root));
            Assert.Equal("rel/path.cs", TraceWriter.SanitizePath("rel/path.cs", root));
            Assert.Equal("rel/path.cs", TraceWriter.SanitizePath(@"rel\path.cs", root));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Trace_file_field_is_relative_for_every_in_root_row()
    {
        // Stronger than the prior assertion: file must never be an absolute
        // path. Either relative (in-root) or "<external>" (out-of-root).
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "reactor-trace-root2-" + Guid.NewGuid()));
        Directory.CreateDirectory(root);
        try
        {
            var d1 = new CheckCommand.Diag("Program.cs", 1, 1, "error", "CS1", "msg");
            var d2 = new CheckCommand.Diag(Path.Combine(root, "src", "X.cs"), 2, 1, "error", "CS2", "msg");
            var d3 = new CheckCommand.Diag(Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid() + ".cs"), 3, 1, "error", "CS3", "msg");

            using var tmp = TempFile.Create();
            using (var w = TraceWriter.Open(tmp.Path, root))
            {
                w.Write(d1);
                w.Write(d2);
                w.Write(d3);
            }

            foreach (var line in File.ReadAllLines(tmp.Path))
            {
                using var doc = JsonDocument.Parse(line);
                var file = doc.RootElement.GetProperty("file").GetString()!;
                var ok = !Path.IsPathRooted(file); // relative, ".", or "<external>"
                Assert.True(ok, $"file='{file}' is an absolute path — trace must not carry machine layout.");
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Diag_row_mode_matches_writer_invocation_mode()
    {
        // Regression test: TraceWriter used to hardcode mode="iteration" on
        // every diag row, even when opened with --final. The Phase-2 fix
        // pipes the mode through Open(...). Without this test, a future edit
        // could reintroduce the bug undetected because the unit pipeline
        // tests don't go through the CLI's mode-resolution path.
        var d = new CheckCommand.Diag("a.cs", 1, 1, "error", "CS1061", "x");

        using var tmp = TempFile.Create();
        using (var w = TraceWriter.Open(tmp.Path, Path.GetFullPath("."), mode: "final"))
            w.Write(d);

        var line = File.ReadAllLines(tmp.Path).Single();
        using var doc = JsonDocument.Parse(line);
        Assert.Equal("final", doc.RootElement.GetProperty("mode").GetString());
    }

    [Fact]
    public void Trace_writes_one_jsonl_row_per_call_appendable()
    {
        var root = Path.GetFullPath(".");
        var d = new CheckCommand.Diag("a.cs", 1, 1, "error", "CS1061", "x");

        using var tmp = TempFile.Create();
        using (var w = TraceWriter.Open(tmp.Path, root)) { w.Write(d); w.Write(d); }
        // re-open and append a third
        using (var w = TraceWriter.Open(tmp.Path, root)) { w.Write(d); }

        var lines = File.ReadAllLines(tmp.Path);
        Assert.Equal(3, lines.Length);
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.Equal("CS1061", doc.RootElement.GetProperty("code").GetString());
            Assert.Equal("iteration", doc.RootElement.GetProperty("mode").GetString());
        }
    }

    [Fact]
    public void Rule_self_disabled_row_has_expected_schema()
    {
        // Spec 038 §3.1a residual: when the registry self-disables a rule
        // because its declared target stopped resolving, the trace must
        // capture {rule, unresolved_target} so a maintainer notices the
        // moment a Reactor minor release breaks something. Stdout stays
        // clean — agents never read traces, and silent rule disablement is
        // the only behavioral effect they see.
        using var tmp = TempFile.Create();
        using (var w = TraceWriter.Open(tmp.Path, Path.GetFullPath("."), mode: "iteration"))
            w.WriteRuleSelfDisabled("AlignmentShortcutRule", "Microsoft.UI.Reactor.ElementExtensions");

        var line = File.ReadAllLines(tmp.Path).Single();
        using var doc = JsonDocument.Parse(line);
        Assert.Equal("rule_self_disabled", doc.RootElement.GetProperty("kind").GetString());
        Assert.Equal("AlignmentShortcutRule", doc.RootElement.GetProperty("rule").GetString());
        Assert.Equal("Microsoft.UI.Reactor.ElementExtensions",
            doc.RootElement.GetProperty("unresolved_target").GetString());
        Assert.Equal("iteration", doc.RootElement.GetProperty("mode").GetString());
        Assert.True(line.Length <= 2048, $"trace row was {line.Length} bytes — exceeds 2 KB cap.");
    }

    [Fact]
    public void Rule_fired_row_has_expected_schema()
    {
        // Spec 038 EC3-final watch-item: per-rule firing-rate audits should
        // be a 1-line grep over the trace file. The row carries enough info
        // to identify the rule, the diagnostic it fired on, the confidence,
        // and the location — so a maintainer can `jq 'select(.kind=="rule_fired")'`
        // and reconstruct firing rates without rejoining against the diag rows.
        using var tmp = TempFile.Create();
        using (var w = TraceWriter.Open(tmp.Path, Path.GetFullPath("."), mode: "iteration"))
        {
            w.WriteRuleFired(
                ruleName: "GridSizeFactoryParensRule",
                code: "CS1955",
                confidence: 0.95,
                evidence: "GridSize.Auto is a property, not a method (cluster:C0004)",
                file: "Program.cs",
                line: 42);
        }

        var line = File.ReadAllLines(tmp.Path).Single();
        using var doc = JsonDocument.Parse(line);
        Assert.Equal("rule_fired", doc.RootElement.GetProperty("kind").GetString());
        Assert.Equal("GridSizeFactoryParensRule", doc.RootElement.GetProperty("rule").GetString());
        Assert.Equal("CS1955", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(0.95, doc.RootElement.GetProperty("confidence").GetDouble());
        Assert.Contains("GridSize.Auto", doc.RootElement.GetProperty("evidence").GetString());
        Assert.Equal("Program.cs", doc.RootElement.GetProperty("file").GetString());
        Assert.Equal(42, doc.RootElement.GetProperty("line").GetInt32());
        Assert.Equal("iteration", doc.RootElement.GetProperty("mode").GetString());
        Assert.True(line.Length <= 2048, $"trace row was {line.Length} bytes — exceeds 2 KB cap.");
    }

    [Fact]
    public void Rule_fired_evidence_is_truncated_under_2KB_cap()
    {
        // Same defensive truncation we apply to diag.msg: a runaway evidence
        // string should never blow the 2 KB row budget.
        var huge = new string('e', 8192);
        using var tmp = TempFile.Create();
        using (var w = TraceWriter.Open(tmp.Path, Path.GetFullPath(".")))
            w.WriteRuleFired("R", "CS1061", 0.9, huge, "a.cs", 1);

        var line = File.ReadAllLines(tmp.Path).Single();
        Assert.True(line.Length <= 2048, $"trace row was {line.Length} bytes — exceeds 2 KB cap.");
    }

    [Fact]
    public void Rule_fired_file_is_sanitized_like_diag_rows()
    {
        // Same path-leak protection that applies to diag rows: a rule fire
        // outside the project root must not leak the absolute file path.
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "reactor-trace-rf-" + Guid.NewGuid()));
        Directory.CreateDirectory(root);
        try
        {
            var outside = Path.Combine(Path.GetTempPath(), "elsewhere-" + Guid.NewGuid(), "Bar.cs");
            using var tmp = TempFile.Create();
            using (var w = TraceWriter.Open(tmp.Path, root))
                w.WriteRuleFired("R", "CS1061", 0.9, "ev", outside, 1);

            var line = File.ReadAllLines(tmp.Path).Single();
            using var doc = JsonDocument.Parse(line);
            Assert.Equal("<external>", doc.RootElement.GetProperty("file").GetString());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    sealed class TempFile : IDisposable
    {
        public string Path { get; }
        TempFile(string p) { Path = p; }
        public static TempFile Create()
        {
            var p = global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), "reactor-trace-" + Guid.NewGuid() + ".jsonl");
            return new TempFile(p);
        }
        public void Dispose()
        {
            try { if (File.Exists(Path)) File.Delete(Path); } catch { }
        }
    }
}
