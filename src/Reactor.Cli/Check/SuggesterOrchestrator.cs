// Wires Tier-2 suggesters into the `mur check` diagnostic pipeline.
// Inputs: a parsed MSBuild `Diag` plus the path of the project under test.
// Outputs: a `Suggestion` to attach to the diagnostic line, or null if no
// suggester wants to claim the diagnostic.
//
// Spec 038 §1.6 wiring:
//   - Codes covered: CS1061, CS0103, CS0117, CS1503, CS7036.
//   - For CS1061 / CS0117 we only run the suggester when the receiver
//     resolves to a Microsoft.UI.Reactor.* symbol — non-Reactor diagnostics
//     pass through unchanged. CS0103 / CS1503 / CS7036 self-filter inside
//     the suggester (they probe the FactoryIndex / message text directly).
//   - The Tier-1 analyzer-ID hint table still wins ties at the format layer
//     (spec §9): if HintFor(code) returns a pointer, that wins over a
//     Tier-2 suggestion for the same diagnostic.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.UI.Reactor.Cli.Check.Suggesters;

namespace Microsoft.UI.Reactor.Cli.Check;

internal sealed record Suggestion(string Text, double Confidence, string Evidence, string SuggesterName);

internal sealed class SuggesterOrchestrator
{
    static readonly HashSet<string> SupportedCodes = new(StringComparer.Ordinal)
    {
        "CS1061", "CS0103", "CS0117", "CS1503", "CS7036",
    };

    readonly CompilationLoader _loader;
    readonly ISuggester[] _suggesters;

    public SuggesterOrchestrator(CompilationLoader? loader = null, ISuggester[]? suggesters = null)
    {
        _loader = loader ?? CompilationLoader.Instance;
        _suggesters = suggesters ?? new ISuggester[] { new SymbolSuggester() };
    }

    /// <summary>
    /// Returns the highest-confidence suggestion attached to <paramref name="diag"/>,
    /// or null if no suggester produced one above its threshold.
    /// </summary>
    public Suggestion? Suggest(CheckCommand.Diag diag, string projectPath)
    {
        if (!SupportedCodes.Contains(diag.Code)) return null;

        CSharpCompilation compilation;
        try { compilation = _loader.Load(projectPath); }
        catch { return null; }
        if (ReferenceEquals(compilation, CompilationLoader.EmptyCompilation)) return null;

        return SuggestAgainst(diag, compilation);
    }

    /// <summary>
    /// Test seam: same suggestion path but with a caller-provided compilation.
    /// Lets unit tests drive the orchestrator without a live project on disk.
    /// </summary>
    internal Suggestion? SuggestAgainst(CheckCommand.Diag diag, CSharpCompilation compilation)
    {
        if (!SupportedCodes.Contains(diag.Code)) return null;

        // Find the syntax tree that matches the diagnostic's file.
        var tree = FindTreeFor(compilation, diag.File);
        if (tree is null) return null;

        var span = ResolveSpan(tree, diag.Line, diag.Col);
        if (span is null) return null;

        var root = tree.GetRoot();
        var node = PickRelevantNode(root, span.Value);
        if (node is null) return null;

        var sm = compilation.GetSemanticModel(tree);
        var receiver = ResolveReceiver(sm, node);

        if (!IsReactorTouching(diag.Code, receiver)) return null;

        var factories = FactoryIndex.Build(compilation);
        var rosDiag = SyntheticDiagnostic(diag);
        var ctx = new SuggesterContext(compilation, rosDiag, node, receiver, factories);

        // ISuggester.Suggest applies the per-code emit threshold from
        // Thresholds.For(code) internally; a non-silent result here has
        // already cleared the gate.
        Suggestion? best = null;
        foreach (var s in _suggesters)
        {
            SuggestionResult r;
            try { r = s.Suggest(ctx); }
            catch { continue; }
            if (!r.HasSuggestion) continue;
            if (best is null || r.Confidence > best.Confidence)
                best = new Suggestion(r.Text!, r.Confidence, r.Evidence, s.Name);
        }
        return best;
    }

