using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_MEMO_001 — a fluent modifier applied to a <b>keyed</b>
/// <c>Memo(key, factory)</c> wrapper silently opts the row out of the virtualized
/// cross-recycle cache.
/// </summary>
/// <remarks>
/// The opt-in keyed memo (<c>Factories.Memo&lt;TKey&gt;(TKey key, Func&lt;Element&gt; factory)</c> →
/// <see cref="Core.KeyedMemoElement"/>) is only served from the owning
/// <c>ElementFactory</c>'s bounded cross-recycle LRU when the wrapper is <em>bare</em>:
/// <c>km.Modifiers is null &amp;&amp; km.Key is null &amp;&amp; km.Extensions is null</c>
/// (<c>ElementFactory.BuildOrCache</c>). Decorating the wrapper — e.g.
/// <c>Memo(item.Id, () =&gt; Row(item)).Padding(8)</c> — sets <c>Modifiers</c> (or
/// <c>Key</c>/<c>Extensions</c>), so the row falls through the cache and is rebuilt +
/// re-diffed on every recycle, quietly losing the perf benefit. Because the reconciler
/// treats the wrapper transparently (it re-applies the wrapper's modifiers to the inner
/// element on mount/update), moving those modifiers onto the element the factory returns
/// (<c>Memo(item.Id, () =&gt; Row(item).Padding(8))</c>) is behaviorally identical <em>and</em>
/// keeps the wrapper cacheable.
///
/// <para>Distinguishing the keyed overload from the non-keyed
/// <c>Memo(Func&lt;RenderContext, Element&gt;, params object?[])</c> (→ <c>MemoElement</c>) is
/// done <em>semantically</em>, by resolving the receiver call's return type — the two share
/// the <c>Memo</c> name but only the keyed one participates in the cross-recycle cache.</para>
///
/// <para>Conservative — fires only when a mechanical fix is possible: the factory is a
/// parameterless lambda with an expression body (or a single-<c>return</c> block), so its
/// body can carry the moved modifiers. Method-group / multi-statement / non-keyed receivers
/// do not fire.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MemoWrapperModifierAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_MEMO_001";

    private const string ElementTypeName = "Element";
    private const string KeyedMemoTypeName = "KeyedMemoElement";
    private const string ReactorCoreNamespace = "Microsoft.UI.Reactor.Core";

    private static readonly LocalizableString Title =
        "Modifier on a keyed Memo wrapper opts out of the recycle cache";

    private static readonly LocalizableString MessageFormat =
        "Modifier '{0}' is applied to the keyed 'Memo(key, factory)' wrapper, which opts the row out of the cross-recycle cache. Move the modifier(s) inside the factory body instead.";

    private static readonly LocalizableString Description =
        "A keyed 'Memo(key, factory)' row is only served from the virtualized-list cross-recycle " +
        "cache while the wrapper is bare (no modifiers, key, or attached state). Decorating the " +
        "wrapper — e.g. 'Memo(id, () => Row(item)).Padding(8)' — makes the row bypass the cache and " +
        "rebuild + re-diff on every recycle, silently losing the perf benefit. Move the modifiers " +
        "onto the element the factory returns — 'Memo(id, () => Row(item).Padding(8))' — to keep the " +
        "wrapper cacheable. Because the memo then caches the modified element per key, make sure every " +
        "input the moved modifiers read is captured by the key (fold selection/theme flags into the " +
        "key, e.g. 'Memo((id, isSelected), () => ...)'); otherwise a cache hit can serve stale content.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Performance",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var modifierInvocation = (InvocationExpressionSyntax)context.Node;

        // Shape: `<receiver>.Modifier(args)`.
        if (modifierInvocation.Expression is not MemberAccessExpressionSyntax modifierAccess)
            return;

        // The receiver must be *directly* a call — the raw `Memo(...)` invocation. This is
        // what makes the rule fire exactly once per chain: for `Memo(k, f).Padding(8).Margin(4)`
        // only `.Padding(8)`'s receiver is the bare `Memo(...)` call; `.Margin(4)`'s receiver is
        // the `Memo(...).Padding(8)` sub-chain, whose callee is `Padding`, not `Memo`.
        if (modifierAccess.Expression is not InvocationExpressionSyntax memoInvocation)
            return;

        // Cheap syntactic gate before any semantic work: the receiver's callee is named `Memo`.
        if (!IsCalleeNamed(memoInvocation.Expression, "Memo"))
            return;

        // Only fire when a mechanical fix is possible: the factory is a movable lambda. This also
        // enforces the keyed overload's 2-argument (key, factory) shape syntactically, so most
        // non-keyed `Memo(...)` calls are rejected before the semantic model is touched.
        if (!TryGetMovableFactory(memoInvocation, out _, out _, out _))
            return;

        // Semantic confirm: the receiver resolves to the KEYED overload. The keyed
        // `Memo<TKey>(TKey, Func<Element>)` returns KeyedMemoElement; the non-keyed
        // `Memo(Func<RenderContext,Element>, params object?[])` returns MemoElement and must not fire.
        if (context.SemanticModel.GetSymbolInfo(memoInvocation, context.CancellationToken).Symbol
                is not IMethodSymbol memoSymbol
            || memoSymbol.Name != "Memo"
            || !IsNamedType(memoSymbol.ReturnType, KeyedMemoTypeName, ReactorCoreNamespace))
            return;

        // The trailing call must be a real Element modifier (returns an Element), not e.g.
        // `.ToString()` / `.GetHashCode()` on the wrapper.
        if (context.SemanticModel.GetSymbolInfo(modifierInvocation, context.CancellationToken).Symbol
                is not IMethodSymbol modifierSymbol
            || !DerivesFromElement(modifierSymbol.ReturnType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            modifierAccess.Name.GetLocation(),
            modifierAccess.Name.Identifier.ValueText));
    }

    /// <summary>The callee expression's simple name equals <paramref name="name"/>.</summary>
    private static bool IsCalleeNamed(ExpressionSyntax callee, string name) => callee switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText == name,
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText == name,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText == name,
        _ => false,
    };

    /// <summary>
    /// The keyed <c>Memo(key, factory)</c> overload takes exactly two arguments; the factory is a
    /// parameterless lambda (<c>Func&lt;Element&gt;</c>). A movable factory is one whose body the
    /// code-fix can carry the moved modifiers onto: an expression body, or a single-<c>return</c>
    /// block. Anything else (method group, multi-statement block, non-lambda) is opaque → no fire.
    /// <para>The factory is located <b>by shape</b> (the parameterless lambda argument), not by
    /// position, so named / out-of-order argument calls (<c>Memo(factory: () =&gt; …, key: id)</c>)
    /// are handled uniformly. The keyed overload's <c>TKey key</c> can never be a bare lambda (a
    /// lambda has no natural type for <c>TKey</c> inference), so exactly one of the two arguments is
    /// the parameterless-lambda factory; anything ambiguous bails.</para>
    /// </summary>
    internal static bool TryGetMovableFactory(
        InvocationExpressionSyntax memoInvocation,
        out ArgumentSyntax factoryArgument,
        out ParenthesizedLambdaExpressionSyntax lambda,
        out ExpressionSyntax body)
    {
        factoryArgument = null!;
        lambda = null!;
        body = null!;

        var arguments = memoInvocation.ArgumentList.Arguments;
        if (arguments.Count != 2)
            return false;

        foreach (var argument in arguments)
        {
            if (argument.Expression is not ParenthesizedLambdaExpressionSyntax candidate
                || candidate.ParameterList.Parameters.Count != 0
                || !TryGetMovableBody(candidate, out var candidateBody))
                continue;

            // A second movable parameterless lambda would make the factory ambiguous — bail.
            if (factoryArgument is not null)
                return false;

            factoryArgument = argument;
            lambda = candidate;
            body = candidateBody;
        }

        return factoryArgument is not null;
    }

    private static bool TryGetMovableBody(ParenthesizedLambdaExpressionSyntax lambda, out ExpressionSyntax body)
    {
        if (lambda.ExpressionBody is { } expressionBody)
        {
            body = expressionBody;
            return true;
        }

        if (lambda.Block is { Statements.Count: 1 } block
            && block.Statements[0] is ReturnStatementSyntax { Expression: { } returnExpr })
        {
            body = returnExpr;
            return true;
        }

        body = null!;
        return false;
    }

    internal static bool DerivesFromElement(ITypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (IsNamedType(current, ElementTypeName, ReactorCoreNamespace))
                return true;
        }
        return false;
    }

    private static bool IsNamedType(ITypeSymbol? type, string name, string containingNamespace) =>
        type is { } t
        && t.Name == name
        && t.ContainingNamespace?.ToDisplayString() == containingNamespace;
}
