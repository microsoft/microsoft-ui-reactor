using System.Collections.Immutable;
using System.Composition;
using System.Linq;
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

            semanticModel ??= await context.Document
                .GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (semanticModel is null) continue;

            // Never offer a fix that wouldn't compile or wouldn't actually route through the hook:
            // UseCommand is a Reactor Component hook, so we require a *Reactor* UseCommand to be in
            // scope here. If none is (e.g. a static helper, or a type that does not derive from
            // Component — both of which can still trip the diagnostic via a static Dsl factory bind),
            // wrapping would either not compile (CS0103) or bind to an unrelated same-named helper
            // that doesn't debounce (and would keep warning). In that case we skip the fix — the
            // warning still fires and the author lifts the command by hand. Mirrors the implicit-new
            // guard below: never emit broken or no-op code.
            if (!semanticModel.LookupSymbols(commandExpr.SpanStart, name: "UseCommand").Any(static s =>
                    s is IMethodSymbol m && CommandDebounceAnalyzer.IsReactorNamespace(m.ContainingNamespace?.ToDisplayString())))
                continue;

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

                var type = semanticModel.GetTypeInfo(implicitNew, context.CancellationToken).Type;
                if (type is null || type.TypeKind == TypeKind.Error) continue;

                var explicitNew = MakeExplicit(implicitNew, type, semanticModel);
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
    /// <remarks>
    /// The type name is rendered with <see cref="SymbolDisplayExtensions.ToMinimalDisplayString"/>,
    /// which asks Roslyn's reducer for the shortest name that is unambiguous <i>at this position</i>.
    /// In the common case it stays <c>Command&lt;int&gt;</c>; if a second <c>Command</c>/<c>Command&lt;T&gt;</c>
    /// is imported it adds exactly enough qualification (up to the fully-qualified
    /// <c>Microsoft.UI.Reactor.Core.Command&lt;int&gt;</c>) to avoid a <c>CS0104</c> ambiguity, so the
    /// emitted fix always compiles. A purely syntactic <c>MinimallyQualifiedFormat</c> would emit a
    /// bare <c>Command&lt;int&gt;</c> that could bind the wrong type or be ambiguous — i.e. break code
    /// that previously compiled.
    /// </remarks>
    private static ObjectCreationExpressionSyntax MakeExplicit(
        ImplicitObjectCreationExpressionSyntax implicitNew, ITypeSymbol type, SemanticModel semanticModel)
    {
        var typeSyntax = SyntaxFactory.ParseTypeName(
            type.ToMinimalDisplayString(semanticModel, implicitNew.SpanStart));

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
