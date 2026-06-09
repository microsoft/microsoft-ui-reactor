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
            "XYFocusUp",
            "XYFocusDown",
            "XYFocusLeft",
            "XYFocusRight",
            "GeoView");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
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

        if (!IsLikelyHandlerOrDescriptorContext(assignment))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, currentAccess.GetLocation()));
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
