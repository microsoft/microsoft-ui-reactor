using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// <c>REACTOR_LIFECYCLE_003</c> — flags a call to <c>Reconciler.UpdateChild</c> in code built on
/// Reactor, typically a <c>RegisterType</c> registration or an <c>IElementHandler</c> that hosts a
/// child element.
/// </summary>
/// <remarks>
/// <c>UpdateChild</c> patches a child control and returns null, or returns a new control when the
/// update had to build one. It is only correct after the internal <c>CanUpdate</c> check (same
/// element type and key), and its caller has to unmount a control it replaces. Code outside the
/// framework can't make that check, and the two call sites the repository had did neither: a child
/// that changed type threw <c>InvalidCastException</c>, and a replaced child's effect cleanups and
/// unmount callbacks never ran and its refs were never cleared. <c>Reconciler.Reconcile</c> takes
/// the same arguments,
/// does both and returns the control the slot should hold, so the rule points there and
/// <see cref="ReconcilerUpdateChildCodeFix"/> rewrites the common shape.
///
/// The rule binds to the one method symbol, so a same-named method elsewhere never trips it. It
/// stays quiet in the assembly that declares <c>Reconciler</c>, whose own slot owners make the
/// <c>CanUpdate</c> check first.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReconcilerUpdateChildAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_LIFECYCLE_003";

    internal const string ReconcilerTypeName = "Reconciler";
    internal const string ReconcilerNamespace = "Microsoft.UI.Reactor.Core";
    internal const string UpdateChildName = "UpdateChild";
    internal const string UnmountChildName = "UnmountChild";
    internal const string ReconcileName = "Reconcile";

    private static readonly LocalizableString Title =
        "Reconciler.UpdateChild skips the element-type check and leaves a replaced control mounted";

    private static readonly LocalizableString MessageFormat =
        "'Reconciler.UpdateChild' neither checks that the child's element type is unchanged nor unmounts a control it replaces; call 'Reconcile' and install its result when it differs from the existing control";

    private static readonly LocalizableString Description =
        "Reconciler.UpdateChild patches a child control in place and returns null, or returns a new " +
        "control when the update had to build one. It doesn't check that the new element has the " +
        "element type and key of the old one, so a child that changes type throws " +
        "InvalidCastException for a built-in control, and it leaves a control it replaced mounted, " +
        "so that subtree's effect cleanups and unmount callbacks never run and its refs are never " +
        "cleared. Reconciler.Reconcile " +
        "takes the same arguments, patches the child or unmounts it and mounts the new element, and " +
        "returns the control the slot should hold: install it when it differs from the existing " +
        "control. In an IElementHandler, UpdateContext.ReconcileChild does the same.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Lifecycle",
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

        // Cheap syntactic gate before any binding.
        var name = GetInvokedName(invocation);
        if (name is null || name.Identifier.ValueText != UpdateChildName)
            return;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is not IMethodSymbol method)
            return;
        if (!IsReconcilerMethod(method, UpdateChildName))
            return;

        // The framework's own slot owners check CanUpdate before calling UpdateChild.
        if (SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, context.Compilation.Assembly))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation()));
    }

    /// <summary>True when <paramref name="method"/> is <c>Reconciler.<paramref name="name"/></c>.</summary>
    internal static bool IsReconcilerMethod(IMethodSymbol method, string name)
    {
        var containingType = method.ContainingType;
        return method.Name == name
            && containingType is not null
            && containingType.Name == ReconcilerTypeName
            && containingType.ContainingNamespace?.ToDisplayString() == ReconcilerNamespace;
    }

    private static SimpleNameSyntax? GetInvokedName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name,
        MemberBindingExpressionSyntax mb => mb.Name,
        SimpleNameSyntax s => s,
        _ => null,
    };
}
