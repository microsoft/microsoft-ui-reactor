using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// <c>REACTOR_LIFECYCLE_002</c> — flags a <c>UseEffect(Action, …)</c> whose body allocates a
/// long-lived producer (a timer, a disposable subscription, or a CLR event subscription) but
/// returns <b>no cleanup</b>.
/// </summary>
/// <remarks>
/// <para>
/// <c>UseEffect</c> has two families of overloads: the <c>Action</c> overloads
/// (<c>RenderContext.cs:363</c> and the arity-1..3 forms) run a fire-and-forget side effect and
/// have <b>no way to return a teardown</b>, while the <c>Func&lt;Action&gt;</c> overloads
/// (<c>RenderContext.cs:379</c>) return a cleanup that the reconciler runs before the next effect
/// and on unmount. When an author creates a <c>PeriodicTimer</c> / <c>Timer</c>, calls a
/// <c>.Subscribe(...)</c> that returns an <see cref="!:System.IDisposable"/>, or wires a CLR event
/// inside the <c>Action</c> overload, the producer outlives the component: after unmount it keeps
/// running and its callback can still fire against a torn-down component — at best leaking the
/// captured closure tree, and (if the callback touches component state, e.g. a state setter)
/// running against a dead <see cref="!:RenderContext"/>. This is the "Missing cleanup" pitfall
/// documented in <c>docs/guide/effects.md</c> §"Missing cleanup" (lines 340-376).
/// </para>
/// <para>
/// Detection is deliberately conservative (nudge, not a mechanical fix — the correct teardown
/// differs per resource and the created handle is often captured into a nested task):
/// the invocation must bind to the Reactor <c>Component</c>/<c>RenderContext</c> <c>UseEffect</c>
/// whose first parameter is the non-generic <see cref="!:System.Action"/> overload; the effect
/// argument must be a lambda whose body is visible; a known-lifetime allocation must appear at the
/// <b>top level</b> of that body (not inside a nested lambda / local function, whose lifetime is
/// its own); and there must be <b>no</b> teardown signal anywhere in the body (<c>using</c>, a
/// <c>Dispose</c>/<c>DisposeAsync</c>/<c>Stop</c>/<c>Cancel</c> call, or any event <c>-=</c> — the
/// unsubscription is not checked against the specific <c>+=</c> handler). Any of those bails the
/// rule. The fix is a template nudge: return a cleanup <c>Action</c> (which selects the
/// <c>Func&lt;Action&gt;</c> overload).
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EffectCleanupAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_LIFECYCLE_002";

    private const string UseEffectName = "UseEffect";
    private const string ComponentType = "Microsoft.UI.Reactor.Core.Component";
    private const string RenderContextType = "Microsoft.UI.Reactor.Core.RenderContext";

    /// <summary>
    /// Simple type names whose <c>new</c> construction inside an effect body denotes a producer
    /// that keeps running until explicitly stopped/disposed. Every entry must be constructible with
    /// <c>new</c> — this set is only consulted for object-creation nodes. <c>System.Threading.Timer</c>,
    /// <c>System.Timers.Timer</c> and the WinUI <c>DispatcherTimer</c> qualify; the factory-created
    /// WinRT timers (<c>DispatcherQueueTimer</c> via <c>DispatcherQueue.CreateTimer()</c>,
    /// <c>ThreadPoolTimer</c> via its static factory) have no public constructor and so are not
    /// listed here — they would need invocation-based detection.
    /// </summary>
    private static readonly HashSet<string> KnownTimerTypes = new(System.StringComparer.Ordinal)
    {
        "PeriodicTimer",
        "Timer",
        "DispatcherTimer",
    };

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "UseEffect allocates a long-lived resource with no cleanup",
        "This UseEffect creates {0} but returns no cleanup; it outlives the component and its callback can still run after unmount. Return a cleanup Action (use the Func<Action> overload) that tears it down.",
        "Reactor.Lifecycle",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "The Action overload of UseEffect cannot return a teardown, so a timer, a disposable " +
            "subscription (a `.Subscribe(...)` that returns IDisposable), or a CLR event wired " +
            "inside it outlives the component. After unmount the " +
            "producer keeps running and its callback can still fire against a torn-down component — " +
            "at best it leaks the captured closure tree, and if the callback touches component state " +
            "(e.g. a state setter) it runs against a dead RenderContext. Switch to the Func<Action> " +
            "overload and return a cleanup that tears the resource down (stop/dispose the timer or " +
            "subscription, or unsubscribe the event) — e.g. " +
            "UseEffect(() => { var t = new PeriodicTimer(...); ...; return () => t.Dispose(); }, ...). " +
            "See docs/guide/effects.md \"Missing cleanup\".");

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

        // Syntactic fast path: bail before any semantic query unless the call names UseEffect.
        if (GetInvokedMethodName(invocation) != UseEffectName)
            return;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count == 0)
            return;

        // The effect must be a lambda/anonymous method whose body we can inspect. A method group
        // (UseEffect(SetUp, ...)) hides the body — unprovable, so bail.
        if (args[0].Expression is not AnonymousFunctionExpressionSyntax effect)
            return;
        var body = (SyntaxNode?)effect.Body;
        if (body is null)
            return;

        // Anchor to the Reactor UseEffect AND select the no-cleanup (Action) overload.
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return;
        if (!IsReactorUseEffect(method) || !IsActionOverload(method))
            return;

        // Conservative bail: any in-body teardown signal (anywhere, including nested continuations)
        // means the author is managing the lifetime — favor a false negative over a false positive.
        if (HasCleanupSignal(body, context.SemanticModel, context.CancellationToken))
            return;

        // Find the offending producer at the top level of the effect body (do not descend into
        // nested lambdas / local functions — their lifetime is their own).
        var (offender, resourceKind) = FindLifetimeAllocation(body, context.SemanticModel, context.CancellationToken);
        if (offender is null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, offender.GetLocation(), resourceKind));
    }

    private static string? GetInvokedMethodName(InvocationExpressionSyntax invocation)
        => invocation.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            // Conditional access — `ctx?.UseEffect(...)` / `timer?.Dispose()` — binds the member
            // via a MemberBindingExpressionSyntax. Mirrors CommandDebounceAnalyzer.
            MemberBindingExpressionSyntax mb => mb.Name.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            GenericNameSyntax gn => gn.Identifier.Text,
            _ => null,
        };

    /// <summary>
    /// True when the resolved <c>UseEffect</c> is declared <b>exactly</b> on the Reactor
    /// <c>Component</c> / <c>Component&lt;T&gt;</c> (the protected wrappers) or <c>RenderContext</c>
    /// (the instance methods) — the types that own the Action-vs-<c>Func&lt;Action&gt;</c> cleanup
    /// contract. A <c>UseEffect</c> a user declares on their own <c>Component</c> subclass shadows
    /// (not overrides — the framework methods are non-virtual) the hook and has unknown semantics,
    /// so an exact-type check (rather than a derives-from walk) avoids a false positive there while
    /// still accepting the idiomatic unqualified call, which binds to <c>Component.UseEffect</c>.
    /// </summary>
    private static bool IsReactorUseEffect(IMethodSymbol method)
        => IsReactorHostType(method.ContainingType, ComponentType)
        || IsReactorHostType(method.ContainingType, RenderContextType);

    /// <summary>
    /// True when the first parameter is the non-generic <see cref="!:System.Action"/> — i.e. the
    /// overload that cannot return a cleanup. The <c>Func&lt;Action&gt;</c> overloads carry a
    /// teardown contract and are intentionally excluded.
    /// </summary>
    private static bool IsActionOverload(IMethodSymbol method)
    {
        if (method.Parameters.Length == 0)
            return false;
        return method.Parameters[0].Type is INamedTypeSymbol { Name: "Action", Arity: 0 } t
            && t.ContainingNamespace?.ToDisplayString() == "System";
    }

    /// <summary>
    /// True when <paramref name="type"/> is exactly <paramref name="fullyQualifiedName"/> or its
    /// generic form (<c>Component&lt;T&gt;</c>) — i.e. the type itself, not a derived type.
    /// </summary>
    private static bool IsReactorHostType(INamedTypeSymbol? type, string fullyQualifiedName)
    {
        if (type is null)
            return false;
        var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "");
        return name == fullyQualifiedName
            || name.StartsWith(fullyQualifiedName + "<", System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Scans the whole effect body (including nested lambdas / continuations) for any teardown
    /// signal: a <c>using</c> statement/declaration, a teardown call
    /// (<c>Dispose</c>/<c>DisposeAsync</c>/<c>Stop</c>/<c>Cancel</c>, including the conditional-access
    /// <c>timer?.Stop()</c> form), or an event unsubscription (<c>-=</c> whose left side binds to an
    /// event). Presence means the author is managing the lifetime — favor a false negative over a
    /// false positive. A numeric <c>-=</c> (e.g. <c>count -= 1</c>) is not counted.
    /// </summary>
    private static bool HasCleanupSignal(SyntaxNode body, SemanticModel model, System.Threading.CancellationToken ct)
    {
        foreach (var node in body.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case UsingStatementSyntax:
                case LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not 0 }:
                    return true;
                case AssignmentExpressionSyntax a
                    when a.IsKind(SyntaxKind.SubtractAssignmentExpression) && IsEvent(a.Left, model, ct):
                    return true;
                case InvocationExpressionSyntax inv
                    when GetInvokedMethodName(inv) is "Dispose" or "DisposeAsync" or "Stop" or "Cancel":
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns the first known-lifetime allocation at the top level of the effect body (skipping
    /// subtrees rooted at a nested anonymous function or local function), plus a human-readable
    /// description of what it is. Returns <c>(null, "")</c> when nothing qualifies.
    /// </summary>
    private static (SyntaxNode? node, string kind) FindLifetimeAllocation(
        SyntaxNode body, SemanticModel model, System.Threading.CancellationToken ct)
    {
        // Walk the body in document order but do not descend into nested lambdas / local functions —
        // allocations there have their own lifetime and are not the effect's setup work. The body
        // node itself is included so an expression-bodied effect (`() => source.Subscribe(...)`) is
        // inspected.
        foreach (var node in body.DescendantNodesAndSelf(
            descendIntoChildren: n => n is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax))
        {
            switch (node)
            {
                case BaseObjectCreationExpressionSyntax creation when TryGetKnownTimer(creation, model, ct) is { } timerName:
                    return (creation, $"a {timerName}");

                case InvocationExpressionSyntax inv
                    when GetInvokedMethodName(inv) == "Subscribe"
                    && ReturnsDisposable(inv, model, ct):
                    return (inv, "a disposable subscription");

                case AssignmentExpressionSyntax add
                    when add.IsKind(SyntaxKind.AddAssignmentExpression)
                    && IsEvent(add.Left, model, ct):
                    return (add, "an event subscription");
            }
        }
        return (null, string.Empty);
    }

    /// <summary>
    /// If <paramref name="creation"/> constructs a known timer type, returns its simple name;
    /// otherwise <c>null</c>. Resolves the type semantically so target-typed <c>new(...)</c> is
    /// covered and a user type that merely shares a timer's simple name is not matched. Falls back
    /// to the written simple name only for an explicit <c>new T(...)</c> whose symbol is unresolved
    /// (incomplete compile).
    /// </summary>
    private static string? TryGetKnownTimer(BaseObjectCreationExpressionSyntax creation, SemanticModel model, System.Threading.CancellationToken ct)
    {
        if (model.GetTypeInfo(creation, ct).Type is { } type)
            return IsKnownTimerType(type) ? type.Name : null;

        if (creation is ObjectCreationExpressionSyntax oc && KnownTimerTypes.Contains(SimpleTypeName(oc.Type)))
            return SimpleTypeName(oc.Type);

        return null;
    }

    /// <summary>
    /// True when <paramref name="type"/> is one of the known lifetime-bearing timer types. The
    /// distinctive names (<c>PeriodicTimer</c> and <c>DispatcherTimer</c>, the latter exposing
    /// Start/Stop rather than <c>IDisposable</c>) are matched by name — a user type coincidentally
    /// sharing one is implausible. The bare <c>Timer</c> name is common enough to collide, so it is
    /// only matched when the type is actually disposable (both <c>System.Threading.Timer</c> and
    /// <c>System.Timers.Timer</c> are), which filters out an unrelated user <c>Timer</c>.
    /// </summary>
    private static bool IsKnownTimerType(ITypeSymbol type)
    {
        if (!KnownTimerTypes.Contains(type.Name))
            return false;
        return type.Name != "Timer" || ImplementsIDisposable(type);
    }

    private static bool ImplementsIDisposable(ITypeSymbol type)
        => type.AllInterfaces.Any(i =>
            i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.IDisposable");

    private static string SimpleTypeName(TypeSyntax type)
        => type switch
        {
            QualifiedNameSyntax q => q.Right.Identifier.Text,
            GenericNameSyntax g => g.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => type.ToString(),
        };

    private static bool ReturnsDisposable(InvocationExpressionSyntax invocation, SemanticModel model, System.Threading.CancellationToken ct)
    {
        if (model.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol m)
            return false;
        var ret = m.ReturnType;
        if (ret is null || ret.SpecialType == SpecialType.System_Void)
            return false;
        if (ret.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.IDisposable")
            return true;
        return ret.AllInterfaces.Any(i =>
            i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.IDisposable");
    }

    private static bool IsEvent(ExpressionSyntax left, SemanticModel model, System.Threading.CancellationToken ct)
        => model.GetSymbolInfo(left, ct).Symbol is IEventSymbol;
}
