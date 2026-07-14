using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// <c>REACTOR_DESC_001</c> — flags a handler-factory lambda passed to one of the four
/// <c>ControlRegistry</c> registration entry points
/// (<c>Register</c>, <c>RegisterForDerivedTypes</c>, <c>RegisterDecorator</c>,
/// <c>RegisterDecoratorForDerivedTypes</c>) that is <b>not</b> declared <c>static</c>.
/// </summary>
/// <remarks>
/// This is a <b>perf / trim-hygiene rule for control authors</b>, not a correctness bug.
/// A non-capturing <c>() =&gt; new MyHandler()</c> is already interned into a static
/// field by Roslyn, so the primary cost is trimmer hygiene: a <c>static</c> factory keeps
/// the trimmer able to drop the holder→handler→control chain from a NativeAOT publish.
/// A capturing lambda additionally allocates a closure on every registration run — but such
/// a lambda cannot be marked <c>static</c>, so for those the analyzer only nudges (the code
/// fix declines to auto-insert <c>static</c>). The authoring guide marks the keyword
/// mandatory and names this analyzer as the sole guard (issue #486).
///
/// Detection is a cheap syntactic gate on the invoked member name, then a single semantic
/// check that the call resolves to <see cref="ControlRegistryTypeName"/> so an unrelated
/// method that merely shares one of the four names never trips the rule.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StaticRegisterLambdaAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_DESC_001";

    private const string ControlRegistryTypeName = "ControlRegistry";
    private const string ControlRegistryNamespace = "Microsoft.UI.Reactor.Core.V1Protocol";

    /// <summary>
    /// The four <c>ControlRegistry</c> registration entry points, each taking a single
    /// <c>Func&lt;…handler…&gt;</c> factory argument
    /// (<c>ControlRegistry.cs:99,180,214,237</c>).
    /// </summary>
    private static readonly ImmutableHashSet<string> RegisterMethodNames =
        ImmutableHashSet.Create(
            System.StringComparer.Ordinal,
            "Register",
            "RegisterForDerivedTypes",
            "RegisterDecorator",
            "RegisterDecoratorForDerivedTypes");

    private static readonly LocalizableString Title =
        "ControlRegistry registration lambda should be static";

    private static readonly LocalizableString MessageFormat =
        "The handler factory passed to 'ControlRegistry.{0}' should be a 'static' lambda for trim/AOT hygiene (refactor out any captured state so it can be marked static)";

    private static readonly LocalizableString Description =
        "ControlRegistry registration factories should be static lambdas. A non-capturing " +
        "'() => new MyHandler()' is already cached in a static field by Roslyn, so marking it " +
        "'static' costs nothing at runtime — its value is trim hygiene: the static form keeps the " +
        "trimmer able to drop the holder→handler→control chain from a NativeAOT publish. A capturing " +
        "lambda additionally allocates a closure per registration and cannot be made static; refactor " +
        "the capture out. This is a performance/trimming recommendation for control authors, not a " +
        "correctness bug.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Descriptor",
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

        // Cheap syntactic gate 1: the invoked simple name is one of the four entry points.
        var invokedName = GetInvokedName(invocation);
        if (invokedName is null || !RegisterMethodNames.Contains(invokedName))
            return;

        // Cheap syntactic gate 2: a single lambda argument that lacks the 'static' modifier.
        var args = invocation.ArgumentList.Arguments;
        if (args.Count != 1)
            return;
        if (args[0].Expression is not AnonymousFunctionExpressionSyntax lambda)
            return;
        if (lambda.Modifiers.Any(SyntaxKind.StaticKeyword))
            return;

        // Semantic confirmation: the call really binds to ControlRegistry (not an unrelated
        // method that merely shares one of the four names). Purely-syntactic detection would
        // false-positive here, so this one symbol lookup is the FP guard.
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is not IMethodSymbol method)
            return;
        if (!IsControlRegistryMethod(method))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, lambda.GetLocation(), invokedName));
    }

    private static bool IsControlRegistryMethod(IMethodSymbol method)
    {
        var containingType = method.ContainingType;
        return containingType is not null
            && containingType.Name == ControlRegistryTypeName
            && containingType.ContainingNamespace?.ToDisplayString() == ControlRegistryNamespace;
    }

    /// <summary>
    /// The simple (unqualified) name of the invoked method — handles both the qualified
    /// <c>ControlRegistry.Register&lt;E,C&gt;(…)</c> member-access form (where the name is a
    /// <see cref="GenericNameSyntax"/>) and an unqualified static-imported call.
    /// </summary>
    private static string? GetInvokedName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        MemberBindingExpressionSyntax mb => mb.Name.Identifier.ValueText,
        SimpleNameSyntax s => s.Identifier.ValueText,
        _ => null,
    };
}
