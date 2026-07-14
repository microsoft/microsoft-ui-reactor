using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// <c>REACTOR_DSL_003</c> — a typed Reactor collection factory
/// (<c>ListView&lt;T&gt;</c>, <c>GridView&lt;T&gt;</c>, <c>FlipView&lt;T&gt;</c>,
/// <c>TreeView&lt;T&gt;</c>, <c>LazyVStack&lt;T&gt;</c>, <c>LazyHStack&lt;T&gt;</c>,
/// <c>ItemsRepeater&lt;T&gt;</c>, <c>ItemsView&lt;T&gt;</c>) is called with a
/// <c>keySelector</c> that never keys by the item — it returns a constant / literal /
/// <c>null</c>, or otherwise never reads its parameter. Every item then maps to the
/// same (or a null) key.
/// </summary>
/// <remarks>
/// Grounding: <c>keySelector</c> is the second positional parameter
/// (<c>Func&lt;T, string&gt;</c>) of the typed collection factories in
/// <c>src/Reactor/Elements/Dsl.cs</c>. Duplicate and null keys force a
/// correctness-preserving bailout in <c>KeyedListDiff</c> (the <c>DuplicateKey</c> /
/// <c>NullKey</c> <c>ReportBailout</c> paths), which re-realizes the whole list on
/// every change — losing focus, selection, and animation state. See
/// <c>docs/guide/collections.md</c>: keys must be stable, unique, and non-null.
/// Distinct from <c>REACTOR_DSL_001</c>, which is about <c>.WithKey</c> on
/// <c>Select</c>-projected layout children.
///
/// Low false-positive posture — fires only when the <c>keySelector</c> is a
/// single-parameter lambda whose body <em>provably</em> does not depend on the item:
/// the parameter is never referenced <b>and</b> the body contains no invocation,
/// object-creation, <c>await</c>, or in-place mutation (increment / decrement /
/// assignment) — any of which could vary per item. It then
/// semantically confirms the argument binds to a <c>keySelector</c> parameter of type
/// <c>Func&lt;T, string&gt;</c> on <c>Microsoft.UI.Reactor.Factories</c>, so the
/// untyped selection overloads' <c>onSelectedIndexChanged</c> callback, the
/// <c>IReactorKeyed</c> <c>viewBuilder</c> overload, method-group selectors, and
/// selectors that read the item never trip it.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConstantKeySelectorAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_DSL_003";

    /// <summary>Fully-qualified name of the Reactor DSL factory class.</summary>
    private const string FactoriesTypeName = "Microsoft.UI.Reactor.Factories";

    /// <summary>The parameter name shared by every typed collection factory's key selector.</summary>
    private const string KeySelectorParameterName = "keySelector";

    /// <summary>
    /// Simple names of the typed, data-driven collection factories that take a
    /// <c>Func&lt;T, string&gt; keySelector</c> as their second parameter (in
    /// <c>src/Reactor/Elements/Dsl.cs</c>). Used only as a cheap first-pass filter so
    /// the analyzer skips the lambda/body scan and semantic lookup for the vast
    /// majority of invocations; the actual match is still confirmed semantically
    /// against <see cref="FactoriesTypeName"/>. Keep in sync with Dsl.cs when a new
    /// typed collection factory is added.
    /// </summary>
    private static readonly ImmutableHashSet<string> TypedCollectionFactoryNames =
        ImmutableHashSet.Create(
            System.StringComparer.Ordinal,
            "ListView", "GridView", "FlipView", "TreeView",
            "LazyVStack", "LazyHStack", "ItemsRepeater", "ItemsView");

    private static readonly LocalizableString Title =
        "Key selector never keys by item";

    private static readonly LocalizableString MessageFormat =
        "The keySelector passed to '{0}' ignores its item parameter, so every item gets the same key. Duplicate keys force the keyed-list diff to bail out and re-realize the whole list on each change. Return a stable, unique per-item key (e.g. item.Id).";

    private static readonly LocalizableString Description =
        "Typed Reactor collections (ListView<T>, GridView<T>, LazyVStack<T>, ...) reconcile by " +
        "the string key returned from keySelector. A selector that returns a constant, returns " +
        "null, or never reads its item parameter yields duplicate or null keys, which the " +
        "keyed-list diff cannot reconcile — it discards the diff and rebuilds every container, " +
        "losing focus, selection, and animation state. Key by a stable, unique property of each item.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Dsl",
        DiagnosticSeverity.Warning,
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
        var invocation = (InvocationExpressionSyntax)context.Node;

        // ── Cheap name gate ───────────────────────────────────────────────
        // A single hash-set lookup skips the lambda/body scan and the semantic
        // symbol lookup for every invocation that isn't a typed collection
        // factory by name (e.g. Enumerable.Select(source, _ => "row")). The
        // containing-type/parameter shape is still confirmed semantically below.
        if (!TypedCollectionFactoryNames.Contains(GetInvokedMethodName(invocation)))
            return;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 2)
            return;

        // ── Cheap syntactic pre-gate ──────────────────────────────────────
        // Locate the keySelector argument (named `keySelector:` or the 2nd
        // positional argument) and require it to be a single-parameter lambda
        // whose body provably ignores the item. This runs before any semantic
        // query so the analyzer stays cheap on the hot path.
        if (!TryGetKeySelectorArgument(args, out var argument, out var positionalIndex))
            return;

        if (!TryGetSingleParameterLambda(argument.Expression, out var lambda, out var parameterName))
            return;

        if (!BodyProvablyIgnoresItem(lambda!, parameterName!))
            return;

        // ── Semantic confirmation ─────────────────────────────────────────
        // Only fire when the argument actually binds to a `keySelector`
        // parameter of type Func<T, string> on Microsoft.UI.Reactor.Factories.
        // This rejects the untyped selection overloads' onSelectedIndexChanged,
        // the IReactorKeyed viewBuilder overload, and any same-named method on an
        // unrelated type.
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return;
        if (method.IsExtensionMethod)
            return;
        if (method.ContainingType?.ToDisplayString() != FactoriesTypeName)
            return;

        var parameter = ResolveBoundParameter(method, argument, positionalIndex);
        if (parameter is null || parameter.Name != KeySelectorParameterName)
            return;
        if (!IsFuncReturningString(parameter.Type))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, lambda!.GetLocation(), GetInvokedMethodName(invocation)));
    }

    /// <summary>
    /// Finds the argument that supplies the <c>keySelector</c> — either explicitly
    /// named <c>keySelector:</c>, or the second positional argument when the first
    /// two arguments are positional (so index maps directly to parameter index).
    /// </summary>
    private static bool TryGetKeySelectorArgument(
        SeparatedSyntaxList<ArgumentSyntax> args,
        out ArgumentSyntax argument,
        out int positionalIndex)
    {
        foreach (var candidate in args)
        {
            if (candidate.NameColon?.Name.Identifier.ValueText == KeySelectorParameterName)
            {
                argument = candidate;
                positionalIndex = -1; // bound by name
                return true;
            }
        }

        if (args[0].NameColon is null && args[1].NameColon is null)
        {
            argument = args[1];
            positionalIndex = 1;
            return true;
        }

        argument = null!;
        positionalIndex = -1;
        return false;
    }

    /// <summary>
    /// Matches a single-parameter lambda (<c>x =&gt; ...</c> or <c>(x) =&gt; ...</c>) and
    /// extracts its parameter name. Multi-parameter lambdas (e.g. the
    /// <c>(item, index)</c> view builder) and non-lambda arguments (method groups)
    /// are rejected.
    /// </summary>
    private static bool TryGetSingleParameterLambda(
        ExpressionSyntax expression, out LambdaExpressionSyntax? lambda, out string? parameterName)
    {
        switch (expression)
        {
            case SimpleLambdaExpressionSyntax simple:
                lambda = simple;
                parameterName = simple.Parameter.Identifier.ValueText;
                return true;
            case ParenthesizedLambdaExpressionSyntax paren when paren.ParameterList.Parameters.Count == 1:
                lambda = paren;
                parameterName = paren.ParameterList.Parameters[0].Identifier.ValueText;
                return true;
            default:
                lambda = null;
                parameterName = null;
                return false;
        }
    }

    /// <summary>
    /// True when the lambda body evaluates to the same value for every item: the
    /// parameter is never referenced, and the body contains no invocation,
    /// object-creation, await, or in-place mutation (increment/decrement/assignment)
    /// — each a source of per-call variation. Both a constant key (duplicates) and a
    /// null key trip this; a body that reads the item, calls a helper (which could
    /// return unique values), or mutates state (e.g. <c>_ =&gt; $"{n++}"</c>) does not.
    /// </summary>
    private static bool BodyProvablyIgnoresItem(LambdaExpressionSyntax lambda, string parameterName)
    {
        var body = (SyntaxNode?)lambda.Body;
        if (body is null)
            return false;

        foreach (var node in body.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case InvocationExpressionSyntax:
                case ObjectCreationExpressionSyntax:
                case ImplicitObjectCreationExpressionSyntax:
                case AnonymousObjectCreationExpressionSyntax:
                case AwaitExpressionSyntax:
                case AssignmentExpressionSyntax:
                    return false;
                case PrefixUnaryExpressionSyntax pre
                    when pre.IsKind(SyntaxKind.PreIncrementExpression) || pre.IsKind(SyntaxKind.PreDecrementExpression):
                    return false;
                case PostfixUnaryExpressionSyntax post
                    when post.IsKind(SyntaxKind.PostIncrementExpression) || post.IsKind(SyntaxKind.PostDecrementExpression):
                    return false;
                case IdentifierNameSyntax id
                    when id.Identifier.ValueText == parameterName && !IsMemberName(id):
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when the identifier is the member half of a member access
    /// (<c>foo.Name</c> / <c>foo?.Name</c>), which is a member lookup, not a
    /// reference to a same-named lambda parameter.
    /// </summary>
    private static bool IsMemberName(IdentifierNameSyntax id) =>
        (id.Parent is MemberAccessExpressionSyntax ma && ma.Name == id) ||
        (id.Parent is MemberBindingExpressionSyntax mb && mb.Name == id);

    /// <summary>Maps the keySelector argument back to its bound parameter symbol.</summary>
    private static IParameterSymbol? ResolveBoundParameter(
        IMethodSymbol method, ArgumentSyntax argument, int positionalIndex)
    {
        if (argument.NameColon is { } nameColon)
        {
            var name = nameColon.Name.Identifier.ValueText;
            return method.Parameters.FirstOrDefault(p => p.Name == name);
        }

        return positionalIndex >= 0 && positionalIndex < method.Parameters.Length
            ? method.Parameters[positionalIndex]
            : null;
    }

    /// <summary>True for <c>System.Func&lt;T, string&gt;</c> (the typed key selector shape).</summary>
    private static bool IsFuncReturningString(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "Func", TypeArguments.Length: 2 } func
        && func.ContainingNamespace?.ToDisplayString() == "System"
        && func.TypeArguments[1].SpecialType == SpecialType.System_String;

    private static string GetInvokedMethodName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
            SimpleNameSyntax sn => sn.Identifier.ValueText, // IdentifierName or GenericName
            _ => "the collection",
        };
}
