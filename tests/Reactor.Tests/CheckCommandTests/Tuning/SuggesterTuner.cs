// Spec 038 Phase-1 ship gate: drive the SymbolSuggester against each row of
// the spec-037 mining corpus (`fixes.jsonl`), capture the raw suggestion +
// raw confidence, and compute per-code (recall@T, precision@T) curves so we
// can tune Thresholds.cs.
//
// "Match" is intentionally an over-approximation: the suggester's text or its
// renamed-member fragment must appear inside the human's after-text but not
// the before-text in the same hunk. Most of the 50-run corpus's CS1061 fixes
// are structural rewrites (`.X(...)` → `.Set(...)`) for which the suggester
// has no plausible right answer; we expect those to score as "did not match",
// which feeds the false-positive rate at threshold T.

using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.UI.Reactor.Cli.Check;
using Microsoft.UI.Reactor.Cli.Check.Suggesters;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Tuning;

internal sealed record TunerEntry(
    string RunId,
    string Code,
    string? ReceiverType,
    string? Member,
    string FixKind,
    string Outcome,             // "fired" | "silent" | "no-diag" | "no-node"
    string? SuggestionText,
    double Confidence,
    bool Match);

internal sealed record CodeSummary(
    string Code,
    int TotalRows,
    int Fired,
    int FiredAndMatch,
    int Silent,
    int NoDiagnostic,
    IReadOnlyList<double> ConfidencesAll,
    IReadOnlyList<double> ConfidencesMatch,
    IReadOnlyList<double> ConfidencesNoMatch);

internal sealed record TuningReport(
    string CorpusPath,
    int TotalCorpusRows,
    int HandledCorpusRows,
    IReadOnlyList<TunerEntry> Entries,
    IReadOnlyList<CodeSummary> Summaries);

internal static class SuggesterTuner
{
    /// <summary>
    /// CS codes the SymbolSuggester knows how to suggest for. Spec 038 §5.
    /// </summary>
    public static readonly IReadOnlySet<string> HandledCodes =
        new HashSet<string>(StringComparer.Ordinal) { "CS1061", "CS0103", "CS0117", "CS1503", "CS7036" };

    public static TuningReport Run(string corpusPath)
    {
        var rows = CorpusReader.Read(corpusPath).Where(r => r is not null).Cast<CorpusFix>().ToList();
        var entries = new List<TunerEntry>();
        var handled = 0;

        // Run the suggester with all per-code thresholds collapsed to zero so
        // we capture the raw confidence distribution. We use SuggestRaw
        // directly, but also reset Thresholds so any path that sneaks through
        // the gated wrapper still emits.
        var savedThresholds = Thresholds.PerCode;
        try
        {
            Thresholds.PerCode = HandledCodes.ToDictionary(c => c, _ => 0.0, StringComparer.Ordinal);

            var suggester = new SymbolSuggester();
            foreach (var row in rows)
            {
                if (!HandledCodes.Contains(row.Diagnostic.Code)) continue;
                handled++;
                entries.Add(EvaluateOne(suggester, row));
            }
        }
        finally
        {
            Thresholds.PerCode = savedThresholds;
        }

        var summaries = entries
            .GroupBy(e => e.Code, StringComparer.Ordinal)
            .Select(g =>
            {
                var fired = g.Where(e => e.Outcome == "fired").ToList();
                return new CodeSummary(
                    Code: g.Key,
                    TotalRows: g.Count(),
                    Fired: fired.Count,
                    FiredAndMatch: fired.Count(e => e.Match),
                    Silent: g.Count(e => e.Outcome == "silent"),
                    NoDiagnostic: g.Count(e => e.Outcome == "no-diag"),
                    ConfidencesAll: fired.Select(e => e.Confidence).OrderByDescending(c => c).ToList(),
                    ConfidencesMatch: fired.Where(e => e.Match).Select(e => e.Confidence).OrderByDescending(c => c).ToList(),
                    ConfidencesNoMatch: fired.Where(e => !e.Match).Select(e => e.Confidence).OrderByDescending(c => c).ToList());
            })
            .OrderByDescending(s => s.TotalRows)
            .ToList();

        return new TuningReport(
            CorpusPath: corpusPath,
            TotalCorpusRows: rows.Count,
            HandledCorpusRows: handled,
            Entries: entries,
            Summaries: summaries);
    }

