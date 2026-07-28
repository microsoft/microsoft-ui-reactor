using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Code fix for <see cref="RawCommandCallbackAnalyzer"/> (<c>REACTOR_CMD_001</c>) — deletes the
/// redundant own callback that shadows the bound <c>Command</c>, so the command runs again.
/// </summary>
/// <remarks>
/// The callback to remove is identified by <see cref="RawCommandCallbackAnalyzer.CallbackKindKey"/>
/// in <see cref="Diagnostic.Properties"/>: an object-initializer assignment (<c>{ …, OnClick = h }</c>)
/// or a constructor argument (<c>new ButtonElement("Save", DoThing)</c>). We only ever <b>delete</b>
/// the callback — never fold its body into <c>cmd.Execute</c> (that bypasses <c>CanExecute</c>,
/// breaks <c>UseCommand</c>/<c>DebounceMs</c> arming, and can swallow an async <c>ExecuteAsync</c>).
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RawCommandCallbackCodeFix))]
[Shared]
public sealed class RawCommandCallbackCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(RawCommandCallbackAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(RawCommandCallbackAnalyzer.CallbackKindKey, out var kind) || kind is null)
                continue;

            diagnostic.Properties.TryGetValue(RawCommandCallbackAnalyzer.CallbackNameKey, out var callbackName);
            var title = $"Remove redundant '{callbackName ?? "callback"}'";

            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

            if (kind == RawCommandCallbackAnalyzer.KindInitializer)
            {
                var assignment = node as AssignmentExpressionSyntax ?? node.FirstAncestorOrSelf<AssignmentExpressionSyntax>();
                if (assignment?.Parent is not InitializerExpressionSyntax initializer)
                    continue;

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title,
                        ct => RemoveInitializerAssignmentAsync(context.Document, root, initializer, assignment, ct),
                        equivalenceKey: RawCommandCallbackAnalyzer.DiagnosticId + ":init"),
                    diagnostic);
            }
            else if (kind == RawCommandCallbackAnalyzer.KindCtorArg)
            {
                var argument = node as ArgumentSyntax ?? node.FirstAncestorOrSelf<ArgumentSyntax>();
                if (argument?.Parent is not ArgumentListSyntax argumentList)
                    continue;

                // Deleting the callback argument is only safe when no *positional* argument follows
                // it: a following positional would shift down into the callback parameter's slot and
                // rebind (e.g. removing OnClick from `new SplitButtonElement("S", h, flyout)` would
                // bind `flyout` to `OnClick`). A named argument can always be removed. When it isn't
                // safe we withhold the fix — the analyzer still reports, the author removes by hand.
                if (!IsSafeToRemoveArgument(argumentList, argument))
                    continue;

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title,
                        ct => RemoveArgumentAsync(context.Document, root, argumentList, argument, ct),
                        equivalenceKey: RawCommandCallbackAnalyzer.DiagnosticId + ":ctor"),
                    diagnostic);
            }
        }
    }

    private static Task<Document> RemoveInitializerAssignmentAsync(
        Document document, SyntaxNode root, InitializerExpressionSyntax initializer, AssignmentExpressionSyntax assignment, CancellationToken cancellationToken)
    {
        var newInitializer = initializer
            .WithExpressions(initializer.Expressions.Remove(assignment))
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newDocument = document.WithSyntaxRoot(root.ReplaceNode(initializer, newInitializer));
        return Formatter.FormatAsync(newDocument, Formatter.Annotation, cancellationToken: cancellationToken);
    }

    private static Task<Document> RemoveArgumentAsync(
        Document document, SyntaxNode root, ArgumentListSyntax argumentList, ArgumentSyntax argument, CancellationToken cancellationToken)
    {
        var newArgumentList = argumentList
            .WithArguments(argumentList.Arguments.Remove(argument))
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newDocument = document.WithSyntaxRoot(root.ReplaceNode(argumentList, newArgumentList));
        return Formatter.FormatAsync(newDocument, Formatter.Annotation, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// A named argument is always safe to delete. A positional argument is safe only when no
    /// positional argument follows it — otherwise removing it would shift a later positional
    /// argument into the deleted parameter's slot and silently rebind it.
    /// </summary>
    private static bool IsSafeToRemoveArgument(ArgumentListSyntax argumentList, ArgumentSyntax argument)
    {
        if (argument.NameColon is not null)
            return true;

        var index = argumentList.Arguments.IndexOf(argument);
        for (var i = index + 1; i < argumentList.Arguments.Count; i++)
        {
            if (argumentList.Arguments[i].NameColon is null)
                return false;
        }

        return true;
    }
}
