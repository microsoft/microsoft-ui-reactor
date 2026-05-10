// Tier-2 suggester contract. Spec 038 §5.
//
// A suggester is a pure function of (Compilation, Diagnostic, SyntaxNode) →
// (suggestion_text, confidence, evidence). It MUST NOT touch the file system,
// network, or spawn any process — those side effects belong upstream in
// CompilationLoader. The contract exists so suggesters are unit-testable by
// constructing a CSharpCompilation in-memory.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Microsoft.UI.Reactor.Cli.Check.Suggesters;

internal interface ISuggester
{
    /// <summary>
    /// Stable identifier used in telemetry payloads (no PII / no source text).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Inspects a single diagnostic. Returns a suggestion or
    /// <see cref="SuggestionResult.Silent"/> if no high-confidence candidate is found.
    /// </summary>
    SuggestionResult Suggest(in SuggesterContext ctx);
}

internal readonly record struct SuggesterContext(
    CSharpCompilation Compilation,
    Diagnostic Diagnostic,
    SyntaxNode? Node,
    ITypeSymbol? Receiver,
    FactoryIndex Factories);

/// <summary>
/// A suggester result. <c>Text == null</c> is the silent path — the suggester
/// looked at the diagnostic but found nothing it could reliably propose.
/// Confidence is on [0, 1]; CheckCommand drops anything below the active T.
/// </summary>
internal readonly record struct SuggestionResult(string? Text, double Confidence, string Evidence)
{
    public static SuggestionResult Silent { get; } = new(null, 0.0, "");

    public bool HasSuggestion => Text is not null;
}
