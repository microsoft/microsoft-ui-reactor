// Spec 038 Phase-1 ship gate: end-to-end threshold-tuning harness.
//
// Two test surfaces here:
//
//  1. SuggesterTuner unit tests — exercise the pure parts (match
//     classification, the Run() pipeline against a 1-row inline corpus) so
//     the harness itself has CI coverage even when the external corpus is
//     not available.
//
//  2. EndToEnd_corpus_run — runs the harness against the real spec-037
//     fixes.jsonl when MUR_TUNING_CORPUS=<path> is set in the environment.
//     Otherwise short-circuits to a no-op. The report is written next to
//     the corpus as `tuning_report.<utc>.json`.
//
// We do NOT bake the corpus path into source: the corpus lives in a sibling
// repo (reactor-tokenusage) and rotates with each data drop.

using Microsoft.UI.Reactor.Tests.CheckCommandTests.Tuning;
using Xunit;
using Xunit.Abstractions;
using SystemText = System.Text;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Tuning;

public class ThresholdTuningTests
{
    readonly ITestOutputHelper _out;

    public ThresholdTuningTests(ITestOutputHelper output) { _out = output; }

    [Fact]
    public void ClassifyMatch_returns_true_when_member_appears_in_after_only()
    {
        var fix = new CorpusFix(
            RunId: "r", TurnBefore: 0, FilePath: "App.cs",
            Diagnostic: new CorpusDiagnostic("CS1061", "E", 1, 1, null, null, null),
            BeforeText: ".HorizontalAlignment(...)",
            AfterText: ".Set(b => b.HorizontalAlignment = ...)",
            Hunks: new[]
            {
                new CorpusHunk(
                    Before: ".HorizontalAlignment(Stretch)",
                    After: ".Set(b => b.HorizontalAlignment = Stretch)")
            },
            FixKind: "renamed_member", Agent: "gpt-5.5");

        // The suggestion text "ButtonElement.Set" should be classified as a
        // match since "Set" appears in after but not before.
        Assert.True(SuggesterTuner.ClassifyMatch("ButtonElement.Set", fix));
    }

    [Fact]
    public void ClassifyMatch_returns_false_when_member_already_in_before()
    {
        var fix = new CorpusFix(
            RunId: "r", TurnBefore: 0, FilePath: "App.cs",
            Diagnostic: new CorpusDiagnostic("CS1061", "E", 1, 1, null, null, null),
            BeforeText: ".HorizontalAlignment(Stretch)",
            AfterText: ".Set(b => b.HorizontalAlignment = Stretch)",
            Hunks: new[]
            {
                new CorpusHunk(
                    Before: ".HorizontalAlignment(Stretch)",
                    After: ".Set(b => b.HorizontalAlignment = Stretch)")
            },
            FixKind: "renamed_member", Agent: "gpt-5.5");

        // "HorizontalAlignment" is in both → not a meaningful "fix" target.
        Assert.False(SuggesterTuner.ClassifyMatch("ButtonElement.HorizontalAlignment", fix));
    }

    [Fact]
    public void ClassifyMatch_short_text_returns_false()
    {
        var fix = new CorpusFix(
            RunId: "r", TurnBefore: 0, FilePath: "App.cs",
            Diagnostic: new CorpusDiagnostic("CS1061", "E", 1, 1, null, null, null),
            BeforeText: "x", AfterText: "y",
            Hunks: Array.Empty<CorpusHunk>(),
            FixKind: "other", Agent: null);

        // Single-letter member fragment is too noisy to count as a match.
        Assert.False(SuggesterTuner.ClassifyMatch("X.a", fix));
    }

