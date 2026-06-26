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

        SemanticModel? semanticModel = null;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var commandExpr = node.FirstAncestorOrSelf<ExpressionSyntax>(static e =>
                e is ObjectCreationExpressionSyntax
                  or ImplicitObjectCreationExpressionSyntax
                  or WithExpressionSyntax);
            if (commandExpr is null) continue;

            // A target-typed `new() { … }` is typed by *its surrounding context*. Wrapping it
            // verbatim in `UseCommand(...)` re-targets it to UseCommand's parameter, which for a
            // generic Command<T> silently binds the non-generic UseCommand(Command) overload and
            // produces non-compiling code (CS0123 / a Command where Command<T> is required). So we
            // first rewrite the implicit creation to an explicit `new Command<T> { … }` using the
            // type the analyzer already resolved. If the type can't be resolved, we skip offering
            // the fix entirely rather than emit a broken one — the warning still fires.
            var replacement = commandExpr;
            if (commandExpr is ImplicitObjectCreationExpressionSyntax implicitNew)
            {
                if (implicitNew.Initializer is null) continue;

                semanticModel ??= await context.Document
                    .GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
                var type = semanticModel?.GetTypeInfo(implicitNew, context.CancellationToken).Type;
                if (type is null || type.TypeKind == TypeKind.Error) continue;

                var explicitNew = MakeExplicit(implicitNew, type);
                if (explicitNew is null) continue;
                replacement = explicitNew;
            }

            var inner = replacement;
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Wrap command in UseCommand(...)",
                    ct =>
                    {
                        var wrapped = SyntaxFactory.InvocationExpression(
                            SyntaxFactory.IdentifierName("UseCommand"),
                            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Argument(inner.WithoutTrivia()))))
                            .WithTriviaFrom(commandExpr);

                        var newRoot = root.ReplaceNode(commandExpr, wrapped);
                        return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                    },
                    equivalenceKey: CommandDebounceAnalyzer.Id),
                diagnostic);
        }
    }

    /// <summary>
    /// Rebuilds a target-typed <c>new() { … }</c> as an explicit <c>new Command&lt;T&gt; { … }</c>
    /// using the resolved <paramref name="type"/>, preserving the original initializer (and any
    /// constructor arguments) verbatim so the wrapped result still compiles.
    /// </summary>
    private static ObjectCreationExpressionSyntax MakeExplicit(
        ImplicitObjectCreationExpressionSyntax implicitNew, ITypeSymbol type)
    {
        var typeSyntax = SyntaxFactory.ParseTypeName(
            type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

        ArgumentListSyntax? argumentList = implicitNew.ArgumentList;
        if (argumentList is null || argumentList.Arguments.Count == 0)
        {
            // Drop the empty `()` so the canonical `new Command<T> { … }` shape is produced, and
            // give the type a trailing space to separate it from the initializer brace.
            argumentList = null;
            typeSyntax = typeSyntax.WithTrailingTrivia(SyntaxFactory.Space);
        }

        return SyntaxFactory.ObjectCreationExpression(
            SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space),
            typeSyntax,
            argumentList,
            implicitNew.Initializer);
    }
}
