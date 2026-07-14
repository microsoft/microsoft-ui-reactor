using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// <c>REACTOR_ANIM_003</c> — flags an <c>async</c> lambda (or <c>async delegate</c>)
/// passed to <see cref="M:AnimationScope.WithAnimation"/> /
/// <c>WithAnimationAsync</c>, where mutations that run <b>after</b> an <c>await</c>
/// silently animate nothing.
/// </summary>
/// <remarks>
/// <para>
/// <c>AnimationScope</c> stores the ambient curve in <c>[ThreadStatic]</c> fields
/// (<c>src/Reactor/Animation/AnimationScope.cs</c>). <c>WithAnimation(Curve?, Action)</c>
/// sets the scope, invokes <c>action()</c> <b>synchronously</b>, then restores the
/// previous scope in a <c>finally</c>. Both scope-taking entry points —
/// <c>WithAnimation</c> (AnimationScope.cs:28) and <c>WithAnimationAsync</c>
/// (AnimationScope.cs:63) — take a plain <c>Action</c>; neither has a
/// <c>Func&lt;Task&gt;</c> overload.
/// </para>
/// <para>
/// Because the parameter is <c>Action</c>, an <c>async</c> lambda binds as
/// <c>async void</c>. Calling it returns the moment control hits the first suspended
/// <c>await</c>, so the <c>finally</c> restores (empties) the scope <b>before</b> the
/// continuation runs. Any property mutation after the <c>await</c> therefore executes
/// with no ambient curve and does not animate:
/// <code>
/// AnimationScope.WithAnimation(Curve.Ease(300), async () =>
/// {
///     setStage("loading");
///     await api.SaveAsync();
///     setStage("done");   // scope already restored — animates nothing
/// });
/// </code>
/// </para>
/// <para>
/// There is <b>no clean mechanical rewrite</b>: switching to <c>WithAnimationAsync</c>
/// would not remove the <c>async void</c> (it also takes an <c>Action</c>, not a
/// <c>Func&lt;Task&gt;</c>), so this rule ships <b>no code fix</b>. The diagnostic
/// message instead advises splitting the animated mutations into a separate
/// <c>WithAnimation</c> call per phase, sequenced around each <c>await</c>. See
/// <c>docs/guide/animation.md</c> ("Awaiting inside <c>WithAnimation</c>").
/// </para>
/// <para>
/// Low false-positive gate: the callee must resolve to the Reactor
/// <c>AnimationScope</c>; the lambda must convert to exactly <c>System.Action</c>
/// (proving the <c>async void</c> binding, and excluding any future
/// <c>Func&lt;Task&gt;</c> overload); and the lambda body must contain an
/// <c>await</c> followed by a real mutation statement — both evaluated at the
/// lambda's own async level, so awaits/mutations inside nested closures never trip
/// (or suppress) the rule.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AnimationScopeAsyncAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_ANIM_003";

    private const string AnimationScopeTypeName = "AnimationScope";
    private const string AnimationNamespace = "Microsoft.UI.Reactor.Animation";
    private const string WithAnimationName = "WithAnimation";
    private const string WithAnimationAsyncName = "WithAnimationAsync";

    private static readonly LocalizableString Title =
        "Async lambda to WithAnimation loses the animation scope after await";

    private static readonly LocalizableString MessageFormat =
        "This async lambda passed to '{0}' binds as 'async void': AnimationScope is [ThreadStatic], " +
        "so mutations after the first 'await' run with an empty scope and don't animate. " +
        "Passing an async lambda to WithAnimationAsync won't help either (it also takes an Action). " +
        "Split the mutations into a separate WithAnimation call per phase, sequenced around each await.";

    private static readonly LocalizableString Description =
        "AnimationScope stores the ambient curve in [ThreadStatic] fields and WithAnimation/" +
        "WithAnimationAsync take a plain Action, so an async lambda runs as async void: it returns " +
        "at the first suspended await and the scope is restored before the continuation resumes. " +
        "Property changes after the await execute with no ambient curve and are not animated. There " +
        "is no one-click fix (the async variant also takes an Action); split the mutations into a " +
        "WithAnimation call per phase around each await.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Animation",
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

        // Cheap syntactic gate: a call named WithAnimation / WithAnimationAsync with at least one
        // async anonymous-function argument, before touching the semantic model.
        var name = GetInvokedSimpleName(invocation.Expression);
        if (name != WithAnimationName && name != WithAnimationAsyncName)
            return;

        var asyncArgs = invocation.ArgumentList.Arguments
            .Select(a => a.Expression)
            .OfType<AnonymousFunctionExpressionSyntax>()
            .Where(f => f.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword))
            .ToList();
        if (asyncArgs.Count == 0)
            return;

        // Anchor on the real Reactor AnimationScope so a look-alike WithAnimation(…, Action) on some
        // other type (not [ThreadStatic]-scoped) never fires. Require resolution — low FP over recall.
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is not IMethodSymbol method)
            return;
        if (method.Name != WithAnimationName && method.Name != WithAnimationAsyncName)
            return;
        if (method.ContainingType?.Name != AnimationScopeTypeName)
            return;
        if (method.ContainingType.ContainingNamespace?.ToDisplayString() != AnimationNamespace)
            return;

        foreach (var lambda in asyncArgs)
        {
            // The lambda must bind to exactly System.Action — that is what makes the async lambda
            // an async void. A Func<Task> parameter (generic) would await correctly and is excluded.
            var converted = context.SemanticModel.GetTypeInfo(lambda, context.CancellationToken).ConvertedType;
            if (!IsSystemAction(converted))
                continue;

            if (!HasPostAwaitMutation(lambda.Body))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                lambda.AsyncKeyword.GetLocation(),
                method.Name));
        }
    }

    /// <summary>
    /// True when a call statement runs <b>after</b> an <c>await</c> in the lambda body, walking the
    /// body block's top-level statements in execution order and tracking whether an <c>await</c> has
    /// already happened (an <c>await</c> nested in control flow — <c>if</c>/loop/<c>using</c> — still
    /// advances that state for the statements that follow it).
    /// </summary>
    /// <remarks>
    /// Reasoning at the top level is deliberate and keeps the rule low-false-positive:
    /// <list type="bullet">
    /// <item>A mutation inside a branch that is <b>mutually exclusive</b> with the await's branch
    /// (the <c>else</c> of <c>if (c) { await X; } else { Mutate(); }</c>, a sibling <c>switch</c>
    /// section, a <c>catch</c>) is never a top-level statement after the await, so it never trips
    /// the rule.</item>
    /// <item>The lost "mutation" is a state-setter <b>call</b> (the documented <c>setStage(...)</c>
    /// shape) — an <see cref="InvocationExpressionSyntax"/> statement. A bare assignment
    /// (<c>i += 1</c>) is not an animated mutation and is intentionally ignored.</item>
    /// <item>A statement that itself awaits (<c>await X;</c>, <c>x = await Y;</c>,
    /// <c>Set(await Y());</c>) is treated as the await, not as lost work (accepted conservative
    /// false negative for an await inlined into a mutation's arguments).</item>
    /// </list>
    /// Awaits inside nested closures (lambdas / anonymous methods / local functions) are ignored via
    /// <see cref="ContainsOwnLevelAwait"/> — they run in their own async context.
    /// </remarks>
    private static bool HasPostAwaitMutation(CSharpSyntaxNode body)
    {
        // Only a block body can sequence a mutation after an await; an expression-bodied async
        // lambda (`async () => await X`) has no trailing statement to lose.
        if (body is not BlockSyntax block)
            return false;

        var sawAwait = false;
        foreach (var statement in block.Statements)
        {
            var hasAwait = ContainsOwnLevelAwait(statement);

            if (sawAwait && !hasAwait
                && statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax })
            {
                return true;
            }

            if (hasAwait)
                sawAwait = true;
        }

        return false;
    }

    private static bool ContainsOwnLevelAwait(SyntaxNode node) =>
        node.DescendantNodesAndSelf(descendIntoChildren: n => !IsClosureBoundary(n))
            .OfType<AwaitExpressionSyntax>()
            .Any();

    private static bool IsClosureBoundary(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;

    private static bool IsSystemAction(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "Action", IsGenericType: false } named
        && named.ContainingNamespace?.ToDisplayString() == "System";

    private static string? GetInvokedSimpleName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        IdentifierNameSyntax id => id.Identifier.ValueText,
        _ => null,
    };
}