    [Fact]
    public void Run_against_inline_one_row_corpus_produces_report()
    {
        // A synthetic CS0103 typo fix the suggester should fire on. The
        // before-text uses a Reactor factory that the stubs cover (Heading).
        var beforeText = """
using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;
class App : Component
{
    public override Element Render() => Headig("hello");
}
""";
        var afterText = beforeText.Replace("Headig", "Heading");
        var line = global::System.Text.Json.JsonSerializer.Serialize(new
        {
            run_id = "r1",
            file = "App.cs",
            turn_before = 1,
            diag = new { line = 5, col = 47, code = "CS0103", msg = "name 'Headig' does not exist", severity = "E" },
            before = new { text = beforeText },
            after = new { text = afterText },
            delta = new { hunks = new[] { new { before = "Headig(\"hello\")", after = "Heading(\"hello\")" } } },
            fix_kind = "other",
            agent = "gpt-5.5",
        });

        var temp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(temp, line);
            var report = SuggesterTuner.Run(temp);

            Assert.Equal(1, report.TotalCorpusRows);
            Assert.Equal(1, report.HandledCorpusRows);
            var summary = Assert.Single(report.Summaries);
            Assert.Equal("CS0103", summary.Code);
            // The suggester should fire and match (Heading is in stubs).
            Assert.Equal(1, summary.Fired);
            Assert.Equal(1, summary.FiredAndMatch);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void Run_restores_thresholds_after_completion()
    {
        var captured = Microsoft.UI.Reactor.Cli.Check.Suggesters.Thresholds.PerCode;
        var temp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(temp, "");
            SuggesterTuner.Run(temp);
            // Thresholds.PerCode is the same reference instance after Run().
            Assert.Same(captured, Microsoft.UI.Reactor.Cli.Check.Suggesters.Thresholds.PerCode);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void EndToEnd_corpus_run()
    {
        var corpus = Environment.GetEnvironmentVariable("MUR_TUNING_CORPUS");
        if (string.IsNullOrEmpty(corpus))
        {
            _out.WriteLine("[skipped] MUR_TUNING_CORPUS not set; skipping live-corpus tuning run.");
            return;
        }
        if (!File.Exists(corpus))
        {
            _out.WriteLine($"[skipped] corpus file not found: {corpus}");
            return;
        }

        var report = SuggesterTuner.Run(corpus);

        // Sanity assertions: the corpus has at least *some* rows and at least
        // one row hits a handled code (the 50-run drop guarantees this).
        Assert.True(report.TotalCorpusRows > 0, "corpus is empty");
        Assert.True(report.HandledCorpusRows > 0,
            $"no rows hit handled codes; corpus codes: {string.Join(",", report.Entries.Select(e => e.Code).Distinct())}");

        var json = SuggesterTuner.SerializeReport(report);
        var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var dir = Path.GetDirectoryName(corpus) ?? Path.GetTempPath();
        var outPath = Path.Combine(dir, $"tuning_report.{stamp}.json");
        File.WriteAllText(outPath, json, new SystemText.UTF8Encoding(false));

        // Also write a brief textual summary the human can read at a glance.
        var sb = new SystemText.StringBuilder();
        sb.AppendLine($"# Tuning report — {stamp}");
        sb.AppendLine($"corpus: {corpus}");
        sb.AppendLine($"rows: {report.TotalCorpusRows} (handled: {report.HandledCorpusRows})");
        sb.AppendLine();
        foreach (var s in report.Summaries)
        {
            sb.AppendLine($"## {s.Code}");
            sb.AppendLine($"  rows seen           : {s.TotalRows}");
            sb.AppendLine($"  fired (any conf)    : {s.Fired}  match: {s.FiredAndMatch}  no-match: {s.Fired - s.FiredAndMatch}");
            sb.AppendLine($"  silent              : {s.Silent}");
            sb.AppendLine($"  no diag in compile  : {s.NoDiagnostic}");
            if (s.ConfidencesAll.Count > 0)
            {
                sb.AppendLine($"  conf all (max..min) : [{string.Join(", ", s.ConfidencesAll.Select(c => c.ToString("F2")))}]");
                if (s.ConfidencesMatch.Count > 0)
                    sb.AppendLine($"  conf match          : [{string.Join(", ", s.ConfidencesMatch.Select(c => c.ToString("F2")))}]");
                if (s.ConfidencesNoMatch.Count > 0)
                    sb.AppendLine($"  conf no-match       : [{string.Join(", ", s.ConfidencesNoMatch.Select(c => c.ToString("F2")))}]");
            }
            sb.AppendLine();
        }
        var summaryPath = Path.Combine(dir, $"tuning_report.{stamp}.md");
        File.WriteAllText(summaryPath, sb.ToString(), new SystemText.UTF8Encoding(false));

        _out.WriteLine(sb.ToString());
        _out.WriteLine($"json: {outPath}");
        _out.WriteLine($"md  : {summaryPath}");
    }
}
