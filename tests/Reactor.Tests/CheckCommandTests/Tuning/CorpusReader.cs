// Spec 038 Phase-1 ship gate: threshold tuning over the spec-037 mining corpus.
//
// Parses one row of `fixes.jsonl` into a structured CorpusFix. The harness
// upstream of this is the spec 037 mining pipeline; the schema here matches
// what that pipeline currently writes (see C:\temp\eval-trace-mining-followups.md
// for fields populated as of audit pass 2, 2026-05-10). New fields the harness
// adds in future drops should be additive and need not break this reader.
//
// Reader is deliberately tolerant: rows missing optional fields parse, rows
// with unrecognized fields are ignored. Failure to parse a row is recorded in
// the report rather than thrown — one bad row should not abort tuning.

using System.Text.Json;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Tuning;

internal sealed record CorpusDiagnostic(
    string Code,
    string? Severity,
    int Line,
    int Col,
    string? Message,
    string? ReceiverType,
    string? Member);

internal sealed record CorpusHunk(string Before, string After);

internal sealed record CorpusFix(
    string RunId,
    int? TurnBefore,
    string FilePath,
    CorpusDiagnostic Diagnostic,
    string BeforeText,
    string AfterText,
    IReadOnlyList<CorpusHunk> Hunks,
    string FixKind,
    string? Agent);

internal static class CorpusReader
{
    static readonly JsonSerializerOptions _opts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Read each line of <paramref name="path"/> as one JSON row. Lines that
    /// fail to parse are returned as null so the caller can count and report
    /// them rather than aborting the run.
    /// </summary>
    public static IEnumerable<CorpusFix?> Read(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            CorpusFix? row;
            try { row = ParseLine(line); }
            catch (JsonException) { row = null; }
            catch (KeyNotFoundException) { row = null; }
            yield return row;
        }
    }

    internal static CorpusFix? ParseLine(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var r = doc.RootElement;

        if (!r.TryGetProperty("diag", out var diagEl)) throw new KeyNotFoundException("diag");
        var code = TryGetString(diagEl, "code");
        if (string.IsNullOrEmpty(code)) throw new KeyNotFoundException("diag.code");

        var diag = new CorpusDiagnostic(
            Code: code,
            Severity: TryGetString(diagEl, "severity"),
            Line: diagEl.TryGetProperty("line", out var ln) && ln.TryGetInt32(out var lnv) ? lnv : 0,
            Col: diagEl.TryGetProperty("col", out var co) && co.TryGetInt32(out var cov) ? cov : 0,
            Message: TryGetString(diagEl, "msg"),
            ReceiverType: TryGetString(diagEl, "receiver_type"),
            Member: TryGetString(diagEl, "member"));

        var beforeText = r.TryGetProperty("before", out var bEl) && bEl.TryGetProperty("text", out var bt)
            ? bt.GetString() ?? "" : "";
        var afterText = r.TryGetProperty("after", out var aEl) && aEl.TryGetProperty("text", out var at)
            ? at.GetString() ?? "" : "";

        var hunks = new List<CorpusHunk>();
        if (r.TryGetProperty("delta", out var deltaEl) && deltaEl.TryGetProperty("hunks", out var hunksEl))
        {
            foreach (var h in hunksEl.EnumerateArray())
            {
                hunks.Add(new CorpusHunk(
                    Before: TryGetString(h, "before") ?? "",
                    After: TryGetString(h, "after") ?? ""));
            }
        }

        return new CorpusFix(
            RunId: TryGetString(r, "run_id") ?? "",
            TurnBefore: r.TryGetProperty("turn_before", out var tb) && tb.TryGetInt32(out var tbv) ? tbv : null,
            FilePath: TryGetString(r, "file") ?? "",
            Diagnostic: diag,
            BeforeText: beforeText,
            AfterText: afterText,
            Hunks: hunks,
            FixKind: TryGetString(r, "fix_kind") ?? "other",
            Agent: TryGetString(r, "agent"));
    }

    static string? TryGetString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Null => null,
            _ => null,
        };
    }
}
