using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_DYM_004 — a Reactor factory called with <em>too few</em> arguments (e.g.
/// <c>ScrollViewer()</c> when the only overload is <c>ScrollViewer(Element child)</c>). The C#
/// compiler already rejects this with <c>CS7036</c> ("There is no argument given that corresponds to
/// the required formal parameter…"); this analyzer adds the actionable <em>did-you-mean</em>: the
/// factory's full parameter shape as a named-argument hint.
/// </summary>
/// <remarks>
/// <para>
/// This is the argument-shape phase of the in-build "did you mean" family (design of record: spec
/// 061 §7). CS7036/CS1503 are <b>rare</b> in the eval corpus (no tuning data), so precision is
/// everything — "a wrong suggestion is worse than no suggestion" (spec 038 §1). The scope is
/// deliberately the single, unambiguous slice a spike proved clean.
/// </para>
/// <para>
/// <b>No code fix (by design).</b> The suggested shape contains placeholders (<c>child: &lt;Element&gt;</c>)
/// that would <em>not</em> compile if inserted, so this ships as a diagnostic <b>message only</b>. A
/// "fix" that produces uncompilable code is worse than none.
/// </para>
/// <para>
/// <b>False-positive gating (all must hold; validated by the spec 061 §7 spike).</b>
/// </para>
/// <list type="number">
///   <item>the invocation did not bind and failed <b>overload resolution</b> against a <b>unique</b>
///     Reactor <c>Factories</c> candidate (shared <see cref="ArgumentShapeGate"/>) — this alone
///     silences multi-overload factories such as <c>Button()</c> (three overloads: there is no single
///     shape to suggest) and non-Reactor look-alikes;</item>
///   <item>every argument is <b>positional</b> — a named argument is a different reasoning problem;</item>
///   <item>strictly <b>fewer</b> arguments than the candidate's required (non-optional, non-<c>params</c>)
///     parameter count — i.e. genuinely the "missing argument" shape, not a type mismatch (CS1503,
///     REACTOR_DYM_005) or a surplus (CS1501);</item>
///   <item>no supplied argument is an <b>error type</b> (cascading edit-in-progress errors);</item>
///   <item>every supplied argument <b>implicitly converts</b> to its positional parameter — so a call
///     that is <em>both</em> short an argument <em>and</em> has a mismatched one (e.g. <c>Grid("x")</c>)
///     falls through to neither analyzer rather than firing a misleading "missing argument" hint.</item>
/// </list>
/// <para>
/// <b>Severity: Warning.</b> It fires only alongside the compiler's own CS7036, so Warning is safe
/// under <c>TreatWarningsAsErrors</c> (the build already fails) while still surfacing the hint in a
/// plain <c>dotnet build</c> and the IDE. Tunable via <c>.editorconfig</c>.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingFactoryArgumentAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_DYM_004";

    private static readonly LocalizableString Title =
        "Reactor factory call is missing a required argument";
    private static readonly LocalizableString MessageFormat =
        "'{0}' is missing a required argument — did you mean '{0}({1})'?";
    private static readonly LocalizableString Description =
        "A Reactor factory was called with too few arguments (compiler CS7036). The suggested shape lists the factory's parameters as named arguments; supply the missing one(s).";

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
        // Compilation-level gate: only Reactor-referencing projects. Resolve the Factories hub once.
        var factoriesType = context.Compilation.GetTypeByMetadataName(ArgumentShapeGate.FactoriesMetadataName);
        if (factoriesType is null)
            return;

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeInvocation(ctx, factoriesType),
            SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, INamedTypeSymbol factoriesType)
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

        // "Missing argument" means fewer args than the required (non-optional, non-params) parameters.
        var required = candidate.Parameters.Count(p => !p.IsOptional && !p.IsParams);
        var provided = args.Count;
        if (provided >= required)
            return;

        // Cascading edit-in-progress errors — the overload-resolution outcome is untrustworthy.
        if (ArgumentShapeGate.AnyArgumentIsErrorType(args, model, ct))
            return;

        // Every supplied argument must positionally convert; otherwise the call is a type mismatch
        // (CS1503 territory) as well as short, and "missing argument" would be the wrong hint.
        if (!AllProvidedArgumentsConvert(candidate, args, model, ct))
            return;

        var shape = string.Join(", ", candidate.Parameters.Select(FormatParameter));
        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            ArgumentShapeGate.CalleeLocation(invocation),
            candidate.Name,
            shape));
    }

    private static bool AllProvidedArgumentsConvert(
        IMethodSymbol candidate, SeparatedSyntaxList<ArgumentSyntax> args, SemanticModel model, CancellationToken ct)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (i >= candidate.Parameters.Length)
                return false; // more args than parameters — a surplus (CS1501), not a shortage.
            var argType = model.GetTypeInfo(args[i].Expression, ct).Type;
            if (argType is null)
                continue; // untyped argument (lambda / null literal) — accept, can't disprove.
            var conversion = model.Compilation.ClassifyCommonConversion(argType, candidate.Parameters[i].Type);
            if (!conversion.Exists || !conversion.IsImplicit)
                return false;
        }
        return true;
    }

    // Named-argument hint form, e.g. "child: <Element>". Optional parameters are marked so the hint
    // never implies an optional must be supplied.
    private static string FormatParameter(IParameterSymbol p)
    {
        var shape = $"{p.Name}: <{p.Type.Name}>";
        return p.IsOptional ? shape + " = …" : shape;
    }
}
