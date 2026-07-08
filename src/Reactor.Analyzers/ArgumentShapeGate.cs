using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Shared false-positive gating for the argument-shape "did you mean" analyzers
/// (<see cref="MissingFactoryArgumentAnalyzer"/> — REACTOR_DYM_004 — and
/// <see cref="StringForElementArgumentAnalyzer"/> — REACTOR_DYM_005). Both react to the same
/// underlying Roslyn state — an invocation that failed <em>overload resolution</em> against a single
/// Reactor <c>Factories</c> candidate — so the gate lives in one place to keep the two from drifting.
/// </summary>
/// <remarks>
/// The gate encodes the conclusions of the spec 061 §7 false-positive spike:
/// <list type="bullet">
///   <item>Only <see cref="CandidateReason.OverloadResolutionFailure"/> is an argument-shape error;
///     an accessibility failure reports <see cref="CandidateReason.Inaccessible"/> (a different fix)
///     and a genuinely unknown name reports <see cref="CandidateReason.None"/>.</item>
///   <item>A <b>unique</b> candidate is required: a multi-overload factory (e.g. <c>Button()</c>,
///     which has three overloads) leaves several candidates, so there is no single argument shape to
///     suggest — the spike showed this is the dominant would-be false positive, and the length gate
///     here is what silences it.</item>
///   <item>The candidate must belong to Reactor's <c>Factories</c> type — never an unrelated
///     look-alike in another namespace, nor a Reactor fluent-modifier extension method.</item>
/// </list>
/// </remarks>
internal static class ArgumentShapeGate
{
    /// <summary>The Reactor factory hub whose calls these analyzers augment.</summary>
    internal const string FactoriesMetadataName = "Microsoft.UI.Reactor.Factories";

    /// <summary>The Reactor element base type used by <see cref="StringForElementArgumentAnalyzer"/>.</summary>
    internal const string ElementMetadataName = "Microsoft.UI.Reactor.Core.Element";

    /// <summary>
    /// Returns the single Reactor <c>Factories</c> method a non-binding invocation resolved to, or
    /// <see langword="null"/> when any gate fails (the invocation bound, the failure was not an
    /// overload-resolution failure, the candidate is not unique, or it is not a Reactor factory).
    /// </summary>
    internal static IMethodSymbol? UniqueReactorFactoryCandidate(SymbolInfo info, INamedTypeSymbol factoriesType)
    {
        // Must NOT have bound — a valid call is never touched (its Symbol is non-null).
        if (info.Symbol is not null)
            return null;
        // Argument-shape failures report OverloadResolutionFailure; accessibility / unknown-name
        // report other reasons and are a different fix, so bail.
        if (info.CandidateReason != CandidateReason.OverloadResolutionFailure)
            return null;
        // A unique best candidate is the whole precision story — see the class remarks.
        if (info.CandidateSymbols.Length != 1)
            return null;
        if (info.CandidateSymbols[0] is not IMethodSymbol method)
            return null;
        // Hard Reactor gate: only Factories methods, never a same-named API elsewhere.
        return SymbolEqualityComparer.Default.Equals(method.ContainingType, factoriesType) ? method : null;
    }

    /// <summary>
    /// True when any supplied argument's type is an error type — a cascading edit-in-progress error
    /// whose overload-resolution outcome cannot be trusted, so both analyzers stay silent.
    /// </summary>
    internal static bool AnyArgumentIsErrorType(
        SeparatedSyntaxList<ArgumentSyntax> args, SemanticModel model, CancellationToken ct)
    {
        foreach (var arg in args)
        {
            var type = model.GetTypeInfo(arg.Expression, ct).Type;
            if (type is null)
                continue; // untyped (lambda / null literal) — not an error type.
            if (type is IErrorTypeSymbol || type.TypeKind == TypeKind.Error)
                return true;
        }
        return false;
    }

    /// <summary>True when any argument is passed by name; both analyzers only reason about positional args.</summary>
    internal static bool HasNamedArgument(SeparatedSyntaxList<ArgumentSyntax> args)
    {
        foreach (var arg in args)
        {
            if (arg.NameColon is not null)
                return true;
        }
        return false;
    }

    /// <summary>Highlights the callee name (the identifier, or the member name in <c>X.Foo()</c>).</summary>
    internal static Location CalleeLocation(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.GetLocation(),
        _ => invocation.Expression.GetLocation(),
    };
}