    static SyntaxTree? FindTreeFor(CSharpCompilation c, string file)
    {
        if (string.IsNullOrEmpty(file)) return null;
        // Diagnostic file may be relative or absolute; try the obvious match first.
        // For the fallback suffix match, require a path-separator boundary so a
        // diagnostic on "Program.cs" doesn't accidentally bind to "MyProgram.cs".
        // If the diagnostic carries only a bare filename (no separators), we
        // fall back to GetFileName equality so cross-platform separators work.
        bool hasSeparator = file.Contains('/') || file.Contains('\\');
        string fileName = hasSeparator ? string.Empty : file;

        SyntaxTree? exact = null;
        SyntaxTree? suffix = null;
        foreach (var t in c.SyntaxTrees)
        {
            if (string.Equals(t.FilePath, file, StringComparison.OrdinalIgnoreCase))
            {
                exact = t;
                break;
            }
            if (hasSeparator)
            {
                // Both possible separators — a Windows-built CSharpCompilation
                // can host trees with either, depending on how the project
                // file specified them.
                if (t.FilePath.EndsWith('/' + file, StringComparison.OrdinalIgnoreCase) ||
                    t.FilePath.EndsWith('\\' + file, StringComparison.OrdinalIgnoreCase))
                {
                    suffix = t;
                }
            }
            else if (string.Equals(Path.GetFileName(t.FilePath), fileName, StringComparison.OrdinalIgnoreCase))
            {
                suffix = t;
            }
        }
        return exact ?? suffix;
    }

    static TextSpan? ResolveSpan(SyntaxTree tree, int line1, int col1)
    {
        // MSBuild emits 1-based; Roslyn uses 0-based linePosition.
        try
        {
            var text = tree.GetText();
            if (line1 < 1 || line1 > text.Lines.Count) return null;
            var lineSpan = text.Lines[line1 - 1];
            var col0 = Math.Max(0, col1 - 1);
            var pos = lineSpan.Start + Math.Min(col0, lineSpan.End - lineSpan.Start);
            return new TextSpan(pos, 0);
        }
        catch { return null; }
    }

    internal static SyntaxNode? PickRelevantNode(SyntaxNode root, TextSpan span)
    {
        SyntaxNode? node;
        try { node = root.FindNode(span, getInnermostNodeForTie: true); }
        catch { return null; }
        if (node is null) return null;

        // Walk upwards to one of the shapes our suggester knows how to read.
        for (var n = node; n is not null; n = n.Parent)
        {
            if (n is MemberAccessExpressionSyntax or InvocationExpressionSyntax or IdentifierNameSyntax or ArgumentSyntax)
                return n;
        }
        return node;
    }

    internal static ITypeSymbol? ResolveReceiver(SemanticModel sm, SyntaxNode node)
    {
        if (node is MemberAccessExpressionSyntax m)
            return sm.GetTypeInfo(m.Expression).Type;
        if (node.Parent is MemberAccessExpressionSyntax mp)
            return sm.GetTypeInfo(mp.Expression).Type;
        return null;
    }

    internal static bool IsReactorTouching(string code, ITypeSymbol? receiver)
    {
        // CS0103 / CS1503 / CS7036 self-filter inside the suggester (they probe
        // the FactoryIndex or message text); we always let them through.
        if (code is "CS0103" or "CS1503" or "CS7036") return true;
        // CS1061 / CS0117 require a Reactor-namespaced receiver.
        if (receiver is null) return false;
        return SymbolSuggester.IsReactorType(receiver);
    }

    static Diagnostic SyntheticDiagnostic(CheckCommand.Diag diag)
    {
        var sev = diag.Severity switch
        {
            "error" => DiagnosticSeverity.Error,
            "warning" => DiagnosticSeverity.Warning,
            _ => DiagnosticSeverity.Info,
        };
        var descriptor = new DiagnosticDescriptor(
            id: diag.Code,
            title: diag.Code,
            messageFormat: "{0}",
            category: "compiler",
            defaultSeverity: sev,
            isEnabledByDefault: true);
        return Diagnostic.Create(descriptor, Location.None, diag.Message);
    }
}
