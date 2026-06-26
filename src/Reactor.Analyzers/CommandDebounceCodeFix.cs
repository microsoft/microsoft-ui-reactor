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
/// Code fix for <see cref="CommandDebounceAnalyzer"/> (<c>REACTOR_HOOKS_009</c>) — wraps the
/// offending <c>new Command { … DebounceMs = … }</c> / <c>… with { DebounceMs = … }</c> in a
/// <c>UseCommand(...)</c> call so the debounce state has a hook store to persist in.
/// </summary>
/// <remarks>
/// The wrap is applied to the creation expression itself, so it works for both the inline bind
/// (<c>Button(new Command{…})</c> → <c>Button(UseCommand(new Command{…}))</c>) and the local
/// case (<c>var c = new Command{…};</c> → <c>var c = UseCommand(new Command{…});</c>), where the
/// already-correct downstream <c>Button(c)</c> then binds the wrapped, debounced command.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CommandDebounceCodeFix))]
[Shared]
public sealed class CommandDebounceCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(CommandDebounceAnalyzer.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var commandExpr = node.FirstAncestorOrSelf<ExpressionSyntax>(static e =>
                e is ObjectCreationExpressionSyntax
                  or ImplicitObjectCreationExpressionSyntax
                  or WithExpressionSyntax);
            if (commandExpr is null) continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Wrap command in UseCommand(...)",
                    ct =>
                    {
                        var wrapped = SyntaxFactory.InvocationExpression(
                            SyntaxFactory.IdentifierName("UseCommand"),
                            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Argument(commandExpr.WithoutTrivia()))))
                            .WithTriviaFrom(commandExpr);

                        var newRoot = root.ReplaceNode(commandExpr, wrapped);
                        return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                    },
                    equivalenceKey: CommandDebounceAnalyzer.Id),
                diagnostic);
        }
    }
}
