using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.UI.Reactor.Compile.Analyzer;

/// <summary>
/// REACTOR1002 — validates that
/// <c>ReactorBinding&lt;TElement&gt;.OnCustomEvent&lt;TArgs&gt;(subscribe,
/// unsubscribe, handler)</c>'s <c>TArgs</c> matches the <c>EventArgs</c>
/// type of every event subscribed via <c>+=</c> / <c>-=</c> inside the
/// <c>subscribe</c> / <c>unsubscribe</c> lambdas.
///
/// <para>
/// <strong>Status (Phase 1):</strong> <em>active</em>. The
/// <c>OnCustomEvent</c> method shape ships in Phase 1 (1.6) and authors
/// will start writing handlers against it immediately.
/// </para>
///
/// <para>
/// <strong>Why this rule isn't redundant with the C# compiler:</strong>
/// WinUI events such as <c>ToggleSwitch.Toggled</c> use the non-generic
/// <c>RoutedEventHandler</c> delegate, not
/// <c>EventHandler&lt;RoutedEventArgs&gt;</c>. Authors must explicitly
/// wrap the handler parameter at the subscription site —
/// <c>(c, h) =&gt; ((ToggleSwitch)c).Toggled += new RoutedEventHandler(h)</c>.
/// The C# compiler is happy with that wrap as long as the
/// <c>RoutedEventHandler</c> constructor accepts <c>h</c>'s shape, but
/// the wrap silently bridges across <em>EventArgs</em> mismatches the
/// author would never catch at runtime (the handler simply never fires,
/// or fires with the wrong EventArgs type cast). This rule reports the
/// mismatch at the <c>+=</c> / <c>-=</c> site by reading the subscribed
/// event's <c>EventArgs</c> and comparing it against the declared
/// <c>TArgs</c>.
/// </para>
///
/// <para>
/// Per the orchestrator brief: the rule fires whenever the event's
/// EventArgs (read from the event's delegate Invoke signature) does not
/// equal the <c>TArgs</c> declared at the <c>OnCustomEvent</c> call.
/// Whether the RHS is wrapped (<c>new XHandler(h)</c>) or bare
/// (<c>h</c>) doesn't matter — the EventArgs of the event itself is
/// what counts.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CustomEventDelegateTypeAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Fully qualified container type name. Matched without binding the
    /// type symbol because <c>ReactorBinding&lt;T&gt;</c> may live behind
    /// the v1 feature flag and may be staged into a sub-namespace. The
    /// match is namespace-agnostic on the leaf type name; the method
    /// name (<c>OnCustomEvent</c>) plus generic-arity (1) is precise
    /// enough that collisions are not a practical concern.
    /// </summary>
    private const string TargetMethodName = "OnCustomEvent";

    /// <summary>Container's metadata name, stripped of generic-arity suffix.</summary>
    private const string TargetContainerName = "ReactorBinding";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(ReactorCompileDiagnostics.CustomEventDelegateType);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(Analyze, OperationKind.Invocation);
    }

    private static void Analyze(OperationAnalysisContext ctx)
    {
        var inv = (IInvocationOperation)ctx.Operation;
        var method = inv.TargetMethod;

        // Cheap negative filters — return early per the analyzer-robustness
        // contract. No try/catch — these are syntactic checks.
        if (method.Name != TargetMethodName) return;
        if (method.TypeArguments.Length != 1) return;

        var container = method.ContainingType;
        if (container is null) return;
        if (container.Name != TargetContainerName) return;

        // TArgs of OnCustomEvent&lt;TArgs&gt;.
        var tArgs = method.TypeArguments[0];
        if (tArgs.TypeKind == TypeKind.Error) return;

        // Walk the subscribe + unsubscribe arguments. The third argument
        // (`handler`) is also a lambda but it doesn't subscribe events,
        // so it won't contain IEventAssignmentOperation — walking it is
        // harmless. We walk all argument values that resolve to a lambda
        // rather than filtering by parameter name, which keeps us
        // resilient to parameter renames in the protocol surface.
        foreach (var arg in inv.Arguments)
        {
            AnalyzeLambdaArgument(ctx, arg, tArgs, method);
        }
    }

    private static void AnalyzeLambdaArgument(
        OperationAnalysisContext ctx,
        IArgumentOperation arg,
        ITypeSymbol tArgs,
        IMethodSymbol containingMethod)
    {
        var lambda = FindLambda(arg.Value);
        if (lambda is null) return;

        // Walk every event +=/-= assignment inside the lambda body.
        foreach (var ea in lambda.Body.Descendants().OfType<IEventAssignmentOperation>())
        {
            AnalyzeEventAssignment(ctx, ea, tArgs, containingMethod);
        }
    }

    /// <summary>
    /// Roslyn wraps a lambda passed to a delegate-typed parameter in
    /// <see cref="IDelegateCreationOperation"/>, optionally with one or
    /// more <see cref="IConversionOperation"/> wrappers in between. Unwrap
    /// until we find the underlying anonymous function, or null if the
    /// argument isn't lambda-shaped.
    /// </summary>
    private static IAnonymousFunctionOperation? FindLambda(IOperation? op)
    {
        while (op is not null)
        {
            switch (op)
            {
                case IAnonymousFunctionOperation lam:
                    return lam;
                case IDelegateCreationOperation dc:
                    op = dc.Target;
                    continue;
                case IConversionOperation conv:
                    op = conv.Operand;
                    continue;
                default:
                    return null;
            }
        }
        return null;
    }

    private static void AnalyzeEventAssignment(
        OperationAnalysisContext ctx,
        IEventAssignmentOperation ea,
        ITypeSymbol tArgs,
        IMethodSymbol containingMethod)
    {
        if (ea.EventReference is not IEventReferenceOperation eref) return;

        var ev = eref.Event;
        if (ev is null) return;

        // The event's delegate type — e.g. RoutedEventHandler or
        // EventHandler<TArgs>.
        if (ev.Type is not INamedTypeSymbol evDelegate) return;
        if (evDelegate.TypeKind != TypeKind.Delegate) return;

        // Compute the event's EventArgs type by reading the Invoke
        // method's second parameter. Most event delegates follow the
        // (sender, args) shape; bail on exotic shapes — they aren't
        // the target of this rule.
        var invokeMethod = evDelegate.DelegateInvokeMethod;
        if (invokeMethod is null) return;
        if (invokeMethod.Parameters.Length != 2) return;
        var eventArgsType = invokeMethod.Parameters[1].Type;
        if (eventArgsType.TypeKind == TypeKind.Error) return;

        // The contract: regardless of whether the RHS is a bare handler
        // ref or wrapped via `new XHandler(h)`, the event being wired
        // must carry the same EventArgs that OnCustomEvent's TArgs
        // declares. A mismatch here means the wrap silently crosses
        // EventArgs types and the user's handler will see the wrong
        // type at runtime.
        if (SymbolEqualityComparer.Default.Equals(eventArgsType, tArgs))
        {
            return;
        }

        var location = ea.Syntax.GetLocation();
        ctx.ReportDiagnostic(Diagnostic.Create(
            ReactorCompileDiagnostics.CustomEventDelegateType,
            location,
            tArgs.ToDisplayString(),
            ev.ToDisplayString(),
            eventArgsType.ToDisplayString()));

        // Silence unused-param warning when no other consumer reads it.
        _ = containingMethod;
    }

}
