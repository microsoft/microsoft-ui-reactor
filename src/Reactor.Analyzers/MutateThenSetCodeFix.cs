using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Code fix for <see cref="HookRulesAnalyzer.MutateThenSetId"/> (<c>REACTOR_HOOKS_010</c>) for the
/// common <c>items.Add(v); setItems(items);</c> shape: drops the in-place mutation and passes a
/// NEW value to the setter — <c>setItems([.. items, v]);</c>.
/// </summary>
/// <remarks>
/// The rewrite emits a <b>value</b> (a collection expression), never a functional updater
/// (<c>setItems(prev =&gt; …)</c>) — the UseState/UsePersisted setter is <c>Action&lt;T&gt;</c>, not
/// <c>Action&lt;Func&lt;T,T&gt;&gt;</c>. Only the single-argument <c>.Add(v)</c> mutation is fixable
/// (flagged via the diagnostic's <c>canFix</c> property); other mutators (<c>Remove</c>,
/// <c>Clear</c>, indexer set, …) keep the warning with no auto-fix.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MutateThenSetCodeFix))]
[Shared]
public sealed class MutateThenSetCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(HookRulesAnalyzer.MutateThenSetId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        SemanticModel? semanticModel = null;

        foreach (var diagnostic in context.Diagnostics)
        {
            // The analyzer only marks the single-arg .Add(v) shape as fixable.
            if (!diagnostic.Properties.TryGetValue("canFix", out var canFix) || canFix != "true") continue;
            if (diagnostic.AdditionalLocations.Count == 0) continue;

            // Setter call: setItems(items).
            var setterCall = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                .FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (setterCall is null) continue;
            var setterArgs = setterCall.ArgumentList.Arguments;
            if (setterArgs.Count != 1) continue;
            var itemsExpr = setterArgs[0].Expression;

            // Only offer the collection-expression rewrite when the state type can actually be
            // built from a collection expression — otherwise `setItems([.. items, v])` would not
            // compile for an exotic collection type. The diagnostic still fires for the author.
            semanticModel ??= await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (semanticModel is null) continue;
            var stateType = semanticModel.GetTypeInfo(itemsExpr, context.CancellationToken).Type;
            if (!SupportsCollectionExpression(stateType)) continue;

            // Mutator call: items.Add(v).
            var mutatorCall = root.FindNode(diagnostic.AdditionalLocations[0].SourceSpan, getInnermostNodeForTie: true)
                .FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (mutatorCall is null) continue;
            if (mutatorCall.ArgumentList.Arguments.Count != 1) continue;
            var valueExpr = mutatorCall.ArgumentList.Arguments[0].Expression;

            var setterStatement = setterCall.FirstAncestorOrSelf<ExpressionStatementSyntax>();
            if (setterStatement is null) continue;

            var mutatorStatement = mutatorCall.FirstAncestorOrSelf<ExpressionStatementSyntax>();
            if (mutatorStatement is null) continue;

            var itemsText = itemsExpr.ToString();
            var valueText = valueExpr.ToString();

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Set a new value with a collection expression",
                    ct =>
                    {
                        var editor = new SyntaxEditor(root, context.Document.Project.Solution.Workspace.Services);

                        // setItems(items) → setItems([.. items, v])
                        var collection = SyntaxFactory.ParseExpression($"[.. {itemsText}, {valueText}]");
                        var newSetterCall = setterCall.WithArgumentList(
                            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Argument(collection))));
                        var newSetterStatement = setterStatement.ReplaceNode(setterCall, newSetterCall);
                        editor.ReplaceNode(setterStatement, newSetterStatement);

                        // Remove the now-redundant `items.Add(v);`. When it carries comments or
                        // #directives, keep them via Roslyn's trivia-aware removal so user content is
                        // never dropped and #if/#endif regions stay balanced (this can leave a blank
                        // line where the statement was — an acceptable cosmetic cost the author tidies
                        // up). Otherwise remove cleanly with no leftover whitespace.
                        var keepTrivia = mutatorStatement.ContainsDirectives
                            || mutatorStatement.GetLeadingTrivia().Any(IsComment)
                            || mutatorStatement.GetTrailingTrivia().Any(IsComment);
                        editor.RemoveNode(mutatorStatement, keepTrivia
                            ? SyntaxRemoveOptions.KeepLeadingTrivia | SyntaxRemoveOptions.KeepTrailingTrivia | SyntaxRemoveOptions.KeepDirectives
                            : SyntaxRemoveOptions.KeepNoTrivia);

                        return Task.FromResult(context.Document.WithSyntaxRoot(editor.GetChangedRoot()));
                    },
                    equivalenceKey: HookRulesAnalyzer.MutateThenSetId),
                diagnostic);
        }
    }

    private static bool IsComment(SyntaxTrivia t) =>
        t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia);

    /// <summary>
    /// True when a value of <paramref name="type"/> can be produced by a collection expression
    /// (<c>[.. items, v]</c>): an array/span, one of the collection interfaces the compiler
    /// materializes as a <c>List&lt;T&gt;</c>, or a concrete <c>IEnumerable</c> with an accessible
    /// parameterless constructor. Withholding the fix for anything else avoids emitting code that
    /// would not compile for an exotic collection type.
    /// </summary>
    private static bool SupportsCollectionExpression(ITypeSymbol? type)
    {
        if (type is IArrayTypeSymbol)
            return true;
        if (type is not INamedTypeSymbol named)
            return false;

        if (named.TypeKind == TypeKind.Interface)
        {
            var iface = named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return iface is "global::System.Collections.Generic.IEnumerable<T>"
                or "global::System.Collections.Generic.IReadOnlyList<T>"
                or "global::System.Collections.Generic.IReadOnlyCollection<T>"
                or "global::System.Collections.Generic.IList<T>"
                or "global::System.Collections.Generic.ICollection<T>";
        }

        var implementsEnumerable = named.AllInterfaces.Any(static i => i.SpecialType == SpecialType.System_Collections_IEnumerable);
        return implementsEnumerable
            && named.InstanceConstructors.Any(static c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0);
    }
}
