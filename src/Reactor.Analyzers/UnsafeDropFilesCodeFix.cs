using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Code fix for REACTOR_INPUT_002: swaps the unsafe
/// <c>DragData.TryGetFiles(out ...)</c> call to
/// <c>DragData.TryGetSafeLocalFiles(out ...)</c>. Both methods share the signature
/// <c>bool(out IReadOnlyList&lt;IStorageItem&gt;)</c>, so only the method-name identifier
/// changes and the rewrite always compiles.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UnsafeDropFilesCodeFix))]
[Shared]
public sealed class UnsafeDropFilesCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(UnsafeDropFilesAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan);
            if (node is not InvocationExpressionSyntax invocation) continue;
            var invokedName = UnsafeDropFilesAnalyzer.GetInvokedName(invocation);
            if (invokedName is null || invokedName.Identifier.Text != UnsafeDropFilesAnalyzer.UnsafeMethodName) continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Use .{UnsafeDropFilesAnalyzer.SafeMethodName}()",
                    ct =>
                    {
                        var newName = SyntaxFactory
                            .IdentifierName(UnsafeDropFilesAnalyzer.SafeMethodName)
                            .WithTriviaFrom(invokedName);
                        var newRoot = root.ReplaceNode(invokedName, newName);
                        return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                    },
                    equivalenceKey: UnsafeDropFilesAnalyzer.DiagnosticId),
                diagnostic);
        }
    }
}