    static TunerEntry EvaluateOne(SymbolSuggester suggester, CorpusFix row)
    {
        // Compose: stubs + the user's pre-fix file content. We rename the
        // user file to a unique synthetic path so tree resolution is
        // unambiguous.
        var sources = new[] { (ReactorCorpusStubs.Source, "_stubs.cs"), (row.BeforeText, "App.cs") };
        var compilation = TestCompilation.Create(sources);
        var factories = FactoryIndex.Build(compilation);

        // Find the matching diagnostic. Prefer (code, line, col) match against
        // the corpus's location; fall back to any first diagnostic with the
        // same id. The corpus's line is 1-based; Roslyn's is 1-based for
        // GetMappedLineSpan but 0-based for FileLinePositionSpan.
        var diagnostic = FindDiagnostic(compilation, row);
        if (diagnostic is null)
        {
            return new TunerEntry(row.RunId, row.Diagnostic.Code, row.Diagnostic.ReceiverType,
                row.Diagnostic.Member, row.FixKind, "no-diag", null, 0.0, false);
        }

        var node = LocateSuggestableNode(diagnostic);
        if (node is null)
        {
            return new TunerEntry(row.RunId, row.Diagnostic.Code, row.Diagnostic.ReceiverType,
                row.Diagnostic.Member, row.FixKind, "no-node", null, 0.0, false);
        }

        var receiver = ResolveReceiver(compilation, node);

        var ctx = new SuggesterContext(compilation, diagnostic, node, receiver, factories);
        var raw = suggester.SuggestRaw(in ctx);
        if (raw.Text is null)
        {
            return new TunerEntry(row.RunId, row.Diagnostic.Code, row.Diagnostic.ReceiverType,
                row.Diagnostic.Member, row.FixKind, "silent", null, 0.0, false);
        }

        var match = ClassifyMatch(raw.Text, row);
        return new TunerEntry(row.RunId, row.Diagnostic.Code, row.Diagnostic.ReceiverType,
            row.Diagnostic.Member, row.FixKind, "fired", raw.Text, raw.Confidence, match);
    }

    static Diagnostic? FindDiagnostic(CSharpCompilation compilation, CorpusFix row)
    {
        var byCode = compilation.GetDiagnostics().Where(d => d.Id == row.Diagnostic.Code).ToList();
        if (byCode.Count == 0) return null;
        if (byCode.Count == 1) return byCode[0];

        // Prefer the one whose line/col matches the corpus.
        Diagnostic? best = null;
        var bestDistance = int.MaxValue;
        foreach (var d in byCode)
        {
            var span = d.Location.GetMappedLineSpan();
            // GetMappedLineSpan returns 0-based; corpus is 1-based.
            var line = span.StartLinePosition.Line + 1;
            var col = span.StartLinePosition.Character + 1;
            var dist = Math.Abs(line - row.Diagnostic.Line) * 1000 + Math.Abs(col - row.Diagnostic.Col);
            if (dist < bestDistance) { bestDistance = dist; best = d; }
        }
        return best ?? byCode[0];
    }

    static SyntaxNode? LocateSuggestableNode(Diagnostic diagnostic)
    {
        var loc = diagnostic.Location;
        if (loc.SourceTree is null) return null;
        var root = loc.SourceTree.GetRoot();
        var node = root.FindNode(loc.SourceSpan, getInnermostNodeForTie: true);

        for (var n = node; n is not null; n = n.Parent)
        {
            if (n is MemberAccessExpressionSyntax or InvocationExpressionSyntax
                or IdentifierNameSyntax or ArgumentSyntax)
            {
                return n;
            }
        }
        return node;
    }

    static ITypeSymbol? ResolveReceiver(CSharpCompilation compilation, SyntaxNode node)
    {
        var tree = node.SyntaxTree;
        var sm = compilation.GetSemanticModel(tree);
        return node switch
        {
            MemberAccessExpressionSyntax m => sm.GetTypeInfo(m.Expression).Type,
            InvocationExpressionSyntax inv when inv.Expression is MemberAccessExpressionSyntax m2 =>
                sm.GetTypeInfo(m2.Expression).Type,
            _ when node.Parent is MemberAccessExpressionSyntax mp => sm.GetTypeInfo(mp.Expression).Type,
            _ => null,
        };
    }

    /// <summary>
    /// Compare the suggester's text to the human fix. Returns true if any
    /// hunk's after-text contains the suggestion (or a renamed-member
    /// fragment of it) and the same fragment was not already in the
    /// before-text. This is an over-approximation; expect to refine when
    /// the corpus is bigger.
    /// </summary>
    internal static bool ClassifyMatch(string suggestionText, CorpusFix row)
    {
        // For "TypeName.Member" suggestions, also try just the Member fragment.
        var tail = suggestionText;
        var dot = suggestionText.LastIndexOf('.');
        if (dot >= 0 && dot < suggestionText.Length - 1)
            tail = suggestionText[(dot + 1)..];
        // Strip parenthesized args if present.
        var paren = tail.IndexOf('(');
        if (paren > 0) tail = tail[..paren];
        tail = tail.Trim();
        if (tail.Length < 2) return false;

        foreach (var hunk in row.Hunks)
        {
            if (!hunk.After.Contains(tail, StringComparison.Ordinal)) continue;
            if (hunk.Before.Contains(tail, StringComparison.Ordinal)) continue;
            return true;
        }
        // Fall back to the whole before/after text bodies.
        if (row.AfterText.Contains(tail, StringComparison.Ordinal)
            && !row.BeforeText.Contains(tail, StringComparison.Ordinal))
            return true;
        return false;
    }

    /// <summary>
    /// Serialize the report to JSON for inspection/checkin.
    /// </summary>
    public static string SerializeReport(TuningReport report)
        => JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
}
