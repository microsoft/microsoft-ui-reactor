using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_DYM_005 — a <c>string</c> passed to a Reactor factory parameter that expects an
/// <c>Element</c> (e.g. <c>ScrollViewer("hi")</c> or <c>Border("hi")</c>). The C# compiler already
/// rejects this with <c>CS1503</c> ("cannot convert from 'string' to 'Element'"); this analyzer adds
/// the actionable <em>did-you-mean</em>: wrap the string in a text factory such as <c>TextBlock(…)</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the CS1503 half of the argument-shape phase (spec 061 §7). It is <b>not</b> a general
/// type-mismatch matcher — it mirrors exactly the one narrow, high-confidence special case the
/// <c>mur check</c> <c>SymbolSuggester</c> encodes for CS1503 (string-supplied-where-Element-expected),
/// and no more. A parity test locks the two so the analyzer never claims a shape the CLI wouldn't.
/// </para>
/// <para>
/// <b>Deliberately not covered.</b> The spike (spec 061 §7) showed the CLI's other CS1503 special
/// case — <c>Action&lt;T&gt;</c> supplied where a parameterless <c>Action</c> is expected — only
/// fires when a <em>typed</em> <c>Action&lt;T&gt;</c> variable is passed, never for the far more
/// common <em>lambda</em> shape (a lambda argument has no type to classify). That is poor coverage
/// for real code, so this analyzer omits it; general numeric/type mismatches, multi-overload
/// factories, <c>params</c>, named-argument and generic-inference calls are all out of scope too.
/// </para>
/// <para>
/// <b>No code fix (by design).</b> Wrapping the string in <c>TextBlock(…)</c> compiles only when a
/// text factory is in scope (a bare <c>using static</c> vs. a qualified call changes the required
/// syntax), so an automated fix risks producing uncompilable code. This ships as a diagnostic
/// <b>message only</b>.
/// </para>
/// <para>
/// <b>False-positive gating (all must hold).</b> The invocation failed overload resolution against a
/// <b>unique</b> Reactor <c>Factories</c> candidate (shared <see cref="ArgumentShapeGate"/>); all
/// arguments are positional; the candidate has no <c>params</c> tail (positional mapping would be
/// unsafe); <b>exactly one</b> positional argument fails to convert; that argument's type is
/// <c>string</c>; and its parameter is <c>Element</c> or an <c>Element</c> subtype. Because it fires
/// only alongside the compiler's CS1503, <b>Warning</b> severity is safe under
/// <c>TreatWarningsAsErrors</c> while still surfacing in a plain <c>dotnet build</c>.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StringForElementArgumentAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_DYM_005";

    private static readonly LocalizableString Title =
        "Reactor factory expects an Element, not a string";
    private static readonly LocalizableString MessageFormat =
        "'{0}' expects an Element here but a string was supplied — wrap it in a text factory such as TextBlock, Heading or Caption";
    private static readonly LocalizableString Description =
        "A string was passed where a Reactor Element is required (compiler CS1503). Wrap it in a text factory such as TextBlock(\"…\").";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.DidYouMean",
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
        // Both types must be present: the Factories hub (gate) and the Element base (the target shape).
        var factoriesType = context.Compilation.GetTypeByMetadataName(ArgumentShapeGate.FactoriesMetadataName);
        if (factoriesType is null)
            return;
        var elementType = context.Compilation.GetTypeByMetadataName(ArgumentShapeGate.ElementMetadataName);
        if (elementType is null)
            return;

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeInvocation(ctx, factoriesType, elementType),
            SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context, INamedTypeSymbol factoriesType, INamedTypeSymbol elementType)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var model = context.SemanticModel;
        var ct = context.CancellationToken;

        var candidate = ArgumentShapeGate.UniqueReactorFactoryCandidate(
            model.GetSymbolInfo(invocation, ct), factoriesType);
        if (candidate is null)
            return;

        var args = invocation.ArgumentList.Arguments;
        if (ArgumentShapeGate.HasNamedArgument(args))
            return;

        // A params tail makes positional arg->parameter mapping ambiguous; stay out of that shape.
        foreach (var p in candidate.Parameters)
        {
            if (p.IsParams)
                return;
        }

        if (!TryFindSingleFailingArgument(candidate, args, model, ct, out var failingIndex))
            return;

        var argType = model.GetTypeInfo(args[failingIndex].Expression, ct).Type;
        if (argType is null || argType.SpecialType != SpecialType.System_String)
            return;

        if (!IsElementType(candidate.Parameters[failingIndex].Type, elementType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            args[failingIndex].GetLocation(),
            candidate.Name));
    }

    // Exactly one positional argument must fail to implicitly convert to its parameter. Zero means the
    // call failed for some other reason we don't model; two or more is ambiguous (and never the clean
    // single-string-arg shape). Any error-typed argument bails (cascading).
    private static bool TryFindSingleFailingArgument(
        IMethodSymbol candidate, SeparatedSyntaxList<ArgumentSyntax> args, SemanticModel model,
        CancellationToken ct, out int failingIndex)
    {
        failingIndex = -1;
        var failingCount = 0;
        for (var i = 0; i < args.Count; i++)
        {
            if (i >= candidate.Parameters.Length)
                return false; // surplus argument (CS1501) — a different error.
            var argType = model.GetTypeInfo(args[i].Expression, ct).Type;
            if (argType is IErrorTypeSymbol || (argType is not null && argType.TypeKind == TypeKind.Error))
                return false; // cascading edit-in-progress error.
            // Classify the argument EXPRESSION so an untyped lambda / `null` still counts as a failure
            // when it doesn't fit its parameter — otherwise a second, differently-broken argument would
            // be silently ignored and let the single-string-arg gate mis-fire.
            var conversion = model.ClassifyConversion(args[i].Expression, candidate.Parameters[i].Type);
            if (!conversion.Exists || !conversion.IsImplicit)
            {
                failingIndex = i;
                failingCount++;
            }
        }
        return failingCount == 1;
    }

    private static bool IsElementType(ITypeSymbol type, INamedTypeSymbol elementType)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, elementType))
                return true;
        }
        return false;
    }
}
