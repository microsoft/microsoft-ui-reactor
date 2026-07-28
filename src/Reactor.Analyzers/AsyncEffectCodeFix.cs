using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Template/preview code fix for <see cref="HookRulesAnalyzer.AsyncEffectId"/>
/// (<c>REACTOR_HOOKS_003</c>). Hoists the body of an <c>async</c> <c>UseEffect</c> lambda into a
/// local <c>async Task RunAsync(CancellationToken)</c> and rewrites the effect to a synchronous
/// shape that starts the task and cancels it on cleanup.
/// </summary>
/// <remarks>
/// This is offered as a preview (it moves user code around and can't perfectly infer intent), not
/// a silent one-click rewrite. Emitted type references are <c>global::</c>-qualified so the result
/// compiles regardless of the call site's <c>using</c>s.
/// <code>
/// UseEffect(() =>
/// {
///     var cts = new System.Threading.CancellationTokenSource();
///     _ = RunAsync(cts.Token);
///     return () => { cts.Cancel(); cts.Dispose(); };
///
///     async System.Threading.Tasks.Task RunAsync(System.Threading.CancellationToken ct)
///     {
///         // original body
///     }
/// }, deps);
/// </code>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AsyncEffectCodeFix))]
[Shared]
public sealed class AsyncEffectCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(HookRulesAnalyzer.AsyncEffectId);

    // A move-user-code refactoring: don't batch it across a document.
    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var lambda = node.FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>();
            if (lambda is null) continue;

            var statements = ExtractBodyStatements(lambda);
            if (statements is null) continue;

            var captured = lambda;
            var body = statements;
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Extract async body into a cancelable Task (preview)",
                    ct =>
                    {
                        var baseIndent = GetLineIndent(root, captured.SpanStart);
                        var newLine = GetNewLine(root);
                        var replacement = SyntaxFactory
                            .ParseExpression(BuildReplacement(body, baseIndent, newLine))
                            .WithTriviaFrom(captured);
                        var newRoot = root.ReplaceNode(captured, replacement);
                        return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                    },
                    equivalenceKey: HookRulesAnalyzer.AsyncEffectId),
                diagnostic);
        }
    }

    /// <summary>
    /// Returns the statements to move into <c>RunAsync</c>, each as trimmed text. For a block body
    /// that is the block's inner statements; for an expression-bodied lambda
    /// (<c>async () =&gt; await F()</c>) the single expression turned into a statement.
    /// </summary>
    private static IReadOnlyList<string>? ExtractBodyStatements(AnonymousFunctionExpressionSyntax lambda)
    {
        switch (lambda.Body)
        {
            case BlockSyntax block:
                return block.Statements.Select(static s => s.ToString()).ToList();
            case ExpressionSyntax expression:
                return new List<string> { expression.WithoutTrivia().ToString() + ";" };
            default:
                return null;
        }
    }

    /// <summary>The leading whitespace of the line containing <paramref name="position"/>.</summary>
    private static string GetLineIndent(SyntaxNode root, int position)
    {
        var text = root.SyntaxTree.GetText();
        var line = text.Lines.GetLineFromPosition(position).ToString();
        int end = 0;
        while (end < line.Length && (line[end] == ' ' || line[end] == '\t')) end++;
        return line.Substring(0, end);
    }

    /// <summary>The document's dominant line ending, so the inserted block matches the file.</summary>
    private static string GetNewLine(SyntaxNode root)
    {
        var text = root.SyntaxTree.GetText().ToString();
        int idx = text.IndexOf('\n');
        return idx > 0 && text[idx - 1] == '\r' ? "\r\n" : "\n";
    }

    private static string BuildReplacement(IReadOnlyList<string> bodyStatements, string baseIndent, string nl)
    {
        var sb = new StringBuilder();
        sb.Append("() =>").Append(nl);
        sb.Append(baseIndent).Append('{').Append(nl);
        sb.Append(baseIndent).Append("    var cts = new global::System.Threading.CancellationTokenSource();").Append(nl);
        sb.Append(baseIndent).Append("    _ = RunAsync(cts.Token);").Append(nl);
        sb.Append(baseIndent).Append("    return () => { cts.Cancel(); cts.Dispose(); };").Append(nl);
        sb.Append(nl);
        sb.Append(baseIndent).Append("    async global::System.Threading.Tasks.Task RunAsync(global::System.Threading.CancellationToken ct)").Append(nl);
        sb.Append(baseIndent).Append("    {").Append(nl);
        foreach (var statement in bodyStatements)
            sb.Append(baseIndent).Append("        ").Append(statement).Append(nl);
        sb.Append(baseIndent).Append("    }").Append(nl);
        sb.Append(baseIndent).Append('}');
        return sb.ToString();
    }
}
