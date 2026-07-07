using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_DYM_003 — a mistyped Reactor <em>factory</em> name in call position (e.g.
/// <c>Buton("x")</c> for <c>Button("x")</c>, <c>Vstack(...)</c> for <c>VStack(...)</c>). The C#
/// compiler already rejects the unknown name with <c>CS0103</c> ("The name '…' does not exist in the
/// current context"); this analyzer adds the actionable <em>did-you-mean</em> pointing at the closest
/// real factory. Paired with <see cref="FuzzyFactoryNameCodeFix"/> for a one-click rename.
/// </summary>
/// <remarks>
/// <para>
/// This is the first <b>fuzzy</b> in-build "did you mean" analyzer (spec 061 §6, the CS0103 case —
/// #2 in the eval corpus). Because it guesses via similarity rather than structure, <b>false-positive
/// control is the whole point</b>: a wrong suggestion is worse than none (spec 038 §1). Precision is
/// deliberately favoured over recall.
/// </para>
/// <para>
/// <b>Live factory set.</b> The candidate names are enumerated once per compilation from the actual
/// <c>Microsoft.UI.Reactor.Factories</c> type (via <see cref="Compilation.GetTypeByMetadataName"/>),
/// so they are always current with the referenced Reactor package and the analyzer never fires in a
/// project that doesn't reference Reactor. (The CLI's <c>FactoryIndex</c> is deliberately not reused:
/// it depends on net8 <c>FrozenDictionary</c> APIs unavailable in this netstandard2.0 analyzer, and
/// enumerating the type live is cleaner.)
/// </para>
/// <para>
/// <b>False-positive gating (all must hold).</b> CS0103 fires on <em>any</em> unknown name — typo'd
/// locals, unknown types, unimported helpers — so we must be conservative:
/// </para>
/// <list type="number">
///   <item>the callee is a <b>bare identifier</b> in invocation position (<c>Foo(...)</c>) — the
///     factory-call shape; member-access typos (<c>x.Foo()</c>) are a different phase;</item>
///   <item>the name is <b>PascalCase</b> — factory names are PascalCase, so this excludes the dominant
///     CS0103 false-positive shape, a typo'd camelCase local/parameter (e.g. <c>myButton</c>);</item>
///   <item>the name is at least <see cref="MinNameLength"/> characters (short names give noisy
///     similarity);</item>
///   <item>the name is <b>not itself a factory name</b> — an exact match that doesn't bind is a
///     missing <c>using static</c>, which is a different fix, not a rename;</item>
///   <item>the name is genuinely <b>unbound</b> (<see cref="SymbolInfo.Symbol"/> is <see langword="null"/>
///     with no candidate symbols) — i.e. the CS0103 shape, not an overload/accessibility failure;</item>
///   <item>the closest factory — searched <b>only among names within <see cref="MaxLengthDelta"/>
///     characters of the same length</b> (typos change length by very little; this length gate is what
///     defeats the Jaro-Winkler common-prefix inflation that would otherwise "correct" <c>List</c> →
///     <c>ListBox</c> or <c>Text</c> → <c>TextBox</c>) — clears the high
///     <see cref="SimilarityThreshold"/> and is a <b>unique</b> best (a tie is genuinely ambiguous,
///     e.g. <c>Stack</c> between <c>HStack</c> and <c>VStack</c>, so we stay silent).</item>
/// </list>
/// <para>
/// <b>Threshold rationale.</b> The <see cref="SimilarityThreshold"/> of <c>0.88</c> is stricter than
/// the CLI's CS0103 floor of <c>0.75</c> (<c>Thresholds.cs</c>), as an always-on analyzer demands. It
/// was calibrated by an empirical spike over the 156 live factory names, 40 realistic factory typos,
/// and 66 realistic non-factory unknown identifiers (typo'd camelCase locals, unrelated PascalCase
/// types/methods, and short words that prefix long factories): the full gate yielded <b>0 false
/// positives at 40/40 recall</b>, with the firing positives clustering at ≥ 0.90 and the closest
/// non-firing negative (<c>Compute</c> → <c>Component</c>) at 0.871 — 0.88 sits in that valley.
/// </para>
/// <para>
/// <b>Severity: Warning.</b> The analyzer fires only when the invocation's name failed to bind, so it
/// always co-occurs with the compiler's CS0103 error. Warning is therefore safe under
/// <c>TreatWarningsAsErrors</c> (the build already fails) while still surfacing the hint in a plain
/// <c>dotnet build</c> and the IDE Error List. Consumers can tune it via <c>.editorconfig</c>.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FuzzyFactoryNameAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_DYM_003";

    /// <summary>Metadata key carrying the suggested factory name to the code fix.</summary>
    internal const string SuggestionProperty = "ReactorFactorySuggestion";

    /// <summary>
    /// Minimum Jaro-Winkler similarity to propose a rename. Deliberately stricter than the CLI's
    /// CS0103 floor of 0.75 — see the class remarks for the spike calibration.
    /// </summary>
    internal const double SimilarityThreshold = 0.88;

    /// <summary>
    /// Only factory names whose length is within this many characters of the mistyped name are
    /// considered. Single-token typos change length by very little; this gate defeats the
    /// Jaro-Winkler common-prefix inflation (e.g. <c>List</c> → <c>ListBox</c>).
    /// </summary>
    internal const int MaxLengthDelta = 2;

    /// <summary>Minimum length of the mistyped name; shorter names give noisy similarity.</summary>
    internal const int MinNameLength = 4;

    private const string FactoriesMetadataName = "Microsoft.UI.Reactor.Factories";

    private static readonly LocalizableString Title =
        "Reactor factory name may be misspelled";
    private static readonly LocalizableString MessageFormat =
        "'{0}' is not defined — did you mean the Reactor factory '{1}'?";
    private static readonly LocalizableString Description =
        "An unresolved call in factory position closely matches a Reactor factory name; it looks like a typo (e.g. Buton -> Button).";

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
        // Compilation-level gate: only Reactor-referencing projects. Enumerate the live factory set
        // once here (not per node), then capture it in the per-invocation action.
        var factoryType = context.Compilation.GetTypeByMetadataName(FactoriesMetadataName);
        if (factoryType is null)
            return;

        var factoryNames = CollectFactoryNames(factoryType);
        if (factoryNames.Count == 0)
            return;

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeInvocation(ctx, factoryNames),
            SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, HashSet<string> factoryNames)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // (1) Bare identifier in call position — the factory-call shape. `x.Foo()` (member access) and
        // generic `Foo<int>()` are intentionally out of scope (different phase / trickier fix).
        if (invocation.Expression is not IdentifierNameSyntax ident)
            return;

        var name = ident.Identifier.ValueText;

        // (2-4) Cheap syntactic pre-filters, before any semantic query:
        //   PascalCase (excludes typo'd camelCase locals), length floor, and exact-factory exclusion
        //   (an exact name that doesn't bind is a missing `using static`, not a rename).
        if (name.Length < MinNameLength)
            return;
        if (!IsPascalCase(name))
            return;
        if (factoryNames.Contains(name))
            return;

        // (5) The name is genuinely unbound — the CS0103 shape. An overload-resolution or
        // accessibility failure leaves CandidateSymbols populated; those are different errors, so bail.
        var symbolInfo = context.SemanticModel.GetSymbolInfo(ident, context.CancellationToken);
        if (symbolInfo.Symbol is not null || !symbolInfo.CandidateSymbols.IsEmpty)
            return;

        // (6) Closest factory within the length window must clear the high threshold and be unique.
        if (!TryFindSuggestion(name, factoryNames, out var suggestion))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty.Add(SuggestionProperty, suggestion);
        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            ident.GetLocation(),
            properties,
            name,
            suggestion));
    }

    // Enumerate the public static ordinary methods of Factories, deduped by name. Mirrors the CLI's
    // FactoryIndex membership rule (public + static + ordinary) so both judge "is a factory" alike.
    private static HashSet<string> CollectFactoryNames(INamedTypeSymbol factoryType)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in factoryType.GetMembers())
        {
            if (member is not IMethodSymbol method) continue;
            if (method.MethodKind != MethodKind.Ordinary) continue;
            if (!method.IsStatic) continue;
            if (method.DeclaredAccessibility != Accessibility.Public) continue;
            names.Add(method.Name);
        }
        return names;
    }

    /// <summary>
    /// Finds the single closest factory to <paramref name="name"/> among candidates within
    /// <see cref="MaxLengthDelta"/> characters of its length, returning it only when it clears
    /// <see cref="SimilarityThreshold"/> and is a strict, unique best (no tie).
    /// </summary>
    internal static bool TryFindSuggestion(string name, IEnumerable<string> factoryNames, out string suggestion)
    {
        suggestion = string.Empty;

        double bestScore = -1.0;
        int bestCount = 0;
        string bestName = string.Empty;

        foreach (var candidate in factoryNames)
        {
            // Length gate — defeats Jaro-Winkler common-prefix inflation on short-name-vs-long-factory.
            if (Math.Abs(candidate.Length - name.Length) > MaxLengthDelta)
                continue;

            var score = StringSimilarity.JaroWinkler(name, candidate);
            if (score > bestScore)
            {
                bestScore = score;
                bestName = candidate;
                bestCount = 1;
            }
            else if (score == bestScore)
            {
                bestCount++;
            }
        }

        if (bestScore < SimilarityThreshold)
            return false;
        // A tie between two equally-close factories (e.g. `Stack` vs HStack/VStack) is genuinely
        // ambiguous — stay silent rather than guess.
        if (bestCount != 1)
            return false;

        suggestion = bestName;
        return true;
    }

    // Factory names are PascalCase; an unbound camelCase identifier is far more likely a typo'd local
    // or parameter than a mistyped factory, so we require an uppercase first letter.
    private static bool IsPascalCase(string name) => name.Length > 0 && char.IsUpper(name[0]);
}
