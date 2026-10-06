using System.Collections.Immutable;
using System.Linq;
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
/// the same arguments, does both and returns the control the slot should hold, so the rule points
/// there and <see cref="ReconcilerUpdateChildCodeFix"/> rewrites the common shape.
///
/// The rule resolves <c>Reconciler.UpdateChild</c> once per compilation and binds each candidate call
/// to it, so a same-named method elsewhere never trips it and a compilation without Reactor pays
/// nothing per call. It stays quiet in the assembly that declares <c>Reconciler</c>, whose own slot
/// owners make the <c>CanUpdate</c> check first.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReconcilerUpdateChildAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_LIFECYCLE_003";

    internal const string ReconcilerTypeName = "Reconciler";
    internal const string ReconcilerNamespace = "Microsoft.UI.Reactor.Core";
    internal const string ReconcilerMetadataName = ReconcilerNamespace + "." + ReconcilerTypeName;
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
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        // Resolve the method once. Without Reactor there is nothing to match, so no per-node
        // callback is registered. The framework's own slot owners check CanUpdate before calling
        // UpdateChild, so the rule also stays quiet in the assembly that declares Reconciler.
        var reconciler = context.Compilation.GetTypeByMetadataName(ReconcilerMetadataName);
        if (reconciler is null
            || SymbolEqualityComparer.Default.Equals(reconciler.ContainingAssembly, context.Compilation.Assembly))
            return;
        var updateChild = reconciler.GetMembers(UpdateChildName).OfType<IMethodSymbol>().ToImmutableArray();
        if (updateChild.IsEmpty)
            return;

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeInvocation(nodeContext, updateChild),
            SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, ImmutableArray<IMethodSymbol> updateChild)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Cheap syntactic gate before any binding.
        var name = GetInvokedName(invocation);
        if (name is null || name.Identifier.ValueText != UpdateChildName)
            return;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method
            || !updateChild.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, method.OriginalDefinition)))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation()));
    }

    /// <summary>
    /// True when <paramref name="method"/> is <c>Reconciler.<paramref name="name"/></c>. The code fix
    /// uses it to recognize <c>UnmountChild</c> calls.
    /// </summary>
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
