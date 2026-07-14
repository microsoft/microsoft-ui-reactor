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
/// Code fix for <see cref="HookRulesAnalyzer.EagerInitialValueId"/> (<c>REACTOR_HOOKS_013</c>) —
/// wraps an eagerly-allocated <c>UseState</c>/<c>UsePersisted</c> initial value in
/// <c>UseMemo(() =&gt; …, [])</c> so it is allocated once instead of on every render.
/// </summary>
/// <remarks>
/// The wrap mirrors the receiver of the enclosing hook call (<c>ctx.UseState(new …())</c> →
/// <c>ctx.UseState(ctx.UseMemo(() =&gt; new …(), []))</c>; an unqualified call stays unqualified).
/// The fix is only offered when a Reactor <c>UseMemo</c> is actually in scope at the call site, so
/// it never emits code that would fail to compile (<c>UseRef</c> is deliberately not offered — it
/// stores an already-evaluated argument and re-allocates every render exactly like the bug).
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EagerInitialValueCodeFix))]
[Shared]
public sealed class EagerInitialValueCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(HookRulesAnalyzer.EagerInitialValueId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        SemanticModel? semanticModel = null;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var initExpr = node.FirstAncestorOrSelf<ExpressionSyntax>();
            if (initExpr is null) continue;

            // Locate the enclosing UseState/UsePersisted call so we can mirror its receiver.
            var hookCall = initExpr.FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (hookCall is null) continue;

            semanticModel ??= await context.Document
                .GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (semanticModel is null) continue;

            string receiverPrefix = "";
            if (hookCall.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                // ctx.UseState(...) → mirror as ctx.UseMemo(...). The receiver already resolved a
                // Reactor hook, so it exposes UseMemo too; trust it.
                receiverPrefix = memberAccess.Expression.ToString() + ".";
            }
            else
            {
                // Unqualified UseState(...). Only offer the fix when an in-scope, Reactor UseMemo
                // exists — otherwise the wrapped call would not compile.
                if (!semanticModel.LookupSymbols(initExpr.SpanStart, name: "UseMemo").Any(static s =>
                        s is IMethodSymbol m && CommandDebounceAnalyzer.IsReactorNamespace(m.ContainingNamespace?.ToDisplayString())))
                    continue;
            }

            // Target-typed expressions (new() / [ … ]) lose their type inside the untyped lambda
            // `() => expr`; emit an explicit UseMemo<T>. Withhold the fix if the type can't be
            // resolved (never emit non-compiling code).
            var typeArg = CodeFixHelpers.UseMemoTypeArgument(initExpr, semanticModel, context.CancellationToken);
            if (typeArg is null) continue;

            // Wrapping in UseMemo(..., []) changes the initializer from "evaluated every render" to
            // "evaluated once". That is purely a win for an allocation, but if the initializer calls
            // a method it may have side effects whose frequency would change — withhold the fix in
            // that case (the diagnostic still nudges) rather than silently altering behavior.
            if (initExpr.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any())
                continue;

            var captured = initExpr;
            var prefix = receiverPrefix;
            var targ = typeArg;
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Allocate once with UseMemo(() => …, [])",
                    ct =>
                    {
                        var wrapped = SyntaxFactory
                            .ParseExpression($"{prefix}UseMemo{targ}(() => {captured.ToString()}, [])")
                            .WithTriviaFrom(captured);
                        var newRoot = root.ReplaceNode(captured, wrapped);
                        return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                    },
                    equivalenceKey: HookRulesAnalyzer.EagerInitialValueId),
                diagnostic);
        }
    }
}
