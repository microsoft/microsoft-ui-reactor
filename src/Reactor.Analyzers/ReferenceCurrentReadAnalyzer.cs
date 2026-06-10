using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_REF_001: detects the high-confidence anti-pattern of assigning an
/// ElementRef.Current snapshot into a known reference property from a control
/// handler/descriptor mount-update path.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReferenceCurrentReadAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_REF_001";

    private static readonly LocalizableString Title =
        "Use a reactive reference edge instead of ElementRef.Current";

    private static readonly LocalizableString MessageFormat =
        "Reading ElementRef.Current to set a reference property is non-reactive (breaks on late binding/unmount — spec 057 §2.3). Declare a reactive reference edge via descriptor.Reference / binding.Reference instead.";

    private static readonly LocalizableString Description =
        "ElementRef.Current is a snapshot. Reference dependency properties should be wired through descriptor.Reference or binding.Reference so late target mount, source unmount, and referrer teardown are handled by the Reactor graph.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Reference",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    private static readonly ImmutableHashSet<string> KnownReferenceProperties =
        ImmutableHashSet.Create(
            "Target",
            "LabeledBy",
            "PlacementTarget",
            "XYFocusUp",
            "XYFocusDown",
            "XYFocusLeft",
            "XYFocusRight",
            "GeoView");

    // Static/attached reference setters, e.g. AutomationProperties.SetLabeledBy(control, ref.Current).
    private static readonly ImmutableHashSet<string> KnownReferenceSetterMethods =
        ImmutableHashSet.Create(
            "SetLabeledBy",
            "SetDescribedBy",
            "SetFlowsTo",
            "SetFlowsFrom");

    // Attached relationship-list accessors whose returned list gets a ref.Current
    // pushed into it, e.g. AutomationProperties.GetDescribedBy(control).Add(ref.Current).
    private static readonly ImmutableHashSet<string> KnownReferenceListAccessors =
        ImmutableHashSet.Create(
            "GetDescribedBy",
            "GetFlowsTo",
            "GetFlowsFrom");

    private static readonly ImmutableHashSet<string> ListMutatorMethods =
        ImmutableHashSet.Create("Add", "Insert");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;

        if (assignment.Left is not MemberAccessExpressionSyntax leftMember)
            return;

        if (!KnownReferenceProperties.Contains(leftMember.Name.Identifier.ValueText))
            return;

        var currentAccess = TryFindCurrentAccess(assignment.Right);
        if (currentAccess is null)
            return;

        if (!IsElementRefCurrent(currentAccess, context.SemanticModel, context.CancellationToken))
            return;

        // CR-007: a bare property-name match (e.g. an unrelated `Target` property) is
        // not enough — require the assigned member to live on a WinUI control
        // (FrameworkElement-derived, which also covers third-party controls such as
        // ArcGIS GeoView). Only fall back to the name/context heuristic when the symbol
        // cannot be resolved, so incomplete compilations still surface the anti-pattern.
        if (!IsReferencePropertyOwner(assignment.Left, context.SemanticModel, context.CancellationToken))
            return;

        if (!IsLikelyHandlerOrDescriptorContext(assignment))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, currentAccess.GetLocation()));
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name.Identifier.ValueText;

        // CR-006: AutomationProperties.SetLabeledBy(control, ref.Current) and the
        // relationship-list .Add/.Insert(ref.Current) forms are just as non-reactive as
        // the assignment form but were previously missed.
        bool isAttachedSetter =
            KnownReferenceSetterMethods.Contains(methodName) &&
            IsAutomationPropertiesReceiver(memberAccess.Expression, context.SemanticModel, context.CancellationToken);

        bool isRelationshipListMutation =
            ListMutatorMethods.Contains(methodName) &&
            ReceiverIsRelationshipListAccessor(memberAccess.Expression, context.SemanticModel, context.CancellationToken);

        if (!isAttachedSetter && !isRelationshipListMutation)
            return;

        if (!IsLikelyHandlerOrDescriptorContext(invocation))
            return;

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            var currentAccess = TryFindCurrentAccess(argument.Expression);
            if (currentAccess is null)
                continue;

            if (!IsElementRefCurrent(currentAccess, context.SemanticModel, context.CancellationToken))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(Rule, currentAccess.GetLocation()));
        }
    }

    private static bool IsAutomationPropertiesReceiver(
        ExpressionSyntax receiver,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        // Symbol-resolved type name is the reliable signal; fall back to the textual
        // receiver name when symbols are unavailable (incomplete compilation).
        var type = semanticModel.GetTypeInfo(receiver, cancellationToken).Type
                   ?? (semanticModel.GetSymbolInfo(receiver, cancellationToken).Symbol as INamedTypeSymbol);
        if (type is not null)
            return type.Name == "AutomationProperties";

        return receiver is IdentifierNameSyntax id && id.Identifier.ValueText == "AutomationProperties"
            || receiver is MemberAccessExpressionSyntax ma && ma.Name.Identifier.ValueText == "AutomationProperties";
    }

    private static bool ReceiverIsRelationshipListAccessor(
        ExpressionSyntax receiver,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (StripParentheses(receiver) is not InvocationExpressionSyntax accessorInvocation)
            return false;

        if (accessorInvocation.Expression is not MemberAccessExpressionSyntax accessorMember)
            return false;

        return KnownReferenceListAccessors.Contains(accessorMember.Name.Identifier.ValueText)
            && IsAutomationPropertiesReceiver(accessorMember.Expression, semanticModel, cancellationToken);
    }

    /// <summary>
    /// CR-007: returns true when the assignment target is a member of a WinUI control
    /// (a <c>Microsoft.UI.Xaml.FrameworkElement</c> subtype, which also covers third-party
    /// controls like ArcGIS <c>GeoView</c>). When the symbol cannot be resolved we keep
    /// the looser behavior so incomplete builds still flag the anti-pattern.
    /// </summary>
    private static bool IsReferencePropertyOwner(
        ExpressionSyntax left,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(left, cancellationToken).Symbol;
        var owner = symbol switch
        {
            IPropertySymbol property => property.ContainingType,
            IFieldSymbol field => field.ContainingType,
            _ => null,
        };

        if (owner is null)
            return true; // unresolved — preserve existing detection.

        return InheritsFromFrameworkElement(owner);
    }

    private static bool InheritsFromFrameworkElement(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.Name == "FrameworkElement" &&
                current.ContainingNamespace?.ToDisplayString() == "Microsoft.UI.Xaml")
            {
                return true;
            }
        }
        return false;
    }

    private static MemberAccessExpressionSyntax? TryFindCurrentAccess(ExpressionSyntax expression)
    {
        expression = StripParentheses(expression);

        if (expression is MemberAccessExpressionSyntax member &&
            member.Name.Identifier.ValueText == "Current")
        {
            return member;
        }

        if (expression is BinaryExpressionSyntax binary &&
            binary.IsKind(SyntaxKind.AsExpression))
        {
            return TryFindCurrentAccess(binary.Left);
        }

        if (expression is CastExpressionSyntax cast)
            return TryFindCurrentAccess(cast.Expression);

        if (expression is PostfixUnaryExpressionSyntax postfix &&
            postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
        {
            return TryFindCurrentAccess(postfix.Operand);
        }

        return null;
    }

    private static ExpressionSyntax StripParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parens)
            expression = parens.Expression;
        return expression;
    }

    private static bool IsElementRefCurrent(
        MemberAccessExpressionSyntax currentAccess,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var receiverType = semanticModel.GetTypeInfo(currentAccess.Expression, cancellationToken).Type;
        if (receiverType is null)
            return false;

        if (receiverType.Name != "ElementRef")
            return false;

        var ns = receiverType.ContainingNamespace?.ToDisplayString();
        return ns == "Microsoft.UI.Reactor.Input";
    }

    private static bool IsLikelyHandlerOrDescriptorContext(SyntaxNode node)
    {
        var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method is not null)
        {
            var methodName = method.Identifier.ValueText;
            if (methodName is "Mount" or "Update")
                return true;
        }

        var type = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (type is null)
            return false;

        var typeName = type.Identifier.ValueText;
        return typeName.EndsWith("Handler", System.StringComparison.Ordinal) ||
               typeName.EndsWith("Descriptor", System.StringComparison.Ordinal) ||
               typeName.EndsWith("DescriptorHandler", System.StringComparison.Ordinal) ||
               typeName.EndsWith("Binding", System.StringComparison.Ordinal);
    }
}
