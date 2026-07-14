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
/// Code fix for <see cref="ContextProvideAnalyzer"/> (<c>REACTOR_CTX_001</c>) — wraps a
/// freshly-allocated context value in <c>UseMemo(() =&gt; …, [])</c> so the same instance is reused
/// across renders and consumers stop thrashing.
/// </summary>
/// <remarks>
/// The deps default to empty (<c>[]</c>, "allocate once"); when the value closes over render state
/// the author widens them. The fix is only offered when a Reactor <c>UseMemo</c> is in scope at the
/// call site so the emitted code always compiles.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ContextProvideCodeFix))]
[Shared]
public sealed class ContextProvideCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ContextProvideAnalyzer.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        SemanticModel? semanticModel = null;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var valueExpr = node.FirstAncestorOrSelf<ExpressionSyntax>();
            if (valueExpr is null) continue;

            semanticModel ??= await context.Document
                .GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (semanticModel is null) continue;

            // Only offer the empty-deps fix when the value is CAPTURE-FREE — it references no
            // render-varying state (locals, parameters, non-const fields, properties, or method
            // calls). A captured value (e.g. new ThemeConfig(isDark)) needs real deps; memoizing it
            // with `[]` would freeze it at the first render's value and silently stop updating
            // consumers, so we withhold the fix and let the author supply deps (the Info diagnostic
            // still nudges). This mirrors the spec's "deps left as a TODO when not inferable".
            if (!IsCaptureFree(valueExpr, semanticModel, context.CancellationToken)) continue;

            // UseMemo is a Component/RenderContext hook; only offer the wrap when a Reactor UseMemo
            // is actually in scope here (otherwise the emitted call would not compile).
            if (!semanticModel.LookupSymbols(valueExpr.SpanStart, name: "UseMemo").Any(static s =>
                    s is IMethodSymbol m && CommandDebounceAnalyzer.IsReactorNamespace(m.ContainingNamespace?.ToDisplayString())))
                continue;

            // Target-typed expressions (new() / [ … ]) lose their type inside the untyped lambda;
            // emit an explicit UseMemo<T>. Withhold if the type can't be resolved.
            var typeArg = CodeFixHelpers.UseMemoTypeArgument(valueExpr, semanticModel, context.CancellationToken);
            if (typeArg is null) continue;

            var captured = valueExpr;
            var targ = typeArg;
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Memoize the context value with UseMemo(() => …, [])",
                    ct =>
                    {
                        var wrapped = SyntaxFactory
                            .ParseExpression($"UseMemo{targ}(() => {captured.ToString()}, [])")
                            .WithTriviaFrom(captured);
                        var newRoot = root.ReplaceNode(captured, wrapped);
                        return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                    },
                    equivalenceKey: ContextProvideAnalyzer.Id),
                diagnostic);
        }
    }

    /// <summary>
    /// True when <paramref name="expr"/> reads no render-varying state, so memoizing it with empty
    /// deps is safe. Rejects: locals / parameters / instance members (via data-flow); method
    /// invocations; and property or non-const static-field reads (e.g. <c>DateTime.Now</c>) that a
    /// data-flow read set does not surface. A value that reads render state (e.g.
    /// <c>new ThemeConfig(isDark)</c>) needs real deps, so the fix is withheld and the author
    /// supplies them (the Info diagnostic still nudges).
    /// </summary>
    private static bool IsCaptureFree(ExpressionSyntax expr, SemanticModel model, System.Threading.CancellationToken ct)
    {
        var flow = model.AnalyzeDataFlow(expr);
        if (flow is null || !flow.Succeeded || !flow.ReadInside.IsEmpty)
            return false;

        // Data-flow only tracks locals / parameters / this. Also reject render-varying reads it
        // does not surface: any method call, and any property or mutable static-field read.
        foreach (var node in expr.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case InvocationExpressionSyntax:
                    return false;

                case MemberAccessExpressionSyntax ma when !IsInitializerTarget(ma):
                    if (ReadsVaryingSymbol(model.GetSymbolInfo(ma, ct).Symbol)) return false;
                    break;

                // A bare identifier that is not the receiver of a member access and not an
                // initializer target (the LHS `X` of `{ X = … }` is a write, not a read).
                case IdentifierNameSyntax id
                    when id.Parent is not MemberAccessExpressionSyntax && !IsInitializerTarget(id):
                    if (ReadsVaryingSymbol(model.GetSymbolInfo(id, ct).Symbol)) return false;
                    break;
            }
        }
        return true;
    }

    private static bool ReadsVaryingSymbol(ISymbol? symbol) => symbol switch
    {
        IPropertySymbol => true,
        IFieldSymbol { IsConst: false, IsStatic: true } => true,
        _ => false,
    };

    private static bool IsInitializerTarget(ExpressionSyntax node) =>
        node.Parent is AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax } assign
        && assign.Left == node;
}
