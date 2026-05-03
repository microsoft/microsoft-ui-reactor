using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Code fix for <c>REACTOR_HOOKS_007</c>: appends the missing capture as an
/// additional argument in the trailing <c>params deps</c> slot of the
/// <c>UseMemoCells</c> / <c>UseMemoCellsByKey</c> / <c>UseMemoCellsByIndex</c>
/// invocation. The capture name travels from the analyzer in
/// <see cref="Diagnostic.Properties"/> under
/// <see cref="UseMemoCellsAnalyzer.CaptureNameProperty"/>; a message-text
/// fallback handles diagnostics produced by older analyzer builds.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseMemoCellsCodeFix))]
[Shared]
public sealed class UseMemoCellsCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(UseMemoCellsAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var span = diagnostic.Location.SourceSpan;
            var node = root.FindNode(span);

            // The diagnostic location is on the lambda; walk up to the invocation.
            var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocation is null) continue;

            // Prefer the structured property the analyzer set; fall back to
            // message-text parsing only if it's missing (i.e., a stale
            // analyzer build emitted the diagnostic).
            string? captureName = null;
            if (diagnostic.Properties.TryGetValue(UseMemoCellsAnalyzer.CaptureNameProperty, out var fromProps)
                && !string.IsNullOrEmpty(fromProps))
            {
                captureName = fromProps;
            }
            else
            {
                captureName = ExtractCaptureName(diagnostic.GetMessage());
            }
            if (string.IsNullOrEmpty(captureName)) continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Add '{captureName}' to dependencies",
                    ct => AddCaptureToDeps(context.Document, invocation, captureName!, ct),
                    equivalenceKey: $"{UseMemoCellsAnalyzer.DiagnosticId}_add_{captureName}"),
                diagnostic);
        }
    }

    private static string? ExtractCaptureName(string message)
    {
        // Fallback path for diagnostics from analyzer builds before the
        // CaptureNameProperty round-trip was introduced. Message format:
        // "'X' is captured by the builder lambda but missing from the dependencies arg list. ..."
        if (string.IsNullOrEmpty(message)) return null;
        int first = message.IndexOf('\'');
        if (first < 0) return null;
        int second = message.IndexOf('\'', first + 1);
        if (second <= first) return null;
        return message.Substring(first + 1, second - first - 1);
    }

    private static async Task<Document> AddCaptureToDeps(
        Document document,
        InvocationExpressionSyntax invocation,
        string captureName,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        var newArg = SyntaxFactory.Argument(SyntaxFactory.IdentifierName(captureName));

        // If the invocation already has the deps as an explicit array, append
        // into the array initializer. Otherwise add a trailing positional arg.
        var argList = invocation.ArgumentList;
        var args = argList.Arguments;

        // Heuristic: detect a single trailing array literal. If the last
        // argument is an array creation expression, treat that as the deps
        // array and append to its initializer.
        if (args.Count > 0)
        {
            var last = args[args.Count - 1];
            if (last.Expression is ArrayCreationExpressionSyntax arr && arr.Initializer is { } init)
            {
                var newInit = init.AddExpressions(SyntaxFactory.IdentifierName(captureName));
                var newArr = arr.WithInitializer(newInit);
                var newLast = last.WithExpression(newArr);
                var newArgs = args.Replace(last, newLast);
                var newRoot = root.ReplaceNode(argList, argList.WithArguments(newArgs));
                return document.WithSyntaxRoot(newRoot);
            }
            if (last.Expression is ImplicitArrayCreationExpressionSyntax implArr)
            {
                var newInit2 = implArr.Initializer.AddExpressions(SyntaxFactory.IdentifierName(captureName));
                var newImpl = implArr.WithInitializer(newInit2);
                var newLast = last.WithExpression(newImpl);
                var newArgs = args.Replace(last, newLast);
                var newRoot = root.ReplaceNode(argList, argList.WithArguments(newArgs));
                return document.WithSyntaxRoot(newRoot);
            }
            if (last.Expression is CollectionExpressionSyntax coll)
            {
                var newColl = coll.AddElements(SyntaxFactory.ExpressionElement(SyntaxFactory.IdentifierName(captureName)));
                var newLast = last.WithExpression(newColl);
                var newArgs = args.Replace(last, newLast);
                var newRoot = root.ReplaceNode(argList, argList.WithArguments(newArgs));
                return document.WithSyntaxRoot(newRoot);
            }
        }

        // Default: append as trailing positional argument (params).
        var updated = argList.AddArguments(newArg);
        var newRoot2 = root.ReplaceNode(argList, updated);
        return document.WithSyntaxRoot(newRoot2);
    }
}
