using System.Collections.Generic;
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
/// Code fix for <see cref="MemoizeCommandAnalyzer"/> (<c>REACTOR_PERF_FUNCREF</c>) — wraps the
/// offending <c>new Command { … }</c> in <c>UseMemo(() =&gt; new Command { … }, deps)</c> so the
/// command keeps a stable instance across renders.
/// </summary>
/// <remarks>
/// <para>
/// The dependency list is computed from a data-flow analysis of the creation expression: every
/// local / parameter read inside it (directly or captured by a nested lambda such as
/// <c>Execute = () =&gt; setCount(count + 1)</c>) that is declared outside becomes a
/// <c>UseMemo</c> dependency, so the memo re-computes exactly when a captured value changes and
/// never serves a stale closure. When nothing is captured the deps list is empty
/// (<c>UseMemo(() =&gt; new Command { … })</c>) — a compute-once memo, which is safe precisely
/// because there is nothing to go stale.
/// </para>
/// <para>
/// A Reactor <c>UseMemo</c> must be in scope for the wrap to compile, so the fix is only offered
/// inside a <c>Component</c> / <c>RenderContext</c> body. A target-typed <c>new() { … }</c> is
/// rewritten to an explicit <c>new Command&lt;T&gt; { … }</c> first (its target type is lost once it
/// becomes the body of the memo lambda). Mirrors <see cref="CommandDebounceCodeFix"/>.
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MemoizeCommandCodeFix))]
[Shared]
public sealed class MemoizeCommandCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MemoizeCommandAnalyzer.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        SemanticModel? semanticModel = null;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var creation = node.FirstAncestorOrSelf<ExpressionSyntax>(static e =>
                e is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax);
            if (creation is null) continue;

            semanticModel ??= await context.Document
                .GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (semanticModel is null) continue;

            // UseMemo is a Reactor Component / RenderContext hook, so it must be a *Reactor* UseMemo
            // in scope here for the wrap to compile and actually memoize. If none is (e.g. a static
            // helper, or a same-named unrelated method), skip the fix — the Info diagnostic still
            // fires and the author lifts the command by hand. Never emit broken or no-op code.
            if (!semanticModel.LookupSymbols(creation.SpanStart, name: "UseMemo").Any(static s =>
                    s is IMethodSymbol m && CommandDebounceAnalyzer.IsReactorNamespace(m.ContainingNamespace?.ToDisplayString())))
                continue;

            // Decline the fix when the command reads a mutable component instance field/property at
            // render time (outside a deferred lambda): that value is snapshotted into the init-only
            // Command property, and `this` is (correctly) never a UseMemo dependency, so a wrapped memo
            // would serve a STALE value when the member changes. We can't turn an arbitrary member read
            // into a dependency, so we leave the command un-fixed — the Info diagnostic still nudges the
            // author to memoize by hand with the right deps. Reads that occur only inside a deferred
            // lambda (e.g. Execute = () => _count++) re-read live and are safe.
            if (ReadsRenderTimeInstanceMember(creation, semanticModel, context.CancellationToken))
                continue;

            // A target-typed `new() { … }` is typed by its surrounding context; once it becomes the
            // body of the memo lambda that context is gone. Rewrite it to an explicit
            // `new Command<T> { … }` using the resolved type, or skip if it can't be resolved.
            ExpressionSyntax inner = creation;
            if (creation is ImplicitObjectCreationExpressionSyntax implicitNew)
            {
                if (implicitNew.Initializer is null) continue;
                var type = semanticModel.GetTypeInfo(implicitNew, context.CancellationToken).Type;
                if (type is null || type.TypeKind == TypeKind.Error) continue;
                inner = MakeExplicit(implicitNew, type, semanticModel);
            }

            var deps = ComputeDependencies(semanticModel, creation, context.CancellationToken);

            var creationForClosure = creation;
            var innerForClosure = inner;
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Wrap command in UseMemo(...)",
                    ct =>
                    {
                        // `() => <command>` with explicit single-space arrow trivia so the emitted
                        // fix reads `() => new Command { … }` regardless of factory defaults. Strip only
                        // the command's OUTER leading/trailing trivia (not WithoutTrivia(), which would
                        // also drop interior comments/newlines inside the initializer).
                        var lambda = SyntaxFactory.ParenthesizedLambdaExpression(
                            SyntaxFactory.ParameterList(),
                            innerForClosure.WithLeadingTrivia().WithTrailingTrivia())
                            .WithArrowToken(SyntaxFactory.Token(SyntaxKind.EqualsGreaterThanToken)
                                .WithLeadingTrivia(SyntaxFactory.Space)
                                .WithTrailingTrivia(SyntaxFactory.Space));

                        // Build the argument list with explicit `, ` separators (a comma with a
                        // trailing space) so `UseMemo(() => …, count, setCount)` is formatted normally.
                        var nodesAndTokens = new List<SyntaxNodeOrToken> { SyntaxFactory.Argument(lambda) };
                        foreach (var dep in deps)
                        {
                            nodesAndTokens.Add(SyntaxFactory.Token(SyntaxKind.CommaToken)
                                .WithTrailingTrivia(SyntaxFactory.Space));
                            nodesAndTokens.Add(SyntaxFactory.Argument(DependencyIdentifier(dep)));
                        }

                        var wrapped = SyntaxFactory.InvocationExpression(
                            SyntaxFactory.IdentifierName("UseMemo"),
                            SyntaxFactory.ArgumentList(
                                SyntaxFactory.SeparatedList<ArgumentSyntax>(nodesAndTokens)))
                            .WithTriviaFrom(creationForClosure);

                        var newRoot = root.ReplaceNode(creationForClosure, wrapped);
                        return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                    },
                    equivalenceKey: MemoizeCommandAnalyzer.Id),
                diagnostic);
        }
    }

    // The captured dependencies of the creation expression: locals / parameters that are read inside
    // it — directly or captured by a nested lambda — and declared outside. These become the UseMemo
    // deps so the memo re-computes exactly when a captured value changes (no stale closure). The union
    // of DataFlowsIn / ReadInside / CapturedInside covers both direct reads and nested-lambda captures;
    // VariablesDeclared removes anything local to the expression itself. Ordered for deterministic output.
    private static ImmutableArray<string> ComputeDependencies(
        SemanticModel model, ExpressionSyntax creation, System.Threading.CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var flow = model.AnalyzeDataFlow(creation);
        if (flow is null || !flow.Succeeded) return ImmutableArray<string>.Empty;

        var declared = new HashSet<ISymbol>(flow.VariablesDeclared, SymbolEqualityComparer.Default);
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var names = new List<string>();

        foreach (var symbol in flow.DataFlowsIn.Concat(flow.ReadInside).Concat(flow.CapturedInside))
        {
            if (symbol is not (ILocalSymbol or IParameterSymbol)) continue;
            // `this` (an implicit parameter, captured whenever the command references an instance
            // member such as `Execute = Save`) is stable across renders — never a memo dependency.
            if (symbol is IParameterSymbol { IsThis: true }) continue;
            if (declared.Contains(symbol)) continue;
            if (!seen.Add(symbol)) continue;
            names.Add(symbol.Name);
        }

        names.Sort(System.StringComparer.Ordinal);
        return names.ToImmutableArray();
    }

    // True when the creation expression reads a mutable component instance field or non-static property
    // at RENDER TIME — i.e. outside any nested lambda / anonymous method (whose body defers to invoke
    // time and re-reads the member live). Such a render-time read is snapshotted into the init-only
    // Command property, and `this` is never a UseMemo dependency, so a wrapped memo would serve a stale
    // value. We skip: reads inside a deferred lambda (safe), the object-initializer assignment targets
    // (the Command's own properties, e.g. `Label = …`), and the member name of a `receiver.Member`
    // access whose receiver is not `this` (that member belongs to a local/parameter/type whose root is
    // captured as a dependency or is static).
    private static bool ReadsRenderTimeInstanceMember(
        ExpressionSyntax creation, SemanticModel model, System.Threading.CancellationToken ct)
    {
        foreach (var id in creation.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (IsInsideNestedAnonymousFunction(id, creation)) continue;

            if (id.Parent is AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax } assign
                && assign.Left == id)
                continue;

            // Skip the member name of a `receiver.Member` access whose receiver is neither `this` nor
            // `base` — that member belongs to a local/parameter/type whose root is captured as a
            // dependency (or is static). `this.`/`base.`/implicit instance reads are the stale hazard.
            if (id.Parent is MemberAccessExpressionSyntax ma && ma.Name == id
                && ma.Expression is not (ThisExpressionSyntax or BaseExpressionSyntax))
                continue;

            if (model.GetSymbolInfo(id, ct).Symbol is IFieldSymbol { IsStatic: false, IsConst: false }
                or IPropertySymbol { IsStatic: false })
                return true;
        }
        return false;
    }

    private static bool IsInsideNestedAnonymousFunction(SyntaxNode node, ExpressionSyntax boundary)
    {
        for (var n = node.Parent; n is not null && n != boundary; n = n.Parent)
        {
            if (n is SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
                return true;
        }
        return false;
    }

    // A local/parameter whose name is a reserved keyword can only exist in source as an escaped
    // identifier (`@event`), and Roslyn reports its Name without the `@` ("event"). Parse the escaped
    // form so the emitted `UseMemo(…, @event)` dependency is a proper verbatim identifier (correct
    // Text AND ValueText) that both compiles and round-trips. Contextual keywords (value, async, …)
    // are valid unescaped identifiers, so they need no `@`.
    private static IdentifierNameSyntax DependencyIdentifier(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
            ? (IdentifierNameSyntax)SyntaxFactory.ParseName("@" + name)
            : SyntaxFactory.IdentifierName(name);

    /// <summary>
    /// Rebuilds a target-typed <c>new() { … }</c> as an explicit <c>new Command&lt;T&gt; { … }</c>
    /// using the resolved <paramref name="type"/>, preserving the initializer (and any constructor
    /// arguments) verbatim — including any trivia between <c>new(…)</c> and the initializer brace. The
    /// type name is rendered with <see cref="SymbolDisplayExtensions.ToMinimalDisplayString"/> so it
    /// stays short yet unambiguous at this position. Mirrors <see cref="CommandDebounceCodeFix"/>.
    /// </summary>
    private static ObjectCreationExpressionSyntax MakeExplicit(
        ImplicitObjectCreationExpressionSyntax implicitNew, ITypeSymbol type, SemanticModel semanticModel)
    {
        var typeSyntax = SyntaxFactory.ParseTypeName(
            type.ToMinimalDisplayString(semanticModel, implicitNew.SpanStart));

        ArgumentListSyntax? argumentList = implicitNew.ArgumentList;
        if (argumentList is null || argumentList.Arguments.Count == 0)
            argumentList = null;

        // Preserve the initializer verbatim (keeps any comments/newlines the author placed before the
        // brace). Only when the dropped `()` leaves the type directly abutting an initializer with no
        // leading trivia do we add a single separating space, so the result is never `Command{ … }`.
        var initializer = implicitNew.Initializer!;
        if (argumentList is null && initializer.GetLeadingTrivia().Count == 0)
            typeSyntax = typeSyntax.WithTrailingTrivia(SyntaxFactory.Space);

        return SyntaxFactory.ObjectCreationExpression(
            SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space),
            typeSyntax,
            argumentList,
            initializer);
    }
}
