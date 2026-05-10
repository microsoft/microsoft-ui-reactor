// Spec 038 Phase-1 ship gate: CorpusReader unit tests. The full corpus lives
// outside this repo (spec 037 mining harness) so these tests use small
// inline JSON exemplars matching the schema captured in audit pass 2 on
// 2026-05-10.

using System.Text.Json;
using Microsoft.UI.Reactor.Tests.CheckCommandTests.Tuning;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Tuning;

public class CorpusReaderTests
{
    [Fact]
    public void Parses_minimal_well_formed_row()
    {
        var line = """
{"run_id":"r1","prompt_id":"p1","turn_before":3,"file":"App.cs",
 "diag":{"file":"App.cs","line":12,"col":4,"severity":"E","code":"CS1061",
         "msg":"...","receiver_type":"ButtonElement","member":"OnClick"},
 "before":{"sha":"a","text":"before-text"},
 "after":{"sha":"b","text":"after-text"},
 "delta":{"hunks":[{"before":"x","after":"y"}]},
 "fix_kind":"renamed_member","agent":"gpt-5.5"}
""".Replace("\r", "").Replace("\n", "");

        var row = CorpusReader.ParseLine(line);

        Assert.NotNull(row);
        Assert.Equal("r1", row!.RunId);
        Assert.Equal(3, row.TurnBefore);
        Assert.Equal("App.cs", row.FilePath);
        Assert.Equal("CS1061", row.Diagnostic.Code);
        Assert.Equal(12, row.Diagnostic.Line);
        Assert.Equal(4, row.Diagnostic.Col);
        Assert.Equal("ButtonElement", row.Diagnostic.ReceiverType);
        Assert.Equal("OnClick", row.Diagnostic.Member);
        Assert.Equal("before-text", row.BeforeText);
        Assert.Equal("after-text", row.AfterText);
        Assert.Single(row.Hunks);
        Assert.Equal("x", row.Hunks[0].Before);
        Assert.Equal("y", row.Hunks[0].After);
        Assert.Equal("renamed_member", row.FixKind);
        Assert.Equal("gpt-5.5", row.Agent);
    }

    [Fact]
    public void Tolerates_null_optional_fields()
    {
        var line = """
{"run_id":"r1","file":"App.cs",
 "diag":{"line":1,"col":1,"code":"CS0618","msg":"...",
         "receiver_type":null,"member":null},
 "before":{"text":"b"},"after":{"text":"a"},"fix_kind":"other"}
""".Replace("\r", "").Replace("\n", "");

        var row = CorpusReader.ParseLine(line);

        Assert.NotNull(row);
        Assert.Null(row!.Diagnostic.ReceiverType);
        Assert.Null(row.Diagnostic.Member);
        Assert.Empty(row.Hunks);
        Assert.Equal("other", row.FixKind);
    }

    [Fact]
    public void Read_skips_blank_lines_and_yields_null_for_malformed_rows()
    {
        var temp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(temp,
                "\n" +
                """{"run_id":"r1","file":"x","diag":{"line":1,"col":1,"code":"CS1061","msg":"m"},"before":{"text":"b"},"after":{"text":"a"},"fix_kind":"other"}""" + "\n" +
                "not-json\n" +
                """{"run_id":"r2","file":"y","diag":{"line":2,"col":2,"code":"CS0103","msg":"m"},"before":{"text":"b"},"after":{"text":"a"},"fix_kind":"other"}""" + "\n" +
                "\n");

            var rows = CorpusReader.Read(temp).ToList();

            // Blank lines skipped; "not-json" yielded as null; two valid rows.
            Assert.Equal(3, rows.Count);
            Assert.NotNull(rows[0]);
            Assert.Null(rows[1]);
            Assert.NotNull(rows[2]);
            Assert.Equal("r1", rows[0]!.RunId);
            Assert.Equal("r2", rows[2]!.RunId);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void Throws_when_required_diag_fields_missing()
    {
        // No "code" → cannot classify the row; reader returns null via JsonException.
        var line = """{"run_id":"r1","diag":{"line":1,"col":1,"msg":"m"},"before":{"text":""},"after":{"text":""},"fix_kind":"other"}""";
        // ParseLine should throw (caller catches in Read); we verify directly:
        Assert.Throws<KeyNotFoundException>(() => CorpusReader.ParseLine(line));
    }
}
