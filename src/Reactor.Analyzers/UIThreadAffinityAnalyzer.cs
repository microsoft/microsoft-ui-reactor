using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_THREAD_001: Detects an invocation of a UI-thread-only Reactor member
/// (marked <c>[UIThreadOnly]</c>) that runs lexically inside a background-launch
/// lambda (<c>Task.Run</c> / <c>Task.Factory.StartNew</c> /
/// <c>ThreadPool.QueueUserWorkItem</c>) without being marshaled back through a
/// <c>DispatcherQueue.TryEnqueue</c>. Such calls hit
/// <c>ThreadAffinity.ThrowIfNotOnUIThread</c> and throw at runtime.
/// </summary>
/// <remarks>
/// The framework is a metadata-only reference in a consumer compilation, so the
/// analyzer cannot inspect a callee's body for the runtime guard. The committed
/// mechanism is the <c>[UIThreadOnly]</c> marker attribute
/// (applied to the members that call <c>ThreadAffinity.ThrowIfNotOnUIThread</c>),
/// which is metadata-visible. The syntactic background-lambda gate runs first;
/// the attribute check is the semantic backstop that keeps false positives low.
/// (spec 060 §4.6)
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UIThreadAffinityAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_THREAD_001";

    internal const string UIThreadOnlyAttributeMetadataName =
        "Microsoft.UI.Reactor.Hosting.UIThreadOnlyAttribute";

    private static readonly LocalizableString Title =
        "UI-thread-only member called on a background thread";

    private static readonly LocalizableString MessageFormat =
        "'{0}' must run on the UI thread; calling it inside a background task throws once the UI " +
        "dispatcher has been captured. Marshal it back with a null-safe " +
        "ReactorApp.UIDispatcher.TryEnqueue(...).";

    private static readonly LocalizableString Description =
        "Members annotated with [UIThreadOnly] call ThreadAffinity.ThrowIfNotOnUIThread, which throws " +
        "InvalidOperationException when reached from a Task.Run / Task.Factory.StartNew / " +
        "ThreadPool.QueueUserWorkItem lambda once the UI dispatcher has been captured (the guard is a " +
        "no-op while ReactorApp.UIDispatcher is still null, before the first window bootstraps). Marshal " +
        "the call back onto the UI thread through ReactorApp.UIDispatcher.TryEnqueue(...), null-checked " +
        "so it falls back to the direct call before the dispatcher exists (never a null-forgiving '!').";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Threading",
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

        // Every assignment kind whose target property setter runs — a compound
        // assignment (`p += x`, `p ??= x`, …) invokes the setter just like `p = x`.
        context.RegisterSyntaxNodeAction(
            AnalyzeAssignment,
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxKind.AddAssignmentExpression,
            SyntaxKind.SubtractAssignmentExpression,
            SyntaxKind.MultiplyAssignmentExpression,
            SyntaxKind.DivideAssignmentExpression,
            SyntaxKind.ModuloAssignmentExpression,
            SyntaxKind.AndAssignmentExpression,
            SyntaxKind.OrAssignmentExpression,
            SyntaxKind.ExclusiveOrAssignmentExpression,
            SyntaxKind.LeftShiftAssignmentExpression,
            SyntaxKind.RightShiftAssignmentExpression,
            SyntaxKind.UnsignedRightShiftAssignmentExpression,
            SyntaxKind.CoalesceAssignmentExpression);

        // Increment / decrement also invoke the property setter (`progress.Value++`).
        context.RegisterSyntaxNodeAction(
            AnalyzeIncrementOrDecrement,
            SyntaxKind.PreIncrementExpression,
            SyntaxKind.PostIncrementExpression,
            SyntaxKind.PreDecrementExpression,
            SyntaxKind.PostDecrementExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!IsBackgroundThreadContext(invocation, context.SemanticModel, context.CancellationToken))
            return;

        // Semantic backstop: confirm the callee carries [UIThreadOnly].
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        var method = symbolInfo.Symbol as IMethodSymbol
            ?? symbolInfo.CandidateSymbols.FirstOrDefault() as IMethodSymbol;
        if (method is null || !HasUIThreadOnlyAttribute(method))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation(),
            method.Name));
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;

        // Only a property write hits a UI-thread-guarded setter. The flagged node is
        // the whole assignment so the code fix can wrap `x.P = v` in the dispatcher
        // marshal the same way it wraps a call.
        ReportIfUIThreadOnlyProperty(context, assignment, assignment.Left);
    }

    private static void AnalyzeIncrementOrDecrement(SyntaxNodeAnalysisContext context)
    {
        var operand = context.Node switch
        {
            PrefixUnaryExpressionSyntax prefix => prefix.Operand,
            PostfixUnaryExpressionSyntax postfix => postfix.Operand,
            _ => null,
        };
        if (operand is null)
            return;

        // `progress.Value++` / `--progress.Value` invoke the setter too.
        ReportIfUIThreadOnlyProperty(context, context.Node, operand);
    }

    private static void ReportIfUIThreadOnlyProperty(
        SyntaxNodeAnalysisContext context,
        SyntaxNode reportNode,
        ExpressionSyntax target)
    {
        if (target is not (MemberAccessExpressionSyntax or IdentifierNameSyntax or MemberBindingExpressionSyntax))
            return;
        if (!IsBackgroundThreadContext(context.Node, context.SemanticModel, context.CancellationToken))
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(target, context.CancellationToken);
        var property = symbolInfo.Symbol as IPropertySymbol
            ?? symbolInfo.CandidateSymbols.FirstOrDefault() as IPropertySymbol;
        if (property is null)
            return;

        // The attribute may sit on the property itself or on its set accessor
        // (`{ get; [UIThreadOnly] set; }`), which is a distinct method symbol.
        if (!HasUIThreadOnlyAttribute(property) &&
            !(property.SetMethod is { } setMethod && HasUIThreadOnlyAttribute(setMethod)))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            reportNode.GetLocation(),
            property.Name));
    }

    /// <summary>
    /// Shared syntactic gate (spec §3): the node is lexically inside a
    /// background-launch lambda, not already marshaled via <c>TryEnqueue</c>, and
    /// not the analyzer/code-fix's own null-dispatcher fallback (which is safe
    /// because <c>ThrowIfNotOnUIThread</c> is a no-op while the dispatcher is null,
    /// and skipping it stops the code fix looping on its own output).
    /// </summary>
    private static bool IsBackgroundThreadContext(SyntaxNode node, SemanticModel semanticModel, CancellationToken cancellationToken) =>
        IsInsideUnmarshaledBackgroundLambda(node, semanticModel, cancellationToken)
        && !IsInsideDispatcherNullFallback(node, semanticModel, cancellationToken);

    /// <summary>
    /// Walk the lexical ancestors of <paramref name="invocation"/>. Returns
    /// <see langword="true"/> when the nearest enclosing thread-affecting lambda
    /// is a background launcher (<c>Task.Run</c> / <c>Task.Factory.StartNew</c> /
    /// <c>ThreadPool.QueueUserWorkItem</c>); returns <see langword="false"/> the
    /// moment a <c>TryEnqueue</c> lambda is seen first (already marshaled) or no
    /// background lambda encloses the call. Walking inner→outer makes nesting
    /// resolve correctly: <c>Task.Run(() =&gt; d.TryEnqueue(() =&gt; w.Close()))</c>
    /// hits the TryEnqueue boundary before the Task.Run boundary.
    /// </summary>
    private static bool IsInsideUnmarshaledBackgroundLambda(SyntaxNode invocation, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        for (var node = invocation.Parent; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case AnonymousFunctionExpressionSyntax lambda:
                    switch (ClassifyLambdaHost(lambda, semanticModel, cancellationToken))
                    {
                        case LambdaHost.Marshaled:
                            return false;
                        case LambdaHost.Background:
                            return true;
                        // LambdaHost.Unrelated → transparent; keep walking outward.
                    }
                    break;

                // Don't leak out of the enclosing member into sibling code.
                case MemberDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    private enum LambdaHost { Unrelated, Background, Marshaled }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="node"/> sits in the
    /// then-branch of an <c>if (d is null)</c> / <c>if (d == null)</c> whose
    /// <c>else</c> marshals through <c>d.TryEnqueue(...)</c> — the null-dispatcher
    /// fallback idiom the code fix emits (and app authors write by hand). The
    /// suppression is tied to the null-checked identifier (the same local must be
    /// the <c>TryEnqueue</c> receiver) <b>and</b> to that local being sourced from
    /// <see cref="Microsoft.UI.Reactor.ReactorApp.UIDispatcher"/> — the safety
    /// argument (<c>ThrowIfNotOnUIThread</c> is a no-op while the framework
    /// dispatcher is null) only holds for that dispatcher, so an unrelated nullable
    /// <c>DispatcherQueue</c> named <c>d</c> does not hide a genuine off-thread call.
    /// </summary>
    private static bool IsInsideDispatcherNullFallback(SyntaxNode node, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var child = node;
        for (var current = node.Parent; current is not null; child = current, current = current.Parent)
        {
            if (current is IfStatementSyntax ifStatement &&
                ReferenceEquals(child, ifStatement.Statement) &&
                TryGetNullCheckedIdentifier(ifStatement.Condition) is { } dispatcher &&
                IsReactorDispatcherLocal(dispatcher, semanticModel, cancellationToken) &&
                ifStatement.Else is { } elseClause &&
                ElseMarshalsThrough(elseClause, dispatcher.Identifier.Text, semanticModel, cancellationToken))
            {
                return true;
            }

            if (current is MemberDeclarationSyntax)
                break;
        }

        return false;
    }

    /// <summary>
    /// For <c>x is null</c> / <c>x == null</c> / <c>null == x</c> where <c>x</c> is a
    /// simple identifier, returns that identifier; otherwise <see langword="null"/>.
    /// </summary>
    private static IdentifierNameSyntax? TryGetNullCheckedIdentifier(ExpressionSyntax condition) => condition switch
    {
        IsPatternExpressionSyntax
        {
            Expression: IdentifierNameSyntax id,
            Pattern: ConstantPatternSyntax { Expression: LiteralExpressionSyntax literal },
        } when literal.IsKind(SyntaxKind.NullLiteralExpression) => id,
        BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) => NullCheckedSide(binary),
        _ => null,
    };

    private static IdentifierNameSyntax? NullCheckedSide(BinaryExpressionSyntax binary)
    {
        if (binary.Right.IsKind(SyntaxKind.NullLiteralExpression) && binary.Left is IdentifierNameSyntax left)
            return left;
        if (binary.Left.IsKind(SyntaxKind.NullLiteralExpression) && binary.Right is IdentifierNameSyntax right)
            return right;
        return null;
    }

    /// <summary>
    /// Confirms <paramref name="identifier"/> binds to a local initialized from
    /// <see cref="Microsoft.UI.Reactor.ReactorApp.UIDispatcher"/>.
    /// </summary>
    private static bool IsReactorDispatcherLocal(IdentifierNameSyntax identifier, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        if (semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is not ILocalSymbol local)
            return false;

        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax { Initializer.Value: { } initializer }
                && IsReactorUIDispatcher(initializer, semanticModel, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReactorUIDispatcher(ExpressionSyntax expression, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
        return symbol is { Name: "UIDispatcher" }
            && FullyQualifiedName(symbol.ContainingType) == "Microsoft.UI.Reactor.ReactorApp";
    }

    /// <summary>
    /// The <c>else</c> branch marshals through <c><paramref name="dispatcherName"/>.TryEnqueue(...)</c>
    /// on an actual <see cref="Microsoft.UI.Dispatching.DispatcherQueue"/>.
    /// </summary>
    private static bool ElseMarshalsThrough(SyntaxNode elseClause, string dispatcherName, SemanticModel semanticModel, CancellationToken cancellationToken) =>
        elseClause.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(invocation =>
        {
            var (methodName, receiverName) = GetInvokedNames(invocation);
            return methodName == "TryEnqueue"
                && receiverName == dispatcherName
                && HostTypeIs(invocation, semanticModel, cancellationToken, DispatcherQueueTypeName);
        });

    /// <summary>
    /// Classify a lambda by the method it is passed to: a background launcher, a
    /// dispatcher marshal (<c>TryEnqueue</c>), or unrelated. A cheap syntactic
    /// name/receiver filter runs first, then the resolved method's containing type
    /// is confirmed semantically so an unrelated same-named API (a non-dispatcher
    /// <c>TryEnqueue</c>, a user-defined <c>Run</c>) does not misclassify.
    /// </summary>
    private static LambdaHost ClassifyLambdaHost(AnonymousFunctionExpressionSyntax lambda, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        if (lambda.Parent is not ArgumentSyntax argument ||
            argument.Parent is not ArgumentListSyntax argumentList ||
            argumentList.Parent is not InvocationExpressionSyntax hostInvocation)
        {
            return LambdaHost.Unrelated;
        }

        // Cheap syntactic filter on the method name, then a semantic confirm of the
        // resolved containing type. The receiver identifier is intentionally not
        // constrained — HostTypeIs is authoritative — so `using static Task; Run(...)`,
        // `taskFactory.StartNew(...)`, `new TaskFactory().StartNew(...)`, etc. are all
        // recognized.
        var methodName = GetInvokedNames(hostInvocation).methodName;

        // DispatcherQueue.TryEnqueue(...) — the call is already marshaled onto the
        // UI thread. Confirm the receiver really is a DispatcherQueue so an unrelated
        // TryEnqueue is not treated as marshaled (which would hide a real bug).
        if (methodName == "TryEnqueue")
            return HostTypeIs(hostInvocation, semanticModel, cancellationToken, DispatcherQueueTypeName)
                ? LambdaHost.Marshaled
                : LambdaHost.Unrelated;

        return methodName switch
        {
            "Run" when HostTypeIs(hostInvocation, semanticModel, cancellationToken, TaskTypeName)
                => LambdaHost.Background,
            "StartNew" when HostTypeIs(hostInvocation, semanticModel, cancellationToken, TaskFactoryTypeName)
                => LambdaHost.Background,
            "QueueUserWorkItem" when HostTypeIs(hostInvocation, semanticModel, cancellationToken, ThreadPoolTypeName)
                => LambdaHost.Background,
            _ => LambdaHost.Unrelated,
        };
    }

    private const string DispatcherQueueTypeName = "Microsoft.UI.Dispatching.DispatcherQueue";
    private const string TaskTypeName = "System.Threading.Tasks.Task";
    private const string TaskFactoryTypeName = "System.Threading.Tasks.TaskFactory";
    private const string ThreadPoolTypeName = "System.Threading.ThreadPool";

    /// <summary>
    /// Confirms the invoked method resolves to a member of
    /// <paramref name="fullyQualifiedTypeName"/>.
    /// </summary>
    private static bool HostTypeIs(InvocationExpressionSyntax invocation, SemanticModel semanticModel, CancellationToken cancellationToken, string fullyQualifiedTypeName)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        var method = symbolInfo.Symbol as IMethodSymbol
            ?? symbolInfo.CandidateSymbols.FirstOrDefault() as IMethodSymbol;
        return FullyQualifiedName(method?.ContainingType) == fullyQualifiedTypeName;
    }

    /// <summary>
    /// Extract the invoked simple method name and the rightmost identifier of its
    /// receiver — e.g. <c>Task.Factory.StartNew</c> → (<c>StartNew</c>,
    /// <c>Factory</c>), <c>Task.Run</c> → (<c>Run</c>, <c>Task</c>).
    /// </summary>
    private static (string? methodName, string? receiverName) GetInvokedNames(InvocationExpressionSyntax invocation)
    {
        switch (invocation.Expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                var receiverName = memberAccess.Expression switch
                {
                    IdentifierNameSyntax id => id.Identifier.Text,
                    MemberAccessExpressionSyntax inner => inner.Name.Identifier.Text,
                    _ => null,
                };
                return (memberAccess.Name.Identifier.Text, receiverName);

            case IdentifierNameSyntax id:
                return (id.Identifier.Text, null);

            case MemberBindingExpressionSyntax binding:
                return (binding.Name.Identifier.Text, null);

            default:
                return (null, null);
        }
    }

    internal static bool HasUIThreadOnlyAttribute(ISymbol member)
    {
        foreach (var attribute in member.GetAttributes())
        {
            if (FullyQualifiedName(attribute.AttributeClass) == UIThreadOnlyAttributeMetadataName)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Fully-qualified name without the <c>global::</c> prefix, matching the
    /// symbol-identity comparison idiom used by the other analyzers in this repo.
    /// </summary>
    private static string? FullyQualifiedName(ISymbol? symbol) =>
        symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "");
}
