using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR0050: warns when a descriptor <c>OneWay</c> entry consumes an
/// <c>Optional&lt;T&gt;</c> getter without a dependency property to clear.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OneWayClearValueAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR0050";

    private const string IntentionalSkipPragma = "REACTOR0050: intentional skip";

    private static readonly LocalizableString Title =
        "Optional OneWay entry should provide dp";

    private static readonly LocalizableString MessageFormat =
        "OneWay entry for '{0}' uses Optional<T> without a dp: parameter — Unset will skip the write rather than call ClearValue. Provide dp: to enable WinUI value-precedence fallback, use OneWayConditional if skip-write was the intent, or suppress this warning with [NoClearValue].";

    private static readonly LocalizableString Description =
        "Optional<T> OneWay descriptor entries need a dependency property so Unset can clear the local value. Without dp:, Unset behaves as skip-write and cannot release WinUI value precedence.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Descriptor",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/specs/050-controlled-prop-authority-and-optional-t.md#64-analyzer-rule-reactor0050-public");

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
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;
        if (memberAccess.Name.Identifier.ValueText != "OneWay")
            return;

        var symbol = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
        if (symbol is null)
            return;
        if (!IsControlDescriptorOneWay(symbol))
            return;
        if (UsesDependencyPropertyOverload(symbol))
            return;
        if (HasNoClearValueAttribute(invocation, context.SemanticModel, context.CancellationToken))
            return;
        if (HasIntentionalSkipPragma(invocation, context.CancellationToken))
            return;

        var getArg = FindGetArgument(invocation);
        if (getArg is null)
            return;

        var getType = context.SemanticModel.GetTypeInfo(getArg.Expression, context.CancellationToken).ConvertedType;
        var returnType = (getType as INamedTypeSymbol)?.DelegateInvokeMethod?.ReturnType;
        if (!IsReactorOptional(returnType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation(),
            TryGetReturnedMemberName(getArg.Expression) ?? "value"));
    }

    private static bool IsControlDescriptorOneWay(IMethodSymbol symbol)
    {
        if (symbol.Name != "OneWay")
            return false;

        var containingType = symbol.ContainingType;
        return containingType is not null
            && containingType.MetadataName == "ControlDescriptor`2"
            && containingType.ContainingNamespace.ToDisplayString() == "Microsoft.UI.Reactor.Core.V1Protocol.Descriptor";
    }

    private static bool UsesDependencyPropertyOverload(IMethodSymbol symbol) =>
        symbol.Parameters.Any(p => p.Name == "dp");

    private static ArgumentSyntax? FindGetArgument(InvocationExpressionSyntax invocation)
    {
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (arg.NameColon?.Name.Identifier.ValueText == "get")
                return arg;
        }

        return invocation.ArgumentList.Arguments.Count > 0
            ? invocation.ArgumentList.Arguments[0]
            : null;
    }

    private static bool IsReactorOptional(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named)
            return false;
        return named.Name == "Optional"
            && named.Arity == 1
            && named.ContainingNamespace.ToDisplayString() == "Microsoft.UI.Reactor";
    }

    private static string? TryGetReturnedMemberName(ExpressionSyntax expression)
    {
        var body = expression switch
        {
            SimpleLambdaExpressionSyntax lambda => lambda.ExpressionBody,
            ParenthesizedLambdaExpressionSyntax lambda => lambda.ExpressionBody,
            _ => null,
        };

        return body switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax id => id.Identifier.ValueText,
            _ => null,
        };
    }

    private static bool HasNoClearValueAttribute(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var field = invocation.FirstAncestorOrSelf<FieldDeclarationSyntax>();
        if (field is not null && HasNoClearValueAttribute(field.AttributeLists, semanticModel, cancellationToken))
            return true;

        var property = invocation.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
        return property is not null && HasNoClearValueAttribute(property.AttributeLists, semanticModel, cancellationToken);
    }

    private static bool HasNoClearValueAttribute(
        SyntaxList<AttributeListSyntax> attributeLists,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var list in attributeLists)
        foreach (var attr in list.Attributes)
        {
            var attrType = semanticModel.GetTypeInfo(attr, cancellationToken).Type;
            if (attrType?.Name is "NoClearValueAttribute" or "NoClearValue")
                return true;

            var syntaxName = attr.Name.ToString();
            if (syntaxName.EndsWith("NoClearValue", StringComparison.Ordinal)
                || syntaxName.EndsWith("NoClearValueAttribute", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool HasIntentionalSkipPragma(
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var text = invocation.SyntaxTree.GetText(cancellationToken);
        var span = invocation.GetLocation().GetLineSpan().Span;
        var start = Math.Max(0, span.Start.Line - 2);
        var end = Math.Min(text.Lines.Count - 1, span.End.Line + 1);

        for (var line = start; line <= end; line++)
        {
            if (text.Lines[line].ToString().Contains(IntentionalSkipPragma, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
