using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CombinedSizeModifierAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_PERF_001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Combine adjacent dimension modifiers",
        "Use 'Size' instead of adjacent 'Width' and 'Height' calls to avoid an intermediate layout, modifier, and element copy",
        "Reactor.Performance",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Size sets both dimensions with one immutable update. Combining adjacent Width/Height calls reduces construction allocations without changing the resulting element.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (TryCombine(invocation, context.SemanticModel, context.CancellationToken) is null)
            return;

        // Let the outer pair own an overlapping Width/Height/Width run.
        if (invocation.Parent is MemberAccessExpressionSyntax access
            && access.Parent is InvocationExpressionSyntax next
            && TryCombine(next, context.SemanticModel, context.CancellationToken) is not null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
    }

    internal static InvocationExpressionSyntax? TryCombine(
        InvocationExpressionSyntax outer, SemanticModel model, CancellationToken ct)
    {
        if (outer.Expression is not MemberAccessExpressionSyntax outerAccess
            || outerAccess.Expression is not InvocationExpressionSyntax inner
            || inner.Expression is not MemberAccessExpressionSyntax innerAccess
            || inner.ArgumentList.Arguments.Count != 1
            || outer.ArgumentList.Arguments.Count != 1)
            return null;

        var first = innerAccess.Name.Identifier.ValueText;
        var second = outerAccess.Name.Identifier.ValueText;
        if (!((first == "Width" && second == "Height") || (first == "Height" && second == "Width")))
            return null;

        var extensions = model.Compilation.GetTypeByMetadataName("Microsoft.UI.Reactor.ElementExtensions");
        if (extensions is null
            || model.GetSymbolInfo(inner, ct).Symbol is not IMethodSymbol innerMethod
            || model.GetSymbolInfo(outer, ct).Symbol is not IMethodSymbol outerMethod
            || !IsDimension(innerMethod, extensions, first)
            || !IsDimension(outerMethod, extensions, second)
            || !SymbolEqualityComparer.Default.Equals(innerMethod.ReturnType, outerMethod.ReturnType)
            || !IsBuiltInConstruction(innerAccess.Expression, model, extensions.ContainingAssembly, ct))
            return null;

        // Do not discard comments or directives on tokens removed by the rewrite.
        if (outer.DescendantTrivia().Any(t => t.SpanStart >= innerAccess.Expression.Span.End
            && !t.IsKind(SyntaxKind.WhitespaceTrivia) && !t.IsKind(SyntaxKind.EndOfLineTrivia)))
            return null;

        if (outer.Ancestors().Any(node => node is QueryExpressionSyntax)
            || outer.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().Any(lambda =>
            model.GetTypeInfo(lambda, ct).ConvertedType is INamedTypeSymbol type
            && type.OriginalDefinition.MetadataName == "Expression`1"
            && type.ContainingNamespace.ToDisplayString() == "System.Linq.Expressions"))
            return null;

        var originalName = outerAccess.Name is GenericNameSyntax ? outerAccess.Name : innerAccess.Name;
        SimpleNameSyntax name = originalName is GenericNameSyntax generic
            ? generic.WithIdentifier(SyntaxFactory.Identifier("Size"))
            : SyntaxFactory.IdentifierName("Size");
        var arguments = new[]
        {
            Named(inner.ArgumentList.Arguments[0], first == "Width" ? "width" : "height"),
            Named(outer.ArgumentList.Arguments[0], second == "Width" ? "width" : "height"),
        };
        var combined = outer.WithExpression(innerAccess.WithName(name))
            .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
                arguments, new[] { SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space) })))
            .WithTriviaFrom(outer);
        var replacement = model.GetSpeculativeSymbolInfo(
            outer.SpanStart, combined, SpeculativeBindingOption.BindAsExpression).Symbol as IMethodSymbol;

        return replacement?.ReducedFrom is { } definition
            && definition.Name == "Size"
            && SymbolEqualityComparer.Default.Equals(definition.ContainingType, extensions)
            && replacement.Parameters.Length == 2
            && replacement.Parameters.All(p => p.Type.SpecialType == SpecialType.System_Double && p.RefKind == RefKind.None)
            && SymbolEqualityComparer.Default.Equals(replacement.ReturnType, outerMethod.ReturnType)
                ? combined : null;
    }

    private static bool IsDimension(IMethodSymbol method, INamedTypeSymbol extensions, string name) =>
        method.ReducedFrom is { } definition
        && definition.Name == name
        && SymbolEqualityComparer.Default.Equals(definition.ContainingType, extensions)
        && method.Parameters.Length == 1
        && method.Parameters[0].Type.SpecialType == SpecialType.System_Double
        && method.Parameters[0].RefKind == RefKind.None
        && method.TypeArguments.Length == 1
        && SymbolEqualityComparer.Default.Equals(method.ReturnType, method.TypeArguments[0]);

    private static ArgumentSyntax Named(ArgumentSyntax argument, string name) =>
        argument.WithNameColon(SyntaxFactory.NameColon(name)
            .WithColonToken(SyntaxFactory.Token(SyntaxKind.ColonToken).WithTrailingTrivia(SyntaxFactory.Space)));

    private static bool IsBuiltInConstruction(
        ExpressionSyntax expression, SemanticModel model, IAssemblySymbol assembly, CancellationToken ct)
    {
        // A custom record's copy constructor can run user code. Unknown receivers
        // are deliberately excluded rather than removing an observable clone.
        while (expression is InvocationExpressionSyntax invocation
            && model.GetSymbolInfo(invocation, ct).Symbol is IMethodSymbol method)
        {
            if (!SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, assembly))
                return false;
            if (method.ReducedFrom is not null
                && method.ContainingType.ToDisplayString() == "Microsoft.UI.Reactor.ElementExtensions"
                && invocation.Expression is MemberAccessExpressionSyntax access
                && SymbolEqualityComparer.Default.Equals(method.ReturnType, model.GetTypeInfo(access.Expression, ct).Type))
            {
                expression = access.Expression;
                continue;
            }
            return method.ContainingType.ToDisplayString() == "Microsoft.UI.Reactor.Factories"
                && IsBuiltInElement(method.ReturnType, assembly);
        }
        return expression is BaseObjectCreationExpressionSyntax { Initializer: null }
            && IsBuiltInElement(model.GetTypeInfo(expression, ct).Type, assembly);
    }

    private static bool IsBuiltInElement(ITypeSymbol? type, IAssemblySymbol assembly) =>
        type is INamedTypeSymbol { IsAbstract: false }
        && type.ContainingNamespace.ToDisplayString() == "Microsoft.UI.Reactor.Core"
        && SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, assembly);
}
